
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MV10.DotnetUptime;

/// <summary>
/// BackgroundService that runs the process scan loop at the configured interval.
/// </summary>
public class ProcessScannerService : BackgroundService
{
    private readonly ProcessManager manager;
    private readonly ILogger<ProcessScannerService> logger;
    private readonly int intervalMs;

    public ProcessScannerService(ProcessManager manager, ConfigParser config,
        ILogger<ProcessScannerService> logger = null)
    {
        this.manager = manager;
        this.logger = logger;
        intervalMs = config.SettingsApp.ProcessScanIntervalMs;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                manager.ScanAndReconcile();
            }
            catch (Exception ex)
            {
                // a single failed scan must not fault ExecuteAsync, which by default stops
                // the host; log it and keep scanning so a transient error self-heals
                logger?.LogError(ex, "Process scan and reconcile pass failed; continuing.");
            }

            try
            {
                await Task.Delay(intervalMs, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
                break;
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        manager.StopAll();
        return base.StopAsync(cancellationToken);
    }
}
