using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;

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
    public static void ConfigureOpenTelemetry(IServiceCollection services, ConfigParser config, int exportIntervalMs)
    {
        var hasOtlp = config.OtlpTargetNames.Count > 0;
        var hasHttp = config.HttpEndpoint is not null;
        if (!hasOtlp && !hasHttp) return;

        services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(OtelMetricsCallback.MeterName);
                ConfigureOtlpExporters(metrics, config, exportIntervalMs);
                ConfigurePrometheus(metrics, config);
            });
    }

    /// <summary>
    /// Builds a standalone MeterProvider (PID-monitor mode).
    /// </summary>
    public static MeterProvider BuildMeterProvider(ConfigParser config, int exportIntervalMs)
    {
        var builder = Sdk.CreateMeterProviderBuilder()
            .AddMeter(OtelMetricsCallback.MeterName);

        ConfigureOtlpExporters(builder, config, exportIntervalMs);
        ConfigurePrometheus(builder, config);

        return builder.Build();
    }

    /// <summary>
    /// Registers each named OTLP push exporter. The export interval is set to match
    /// the counter collection interval so every collected value is pushed exactly
    /// once (values are last-value gauges, so pushing more or less often than we
    /// collect would re-send stale values or drop collected ones).
    /// </summary>
    private static void ConfigureOtlpExporters(MeterProviderBuilder metrics, ConfigParser config, int exportIntervalMs)
    {
        foreach (var name in config.OtlpTargetNames)
        {
            var endpoint = config.OtlpEndpoints[name];
            metrics.AddOtlpExporter(name, (options, reader) =>
            {
                options.Endpoint = new Uri(endpoint.Endpoint);
                options.Protocol = endpoint.Protocol == "http"
                    ? OtlpExportProtocol.HttpProtobuf
                    : OtlpExportProtocol.Grpc;
                options.TimeoutMilliseconds = endpoint.TimeoutMs;
                var headers = endpoint.GetHeaders();
                if (headers.Count > 0)
                    options.Headers = string.Join(",", headers.Select(h => $"{h.Key}={h.Value}"));

                reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = exportIntervalMs;
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
