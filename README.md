# dotnet-uptime

Uptime is a cross-platform .NET diagnostics collection and telemetry utility supporting enterprise-style stability-focused observability, monitoring, alerting, and triage for _all_ .NET processes running on a given host. It runs as a Windows Service or a Linux service (systemd or SysV Init). MacOS is not supported. It is similar to a continuously-running version of the standard [`dotnet-counters`](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters) utility with output available to [OpenTelemetry](https://opentelemetry.io/) (aka "OTel") OTLP collectors.

Critically, diagnostic metrics can be collected from every .NET application without making _any_ changes to the monitored applications, including vendor or other third-party applications where you usually can't obtain the source code. Without Uptime, each and every individual application would need to deal with a variety of complex issues to expose the same data. Uptime is designed to be an easily-configured fire-and-forget service that can be distributed broadly to many thousands of servers.

The service continuously scans for new "eligible" processes, and various name and command line pattern-matching rules control which processes are actually monitored. Several interactive features are available for testing and experimenting. It supports containers. On Windows these are simply named-pipes. For Linux, run it on the underlying host, metrics are tagged with the host PID, the 64-character container ID, and the PID inside the container. Routed diagnostic ports (via `dotnet-dsrouter`) are not supported, they provide TCP bridging to expose data from mobile platforms (iOS, Android, etc), which are not supported by Uptime. For local developer scenarios, output via Prometheus HTTP is also supported.

Uptime only supports processes running under .NET 8 or newer. For a list of available metrics, refer to Microsoft's [Built-in Metrics in .NET](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics) documentation. Anything listed under "Older Metrics" is legacy and is not supported by Uptime. 

## Installation and Usage

Each [release](https://github.com/MV10/dotnet-uptime/releases) offers Windows and Linux versions packaged two ways. The "self-contained deployment" (scd) builds are single files which are ready to use as soon as you unarchive the files. The smaller "framework-dependent" (fx) builds require a machine-wide installation of the .NET runtime. Both versions work identically once installed and configured.

The application can be unarchived into to any directory. Although it will run interactively without configuration, running it as a service requires creating a configuration file. Refer to the [_Configuration_](https://github.com/MV10/dotnet-uptime#configuration) section for details.

### Local-Machine Testing

The repository document [`testing.md`](https://github.com/MV10/dotnet-uptime/blob/master/testing.md) explains how to quickly and easily test the application. 

### Interactive Commands

The following commands can be run interactively from a console, over SSH, etc:

```
Usage: dotnet-uptime [command]

Commands:
  (none)        Run as a service (on Windows, permissions limitations may apply)
  list          Show eligible .NET processes with full details
  procs         Show eligible .NET processes (PID and command line only)
  <PID>         Monitor a single process (console + OTel output, 1 second interval)
  version       Show program version
  help          Show this help message
```

Invoking the program without a command runs in service mode. However, on Windows the program will inherit your account's permissions. If your account does not have elevated permissions (usually this means Administrator), the program will not be able to monitor any elevated processes. This is probably fine for testing, but for normal use, see below to correctly install the program as a Windows Service, where it will run with elevated rights. No such concerns apply to usage on Linux.

### Windows Service

> Replace `<install-dir>` with the directory where you deployed the application and configuration files.

From an elevated command prompt, create and start the service (note the required space after each `=`):

```
sc create dotnet-uptime binPath= "<install-dir>\dotnet-uptime.exe" start= delayed-auto
sc start dotnet-uptime
```

To stop and remove it:

```
sc stop dotnet-uptime
sc delete dotnet-uptime
```

### Linux systemd Service

> Replace `<install-dir>` with the directory where you deployed the application and configuration files.

Create a `/etc/systemd/system/dotnet-uptime.service` unit file:

```ini
[Unit]
Description=dotnet-uptime metrics service
After=network.target

[Service]
Type=notify
ExecStart=<install-dir>/dotnet-uptime
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

Then enable and start it:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now dotnet-uptime
```

Check status and logs with `systemctl status dotnet-uptime` and `journalctl -u dotnet-uptime`.

To remove it, run `sudo systemctl disable --now dotnet-uptime` and delete the unit file.

### Linux SysV Init Service

> Replace `<install-dir>` with the directory where you deployed the application and configuration files.

Create `/etc/init.d/dotnet-uptime` and make it executable (`sudo chmod +x /etc/init.d/dotnet-uptime`):

```sh
#!/bin/sh
### BEGIN INIT INFO
# Provides:          dotnet-uptime
# Required-Start:    $network $local_fs
# Required-Stop:     $network $local_fs
# Default-Start:     2 3 4 5
# Default-Stop:      0 1 6
# Short-Description: dotnet-uptime metrics service
### END INIT INFO

DAEMON=<install-dir>/dotnet-uptime
PIDFILE=/var/run/dotnet-uptime.pid

case "$1" in
  start)
    start-stop-daemon --start --background --make-pidfile --pidfile "$PIDFILE" --exec "$DAEMON"
    ;;
  stop)
    start-stop-daemon --stop --pidfile "$PIDFILE" --retry 10
    ;;
  restart)
    "$0" stop
    "$0" start
    ;;
  *)
    echo "Usage: $0 {start|stop|restart}"
    exit 1
    ;;
esac
```

Register it to start at boot with `sudo update-rc.d dotnet-uptime defaults`, then start it with `sudo service dotnet-uptime start`.


## Configuration

You must create a configuration file to run Uptime as a service. (The interactive commands will use defaults if no config is found.)

Configuration is a simple text file named `uptime.conf` in the same directory as the application. The repository has a [sample](https://github.com/MV10/dotnet-uptime/blob/master/dotnet-uptime/uptime.conf) configuration file but this is not packaged with the release files.

Config defines process polling frequency, OTel endpoint details, process include/exclude specifications, diagnostics sources and required metrics. Lines or any trailing content prefixed by a hash symbol (`#`) are treated as comments and disregarded. Settings are grouped into `[sections]` exclusively containing either `key=value` pairs or lists of values. Blank lines are ignored and leading/trailing whitespace is ignored. The application will not start in service mode without a config file, but the interactive commands will work with default values.

### [app] Config Section

The `[app]` section contains settings that control overall application behavior.

| Setting | Default | Description                                                                  |
|---------|---------|---|
| `pscan` | 15000 | Process scan interval, milliseconds |
| `diags` | 15000 | Counter collection interval, milliseconds |
| `maxhistograms` | 10 | Max histogram instruments tracked per process |
| `maxtimeseries` | 1000 | Max time series tracked per process |
| `excludeself` | true | When true, `dotnet-uptime` excludes its own PID from monitoring |

Note that the `diags` counter collection interval is _also_ the interval at which data is pushed to OTLP collectors. When running in interactive mode monitoring a specific PID (console output), the rate is always 1 second, but any configured OTLP collection will continue at the configured rate. Collectors are expected to downsample to whatever data they actually wish to process and store.

### [include] and [exclude] Config Sections

The `[include]` and `[exclude]` sections are mutually exclusive and define which processes Uptime will monitor. When `[include]` is used, only matching processes are monitored and all others are ignored. When `[exclude]` is used, all eligible process are monitored except those matching anything in the list.

List entries consist of the executable filename and an optional specifier. If a specifier is needed, add a colon then the specifier regex (using [.NET regex syntax](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference), and leading/trailing whitespace is ignored) which must produce a valid Specifier result. (This means Linux applications with a colon in the app name can't be referenced, but this is not commonly a problem.)

For example, the following will avoid reporting metrics from the IIS w3wp.exe instance hosting DefaultAppPool:

```
[exclude]
w3wp.exe: -ap """"(?<Specifier>DefaultAppPool)""""
```

### [diags] Config Section

The `[diags]` section lists diagnostics providers to collect. Each entry is a provider name, optionally followed by `[counter1,counter2]` to select specific counters (omit the list to collect all counters). An optional executable name can be added following a colon, which means those metrics are only collected for matching processes. If this section is missing or empty, defaults to `System.Runtime` (all counters for every monitored process).

A provider name may end with `.*` to match a namespace prefix, or be a bare `*` to match every meter. A prefix wildcard also matches the namespace root itself, so `System.Net.*` collects `System.Net` as well as `System.Net.Http`, `System.Net.NameResolution`, and so on. Wildcards apply to modern `System.Diagnostics.Metrics` meters only, not to legacy EventCounter providers, which must be named exactly.

```
[diags]
System.Runtime                               # all counters, all processes
System.Runtime[cpu-usage,gc-heap-size]       # specific counters, all processes
Microsoft.AspNetCore.Hosting: w3wp.exe       # all counters, only from w3wp.exe
System.Net.*                                 # every meter under the System.Net namespace
```

Wildcards are convenient but not free: a wildcard subscribes to _all_ meters in the target process, and non-matching data is discarded after collection rather than being filtered at the source. Prefer explicit provider names where the set is known.

### [otlp] Config Section

The `[otlp]` section lists named OpenTelemetry OTLP push targets. Each name corresponds to its own config section with endpoint settings. Data is pushed to all listed targets simultaneously. Section names listed in `[otlp]` must not conflict with built-in section names (`app`, `include`, `exclude`, `diags`, `otlp`, `http`).

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

Any valid OTLP endpoint can be listed (DataDog, Loki, New Relic, etc.). For example, the standard OTel Collector could be run locally to store data on the server itself as high-resolution logging for detailed incident triage activities, similar to legacy PerfMon BLG files.

### [http] Config Section

The optional `[http]` section exposes an HTTP listener for scrape-based telemetry collection. At most one `[http]` section is allowed. OTLP and HTTP endpoints can coexist -- an enterprise deployment might push to Splunk while also exposing a local Prometheus scrape endpoint for utilities like Grafana.

| Setting | Default | Description |
|---------|---------|-------------|
| `type` | prometheus | Scrape format; `prometheus` is the only supported value currently |
| `endpoint` | *(required)* | Local listen URL |

```
[http]
type=prometheus
endpoint=http://localhost:9464
```

For a Prometheus endpoint (which is the only standard HTTP format defined today), only `localhost` is supported (or equivalents, `127.0.0.1` or `::1`). This endpoint is _not_ secure and is typically only suited for development purposes. The OTel team has expressly stated the built-in HTTP listener will _never_ be suited for production usage. For secure, broadly accessible Prometheus data, a separate OTel collector should be used to expose Prometheus data. That is beyond the scope of the Uptime service.
