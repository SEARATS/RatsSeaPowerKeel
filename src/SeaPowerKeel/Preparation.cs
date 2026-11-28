using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SeaPowerKeel;

// One worker runs the startup helpers in mod-list order. Normal plugins follow Keel's usual schedule.
internal static class Preparation
{
    private static volatile IReadOnlyDictionary<string, Job> jobsByFolder = new Dictionary<string, Job>();

    private class Job(string dll, StartupHelper kind)
    {
        public string Dll { get; } = dll;
        public StartupHelper Kind { get; } = kind;
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal static Task For(string folder, StartupHelper kind = StartupHelper.Prepare)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return Task.CompletedTask;
        try
        {
            return jobsByFolder.TryGetValue(Normal(folder), out Job job) && job.Kind == kind
                ? job.Completion.Task : Task.CompletedTask;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return Task.CompletedTask;
        }
    }

    internal static List<string> Start(IList<ModFolder> folders, string bepInExRoot, string streamingAssets, string gameVersion,
        bool hold, Action<string> status, Action<string> info, Action<string> warn,
        Func<string, Action<string, string[], Action<string>>> openPreloader = null)
    {
        var notes = new List<string>();
        string stateFile = Path.Combine(bepInExRoot, KeelNames.PrepareFile);
        Dictionary<string, string> held = ReadHeld(stateFile, hold, notes);
        ModFolder[] enabled = folders.Where(f => f.Enabled).ToArray();
        var byFolder = new Dictionary<string, Job>(StringComparer.OrdinalIgnoreCase);
        var queue = new List<Job>();
        foreach (ModFolder folder in enabled)
        {
            string preload = ReadOption(folder.InfoLines, StartupHelper.Preload);
            string prepare = ReadOption(folder.InfoLines, StartupHelper.Prepare);
            if (preload != null && prepare != null)
            {
                notes.Add($"Skipped startup helpers in {folder.Label}. Choose either [Keel] Preload= or Prepare=.");
                continue;
            }
            string name = preload ?? prepare;
            if (name == null)
                continue;
            var kind = preload == null ? StartupHelper.Prepare : StartupHelper.Preload;
            if (kind == StartupHelper.Preload && openPreloader == null)
            {
                notes.Add($"Skipped [Keel] Preload= in {folder.Label}. Install the updated Keel stub to enable preloading.");
                continue;
            }
            string dll = ResolveHelper(folder.Path, name, kind);
            if (dll == null)
            {
                string expected = kind == StartupHelper.Preload
                    ? "Specify a file ending in Preloader.dll inside its mod folder."
                    : "Specify a DLL at the top level of its mod folder.";
                notes.Add($"Skipped [Keel] {kind}={name} in {folder.Label}. {expected}");
                continue;
            }
            if (InfoVersion.Mismatch(folder.InfoLines, gameVersion) != null)
            {
                notes.Add($"Skipped startup helper {name} in {folder.Label} because its declared game version differs.");
                continue;
            }
            if (IsHeld(dll, held))
            {
                notes.Add(HeldNote(dll));
                continue;
            }
            var job = new Job(dll, kind);
            if (byFolder.TryAdd(Normal(folder.Path), job))
                queue.Add(job);
        }
        jobsByFolder = byFolder;
        WriteState(stateFile, held);
        if (queue.Count > 0)
        {
            string[] paths = enabled.Select(f => f.Path).ToArray();
            _ = Task.Run(() =>
            {
                foreach (Job job in queue)
                    RunOne(job, streamingAssets, paths, stateFile, held, status, info, warn, openPreloader);
            });
        }
        return notes;
    }

    private static string ReadOption(string[] lines, StartupHelper kind)
    {
        string value = EarlyStart.ValueInInfoIni(lines, kind.ToString())?.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool IsHeld(string dll, Dictionary<string, string> held) =>
        held.TryGetValue(dll, out string stamp) && stamp == CrashGuard.Stamp(dll);

    private static string HeldNote(string dll) =>
        $"Skipped {Path.GetFileName(dll)} after the last game ended during preparation. " +
        $"It can run again after a mod update or after clearing {KeelNames.PrepareFile}.";

    internal static string ResolveHelper(string folder, string name, StartupHelper kind)
    {
        string[] segments = name.Split('\\', '/');
        string file = segments[^1];
        if (kind == StartupHelper.Prepare
            ? segments.Length != 1 || !file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            : !file.EndsWith("Preloader.dll", StringComparison.OrdinalIgnoreCase)
                || file.Equals(KeelNames.AnchorChainPreloaderFile, StringComparison.OrdinalIgnoreCase))
            return null;

        string path = folder;
        foreach (string segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.Contains(':')
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || (kind == StartupHelper.Preload && segment != segment.TrimEnd(' ', '.')))
                return null;

            path = Path.Combine(path, segment);
            if (kind == StartupHelper.Preload)
            {
                try
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        return null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return null;
                }
            }
        }
        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }

    private static void RunOne(Job job, string streamingAssets, string[] folders, string stateFile,
        Dictionary<string, string> held, Action<string> status, Action<string> info, Action<string> warn,
        Func<string, Action<string, string[], Action<string>>> openPreloader)
    {
        string name = Path.GetFileName(job.Dll);
        held.Remove(job.Dll);
        WriteState(stateFile, held, job.Dll);
        try
        {
            var run = job.Kind == StartupHelper.Preload ? openPreloader(job.Dll) : PreparationEntry.Open(job.Dll, job.Kind);
            info($"Running {job.Kind} from {name}.");
            run(streamingAssets, (string[])folders.Clone(), status);
            info($"Finished {job.Kind} from {name}.");
        }
        catch (Exception ex)
        {
            warn($"Startup helper {name} failed during {job.Kind}. {ex.Message}");
        }
        finally
        {
            WriteState(stateFile, held);
            status(null);
            job.Completion.TrySetResult(true);
        }
    }

    private static Dictionary<string, string> ReadHeld(string path, bool hold, List<string> notes)
    {
        var held = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in CrashText.ReadLines(path))
        {
            string[] parts = line.Split('\t');
            if (parts.Length != 3)
                continue;
            if (parts[0] == "running")
                notes.Add($"The last game ended while {Path.GetFileName(parts[1])} was preparing startup data.");
            if (hold && parts[0] is "running" or "held")
                held[parts[1]] = parts[2];
        }
        return held;
    }

    private static void WriteState(string path, Dictionary<string, string> held, string running = null)
    {
        try
        {
            if (running == null && held.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            IEnumerable<string> lines = held.Select(h => $"held\t{h.Key}\t{h.Value}");
            if (running != null)
                lines = lines.Append($"running\t{running}\t{CrashGuard.Stamp(running)}");
            string temp = path + ".tmp";
            File.WriteAllLines(temp, lines);
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }
        catch (Exception)
        {
            // Preparation can still run when its crash marker cannot be written.
        }
    }

    private static string Normal(string folder) => Path.GetFullPath(folder).TrimEnd('\\', '/');
}
