using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using UnityEngine;

namespace SeaPowerKeel;

/// <summary>
/// Loads and starts plugins one at a time. A plugin that throws is marked and the next one starts: one broken mod never
/// stops the others or freezes the game. Errors a mod throws later in the session are counted against it too (matched by
/// the mod's namespaces in the stack trace), so the report shows which mod a red line in the log belongs to.
/// </summary>
internal sealed class Runner
{
    private readonly ManualLogSource log;
    private readonly GameObject host;
    private readonly Dictionary<string, Assembly> loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> libraryIndex = new(StringComparer.OrdinalIgnoreCase);  // assembly name -> path
    private readonly List<(string[] namespaces, PluginEntry plugin)> owners = new();
    private PluginEntry starting;
    private string startupException;

    public string AnchorChainDll;

    /// <summary>Called for every exception Unity logs that Keel can pin on a mod: the mod, the message, the stack trace.</summary>
    public Action<PluginEntry, string, string> ModError;

    /// <summary>The mod whose code appears in this text (a message with a stack trace), or null.</summary>
    public string OwnerOf(string text) => Find(text)?.Display;

    /// <summary>
    /// The plugin a stack trace belongs to. A plugin's own class beats a namespace match (several plugins of one DLL share
    /// the namespace), and the topmost frame wins (a mod calling into another mod's code is not the one that threw).
    /// </summary>
    private PluginEntry Find(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        PluginEntry best = null;
        int bestAt = int.MaxValue;
        foreach ((string[] namespaces, PluginEntry plugin) in owners)
        {
            string type = plugin.TypeName.Replace('+', '/');
            foreach (string probe in new[] { plugin.TypeName + ".", plugin.TypeName + ":", type + ".", type + ":" })
            {
                int at = text.IndexOf(probe, StringComparison.Ordinal);
                if (at >= 0 && at < bestAt)
                {
                    best = plugin;
                    bestAt = at;
                }
            }
        }
        if (best != null)
            return best;
        foreach ((string[] namespaces, PluginEntry plugin) in owners)
        {
            foreach (string ns in namespaces)
            {
                int at = text.IndexOf(ns, StringComparison.Ordinal);
                if (at >= 0 && at < bestAt)
                {
                    best = plugin;
                    bestAt = at;
                }
            }
        }
        return best;
    }

    public Runner(ManualLogSource log, GameObject host, IEnumerable<ModFolder> folders)
    {
        this.log = log;
        this.host = host;
        AddFolders(folders);
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        Application.logMessageReceived += OnLog;
    }

