using System.IO;
using BepInEx;
using SeaPowerKeel;

namespace KeelRecovery;

internal static class TestProbe
{
    internal static bool Ready => Keel.GetUiState().StartupComplete;
    internal static string Report => File.ReadAllText(Path.Combine(Paths.BepInExRootPath, "Keel-report.txt"));
}
