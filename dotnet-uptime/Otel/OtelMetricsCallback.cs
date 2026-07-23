
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace MV10.DotnetUptime;

/// <summary>
/// Records counter payloads on System.Diagnostics.Metrics instruments for OTel export.
/// </summary>
class OtelMetricsCallback : IMetricsCallback, IDisposable
{
    public const string MeterName = "dotnet-uptime";

    private readonly Meter meter = new(MeterName);
    private readonly ConcurrentDictionary<string, Gauge<double>> gauges = new();
    private readonly ILogger<OtelMetricsCallback> logger;
    private readonly SelfMetrics selfMetrics;

    // one collision warning per monitored process; entries are dropped when the
    // session ends so a long-running service doesn't accumulate them as PIDs churn
    private readonly ConcurrentDictionary<int, byte> collisionWarned = new();

    // attribute names Uptime applies itself; a monitored application publishing a
    // tag of the same name would produce a duplicate key, so Uptime's value wins
    private static readonly HashSet<string> ReservedTagKeys = new(StringComparer.OrdinalIgnoreCase)
        { "pid", "counter.kind", "container.pid", "container.id" };

    public OtelMetricsCallback(ILogger<OtelMetricsCallback> logger = null, SelfMetrics selfMetrics = null)
    {
        this.logger = logger;
        this.selfMetrics = selfMetrics;
    }

    public void OnCounterPayload(int pid, CounterPayload payload)
    {
        selfMetrics?.MeasurementReceived();

        var instrumentName = $"{payload.ProviderName}.{payload.CounterName}";
        var gauge = gauges.GetOrAdd(instrumentName, name =>
            meter.CreateGauge<double>(name, payload.DisplayUnits, payload.DisplayName));

        var tags = new TagList
        {
            { "pid", pid },
            { "counter.kind", payload.Kind == CounterKind.Rate ? "rate" : "gauge" }
        };

        // only present when the monitored process runs in a container
        if (payload.ContainerPID.HasValue)
            tags.Add("container.pid", payload.ContainerPID.Value);

        if (!string.IsNullOrEmpty(payload.ContainerID))
            tags.Add("container.id", payload.ContainerID);

        // per-process facts selected in [processtags]
        foreach (var tag in payload.ProcessTags)
            tags.Add(tag.Key, tag.Value);

        // tags published by the monitored app are exported as individual attributes
        // under their original names, so backends can filter and group on them
        foreach (var tag in payload.Tags)
        {
            if (TryGetUptimeValue(pid, tag.Key, payload, out var ours))
            {
                ReportCollision(pid, tag, ours);
                continue;
            }

            tags.Add(tag.Key, tag.Value);
        }

        gauge.Record(payload.Value, tags);
    }

    /// <summary>
    /// Determines whether an attribute name is one Uptime supplies itself, either a
    /// fixed attribute or a [processtags] selection, and returns Uptime's value.
    /// </summary>
    private static bool TryGetUptimeValue(int pid, string key, CounterPayload payload, out string ours)
    {
        if (ReservedTagKeys.Contains(key))
        {
            ours = key.ToLowerInvariant() switch
            {
                "pid" => pid.ToString(CultureInfo.InvariantCulture),
                "counter.kind" => payload.Kind == CounterKind.Rate ? "rate" : "gauge",
                "container.pid" => payload.ContainerPID?.ToString(CultureInfo.InvariantCulture),
                "container.id" => payload.ContainerID,
                _ => null
            };
            return true;
        }

        foreach (var processTag in payload.ProcessTags)
        {
            if (string.Equals(processTag.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                ours = processTag.Value;
                return true;
            }
        }

        ours = null;
        return false;
    }

    /// <summary>
    /// Warns once per process when a monitored application publishes a tag that
    /// collides with one of Uptime's own attributes and the values disagree.
    /// Matching values are dropped silently because no information is lost.
    /// </summary>
    private void ReportCollision(int pid, KeyValuePair<string, string> tag, string ours)
    {
        if (string.Equals(ours, tag.Value, StringComparison.Ordinal)) return;

        if (!collisionWarned.TryAdd(pid, 0)) return;

        logger?.LogWarning(
            "PID {Pid} publishes tag '{TagKey}={TagValue}' which collides with Uptime's own '{TagKey}={OurValue}'. "
            + "Uptime's value is kept and the application's is dropped. Further collisions for this process are not reported.",
            pid, tag.Key, tag.Value, tag.Key, ours);
    }

    public void OnSessionEnded(int pid)
    {
        collisionWarned.TryRemove(pid, out _);
    }

    public void Dispose()
    {
        meter.Dispose();
    }
}
