using System.Reflection;
using OpenTelemetry.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace MV10.DotnetUptime;

class Program
{
    // the entrypoint assembly name of the service, used to find it from another instance
    private const string ServiceAssemblyName = "dotnet-uptime";

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

            case "stats":
                return ShowStats();

            case "summary":
                return ShowSummary();

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

        using var loggerFactory = BeginInteractive(verbose ? "list" : "procs");

        var procs = new Dictionary<int, DiagnosticProcess>();
        var handler = new ProcessDiscovery(loggerFactory.CreateLogger<ProcessDiscovery>());

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

        using var loggerFactory = BeginInteractive($"monitor {pid}");

        // OTLP export stays at the configured interval; only the console collection
        // rate is sped up below, so capture the configured value first
        int otlpExportIntervalMs = config.App.DiagnosticsIntervalMs;

        // interactive monitoring always collects on a 1-second interval for responsive
        // console output, regardless of the configured (service-oriented) interval
        config.App.DiagnosticsIntervalMs = 1000;

        Console.WriteLine($"Monitoring PID {pid} ({runtimeInfo.ManagedEntrypointAssemblyName}), press Ctrl+C to stop.");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();

        // a running service already monitors this process and exports the same series with
        // the same attributes, so exporting from here as well would only duplicate it
        using var meterProvider = TryBuildMeterProvider(config, otlpExportIntervalMs);
        using var otelCallback = meterProvider is null ? null : new OtelMetricsCallback();

        var callbacks = new List<IMetricsCallback> { new ConsoleMetricsCallback() };
        if (otelCallback is not null) callbacks.Add(otelCallback);
        // the monitored process exiting ends the session; cancel so we exit
        // instead of blocking on Ctrl+C
        callbacks.Add(new SessionEndedCallback(() => cts.Cancel()));
        var callback = new CompositeMetricsCallback(callbacks.ToArray());

        // [processtags] needs the discovered process, which GetProcessInfo does not
        // supply (no filename, path or command line), so look it up once at startup
        IReadOnlyList<KeyValuePair<string, string>> processTags = Array.Empty<KeyValuePair<string, string>>();
        if (config.ProcessTagNames.Count > 0)
        {
            var discovered = new Dictionary<int, DiagnosticProcess>();
            new ProcessDiscovery(loggerFactory.CreateLogger<ProcessDiscovery>()).Discover(discovered);
            if (discovered.TryGetValue(pid, out var proc))
                processTags = ProcessTagBuilder.Build(proc, config.ProcessTagNames, config.App.RedactPayload);
        }

        // interactive single-PID monitoring: pass null so [diags] process filters are
        // ignored and every configured provider applies to the chosen process
        using var session = new MetricsSession(pid, null, callback, config, processTags,
            loggerFactory.CreateLogger<MetricsSession>());

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

