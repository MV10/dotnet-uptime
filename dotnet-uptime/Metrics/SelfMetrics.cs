
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MV10.DotnetUptime;

/// <summary>
/// Uptime's own operational metrics. Published on a real Meter so they travel the
/// same paths as everything else: out to configured endpoints when listed in [diags],
/// and over EventPipe to the interactive `stats` command. See stats_metrics.md.
/// </summary>
public sealed class SelfMetrics : IDisposable
{
    public const string MeterName = "dotnet-uptime.self";

    private readonly Meter meter = new(MeterName);

    private readonly Counter<long> sessionsStarted;
    private readonly Counter<long> sessionsEnded;
    private readonly Counter<long> sessionsFailed;
    private readonly Counter<long> measurementsReceived;
    private readonly Counter<long> exportAttempts;
    private readonly Counter<long> exportFailures;
    private readonly Histogram<double> discoveryDuration;
    private readonly Histogram<double> exportDuration;

    // observable gauges read live state owned elsewhere; the defaults keep the
    // instruments valid before ProcessManager has registered its providers
    private Func<int> monitoredProcesses = () => 0;
    private Func<int> filteredProcesses = () => 0;
    private Func<int> activeSessions = () => 0;

    // running totals kept alongside the write-only Counter/Histogram instruments, which
    // expose no accumulated value; the summary command reads state, not a metrics stream,
    // so it needs these back out. See project-todo-summary-command.
    private long sessionsFailedTotal;
    private long exportAttemptsTotal;
    private long exportFailuresTotal;
    private double lastDiscoveryMs;

    /// <summary>
    /// When the service's metrics were initialized, used to report uptime.
    /// </summary>
    public DateTime StartedUtc { get; } = DateTime.UtcNow;

    public long SessionsFailedTotal => Interlocked.Read(ref sessionsFailedTotal);

    public long ExportAttemptsTotal => Interlocked.Read(ref exportAttemptsTotal);

    public long ExportFailuresTotal => Interlocked.Read(ref exportFailuresTotal);

    /// <summary>
    /// Wall time of the most recent discovery and reconcile pass, in milliseconds.
    /// </summary>
    public double LastDiscoveryMs => Volatile.Read(ref lastDiscoveryMs);

    public SelfMetrics()
    {
        meter.CreateObservableGauge("uptime.processes.monitored", () => monitoredProcesses(),
            "{process}", "Processes currently monitored");

        meter.CreateObservableGauge("uptime.processes.filtered", () => filteredProcesses(),
            "{process}", "Eligible .NET processes excluded by the configured rules");

        meter.CreateObservableGauge("uptime.sessions.active", () => activeSessions(),
            "{session}", "Sessions whose EventPipe reader is running");

        sessionsStarted = meter.CreateCounter<long>("uptime.sessions.started",
            "{session}", "Sessions started since service start");

        sessionsEnded = meter.CreateCounter<long>("uptime.sessions.ended",
            "{session}", "Sessions ended normally");

        sessionsFailed = meter.CreateCounter<long>("uptime.sessions.failed",
            "{session}", "Sessions ended on an IPC or EventPipe error");

        measurementsReceived = meter.CreateCounter<long>("uptime.measurements.received",
            "{measurement}", "Counter payloads received from monitored processes");

        exportAttempts = meter.CreateCounter<long>("uptime.export.attempts",
            "{export}", "Export attempts");

        exportFailures = meter.CreateCounter<long>("uptime.export.failures",
            "{failure}", "Export attempts that failed");

        discoveryDuration = meter.CreateHistogram<double>("uptime.discovery.duration",
            "ms", "Wall time of a discovery and reconcile pass");

        exportDuration = meter.CreateHistogram<double>("uptime.export.duration",
            "ms", "Wall time of one export attempt");
    }

    /// <summary>
    /// Supplies the live counts backing the observable gauges. Called once by
    /// ProcessManager, which owns the state they report.
    /// </summary>
    public void SetStateProviders(Func<int> monitored, Func<int> filtered, Func<int> active)
    {
        monitoredProcesses = monitored ?? (() => 0);
        filteredProcesses = filtered ?? (() => 0);
        activeSessions = active ?? (() => 0);
    }

    public void SessionStarted() => sessionsStarted.Add(1);

    public void SessionEnded() => sessionsEnded.Add(1);

    public void SessionFailed()
    {
        sessionsFailed.Add(1);
        Interlocked.Increment(ref sessionsFailedTotal);
    }

    public void MeasurementReceived() => measurementsReceived.Add(1);

    public void RecordDiscovery(double milliseconds)
    {
        discoveryDuration.Record(milliseconds);
        Volatile.Write(ref lastDiscoveryMs, milliseconds);
    }

    /// <summary>
    /// Records one export attempt. The target tag names the [otlp] section, the single
    /// deliberate exception to keeping these instruments untagged: with several targets
    /// configured there is otherwise no way to tell which endpoint is slow or failing.
    /// </summary>
    public void RecordExport(string target, double milliseconds, bool succeeded)
    {
        var tag = new KeyValuePair<string, object>("target", target);

        exportDuration.Record(milliseconds, tag);
        exportAttempts.Add(1, tag);
        Interlocked.Increment(ref exportAttemptsTotal);
        if (!succeeded)
        {
            exportFailures.Add(1, tag);
            Interlocked.Increment(ref exportFailuresTotal);
        }
    }

    /// <summary>
    /// Times an operation and records it as a discovery pass.
    /// </summary>
    public void TimeDiscovery(Action action)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            action();
        }
        finally
        {
            RecordDiscovery(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    public void Dispose() => meter.Dispose();
}
