using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SeaPowerKeel;

internal static class AnchorChainStartup
{
    internal enum Wait { StartupDone, RetryFolders }
    internal const float RetrySeconds = 0.5f;

    // Standalone checks the directories exposed by FileManager, including folders outside the mod list.
    internal static bool HasStandaloneLoader(IEnumerable<DirectoryInfo> directories)
    {
        foreach (var directory in directories.Where(d => d?.Exists == true))
        {
            var dll = directory.EnumerateFiles("*AnchorChain.dll", SearchOption.AllDirectories)
                .FirstOrDefault(f => f.Name.EndsWith("AnchorChain.dll", StringComparison.Ordinal));
            if (dll == null) continue;
            using var module = Mono.Cecil.ModuleDefinition.ReadModule(dll.FullName);
            if (module.GetType(AcLoaderShim.LoaderType)?.IsPublic == true) return true;
        }
        return false;
    }

    // The caller drives this iterator from Awake. The discovery deadline starts after readiness.
    internal static IEnumerable<Wait> Discover(Func<bool> tryLoad, Func<float> realtime)
    {
        if (tryLoad()) yield break;
        yield return Wait.StartupDone;
        float deadline = realtime() + 60f;
        while (realtime() < deadline && !tryLoad())
            yield return Wait.RetryFolders;
    }
}
