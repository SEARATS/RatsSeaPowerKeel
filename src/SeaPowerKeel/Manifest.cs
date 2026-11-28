using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SeaPowerKeel;

/// <summary>Optional per-DLL game build rules from the mod folder's keel.ini.</summary>
internal class Manifest(string folder)
{
    public const string FileName = "keel.ini";

    private readonly HashSet<string> globe = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> flat = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<GameBuildRule>> versions = new(StringComparer.OrdinalIgnoreCase);

    public static Manifest Read(string folder, Action<string> warn)
    {
        string path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
            return null;
        var manifest = new Manifest(folder);
        string section = "";
        bool legacy = false;
        try
        {
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Split(';', '#')[0].Trim();
                if (line.StartsWith("["))
                {
                    section = line.ToLowerInvariant();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (section == "[gameversions]")
                    manifest.AddRules(key, value, path, warn);
                else if (section == "[keel]" && key.ToLowerInvariant() is ("globe" or "flat"))
                {
                    legacy = true;
                    HashSet<string> files = key.Equals("globe", StringComparison.OrdinalIgnoreCase) ? manifest.globe : manifest.flat;
                    files.UnionWith(value.Split(',').Select(f => Normalize(f.Trim())).Where(f => f.Length > 0));
                }
            }
        }
        catch (Exception ex)
        {
            warn($"Cannot read {path}: {ex.Message}");
            return null;
        }
        if (legacy)
            warn($"{path}: [Keel] globe and flat DLL rules are deprecated. Use [GameVersions] to select DLLs by game build.");
        return manifest;
    }

    private void AddRules(string key, string value, string path, Action<string> warn)
    {
        string file = Normalize(key);
        if (!versions.TryGetValue(file, out List<GameBuildRule> rules))
            versions[file] = rules = new();
        foreach (string option in value.Split(','))
        {
            GameBuildRule rule = GameBuildRule.Parse(option.Trim());
            if (rule != null)
                rules.Add(rule);
            else
                warn($"{path}: invalid [GameVersions] rule for {key}: '{option.Trim()}'.");
        }
    }

    public string Mismatch(string dll, GameBuild build)
    {
        string relative = Normalize(Path.GetRelativePath(folder, dll));
        string name = Path.GetFileName(dll);
        if (versions.TryGetValue(relative, out List<GameBuildRule> rules) || versions.TryGetValue(name, out rules))
        {
            if (rules.Any(rule => rule.Accepts(build)))
                return null;
            if (rules.Count == 0)
                return "No valid game build rule (keel.ini [GameVersions]).";
            string expected = string.Join(", ", rules.Select(rule => rule.Text));
            string actual = build.Release == null ? "game version unavailable" : $"game version {build.VersionText}";
            if (rules.Any(rule => rule.UsesHash))
                actual += string.IsNullOrEmpty(build.Sha256) ? ", Seapower-Scripts.dll SHA-256 unavailable" : $", Seapower-Scripts.dll SHA-256 {build.Sha256}";
            return $"Requires {expected}. Found {actual} (keel.ini [GameVersions]).";
        }

        bool forGlobe = globe.Contains(relative) || globe.Contains(name);
        bool forFlat = flat.Contains(relative) || flat.Contains(name);
        return forFlat && !forGlobe ? "Flat-map DLL excluded on the globe build (keel.ini)." : null;
    }

    private static string Normalize(string path) => path.Replace('/', '\\');
}
