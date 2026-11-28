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

// Preparation DLLs and the compatibility checks made on a worker during the splash (PR #16, reworked).
internal static partial class SelfTest
{
    private static void PreparationChecks(string tmp, string managed, string core)
    {
        Console.WriteLine("Preparation DLLs: opt-in by [Keel] Prepare=, run on a worker, held after a crash inside one");
        Check(EarlyStart.ValueInInfoIni(["[Language_en]", "Prepare=No.dll", "[Keel]", "Prepare = My.Prepare.dll ; note"], Preparation.InfoKey) == "My.Prepare.dll"
              && EarlyStart.ValueInInfoIni(["[Keel]", "StartEarly=true"], Preparation.InfoKey) == null,
              "prepare: the manifest parser reads Prepare= in [Keel] and trims comments");
        Check(PreparationEntry.Forbidden("UnityEngine.CoreModule") && PreparationEntry.Forbidden("Assembly-CSharp") && PreparationEntry.Forbidden("0Harmony")
              && PreparationEntry.Forbidden("SeaPowerKeel") && !PreparationEntry.Forbidden("System.Runtime") && !PreparationEntry.Forbidden("Newtonsoft.Json"),
              "prepare: game, Unity, loaders and Harmony are refused as references; the runtime and plain libraries are not");

        string mod = Path.Combine(tmp, "prep-mod");
        Directory.CreateDirectory(Path.Combine(mod, "sub"));
        string dll = Path.Combine(mod, "Prep.Prepare.dll");
        WritePrepareDll(dll, "PrepFixture");
        File.WriteAllText(Path.Combine(mod, "_info.ini"), "[Keel]\nPrepare=Prep.Prepare.dll\n");
        var notes = new List<string>();
        Check(Preparation.Requested(new ModFolder { Path = mod, Name = "prep-mod" }, notes) == Path.GetFullPath(dll) && notes.Count == 0, "prepare: a DLL at the top of the folder is taken");
        File.WriteAllText(Path.Combine(mod, "_info.ini"), "[Keel]\nPrepare=sub\\Prep.Prepare.dll\n");
        Check(Preparation.Requested(new ModFolder { Path = mod, Name = "prep-mod" }, notes) == null && notes.Count == 1, "prepare: a path into a subfolder is refused, with a note");
        File.WriteAllText(Path.Combine(mod, "_info.ini"), "[Keel]\nPrepare=Prep.Prepare.dll\n");

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
        File.WriteAllText(Path.Combine(root, KeelNames.PrepareFile), $"running\t{Path.GetFullPath(dll)}\t{CrashGuard.Stamp(dll)}\n");
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

    private class CurrentBoot
    {
        public static Func<string, string[]> Find;
        public static Func<string, Action<string, string[], Action<string>>> Open;

        public static void BootWithPreloaders(string version, string path, Func<string, string[]> find,
            Func<string, Action<string, string[], Action<string>>> open)
        {
            Find = find;
            Open = open;
        }

        public static void Boot(string version, string path) => throw new InvalidOperationException("Expected preloader callbacks.");
    }

    private class LegacyBoot
    {
        public static string Path;

        public static void Boot(string version, string path) => Path = path;
    }

    private static void StubPreloaderChecks(string tmp, string root)
    {
        Console.WriteLine("Stub preloader discovery and boot compatibility");
        Check(StartupPreloaders.Boot(typeof(LegacyBoot), "1.1.0", "legacy.dll") && LegacyBoot.Path == "legacy.dll",
              "preloader: the new stub still calls Boot on an older Keel");
        Check(StartupPreloaders.Boot(typeof(CurrentBoot), "1.1.0", "keel.dll")
              && CurrentBoot.Find != null && CurrentBoot.Open?.Method.DeclaringType?.Assembly == typeof(StartupPreloaders).Assembly,
              "preloader: the new Keel boot receives the stub's discovery and execution callbacks");
        Check(typeof(Keel).GetMethod("Boot")?.GetParameters().Length == 2,
              "preloader: older stubs still have the original unambiguous boot entry");

        string folder = Path.Combine(tmp, "preloaders"), nested = Path.Combine(folder, "code", "helpers");
        Directory.CreateDirectory(nested);
        string direct = Path.Combine(folder, "DirectPreloader.dll"), deep = Path.Combine(nested, "Nested.PreLoader.DLL");
        string legacy = Path.Combine(folder, KeelNames.AnchorChainPreloaderFile), similar = Path.Combine(folder, "Extra.Preloader.dll.bak");
        WritePrepareDll(direct, "DirectPreloaderFixture");
        WritePrepareDll(deep, "NestedPreloaderFixture");
        File.WriteAllText(legacy, "reserved loader");
        File.WriteAllText(similar, "backup");
        File.WriteAllText(Path.Combine(folder, "_info.ini"), "[Keel]\nPrepare=DirectPreloader.dll\n");
        Check(CurrentBoot.Find(folder).OrderBy(x => x).SequenceEqual(new[] { direct, deep }.OrderBy(x => x)),
              "preloader: suffix discovery is recursive, case-insensitive and leaves out Anchor Chain and backup files");

        string disabled = Path.Combine(tmp, "preloaders-off");
        Directory.CreateDirectory(disabled);
        WritePrepareDll(Path.Combine(disabled, "OffPreloader.dll"), "DisabledPreloaderFixture");
        string outdated = Path.Combine(tmp, "preloaders-outdated");
        Directory.CreateDirectory(outdated);
        WritePrepareDll(Path.Combine(outdated, "OldPreloader.dll"), "OutdatedPreloaderFixture");
        File.WriteAllText(Path.Combine(outdated, "_info.ini"), "[Compatibility]\nExactVersion=0.9.2\n");
        ModFolder[] folders = [Folder(folder), Folder(disabled), Folder(outdated)];
        folders[0].Enabled = true;
        folders[1].Enabled = false;
        folders[2].Enabled = true;
        var ran = new List<string>();
        var warned = new List<string>();

        Action<string, string[], Action<string>> Open(string path)
        {
            ran.Add(path);
            return CurrentBoot.Open(path);
        }
        int reports = 0;
        Preparation.Start(folders, root, tmp, "0.9.3", true, message => { if (message != null) reports++; },
            _ => { }, warned.Add, CurrentBoot.Find, Open);
        Check(Preparation.For(folder).Wait(10000) && ran.SequenceEqual(CurrentBoot.Find(folder))
              && reports == 2 && warned.Count == 0 && Preparation.For(disabled).IsCompleted && Preparation.For(outdated).IsCompleted,
              "preloader: both helpers run once through the stub, even with a matching Prepare entry, and other folders stay out");

        string state = Path.Combine(root, KeelNames.PrepareFile);
        File.WriteAllText(state, $"running\t{deep}\t{CrashGuard.Stamp(deep)}\n");
        ran.Clear();
        warned.Clear();
        Preparation.Start(folders, root, tmp, "0.9.3", true, _ => { }, _ => { }, warned.Add, CurrentBoot.Find, Open);
        Check(Preparation.For(folder).Wait(10000) && ran.SequenceEqual([direct]) && warned.Count == 1
              && warned[0].Contains("Nested.PreLoader.DLL") && File.ReadAllText(state).StartsWith("held\t"),
              "preloader: the crash holdout applies to an automatically discovered helper");
    }

    // A preparation DLL written with Cecil: Prepare(streamingAssets, folders, report) reports "prepared <first folder's name>".
    private static void WritePrepareDll(string path, string name)
    {
        using var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, new Version(1, 0)), name, ModuleKind.Dll);
        ModuleDefinition m = asm.MainModule;
        var type = new TypeDefinition("Fixture", "Prep", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, m.TypeSystem.Object);
        m.Types.Add(type);
        var prepare = new MethodDefinition("Prepare", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, m.TypeSystem.Void);
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
