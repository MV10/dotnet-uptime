# dotnet-uptime

> Work In Progress

Uptime is a .NET telemetry collection and broadcast utility supporting enterprise-style stability-focused monitoring, alerting, and triage for all .NET processes running on a given host. It runs as a Windows Service or a Linux service (systemd or SysV Init). MacOS is not supported. It is similar to a continuously-running version of the standard [`dotnet-counters`](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters) utility with output pushed to [OpenTelemetry](https://opentelemetry.io/) (aka "OTel") endpoints (OTLP or Prometheus HTTP). The service continuously scans for new "eligible" processes, and various name and command line pattern-matching rules control which processes are actually monitored. Several interactive features are available for testing and experimenting. The standard OTel Collector can be used to store data locally to provide high-resolution logging for detailed incident triage activities.

This service only supports processes running under .NET 8 or newer. For a list of available metrics, refer to Microsoft's [Built-in Metrics in .NET](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics) documentation. Anything listed under "Older Metrics" are legacy and are not supported by Uptime. 

## Usage

### Interactive Commands

The following commands can be run interactively from a console, over SSH, etc:

```
Usage: dotnet-uptime [command]

Commands:
  (none)        Run as a service (normal operation)
  list          Show eligible .NET processes with full details
  procs         Show eligible .NET processes (PID and command line only)
  <PID>         Monitor a single process (console + OTel output)
  version       Show program version
  help          Show this help message
```

### Install as Windows Service

(TBD)

### Install as Linux Service (systemd)

(TBD)

### Install as Linux Service (SysV Init)

(TBD)

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

List entries consist of the executable filename and an optional specifier. If a specifier is needed, add a colon then the specifier regex (using [.NET regex syntax](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference), and leading/trailing whitespace is ignored) which must produce a valid Specifier result. (This means Linux applications with a colon in the app name can't be referenced, but this is unlikely to be a problem). For example, this will avoid reporting metrics from the IIS w3wp.exe instance hosting DefaultAppPool:

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
