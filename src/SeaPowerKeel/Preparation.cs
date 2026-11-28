using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SeaPowerKeel;

// One worker runs the startup helpers in mod-list order. Normal plugins follow Keel's usual schedule.
internal static class Preparation
{
    public const string InfoKey = "Prepare";
    private static volatile IReadOnlyDictionary<string, Job> jobsByFolder = new Dictionary<string, Job>();

    private class Job(string folder, string named, bool namedHeld)
    {
        public string Folder { get; } = folder;
        public string Named { get; } = named;
        public bool NamedHeld { get; } = namedHeld;
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private class Helper(string dll, Func<string, Action<string, string[], Action<string>>> open)
    {
        public string Dll { get; } = dll;
        public Func<string, Action<string, string[], Action<string>>> Open { get; } = open;
    }

    internal static Task For(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return Task.CompletedTask;
        try
        {
            return jobsByFolder.TryGetValue(Normal(folder), out Job job) ? job.Completion.Task : Task.CompletedTask;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return Task.CompletedTask;
        }
    }

    internal static List<string> Start(IList<ModFolder> folders, string bepInExRoot, string streamingAssets, string gameVersion,
        bool hold, Action<string> status, Action<string> info, Action<string> warn,
        Func<string, string[]> findPreloaders = null, Func<string, Action<string, string[], Action<string>>> openPreloader = null)
    {
        var notes = new List<string>();
        string stateFile = Path.Combine(bepInExRoot, KeelNames.PrepareFile);
        Dictionary<string, string> held = ReadHeld(stateFile, hold, notes);
        ModFolder[] enabled = folders.Where(f => f.Enabled).ToArray();
        var byFolder = new Dictionary<string, Job>(StringComparer.OrdinalIgnoreCase);
        var queue = new List<Job>();
        foreach (ModFolder folder in enabled)
        {
            string dll = Requested(folder, notes);
            if (dll == null && findPreloaders == null)
                continue;
            if (InfoVersion.Mismatch(folder.InfoLines, gameVersion) != null)
            {
                if (dll != null)
                    notes.Add($"Skipped startup preparation for {folder.Label} because its declared game version differs.");
                continue;
            }
            bool namedHeld = dll != null && IsHeld(dll, held);
            if (namedHeld)
            {
                notes.Add(HeldNote(dll));
                if (findPreloaders == null)
                    continue;
            }
            var job = new Job(folder.Path, dll, namedHeld);
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
                // Resolve empty folders before starting any helper so their tasks can finish immediately after discovery.
                var batches = new List<(Job Job, List<Helper> Helpers)>();
                foreach (Job job in queue)
                {
                    List<Helper> helpers = FindHelpers(job, held, findPreloaders, openPreloader, warn);
                    if (helpers.Count == 0)
                        job.Completion.TrySetResult(true);
                    else
                        batches.Add((job, helpers));
                }
                foreach (var (job, helpers) in batches)
                {
                    foreach (Helper helper in helpers)
                        RunOne(helper, streamingAssets, paths, stateFile, held, status, info, warn);
                    job.Completion.TrySetResult(true);
                }
            });
        }
        return notes;
    }

    private static List<Helper> FindHelpers(Job job, Dictionary<string, string> held, Func<string, string[]> findPreloaders,
        Func<string, Action<string, string[], Action<string>>> openPreloader, Action<string> warn)
    {
        var helpers = new List<Helper>();
        if (findPreloaders != null)
        {
            try
            {
                foreach (string dll in findPreloaders(job.Folder))
                {
                    if (IsHeld(dll, held))
                    {
                        if (!ModFolders.SamePath(dll, job.Named))
                            warn(HeldNote(dll));
                    }
                    else
                        helpers.Add(new Helper(dll, openPreloader));
                }
            }
            catch (Exception ex)
            {
                warn($"Could not find startup preloaders in {job.Folder}. {ex.Message}");
            }
        }
        if (job.Named != null && !job.NamedHeld && helpers.All(helper => !ModFolders.SamePath(helper.Dll, job.Named)))
            helpers.Add(new Helper(job.Named, PreparationEntry.Open));
        return helpers;
    }

    private static bool IsHeld(string dll, Dictionary<string, string> held) =>
        held.TryGetValue(dll, out string stamp) && stamp == CrashGuard.Stamp(dll);

    private static string HeldNote(string dll) =>
        $"Skipped {Path.GetFileName(dll)} after the last game ended during preparation. " +
        $"It can run again after a mod update or after clearing {KeelNames.PrepareFile}.";

    internal static string Requested(ModFolder folder, List<string> notes)
    {
        string name = EarlyStart.ValueInInfoIni(folder.InfoLines, InfoKey)?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(name))
            return null;
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && name.IndexOfAny(['/', '\\', ':']) < 0
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
        {
            string path = Path.Combine(folder.Path, name);
            if (File.Exists(path))
                return Path.GetFullPath(path);
        }
        notes.Add($"Skipped [Keel] Prepare={name} in {folder.Label}. Specify a DLL in the top level of the mod folder.");
        return null;
    }

    private static void RunOne(Helper helper, string streamingAssets, string[] folders, string stateFile,
        Dictionary<string, string> held, Action<string> status, Action<string> info, Action<string> warn)
    {
        string name = Path.GetFileName(helper.Dll);
        held.Remove(helper.Dll);
        WriteState(stateFile, held, helper.Dll);
        try
        {
            Action<string, string[], Action<string>> prepare = helper.Open(helper.Dll);
            info($"Preparing startup data with {name}.");
            prepare(streamingAssets, (string[])folders.Clone(), status);
            info($"Finished startup preparation with {name}.");
        }
        catch (Exception ex)
        {
            warn($"Startup preparation failed for {name} ({helper.Dll}). {ex.Message}");
        }
        finally
        {
            WriteState(stateFile, held);
            status(null);
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
