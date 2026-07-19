
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace MV10.DotnetUptime.Processes;

/// <summary>
/// Manages a long-lived EventPipe connection to a single process, streaming counter events.
/// Handles both legacy EventCounters and modern System.Diagnostics.Metrics events with
/// deduplication for .NET 8+ runtimes that emit both.
/// </summary>
public class MetricsSession : IDisposable
{
    private const string MetricsProviderName = "System.Diagnostics.Metrics";

    private readonly int pid;
    private readonly int? containerPid;
    private readonly string containerId;
    private readonly IMetricsCallback callback;
    private readonly List<DiagProviderSpec> providers;
    private readonly int intervalSeconds;
    private readonly int maxHistograms;
    private readonly int maxTimeSeries;
    private readonly CancellationTokenSource cts = new();
    private Task processingTask;
    private EventPipeSession session;
    private bool disposed;

    // deduplication: providers that have sent Meter events supersede EventCounters
    private readonly ConcurrentDictionary<string, bool> meterProviders = new(StringComparer.OrdinalIgnoreCase);

    public int PID => pid;
    public bool IsRunning => processingTask is not null && !processingTask.IsCompleted;

    public MetricsSession(int pid, IMetricsCallback callback, ConfigParser config)
    {
        this.pid = pid;
        // a differing namespace PID means the process runs in a container
        if (ProcessDiscovery.TryGetNamespacePid(pid, out int nsPid))
            containerPid = nsPid;
        containerId = ProcessDiscovery.GetContainerId(pid);
        this.callback = callback;
        providers = config.DiagProviders;
        intervalSeconds = config.App.DiagnosticsIntervalMs / 1000;
        if (intervalSeconds < 1) intervalSeconds = 1;
        maxHistograms = config.App.MaxHistograms;
        maxTimeSeries = config.App.MaxTimeSeries;
    }

    public void Start()
    {
        if (processingTask is not null) return;
        processingTask = Task.Factory.StartNew(Run, TaskCreationOptions.LongRunning);
    }

    public void Stop()
    {
        cts.Cancel();
        try { session?.Stop(); } catch { }
    }

    public async Task StopAsync()
    {
        Stop();
        if (processingTask is not null)
        {
            try { await processingTask.ConfigureAwait(false); } catch { }
        }
    }

    private void Run()
    {
        try
        {
            var client = CreateClient();

            var pipeProviders = BuildEventPipeProviders(client);
            session = client.StartEventPipeSession(pipeProviders, requestRundown: false);

            using var source = new EventPipeEventSource(session.EventStream);

            source.Dynamic.All += OnTraceEvent;

            source.Process();
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            // expected shutdown
        }
        catch (Exception)
        {
            // process exited or IPC failure
        }
        finally
        {
            callback.OnSessionEnded(pid);
        }
    }

    /// <summary>
    /// Builds a DiagnosticsClient for the target process. On Linux the diagnostic
    /// socket is resolved explicitly so processes in a container (whose socket lives
    /// under the container's /tmp and is named with the namespace PID) can be reached;
    /// the library's pid constructor only looks for a host-PID socket in the host /tmp.
    /// </summary>
    private DiagnosticsClient CreateClient()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new DiagnosticsClient(pid);

        var socketPath = DiagnosticIpc.FindDiagnosticSocket(pid);
        if (socketPath is null)
            throw new FileNotFoundException($"No diagnostic socket found for PID {pid}");

