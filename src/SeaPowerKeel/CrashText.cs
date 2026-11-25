using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SeaPowerKeel;

/// <summary>Reading a crash: shared source, compiled into Keel and into the watcher, so both name the same mod.</summary>
internal static class CrashText
{
    public const string CrashPrefix = "Crash: ";
    public const string FilePrefix = "File: ";
    /// <summary>How long before a crash a file may have been opened to count as the likely cause.</summary>
    public const int PackWindowMs = 10000;

    /// <summary>What the journal (Keel-session.txt) of a session says.</summary>
    public sealed class Journal
    {
        /// <summary>The last session has a journal and it never reached a normal quit.</summary>
        public bool Unclean;
        /// <summary>"load" or "start" when the session ended inside that step of a mod, else null.</summary>
        public string Step;
        public string Guid;
        public string Scene;
        public string Began;
        public readonly List<Activity> Activities = new();
    }

    public class Activity
    {
        public string Token, Guid, Operation, Detail;
        public long Started, Ended;
        public string Status => Ended == 0 ? "not completed" : "completed";
    }

    private static readonly string[] CrashMarkers =
    {
        "Got a UNKNOWN while executing native code",
        "Native Crash Reporting",
        "Crash!!!",
        "A crash has been intercepted by the crash handler",
        "Received signal SIG",
    };

    private const int MaxCrashLines = 400;

    /// <summary>
    /// Reads a journal. A "load" or "start" stays open until the next one or "done": the game gets a frame after each step, and a
    /// BepInEx plugin's own Start() runs in that frame, so a crash there still counts against the mod.
    /// </summary>
    public static Journal ReadJournal(IEnumerable<string> lines)
    {
        var v = new Journal();
        bool any = false, quit = false;
        foreach (string line in lines)
        {
            int tab = line.IndexOf('\t');
            if (tab <= 0)
                continue;
            any = true;
            string kind = line.Substring(0, tab), detail = line.Substring(tab + 1);
            switch (kind)
            {
                case "session": v.Began = detail; break;
                case "load":
                case "start": v.Step = kind; v.Guid = detail; break;
                case "done": v.Step = null; v.Guid = null; break;
                case "scene": v.Scene = detail; break;
                case "quit": quit = true; break;
                case "activity":
                    string[] fields = detail.Split('\t');
                    if (fields.Length != 5 || !long.TryParse(fields[1], out long started)
                        || started < DateTime.MinValue.Ticks || started > DateTime.MaxValue.Ticks) break;
                    v.Activities.Add(new Activity { Token = fields[0], Started = started, Guid = fields[2], Operation = fields[3], Detail = fields[4] });
                    if (v.Activities.Count > 32) v.Activities.RemoveAt(0);
                    break;
                case "activity-end":
                    string[] ending = detail.Split('\t');
                    if (ending.Length == 2 && long.TryParse(ending[1], out long ended)
                        && ended > 0 && ended <= DateTime.MaxValue.Ticks
                        && v.Activities.LastOrDefault(activity => activity.Token == ending[0]) is { } activity)
                        activity.Ended = ended;
                    break;
            }
        }
        v.Unclean = any && !quit;
        if (!v.Unclean)
        {
            v.Step = null;
            v.Guid = null;
        }
        return v;
    }

