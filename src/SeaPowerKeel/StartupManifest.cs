using System;
using System.Collections.Generic;
using System.Linq;

namespace SeaPowerKeel;

internal class StartupManifest
{
    public string Prepare { get; private set; }
    public List<string> Preloaders { get; } = new();
    public bool HasConflict => Prepare != null && Preloaders.Count != 0;

    public static StartupManifest Read(IEnumerable<string> lines)
    {
        var result = new StartupManifest();
        string section = "";
        foreach (string raw in lines)
        {
            int comment = raw.IndexOfAny([';', '#']);
            string line = (comment < 0 ? raw : raw.Substring(0, comment)).Trim();
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                section = line;
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            string key = line.Substring(0, eq).Trim();
            string value = line.Substring(eq + 1).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (section.Equals("[Keel]", StringComparison.OrdinalIgnoreCase) && key.Equals("Prepare", StringComparison.OrdinalIgnoreCase))
                result.Prepare ??= value;
            else if (section.Equals("[Preloader]", StringComparison.OrdinalIgnoreCase) &&
                key.StartsWith("Load", StringComparison.OrdinalIgnoreCase) && key.Skip(4).All(char.IsDigit))
                result.Preloaders.Add(value);
        }
        return result;
    }
}
