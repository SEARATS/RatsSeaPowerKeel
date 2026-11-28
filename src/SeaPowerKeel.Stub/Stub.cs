using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace SeaPowerKeel.Stub;

/// <summary>
/// Starts explicitly listed preloaders from enabled mod folders, then boots Keel if an enabled folder provides it.
/// </summary>
[BepInPlugin(Guid, "Keel stub", "1.1.0")]
public class Stub : BaseUnityPlugin
{
    public const string Guid = KeelNames.StubGuid;
    private const string LoaderFile = KeelNames.LoaderFile;
    private const string LoaderType = "SeaPowerKeel.Keel";

    private void Awake()
    {
        try
        {
            List<ModFolder> folders = ModFolders.FromDisk();
            StartupPreloaders.Run(folders, Paths.BepInExRootPath, Application.streamingAssetsPath, Application.version,
                Logger.LogInfo, Logger.LogWarning);
            StartKeel(folders);
        }
        catch (Exception ex)
        {
            Logger.LogError($"BepInEx startup failed. {ex.GetBaseException().Message}");
        }
    }

    private void StartKeel(List<ModFolder> folders)
    {
        var candidates = new List<(string path, Version version, ModFolder folder)>();
        foreach (ModFolder folder in folders.Where(f => f.Enabled))
        {
            foreach (string path in ModFolders.FindFile(folder.Path, LoaderFile))
            {
                try
                {
                    candidates.Add((path, AssemblyName.GetAssemblyName(path).Version, folder));
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Not a usable {LoaderFile}: {path} ({ex.Message})");
                }
            }
        }
        if (candidates.Count == 0)
        {
            if (folders.Any(f => !f.Enabled && ModFolders.FindFile(f.Path, LoaderFile).Any()))
                Logger.LogInfo("Keel is switched off in the game's mod list.");
            return;
        }

        // Newest first; on a tie the one higher up in the game's mod list.
        (string path, Version version, ModFolder folder) pick =
            candidates.OrderByDescending(c => c.version).ThenBy(c => c.folder.Order).First();
        Logger.LogInfo($"Starting Keel {pick.version} from {pick.folder.Label} ({pick.path})");
        Assembly loader = Assembly.LoadFrom(pick.path);
        Type keel = loader.GetType(LoaderType, throwOnError: true);
        MethodInfo boot = keel.GetMethod("Boot", BindingFlags.Public | BindingFlags.Static);
        if (boot == null)
        {
            Logger.LogError($"Expected {LoaderType}.Boot in {pick.path}.");
            return;
        }
        boot.Invoke(null, new object[] { Info.Metadata.Version.ToString(), pick.path });
    }
}
