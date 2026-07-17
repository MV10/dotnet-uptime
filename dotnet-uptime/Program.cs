using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MV10.DotnetUptime.Lib;

namespace MV10.DotnetUptime;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            RunService(args);
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "list":
                ListProcesses(verbose: true);
                break;

            case "procs":
                ListProcesses(verbose: false);
                break;

            case "version":
                PrintVersion();
                break;

            case "help":
                PrintHelp();
                break;

            default:
                if (int.TryParse(args[0], out int pid))
                {
                    MonitorProcess(pid);
                }
                else
                {
                    PrintHelp();
                }
                break;
        }
    }

    static void ListProcesses(bool verbose)
    {
        UptimeConfig config = null;
        try
        {
            config = UptimeConfig.Load();
        }
        catch (ConfigException)
        {
            // list/procs work without config (no filtering)
        }

        var procs = new Dictionary<int, DiagnosticProcess>();
        var handler = new ProcessHandler();

        if (config is not null && config.Rules.Count > 0)
            handler.Scan(procs, config.Rules, config.RuleType);
        else
            handler.Scan(procs);

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

    static void MonitorProcess(int pid)
    {
        // validate that the PID is an eligible .NET process
        var runtimeInfo = DiagnosticIpc.GetProcessInfo(pid);
        if (runtimeInfo.RuntimeInstanceCookie == Guid.Empty)
        {
            Console.WriteLine($"PID {pid} is not a running .NET process with a diagnostic port.");
            return;
        }

        UptimeConfig config;
        try
        {
            config = UptimeConfig.Load();
        }
        catch (ConfigException ex)
        {
            Console.WriteLine($"Config error: {ex.Message}");
            return;
        }

        Console.WriteLine($"Monitoring PID {pid} ({runtimeInfo.ManagedEntrypointAssemblyName}), press Ctrl+C to stop.");
        Console.WriteLine();

        var callback = new ConsoleMetricsCallback();

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
    }

    static void RunService(string[] args)
    {
        UptimeConfig config;
        try
        {
            config = UptimeConfig.Load();
        }
        catch (ConfigException ex)
        {
            Console.WriteLine($"Config error: {ex.Message}");
            return;
        }

        var callback = new ConsoleMetricsCallback();

        // TODO: add OTel exporters based on config
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                services.AddSingleton(config);
                services.AddSingleton<IMetricsCallback>(callback);
                services.AddSingleton<ProcessManager>();
                services.AddHostedService<ProcessScannerService>();
            })
            .Build();

        host.Run();
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
        Console.WriteLine($"[{payload.Timestamp:HH:mm:ss}] {pid} {payload.ProviderName}/{payload.CounterName}: {payload.Value:F2} {payload.DisplayUnits}{tags}");
    }

    public void OnSessionEnded(int pid)
    {
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] Session ended for PID {pid}");
    }
}
