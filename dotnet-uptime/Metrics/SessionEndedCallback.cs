
namespace MV10.DotnetUptime;

/// <summary>
/// Invokes an action when the monitored session ends, used by interactive mode
/// to stop waiting once the target process exits.
/// </summary>
class SessionEndedCallback : IMetricsCallback
{
    private readonly Action onSessionEnded;

    public SessionEndedCallback(Action onSessionEnded)
    {
        this.onSessionEnded = onSessionEnded;
    }

    public void OnCounterPayload(int pid, CounterPayload payload) { }

    public void OnSessionEnded(int pid) => onSessionEnded();
}
