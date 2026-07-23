
namespace MV10.DotnetUptime;

/// <summary>
/// A point-in-time view of one monitored process for the summary command: the discovered
/// process and whether its metrics session is currently running. Connected means the
/// EventPipe reader is live; not connected means the session task has ended (a fault, since
/// a process still being tracked has not been stopped deliberately) and is not retried today.
/// </summary>
public sealed record MonitoredProcessInfo(DiagnosticProcess Process, bool Connected);
