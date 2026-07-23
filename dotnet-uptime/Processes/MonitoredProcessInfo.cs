
namespace MV10.DotnetUptime;

/// <summary>
/// A point-in-time view of one monitored process for the summary command: the discovered
/// process and its session state. Connected means the EventPipe reader is live; Reconnecting
/// means it dropped and is being re-established; neither means the session gave up after
/// repeated failures and the process is no longer monitored until it restarts.
/// </summary>
public sealed record MonitoredProcessInfo(DiagnosticProcess Process, bool Connected, bool Reconnecting);
