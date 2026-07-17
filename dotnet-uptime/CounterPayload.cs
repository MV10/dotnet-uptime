
namespace MV10.DotnetUptime;

/// <summary>
/// A single counter measurement from a monitored process.
/// </summary>
public class CounterPayload
{
    public string ProviderName { get; init; }
    public string CounterName { get; init; }
    public string DisplayName { get; init; }
    public string DisplayUnits { get; init; }
    public double Value { get; init; }
    public DateTime Timestamp { get; init; }
    public string Tags { get; init; }
    public CounterKind Kind { get; init; }
}

public enum CounterKind
{
    Gauge,
    Rate
}

/// <summary>
/// Callback interface for delivering counter measurements from a MetricsSession.
/// </summary>
public interface IMetricsCallback
{
    void OnCounterPayload(int pid, CounterPayload payload);
    void OnSessionEnded(int pid);
}