    /// <summary>The crash part of a Unity player log, or null when the log shows no crash.</summary>
    internal static string CrashBlock(string log)
    {
        if (string.IsNullOrEmpty(log))
            return null;
        int at = CrashMarkers.Select(m => log.IndexOf(m, StringComparison.Ordinal)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        if (at < 0)
            return null;
        int lineStart = log.LastIndexOf('\n', at) + 1;
        return string.Join("\n", log.Substring(lineStart).Replace("\r\n", "\n").Split('\n').Take(MaxCrashLines));
    }

    /// <summary>
    /// No quit mark, but no crash record either and Unity shut down the normal way: the game was closed or restarted without
    /// Keel's quit (issue keel-crash-without-data). Not a crash: no mod is named or held, no crash window.
    /// </summary>
    public static bool ClosedNormally(string caughtStack, string playerLog) =>
        string.IsNullOrWhiteSpace(caughtStack) && CrashBlock(ReadAll(playerLog)) == null && LogEndedNormally(playerLog);

    public static bool IsManagedFrame(string line)
    {
        string t = line.TrimStart();
        return t.StartsWith("at ", StringComparison.Ordinal) || t.StartsWith("(wrapper", StringComparison.Ordinal) || line.Contains("(Mono JIT code)");
    }

    public static bool HasRoot(string line, string root)
    {
        string probe = root + ".";
        for (int at = line.IndexOf(probe, StringComparison.Ordinal); at >= 0; at = line.IndexOf(probe, at + 1, StringComparison.Ordinal))
            if (at == 0 || !(char.IsLetterOrDigit(line[at - 1]) || line[at - 1] == '.' || line[at - 1] == '_'))
                return true;
        return false;
    }

    // Unity's object search (FindObjectOfType and its kin) walks the game's whole object list. When that list is already broken -
    // the most common crash in the 0.9.x reports, issue keel-findobject-blame - the search crashes in whatever code called it,
    // a mod's or Keel's own. The caller did nothing wrong, so such a crash names no mod and no pack.
    private static readonly string[] SearchCalls = { "FindObjectOfType", "FindObjectsOfType", "FindObjectsByType", "FindAnyObjectByType", "FindFirstObjectByType", "FindObjectsOfTypeAll" };

    /// <summary>The top of the stack is Unity's object search: only UnityEngine frames above the first other frame, one of them a search.</summary>
    public static bool InObjectSearch(string block)
    {
        if (block == null)
            return false;
        bool search = false;
        foreach (string line in block.Replace("\r\n", "\n").Split('\n').Where(IsManagedFrame))
        {
            string code = FrameCode(line);
            if (!code.StartsWith("UnityEngine.", StringComparison.Ordinal))
                return search;
            if (SearchCalls.Any(c => code.IndexOf(c, StringComparison.Ordinal) >= 0))
                search = true;
        }
        return search;
    }

    /// <summary>
    /// The code a frame names, without its address, "at ", "(Mono JIT code)" and "(wrapper ...)" parts:
    /// "0x1 (Mono JIT code) (wrapper managed-to-native) UnityEngine.Object:FindObjectsOfType (...)" gives "UnityEngine.Object:FindObjectsOfType (...)".
    /// </summary>
    internal static string FrameCode(string frame)
    {
        string t = frame.Trim();
        int jit = t.IndexOf("(Mono JIT code)", StringComparison.Ordinal);
        if (jit >= 0)
            t = t.Substring(jit + "(Mono JIT code)".Length).TrimStart();
        if (t.StartsWith("at ", StringComparison.Ordinal))
            t = t.Substring(3).TrimStart();
        while (t.StartsWith("(wrapper", StringComparison.Ordinal))
        {
            int close = t.IndexOf(')');
            if (close < 0)
                break;
            t = t.Substring(close + 1).TrimStart();
        }
        return t;
    }

    /// <summary>
    /// "Input System module state changed to: Shutdown." is the last line Unity writes when the game is closed the normal way. A session
    /// that ends with it and left no crash record was closed (or restarted) without Keel's quit mark: not a crash.
    /// </summary>
    public const string ShutdownLine = "Input System module state changed to: Shutdown";

    public static bool LogEndedNormally(string playerLog)
    {
        string text = ReadAll(playerLog);
        if (string.IsNullOrEmpty(text))
            return false;
        string tail = text.Length > 4000 ? text.Substring(text.Length - 4000) : text;
        return tail.IndexOf(ShutdownLine, StringComparison.Ordinal) >= 0;
    }

    /// <summary>Keel-crash.txt: what the session left, written for the session that just ended (by Keel at the next start, or by the watcher).</summary>
    public static string CrashFileText(Journal journal, string block) =>
        $"The game session started {journal.Began ?? "?"} did not end normally.\n" +
        $"Keel was {(journal.Guid != null ? $"at the '{journal.Step}' step of {journal.Guid}" : "not loading or starting a mod")}; last scene: {journal.Scene ?? "none"}.\n\n" +
        (block ?? NoRecord) + "\n" + ActivityText(journal.Activities);

    private static string ActivityText(List<Activity> activities)
    {
        if (activities.Count == 0) return "";
        return "\nRecorded mod activity (diagnostic context, not proof of cause; no mod is held from this alone):\n" +
            string.Join("\n", activities.Select(activity => $"{new DateTime(activity.Started, DateTimeKind.Utc):O} " +
                $"{activity.Guid}: {activity.Operation} {activity.Detail} ({activity.Status})")) + "\n";
    }

    public static string ActivityHint(Journal journal)
    {
        var last = journal.Activities.LastOrDefault();
        return last == null ? "" : $" Last recorded activity: {last.Guid}, {last.Operation}" +
            $"{(string.IsNullOrEmpty(last.Detail) ? "" : " (" + last.Detail + ")")}, " +
            $"{last.Status}. This is context, not proof of cause.";
    }

    public const string NoRecord = "No crash record: the crash catcher wrote nothing and the game's log shows no crash. The game may have frozen and been ended, or been closed from outside.";

    /// <summary>The index of the root whose code is highest on the stack, or -1. A crash inside Unity's object search names no one.</summary>
    public static int FirstHit(string block, IList<string> roots)
    {
        if (block == null || InObjectSearch(block))
            return -1;
        foreach (string line in block.Replace("\r\n", "\n").Split('\n').Where(IsManagedFrame))
            for (int i = 0; i < roots.Count; i++)
                if (HasRoot(line, roots[i]))
                    return i;
        return -1;
    }

    /// <summary>"access violation (0xC0000005) in UnityPlayer.dll", when the block is the catcher's; else null.</summary>
    public static string CrashLine(string block)
    {
        if (block == null || !block.StartsWith(CrashPrefix, StringComparison.Ordinal))
            return null;
        int nl = block.IndexOf('\n');
        return (nl < 0 ? block : block.Substring(0, nl)).Substring(CrashPrefix.Length).Trim();
    }

    private static readonly string[] NotPacks = { "original", "user", "EntityScenes", "SeaPowerKeel", "ACConfigs", "3835527459", "3380210757", KeelNames.AlpineWorkshopId.ToString() };

    /// <summary>The mod folder a full path lies in (a Workshop id or a StreamingAssets folder), or null for the game's own files.</summary>
    public static string PackOf(string path)
    {
        string p = path.Replace('\\', '/');
        foreach (string marker in new[] { $"/workshop/content/{KeelNames.SteamAppId}/", "/StreamingAssets/" })
        {
            int at = p.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                continue;
            int start = at + marker.Length, end = p.IndexOf('/', start);
            if (end < 0)
                return null;
            string folder = p.Substring(start, end - start);
            return NotPacks.Contains(folder, StringComparer.OrdinalIgnoreCase) ? null : folder;
        }
        return null;
    }

    /// <summary>The mod file the game opened last, when that was shortly before the crash. The block is the catcher's.</summary>
    public static bool LastPack(string block, out string folder, out string fullPath)
    {
        folder = fullPath = null;
        if (block == null || InObjectSearch(block))
            return false;
        foreach (string line in block.Replace("\r\n", "\n").Split('\n').Where(l => l.StartsWith(FilePrefix, StringComparison.Ordinal)))
        {
            string[] parts = line.Substring(FilePrefix.Length).Split('\t');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int age) || age < 0 || age > PackWindowMs)
                continue;
            folder = PackOf(parts[1]);
            if (folder == null)
                continue;
            fullPath = parts[1];
            return true;
        }
        return false;
    }

    public static string ReadAll(string path)
    {
        try
        {
            if (path == null || !File.Exists(path))
                return null;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The file's non-empty lines; none when it cannot be read.</summary>
    public static string[] ReadLines(string path)
    {
        string text = ReadAll(path);
        return text == null ? new string[0] : text.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).ToArray();
    }
}
