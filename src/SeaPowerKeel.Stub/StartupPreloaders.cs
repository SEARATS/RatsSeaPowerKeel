using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SeaPowerKeel.Stub;

internal static class StartupPreloaders
{
    private static readonly EnumerationOptions Search = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    internal static bool Boot(Type keel, string version, string path)
    {
        const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
        if (keel.GetMethod("BootWithPreloaders", publicStatic) is MethodInfo bootWithPreloaders)
        {
            bootWithPreloaders.Invoke(null, new object[] { version, path,
                (Func<string, string[]>)Find, (Func<string, Action<string, string[], Action<string>>>)PreparationEntry.Open });
            return true;
        }
        MethodInfo boot = keel.GetMethod("Boot", publicStatic);
        if (boot == null)
            return false;
        boot.Invoke(null, new object[] { version, path });
        return true;
    }

    // Anchor Chain's own starter is handled by Keel's takeover patcher.
    internal static string[] Find(string folder) =>
        Directory.EnumerateFiles(folder, "*Preloader.dll", Search)
            .Where(path => !Path.GetFileName(path).Equals(KeelNames.AnchorChainPreloaderFile, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
}
