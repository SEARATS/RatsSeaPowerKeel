using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SeaPowerKeel.Stub;

// Runs during the BepInEx plugin's Awake, before the optional Keel boot. The callback may initialize a loader.
internal static class StartupPreloaders
{
    internal static void Run(IEnumerable<ModFolder> folders, string bepInExRoot, string streamingAssets, string gameVersion,
        Action<string> info, Action<string> warn)
    {
        ModFolder[] enabled = folders.Where(folder => folder.Enabled).ToArray();
        string[] paths = enabled.Select(folder => folder.Path).ToArray();
        var started = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        StartupJournal journal = null;
        foreach (ModFolder folder in enabled)
        {
            StartupManifest manifest = StartupManifest.Read(folder.InfoLines);
            if (manifest.Preloaders.Count == 0)
                continue;
            if (manifest.HasConflict)
            {
                warn($"Skipped preloaders in {folder.Label}. Choose [Preloader] Load= or [Keel] Prepare=.");
                continue;
            }
            if (InfoVersion.Mismatch(folder.InfoLines, gameVersion) != null)
            {
                warn($"Skipped preloaders in {folder.Label} because its declared game version differs.");
                continue;
            }
            foreach (string name in manifest.Preloaders)
            {
                string dll = Resolve(folder.Path, name);
                if (dll == null)
                {
                    warn($"Skipped [Preloader] {name} in {folder.Label}. Specify a file ending in Preloader.dll inside its mod folder.");
                    continue;
                }
                if (!started.Add(dll))
                    continue;

                journal ??= new StartupJournal(Path.Combine(bepInExRoot, KeelNames.PreloaderJournal), true, warn);
                if (journal.CheckHeld(dll, warn))
                    continue;
                journal.Start(dll);
                try
                {
                    var preload = PreparationEntry.Open(dll, StartupHelper.Preload);
                    info($"Starting preloader {name} in {folder.Label}.");
                    preload(streamingAssets, (string[])paths.Clone(), message => { if (!string.IsNullOrEmpty(message)) info(message); });
                }
                catch (Exception ex)
                {
                    warn($"Preloader {name} failed in {folder.Label}. {ex.GetBaseException().Message}");
                }
                finally
                {
                    journal.Save();
                }
            }
        }
    }

    internal static string Resolve(string folder, string name)
    {
        string[] segments = name.Split('\\', '/');
        string file = segments[^1];
        if (!file.EndsWith("Preloader.dll", StringComparison.OrdinalIgnoreCase)
            || file.Equals(KeelNames.AnchorChainPreloaderFile, StringComparison.OrdinalIgnoreCase))
            return null;
        string path = folder;
        try
        {
            foreach (string segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) || segment != segment.TrimEnd(' ', '.')
                    || segment.Contains(':') || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    return null;
                path = Path.Combine(path, segment);
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    return null;
            }
            return File.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
