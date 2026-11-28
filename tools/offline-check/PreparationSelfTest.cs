extern alias KeelStub;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SeaPowerKeel;
using StartupPreloaders = KeelStub::SeaPowerKeel.Stub.StartupPreloaders;
using StubFolder = KeelStub::SeaPowerKeel.ModFolder;

// Preparation DLLs and the compatibility checks made on a worker during the splash (PR #16, reworked).
internal static partial class SelfTest
{
    private static void PreparationChecks(string tmp, string managed, string core)
    {
        Console.WriteLine("Preparation DLLs: opt-in by [Keel] Prepare=, run on a worker, held after a crash inside one");
        Check(StartupManifest.Read(["[Language_en]", "Prepare=No.dll", "[Keel]", "Prepare = My.Prepare.dll ; note"]).Prepare == "My.Prepare.dll"
              && StartupManifest.Read(["[Keel]", "StartEarly=true"]).Prepare == null,
              "prepare: the manifest parser reads Prepare= in [Keel] and trims comments");
        Check(PreparationEntry.Forbidden("UnityEngine.CoreModule") && PreparationEntry.Forbidden("Assembly-CSharp") && PreparationEntry.Forbidden("0Harmony")
              && PreparationEntry.Forbidden("SeaPowerKeel") && !PreparationEntry.Forbidden("System.Runtime") && !PreparationEntry.Forbidden("Newtonsoft.Json"),
              "prepare: game, Unity, loaders and Harmony are refused as references; the runtime and plain libraries are not");

        string mod = Path.Combine(tmp, "prep-mod");
        Directory.CreateDirectory(Path.Combine(mod, "sub"));
        string dll = Path.Combine(mod, "Prep.Prepare.dll");
        WritePrepareDll(dll, "PrepFixture");
        File.WriteAllText(Path.Combine(mod, "_info.ini"), "[Keel]\nPrepare=Prep.Prepare.dll\n");
        Check(Preparation.ResolveHelper(mod, "Prep.Prepare.dll") == Path.GetFullPath(dll),
              "prepare: a DLL at the top of the folder is taken");
        Check(Preparation.ResolveHelper(mod, "sub/Prep.Prepare.dll") == null,
              "prepare: a path into a subfolder is refused");

        string root = Path.Combine(tmp, "prep-bepinex");
        Directory.CreateDirectory(root);
        var folders = new List<ModFolder> { new() { Path = mod, Name = "prep-mod", Enabled = true } };
        string seen = null;
        var warned = new List<string>();
        List<string> said = Preparation.Start(folders, root, tmp, "0.9.3", true, t => seen ??= t, _ => { }, warned.Add);
        bool finished = Preparation.For(mod).Wait(10000);
        Check(finished && said.Count == 0 && warned.Count == 0 && seen == "prepared prep-mod",
              "prepare: the DLL's Prepare ran on a worker, got the mod folders and reported its line");
        Check(!File.Exists(Path.Combine(root, KeelNames.PrepareFile)), "prepare: nothing left in Keel-prepare.txt after a clean run");

        // The game died during the run: the line is still "running" at the next start.
        File.WriteAllText(Path.Combine(root, KeelNames.PrepareFile), $"running\t{Path.GetFullPath(dll)}\t{StartupJournal.Stamp(dll)}\n");
        seen = null;
        said = Preparation.Start(folders, root, tmp, "0.9.3", true, t => seen ??= t, _ => { }, warned.Add);
        Check(said.Count == 2 && Preparation.For(mod).IsCompleted && seen == null
              && File.ReadAllText(Path.Combine(root, KeelNames.PrepareFile)).StartsWith("held\t"),
              "prepare: a DLL that was running when the game died is held and named, the mod is told it is done");
        said = Preparation.Start(folders, root, tmp, "0.9.3", false, t => seen ??= t, _ => { }, warned.Add);
        Preparation.For(mod).Wait(10000);
        Check(seen == "prepared prep-mod", "prepare: with HoldOutCrashers off it runs anyway");
        // Held from an older file (the mod was updated since): the stamp no longer matches. (The DLL itself is loaded by now and
        // cannot be rewritten in this process.)
        File.WriteAllText(Path.Combine(root, KeelNames.PrepareFile), $"held\t{Path.GetFullPath(dll)}\t0:0\n");
        seen = null;
        said = Preparation.Start(folders, root, tmp, "0.9.3", true, t => seen ??= t, _ => { }, warned.Add);
        Preparation.For(mod).Wait(10000);
        Check(said.Count == 0 && seen == "prepared prep-mod", "prepare: an updated DLL gets another chance");
        Check(Preparation.For(Path.Combine(tmp, "no-such-mod")).IsCompleted, "prepare: a folder without one is done at once");

        string next = Path.Combine(tmp, "prep-next");
        string disabled = Path.Combine(tmp, "prep-disabled");
        foreach (string folder in new[] { next, disabled })
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "_info.ini"), "[Keel]\nPrepare=Prep.dll\n");
            WritePrepareDll(Path.Combine(folder, "Prep.dll"), Path.GetFileName(folder));
        }
        folders.Add(new ModFolder { Path = next, Name = "prep-next", Enabled = true });
        folders.Add(new ModFolder { Path = disabled, Name = "prep-disabled", Enabled = false });
        File.WriteAllText(Path.Combine(mod, "_info.ini"), "[Keel]\nPrepare=Broken.dll\n");
        File.WriteAllText(Path.Combine(mod, "Broken.dll"), "not a managed assembly");
        folders[0] = new ModFolder { Path = mod, Name = "prep-mod", Enabled = true };
        warned.Clear();
        var steps = new List<string>();
        int caller = Environment.CurrentManagedThreadId, worker = caller;
        Preparation.Start(folders, root, tmp, "0.9.3", true, _ => worker = Environment.CurrentManagedThreadId, steps.Add, warned.Add);
        Check(Preparation.For(next).Wait(10000) && worker != caller && Preparation.For(disabled).IsCompleted
              && warned.Count == 1 && warned[0].Contains("Broken.dll") && steps.Count == 2,
              "prepare: a broken helper completes, the next enabled helper runs off-thread and the disabled helper stays out");

        StubPreloaderChecks(tmp, root);

        Console.WriteLine("Compatibility checks made on a worker during the splash");
        string checkDir = Path.Combine(tmp, "prep-check");
        Directory.CreateDirectory(checkDir);
        string current = Path.Combine(checkDir, "Mod.dll"), changed = Path.Combine(checkDir, "Changed.dll");
        string outdated = Path.Combine(checkDir, "Outdated.dll");
        foreach (string file in new[] { current, changed, outdated })
            File.Copy(typeof(SelfTest).Assembly.Location, file);
        var prepared = new PreparedPreflight(managed, core, "0.9.3",
            [(current, []), (changed, []), (outdated, ["[Compatibility]", "ExactVersion=0.9.2"])]);
        Check(prepared.Completion.Wait(10000) && prepared.Take(current) != null,
              "prepared check: a completed result is used for identical DLL bytes");
        File.AppendAllText(changed, " ");
        Check(prepared.Take(changed) == null, "prepared check: an updated DLL falls back to the regular check");
        Check(prepared.Take(outdated) == null, "prepared check: declared mismatches go through the method-history path");
        Check(prepared.Take(Path.Combine(checkDir, "Other.dll")) == null, "prepared check: a DLL absent from the disk scan falls back");
    }

    // Run separately from the stand-in tests so the release assemblies resolve to their real interfaces.
    public static int CheckReleasedPreloaders(string managed, string previousStub, string anchorPreloader, string anchorLoader)
    {
        Program.ResolveFrom(managed);
        using var previous = AssemblyDefinition.ReadAssembly(previousStub);
        MethodDefinition awake = previous.MainModule.GetType("SeaPowerKeel.Stub.Stub").Methods.Single(m => m.Name == "Awake");
        string[] names = awake.Body.Instructions.Where(i => i.OpCode.Code == Code.Ldstr).Select(i => (string)i.Operand).ToArray();
        MethodInfo boot = typeof(Keel).GetMethod("Boot", BindingFlags.Public | BindingFlags.Static);
        Check(boot != null && names.Contains(typeof(Keel).FullName) && names.Contains(boot.Name) && boot.ReturnType == typeof(void)
              && boot.GetParameters().Select(p => p.ParameterType).SequenceEqual([typeof(string), typeof(string)]),
              $"preloader: released Keel stub {previous.Name.Version.ToString(3)} resolves the legacy boot signature");

        Assembly preloader = Assembly.LoadFrom(Path.GetFullPath(anchorPreloader));
        Assembly loader = Assembly.LoadFrom(Path.GetFullPath(anchorLoader));
        Type loaderType = loader.GetType(AcLoaderShim.LoaderType, throwOnError: true);
        Type contract = preloader.GetType("AnchorChain.Preloader.IPluginLoader", throwOnError: true);
        Check(contract.IsAssignableFrom(loaderType) && contract.GetMethod("LoadPlugins")?.ReturnType == typeof(void),
              $"preloader: Anchor Chain {loader.GetName().Version.ToString(3)} binds to the current preloader's LoadPlugins interface");
        string reason = AcLoaderShim.Prime(loader, [], Path.GetTempPath());
        Check(reason == null, reason ?? "preloader: Keel can prime the released Anchor Chain loader for its mods");
        return failures == 0 ? 0 : 1;
    }

    private static void StubPreloaderChecks(string tmp, string root)
    {
        Console.WriteLine("Standalone BepInEx preloaders");
        Check(typeof(Keel).GetMethod("Boot")?.GetParameters().Length == 2,
              "preloader: Keel retains the original boot entry for older stubs");
        Check(typeof(StartupPreloaders).Assembly.GetReferencedAssemblies().All(name => name.Name != "SeaPowerKeel"),
              "preloader: the stub has no dependency on the optional Keel loader");

        StubFolder Add(string name, string setting, bool enabled = true)
        {
            string path = Path.Combine(tmp, name);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "_info.ini"), setting);
            return new StubFolder { Path = path, Name = name, Enabled = enabled };
        }

        const string first = "FirstPreloader.dll", second = "code/Second.PreLoader.DLL", bad = "BrokenPreloader.dll";
        StubFolder configured = Add("preloader-explicit", $"[Preloader]\nLoad={first}\nLoad2={bad}\nLoad3={second}\nLoad4={first}\n");
        StubFolder missingOptIn = Add("preloader-unlisted", "[Preloader]\n");
        StubFolder conflict = Add("preloader-conflict", $"[Preloader]\nLoad={first}\n[Keel]\nPrepare=Other.dll\n");
        StubFolder disabled = Add("preloader-disabled", $"[Preloader]\nLoad={first}\n", false);
        StubFolder oldGame = Add("preloader-old-game", $"[Preloader]\nLoad={first}\n[Compatibility]\nExactVersion=0.9.2\n");
        StubFolder oldOption = Add("preloader-keel-draft", $"[Keel]\nPreload={first}\n");

        foreach (StubFolder item in new[] { configured, missingOptIn, conflict, disabled, oldGame, oldOption })
            WritePrepareDll(Path.Combine(item.Path, first), item.Name, StartupHelper.Preload, gameReference: true);
        Directory.CreateDirectory(Path.Combine(configured.Path, "code"));
        string target = Path.GetFullPath(Path.Combine(configured.Path, second));
        WritePrepareDll(target, "second-preloader", StartupHelper.Preload);
        WritePrepareDll(Path.Combine(configured.Path, bad), "wrong-preloader-entry", StartupHelper.Prepare);
        WritePrepareDll(Path.Combine(conflict.Path, "Other.dll"), "conflicting-prepare");

        Check(StartupPreloaders.Resolve(configured.Path, second) == target
              && StartupPreloaders.Resolve(conflict.Path, "../preloader-unlisted/" + first) == null
              && StartupPreloaders.Resolve(conflict.Path, ".. /preloader-unlisted/" + first) == null
              && StartupPreloaders.Resolve(configured.Path, target) == null,
              "preloader: an explicitly named nested DLL is accepted and unsupported paths are refused");

        var messages = new List<string>();
        var warnings = new List<string>();
        StartupPreloaders.Run([configured, missingOptIn, conflict, disabled, oldGame, oldOption], root, tmp, "0.9.3",
            messages.Add, warnings.Add);
        string[] started = messages.Where(x => x.StartsWith("Starting preloader ", StringComparison.Ordinal)).ToArray();
        Check(started.Length == 2 && started[0].Contains(first) && started[1].Contains(second)
              && messages.Count(x => x == "prepared preloader-explicit") == 2,
              "preloader: the standalone stub starts multiple DLLs in manifest order, including a loader with a game reference");
        Check(warnings.Count == 3 && warnings.Any(x => x.Contains("Expected one public static void Preload"))
              && warnings.Any(x => x.Contains("Choose [Preloader]")) && warnings.Any(x => x.Contains("declared game version")),
              "preloader: invalid helpers, conflicting settings and mismatched versions cannot start a callback");

        List<string> notes = Preparation.Start([Folder(conflict.Path)], root, tmp, "0.9.3", true, _ => { }, _ => { }, warnings.Add);
        Check(notes.Any(x => x.Contains("Choose [Keel]")) && Keel.WhenPrepared(conflict.Path).IsCompleted,
              "prepare: Keel leaves a conflicting folder idle");

        string state = Path.Combine(root, KeelNames.PreloaderJournal);
        File.WriteAllText(state, $"running\t{target}\t{StartupJournal.Stamp(target)}\n");
        messages.Clear();
        warnings.Clear();
        StartupPreloaders.Run([configured], root, tmp, "0.9.3", messages.Add, warnings.Add);
        Check(messages.Count(x => x.StartsWith("Starting preloader ", StringComparison.Ordinal)) == 1
              && warnings.Any(x => x.Contains(KeelNames.PreloaderJournal)) && File.ReadAllText(state).StartsWith("held\t"),
              "preloader: the standalone stub holds a helper that crashed and continues with the others");
    }

    // A preparation DLL written with Cecil: Prepare(streamingAssets, folders, report) reports "prepared <first folder's name>".
    private static void WritePrepareDll(string path, string name, StartupHelper entry = StartupHelper.Prepare, bool gameReference = false)
    {
        using var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, new Version(1, 0)), name, ModuleKind.Dll);
        ModuleDefinition m = asm.MainModule;
        if (gameReference)
            m.AssemblyReferences.Add(new AssemblyNameReference("Seapower-Scripts", new Version(1, 0)));
        var type = new TypeDefinition("Fixture", "Prep", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, m.TypeSystem.Object);
        m.Types.Add(type);
        var prepare = new MethodDefinition(entry.ToString(), MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, m.TypeSystem.Void);
        prepare.Parameters.Add(new ParameterDefinition(m.TypeSystem.String));
        prepare.Parameters.Add(new ParameterDefinition(m.ImportReference(typeof(string[]))));
        prepare.Parameters.Add(new ParameterDefinition(m.ImportReference(typeof(Action<string>))));
        type.Methods.Add(prepare);
        ILProcessor il = prepare.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldstr, "prepared ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, m.ImportReference(typeof(Path).GetMethod(nameof(Path.GetFileName), new[] { typeof(string) })));
        il.Emit(OpCodes.Call, m.ImportReference(typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) })));
        il.Emit(OpCodes.Callvirt, m.ImportReference(typeof(Action<string>).GetMethod("Invoke")));
        il.Emit(OpCodes.Ret);
        asm.Write(path);
    }
}