    /// <summary>
    /// Builds the exporters for interactive monitoring, or returns null when they must be
    /// skipped. Console output is the point of interactive monitoring, so anything that
    /// prevents exporting is reported and stepped around rather than being fatal.
    /// </summary>
    static MeterProvider TryBuildMeterProvider(ConfigParser config, int otlpExportIntervalMs)
    {
        if (ControlPipe.IsServiceRunning(config))
        {
            Console.WriteLine("A dotnet-uptime service is running on this host, which already exports");
            Console.WriteLine("metrics for this process. Console output only; nothing is exported from");
            Console.WriteLine("this session, which would otherwise duplicate the service's data.");
            Console.WriteLine();
            return null;
        }

        try
        {
            return OtelConfiguration.BuildMeterProvider(config, otlpExportIntervalMs);
        }
        catch (Exception ex)
        {
            // the service may be running somewhere this process cannot see it, most often
            // an elevated service whose control pipe an unprivileged caller cannot reach,
            // in which case a configured [http] endpoint is already holding its port
            Console.WriteLine($"Metrics export is unavailable: {ex.Message}");
            Console.WriteLine("Continuing with console output only.");
            Console.WriteLine();
            return null;
        }
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

        // an empty tag value would silently mislabel every metric this host exports
        if (config.UnresolvedEnvVars.Count > 0)
        {
            Console.Error.WriteLine("Config error: [hosttags] references environment variables that are not set:");
            foreach (var name in config.UnresolvedEnvVars.Distinct())
                Console.Error.WriteLine($"  {name}");
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

        // two instances would both discover every process and export identical series to
        // the same endpoints: counters double, gauges fight, and nothing reports an error
        if (ControlPipe.IsServiceRunning(config))
        {
            Console.Error.WriteLine("dotnet-uptime is already running as a service.");
            return 1;
        }

        // done before the host is built so a failure is a clean refusal to start rather
        // than a background service fault after the rest of the service is running
        if (!ControlPipe.TryPrepareDirectory(config, out var pipeError))
        {
            Console.Error.WriteLine($"Control pipe error: {pipeError}");
            return 1;
        }

        // created here rather than resolved from DI because the exporter wrapper needs
        // it while the service collection is still being configured
        var selfMetrics = new SelfMetrics();

        var host = Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .UseSystemd()
            .ConfigureLogging(logging => logging.SetMinimumLevel(config.App.MinimumLogLevel))
            .ConfigureServices(services =>
            {
                services.AddSingleton(config);
                services.AddSingleton(selfMetrics);
                // constructed by DI so it receives an ILogger, and so the host
                // disposes it (and its Meter) on shutdown
                services.AddSingleton<IMetricsCallback, OtelMetricsCallback>();
                services.AddSingleton<ProcessManager>();
                // registered as a hosted service so the host constructs it at startup
                // (it begins listening in its constructor) and disposes it on shutdown
                services.AddHostedService<OtelDiagnosticsListener>();
                services.AddHostedService<ProcessScannerService>();
                services.AddHostedService<ControlPipeService>();
                OtelConfiguration.ConfigureOpenTelemetry(services, config, config.App.DiagnosticsIntervalMs, selfMetrics);
            })
            .Build();

        host.Run();
        return 0;
    }

    /// <summary>
    /// Streams the running service's own metrics to the console. Connects to the
    /// service over EventPipe exactly as single-PID monitoring does, filtered to
    /// Uptime's self-metrics meter. Process inventory is the `summary` command's job.
    /// </summary>
    static int ShowStats()
    {
        using var loggerFactory = BeginInteractive("stats");

        var procs = new Dictionary<int, DiagnosticProcess>();
        new ProcessDiscovery(loggerFactory.CreateLogger<ProcessDiscovery>()).Discover(procs);

        // exactly one service instance is the supported deployment, so the service is
        // the single Uptime instance that is not this process. Identify it by entrypoint
        // assembly rather than filename, which is "dotnet" for a framework-dependent run.
        var service = procs.Values.FirstOrDefault(p =>
            p.PID != Environment.ProcessId
            && string.Equals(p.ManagedEntrypointAssemblyName, ServiceAssemblyName, StringComparison.OrdinalIgnoreCase));

        if (service is null)
        {
            Console.WriteLine("No running dotnet-uptime service was found.");
            return 1;
        }

        // a synthetic config: only the self-metrics meter, collected once per second
        var config = ConfigParser.Parse(new[] { "[diags]", SelfMetrics.MeterName });
        config.App.DiagnosticsIntervalMs = 1000;

        Console.WriteLine($"Reading service metrics from PID {service.PID}, press Ctrl+C to stop.");
        Console.WriteLine("Values update once per discovery pass, so they may repeat between passes.");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();

        var callback = new CompositeMetricsCallback(
            new ConsoleMetricsCallback(),
            new SessionEndedCallback(() => cts.Cancel()));

        // null filename so the [diags] process filters do not apply
        using var session = new MetricsSession(service.PID, null, callback, config, null,
            loggerFactory.CreateLogger<MetricsSession>());

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
            // Ctrl+C, or the service stopped
        }

        session.Stop();
        Console.WriteLine("Stopped.");
        return 0;
    }

