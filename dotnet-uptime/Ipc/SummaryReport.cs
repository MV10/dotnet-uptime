
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
        var connected = monitored.Count(p => p.Connected);
        var faulted = monitored.Count - connected;
        var uptime = DateTime.UtcNow - metrics.StartedUtc;
        var attempts = metrics.ExportAttemptsTotal;
        var failures = metrics.ExportFailuresTotal;

        var report = new StringBuilder();
        report.AppendLine("dotnet-uptime service summary");
        report.AppendLine();
        report.AppendLine($"Uptime            {FormatUptime(uptime)}");
        report.AppendLine($"Monitored         {monitored.Count} process(es)");
        report.AppendLine($"Sessions          {connected} connected, {faulted} faulted");
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
        report.AppendLine($"  {"PID",-8}{"STATE",-11}COMMAND");
        foreach (var p in monitored)
            report.AppendLine($"  {p.Process.PID,-8}{(p.Connected ? "connected" : "faulted"),-11}{Redact(p.Process)}");

        return report.ToString().TrimEnd();
    }

    // the real argv (Linux) redacts accurately; Windows exposes only a flattened string
    private static string Redact(DiagnosticProcess process)
        => process.CommandLineArgs is not null
            ? CommandLineRedactor.Redact(process.CommandLineArgs)
            : CommandLineRedactor.RedactFlattened(process.CommandLine);

    private static string FormatUptime(TimeSpan uptime)
        => uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s"
            : $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s";
}
