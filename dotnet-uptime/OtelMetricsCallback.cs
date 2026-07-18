
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MV10.DotnetUptime;

/// <summary>
/// Records counter payloads on System.Diagnostics.Metrics instruments for OTel export.
/// </summary>
class OtelMetricsCallback : IMetricsCallback, IDisposable
{
    public const string MeterName = "dotnet-uptime";

    private readonly Meter meter = new(MeterName);
    private readonly ConcurrentDictionary<string, Gauge<double>> gauges = new();

    public void OnCounterPayload(int pid, CounterPayload payload)
    {
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

        if (!string.IsNullOrEmpty(payload.Tags))
            tags.Add("counter.tags", payload.Tags);

        gauge.Record(payload.Value, tags);
    }

    public void OnSessionEnded(int pid) { }

    public void Dispose()
    {
        meter.Dispose();
    }
}

/// <summary>
/// Delegates to multiple IMetricsCallback implementations.
/// </summary>
class CompositeMetricsCallback : IMetricsCallback
{
    private readonly IMetricsCallback[] callbacks;

    public CompositeMetricsCallback(params IMetricsCallback[] callbacks)
    {
        this.callbacks = callbacks;
    }

    public void OnCounterPayload(int pid, CounterPayload payload)
    {
        foreach (var cb in callbacks)
            cb.OnCounterPayload(pid, payload);
    }

    public void OnSessionEnded(int pid)
    {
        foreach (var cb in callbacks)
            cb.OnSessionEnded(pid);
    }
}
