

using Microsoft.Extensions.Logging;

namespace MV10.DotnetUptime;

/// <summary>
/// Coordinates process tracking and their MetricsSessions.
/// </summary>
public class ProcessManager
{
    private readonly Dictionary<int, ManagedProcess> processes = new();
    private readonly Dictionary<int, DiagnosticProcess> knownProcesses = new();
    private readonly ProcessDiscovery discovery = new();
    private readonly ConfigParser config;
    private readonly IMetricsCallback metricsCallback;
    private readonly ILogger<ProcessManager> logger;
    private readonly ILogger sessionLogger;
    private readonly object syncLock = new();

    public ProcessManager(ConfigParser config, IMetricsCallback callback, ILoggerFactory loggerFactory = null)
    {
        this.config = config;
        metricsCallback = callback;
        logger = loggerFactory?.CreateLogger<ProcessManager>();
        sessionLogger = loggerFactory?.CreateLogger<MetricsSession>();
    }

    public void ScanAndReconcile()
    {
        lock (syncLock)
        {
            var (added, removed) = discovery.Discover(
                knownProcesses,
                config.Rules.Count > 0 ? config.Rules : null,
                config.RuleType);

            // exclude self if configured
            if (config.App.ExcludeSelf)
            {
                var selfPid = Environment.ProcessId;
                if (knownProcesses.ContainsKey(selfPid))
                {
                    knownProcesses.Remove(selfPid);
                    added = added.Where(p => p.PID != selfPid).ToList();
                }
            }

            foreach (var proc in removed)
            {
                StopSession(proc.PID);
            }

            foreach (var proc in added)
            {
                // PID reuse: if we already have a session for this PID with a different cookie, stop it
                if (processes.TryGetValue(proc.PID, out var existing))
                {
                    if (existing.Process.RuntimeInstanceCookie != proc.RuntimeInstanceCookie)
                        StopSession(proc.PID);
                    else
                        continue;
                }

                StartSession(proc);
            }
        }
    }

    private void StartSession(DiagnosticProcess proc)
    {
        // per-process facts are constant for the session, so resolve them once here
        var processTags = ProcessTagBuilder.Build(proc, config.ProcessTagNames);
        var session = new MetricsSession(proc.PID, proc.Filename, metricsCallback, config, processTags, sessionLogger);
        processes[proc.PID] = new ManagedProcess(proc, session);
        session.Start();

        logger?.LogInformation("Added PID {Pid} {CommandLine}", proc.PID, proc.CommandLine);
    }

    private void StopSession(int pid)
    {
        // the command line lives on the tracked process, so read it before removing
        if (processes.Remove(pid, out var managed))
        {
            managed.Session.Dispose();
            logger?.LogInformation("Removed PID {Pid} {CommandLine}", pid, managed.Process.CommandLine);
        }
    }

    public void StopAll()
    {
        lock (syncLock)
        {
            foreach (var kvp in processes)
                kvp.Value.Session.Dispose();
            processes.Clear();
        }
    }

    public IReadOnlyDictionary<int, DiagnosticProcess> KnownProcesses
    {
        get { lock (syncLock) { return new Dictionary<int, DiagnosticProcess>(knownProcesses); } }
    }

    private record ManagedProcess(DiagnosticProcess Process, MetricsSession Session);
}
