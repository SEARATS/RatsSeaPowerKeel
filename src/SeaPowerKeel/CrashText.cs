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
    /// <summary>The last game's stack, kept when Keel installs the catcher (which empties the stack file) at the start of the next one.</summary>
    public const string StackPrevFile = "Keel-crash-stack.prev.txt";
    /// <summary>Written by the crash window: this pack folder is to be switched off in the game's mod list.</summary>
    public const string PackOffFile = "Keel-packoff.txt";
    /// <summary>How long before a crash a file may have been opened to count as the likely cause.</summary>
    public const int PackWindowMs = 10000;
    public const string StackFile = "Keel-crash-stack.txt";
    public const string ModsFile = "Keel-mods.txt";
    public const string JournalFile = "Keel-session.txt";
    /// <summary>Written by the crash window: the player chose to keep this mod on.</summary>
    public const string KeepFile = "Keel-keep.txt";

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
    public static string CrashFileText(string began, string step, string guid, string scene, string block) =>
        $"The game session started {began ?? "?"} did not end normally.\n" +
        $"Keel was {(guid != null ? $"at the '{step}' step of {guid}" : "not loading or starting a mod")}; last scene: {scene ?? "none"}.\n\n" +
        (block ?? NoRecord) + "\n";

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

    private static readonly string[] NotPacks = { "original", "user", "EntityScenes", "SeaPowerKeel", "ACConfigs", "3835527459", "3380210757", "3832006068" };

    /// <summary>The mod folder a full path lies in (a Workshop id or a StreamingAssets folder), or null for the game's own files.</summary>
    public static string PackOf(string path)
    {
        string p = path.Replace('\\', '/');
        foreach (string marker in new[] { "/workshop/content/1286220/", "/StreamingAssets/" })
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
}
