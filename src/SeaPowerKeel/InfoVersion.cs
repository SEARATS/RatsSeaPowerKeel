using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SeaPowerKeel;

/// <summary>
/// What a mod's <c>_info.ini</c> <c>[Compatibility]</c> section says about the game version, judged by the game's own rules
/// (<c>SeaPower.ModCompatibility.Matches</c>). A backup for what the DLL check cannot see, such as a transpiler that no
/// longer matches its target: the author's statement is the only sign of those. A statement Keel cannot read is no verdict
/// (the game counts it as a mismatch).
/// This file stands alone (no Keel, Unity or BepInEx types) so other tools can compile the same rule.
/// </summary>
internal static class InfoVersion
{
    public const string Section = "Compatibility";
    public const string ExactKey = "ExactVersion", ApproximateKey = "ApproximateVersion", ExcludeKey = "ExcludeVersions";

    /// <summary>One side of the range: the strict key wins over its "or equal" twin, as in the game.</summary>
    public sealed class Bound
    {
        public string StrictKey, OrEqualKey;
        public string StrictSign, OrEqualSign;          // ">", ">=" ...: how the game's dialog list writes them
        public string StrictWords, OrEqualWords;        // "newer than {0}", "{0} or newer" ...: the report's words
        public int Side;                                // +1: the game must be newer, -1: older
    }

    public static readonly Bound[] Bounds =
    {
        new() { StrictKey = "GreaterThanVersion", OrEqualKey = "GreaterThanEqualToVersion", StrictSign = ">", OrEqualSign = ">=",
                StrictWords = "newer than {0}", OrEqualWords = "{0} or newer", Side = 1 },
        new() { StrictKey = "LessThanVersion", OrEqualKey = "LessThanEqualToVersion", StrictSign = "<", OrEqualSign = "<=",
                StrictWords = "older than {0}", OrEqualWords = "{0} or older", Side = -1 },
    };

    /// <summary>The sentence for the report when the mod's folder says it does not fit this game; otherwise null.</summary>
    public static string MismatchInFolder(string folder, string appVersion)
    {
        try
        {
            string ini = Path.Combine(folder ?? "", "_info.ini");
            return File.Exists(ini) ? Mismatch(File.ReadAllLines(ini), appVersion) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static string Mismatch(IEnumerable<string> iniLines, string appVersion)
    {
        int[] app = Parse(appVersion);
        if (app == null)
            return null;
        string statement = Statement(Read(iniLines), app);
        return statement == null ? null
            : $"Its _info.ini says {statement}, and this game is {appVersion}. Keel can't see every incompatibility in a DLL, so expect errors or parts that do nothing.";
    }

    // What the section says that this game is not, in the game's order: exact, approximate, excluded, then the two bounds.
    private static string Statement(Dictionary<string, string> c, int[] app)
    {
        string Get(string key) => c.TryGetValue(key, out string v) ? v : "";
        string exact = Get(ExactKey);
        if (exact != "")
            return Parse(exact) is int[] e && Compare(app, e) != 0 ? $"exactly {exact}" : null;
        string approx = Get(ApproximateKey);
        if (approx != "")
            return Parse(approx) is int[] a && !(app[0] == a[0] && app[1] == a[1] && app[2] >= a[2]) ? $"about {approx}" : null;
        string excluded = Get(ExcludeKey).Split(',').Select(s => s.Trim()).FirstOrDefault(x => Parse(x) is int[] v && Compare(app, v) == 0);
        if (excluded != null)
            return $"not {excluded}";
        return Bounds.Select(b => Outside(b, Get(b.StrictKey), Get(b.OrEqualKey), app)).FirstOrDefault(s => s != null);
    }

    private static string Outside(Bound b, string strict, string orEqual, int[] app)
    {
        bool isStrict = strict != "";
        string value = isStrict ? strict : orEqual;
        if (value == "" || Parse(value) is not int[] v)
            return null;
        int side = Compare(app, v) * b.Side;            // > 0: on the right side of the bound
        bool fits = isStrict ? side > 0 : side >= 0;
        return fits ? null : string.Format(isStrict ? b.StrictWords : b.OrEqualWords, value);
    }

    private static Dictionary<string, string> Read(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool inside = false;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                continue;
            if (line[0] == '[')
            {
                inside = line.Equals("[" + Section + "]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            int eq = line.IndexOf('=');
            if (inside && eq > 0)
                values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return values;
    }

    private static int[] Parse(string version)
    {
        Match m = Regex.Match(version ?? "", @"^\s*v?(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success)
            return null;
        try
        {
            return new[] { int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0 };
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static int Compare(int[] a, int[] b)
    {
        for (int i = 0; i < 3; i++)
            if (a[i] != b[i])
                return a[i].CompareTo(b[i]);
        return 0;
    }
}
