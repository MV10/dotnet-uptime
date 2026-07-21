
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace MV10.DotnetUptime;

/// <summary>
/// Wraps an OTLP exporter to time each export attempt. The OpenTelemetry SDK owns the
/// export path and reports outcomes on its EventSources without any timing data, so
/// wrapping is the only way to answer whether exports are keeping up with the configured
/// interval. See stats_metrics.md.
/// </summary>
public sealed class TimedMetricExporter : BaseExporter<Metric>
{
    private readonly BaseExporter<Metric> inner;
    private readonly string targetName;
    private readonly SelfMetrics selfMetrics;

    public TimedMetricExporter(BaseExporter<Metric> inner, string targetName, SelfMetrics selfMetrics)
    {
        this.inner = inner;
        this.targetName = targetName;
        this.selfMetrics = selfMetrics;
    }

    public override ExportResult Export(in Batch<Metric> batch)
    {
        var started = Stopwatch.GetTimestamp();
        var result = inner.Export(batch);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        selfMetrics?.RecordExport(targetName, elapsed, result == ExportResult.Success);

        return result;
    }

    protected override bool OnShutdown(int timeoutMilliseconds) => inner.Shutdown(timeoutMilliseconds);

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
