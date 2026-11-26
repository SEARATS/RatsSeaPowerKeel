namespace SeaPowerKeel;

/// <summary>
/// How KeelWatcher starts the game again after a restart or a crash. Kept apart from the watcher (and free of Unity and BepInEx
/// types) so the offline self-test can check the command it builds.
/// </summary>
internal static class GameStart
{
    internal const string SteamLink = "steam://rungameid/";
    private const string SteamFolder = "/steamapps/common/";

    // The arguments for cmd.exe: wait until the old game is gone, then start the new one from a detached shell.
    // A Steam install on Windows starts through Steam, so Steam's own launch options apply. Under Wine (Linux, Proton) the game is
    // started straight from inside Wine instead: the steam link there starts it without the player's launch options, and
    // WINEDLLOVERRIDES, which BepInEx needs on Linux, is one of them (issue #4). A direct start inherits it from this process.
    internal static string Command(string exe, bool onWine, int delaySeconds)
    {
        bool steam = exe.Replace('\\', '/').IndexOf(SteamFolder, System.StringComparison.OrdinalIgnoreCase) >= 0;
        string start = steam && !onWine
            ? "start \"\" " + SteamLink + KeelNames.SteamAppId
            : $"start \"\" /d {Quote(System.IO.Path.GetDirectoryName(exe))} {Quote(exe)}";
        return $"/c timeout /t {delaySeconds} /nobreak >nul & " + start;
    }

    private static string Quote(string text) => "\"" + text + "\"";
}
