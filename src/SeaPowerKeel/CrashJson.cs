using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SeaPowerKeel;

/// <summary>
/// Keel-crash.json: the crash as fields, so the feedback board groups reports without guessing from log text (schema in
/// docs/crash-json.md). Shared source, compiled into Keel and into the watcher, so both write the same file for one crash.
/// No paths, no arguments, no user names: only type and method names, a module's file name and the mod Keel pins it on.
/// </summary>
internal static class CrashJson
{
    public const int Schema = 1;
    public const int MaxFrames = 20;
    private const int MaxFrameChars = 160;

    /// <summary>The mod a crash is pinned on, as the game shows it; null fields are left out of the file.</summary>
    public sealed class Culprit
    {
        public string Name, Guid, Version;
    }

    /// <summary>The file's text for one crash. <paramref name="block"/> is the catcher's record or the crash part of Player.log.</summary>
    public static string Text(CrashText.Journal journal, string block, Culprit culprit)
    {
        var fields = new List<KeyValuePair<string, object>> { new("schema", Schema) };
        void Add(string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                fields.Add(new KeyValuePair<string, object>(key, value.Trim()));
        }
        Add("exception", ExceptionType(block));
        fields.Add(new KeyValuePair<string, object>("frames", Frames(block)));
        if (Native(block, out string module, out string code))
        {
            Add("module", module);
            Add("code", code);
        }
        Add("mod", culprit?.Name);
        Add("guid", culprit?.Guid);
        Add("modVersion", culprit?.Version);
        Add("step", journal.Step ?? "running");
        Add("scene", journal.Scene);
        return Write(fields);
    }

    // "Crash: access violation (0xC0000005) in UnityPlayer.dll", the catcher's first line.
    private static readonly Regex NativeLine = new(@"\((0x[0-9A-Fa-f]{8})\) in (.+)$");

    /// <summary>Module and code of a native crash, from the catcher's first line. A module the catcher could not name is "unknown".</summary>
    internal static bool Native(string block, out string module, out string code)
    {
        module = code = null;
        Match m = NativeLine.Match(CrashText.CrashLine(block) ?? "");
        if (!m.Success)
            return false;
        code = m.Groups[1].Value.ToUpperInvariant().Replace("0X", "0x");
        string name = m.Groups[2].Value.Trim();
        module = name.IndexOfAny(new[] { '\\', '/', ' ' }) >= 0 ? "unknown" : name;
        return true;
    }

    // "Unhandled Exception: System.NullReferenceException: Object reference ..." or "NullReferenceException: ..." at a line's start.
    private static readonly Regex ExceptionLine = new(@"^(?:Unhandled [Ee]xception:\s*)?((?:[A-Za-z_][\w`]*\.)*[A-Za-z_]\w*Exception)(?::|\s*$)", RegexOptions.Multiline);

    /// <summary>The managed exception's type without its namespace, when the record names one.</summary>
    internal static string ExceptionType(string block)
    {
        if (block == null)
            return null;
        Match m = ExceptionLine.Match(block.Replace("\r\n", "\n"));
        if (!m.Success)
            return null;
        string type = m.Groups[1].Value;
        return type.Substring(type.LastIndexOf('.') + 1);
    }

    /// <summary>The managed frames, top first, as Namespace.Type.Method; Keel's own catcher frames and anything path-like left out.</summary>
    internal static List<string> Frames(string block)
    {
        var frames = new List<string>();
        if (block == null)
            return frames;
        foreach (string line in block.Replace("\r\n", "\n").Split('\n').Where(CrashText.IsManagedFrame))
        {
            string frame = Frame(line);
            if (frame == null || frame.StartsWith("SeaPowerKeel.CrashCatcher", StringComparison.Ordinal))
                continue;
            frames.Add(frame);
            if (frames.Count == MaxFrames)
                break;
        }
        return frames;
    }

    private static readonly Regex Patched = new(@"DMD<([^>]+)>");      // Harmony: "Type.DMD<Type::Method>"
    private static readonly Regex SourceSpot = new(@"^\[[^\]]*\]\s*");              // Unity's "[File.cs:12] " before the name
    private static readonly Regex Allowed = new(@"^[A-Za-z_<][\w.`<>+$\-]*$");

    /// <summary>One frame line as Namespace.Type.Method, or null when it names no method or could carry a path.</summary>
    internal static string Frame(string line)
    {
        string code = SourceSpot.Replace(CrashText.FrameCode(line), "");
        Match patched = Patched.Match(code);
        if (patched.Success)
            code = patched.Groups[1].Value;
        int cut = code.IndexOfAny(new[] { '(', ' ', '[' });
        if (cut >= 0)
            code = code.Substring(0, cut);
        code = code.Replace("::", ".").Replace(':', '.').Trim('.');
        if (code.Length == 0 || code.Length > MaxFrameChars || !Allowed.IsMatch(code))
            return null;
        return code;
    }

    /// <summary>A string field of a Keel-crash.json, or null (enough for Keel's own file; not a general JSON reader).</summary>
    public static string Field(string json, string key)
    {
        if (json == null)
            return null;
        Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        return m.Success ? Unescape(m.Groups[1].Value) : null;
    }

    /// <summary>True when <paramref name="a"/> is a newer version than <paramref name="b"/>, by its numbers ("1.10" &gt; "1.9").</summary>
    public static bool Newer(string a, string b)
    {
        int[] x = Numbers(a), y = Numbers(b);
        if (x.Length == 0 || y.Length == 0)
            return false;
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
            if (p != q)
                return p > q;
        }
        return false;
    }

    private static int[] Numbers(string version) =>
        Regex.Matches(version ?? "", @"\d+").Cast<Match>().Take(6)
             .Select(m => int.TryParse(m.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0).ToArray();

    private static string Write(List<KeyValuePair<string, object>> fields)
    {
        var sb = new StringBuilder("{\n");
        for (int i = 0; i < fields.Count; i++)
        {
            sb.Append("  ").Append(Quote(fields[i].Key)).Append(": ");
            switch (fields[i].Value)
            {
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case List<string> list: sb.Append('[').Append(string.Join(", ", list.Select(Quote))).Append(']'); break;
                default: sb.Append(Quote(fields[i].Value.ToString())); break;
            }
            sb.Append(i < fields.Count - 1 ? ",\n" : "\n");
        }
        return sb.Append("}\n").ToString();
    }

    private static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    private static string Unescape(string s) =>
        Regex.Replace(s, @"\\(u[0-9a-fA-F]{4}|.)", m => m.Groups[1].Value switch
        {
            "n" => "\n",
            "r" => "\r",
            "t" => "\t",
            var u when u.Length == 5 => ((char)int.Parse(u.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString(),
            var other => other,
        });
}
