using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using HarmonyLib;
using SeaPowerKeel;
using UnityEngine;

namespace KeelRecovery;

[BepInPlugin("keel.fixture.healthy", "Recovery healthy sibling", "1.0.0")]
public class Healthy : BaseUnityPlugin
{
    internal static int Calls;
    private void Awake()
    {
        new Harmony("keel.fixture.healthy").Patch(AccessTools.Method(typeof(SharedTarget), nameof(SharedTarget.Tick)),
            postfix: new HarmonyMethod(typeof(Healthy), nameof(Postfix)));
        gameObject.AddComponent<Verify>();
    }
    private static void Postfix() => Calls++;
}

public static class SharedTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Tick() { }
}

[BepInPlugin("keel.fixture.caught", "Recovery caught startup", "1.0.0")]
[BepInDependency("keel.fixture.healthy")]
public class CaughtFailure : BaseUnityPlugin
{
    internal static int Updates;
    private void Awake()
    {
        var harmony = new Harmony("keel.fixture.caught");
        harmony.Patch(AccessTools.Method(typeof(SharedTarget), nameof(SharedTarget.Tick)),
            postfix: new HarmonyMethod(typeof(CaughtFailure), nameof(Postfix)));
        try
        {
            harmony.CreateClassProcessor(typeof(MissingTargetPatch)).Patch();
        }
        catch (Exception ex)
        {
            Debug.LogError("[Recovery caught startup] Harmony patching failed: " + ex);
        }
    }
    private static void Postfix() { }
    private void Update() => Updates++;
}

[HarmonyPatch(typeof(SharedTarget), "MissingTarget")]
internal static class MissingTargetPatch
{
    private static void Prefix() { }
}

[BepInPlugin("keel.fixture.ordinary", "Recovery ordinary error", "1.0.0")]
public class OrdinaryError : BaseUnityPlugin
{
    private void Awake() => Debug.LogError("[Recovery ordinary error] Optional icon unavailable");
}

// Prepare replaces references to this stand-in with an absent game type. The method using it is never executed.
internal static class ReferenceStub
{
    internal static void Gone() { }
}

internal static class FailureCode
{
    internal const string TypeMessage = "Could not resolve type with token 010000b7 from typeref (expected class 'SeaPower.KeelRecoveryGone' in assembly 'Seapower-Scripts, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null')";
    internal const string MethodMessage = "Method not found: void SeaPower.KeelRecoveryGone.Gone()";
    internal static IEnumerable<CodeInstruction> Throw(Type exception, string message) =>
        new[] { new CodeInstruction(OpCodes.Ldstr, message),
            new CodeInstruction(OpCodes.Newobj, AccessTools.Constructor(exception, new[] { typeof(string) })),
            new CodeInstruction(OpCodes.Throw) };
    private static Type MissingMetadata()
    {
        ReferenceStub.Gone();
        return typeof(ReferenceStub);
    }
}

[BepInPlugin("keel.fixture.type", "Recovery missing type", "1.0.0")]
public class MissingType : BaseUnityPlugin
{
    internal static int Errors;
    private int attempts;
    private float nextAttempt;
    private void Awake() => new Harmony("keel.fixture.type").Patch(AccessTools.Method(typeof(MissingType), nameof(Target)),
        transpiler: new HarmonyMethod(typeof(MissingType), nameof(Transpiler)));
    private static IEnumerable<CodeInstruction> Transpiler() => FailureCode.Throw(typeof(TypeLoadException), FailureCode.TypeMessage);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Target() { }
    private void Update()
    {
        if (!TestProbe.Ready || attempts >= 2 || Time.unscaledTime < nextAttempt) return;
        attempts++;
        nextAttempt = Time.unscaledTime + 0.5f;
        try { Target(); }
        catch (Exception ex) { Errors++; Debug.LogException(ex); }
    }
}

[BepInPlugin("keel.fixture.method", "Recovery missing method", "1.0.0")]
public class MissingMethod : BaseUnityPlugin
{
    internal static int Errors;
    private int attempts;
    private float nextAttempt;
    private void Awake() => new Harmony("keel.fixture.method").Patch(AccessTools.Method(typeof(MissingMethod), nameof(Target)),
        transpiler: new HarmonyMethod(typeof(MissingMethod), nameof(Transpiler)));
    private static IEnumerable<CodeInstruction> Transpiler() => FailureCode.Throw(typeof(MissingMethodException), FailureCode.MethodMessage);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Target() { }
    private void Update()
    {
        if (!TestProbe.Ready || attempts >= 2 || Time.unscaledTime < nextAttempt) return;
        attempts++;
        nextAttempt = Time.unscaledTime + 0.5f;
        try { Target(); }
        catch (Exception ex) { Errors++; Debug.LogException(ex); }
    }
}

internal class Verify : MonoBehaviour
{
    private float readyAt;
    private void Update()
    {
        if (!TestProbe.Ready) return;
        if (readyAt == 0) readyAt = Time.unscaledTime + 3;
        if (Time.unscaledTime < readyAt) return;
        enabled = false;
        SharedTarget.Tick();
        var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(SharedTarget), nameof(SharedTarget.Tick)));
        bool healthy = Healthy.Calls > 0 && patches.Postfixes.Any(p => p.owner == "keel.fixture.healthy");
        bool partial = patches.Postfixes.Any(p => p.owner == "keel.fixture.caught");
        string report = TestProbe.Report;
        bool recovered = report.Contains("[FAILED] Recovery caught startup") && !partial && CaughtFailure.Updates == 0 &&
            report.Contains("[ERRORS] Recovery ordinary error") && MissingType.Errors == 1 && MissingMethod.Errors == 1 && healthy;
        Debug.Log($"KEEL_RECOVERY {(recovered ? "PASS" : "BASELINE")} healthy={healthy} partial_patch={partial} failed_updates={CaughtFailure.Updates} type_errors={MissingType.Errors} method_errors={MissingMethod.Errors}");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(typeof(Verify).Assembly.Location), "result.txt"), report);
    }
}
