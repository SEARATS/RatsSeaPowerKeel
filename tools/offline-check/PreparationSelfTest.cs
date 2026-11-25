using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SeaPowerKeel;

// Preparation DLLs and the compatibility checks made on a worker during the splash (PR #16, reworked).
internal static partial class SelfTest
{
    private static void PreparationChecks(string tmp, string managed, string core)
    {
        Console.WriteLine("Preparation DLLs: opt-in by [Keel] Prepare=, run on a worker, held after a crash inside one");
        Check(EarlyStart.ValueInInfoIni("[Language_en]\nPrepare=No.dll\n[Keel]\nPrepare = My.Prepare.dll ; note\n", Preparation.InfoKey) == "My.Prepare.dll"
              && EarlyStart.ValueInInfoIni("[Keel]\nStartEarly=true\n", Preparation.InfoKey) == null,
              "prepare: only [Keel] Prepare= counts, value trimmed, comments cut");
        Check(Preparation.Forbidden("UnityEngine.CoreModule") && Preparation.Forbidden("Assembly-CSharp") && Preparation.Forbidden("0Harmony")
              && Preparation.Forbidden("SeaPowerKeel") && !Preparation.Forbidden("System.Runtime") && !Preparation.Forbidden("Newtonsoft.Json"),
              "prepare: game, Unity, loaders and Harmony are refused as references; the runtime and plain libraries are not");

        string mod = Path.Combine(tmp, "prep-mod");
        Directory.CreateDirectory(Path.Combine(mod, "sub"));
        string dll = Path.Combine(mod, "Prep.Prepare.dll");
        WritePrepareDll(dll, "PrepFixture");
        File.WriteAllText(Path.Combine(mod, "_info.ini"), "[Keel]\nPrepare=Prep.Prepare.dll\n");
        var notes = new List<string>();
        Check(Preparation.Requested(mod, notes) == Path.GetFullPath(dll) && notes.Count == 0, "prepare: a DLL at the top of the folder is taken");
        File.WriteAllText(Path.Combine(mod, "_info.ini"), "[Keel]\nPrepare=sub\\Prep.Prepare.dll\n");
        Check(Preparation.Requested(mod, notes) == null && notes.Count == 1, "prepare: a path into a subfolder is refused, with a note");
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

        Console.WriteLine("Compatibility checks made on a worker during the splash");
        string checkDir = Path.Combine(tmp, "prep-check");
        Directory.CreateDirectory(checkDir);
        string target = Path.Combine(checkDir, "Mod.dll");
        File.Copy(typeof(SelfTest).Assembly.Location, target);
        var prepared = Preflight.Prepared.Start(managed, core, new[] { target }).Result;
        Check(prepared.For(target) != null, "prepared check: the worker's result is used while the file is the same");
        File.AppendAllText(target, " ");
        Check(prepared.For(target) == null, "prepared check: a DLL that changed since is checked again on the main thread");
        Check(prepared.For(Path.Combine(checkDir, "Other.dll")) == null, "prepared check: a DLL the worker did not see is checked on the main thread");
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
