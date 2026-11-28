using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace SeaPowerKeel.Stub;

/// <summary>
/// Installed in <c>BepInEx\plugins</c>. Starts opted-in preloaders, then boots the newest Keel from the enabled mod folders.
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
            var preloaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates = new List<(string path, Version version, ModFolder folder)>();
            foreach (ModFolder folder in enabled)
            {
                foreach (string name in ListedPreloaders(folder.InfoLines))
                {
                    try
                    {
                        string root = Path.GetFullPath(folder.Path).TrimEnd('\\', '/');
                        string path = Path.GetFullPath(Path.Combine(root, name));
                        if (Path.IsPathRooted(name) || name.Contains(':') || !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                            !path.EndsWith("Preloader.dll", IgnoreCase) || Path.GetFileName(path).Equals(KeelNames.AnchorChainPreloaderFile, IgnoreCase))
                            throw new ArgumentException("Use a relative path to a Preloader.dll inside the mod folder.");
                        if (!preloaded.Add(path))
                            continue;
                        for (string entry = path; entry.Length > root.Length; entry = Path.GetDirectoryName(entry))
                            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                                throw new IOException($"The path crosses a link at {entry}.");

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
                        Logger.LogWarning($"Preloader {name} failed in {folder.Label}. {ex.GetBaseException().Message}");
                    }
                }

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

    private static List<string> ListedPreloaders(string[] lines)
    {
        var files = new List<string>();
        string section = "";
        foreach (string raw in lines)
        {
            string line = raw.Split(';', '#')[0].Trim();
            if (line.StartsWith('['))
                section = line;
            if (line.Split('=', 2) is not [string key, string rawValue])
                continue;
            string value = rawValue.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value))
                continue;
            key = key.Trim();
            if (section.Equals("[Keel]", IgnoreCase) && key.Equals("Prepare", IgnoreCase))
                return [];
            if (section.Equals("[Preloader]", IgnoreCase) && key.StartsWith("Load", IgnoreCase) && key.Skip(4).All(char.IsDigit))
                files.Add(value);
        }
        return files;
    }
}