    /// <summary>
    /// Libraries a mod ships (and Anchor Chain's DLL, for Anchor Chain mods): found by assembly name when a mod asks for one,
    /// first in mod-list order. Called again for folders switched on in the running game.
    /// </summary>
    public void AddFolders(IEnumerable<ModFolder> folders)
    {
        foreach (ModFolder f in folders)
        {
            try
            {
                foreach (string dll in Directory.GetFiles(f.Path, "*.dll", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileNameWithoutExtension(dll);
                    if (!libraryIndex.ContainsKey(name))
                        libraryIndex[name] = dll;
                }
            }
            catch (Exception)
            {
                // unreadable folder: the scanner already said so
            }
        }
    }

    /// <summary>
    /// Takes a running mod out as far as the running game allows: its Harmony patches come off and a BepInEx plugin's component
    /// is destroyed. Its assembly stays loaded (Mono cannot unload it), and objects or events the mod set up itself stay until
    /// the game restarts. Returns the number of patches removed.
    /// </summary>
    public int TurnOff(PluginEntry p)
    {
        Assembly asm = loaded.TryGetValue(Path.GetFullPath(p.Dll), out Assembly a) ? a : p.Instance?.GetType().Assembly;
        int removed = asm != null ? RemovePatches(p, PatchesIn(asm)) : 0;
        StopInstance(p);
        return removed;
    }

    private static void StopInstance(PluginEntry p)
    {
        if (p.Instance is MonoBehaviour behaviour && behaviour != null)
        {
            behaviour.enabled = false;
            UnityEngine.Object.Destroy(behaviour);
            if (behaviour is BaseUnityPlugin plugin && Chainloader.PluginInfos.TryGetValue(p.Guid, out var info) && info == plugin.Info)
                Chainloader.PluginInfos.Remove(p.Guid);
        }
    }

    private static IEnumerable<(MethodBase target, MethodInfo method)> PatchesIn(Assembly asm) =>
        (from target in HarmonyLib.Harmony.GetAllPatchedMethods()
         let info = HarmonyLib.Harmony.GetPatchInfo(target)
         where info != null
         from patch in AllPatches(info)
         where patch.PatchMethod?.DeclaringType?.Assembly == asm
         select (target, patch.PatchMethod)).Distinct();

    private int RemovePatches(PluginEntry p, IEnumerable<(MethodBase target, MethodInfo method)> patches)
    {
        int removed = 0;
        var harmony = new HarmonyLib.Harmony("dev.sewerlabs.seapowerkeel.turnoff");
        foreach ((MethodBase target, MethodInfo method) in patches.ToList())
        {
            try
            {
                harmony.Unpatch(target, method);
                removed++;
            }
            catch (Exception ex)
            {
                log.LogWarning($"{p.Display}: could not take its patch off {target.DeclaringType?.Name}.{target.Name}: {ex.Message}");
            }
        }
        return removed;
    }

    private Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        Assembly already = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
        if (already != null)
            return already;
        string path = name == "AnchorChain" && AnchorChainDll != null ? AnchorChainDll
                    : libraryIndex.TryGetValue(name, out string p) ? p : null;
        if (path == null && name == AcLoaderShim.PreloaderAssembly && AnchorChainDll != null)
            return PreloaderStub();   // Anchor Chain's DLL needs its preloader's interface to load AnchorChainLoader (see AcLoaderShim)
        if (path == null)
            return null;
        try
        {
            return Load(path);
        }
        catch (Exception ex)
        {
            log.LogWarning($"Could not load {path} for {args.Name}: {ex.Message}");
            return null;
        }
    }

    private Assembly preloaderStub;

