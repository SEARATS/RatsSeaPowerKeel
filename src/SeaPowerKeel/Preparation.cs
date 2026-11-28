using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Mono.Cecil;

namespace SeaPowerKeel;

// One worker runs the explicitly named helpers in mod-list order. The normal plugin follows Keel's usual schedule.
internal static class Preparation
{
    public const string InfoKey = "Prepare";
    private static volatile IReadOnlyDictionary<string, Job> jobsByFolder = new Dictionary<string, Job>();

    private class Job(string dll)
    {
        public string Dll { get; } = dll;
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        bool hold, Action<string> status, Action<string> info, Action<string> warn)
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
            if (dll == null)
                continue;
            if (InfoVersion.Mismatch(folder.InfoLines, gameVersion) != null)
            {
                notes.Add($"Skipped startup preparation for {folder.Label} because its declared game version differs.");
                continue;
            }
            if (held.TryGetValue(dll, out string stamp) && stamp == CrashGuard.Stamp(dll))
            {
                notes.Add($"Skipped {Path.GetFileName(dll)} in {folder.Label} after the last game ended during preparation. " +
                          $"It can run again after a mod update or after clearing {KeelNames.PrepareFile}.");
                continue;
            }
            held.Remove(dll);
            var job = new Job(dll);
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
                    RunOne(job, streamingAssets, paths, stateFile, held, status, info, warn);
            });
        }
        return notes;
    }

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

    private static void RunOne(Job job, string streamingAssets, string[] folders, string stateFile,
        Dictionary<string, string> held, Action<string> status, Action<string> info, Action<string> warn)
    {
        string name = Path.GetFileName(job.Dll);
        WriteState(stateFile, held, job.Dll);
        try
        {
            Action<string, string[], Action<string>> prepare = Entry(job.Dll);
            info($"Preparing startup data with {name}.");
            prepare(streamingAssets, (string[])folders.Clone(), status);
            info($"Finished startup preparation with {name}.");
        }
        catch (Exception ex)
        {
            warn($"Startup preparation failed for {name} ({job.Dll}). {ex.Message}");
        }
        finally
        {
            WriteState(stateFile, held);
            status(null);
            job.Completion.TrySetResult(true);
        }
    }

    private static Action<string, string[], Action<string>> Entry(string dll)
    {
        string typeName;
        using (var metadata = AssemblyDefinition.ReadAssembly(dll))
        {
            ModuleDefinition module = metadata.MainModule;
            string bad = module.AssemblyReferences.Select(r => r.Name).FirstOrDefault(Forbidden);
            if (bad != null)
                throw new InvalidOperationException($"The helper references {bad}. Only use code that can run without the game or loader.");
            string[] types = module.Types.Where(t => t.IsPublic && !t.HasGenericParameters && t.Methods.Any(IsPrepare))
                .Select(t => t.FullName).Take(2).ToArray();
            if (types.Length != 1)
                throw new InvalidOperationException("Expected one public static void Prepare(string, string[], Action<string>).");
            typeName = types[0];
        }
        Assembly assembly = Assembly.LoadFrom(dll);
        if (!ModFolders.SamePath(Path.GetFullPath(assembly.Location), dll))
            throw new InvalidOperationException($"The assembly name is already used by {assembly.Location}.");
        Type[] signature = [typeof(string), typeof(string[]), typeof(Action<string>)];
        MethodInfo method = assembly.GetType(typeName, throwOnError: true)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Single(m => m.Name == "Prepare" && !m.IsGenericMethod && m.ReturnType == typeof(void)
                && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(signature));
        return (Action<string, string[], Action<string>>)method.CreateDelegate(typeof(Action<string, string[], Action<string>>));
    }

    private static bool IsPrepare(MethodDefinition m) =>
        m.Name == "Prepare" && m.IsPublic && m.IsStatic && !m.HasGenericParameters && m.ReturnType.FullName == "System.Void"
        && m.Parameters.Select(p => p.ParameterType.FullName)
            .SequenceEqual(new[] { "System.String", "System.String[]", "System.Action`1<System.String>" });

    internal static bool Forbidden(string reference) =>
        reference.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) || reference.StartsWith("AnchorChain", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("SeaPowerKeel", StringComparison.OrdinalIgnoreCase)
        || reference is "Assembly-CSharp" or "Seapower-Scripts" or "Noesis.NoesisGUI" or "BepInEx" or "0Harmony";

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
