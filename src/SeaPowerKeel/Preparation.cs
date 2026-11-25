using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Mono.Cecil;

namespace SeaPowerKeel;

/// <summary>
/// Preparation DLLs (agm-114, PR #16): a mod that has slow work to do before it starts (reading or converting its data) can do it
/// on a worker while the game shows its splash, instead of on the main thread later. Opt-in, in the mod's <c>_info.ini</c>:
/// <code>
/// [Keel]
/// Prepare=MyMod.Prepare.dll
/// </code>
/// The file sits at the top of the mod folder and has one <c>public static void Prepare(string streamingAssets,
/// string[] enabledModFolders, Action&lt;string&gt; report)</c>; <c>report(text)</c> shows a line in Keel's loading window.
/// It must not reference Unity, the game, BepInEx, Harmony, Anchor Chain or Keel: it runs off the main thread, before any of
/// them is ready. The mod's plugin asks <see cref="Keel.WhenPrepared"/> whether the work is done.
/// If the game dies while a preparation runs, that DLL is held at the next starts until it is updated, like a crashing plugin.
/// </summary>
internal static class Preparation
{
    public const string InfoKey = "Prepare";

    private static readonly ConcurrentDictionary<string, Task> byFolder = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object fileLock = new();
    private static string stateFile;

    /// <summary>The folder's preparation; a finished task when it has none (or it was skipped).</summary>
    internal static Task For(string folder) =>
        folder != null && byFolder.TryGetValue(Normal(folder), out Task t) ? t : Task.CompletedTask;

    /// <summary>
    /// Starts the preparations of <paramref name="enabled"/> one after another on a worker. Returns the notes for the report
    /// (held DLLs, wrong opt-ins); the worker logs its own results.
    /// </summary>
    internal static List<string> Start(IList<ModFolder> enabled, string bepInExRoot, string streamingAssets, string gameVersion,
        bool hold, Action<string> status, Action<string> info, Action<string> warn)
    {
        var notes = new List<string>();
        stateFile = Path.Combine(bepInExRoot, KeelNames.PrepareFile);
        Dictionary<string, string> held = TakeHeld(hold, notes);
        string[] folders = enabled.Select(f => f.Path).ToArray();
        var queue = new List<(string Folder, string Dll)>();
        foreach (string folder in folders)
        {
            string dll = Requested(folder, notes);
            if (dll == null || InfoVersion.MismatchInFolder(folder, gameVersion) != null)
                continue;
            if (held.TryGetValue(dll, out string stamp) && stamp == CrashGuard.Stamp(dll))
            {
                notes.Add($"{Path.GetFileName(dll)} ({Path.GetFileName(folder)}) is not prepared: the game ended while it ran. " +
                          $"Keel runs it again when the mod is updated, or after {KeelNames.PrepareFile} is deleted.");
                continue;
            }
            queue.Add((folder, dll));
        }
        SaveHeld(held.Where(h => queue.All(q => q.Dll != h.Key)));
        if (queue.Count == 0)
            return notes;
        // One worker, one DLL after the other: preparations read a lot from disk, and in parallel they would fight over it.
        Task previous = Task.CompletedTask;
        foreach ((string folder, string dll) in queue)
        {
            string d = dll;
            previous = previous.ContinueWith(_ => RunOne(d, streamingAssets, folders, status, info, warn), TaskScheduler.Default);
            byFolder[Normal(folder)] = previous;
        }
        return notes;
    }