    /// <summary>
    /// Asks the running service for a snapshot of its state: monitored processes and their
    /// session state, uptime, last scan duration, and export counts. Command lines in the
    /// reply are redacted by the service before they cross the pipe.
    /// </summary>
    static int ShowSummary()
    {
        ConfigParser config;
        try
        {
            config = ConfigParser.Load();
        }
        catch (ConfigMissingException)
        {
            // the pipe name derives from [app] summarycommand; with no config the default
            // (disabled) posture is correct, which is what an empty config yields
            config = ConfigParser.Parse(Array.Empty<string>());
        }
        catch (ConfigException ex)
        {
            Console.WriteLine($"Config error: {ex.Message}");
            return 1;
        }

        using var loggerFactory = BeginInteractive("summary");

        // the caller-side elevation check for summarycommand=elevated. On Linux the root-only
        // pipe already enforces this; on Windows it is the only guard, and a guardrail rather
        // than a boundary. The service refuses the command again regardless.
        if (config.App.SummaryCommand == SummaryCommandMode.Elevated && !Environment.IsPrivilegedProcess)
        {
            Console.Error.WriteLine("The summary command requires an elevated caller (summarycommand=elevated).");
            return 1;
        }

        if (!ControlPipe.TrySend(config, new[] { "summary" }, out var response))
        {
            Console.WriteLine("No running dotnet-uptime service was found.");
            return 1;
        }

        Console.WriteLine(response);
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
        Console.WriteLine($"  pscan            {config.App.ProcessScanIntervalMs}");
        Console.WriteLine($"  diags            {config.App.DiagnosticsIntervalMs}");
        Console.WriteLine($"  maxhistograms    {config.App.MaxHistograms}");
        Console.WriteLine($"  maxtimeseries    {config.App.MaxTimeSeries}");
        Console.WriteLine($"  loglevel         {config.App.MinimumLogLevel}");
        Console.WriteLine($"  summarycommand   {config.App.SummaryCommand.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  redactpayload    {config.App.RedactPayload.ToString().ToLowerInvariant()}");

        // derived rather than configured, but the summarycommand setting moves it
        Console.WriteLine($"  control pipe     {ControlPipe.Name(config)}");

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
        if (config.ProcessTagNames.Count == 0)
        {
            Console.WriteLine("Process tags: none");
        }
        else
        {
            Console.WriteLine("Process tags:");
            foreach (var name in config.ProcessTagNames)
                Console.WriteLine($"  {name} -> {ProcessTagBuilder.AttributeName(name)}");
        }

        Console.WriteLine();
        if (config.HostTags.Count == 0)
        {
            Console.WriteLine("Host tags: none (baseline host.name and os.type are always emitted)");
        }
        else
        {
            Console.WriteLine("Host tags:");
            foreach (var tag in config.HostTags)
                Console.WriteLine($"  {tag.Key} = {tag.Value}");
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

        // an unset variable may simply mean `validate` is running as a different user
        // than the service will, so this is a warning here and fatal at service start
        if (config.UnresolvedEnvVars.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("WARNING: [hosttags] references environment variables that are not set:");
            foreach (var name in config.UnresolvedEnvVars.Distinct())
                Console.WriteLine($"  {name}");
            Console.WriteLine("Service mode will refuse to start until they are set.");
        }

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

    /// <summary>
    /// Builds a console logger for an interactive command and records an audit entry naming
    /// the command and the invoking user. Log output goes to stderr so piped command results
    /// on stdout stay clean. The audit entry is Information, so it appears only at
    /// loglevel=information or lower; the level is read from uptime.conf when present.
    /// </summary>
    static ILoggerFactory BeginInteractive(string command)
    {
        var level = LogLevel.Warning;
        try
        {
            level = ConfigParser.Load().App.MinimumLogLevel;
        }
        catch
        {
            // no config, or one that will be reported by the command itself: interactive
            // logging just falls back to the default level rather than failing here
        }

        var factory = LoggerFactory.Create(builder =>
        {
            builder
                .AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                })
                .SetMinimumLevel(level);

            // route all log output to stderr so piped command results on stdout stay clean
            builder.Services.Configure<ConsoleLoggerOptions>(
                options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        });

        factory.CreateLogger("Interactive").LogInformation(
            "Command '{Command}' invoked by user '{User}' (process {Pid}).",
            command, Environment.UserName, Environment.ProcessId);

        return factory;
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
  stats         Show the running service's own operational metrics
  summary       Show a snapshot of the running service's monitored processes
  validate      Check uptime.conf and show the effective settings
  version       Show program version
  help          Show this help message");
    }
}
