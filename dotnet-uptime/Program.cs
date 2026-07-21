using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

namespace MV10.DotnetUptime;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            // On Windows, running with no args outside the SCM (e.g. for testing) is
            // allowed, but such a process only has the launching user's privileges.
            // Attaching to elevated (admin/LocalSystem) targets requires this process
            // to be elevated too, which normally comes from the SCM starting the service.
            if (OperatingSystem.IsWindows() && !WindowsServiceHelpers.IsWindowsService())
            {
                Console.Error.WriteLine("Warning: not started by the Windows Service Control Manager (sc start).");
                Console.Error.WriteLine("Running with the current user's privileges; elevated processes cannot be");
                Console.Error.WriteLine("monitored unless this process is also elevated (run as administrator).");
                Console.Error.WriteLine();
            }

            return RunService(args);
        }

        switch (args[0].ToLowerInvariant())
        {
            case "list":
                ListProcesses(verbose: true);
                return 0;

            case "procs":
                ListProcesses(verbose: false);
                return 0;

            case "validate":
                return ValidateConfig();

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

        // OTLP export stays at the configured interval; only the console collection
        // rate is sped up below, so capture the configured value first
        int otlpExportIntervalMs = config.App.DiagnosticsIntervalMs;

        // interactive monitoring always collects on a 1-second interval for responsive
        // console output, regardless of the configured (service-oriented) interval
        config.App.DiagnosticsIntervalMs = 1000;

        Console.WriteLine($"Monitoring PID {pid} ({runtimeInfo.ManagedEntrypointAssemblyName}), press Ctrl+C to stop.");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();

        using var otelCallback = new OtelMetricsCallback();
        // the monitored process exiting ends the session; cancel so we exit
        // instead of blocking on Ctrl+C
        var callback = new CompositeMetricsCallback(
            new ConsoleMetricsCallback(),
            otelCallback,
            new SessionEndedCallback(() => cts.Cancel()));
        using var meterProvider = OtelConfiguration.BuildMeterProvider(config, otlpExportIntervalMs);

        // interactive single-PID monitoring: pass null so [diags] process filters are
        // ignored and every configured provider applies to the chosen process
        using var session = new MetricsSession(pid, null, callback, config);

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
            // Ctrl+C or the monitored process exited
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
            Console.Error.WriteLine($"Config errors in {ConfigParser.ConfigFilePath}");
            foreach (var error in ex.Errors)
                Console.Error.WriteLine($"  {error}");
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

        var host = Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .UseSystemd()
            .ConfigureLogging(logging => logging.SetMinimumLevel(config.App.MinimumLogLevel))
            .ConfigureServices(services =>
            {
                services.AddSingleton(config);
                // constructed by DI so it receives an ILogger, and so the host
                // disposes it (and its Meter) on shutdown
                services.AddSingleton<IMetricsCallback, OtelMetricsCallback>();
                services.AddSingleton<ProcessManager>();
                services.AddHostedService<ProcessScannerService>();
                OtelConfiguration.ConfigureOpenTelemetry(services, config, config.App.DiagnosticsIntervalMs);
            })
            .Build();

        host.Run();
        return 0;
    }

    /// <summary>
    /// Reports every problem in uptime.conf and echoes the effective settings.
    /// Returns non-zero when the file is missing or invalid so deployment
    /// scripts can gate on the result.
    /// </summary>
    static int ValidateConfig()
    {
        var path = ConfigParser.ConfigFilePath;
        Console.WriteLine($"Config file: {path}");
        Console.WriteLine();

        ConfigParser config;
        try
        {
            config = ConfigParser.Load();
        }
        catch (ConfigException ex)
        {
            Console.WriteLine($"INVALID - {ex.Errors.Count} error(s) found:");
            foreach (var error in ex.Errors)
                Console.WriteLine($"  {error}");
            return 1;
        }

        Console.WriteLine("VALID");
        Console.WriteLine();
        Console.WriteLine("Effective settings (defaults shown where unspecified):");
        Console.WriteLine($"  pscan          {config.App.ProcessScanIntervalMs}");
        Console.WriteLine($"  diags          {config.App.DiagnosticsIntervalMs}");
        Console.WriteLine($"  maxhistograms  {config.App.MaxHistograms}");
        Console.WriteLine($"  maxtimeseries  {config.App.MaxTimeSeries}");
        Console.WriteLine($"  excludeself    {config.App.ExcludeSelf}");
        Console.WriteLine($"  loglevel       {config.App.MinimumLogLevel}");

        Console.WriteLine();
        if (config.Rules.Count == 0)
        {
            Console.WriteLine("Process rules: none (all eligible processes are monitored)");
        }
        else
        {
            Console.WriteLine($"Process rules: {config.RuleType.ToString().ToLowerInvariant()}");
            foreach (var rule in config.Rules.Values)
                Console.WriteLine($"  {rule.Filename}{(rule.SpecifierRegex is null ? "" : $": {rule.SpecifierRegex}")}");
        }

        Console.WriteLine();
        Console.WriteLine("Diagnostic providers:");
        foreach (var provider in config.DiagProviders)
        {
            var counters = provider.Counters is null ? "" : $"[{string.Join(",", provider.Counters)}]";
            var filter = string.IsNullOrEmpty(provider.ProcessFilter) ? "" : $": {provider.ProcessFilter}";
            Console.WriteLine($"  {provider.ProviderName}{counters}{filter}");
        }

        Console.WriteLine();
        if (config.OtlpTargetNames.Count == 0)
        {
            Console.WriteLine("OTLP targets: none");
        }
        else
        {
            Console.WriteLine("OTLP targets:");
            foreach (var name in config.OtlpTargetNames)
            {
                var endpoint = config.OtlpEndpoints[name];
                Console.WriteLine($"  {name}: {endpoint.Endpoint} ({endpoint.Protocol}, timeout {endpoint.TimeoutMs}ms)");
            }
        }

        Console.WriteLine(config.HttpEndpoint is null
            ? "HTTP endpoint: none"
            : $"HTTP endpoint: {config.HttpEndpoint.Endpoint} ({config.HttpEndpoint.Type})");

        // not an error: list, procs and single-PID monitoring are all usable
        // without an export target, but service mode refuses to start
        if (config.OtlpTargetNames.Count == 0 && config.HttpEndpoint is null)
        {
            Console.WriteLine();
            Console.WriteLine("WARNING: no export endpoints defined. Interactive commands will work,");
            Console.WriteLine("but service mode requires at least one [otlp] target or an [http] section.");
        }

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
  (none)        Run as a service (on Windows, permissions limitations may apply)
  <PID>         Monitor a single process (OTel output + 1 per second console output)
  list          Show eligible .NET processes with full details
  procs         Show eligible .NET processes (PID and command line only)
  validate      Check uptime.conf and show the effective settings
  version       Show program version
  help          Show this help message");
    }
}
