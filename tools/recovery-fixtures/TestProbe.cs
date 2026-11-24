using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using SeaPowerKeel;

namespace KeelRecovery;

internal static class TestProbe
{
    private static readonly FieldInfo plugins = typeof(Keel).GetField("Plugins", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo state = typeof(Keel).Assembly.GetType("SeaPowerKeel.PluginEntry").GetField("State");
    internal static bool Ready
    {
        get
        {
            var entries = ((IEnumerable)plugins.GetValue(null)).Cast<object>().ToList();
            return entries.Count > 0 && entries.All(plugin => Convert.ToInt32(state.GetValue(plugin)) != 0);
        }
    }
    internal static string Report => File.ReadAllText(Path.Combine(Paths.BepInExRootPath, "Keel-report.txt"));
}
