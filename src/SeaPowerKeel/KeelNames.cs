namespace SeaPowerKeel;

/// <summary>
/// Names and ids that more than one of Keel's programs use: the loader, the stub, the patcher, KeelSetup and KeelWatcher compile
/// this file, so a file name or an id is written once. No Unity or BepInEx types here.
/// </summary>
internal static class KeelNames
{
    public const ulong SteamAppId = 1286220;
    public const ulong AlpineWorkshopId = 3832006068;          // Alpine's Mod Loader: a mod loader, not a pack
    public const string AlpineGuid = "alpine.seapower.modloader";
    public const string StubGuid = "dev.sewerlabs.seapowerkeel.stub";

    // The files Keel keeps in BepInEx\.
    public const string ReportFile = "Keel-report.txt";
    public const string LogFile = "Keel-log.txt";
    public const string JournalFile = "Keel-session.txt";
    public const string HeldFile = "Keel-held.txt";
    public const string CrashFile = "Keel-crash.txt";
    public const string WatcherFile = "Keel-watcher.txt";
    public const string ModsFile = "Keel-mods.txt";
    public const string StackFile = "Keel-crash-stack.txt";
    /// <summary>The last game's stack, kept when Keel installs the catcher (which empties the stack file) at the start of the next one.</summary>
    public const string StackPrevFile = "Keel-crash-stack.prev.txt";
    /// <summary>Written by the crash window: this pack folder is to be switched off in the game's mod list.</summary>
    public const string PackOffFile = "Keel-packoff.txt";
    /// <summary>Written by the crash window: the player chose to keep this mod on.</summary>
    public const string KeepFile = "Keel-keep.txt";

    /// <summary>What a log bundle (Save logs, Send report) takes from BepInEx\, as file patterns.</summary>
    public static readonly string[] BundleFiles =
        { "Keel-log*.txt", ReportFile, "Keel-crash*.txt", JournalFile, ModsFile, HeldFile, WatcherFile, "LogOutput.log" };

    /// <summary>
    /// The game's folder in LocalLow (Player.log, CrashDiagnostics), named from Unity's company and product. Keel itself asks Unity
    /// (Application.consoleLogPath); KeelWatcher, outside the game, uses this.
    /// </summary>
    public static string PlayerDataDir => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Triassic Games", "Sea Power");

    /// <summary>How much older than this game's start a file may look and still be this session's (file times are coarse).</summary>
    public const int TimeSlackSeconds = 5;

    // Keel's own files in BepInEx\plugins and \patchers, and BepInEx's plugin cache.
    public const string StubFile = "SeaPowerKeel.Stub.dll";
    public const string PatcherFile = "SeaPowerKeel.Patcher.dll";
    public const string LoaderFile = "SeaPowerKeel.dll";
    public const string AnchorChainPreloaderFile = "AnchorChain.Preloader.dll";
    public const string PluginCacheFile = "chainloader_typeloader.dat";
}
