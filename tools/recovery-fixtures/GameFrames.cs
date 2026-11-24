using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using HarmonyLib;
using KeelRecovery;
using UnityEngine;

namespace KeelGameFrames
{
    [BepInPlugin("keel.fixture.game-frames", "Recovery game frames", "1.0.0")]
    public class GameFrames : BaseUnityPlugin
    {
        private float verifyAt;
        private static MethodBase Target => AccessTools.Method(AccessTools.TypeByName("SeaPower.GameMain"), "LateUpdate");
        private static bool Ready() => TestProbe.Ready;

        private static IEnumerable<CodeInstruction> Transpiler() =>
            new[]
            {
                new CodeInstruction(OpCodes.Ldstr, "Could not resolve type with token 010000b7 from typeref (expected class 'SeaPower.KeelRecoveryGone' in assembly 'Seapower-Scripts, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null')"),
                new CodeInstruction(OpCodes.Newobj, AccessTools.Constructor(typeof(TypeLoadException), new[] { typeof(string) })),
                new CodeInstruction(OpCodes.Throw)
            };

        private void Update()
        {
            if (!Ready()) return;
            if (verifyAt == 0)
            {
                new Harmony("keel.fixture.game-frames").Patch(Target,
                    transpiler: new HarmonyMethod(typeof(GameFrames), nameof(Transpiler)));
                verifyAt = Time.unscaledTime + 3;
                return;
            }
            if (Time.unscaledTime < verifyAt) return;
            var info = Harmony.GetPatchInfo(Target);
            bool removed = info == null || info.Transpilers.All(p => p.owner != "keel.fixture.game-frames");
            bool attributed = TestProbe.Report.Contains("[ERRORS] Recovery game frames");
            if (!attributed) return; // GameMain.LateUpdate first runs after a mission loads.
            enabled = false;
            Debug.Log($"KEEL_GAME_FRAMES {(removed && attributed ? "PASS" : "FAIL")} removed={removed} attributed={attributed}");
        }
    }
}

namespace KeelRecovery
{
    internal static class ReferenceStub
    {
        internal static void Gone() { }
    }

    internal static class FailureCode
    {
        private static Type MissingMetadata()
        {
            ReferenceStub.Gone();
            return typeof(ReferenceStub);
        }
    }
}
