using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MV10.DotnetUptime.Otel;
using MV10.DotnetUptime.Processes;

namespace MV10.DotnetUptime;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
            return RunService(args);

        switch (args[0].ToLowerInvariant())
        {
            case "list":
                ListProcesses(verbose: true);
                return 0;

            case "procs":
                ListProcesses(verbose: false);
                return 0;

            case "version":
                PrintVersion();
                return 0;

            case "help":
                PrintHelp();
                return 0;

            default:
                if (int.TryParse(args[0], out int pid))
                    return MonitorProcess(pid);

                PrintHelp();
                return 0;
        }
    }

    static void ListProcesses(bool verbose)
    {
        ConfigParser config = null;
        try
        {
            config = ConfigParser.Load();
        }
        catch (ConfigMissingException)
        {
            // list/procs work without config (no filtering)
        }
        catch (ConfigException ex)
        {
            Console.WriteLine($"Config error: {ex.Message}");
            return;
        }

        var procs = new Dictionary<int, DiagnosticProcess>();
        var handler = new ProcessDiscovery();

        if (config is not null && config.Rules.Count > 0)
            handler.Discover(procs, config.Rules, config.RuleType);
        else
            handler.Discover(procs);

        if (procs.Count == 0)
        {
            Console.WriteLine("No eligible .NET processes found.");
            return;
        }

        foreach (var kvp in procs)
        {
            var p = kvp.Value;
            if (verbose)
            {
                Console.WriteLine($"PID={p.PID}  File={p.Filename}");
                Console.WriteLine($"  Cookie={p.RuntimeInstanceCookie}");
                Console.WriteLine($"  Arch={p.ProcessArchitecture}");
                Console.WriteLine($"  Entry={p.ManagedEntrypointAssemblyName}");
                Console.WriteLine($"  CLR={p.ClrProductVersionString}");
                Console.WriteLine($"  RID={p.PortableRuntimeIdentifier}");
                Console.WriteLine($"  Cmd={p.CommandLine}");
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine($"{p.PID}\t{p.CommandLine}");
            }
        }
    }

    static int MonitorProcess(int pid)
    {
        var runtimeInfo = DiagnosticIpc.GetProcessInfo(pid);
        if (runtimeInfo.RuntimeInstanceCookie == Guid.Empty)
        {
            Console.WriteLine($"PID {pid} is not a running .NET process with a diagnostic port.");
            return 1;
        }

        ConfigParser config;
        try
        {
            config = ConfigParser.Load();
        }
        catch (ConfigMissingException)
        {
            // no config file: monitor with built-in defaults (console output, no exporters)
            config = ConfigParser.Parse(Array.Empty<string>());
        }
        catch (ConfigException ex)
        {
            Console.WriteLine($"Config error: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Monitoring PID {pid} ({runtimeInfo.ManagedEntrypointAssemblyName}), press Ctrl+C to stop.");
        Console.WriteLine();

        using var otelCallback = new OtelMetricsCallback();
        var callback = new CompositeMetricsCallback(new ConsoleMetricsCallback(), otelCallback);
        using var meterProvider = OtelConfiguration.BuildMeterProvider(config);

        using var session = new MetricsSession(pid, callback, config);
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        session.Start();

        try
        {
            Task.Delay(Timeout.Infinite, cts.Token).Wait();
        }
        catch (AggregateException ex) when (ex.InnerException is TaskCanceledException)
        {
            // Ctrl+C
        }

        session.Stop();
        Console.WriteLine("Stopped.");
        return 0;
    }

    static int RunService(string[] args)
    {
        ConfigParser config;
        try
        {
            config = ConfigParser.Load();
        }
        catch (ConfigException ex)
        {
            // service mode requires a valid config; exit non-zero so the
            // service manager (systemd / Windows SCM) registers a failure
            Console.Error.WriteLine($"Config error: {ex.Message}");
            Console.Error.WriteLine("A valid uptime.conf is required to run as a service.");
            return 1;
        }

        // service mode collects metrics only to export them; without a destination
        // there is nothing to do, so refuse to start
        if (config.OtlpTargetNames.Count == 0 && config.HttpEndpoint is null)
        {
            Console.Error.WriteLine("Config error: no export endpoints defined.");
            Console.Error.WriteLine("Service mode requires at least one [otlp] target or an [http] section.");
            return 1;
        }

        var otelCallback = new OtelMetricsCallback();

        var host = Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .UseSystemd()
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                services.AddSingleton(config);
                services.AddSingleton<IMetricsCallback>(otelCallback);
                services.AddSingleton<ProcessManager>();
                services.AddHostedService<ProcessScannerService>();
                OtelConfiguration.ConfigureOpenTelemetry(services, config);
            })
            .Build();

        host.Run();
        return 0;
    }

    static void PrintVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Console.WriteLine($"dotnet-uptime {version}");
    }

    static void PrintHelp()
    {
        Console.WriteLine(@"Usage: dotnet-uptime [command]

Commands:
  (none)        Run as a service (normal operation)
  list          Show eligible .NET processes with full details
  procs         Show eligible .NET processes (PID and command line only)
  <PID>         Monitor a single process (console + OTel output)
  version       Show program version
  help          Show this help message");
    }
}

/// <summary>
/// Writes counter payloads to the console.
/// </summary>
class ConsoleMetricsCallback : IMetricsCallback
{
    public void OnCounterPayload(int pid, CounterPayload payload)
    {
        var tags = string.IsNullOrEmpty(payload.Tags) ? "" : $" [{payload.Tags}]";

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
