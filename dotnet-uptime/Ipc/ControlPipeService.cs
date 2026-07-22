
using CommandLineSwitchPipe;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;

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

    public ControlPipeService(ConfigParser config, ILoggerFactory loggerFactory)
    {
        this.config = config;
        this.loggerFactory = loggerFactory;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CommandLineSwitchServer.Options.PipeName = ControlPipe.Name(config);
        CommandLineSwitchServer.Options.LoggerFactory = loggerFactory;

        // command lines carry connection strings and tokens; the TCP transport is
        // documented as having no security whatsoever, so it stays disabled
        CommandLineSwitchServer.Options.Advanced.UnsecuredPort = 0;

        // the library defaults to ASCII, which silently replaces non-ASCII characters
        // with question marks and would corrupt paths and command lines
        CommandLineSwitchServer.Options.Advanced.Encoding = Encoding.UTF8;

        // without this the library forcibly exits the process if the listener ever
        // faults, which would take metrics collection down with the control channel
        CommandLineSwitchServer.Options.Advanced.AutoRestartServer = true;

        return CommandLineSwitchServer.StartServer(HandleSwitches, stoppingToken);
    }

    // Runs on the pipe listener thread. Commands are added by the features that need
    // them; the transport itself understands none.
    private string HandleSwitches(string[] args)
        => args.Length == 0
            ? "No command was provided."
            : $"Unrecognized command: {args[0]}";
}
