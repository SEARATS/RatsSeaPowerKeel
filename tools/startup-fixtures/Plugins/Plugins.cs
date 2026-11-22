using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using AnchorChain;
using SeaPowerKeel;
using UnityEngine;

#if EARLY
[assembly: AssemblyMetadata("Keel.LoadTiming", "Early")]
#endif

namespace KeelFixture;

internal static class Probe
{
    internal static readonly AppDomain Domain = AppDomain.CurrentDomain;
    private static readonly BepInEx.Logging.ManualLogSource log = BepInEx.Logging.Logger.CreateLogSource("Keel startup fixture");
    internal static bool StartupDone => (bool)typeof(Keel).Assembly.GetType("SeaPowerKeel.GameState")
        .GetMethod("StartupDone", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("KEEL_FIXTURE " + message);
    }
    internal static void Pass(string name)
    {
        Domain.SetData("KeelFixture." + name, true);
        log.LogInfo("KEEL_FIXTURE PASS " + name);
    }
}

#if EARLY
[ACPlugin("keel.fixture.early-dependency", "Keel fixture early dependency", "1.0")]
public class EarlyDependency : IAnchorChainMod
{
    public void TriggerEntryPoint()
    {
        Probe.Require(!Probe.StartupDone, "early dependency started after startup");
        Probe.Pass("early-dependency");
    }
}

[ACPlugin("keel.fixture.early", "Keel fixture early consumer", "1.0")]
[ACDependency("keel.fixture.early-dependency", null, null)]
public class EarlyConsumer : IAnchorChainMod
{
    public void TriggerEntryPoint()
    {
        Probe.Require(!Probe.StartupDone && Keel.IsRunning("keel.fixture.early-dependency"), "early dependency order");
        string[] folders = Keel.GetEnabledModFolders();
        string folder = folders.Single(path => Path.GetFileName(path) == "KeelEarlyFixture");
        bool preparation = File.ReadAllText(Path.Combine(folder, "expect-preparation.txt")).Trim() == "true";
        if (preparation)
        {
            Probe.Require(SpinWait.SpinUntil(() => Probe.Domain.GetData("KeelFixture.Prepared") != null, 5000), "preparation did not complete");
            Probe.Require((string)Probe.Domain.GetData("KeelFixture.Prepared") == "fixture-input", "preparation input");
            Probe.Require(Probe.Domain.GetData("KeelFixture.Failure") is true, "failed callback was not attempted");
            Probe.Require((int)Probe.Domain.GetData("KeelFixture.PreparationThread") != Environment.CurrentManagedThreadId, "preparation ran on the main thread");
            Probe.Require(((string[])Probe.Domain.GetData("KeelFixture.PreparationFolders")).SequenceEqual(folders, StringComparer.OrdinalIgnoreCase), "preparation mod order");
            Probe.Pass("preparation");
        }
        else
            Probe.Require(Probe.Domain.GetData("KeelFixture.Prepared") == null, "old stub unexpectedly prepared data");
        Probe.Pass("early-consumer");
        var host = new GameObject("Keel startup fixture");
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<ProgressProbe>();
    }
}

[ACPlugin("keel.fixture.deferred", "Keel fixture deferred consumer", "1.0")]
[ACDependency("keel.fixture.late", null, null)]
public class DeferredConsumer : IAnchorChainMod
{
    public void TriggerEntryPoint()
    {
        Probe.Require(Probe.StartupDone && Keel.IsRunning("keel.fixture.late"), "early mod did not wait for its normal dependency");
        Probe.Pass("deferred-consumer");
    }
}

public class ProgressProbe : MonoBehaviour
{
    private bool handedOff;
    private static readonly FieldInfo summary = typeof(Keel).Assembly.GetType("SeaPowerKeel.Loading.LoadingWindow")
        .GetField("GameDrawsSummary", BindingFlags.NonPublic | BindingFlags.Static);

    private void OnGUI()
    {
        if (Probe.StartupDone) return;
        GUI.Label(new Rect(12, 70, 400, 26), "Keel startup integration test");
        if (handedOff || Event.current.type != EventType.Repaint) return;
        handedOff = true;
        Keel.HandOffStartupWindow();
        Probe.Pass("progress-handoff");
    }

    private void Update()
    {
        if (!handedOff || !Keel.IsRunning("keel.fixture.deferred") || !(bool)summary.GetValue(null)) return;
        Probe.Pass("summary");
        Destroy(gameObject);
    }
}
#else
[ACPlugin("keel.fixture.late", "Keel fixture normal dependency", "1.0")]
[ACDependency("keel.fixture.early", null, null)]
public class NormalDependency : IAnchorChainMod
{
    public void TriggerEntryPoint()
    {
        Probe.Require(Probe.StartupDone && Keel.IsRunning("keel.fixture.early"), "normal phase lost its early dependency");
        Probe.Pass("normal-dependency");
    }
}
#endif
