using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace SeaPowerKeel;

internal enum StartupTiming { AnchorChain, Early, Blocking }

internal static class EarlyStart
{
    private const string Timing = "LoadTiming", Legacy = "StartEarly";

    // Null means the author did not specify a timing. Anchor Chain timing is the default.
    public static StartupTiming? InInfoIni(string text)
    {
        bool inKeel = false;
        StartupTiming? timing = null, legacy = null;
        bool blocking = false;
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string line = raw.Split(';', '#')[0].Trim();
            if (line.StartsWith("["))
            {
                inKeel = line.Equals("[Keel]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            int eq = line.IndexOf('=');
            if (!inKeel || eq <= 0)
                continue;
            string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1);
            if (key.Equals(Timing, StringComparison.OrdinalIgnoreCase))
                timing = ParseTiming(value);
            else if (key.Equals(Legacy, StringComparison.OrdinalIgnoreCase))
                legacy = IsTrue(value) ? StartupTiming.Early : StartupTiming.AnchorChain;
            else if (key.Equals("BlockingStart", StringComparison.OrdinalIgnoreCase))
                blocking = IsTrue(value);
        }
        return blocking ? StartupTiming.Blocking : timing ?? legacy;
    }

    public static StartupTiming? FolderTiming(string folder)
    {
        try
        {
            string path = Path.Combine(folder, "_info.ini");
            return File.Exists(path) ? InInfoIni(File.ReadAllText(path)) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static StartupTiming? AssemblyTiming(AssemblyDefinition assembly)
    {
        string Value(string key) => assembly?.CustomAttributes
            .Where(a => a.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute" &&
                        a.ConstructorArguments.Count == 2 && a.ConstructorArguments[0].Value as string == key)
            .Select(a => a.ConstructorArguments[1].Value as string).FirstOrDefault();

        if (IsTrue(Value("Keel.BlockingStart"))) return StartupTiming.Blocking;
        return ParseTiming(Value("Keel." + Timing)) ?? (IsTrue(Value("Keel." + Legacy)) ? StartupTiming.Early : (StartupTiming?)null);
    }

    private static StartupTiming? ParseTiming(string value) => value?.Trim().Trim('"').ToUpperInvariant() switch
    {
        "EARLY" => StartupTiming.Early,
        "ANCHORCHAIN" => StartupTiming.AnchorChain,
        _ => null,
    };

    private static bool IsTrue(string value) =>
        value?.Trim().Trim('"').Equals("true", StringComparison.OrdinalIgnoreCase) == true || value?.Trim() == "1";

    /// <summary>Selects pending early plugins whose prerequisites can also start early.</summary>
    public static HashSet<PluginEntry> Set(IList<PluginEntry> all, Action<PluginEntry, string> note, bool blocking = false)
    {
        List<PluginEntry> pending = all.Where(p => p.State == PluginState.Pending).ToList();
        var prerequisites = Planner.Prerequisites(pending);
        var early = new HashSet<PluginEntry>(pending.Where(p => blocking ? p.BlockingStart : p.StartEarly));
        bool changed;
        do
        {
            changed = false;
            foreach (PluginEntry plugin in pending.Where(early.Contains))
            {
                PluginEntry late = prerequisites[plugin].FirstOrDefault(p => !early.Contains(p));
                if (late == null)
                    continue;
                early.Remove(plugin);
                changed = true;
                note?.Invoke(plugin, blocking
                    ? $"Cannot block the load screen because it must start after {late.Display}."
                    : $"Starts at Anchor Chain timing because it must start after {late.Display}.");
            }
        } while (changed);
        return early;
    }
}
