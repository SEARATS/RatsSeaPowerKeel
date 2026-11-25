using System;
using System.Reflection;
using HarmonyLib;

namespace SeaPowerKeel;

/// <summary>
/// The moment right before the game shows its first load screen: <c>SeaPower.LoadScreen.ExecuteLoadScreen</c> is called (once, at the
/// start-up) and the picture, the tip and the text are not chosen yet. A mod that asks for a blocking start (<c>BlockingStart</c>)
/// is started here, so it can change them. The game's frames do not exist before its own start-up loading is through, and a mod
/// cannot run in the background (Unity's API belongs to the main thread), so the game waits for such a mod at this one point.
/// </summary>
internal static class LoadScreenGate
{
    public const string HarmonyId = "dev.sewerlabs.seapowerkeel.loadscreengate";
    private static Action fire;
    private static Action<string> warn;

    /// <summary>Hooks the call. Null when done, else why not. <paramref name="start"/> runs once, on the first call of the load screen.</summary>
    public static string Install(Action start, Action<string> warning)
    {
        try
        {
            MethodBase target = AccessTools.Method("SeaPower.LoadScreen:ExecuteLoadScreen");
            if (target == null)
                return "LoadScreen.ExecuteLoadScreen is not in this build";
            fire = start;
            warn = warning;
            new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(typeof(LoadScreenGate).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)));
            return null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private static void Prefix()
    {
        Action start = fire;
        fire = null;
        try
        {
            start?.Invoke();
        }
        catch (Exception ex)
        {
            warn?.Invoke($"The blocking start before the load screen failed: {ex.GetType().Name}: {ex.Message}");   // never take the game down with it
        }
    }
}
