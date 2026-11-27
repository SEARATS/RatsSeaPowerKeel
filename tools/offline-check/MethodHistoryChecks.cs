using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SeaPowerKeel;

internal static class MethodHistoryChecks
{
    private const string Target = "System.Void SeaPowerKeelTest.HistoryFixture::Patched()";
    private const string Lookup = "System.Void SeaPowerKeelTest.HistoryFixture::LookedUp()";
    private const string Namespace = "SeaPowerKeelTest";
    private const string TypeName = Namespace + ".HistoryFixture";

    public static void Run(string temp, string core, string mod, Action<bool, string> check)
    {
        const string old = "0.9.3-261116", next = "0.9.4-fixture";
        byte[] baselineImage = Game();
        MethodSnapshot baseline = MethodSnapshot.Scan(old, baselineImage);
        var result = new Preflight.Result();
        result.PatchTargets.Add(Target);
        result.LookupTargets.Add(Lookup);

        string[] Compare(byte[] image) => HistoryPreflight.Changes(result, MethodSnapshot.Scan(next, image), [baseline]);
        check(Compare(Game(reverseOrder: true)).Length == 0, "metadata token and method-order changes do not alter method history");
        check(baseline.Methods.Count == 7 && baseline.Methods[Target].Calls.Length == 1,
            "snapshot includes each method and its direct edges");
        check(Compare(Game(target: 2)).Single().Contains("code or metadata"), "a changed patch body names its target");
        check(Compare(Game(callee: 2)).Single().Contains("downstream"), "a changed transitive callee is found through a recursive pair");
        check(Compare(Game(caller: 2)).Single().Contains("upstream"), "a changed transitive caller is attributed to the caller graph");
        check(Compare(Game(unrelated: 2)).Single().StartsWith("AccessTools lookup"), "an unrelated change is attributed only to its lookup");
        check(HistoryPreflight.Changes(result, MethodSnapshot.Scan(next, Game(callee: 2)),
            [baseline, MethodSnapshot.Scan(old, Game(callee: 2))]).Length == 0,
            "one matching build variant prevents an ambiguous change warning");

        string managed = Path.Combine(temp, "history-managed");
        string history = Path.Combine(temp, "history");
        string cache = Path.Combine(temp, "history-cache");
        Directory.CreateDirectory(managed);
        string baselinePath = Path.Combine(history, "0.9.3", "fixture" + MethodSnapshot.Extension);
        baseline.Write(baselinePath);
        string gamePath = Path.Combine(managed, MethodSnapshot.AssemblyFile);
        string modCopy = Path.Combine(temp, "history-mod.dll");
        File.Copy(mod, modCopy);
        File.WriteAllBytes(gamePath, Game(callee: 2));
        List<string> logs = [];
        HistoryPreflight Analyzer() => new(managed, core, history, cache, next, logs.Add);
        var analyzer = Analyzer();
        string[] baselines = analyzer.Baselines(["[Compatibility]", "ExactVersion=0.9.3"]);
        check(baselines.SequenceEqual([baselinePath]) &&
            analyzer.Baselines(["[Compatibility]", "ExactVersion=0.9.2"]).Length == 0,
            "history uses a published build covered by the mod's compatibility declaration");
        using (var preflight = new Preflight(managed, core))
        {
            HistoryPreflight.Review first = analyzer.Check(preflight, modCopy, baselines);
            check(first.Result.PatchTargets.Contains(Target) && first.Result.LookupTargets.Contains(Lookup),
                "the regular preflight identifies a Harmony attribute and an AccessTools method lookup");
            check(first.Warnings.Any(w => w.Contains(Target) && w.Contains("downstream")),
                "the cold preflight reports the changed target's callees");
        }
        HistoryPreflight.Review cached = Analyzer().Check(null, modCopy, baselines);
        check(cached.Warnings.Any(w => w.Contains("downstream")) && logs.Last().Contains("cache hit"),
            "a new analyzer returns the result without reopening the mod in Cecil");

        File.WriteAllBytes(gamePath, Game(caller: 2));
        using (var preflight = new Preflight(managed, core))
        {
            HistoryPreflight.Review changed = Analyzer().Check(preflight, modCopy, baselines);
            check(changed.Warnings.Any(w => w.Contains("upstream")) && !logs.Last().Contains("cache hit"),
                "different game DLL bytes invalidate the scan even with the same version string");
        }
        using (var stream = new FileStream(modCopy, FileMode.Append))
            stream.WriteByte(0);
        using (var preflight = new Preflight(managed, core))
        {
            HistoryPreflight.Review changed = Analyzer().Check(preflight, modCopy, baselines);
            check(changed.Warnings.Any(w => w.Contains("upstream")) && !logs.Last().Contains("cache hit"),
                "different mod DLL bytes invalidate the scan at the same path");
        }
    }

    private static byte[] Game(int target = 1, int caller = 1, int callee = 1, int unrelated = 1, bool reverseOrder = false)
    {
        using ModuleDefinition game = ModuleDefinition.CreateModule(Path.GetFileNameWithoutExtension(MethodSnapshot.AssemblyFile), ModuleKind.Dll);
        var type = new TypeDefinition(Namespace, "HistoryFixture", TypeAttributes.Public, game.TypeSystem.Object);
        game.Types.Add(type);
        string[] names = ["Top", "Parent", "Patched", "Helper", "Leaf", "LookedUp", "Independent"];
        foreach (string name in reverseOrder ? names.AsEnumerable().Reverse() : names)
            type.Methods.Add(new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, game.TypeSystem.Void));

        void Write(string name, int constant, params string[] calls)
        {
            ILProcessor il = type.Methods.Single(m => m.Name == name).Body.GetILProcessor();
            il.Emit(OpCodes.Ldc_I4, constant);
            il.Emit(OpCodes.Pop);
            foreach (string call in calls)
                il.Emit(OpCodes.Call, type.Methods.Single(m => m.Name == call));
            il.Emit(OpCodes.Ret);
        }
        Write("Top", caller, "Parent");
        Write("Parent", 1, "Patched");
        Write("Patched", target, "Helper");
        Write("Helper", 1, "Leaf");
        Write("Leaf", callee, "Helper");
        Write("LookedUp", unrelated);
        Write("Independent", 1);
        using var stream = new MemoryStream();
        game.Write(stream);
        return stream.ToArray();
    }

    [HarmonyPatch(TypeName, "Patched")]
    private class FixturePatch
    {
        private static void Prefix() { }

        private static void LookupByName() => AccessTools.Method(TypeName + ":LookedUp");
    }
}
