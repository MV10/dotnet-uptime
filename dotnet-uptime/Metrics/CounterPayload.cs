
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

    /// <summary>
    /// Namespace-internal PID when the monitored process runs in a separate PID
    /// namespace (a container); null for host processes.
    /// </summary>
    public int? ContainerPID { get; init; }

    /// <summary>
    /// Container ID from the process cgroup (Docker/containerd); null for host processes.
    /// </summary>
    public string ContainerID { get; init; }
}
