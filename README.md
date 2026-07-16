# dotnet-uptime

> Work In Progress

Uptime is a .NET telemetry collection and broadcast utility intended for stability-focused monitoring and alerting, as one finds in an enterprise environment. It runs as a Windows or Linux service (either under systemd or as a simple SysV init-scripted process). MacOS is not supported. It is essentially a continuously-running version of the standard [`dotnet-counters`](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters) utility with output made available as an [OpenTelemetry](https://opentelemetry.io/) (aka "OTel") endpoint. Each polling cycle (configurable, but 15 seconds by default) scans for new "eligible" processes, then retrieves all of the requested data from all targeted processes.

## Configuration

A simple text file named `uptime.conf` is in the same directory as the executable. It defines polling frequencies, OTel endpoint details, process include/exclude specifications, diagnostics sources and required metrics. Lines or any trailing content prefixed by a hash symbol (`#`) are treated as comments and disregarded. Settings are grouped into `[sections]` exclusively containing either `key=value` pairs or lists of values. Blank lines are ignored and leading/trailing whitespace is ignored.

### [app] Config Section

The `[app]` section contains settings that control overall application behavior.

| Setting | Default | Description                                       |
|---------|---|---------------------------------------------------|
| `pscan` | 15000 | Polling rate for eligible processes, milliseconds |
| `diags` | 15000 | Retrieval rate for diagnostic data, milliseconds  | 
| (TBD)   | (TBD) | TODO                                              |

### [include] and [exclude] Config Sections

The `[include]` and `[exclude]` sections are mutually exclusive and define which processes Uptime will monitor. When `[include]` is used, only matching processes are monitored and all others are ignored. When `[exclude]` is used, all eligible process are monitored except those matching anything in the list.

List entries consist of the executable filename and an optional specifier. If a specifier is needed, add a colon then the specifier regex (using .NET regex syntax, leading/trailing whitespace ignored) which must produce a valid Specifier result. (This means Linux applications with a colon in the app name can't be reference, which is unlikely to be a problem). For example, this will avoid reporting metrics from the IIS DefaultAppPool instance:

```
[exclude]
w3wp.exe: -ap """"(?<Specifier>DefaultAppPool)""""
```

### [diags] Config Section

The `[diags]` section lists diagnostics sources from which data is to be collected, and optionally includes or excludes processes from collection for each entry.

Details about these entries are TBD.
