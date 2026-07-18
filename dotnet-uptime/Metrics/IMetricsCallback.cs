
namespace MV10.DotnetUptime;

/// <summary>
/// Callback interface for delivering counter measurements from a MetricsSession.
/// </summary>
public interface IMetricsCallback
{
    void OnCounterPayload(int pid, CounterPayload payload);
    void OnSessionEnded(int pid);
}