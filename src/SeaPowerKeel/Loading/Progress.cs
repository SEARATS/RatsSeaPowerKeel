using System;
using System.Collections.Generic;

namespace SeaPowerKeel.Loading;

internal sealed class ModLine
{
    public string Name;
    public PluginState State;
    public double TookSeconds;     // time since the event before it: roughly how long this mod held the game up
}

/// <summary>
/// What the overlay shows. Keel tells its own steps and results by calling the methods here (KeelHost), so nothing of Keel's
/// wording is parsed back; Anchor Chain's preloader and the game itself are only heard through their log lines (<see cref="Feed"/>).
/// Calls come from any thread, the overlay reads from its own thread, so everything goes through one lock. No game or BepInEx
/// types in here: the preview tool in tools/loading-preview feeds it by hand.
/// </summary>
internal sealed class Progress
{
    private readonly object gate = new object();
    private readonly List<ModLine> mods = new List<ModLine>();
    private readonly Func<double> clock;          // seconds since the game process started

    private string phase = "Starting up";
    private int total;                            // plugins Keel found (0 = unknown)
    private int handled;                          // plugins with a result so far
    private double lastEvent;
    private string lastEventText = "";
    private bool done;
    private string keelVersion = "";
    private int running, withErrors, skipped, notStarted, orderWarnings;
    private double keelMs, doneElapsed, slowestSeconds;
    private string slowestName = "";
    private int startIndex;                       // the step Keel is in (Starting)
    private string current;                       // the mod Keel is starting right now (null between two)
    private double currentSince;
    private double doneAt;

    public Progress(Func<double> clock)
    {
        this.clock = clock;
        lastEvent = clock();
    }

    public sealed class Snapshot
    {
        public string Phase;
        public int Total, Handled;
        public double Elapsed, Idle;
        public string LastEvent;
        public bool Done;
        // what the summary shows once every mod has started
        public string KeelVersion, SlowestName;
        public int Running, WithErrors, Skipped, NotStarted, OrderWarnings;
        public bool HasProblems => WithErrors + NotStarted + OrderWarnings > 0;

        // The summary card's lines, the same on the loading window's card and the game-drawn one.
        public string Title => "KEEL" + (string.IsNullOrEmpty(KeelVersion) ? "" : " " + KeelVersion);
        public string ReadyLine => $"{Running} running · Game ready in {Clock(DoneElapsed > 0 ? DoneElapsed : Elapsed)}" + (Skipped > 0 ? $" · {Skipped} skipped" : "");

        public string SummaryLine(string alert)
        {
            if (alert != null) return alert + " - click for details";
            int errors = WithErrors + NotStarted;
            if (errors > 0 && OrderWarnings > 0) return $"{errors} mod problems, {OrderWarnings} load order warnings";
            if (errors > 0) return $"{errors} {(errors == 1 ? "mod has" : "mods have")} problems - click for the report";
            if (OrderWarnings > 0) return $"Load order warnings ({OrderWarnings}) - click for the report";
            return "No problems  -  click for the report";
        }
        public double KeelSeconds, DoneElapsed, SlowestSeconds;
        public string Current;                  // the mod being started now
        public double CurrentFor;                 // for how long
        public int CurrentIndex;
        public double SinceDone;
        public readonly List<ModLine> Recent = new List<ModLine>();
    }

    public Snapshot Take(int lines)
    {
        lock (gate)
        {
            double now = clock();
            var s = new Snapshot
            {
                Phase = phase, Total = total, Handled = handled, Elapsed = now, Idle = now - lastEvent,
                LastEvent = lastEventText, Done = done, KeelVersion = keelVersion, SlowestName = slowestName,
                Running = running, WithErrors = withErrors, Skipped = skipped, NotStarted = notStarted, OrderWarnings = orderWarnings,
                KeelSeconds = keelMs / 1000.0, DoneElapsed = doneElapsed, SlowestSeconds = slowestSeconds, Current = current,
                CurrentFor = current != null ? now - currentSince : 0, CurrentIndex = startIndex, SinceDone = done ? now - doneAt : 0,
            };
            for (int i = Math.Max(0, mods.Count - lines); i < mods.Count; i++)
                s.Recent.Add(mods[i]);
            return s;
        }
    }

