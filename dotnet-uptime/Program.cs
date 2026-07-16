using System.Reflection;
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
        // TODO: validate PID, start Generic Host with single MetricsSession
        Console.WriteLine($"Monitor mode for PID {pid} is not yet implemented.");
    }

    static void RunService(string[] args)
    {
        // TODO: start Generic Host with ProcessScannerService
        Console.WriteLine("Service mode is not yet implemented.");
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
