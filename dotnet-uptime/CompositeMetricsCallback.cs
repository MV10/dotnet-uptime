namespace MV10.DotnetUptime;

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
