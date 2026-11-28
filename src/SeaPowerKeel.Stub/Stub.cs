using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace SeaPowerKeel.Stub;

/// <summary>
/// Installed in <c>BepInEx\plugins</c>. Starts standalone preloaders, then boots the newest Keel from the enabled mod folders.
/// </summary>
[BepInPlugin(Guid, "R.A.T.S. Keel stub", "1.0.0")]
public class Stub : BaseUnityPlugin
{
    public const string Guid = KeelNames.StubGuid;
    private const string LoaderFile = KeelNames.LoaderFile;
    private const string LoaderType = "SeaPowerKeel.Keel";
    private const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;

    private void Awake()
    {
        try
        {
            List<ModFolder> folders = ModFolders.FromDisk();
            ModFolder[] enabled = folders.Where(f => f.Enabled).ToArray();
            string[] enabledPaths = Array.ConvertAll(enabled, folder => folder.Path);
            Type[] signature = [typeof(string), typeof(string[]), typeof(Action<string>)];
            const BindingFlags preloadFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
            Action<string> report = Logger.LogInfo;
            var candidates = new List<(string path, Version version, ModFolder folder)>();
            foreach (ModFolder folder in enabled)
            {
                foreach (string path in FindPreloaders(folder.Path).Concat(ModFolders.FindFile(folder.Path, LoaderFile)))
                {
                    string name = Path.GetFileName(path);
                    bool isKeel = name.Equals(LoaderFile, IgnoreCase);
                    try
                    {
                        if (isKeel)
                        {
                            candidates.Add((path, AssemblyName.GetAssemblyName(path).Version, folder));
                            continue;
                        }
                        Assembly assembly = Assembly.LoadFrom(path);
                        if (!ModFolders.SamePath(path, assembly.Location))
                            throw new InvalidOperationException($"The assembly name is already used by {assembly.Location}.");
                        MethodInfo method = assembly.GetExportedTypes()
                            .Select(type => type.GetMethod("Preload", preloadFlags, null, signature, null))
                            .Single(method => method?.ReturnType == typeof(void) && !method.ContainsGenericParameters);
                        method.Invoke(null, [Application.streamingAssetsPath, enabledPaths.Clone(), report]);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(isKeel
                            ? $"Not a usable {LoaderFile}: {path} ({ex.Message})"
                            : $"Preloader {name} failed in {folder.Label}. {ex.GetBaseException().Message}");
                    }
                }
            }
            if (candidates.Count == 0)
            {
                bool off = folders.Any(f => !f.Enabled && ModFolders.FindFile(f.Path, LoaderFile).Any());
                Logger.LogWarning(off
                    ? "R.A.T.S. Keel is switched off in the game's mod list: no Keel mod loads. Switch it on in Mods and restart."
                    : "R.A.T.S. Keel is not installed (subscribe to it on the Workshop): no Keel mod loads.");
            }
            else
            {
                // Newest first; on a tie the one higher up in the game's mod list.
                var pick = candidates.OrderByDescending(c => c.version).ThenBy(c => c.folder.Order).First();
                Logger.LogInfo($"Starting R.A.T.S. Keel {pick.version} from {pick.folder.Label} ({pick.path})");
                Assembly loader = Assembly.LoadFrom(pick.path);
                MethodInfo boot = loader.GetType(LoaderType, throwOnError: true)
                                        .GetMethod("Boot", BindingFlags.Public | BindingFlags.Static);
                if (boot == null)
                    Logger.LogError($"{pick.path} has no {LoaderType}.Boot: not a Keel loader this stub can start.");
                else
                    boot.Invoke(null, [Info.Metadata.Version.ToString(), pick.path]);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Could not start R.A.T.S. Keel: {(ex is TargetInvocationException t && t.InnerException != null ? t.InnerException : ex)}");
        }
    }

    private static string[] FindPreloaders(string folder)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        try
        {
            return Directory.GetDirectories(folder, "*", options).OrderBy(dir => dir, StringComparer.OrdinalIgnoreCase).Prepend(folder)
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*Preloader.dll", options).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                .Where(path => !Path.GetFileName(path).Equals(KeelNames.AnchorChainPreloaderFile, IgnoreCase)).ToArray();
        }
        catch (IOException)
        {
            return [];
        }
    }
}
