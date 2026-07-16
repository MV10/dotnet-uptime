# dotnet-uptime

> Work In Progress

Uptime is a .NET telemetry collection and broadcast utility intended for stability-focused monitoring, alerting as you would find in an enterprise environment. It runs as a Windows or Linux service (either under systemd or as a simple SysV init-scripted process). MacOS is not supported. It is essentially a continuously-running version of the standard [`dotnet-counters`](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters) utility with output made available as an [OpenTelemetry](https://opentelemetry.io/) (aka "OTel") endpoint. Each polling cycle (configurable, but 15 seconds by default) scans for new "eligible" processes, then retrieves all of the requested data from all targeted processes.

## Configuration

A simple text file named `uptime.conf` is in the same directory as the executable. It defines polling frequencies, OTel endpoint details, process include/exclude specifications, event sources and required metrics. Lines or any trailing content prefixed by a hash symbol (`#`) are treated as comments and disregarded. Settings are grouped into `[sections]` exclusively containing either `key=value` pairs or lists of values. Blank lines are ignored and leading/trailing whitespace is ignored.

### [app] Config Section

The `[app]` section contains settings that control overall application behavior.

| Setting   | Default | Description |
|---|---|---|
| `pscan`   | 15000 | Polling rate for eligible processes, milliseconds |
| `metrics` | 15000 | Retrieval rate for metrics data, milliseconds | 
| (TBD)     | (TBD) | TODO |

### [processes] Config Section

The `[processes]` section lists processes to include or exclude. By default all processes are included (this section can be omitted or left empty). Each line should start with a `+` to include the process or `-` to exclude the process, followed by the process name. To only include specific processes, add a `-` line with no process name.

Details about the process name (and regex?) are TBD.

### [metrics] Config Section

The `[metrics]` section lists event sources for which metrics data is to be collected, and optionally includes or excludes processes from collection for each entry.

Details about these entries are TBD.
