using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace SeaPowerKeel.Stub;

/// <summary>Starts explicitly listed preloaders, then boots Keel if an enabled mod folder provides it.</summary>
[BepInPlugin(Guid, "R.A.T.S. Keel stub", "1.0.0")]
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
            ModFolder[] enabled = folders.Where(f => f.Enabled).ToArray();
            RunPreloaders(enabled, Application.streamingAssetsPath, Logger.LogInfo, Logger.LogWarning);
            var candidates = new List<(string path, Version version, ModFolder folder)>();
            foreach (ModFolder folder in enabled)
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
            Logger.LogInfo($"Starting R.A.T.S. Keel {pick.version} from {pick.folder.Label} ({pick.path})");
            Assembly loader = Assembly.LoadFrom(pick.path);
            MethodInfo boot = loader.GetType(LoaderType, throwOnError: true)
                                    .GetMethod("Boot", BindingFlags.Public | BindingFlags.Static);
            if (boot == null)
            {
                Logger.LogError($"{pick.path} has no {LoaderType}.Boot: not a Keel loader this stub can start.");
                return;
            }
            boot.Invoke(null, new object[] { Info.Metadata.Version.ToString(), pick.path });
        }
        catch (Exception ex)
        {
            Logger.LogError($"Could not start R.A.T.S. Keel: {(ex is TargetInvocationException t && t.InnerException != null ? t.InnerException : ex)}");
        }
    }

    private static void RunPreloaders(ModFolder[] enabled, string streamingAssets, Action<string> info, Action<string> warn)
    {
        Type[] signature = [typeof(string), typeof(string[]), typeof(Action<string>)];
        string[] folders = enabled.Select(folder => folder.Path).ToArray();
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ModFolder folder in enabled)
        {
            foreach (string name in ListedPreloaders(folder.InfoLines))
            {
                try
                {
                    string path = PreloaderPath(folder.Path, name);
                    if (path == null)
                        throw new InvalidOperationException("Use a relative path to a Preloader.dll inside the mod folder.");
                    if (!loaded.Add(path))
                        continue;
                    Assembly assembly = Assembly.LoadFrom(path);
                    if (!ModFolders.SamePath(path, assembly.Location))
                        throw new InvalidOperationException($"The assembly name is already used by {assembly.Location}.");
                    MethodInfo preload = assembly.GetExportedTypes()
                        .Select(type => type.GetMethod("Preload", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                            null, signature, null))
                        .Single(method => method?.ReturnType == typeof(void) && !method.ContainsGenericParameters);
                    preload.Invoke(null, [streamingAssets, folders.Clone(), info]);
                }
                catch (Exception ex)
                {
                    warn($"Preloader {name} failed in {folder.Label}. {ex.GetBaseException().Message}");
                }
            }
        }
    }

    private static List<string> ListedPreloaders(string[] lines)
    {
        var files = new List<string>();
        string section = "";
        foreach (string raw in lines)
        {
            string line = raw.Split(';', '#')[0].Trim();
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                section = line;
                continue;
            }
            string[] pair = line.Split('=', 2);
            if (pair.Length != 2)
                continue;
            string key = pair[0].Trim();
            string value = pair[1].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (section.Equals("[Keel]", StringComparison.OrdinalIgnoreCase) && key.Equals("Prepare", StringComparison.OrdinalIgnoreCase))
                return [];
            if (section.Equals("[Preloader]", StringComparison.OrdinalIgnoreCase) &&
                key.StartsWith("Load", StringComparison.OrdinalIgnoreCase) && key.Skip(4).All(char.IsDigit))
                files.Add(value);
        }
        return files;
    }

    private static string PreloaderPath(string folder, string name)
    {
        string[] parts = name.Split('/', '\\');
        string file = parts[^1];
        if (!file.EndsWith("Preloader.dll", StringComparison.OrdinalIgnoreCase) ||
            file.Equals(KeelNames.AnchorChainPreloaderFile, StringComparison.OrdinalIgnoreCase))
            return null;
        foreach (string part in parts)
        {
            if (string.IsNullOrWhiteSpace(part) || part != part.TrimEnd(' ', '.') ||
                part.Contains(':') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return null;
            folder = Path.Combine(folder, part);
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                return null;
        }
        return File.Exists(folder) ? Path.GetFullPath(folder) : null;
    }
}
