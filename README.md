# dotnet-uptime

> Work In Progress

Uptime is a .NET telemetry collection and broadcast utility intended for stability-focused monitoring and alerting, as one finds in an enterprise environment. It runs as a Windows or Linux service (either under systemd or as a simple SysV init-scripted process). MacOS is not supported. It is essentially a continuously-running version of the standard [`dotnet-counters`](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters) utility with output made available as an [OpenTelemetry](https://opentelemetry.io/) (aka "OTel") endpoint. Each polling cycle (configurable, but 15 seconds by default) scans for new "eligible" processes, then retrieves all of the requested data from all targeted processes.

## Configuration

A simple text file named `uptime.conf` is in the same directory as the executable. It defines polling frequencies, OTel endpoint details, process include/exclude specifications, diagnostics sources and required metrics. Lines or any trailing content prefixed by a hash symbol (`#`) are treated as comments and disregarded. Settings are grouped into `[sections]` exclusively containing either `key=value` pairs or lists of values. Blank lines are ignored and leading/trailing whitespace is ignored.

### [app] Config Section

The `[app]` section contains settings that control overall application behavior.

| Setting | Default | Description |
|---------|---------|-------------|
| `pscan` | 15000 | Process scan interval, milliseconds |
| `diags` | 15000 | Counter reporting interval, milliseconds |
| `maxhistograms` | 10 | Max histogram instruments tracked per process |
| `maxtimeseries` | 1000 | Max time series tracked per process |
| `excludeself` | true | When true, `dotnet-uptime` excludes its own PID from monitoring |

### [include] and [exclude] Config Sections

The `[include]` and `[exclude]` sections are mutually exclusive and define which processes Uptime will monitor. When `[include]` is used, only matching processes are monitored and all others are ignored. When `[exclude]` is used, all eligible process are monitored except those matching anything in the list.

List entries consist of the executable filename and an optional specifier. If a specifier is needed, add a colon then the specifier regex (using .NET regex syntax, leading/trailing whitespace ignored) which must produce a valid Specifier result. (This means Linux applications with a colon in the app name can't be reference, which is unlikely to be a problem). For example, this will avoid reporting metrics from the IIS DefaultAppPool instance:

```
[exclude]
w3wp.exe: -ap """"(?<Specifier>DefaultAppPool)""""
```

### [diags] Config Section

The `[diags]` section lists EventPipe providers to collect. Each entry is a provider name, optionally followed by `[counter1,counter2]` to select specific counters (omit brackets for all counters). An optional process filter can follow a colon. If this section is missing or empty, defaults to `System.Runtime` (all counters, all processes).

```
[diags]
System.Runtime                               # all counters, all processes
System.Runtime[cpu-usage,gc-heap-size]       # specific counters, all processes
Microsoft.AspNetCore.Hosting: w3wp.exe       # all counters, only from w3wp.exe
```

### [otlp] Config Section

The `[otlp]` section lists named OpenTelemetry OTLP push targets. Each name corresponds to its own config section with endpoint settings. Data is pushed to all listed targets simultaneously.

```
[otlp]
local-collector
splunk
```

Each named section has these settings:

| Setting | Default | Description |
|---------|---------|-------------|
| `endpoint` | *(required)* | Remote collector URL |
| `protocol` | grpc | Transport: `grpc` or `http` (http/protobuf) |
| `header` | *(none)* | Header(s) in `Key:Value` format; semicolon-delimited for multiple |
| `timeout` | 10000 | Export timeout, milliseconds |

```
[local-collector]
endpoint=http://localhost:4317

[splunk]
endpoint=https://ingest.signalfx.com/v2/datapoint/otlp
header=X-SF-Token:your-token-here
```

Section names listed in `[otlp]` must not conflict with built-in section names (`app`, `include`, `exclude`, `diags`, `otlp`, `http`).

### [http] Config Section

The optional `[http]` section exposes a local OpenTelemetry HTTP listener for scrape-based collection (currently Prometheus). At most one `[http]` section is allowed.

| Setting | Default | Description |
|---------|---------|-------------|
| `type` | prometheus | Scrape format; `prometheus` is the only supported value currently |
| `endpoint` | *(required)* | Local listen URL |

```
[http]
type=prometheus
endpoint=http://localhost:9464
```

OTLP and HTTP endpoints can coexist -- an enterprise deployment might push to Splunk while also exposing a local Prometheus scrape endpoint for Grafana.
