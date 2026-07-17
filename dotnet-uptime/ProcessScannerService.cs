
using Microsoft.Extensions.Hosting;

namespace MV10.DotnetUptime;

/// <summary>
/// BackgroundService that runs the process scan loop at the configured interval.
/// </summary>
public class ProcessScannerService : BackgroundService
{
    private readonly ProcessManager manager;
    private readonly int intervalMs;

    public ProcessScannerService(ProcessManager manager, UptimeConfig config)
    {
        this.manager = manager;
        intervalMs = config.App.ProcessScanIntervalMs;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            manager.ScanAndReconcile();
            await Task.Delay(intervalMs, stoppingToken).ConfigureAwait(false);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        manager.StopAll();
        return base.StopAsync(cancellationToken);
    }
}
