using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SeaPowerKeel;

internal enum PluginState
{
    Pending,
    Running,            // started, no error so far
    RunningWithErrors,  // started, but threw while starting or later
    Failed,             // threw while starting
    NotStarted,         // a rule kept it from starting: missing dependency, conflict, wrong game version ...
    Skipped,            // on purpose and harmless: duplicate copy, other game version's build, switched off
    LeftToAnchorChain,  // an Anchor Chain mod while Anchor Chain itself is running
}

/// <summary>How the counts in a summary are coloured: the report window colours each part by this, not by its words.</summary>
internal enum Tone { Good, Warning, Problem, OtherLoader }

/// <summary>
/// The words Keel writes for each plugin state and the shape of the report's lines, in one place. Keel's report, the loading
/// window and KeelWatcher (which reads Keel-report.txt from outside the game) all go by this file, so a tag or a counts line
/// cannot be worded one way and read back another (the loading window waited for tags Keel no longer wrote, 2026-11-19).
/// No Unity or BepInEx types: KeelWatcher and the loading preview compile it too.
/// </summary>
internal static class ReportFormat
{
    public static string Tag(PluginState s) => s switch
    {
        PluginState.Running => "RUNNING",
        PluginState.RunningWithErrors => "ERRORS",
        PluginState.Failed => "FAILED",
        PluginState.NotStarted => "NOT STARTED",
        PluginState.Skipped => "SKIPPED",
        PluginState.LeftToAnchorChain => "OTHER LOADER",
        _ => "WAITING",
    };

    public static bool IsProblem(PluginState s) => s is PluginState.Failed or PluginState.NotStarted or PluginState.RunningWithErrors;

    public static bool IsRunning(PluginState s) => s is PluginState.Running or PluginState.RunningWithErrors;

    /// <summary>The state a tag stands for; false for any other text.</summary>
    public static bool TryParseTag(string tag, out PluginState state)
    {
        foreach (PluginState s in System.Enum.GetValues(typeof(PluginState)))
        {
            if (Tag(s) == tag)
            {
                state = s;
                return true;
            }
        }
        state = PluginState.Pending;
        return false;
    }

    /// <summary>The start of a mod's line in the report: "[FAILED] Name 1.0 (guid)".</summary>
    public static string ModLine(PluginState s, string display, string version, string guid) => $"[{Tag(s)}] {display} {version} ({guid})";

    /// <summary>A problem mod's line as <see cref="ModLine"/> writes it; group 2 is "Name version".</summary>
    public static readonly Regex ProblemLine = new(
        @"^\[(" + string.Join("|", System.Enum.GetValues(typeof(PluginState)).Cast<PluginState>().Where(IsProblem).Select(s => Regex.Escape(Tag(s)))) + @")\] (.+?) \(",
        RegexOptions.Multiline);

    private const string RunningWord = "running";
    private static readonly Regex CountsLine = new(@"^\d+ " + RunningWord + @"\b");

    /// <summary>Is this the counts line <see cref="Summary"/> writes ("17 running, 1 failed")?</summary>
    public static bool IsCountsLine(string line) => CountsLine.IsMatch(line);

    /// <summary>"17 running, 1 failed, ..." as parts with their tone.</summary>
    public static List<(string text, Tone tone)> Summary(int running, int errors, int failed, int notStarted, int otherLoader, int orderWarnings)
    {
        var parts = new List<(string, Tone)> { ($"{running} {RunningWord}", Tone.Good) };
        if (errors > 0) parts.Add(($"{errors} with errors", Tone.Warning));
        if (failed > 0) parts.Add(($"{failed} failed", Tone.Problem));
        if (notStarted > 0) parts.Add(($"{notStarted} not started", Tone.Problem));
        if (otherLoader > 0) parts.Add(($"{otherLoader} left to another loader", Tone.OtherLoader));
        if (orderWarnings > 0) parts.Add(($"{orderWarnings} with load order warnings", Tone.Warning));
        return parts;
    }

    public const string SummarySeparator = ", ";
}
