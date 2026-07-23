
namespace MV10.DotnetUptime;

/// <summary>
/// Writes counter payloads to the console.
/// </summary>
class ConsoleMetricsCallback : IMetricsCallback
{
    public void OnCounterPayload(int pid, CounterPayload payload)
    {
        var tags = payload.Tags.Count == 0
            ? ""
            : $" [{string.Join(" ", payload.Tags.Select(t => $"{t.Key}={t.Value}"))}]";

        var container = "";
        if (payload.ContainerPID.HasValue || !string.IsNullOrEmpty(payload.ContainerID))
        {
            var parts = new List<string>();
            if (payload.ContainerPID.HasValue) parts.Add($"container.pid={payload.ContainerPID.Value}");
            if (!string.IsNullOrEmpty(payload.ContainerID)) parts.Add($"container.id={payload.ContainerID}");
            container = $" {{{string.Join(" ", parts)}}}";
        }

        Console.WriteLine($"[{payload.Timestamp:HH:mm:ss}] {pid} {payload.ProviderName}/{payload.CounterName}: {payload.Value:F2} {payload.DisplayUnits}{tags}{container}");
    }

    public void OnSessionEnded(int pid)
    {
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] Session ended for PID {pid}");
    }
}