    // The DLL named by [Keel] Prepare=, when it is a file at the top of the folder; null otherwise (a wrong name is noted).
    internal static string Requested(string folder, List<string> notes)
    {
        string name;
        try
        {
            string ini = Path.Combine(folder ?? "", "_info.ini");
            name = File.Exists(ini) ? EarlyStart.ValueInInfoIni(File.ReadAllText(ini), InfoKey) : null;
        }
        catch (Exception)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(name))
            return null;
        name = name.Trim().Trim('"');
        string path = Path.Combine(folder, name);
        if (name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            notes?.Add($"{Path.GetFileName(folder)}: [Keel] Prepare={name} is not a DLL at the top of the mod folder; nothing is prepared.");
            return null;
        }
        return Path.GetFullPath(path);
    }

    private static void RunOne(string dll, string streamingAssets, string[] folders, Action<string> status, Action<string> info, Action<string> warn)
    {
        MarkRunning(dll, true);
        try
        {
            MethodInfo prepare = Entry(dll);
            info($"Preparing startup data with {Path.GetFileName(dll)}.");
            prepare.Invoke(null, new object[] { streamingAssets, (string[])folders.Clone(), status });
            info($"{Path.GetFileName(dll)} is done preparing.");
        }
        catch (Exception ex)
        {
            warn($"Startup preparation failed for {dll}: {(ex is TargetInvocationException { InnerException: { } inner } ? inner : ex).Message}");
        }
        finally
        {
            MarkRunning(dll, false);
            status(null);
        }
    }

    // Checked with Cecil before anything is loaded: one public static Prepare with the right signature, and no reference to
    // what is not safe off the main thread or not ready yet.
    internal static MethodInfo Entry(string dll)
    {
        string typeName;
        using (var metadata = AssemblyDefinition.ReadAssembly(dll))
        {
            string bad = metadata.MainModule.AssemblyReferences.Select(r => r.Name).FirstOrDefault(Forbidden);
            if (bad != null)
                throw new InvalidOperationException($"it references {bad}; a preparation DLL must not use the game or a mod loader.");
            string[] types = metadata.MainModule.Types.Where(t => t.IsPublic && !t.HasGenericParameters && t.Methods.Any(IsPrepare))
                                     .Select(t => t.FullName).ToArray();
            if (types.Length != 1)
                throw new InvalidOperationException($"expected one public static Prepare(string, string[], Action<string>), found {types.Length}.");
            typeName = types[0];
        }
        Assembly assembly = Assembly.LoadFrom(dll);
        if (!Path.GetFullPath(assembly.Location).Equals(Path.GetFullPath(dll), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"its assembly name is already used by {assembly.Location}.");
        return assembly.GetType(typeName, throwOnError: true).GetMethod("Prepare", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            null, new[] { typeof(string), typeof(string[]), typeof(Action<string>) }, null);
    }

    internal static bool Forbidden(string reference) =>
        reference.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) || reference.StartsWith("AnchorChain", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("SeaPowerKeel", StringComparison.OrdinalIgnoreCase)
        || reference is "Assembly-CSharp" or "Seapower-Scripts" or "Noesis.NoesisGUI" or "BepInEx" or "0Harmony";

    private static bool IsPrepare(MethodDefinition m) =>
        m.Name == "Prepare" && m.IsPublic && m.IsStatic && !m.HasGenericParameters && m.ReturnType.FullName == "System.Void"
        && m.Parameters.Select(p => p.ParameterType.FullName)
            .SequenceEqual(new[] { "System.String", "System.String[]", "System.Action`1<System.String>" });

    // Keel-prepare.txt: "running <tab> dll <tab> stamp" while one runs, "held <tab> dll <tab> stamp" after the game died during it.
    // A line still "running" at start-up means the last game ended inside that preparation: it becomes held (when hold is on).
    private static Dictionary<string, string> TakeHeld(bool hold, List<string> notes)
    {
        var held = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in CrashText.ReadLines(stateFile))
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

    private static void SaveHeld(IEnumerable<KeyValuePair<string, string>> held) =>
        WriteState(held.Select(h => $"held\t{h.Key}\t{h.Value}").ToList());

    private static void MarkRunning(string dll, bool running)
    {
        lock (fileLock)
        {
            List<string> lines = CrashText.ReadLines(stateFile).Where(l => !l.StartsWith("running\t")).ToList();
            if (running)
                lines.Add($"running\t{dll}\t{CrashGuard.Stamp(dll)}");
            WriteState(lines);
        }
    }

    private static void WriteState(List<string> lines)
    {
        try
        {
            if (lines.Count > 0)
                File.WriteAllLines(stateFile, lines);
            else if (File.Exists(stateFile))
                File.Delete(stateFile);
        }
        catch (Exception)
        {
            // without the file a crash in a preparation is only named by the crash journal, not held
        }
    }

    private static string Normal(string folder) => Path.GetFullPath(folder).TrimEnd('\\', '/');
}
