using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SeaPowerKeel;

/// <summary>
/// What Keel can do about a crash. A crash in native code (Unity, the graphics driver, a bad transpiler) ends the process, and no
/// code inside it can catch that. So Keel keeps a journal on disk of what it is doing (<c>Keel-session.txt</c>, handed to the
/// operating system at every step, so it survives the process dying), and the next start reads it: a game that did not quit
/// normally while Keel was loading or starting a mod names that mod, and the mod is held out from then on, so the game starts.
/// A crash later on is pinned on a mod when the mod's code is on the crash's stack in Unity's Player-prev.log.
/// Kept apart from Unity so it can be tested offline.
/// </summary>
internal sealed class CrashGuard
{
    private readonly string dir;
    private readonly string journalPath;
    private readonly string heldPath;
    private readonly string playerPrev;
    private FileStream journal;

    public CrashText.Journal Previous { get; }

    /// <summary>One short line for the summary card, when the last game crashed.</summary>
    public string Short { get; private set; }

    /// <summary>A mod is being kept out in this game because of an earlier crash.</summary>
    public bool HeldActive { get; private set; }
    internal bool HasHeldPlugins => File.Exists(heldPath);

    /// <summary>The report's button: every held mod runs again from the next start.</summary>
    public void ClearHeld()
    {
        try
        {
            File.Delete(heldPath);
        }
        catch (Exception)
        {
            // the list stays as it is
        }
    }

