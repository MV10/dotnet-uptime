
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MV10.DotnetUptime;

/// <summary>
/// Surfaces OpenTelemetry SDK problems as log entries. Export failures (endpoint
/// unreachable, timeouts, HTTP errors) never throw into Uptime's code — the SDK
/// swallows them internally and reports on its own EventSources — so without this
/// a collector that stops accepting data looks exactly like a healthy service.
/// </summary>
public sealed class OtelDiagnosticsListener : EventListener, IHostedService
{
    private const string OtelEventSourcePrefix = "OpenTelemetry";

    // events that are expected in Uptime's configuration and carry no diagnostic value.
    // MetricInstrumentIgnored fires for every instrument in Uptime's own process that the
    // MeterProvider does not subscribe to (System.Runtime, System.Net.Http, and so on),
    // which is by design; the two ProviderNotRegistered events are expected because
    // Uptime exports metrics only.
    private static readonly HashSet<string> IgnoredEvents = new(StringComparer.Ordinal)
    {
        "MetricInstrumentIgnored",
        "TracerProviderNotRegistered",
        "LoggerProviderNotRegistered"
    };

    // an endpoint that stays down would otherwise log an export failure, with a full
    // stack trace, on every export interval for as long as the service runs
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, DateTime> lastLoggedUtc = new(StringComparer.Ordinal);

    private ILogger<OtelDiagnosticsListener> logger;

    // OnEventSourceCreated fires during the base constructor, before any field
    // assignment here, so sources seen that early are recorded and enabled once
    // the logger exists
    private readonly List<EventSource> pendingSources = new();
    private readonly object syncLock = new();

    public OtelDiagnosticsListener(ILogger<OtelDiagnosticsListener> logger)
    {
        lock (syncLock)
        {
            this.logger = logger;
            foreach (var source in pendingSources) EnableOtelEvents(source);
            pendingSources.Clear();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        base.OnEventSourceCreated(eventSource);

        if (eventSource.Name is null || !eventSource.Name.StartsWith(OtelEventSourcePrefix, StringComparison.Ordinal))
            return;

        lock (syncLock)
        {
            if (logger is null)
            {
                pendingSources.Add(eventSource);
                return;
            }
        }

        EnableOtelEvents(eventSource);
    }

    // only Warning and above; the SDK's informational events are extremely chatty
    // and would dominate the log of a service monitoring hundreds of processes
    private void EnableOtelEvents(EventSource eventSource)
        => EnableEvents(eventSource, EventLevel.Warning);

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (logger is null) return;
        if (eventData.EventName is not null && IgnoredEvents.Contains(eventData.EventName)) return;
        if (!ShouldLog(eventData.EventName)) return;

        var level = eventData.Level switch
        {
            EventLevel.Critical => LogLevel.Critical,
            EventLevel.Error => LogLevel.Error,
            EventLevel.Warning => LogLevel.Warning,
            _ => LogLevel.Information
        };

        if (!logger.IsEnabled(level)) return;

        logger.Log(level, "{Source}/{EventName}: {Message}",
            eventData.EventSource?.Name, eventData.EventName, FormatMessage(eventData));
    }

    /// <summary>
    /// Rate-limits each distinct event so a persistently failing exporter reports
    /// periodically rather than on every export interval.
    /// </summary>
    private bool ShouldLog(string eventName)
    {
        var key = eventName ?? string.Empty;
        var now = DateTime.UtcNow;

        lock (lastLoggedUtc)
        {
            if (lastLoggedUtc.TryGetValue(key, out var previous) && now - previous < RepeatInterval)
                return false;

            lastLoggedUtc[key] = now;
            return true;
        }
    }

    /// <summary>
    /// EventSource messages are format strings whose arguments arrive separately.
    /// A malformed pair must not take down the listener, so formatting failures
    /// fall back to the raw message.
    /// </summary>
    private static string FormatMessage(EventWrittenEventArgs eventData)
    {
        if (string.IsNullOrEmpty(eventData.Message))
            return eventData.EventName ?? "(no message)";

        try
        {
            return eventData.Payload is null or { Count: 0 }
                ? eventData.Message
                : string.Format(eventData.Message, eventData.Payload.ToArray());
        }
        catch (FormatException)
        {
            return eventData.Message;
        }
    }
}