        // ",connect" is required: a bare path parses as Listen mode, which would
        // wait for the runtime to connect to us instead of connecting to its socket
        return DiagnosticsClientConnector
            .FromDiagnosticPort($"{socketPath},connect", cts.Token)
            .GetAwaiter().GetResult()
            .Instance;
    }

    private List<EventPipeProvider> BuildEventPipeProviders(DiagnosticsClient client)
    {
        var pipeProviders = new List<EventPipeProvider>();
        var meterNames = new List<string>();

        foreach (var spec in providers)
        {
            // every provider gets an EventCounters subscription (legacy path)
            pipeProviders.Add(new EventPipeProvider(
                spec.ProviderName,
                EventLevel.Informational,
                (long)ClrTraceEventParser.Keywords.None,
                new Dictionary<string, string>
                {
                    ["EventCounterIntervalSec"] = intervalSeconds.ToString(CultureInfo.InvariantCulture)
                }));

            meterNames.Add(spec.ProviderName);
        }

        // add the System.Diagnostics.Metrics provider for modern meters
        // shared sessions available on .NET 8+; always request it
        var sessionId = "SHARED";
        var clientId = Guid.NewGuid().ToString();

        const long timeSeriesValuesKeyword = 0x2;
        pipeProviders.Add(new EventPipeProvider(
            MetricsProviderName,
            EventLevel.Informational,
            timeSeriesValuesKeyword,
            new Dictionary<string, string>
            {
                ["SessionId"] = sessionId,
                ["Metrics"] = string.Join(',', meterNames),
                ["RefreshInterval"] = intervalSeconds.ToString(CultureInfo.InvariantCulture),
                ["MaxTimeSeries"] = maxTimeSeries.ToString(CultureInfo.InvariantCulture),
                ["MaxHistograms"] = maxHistograms.ToString(CultureInfo.InvariantCulture),
                ["ClientId"] = clientId
            }));

        return pipeProviders;
    }

    private void OnTraceEvent(TraceEvent traceEvent)
    {
        if (cts.IsCancellationRequested) return;

        try
        {
            if (traceEvent.EventName == "EventCounters")
            {
                HandleEventCounters(traceEvent);
            }
            else if (traceEvent.ProviderName == MetricsProviderName)
            {
                HandleMeterEvent(traceEvent);
            }
        }
        catch { }
    }

    /// <summary>
    /// Handles legacy EventCounters events. Suppressed for providers that have
    /// started emitting modern Meter events (deduplication for .NET 8+).
    /// </summary>
    private void HandleEventCounters(TraceEvent traceEvent)
    {
        // if this provider has already sent Meter events, skip the legacy path
        if (meterProviders.ContainsKey(traceEvent.ProviderName)) return;

        var payloadVal = (IDictionary<string, object>)traceEvent.PayloadValue(0);
        var fields = (IDictionary<string, object>)payloadVal["Payload"];

        var counterName = fields["Name"].ToString();
        var displayName = fields["DisplayName"].ToString();
        var displayUnits = fields["DisplayUnits"].ToString();

        double value;
        CounterKind kind;
        if (fields["CounterType"].Equals("Mean"))
        {
            value = (double)fields["Mean"];
            kind = CounterKind.Gauge;
        }
        else
        {
            value = (double)fields["Increment"];
            kind = CounterKind.Rate;
        }

        if (!IsCounterIncluded(traceEvent.ProviderName, counterName)) return;

        callback.OnCounterPayload(pid, new CounterPayload
        {
            ProviderName = traceEvent.ProviderName,
            CounterName = counterName,
            DisplayName = displayName,
            DisplayUnits = displayUnits,
            Value = value,
            Timestamp = traceEvent.TimeStamp,
            Kind = kind,
            ContainerPID = containerPid,
            ContainerID = containerId
        });
    }

    /// <summary>
    /// Handles modern System.Diagnostics.Metrics events (Gauge, CounterRate, UpDownCounter, Histogram).
    /// </summary>
    private void HandleMeterEvent(TraceEvent traceEvent)
    {
        switch (traceEvent.EventName)
        {
            case "GaugeValuePublished":
                HandleMeterValue(traceEvent, CounterKind.Gauge, valueIndex: 6, tagsIndex: 5);
                break;

            case "CounterRateValuePublished":
                HandleMeterValue(traceEvent, CounterKind.Rate, valueIndex: 6, tagsIndex: 5);
                break;

            case "UpDownCounterRateValuePublished":
                HandleMeterValue(traceEvent, CounterKind.Rate, valueIndex: 6, tagsIndex: 5);
                break;

            case "HistogramValuePublished":
                // payload index 6 is Quantiles (string), 5 is tags
                HandleHistogramValue(traceEvent);
                break;
        }
    }

    private void HandleMeterValue(TraceEvent traceEvent, CounterKind kind, int valueIndex, int tagsIndex)
    {
        var meterName = (string)traceEvent.PayloadValue(1);
        var instrumentName = (string)traceEvent.PayloadValue(3);

        if (!IsCounterIncluded(meterName, instrumentName)) return;

        meterProviders[meterName] = true;

        var tags = (string)traceEvent.PayloadValue(tagsIndex);
        var valueText = (string)traceEvent.PayloadValue(valueIndex);

        if (!double.TryParse(valueText, NumberStyles.Number | NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return;

        callback.OnCounterPayload(pid, new CounterPayload
        {
            ProviderName = meterName,
            CounterName = instrumentName,
            DisplayName = instrumentName,
            DisplayUnits = string.Empty,
            Value = value,
            Timestamp = traceEvent.TimeStamp,
            Tags = tags,
            Kind = kind,
            ContainerPID = containerPid,
            ContainerID = containerId
        });
    }

    private void HandleHistogramValue(TraceEvent traceEvent)
    {
        var meterName = (string)traceEvent.PayloadValue(1);
        var instrumentName = (string)traceEvent.PayloadValue(3);

        if (!IsCounterIncluded(meterName, instrumentName)) return;

        meterProviders[meterName] = true;

        var tags = (string)traceEvent.PayloadValue(5);
        var quantilesText = (string)traceEvent.PayloadValue(6);

        // quantiles format: "50=1.23;95=4.56;99=7.89"
        if (string.IsNullOrEmpty(quantilesText)) return;

        foreach (var pair in quantilesText.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex <= 0) continue;
            var pctText = pair.Substring(0, eqIndex);
            var valText = pair.Substring(eqIndex + 1);
            if (!double.TryParse(valText, NumberStyles.Number | NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                continue;

            var pctTag = string.IsNullOrEmpty(tags) ? $"Percentile={pctText}" : $"{tags},Percentile={pctText}";

            callback.OnCounterPayload(pid, new CounterPayload
            {
                ProviderName = meterName,
                CounterName = instrumentName,
                DisplayName = instrumentName,
                DisplayUnits = string.Empty,
                Value = val,
                Timestamp = traceEvent.TimeStamp,
                Tags = pctTag,
                Kind = CounterKind.Gauge,
                ContainerPID = containerPid,
                ContainerID = containerId
            });
        }
    }

    private bool IsCounterIncluded(string providerName, string counterName)
    {
        foreach (var spec in providers)
        {
            if (!string.Equals(spec.ProviderName, providerName, StringComparison.OrdinalIgnoreCase))
                continue;

            // process filter is checked at session creation, not here
            if (spec.Counters is null || spec.Counters.Length == 0)
                return true;

            foreach (var c in spec.Counters)
            {
                if (string.Equals(c, counterName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        return false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cts.Cancel();
        try { session?.Stop(); } catch { }
        session?.Dispose();
        cts.Dispose();
    }
}
