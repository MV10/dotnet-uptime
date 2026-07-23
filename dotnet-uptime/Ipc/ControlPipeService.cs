
using CommandLineSwitchPipe;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MV10.DotnetUptime;

/// <summary>
/// Hosts the control pipe that service-directed commands arrive on. Only commands aimed
/// at the running service use this channel; list, procs and single-PID monitoring run
/// standalone under the caller's own privileges and never touch it.
/// </summary>
public class ControlPipeService : BackgroundService
{
    private readonly ConfigParser config;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<ControlPipeService> logger;
    private readonly ProcessManager processManager;
    private readonly SelfMetrics selfMetrics;

    public ControlPipeService(ConfigParser config, ILoggerFactory loggerFactory,
        ILogger<ControlPipeService> logger, ProcessManager processManager, SelfMetrics selfMetrics)
    {
        this.config = config;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
        this.processManager = processManager;
        this.selfMetrics = selfMetrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ControlPipe.Configure(config, loggerFactory);

        try
        {
            await CommandLineSwitchServer.StartServer(HandleSwitches, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            // reported rather than rethrown: a dead control channel only costs the commands
            // aimed at this service, whereas letting it escape would stop the host and end
            // metrics collection, which is the reason the service exists
            logger.LogError(ex,
                "The control pipe {PipeName} has failed, so commands directed at this service will not "
                + "work. Metrics collection is unaffected and continues. A socket left behind by another "
                + "user's instance can prevent binding, in which case removing it and restarting resolves this.",
                ControlPipe.Name(config));
        }
    }

    // Runs on the pipe listener thread. Commands are added by the features that need
    // them; the transport itself understands none.
    private string HandleSwitches(string[] args)
    {
        if (args.Length == 0) return "No command was provided.";

        return args[0].ToLowerInvariant() switch
        {
            // enforced here as well as client-side, so a hand-written client cannot reach
            // the report by talking to the pipe directly. Command lines are redacted inside
            // Build, before the text reaches the pipe.
            "summary" when config.SettingsApp.SummaryCommand == SummaryCommandMode.Disabled
                => "The summary command is disabled on this service.",
            "summary" => SummaryReport.Build(processManager, selfMetrics),
            _ => $"Unrecognized command: {args[0]}"
        };
    }
}