    public void SetOrderWarnings(int count) { lock (gate) orderWarnings = count; }

    /// <summary>"1:05" for 65 seconds.</summary>
    public static string Clock(double seconds) => ((int)seconds / 60) + ":" + ((int)seconds % 60).ToString("00");

    // Keel's own steps, told by Keel: each takes the line Keel logs with it, which the window shows as the last event.

    public void KeelStarted(string version, string line) => Step("Mod loader (Keel) started", line, () => keelVersion = version);

    public void ModListThere(string line) => Step("Waiting for the game's load screen", line);

    public void LoadScreenDone(string line) => Step("Looking for mods", line);

    public void FoldersOn(string line) => Step("Looking for mods", line);

    public void Found(int plugins) => Step("Starting mods", "found " + plugins + " mods", () =>
    {
        if (startIndex == 0) total = plugins;
    });

    /// <summary>Right before a mod starts: its place among the mods to start and their number (the bar fills exactly).</summary>
    public void Starting(int index, int count, string name) => Step("Starting mods", "starting " + name, () =>
    {
        startIndex = index;
        total = count;
        handled = index - 1;
        current = name;
        currentSince = clock();
    });

    public void Result(string name, PluginState state) => Step("Starting mods", name, () =>
    {
        double now = clock();
        double took = current != null ? now - currentSince : now - lastEvent;
        mods.Add(new ModLine { Name = name, State = state, TookSeconds = took });
        handled = startIndex > 0 ? startIndex : handled + 1;
        current = null;
        Count(state);
        if (took > slowestSeconds)
        {
            slowestSeconds = took;
            slowestName = name;
        }
    });

    public void StartedIn(double ms) { lock (gate) keelMs = ms; }

    public void Done(string line) => Step("All mods started", line, () =>
    {
        done = true;
        current = null;
        doneAt = clock();
        doneElapsed = doneAt;
    });

    private void Count(PluginState state)
    {
        if (ReportFormat.IsRunning(state)) running++;
        if (state == PluginState.RunningWithErrors) withErrors++;
        else if (state is PluginState.Skipped or PluginState.LeftToAnchorChain) skipped++;
        else if (state != PluginState.Running) notStarted++;
    }

    // newPhase null: the phase stays.
    private void Step(string newPhase, string eventText, Action change = null)
    {
        lock (gate)
        {
            change?.Invoke();
            phase = newPhase ?? phase;
            lastEvent = clock();
            lastEventText = eventText;
        }
    }

    /// <summary>
    /// A log line of another program part: <paramref name="source"/> is the log source's name ("Unity Log", "AnchorChain Preloader" ...).
    /// The loading window hands every log line but Keel's own here.
    /// </summary>
    public void Feed(string source, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        int nl = text.IndexOf('\n');
        string line = (nl > 0 ? text.Substring(0, nl) : text).Trim();
        if (IsAnchorChain(source ?? ""))
            Step(line.Contains("started") ? "Anchor Chain: searching the mod folders"
                 : line.StartsWith("Could not find", StringComparison.Ordinal) ? "Starting up" : null, line);
        else if (GameStep(line) is (string newPhase, string what))
            Step(newPhase, what);
    }

    private static bool IsAnchorChain(string source) =>
        source.IndexOf("AnchorChain", StringComparison.OrdinalIgnoreCase) >= 0 || source.IndexOf("Anchor Chain", StringComparison.OrdinalIgnoreCase) >= 0;

    // The game's own start-up, from its log.
    private static (string phase, string what)? GameStep(string line)
    {
        if (line.Contains("(C) Triassic Games")) return ("Game starting", "game engine is up");
        if (line.Contains("FileManager.CheckFileValidity")) return ("Checking game files", "checking game files");
        if (line.Contains("FileManager.OnStart")) return ("Reading the mod list", "reading the mod list");
        if (line.Contains("3D WebView")) return ("Starting the game's web view", "web view");
        return null;
    }
}