    public CrashGuard(string dir, string playerPrevLog)
    {
        this.dir = dir;
        playerPrev = playerPrevLog;
        journalPath = Path.Combine(dir, KeelNames.JournalFile);
        heldPath = Path.Combine(dir, KeelNames.HeldFile);
        Previous = CrashText.ReadJournal(CrashText.ReadLines(journalPath));
        KeepLastStack();
        try
        {
            journal = new FileStream(journalPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        }
        catch (Exception)
        {
            // no journal this session: the next start simply has nothing to say
        }
        Mark("session", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    // The catcher empties its file when it is installed, which is before this game's mods are known.
    private void KeepLastStack()
    {
        try
        {
            string stack = Path.Combine(dir, KeelNames.StackFile), kept = Path.Combine(dir, KeelNames.StackPrevFile);
            if (File.Exists(stack) && new FileInfo(stack).Length > 0)
                File.Copy(stack, kept, true);
            else if (File.Exists(kept))
                File.Delete(kept);
        }
        catch (Exception)
        {
            // then the next start has less to say
        }
    }

    /// <summary>One step, handed to the operating system at once. Never throws.</summary>
    public void Mark(string kind, string detail)
    {
        if (journal == null)
            return;
        try
        {
            byte[] line = Encoding.UTF8.GetBytes(kind + "\t" + detail + Environment.NewLine);
            journal.Write(line, 0, line.Length);
            journal.Flush();
        }
        catch (Exception)
        {
            journal = null;
        }
    }

    /// <summary>The first part of a mod's main class name, or null when that says nothing about whose code it is.</summary>
    internal static string RootOf(PluginEntry p)
    {
        if (string.IsNullOrEmpty(p.TypeName))
            return null;
        int dot = p.TypeName.IndexOf('.');
        string root = dot > 0 ? p.TypeName.Substring(0, dot) : p.TypeName;
        return root.Length > 2 && !Runner.SharedNamespaces.Contains(root) ? root : null;
    }

    /// <summary>The mod whose code is highest on the crash's stack, or null when only the game's and Unity's frames are there.</summary>
    internal static PluginEntry Blame(string crashBlock, IReadOnlyCollection<PluginEntry> plugins)
    {
        List<PluginEntry> named = Blamable(plugins);
        int hit = CrashText.FirstHit(crashBlock, named.Select(RootOf).ToList());
        return hit >= 0 ? named[hit] : null;
    }

    /// <summary>The mods a stack can be pinned on, in the order blame goes by.</summary>
    internal static List<PluginEntry> Blamable(IEnumerable<PluginEntry> plugins) => plugins.OrderBy(p => p.Order).Where(p => RootOf(p) != null).ToList();

    /// <summary>For the watcher, which runs outside the game: id, name and code root of every mod.</summary>
    public void WriteModsFile(IEnumerable<PluginEntry> plugins)
    {
        try
        {
            File.WriteAllLines(Path.Combine(dir, KeelNames.ModsFile), Blamable(plugins).Select(p => $"{p.Guid}\t{p.Display}\t{RootOf(p)}"));
        }
        catch (Exception)
        {
            // the watcher then names mods by their id, or not at all
        }
    }

    private static string Stamp(string dll)
    {
        try
        {
            var f = new FileInfo(dll);
            return f.LastWriteTimeUtc.Ticks + ":" + f.Length;
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>
    /// Reads what the last session left and acts on it: says what happened, and holds out the mod it can name (when <paramref name="hold"/>).
    /// A held mod stays out until its DLL changes (an update of the mod) or its line is removed from Keel-held.txt.
    /// </summary>
    public void Apply(IReadOnlyCollection<PluginEntry> plugins, bool hold, Action<string> note)
    {
        Dictionary<string, (string stamp, string reason)> held = ReadHeld();
        string caughtStack = Previous.Unclean ? CrashText.ReadAll(Path.Combine(dir, KeelNames.StackPrevFile)) : null;
        if (Previous.Unclean && CrashText.ClosedNormally(caughtStack, playerPrev))
        {
            // Only a line in the log (info): no notice on screen, no error - nothing went wrong (test 2026-11-18 found it as an error line).
            QuietNote = "The last game ended without Keel's quit mark, but Unity shut it down the normal way: no crash.";
            Previous.Unclean = false;
        }
        if (Previous.Unclean)
        {
            var keptOn = new HashSet<string>(CrashText.ReadLines(Path.Combine(dir, KeelNames.KeepFile)).Select(l => l.Trim()));
            PluginEntry culprit = Explain(plugins, caughtStack, note);
            if (culprit != null && hold && !keptOn.Contains(culprit.Guid))
                held[culprit.Guid] = (Stamp(culprit.Dll), $"The last game ended unexpectedly {culpritHow}.");
        }
        else
        {
            // Keel-crash.txt describes the last unclean session only; after a clean one it would sit in the next report's bundle
            // as if it were that session's crash (issue keel-bundle-stale-files).
            try { File.Delete(Path.Combine(dir, KeelNames.CrashFile)); } catch (Exception) { /* it stays, it is dated */ }
        }
        try
        {
            File.Delete(Path.Combine(dir, KeelNames.KeepFile));
        }
        catch (Exception)
        {
            // only an answer to the last crash
        }
        SaveHeld(HoldOut(plugins, held, hold));
    }

    /// <summary>A line for the log at info level only (set by <see cref="Apply"/>), or null.</summary>
    public string QuietNote;

    private string culpritHow;      // how Explain pinned the crash on its culprit

    // The crash in words (note, Short, Keel-crash.txt) and the mod it is pinned on: the one Keel was loading or starting, else the
    // one whose code is on the stack; null when Keel cannot tell.
    private PluginEntry Explain(IReadOnlyCollection<PluginEntry> plugins, string caughtStack, Action<string> note)
    {
        string block = !string.IsNullOrWhiteSpace(caughtStack) ? caughtStack : CrashText.CrashBlock(CrashText.ReadAll(playerPrev));
        WriteCrashFile(block);
        string where = Previous.Scene != null ? $" (last scene: {Previous.Scene})" : "";
        PluginEntry culprit = Previous.Guid != null ? plugins.FirstOrDefault(p => p.Guid == Previous.Guid) : null;
        culpritHow = culprit == null ? null : Previous.Step == "load" ? "while Keel was loading it" : "while Keel was starting it";
        if (culprit == null)
        {
            culprit = Blame(block, plugins);
            culpritHow = "with its code on the crash's stack";
        }
        if (culprit != null)
        {
            Short = $"Last game crashed: {culprit.Display}";
            note($"The last game did not end normally{where}: {culprit.Display} {culprit.Version} ({culpritHow}). Details: {KeelNames.CrashFile}.");
            return culprit;
        }
        Short = "Last game crashed: cause unknown";
        string loading = "";
        if (CrashText.LastPack(block, out string packFolder, out string packFile))
        {
            Short = $"Last game crashed loading {Path.GetFileName(packFile)}";
            loading = $" It was loading {Path.GetFileName(packFile)} from mod folder {packFolder} just before.";
        }
        string detail = CrashText.CrashLine(block);
        note($"The last game did not end normally{where}, and Keel cannot tell for certain which mod did it{(detail != null ? $" ({detail})" : "")}.{loading} Details: {KeelNames.CrashFile}.");
        return null;
    }

    private Dictionary<string, (string stamp, string reason)> ReadHeld()
    {
        var held = new Dictionary<string, (string stamp, string reason)>();
        foreach (string line in CrashText.ReadLines(heldPath))
        {
            string[] parts = line.Split('\t');
            if (parts.Length == 3)
                held[parts[0]] = (parts[1], parts[2]);
        }
        return held;
    }

    // Keeps the held mods out (when hold is on) and returns the lines of the hold list that stay.
    private List<string> HoldOut(IReadOnlyCollection<PluginEntry> plugins, Dictionary<string, (string stamp, string reason)> held, bool hold)
    {
        var keep = new List<string>();
        foreach (KeyValuePair<string, (string stamp, string reason)> h in held)
        {
            PluginEntry p = plugins.FirstOrDefault(x => x.Guid == h.Key);
            if (p != null && Stamp(p.Dll) != h.Value.stamp)
                continue;   // the mod was updated: it gets another chance
            keep.Add($"{h.Key}\t{h.Value.stamp}\t{h.Value.reason}");   // with no mod (its folder is off for now) the hold stays too
            if (p != null && hold && p.State == PluginState.Pending)
            {
                p.Stop(PluginState.NotStarted, h.Value.reason + " Keel keeps it out so the game starts, until the mod is updated. To try it anyway, use 'Let held mods run' in this window.");
                HeldActive = true;
            }
        }
        return keep;
    }

    private void SaveHeld(List<string> keep)
    {
        try
        {
            if (keep.Count > 0)
                File.WriteAllLines(heldPath, keep);
            else if (File.Exists(heldPath))
                File.Delete(heldPath);
        }
        catch (Exception)
        {
            // the hold list could not be saved: next start decides again from the journal
        }
    }

    private void WriteCrashFile(string block)
    {
        try
        {
            File.WriteAllText(Path.Combine(dir, KeelNames.CrashFile), CrashText.CrashFileText(Previous.Began, Previous.Step, Previous.Guid, Previous.Scene, block));
        }
        catch (Exception)
        {
            // only a convenience
        }
    }
}
