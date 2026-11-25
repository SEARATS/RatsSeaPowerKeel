namespace SeaPowerKeel;

/// <summary>Loader-owned UI state for tools. Contains no Unity or internal UI types.</summary>
public class KeelUiState(string version, bool startupComplete, bool reportOpen, string waitingFor)
{
    public string Version { get; } = version;
    public bool StartupComplete { get; } = startupComplete;
    public bool ReportOpen { get; } = reportOpen;
    public string WaitingFor { get; } = waitingFor;
}
