
using System.Text;

namespace MV10.DotnetUptime;

/// <summary>
/// Builds the human-readable state snapshot returned by the summary command. Runs in the
/// service, on the control-pipe thread, and redacts every command line before it is
/// returned: the pipe is world-open in the default posture, so secrets must never reach it.
/// </summary>
public static class SummaryReport
{
    public static string Build(ProcessManager processManager, SelfMetrics metrics)
    {
        var monitored = processManager.SnapshotMonitored();
        var connected = monitored.Count(p => p.Connected && !p.Reconnecting);
        var reconnecting = monitored.Count(p => p.Connected && p.Reconnecting);
        var faulted = monitored.Count(p => !p.Connected);
        var uptime = DateTime.UtcNow - metrics.StartedUtc;
        var attempts = metrics.ExportAttemptsTotal;
        var failures = metrics.ExportFailuresTotal;

        var report = new StringBuilder();
        report.AppendLine("dotnet-uptime service summary");
        report.AppendLine();
        report.AppendLine($"Uptime            {FormatUptime(uptime)}");
        report.AppendLine($"Monitored         {monitored.Count} process(es)");
        report.AppendLine($"Sessions          {connected} connected, {reconnecting} reconnecting, {faulted} faulted");
        report.AppendLine($"Last scan         {metrics.LastDiscoveryMs:0.0} ms");
        report.AppendLine($"Exports           {attempts} attempted, {failures} failed");
        report.AppendLine($"Session failures  {metrics.SessionsFailedTotal}");
        report.AppendLine();

        if (monitored.Count == 0)
        {
            report.Append("No processes are currently monitored.");
            return report.ToString();
        }

        report.AppendLine("Monitored processes:");
        report.AppendLine($"  {"PID",-8}{"STATE",-13}COMMAND");
        foreach (var p in monitored)
            report.AppendLine($"  {p.Process.PID,-8}{State(p),-13}{CommandLineRedactor.Redact(p.Process)}");

        return report.ToString().TrimEnd();
    }

    private static string State(MonitoredProcessInfo p)
        => !p.Connected ? "faulted" : p.Reconnecting ? "reconnecting" : "connected";

    private static string FormatUptime(TimeSpan uptime)
        => uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s"
            : $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s";
}
