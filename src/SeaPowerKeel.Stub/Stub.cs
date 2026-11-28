using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;

namespace SeaPowerKeel.Stub;

/// <summary>
/// The only part of Keel that goes into <c>BepInEx\plugins</c>. It finds <c>SeaPowerKeel.dll</c> in the game's
/// enabled mod folders (newest version wins), and discovers and loads startup helpers for Keel's worker.
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
                bool off = folders.Any(f => !f.Enabled && ModFolders.FindFile(f.Path, LoaderFile).Any());
                Logger.LogWarning(off
                    ? "R.A.T.S. Keel is switched off in the game's mod list: no Keel mod loads. Switch it on in Mods and restart."
                    : "R.A.T.S. Keel is not installed (subscribe to it on the Workshop): no Keel mod loads.");
                return;
            }
            // Newest first; on a tie the one higher up in the game's mod list.
            (string path, Version version, ModFolder folder) pick =
                candidates.OrderByDescending(c => c.version).ThenBy(c => c.folder.Order).First();
            Logger.LogInfo($"Starting R.A.T.S. Keel {pick.version} from {pick.folder.Label} ({pick.path})");
            Assembly loader = Assembly.LoadFrom(pick.path);
            Type keel = loader.GetType(LoaderType, throwOnError: true);
            if (!StartupPreloaders.Boot(keel, Info.Metadata.Version.ToString(), pick.path))
                Logger.LogError($"{pick.path} has no {LoaderType}.Boot: not a Keel loader this stub can start.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Could not start R.A.T.S. Keel: {(ex is TargetInvocationException t && t.InnerException != null ? t.InnerException : ex)}");
        }
    }
}