    /// <summary>Loads Keel's stand-in for Anchor Chain's preloader now, before anything asks for it (see the takeover).</summary>
    public void UsePreloaderStandIn()
    {
        if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == AcLoaderShim.PreloaderAssembly))
            PreloaderStub();
    }

    private Assembly PreloaderStub()
    {
        if (preloaderStub == null)
        {
            try
            {
                preloaderStub = AcLoaderShim.BuildPreloaderStub();
                keelLoaded.Add(preloaderStub);
                log.LogInfo("Anchor Chain's preloader is not installed: Keel provides the one interface of it that Anchor Chain's DLL needs (AnchorChain.Preloader.IPluginLoader).");
            }
            catch (Exception ex)
            {
                log.LogWarning($"Could not make the AnchorChain.Preloader stand-in: {ex.Message}");
            }
        }
        return preloaderStub;
    }

    /// <summary>
    /// Mods that call <c>AnchorChainLoader.FindFile</c> (Sonar Audio Loader does, for audio in other mods' folders) need its list
    /// of mod folders, which only Anchor Chain's own loader fills. Null when done, else why not.
    /// </summary>
    public string PrimeAnchorChainLoader(IEnumerable<string> folders, string configDir)
    {
        if (AnchorChainDll == null)
            return "Anchor Chain's DLL was not found";
        try
        {
            return AcLoaderShim.Prime(Load(AnchorChainDll), folders, configDir);
        }
        catch (Exception ex)
        {
            Exception inner = Reflected.Unwrap(ex);
            return $"{inner.GetType().Name}: {inner.Message}";
        }
    }

    private readonly HashSet<Assembly> keelLoaded = new();
    private readonly HashSet<PluginEntry> ownerRegistered = new();

    private static string LocationOf(Assembly a)
    {
        try
        {
            return a.IsDynamic ? null : a.Location;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Assembly Load(string path)
    {
        string full = Path.GetFullPath(path);
        if (!loaded.TryGetValue(full, out Assembly a))
        {
            // Already in the game (someone else loaded that very file): use that one, it is not ours.
            a = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => string.Equals(LocationOf(x), full, StringComparison.OrdinalIgnoreCase));
            if (a == null)
            {
                a = Assembly.LoadFrom(full);
                keelLoaded.Add(a);
            }
            loaded[full] = a;
        }
        return a;
    }

    // Everything that gets loaded while Keel loads or starts a plugin is Keel's doing (an AnchorChain.dll copy next to a mod,
    // found by the runtime's own probing, included): it must not make Keel think Anchor Chain is running.
    private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs e)
    {
        if (starting != null)
            keelLoaded.Add(e.LoadedAssembly);
    }

    /// <summary>Did Keel itself load this assembly (as opposed to Anchor Chain's preloader, BepInEx or the game)?</summary>
    public bool OwnsAssembly(Assembly a) => keelLoaded.Contains(a);

    /// <summary>Is an Anchor Chain loader in the game that Keel did not bring in itself?</summary>
    public bool AnchorChainRunning() => GameState.AnchorChainRunning(OwnsAssembly);

    /// <summary>The name of the other loader that is running, or null.</summary>
    public string OtherLoader() => GameState.OtherLoader(OwnsAssembly);

    private void RegisterOwner(PluginEntry p, Assembly asm)
    {
        if (ownerRegistered.Add(p))
            owners.Add((Namespaces(asm), p));
    }

    /// <summary>
    /// First phase for every plugin: load its DLL and, for an Anchor Chain mod, create the instance (Anchor Chain does this
    /// for all mods before any of them starts, so a mod's start already sees every other mod's DLL loaded). Nothing of the
    /// mod's entry point runs. A mod that cannot be loaded or created, or has no <c>IAnchorChainMod.TriggerEntryPoint</c>,
    /// is marked <see cref="PluginState.Failed"/>.
    /// </summary>
    public void Prepare(PluginEntry p)
    {
        var watch = Stopwatch.StartNew();
        p.FirstStartupError = null;
        starting = p;
        startupException = null;
        try
        {
            Assembly asm = Load(p.Dll);
            Type type = asm.GetType(p.TypeName, throwOnError: true);
            RegisterOwner(p, asm);
            if (p.Kind == PluginKind.AnchorChain)
            {
                if (AcEntry.FindEntryPoint(type) == null)
                {
                    p.Stop(PluginState.Failed, $"Entry point not found: {type.FullName} does not implement {AcEntry.InterfaceName}.{AcEntry.MethodName}() (the DLL's copy of Anchor Chain's interface differs?).");
                    log.LogError($"{p.Display} ({p.Guid}): {p.Reason}");
                    return;
                }
                p.Instance = Activator.CreateInstance(type);
                if (startupException != null)
                    p.Stop(PluginState.Failed, "Exception while being created: " + startupException);
            }
        }
        catch (Exception ex)
        {
            Exception inner = Reflected.Unwrap(ex);
            p.Stop(PluginState.Failed, Explain(inner));
            log.LogError($"{p.Display} ({p.Guid}) failed to load: {inner}");
        }
        finally
        {
            starting = null;
            p.StartMs += (float)watch.Elapsed.TotalMilliseconds;
        }
    }

    public void Start(PluginEntry p)
    {
        var watch = Stopwatch.StartNew();
        Assembly asm = null;
        HashSet<(MethodBase target, MethodInfo method)> before = null;
        starting = p;
        startupException = null;
        try
        {
            asm = Load(p.Dll);
            Type type = asm.GetType(p.TypeName, throwOnError: true);
            RegisterOwner(p, asm);
            before = PatchesIn(asm).ToHashSet();
            if (p.Kind == PluginKind.BepInEx)
            {
                var plugin = (BaseUnityPlugin)host.AddComponent(type);
                p.Instance = plugin;
                if (plugin != null && plugin.Info != null && !Chainloader.PluginInfos.ContainsKey(p.Guid))
                    Chainloader.PluginInfos[p.Guid] = plugin.Info;   // so [BepInDependency] checks and other mods see it
            }
            else
            {
                p.Instance ??= Activator.CreateInstance(type);
                AcEntry.Invoke(p.Instance);   // through the interface: explicit implementations work; a missing entry point throws
            }
            if (startupException != null)
                p.Stop(PluginState.Failed, "Exception while starting: " + startupException);
            else if (p.FirstStartupError != null)
                p.Stop(PluginState.RunningWithErrors, "Logged an error during startup: " + p.FirstStartupError);
            else
                p.State = PluginState.Running;
        }
        catch (Exception ex)
        {
            Exception inner = Reflected.Unwrap(ex);
            p.Stop(PluginState.Failed, inner is MissingMethodException && inner.Message.Contains(AcEntry.MethodName) ? "Entry point not found: " + inner.Message : Explain(inner));
            log.LogError($"{p.Display} ({p.Guid}) failed to start: {inner}");
        }
        finally
        {
            starting = null;
            if (p.State == PluginState.Failed && before != null)
            {
                try
                {
                    int removed = RemovePatches(p, PatchesIn(asm).Where(patch => !before.Contains(patch)));
                    StopInstance(p);
                    p.Reason += $" Keel removed {removed} new Harmony patch(es){(p.Kind == PluginKind.BepInEx ? " and stopped the plugin" : "")}. Restart the game to clear any other setup left by the mod.";
                }
                catch (Exception ex)
                {
                    p.Warnings.Add($"Startup cleanup failed: {ex.Message}. Restart the game before playing.");
                    log.LogWarning($"{p.Display}: {p.Warnings.Last()}");
                }
            }
            p.StartMs += (float)watch.Elapsed.TotalMilliseconds;
        }
    }

    private static string Explain(Exception ex) => ex switch
    {
        MissingMethodException or MissingFieldException or TypeLoadException or FileNotFoundException =>
            $"Made for another game version or missing a library: {ex.Message}",
        ReflectionTypeLoadException r => "Could not load its types: " + (r.LoaderExceptions.FirstOrDefault()?.Message ?? r.Message),
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };

    // Unity logs exceptions from Awake/Start/Update, and mods can log caught exceptions as errors.
    private bool inLog;

    private void OnLog(string message, string stackTrace, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error || inLog)
            return;
        inLog = true;
        try
        {
            Attribute(message, stackTrace, type);
        }
        finally
        {
            inLog = false;
        }
    }

    private void Attribute(string message, string stackTrace, LogType type)
    {
        if (starting != null)
        {
            string summary = FirstLine(message);
            starting.FirstStartupError ??= summary;
            if (type == LogType.Exception || ReportsPatchFailure(message))
                startupException ??= summary;
            ModError?.Invoke(starting, message, stackTrace);
            return;
        }
        if (type != LogType.Exception || string.IsNullOrEmpty(stackTrace))
            return;
        PluginEntry direct = Find(stackTrace);
        PluginEntry plugin = GuardMissingMember(message, stackTrace, direct) ?? direct;
        if (plugin == null)
            return;
        plugin.LaterErrors++;
        plugin.FirstLaterError ??= FirstLine(message);
        if (plugin.State == PluginState.Running)
            plugin.State = PluginState.RunningWithErrors;
        ModError?.Invoke(plugin, message, stackTrace);
    }

    private readonly HashSet<string> blamed = new();

    /// <summary>Remove patches that use missing game methods, fields or types; off = only name the mod.</summary>
    public static bool GuardPatches = true;

    // Better TacMap reports a failed installation this way. Other mods log caught exceptions after a successful fallback.
    internal static bool ReportsPatchFailure(string message) =>
        message?.IndexOf("Harmony patching failed: HarmonyLib.HarmonyException:", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// The missing type or <c>Namespace.Type::Member</c> named by a compatibility error, in the form used by
    /// <see cref="PluginEntry.MissingUses"/>. Null for an error without a recognized missing reference.
    /// </summary>
    internal static string MissingKey(string message)
    {
        if (message?.StartsWith("TypeLoadException", StringComparison.Ordinal) == true)
        {
            var match = Regex.Match(message, @"(?:expected (?:class|valuetype)|Could not load type) '([^']+)' (?:in|from) assembly '");
            return match.Success ? match.Groups[1].Value.Replace('+', '/') : null;
        }
        if (message == null || !(message.StartsWith("MissingMethodException") || message.StartsWith("MissingFieldException")))
            return null;
        string sig = NotFound(message);          // e.g. List`1<string> SeaPower.FileManager.GetDirectories(string)
        if (sig == null)
            return null;
        int paren = sig.IndexOf('(');
        string head = paren < 0 ? sig : sig.Substring(0, paren);
        int space = head.LastIndexOf(' ');
        if (space >= 0)
            head = head.Substring(space + 1);
        int dot = head.LastIndexOf('.');
        if (dot <= 0)
            return null;
        return head.Substring(0, dot).Replace('+', '/') + "::" + head.Substring(dot + 1);
    }

    private const string NotFoundMark = " not found: ";

    // What a "... not found: X" message names, without Mono's " Due to: Could not find field in class" (on the same line in the
    // Unity log); null for another message.
    private static string NotFound(string message)
    {
        int at = message.IndexOf(NotFoundMark, StringComparison.Ordinal);
        if (at < 0)
            return null;
        string what = FirstLine(message.Substring(at + NotFoundMark.Length));
        int due = what.IndexOf(" Due to:", StringComparison.Ordinal);
        return due > 0 ? what.Substring(0, due) : what;
    }

    private static IEnumerable<HarmonyLib.Patch> AllPatches(HarmonyLib.Patches info) =>
        info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers);

    // A DMD frame names a patched method but does not identify its overload.
    internal static (string type, string method)? InnermostPatchedMethod(string stackTrace)
    {
        var frame = Regex.Match(stackTrace ?? "", @"DMD<([^>\s]+?)::([^>\s]+?)>");
        return frame.Success ? (frame.Groups[1].Value, frame.Groups[2].Value) : null;
    }

    /// <summary>Match a missing game reference against preflight findings and the failing patch. Report ambiguous matches.</summary>
    private PluginEntry GuardMissingMember(string message, string stackTrace, PluginEntry direct)
    {
        string key = MissingKey(message);
        if (key == null)
            return null;
        List<PluginEntry> candidates = owners.Select(o => o.plugin)
            .Where(p => (direct == null || p == direct) && p.MissingUses.Any(u => u == key || u.StartsWith(key + "(", StringComparison.Ordinal))).ToList();
        if (candidates.Count == 0)
            return null;
        string what = NotFound(message) ?? key;
        if (GuardPatches && TakeOffPatches(candidates, stackTrace) is { } recovery)
        {
            var (plugin, method, count) = recovery;
            if (blamed.Add(plugin.Guid + "|" + key + "|guard"))
                Warn(plugin, $"This game build is missing '{what}'. Keel removed {count} patch(es) installed by {plugin.Display} on {method}. The rest of the mod keeps running.");
            return plugin;
        }
        if (candidates.Count == 1)
        {
            PluginEntry p = candidates[0];
            if (blamed.Add(p.Guid + "|" + key))
                Warn(p, $"The game could not find '{what}'. {p.Display} uses it and this game build does not have it: the mod was made for another game version (see its warnings in the report).");
            return p;
        }
        if (blamed.Add("many|" + key))
        {
            string why = $"The game could not find '{what}'. These mods use it and this game build does not have it: {string.Join(", ", candidates.Select(c => c.Display))}. " +
                         "One of them was made for another game version (Keel could not tell which one from the stack; switching one off at a time finds it).";
            foreach (PluginEntry c in candidates)
                c.Warnings.Add(why);
            log.LogWarning(why);
        }
        return null;
    }

    private void Warn(PluginEntry p, string why)
    {
        p.Warnings.Add(why);
        log.LogWarning(why);
    }

    private (PluginEntry plugin, string method, int count)? TakeOffPatches(List<PluginEntry> candidates, string stackTrace)
    {
        if (InnermostPatchedMethod(stackTrace) is not { } frame)
            return null;
        try
        {
            var type = HarmonyLib.AccessTools.TypeByName(frame.type);
            if (type == null) return null;
            var targets = HarmonyLib.AccessTools.GetDeclaredMethods(type).Where(m => m.Name == frame.method)
                .Select(method => (method, info: HarmonyLib.Harmony.GetPatchInfo(method)))
                .Where(target => target.info != null).ToList();
            if (targets.Count != 1) return null;

            var (target, info) = targets[0];
            var pluginsByAssembly = ownerRegistered.ToLookup(p => p.AssemblyName);
            var patches = new List<(MethodInfo method, PluginEntry owner)>();
            foreach (var patch in AllPatches(info))
            {
                var method = patch.PatchMethod;
                var plugins = pluginsByAssembly[method?.DeclaringType?.Assembly.GetName().Name];
                var owner = PatchOwner(plugins, patch.owner);
                if (owner == null && plugins.Any(candidates.Contains)) return null;
                patches.Add((method, owner));
            }
            var implicated = patches.Select(p => p.owner).Where(candidates.Contains).Distinct().ToList();
            if (implicated.Count != 1) return null;

            var plugin = implicated[0];
            // Harmony removes every registration of a patch method. Preserve methods shared with another owner.
            var removable = patches.GroupBy(p => p.method).Where(group => group.All(p => p.owner == plugin))
                .Select(group => ((MethodBase)target, group.Key));
            int count = RemovePatches(plugin, removable);
            return count > 0 ? (plugin, $"{frame.type}.{frame.method}", count) : null;
        }
        catch (Exception ex)
        {
            log.LogWarning($"Keel could not take a failing patch off: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    internal static PluginEntry PatchOwner(IEnumerable<PluginEntry> plugins, string harmonyId) =>
        plugins.Count() == 1 ? plugins.First() : plugins.FirstOrDefault(p => p.Guid == harmonyId);

    // A message's first line, cut for the report.
    private const int LineLength = 240;

    private static string FirstLine(string s)
    {
        int nl = s.IndexOf('\n');
        string line = nl < 0 ? s : s.Substring(0, nl);
        return line.Length > LineLength ? line.Substring(0, LineLength) + "…" : line.Trim();
    }
    // Namespaces a mod may put types into that are not its own: matching them would blame it for the game's errors.
    internal static readonly HashSet<string> SharedNamespaces = new()
    {
        "SeaPower", "SeapowerUI", "Seapower", "UnityEngine", "Unity", "System", "BepInEx", "HarmonyLib", "MonoMod", "Microsoft", "AnchorChain",
    };

    // "MyMod." for every top-level namespace of the mod (types without one count by their own name).
    private static string[] Namespaces(Assembly asm)
    {
        Type[] types;
        try
        {
            types = asm.GetTypes();
        }
        catch (ReflectionTypeLoadException r)
        {
            types = r.Types.Where(t => t != null).ToArray();
        }
        return types.Select(t => string.IsNullOrEmpty(t.Namespace) ? t.Name : t.Namespace.Split('.')[0])
                    .Where(n => n.Length > 2 && !n.StartsWith("<") && !SharedNamespaces.Contains(n))
                    .Distinct().Select(n => n + ".").ToArray();
    }
}
