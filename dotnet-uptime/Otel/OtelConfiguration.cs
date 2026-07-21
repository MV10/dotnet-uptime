using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace MV10.DotnetUptime;

/// <summary>
/// Builds OpenTelemetry metrics export from ConfigParser: named OTLP push exporters
/// and the optional Prometheus scrape listener, for both the service host (DI) and
/// the standalone PID-monitor mode.
/// </summary>
static class OtelConfiguration
{
    /// <summary>
    /// Registers OTel metrics on the host service collection (service mode).
    /// No-op when neither an OTLP target nor an HTTP endpoint is configured.
    /// </summary>
    public static void ConfigureOpenTelemetry(IServiceCollection services, ConfigParser config,
        int exportIntervalMs, SelfMetrics selfMetrics)
    {
        var hasOtlp = config.OtlpTargetNames.Count > 0;
        var hasHttp = config.HttpEndpoint is not null;
        if (!hasOtlp && !hasHttp) return;

        services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(BuildResource(config));
                metrics.AddMeter(OtelMetricsCallback.MeterName);
                ConfigureOtlpExporters(metrics, config, exportIntervalMs, selfMetrics);
                ConfigurePrometheus(metrics, config);
            });
    }

    /// <summary>
    /// Builds a standalone MeterProvider (PID-monitor mode).
    /// </summary>
    public static MeterProvider BuildMeterProvider(ConfigParser config, int exportIntervalMs,
        SelfMetrics selfMetrics = null)
    {
        var builder = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(BuildResource(config))
            .AddMeter(OtelMetricsCallback.MeterName);

        ConfigureOtlpExporters(builder, config, exportIntervalMs, selfMetrics);
        ConfigurePrometheus(builder, config);

        return builder.Build();
    }

    /// <summary>
    /// Builds the OTel resource describing this host. Baseline attributes identifying
    /// the machine are always present because metrics with no host attribution are
    /// useless; [hosttags] entries are layered on top and may override them.
    /// </summary>
    private static ResourceBuilder BuildResource(ConfigParser config)
    {
        var attributes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["host.name"] = Environment.MachineName,
            ["os.type"] = HostTagResolver.OperatingSystemName()
        };

        foreach (var tag in config.HostTags)
            attributes[tag.Key] = tag.Value;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        return ResourceBuilder.CreateEmpty()
            .AddService(serviceName: "dotnet-uptime", serviceVersion: version)
            .AddAttributes(attributes);
    }

    /// <summary>
    /// Registers each named OTLP push exporter. The export interval is set to match
    /// the counter collection interval so every collected value is pushed exactly
    /// once (values are last-value gauges, so pushing more or less often than we
    /// collect would re-send stale values or drop collected ones).
    /// </summary>
    private static void ConfigureOtlpExporters(MeterProviderBuilder metrics, ConfigParser config,
        int exportIntervalMs, SelfMetrics selfMetrics)
    {
        foreach (var name in config.OtlpTargetNames)
        {
            var endpoint = config.OtlpEndpoints[name];

            var options = new OtlpExporterOptions
            {
                Endpoint = new Uri(endpoint.Endpoint),
                Protocol = endpoint.Protocol == "http"
                    ? OtlpExportProtocol.HttpProtobuf
                    : OtlpExportProtocol.Grpc,
                TimeoutMilliseconds = endpoint.TimeoutMs
            };

            var headers = endpoint.GetHeaders();
            if (headers.Count > 0)
                options.Headers = string.Join(",", headers.Select(h => $"{h.Key}={h.Value}"));

            // the exporter is built by hand rather than through AddOtlpExporter so it can
            // be wrapped for timing; the SDK exposes no export duration of its own
            var exporter = new TimedMetricExporter(new OtlpMetricExporter(options), name, selfMetrics);

            // the export interval matches the counter collection interval so every
            // collected value is pushed exactly once
            metrics.AddReader(new PeriodicExportingMetricReader(exporter, exportIntervalMs)
            {
                TemporalityPreference = MetricReaderTemporalityPreference.Cumulative
            });
        }
    }

    private static void ConfigurePrometheus(MeterProviderBuilder metrics, ConfigParser config)
    {
        if (config.HttpEndpoint is null) return;

        var uri = new Uri(config.HttpEndpoint.Endpoint);
        metrics.AddPrometheusHttpListener(options =>
        {
            options.Host = uri.Host;
            options.Port = uri.Port;
        });
    }
}
