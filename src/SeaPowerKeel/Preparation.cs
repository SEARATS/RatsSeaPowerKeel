using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SeaPowerKeel;

internal static class Preparation
{
    private static volatile IReadOnlyDictionary<string, Task> jobsByFolder = new Dictionary<string, Task>();

    internal static Task For(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return Task.CompletedTask;
        try
        {
            return jobsByFolder.TryGetValue(Normal(folder), out Task task) ? task : Task.CompletedTask;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return Task.CompletedTask;
        }
    }

    internal static List<string> Start(IList<ModFolder> folders, string bepInExRoot, string streamingAssets, string gameVersion,
        bool hold, Action<string> status, Action<string> info, Action<string> warn)
    {
        var notes = new List<string>();
        var journal = new StartupJournal(Path.Combine(bepInExRoot, KeelNames.PrepareFile), hold, notes.Add);
        ModFolder[] enabled = folders.Where(f => f.Enabled).ToArray();
        var byFolder = new Dictionary<string, Task>(StringComparer.OrdinalIgnoreCase);
        var queue = new List<(string Dll, TaskCompletionSource<bool> Completion)>();
        foreach (ModFolder folder in enabled)
        {
            StartupManifest manifest = StartupManifest.Read(folder.InfoLines);
            if (manifest.Prepare == null)
                continue;
            if (manifest.HasConflict)
            {
                notes.Add($"Skipped startup preparation in {folder.Label}. Choose [Keel] Prepare= or [Preloader] Load=.");
                continue;
            }
            string dll = ResolveHelper(folder.Path, manifest.Prepare);
            if (dll == null)
            {
                notes.Add($"Skipped [Keel] Prepare={manifest.Prepare} in {folder.Label}. Specify a DLL at the top level of its mod folder.");
                continue;
            }
            if (InfoVersion.Mismatch(folder.InfoLines, gameVersion) != null)
            {
                notes.Add($"Skipped startup preparation for {folder.Label} because its declared game version differs.");
                continue;
            }
            if (journal.CheckHeld(dll, notes.Add))
                continue;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (byFolder.TryAdd(Normal(folder.Path), completion.Task))
                queue.Add((dll, completion));
        }
        jobsByFolder = byFolder;
        if (queue.Count > 0)
        {
            string[] paths = enabled.Select(f => f.Path).ToArray();
            _ = Task.Run(() =>
            {
                foreach (var (dll, completion) in queue)
                {
                    RunOne(dll, streamingAssets, paths, journal, status, info, warn);
                    completion.TrySetResult(true);
                }
            });
        }
        return notes;
    }

    internal static string ResolveHelper(string folder, string name)
    {
        if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.IndexOfAny(['/', '\\', ':']) >= 0
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        string path = Path.Combine(folder, name);
        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }

    private static void RunOne(string dll, string streamingAssets, string[] folders, StartupJournal journal,
        Action<string> status, Action<string> info, Action<string> warn)
    {
        string name = Path.GetFileName(dll);
        journal.Start(dll);
        try
        {
            var prepare = PreparationEntry.Open(dll, StartupHelper.Prepare);
            info($"Preparing startup data with {name}.");
            prepare(streamingAssets, (string[])folders.Clone(), status);
            info($"Finished startup preparation with {name}.");
        }
        catch (Exception ex)
        {
            warn($"Startup preparation failed for {name} ({dll}). {ex.Message}");
        }
        finally
        {
            journal.Save();
            status(null);
        }
    }

    private static string Normal(string folder) => Path.GetFullPath(folder).TrimEnd('\\', '/');
}
