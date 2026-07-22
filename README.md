# dotnet-uptime

Uptime is a cross-platform .NET diagnostics collection and telemetry utility supporting enterprise-style stability-focused observability, monitoring, alerting, and triage for _all_ .NET processes running on a given host. It runs as a Windows Service or a Linux service (systemd or SysV Init). MacOS is not supported. It is similar to a continuously-running version of the standard [`dotnet-counters`](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters) utility with output available to [OpenTelemetry](https://opentelemetry.io/) (aka "OTel") OTLP collectors.

Critically, diagnostic metrics can be collected from every .NET application without making _any_ changes to the monitored applications, including vendor or other third-party applications where you usually can't obtain the source code. Without Uptime, each and every individual application would need to deal with a variety of complex issues to expose the same data. Uptime is designed to be an easily-configured fire-and-forget service that can be distributed broadly to many thousands of servers.

The service continuously scans for new "eligible" processes, and various name and command line pattern-matching rules control which processes are actually monitored. Several interactive features are available for testing and experimenting. It supports containers. On Windows these are simply named-pipes. For Linux, run it on the underlying host, metrics are tagged with the host PID, the 64-character container ID, and the PID inside the container. Routed diagnostic ports (via `dotnet-dsrouter`) are not supported, they provide TCP bridging to expose data from mobile platforms (iOS, Android, etc), which are not supported by Uptime. For local developer scenarios, output via Prometheus HTTP is also supported.

Uptime only supports processes running under .NET 8 or newer. For a list of available metrics, refer to Microsoft's [Built-in Metrics in .NET](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics) documentation. Anything listed under "Older Metrics" is legacy. Older `EventCounter` metrics are also supported (they are obsolete as of .NET 6, but haven't been fully replaced yet), but .NET Framework PerfMon counters are not supported by Uptime. 

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
  <PID>         Monitor a single process (OTel output + 1 per second console output)
  list          Show eligible .NET processes with full details
  procs         Show eligible .NET processes (PID and command line only)
  stats         Show the running service's own operational metrics
  validate      Check uptime.conf and show the effective settings
  version       Show program version
  help          Show this help message
```

Only one service instance may run at a time. Starting a second one exits immediately with `dotnet-uptime is already running as a service` rather than starting, because two instances would each discover every process and export identical data to the same endpoints, doubling counters and producing conflicting gauge values without reporting any error. This applies only to service mode; the interactive commands are designed to run alongside a service.

Interactive PID monitoring behaves slightly differently while a service is running. Because the service already collects and exports that process, the interactive session prints to the console but does **not** export, which would otherwise duplicate the service's data. A message says so at startup. With no service running, interactive monitoring exports normally, so it can be used on its own to push metrics without installing a service.

Invoking the program without a command runs in service mode. On Windows the program will inherit your account's permissions. If your account does not have elevated permissions (usually this means Administrator), the program will not be able to monitor any elevated processes. This is probably fine for testing, but for normal use, see below to correctly install the program as a dedicated Windows Service, where it will run with elevated rights. No such concerns apply to usage on Linux.

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
| `loglevel` | warning | Minimum log level: `trace`, `debug`, `information`, `warning`, `error`, `critical`, or `none` |
| `elevatedsummary` | false | Restrict the `summary` command to elevated callers (see below) |

Note that the `diags` counter collection interval is _also_ the interval at which data is pushed to OTLP collectors. When running in interactive mode monitoring a specific PID (console output), the rate is always 1 second, but any configured OTLP collection will continue at the configured rate. Collectors are expected to downsample to whatever data they actually wish to process and store.

The `elevatedsummary` setting secures access to the `summary` command because it reports the command lines of monitored processes, which routinely carry passwords, tokens and connection strings. Unlike `list` and `procs`, which run their own discovery under the calling user's privileges, `summary` asks the running service, so an unprivileged caller would be borrowing the service's view of every process on the host. (On Linux, it is secured at the OS level. On Windows, it is only secured internally by Uptime; a malicious application could still connect to Uptime's named pipe and read process command lines.)

### [include] and [exclude] Config Sections

The `[include]` and `[exclude]` sections are mutually exclusive and define which processes Uptime will monitor. When `[include]` is used, only matching processes are monitored and all others are ignored. When `[exclude]` is used, all eligible process are monitored except those matching anything in the list.

A list entry matches a process by _either_ its executable filename _or_ its managed entrypoint assembly name. Neither alone identifies every .NET process scenario. A native host such as `w3wp.exe` has no entrypoint assembly, so it can only be named by filename. Conversely, every framework-dependent application launched as `dotnet myapp.dll` has the filename `dotnet`, so filename is unreliable and only the assembly name (`myapp`) identifies it (an espeically common case on Linux). For self-contained and platform-specific builds the two are the same name, so either works.

**Matching is case-insensitive.** When a process could match two different rules, the one naming the entrypoint assembly wins because it is more specific.

```
[include]
w3wp.exe        # a native host, matched by filename
myapp           # "dotnet myapp.dll", matched by entrypoint assembly
```

List entries consist of that name and an optional specifier. If a specifier is needed, add a colon then the specifier regex (using [.NET regex syntax](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference), and leading/trailing whitespace is ignored) which must produce a valid Specifier result. (This means Linux applications with a colon in the app name can't be referenced, but this is not commonly a problem.)

For example, the following will avoid reporting metrics from the IIS w3wp.exe instance hosting DefaultAppPool:

```
[exclude]
w3wp.exe: -ap """"(?<Specifier>DefaultAppPool)""""
```

**Uptime is not special-cased.** It is an ordinary .NET process with a diagnostic port, so with no rule naming it, Uptime monitors itself and any other running Uptime instance, including a short-lived `stats` or `summary` invocation. The sample configuration therefore ships with:

```
[exclude]
dotnet-uptime
```

Because `dotnet-uptime` is both the executable filename and the entrypoint assembly name, that single entry covers every instance and every launch scenario. Delete it to collect standard runtime metrics (memory, GC, thread pool) about Uptime itself, which is a reasonable thing to want at enterprise-scale. It is opt-in rather than automatic because it adds roughly sixty series per host.

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

Wildcards are convenient but not free: a wildcard subscribes to _all_ meters in the target process, and non-matching data is discarded after collection rather than being filtered at the source. Prefer explicit provider names where the set is known. The per-provider process filter (the executable name after the colon) is the best way to bound this cost, since it limits a wildcard to only the processes that need it:

```
System.Net.*: w3wp.exe                       # every System.Net meter, only from w3wp.exe
```

The process filter matches either the executable filename or the managed entrypoint assembly name, the same way `[include]`/`[exclude]` entries do, so it works for applications launched as `dotnet myapp.dll` where every filename is `dotnet`.

The process filter is enforced only during service-mode scanning, where Uptime chooses which discovered processes get which providers. Interactive single-PID monitoring (`dotnet-uptime <PID>`) ignores process filters and applies every configured provider to the chosen process, since you have already selected exactly one target.

#### Uptime's own metrics

Uptime publishes metrics about its own operation — processes monitored, discovery pass duration, export success and timing — on the `dotnet-uptime.self` meter. These are documented in [`stats_metrics.md`](https://github.com/MV10/dotnet-uptime/blob/master/stats_metrics.md).

They are a custom meter like any other, so they are collected only if listed here, and only if Uptime is not excluded by the process rules. Because the sample configuration excludes Uptime by default, **these metrics are not exported unless you opt in**:

```
[diags]
System.Runtime
dotnet-uptime.self: dotnet-uptime
```

The process filter scopes the self meter to Uptime alone, leaving other monitored applications unaffected. The `dotnet-uptime` entry must also be removed from `[exclude]` for any of it to be collected. (The `stats` command needs none of this. It reads the meter directly from the running service, so it works regardless of configuration.)

### [processtags] Config Section

The `[processtags]` section lists facts about each monitored process to emit as tags on every metric it produces. Uptime always tags metrics with the PID, but a PID is recycled and carries no meaning across restarts, so without at least `assembly` or `filename` there is no way to tell which application a series belongs to.

| Name | Emitted as | Value |
|---|---|---|
| `assembly` | `process.assembly` | Managed entrypoint assembly name |
| `filename` | `process.executable.name` | Executable filename |
| `pathname` | `process.executable.path` | Full executable path |
| `commandline` | `process.command_line` | Full command line, including arguments |
| `clrversion` | `process.runtime.version` | CLR product version |
| `arch` | `process.architecture` | Process architecture (`x64`, `arm64`, etc) |
| `rid` | `process.runtime.rid` | Portable runtime identifier (.NET 9 and newer only) |
| `cookie` | `process.runtime.cookie` | Runtime instance ID, which distinguishes PID reuse |
| `specifier` | `process.specifier` | Value captured by an `[include]`/`[exclude]` rule regex |

```ini
[processtags]
assembly
clrversion
```

> **`commandline` exposes secrets.** Command lines routinely contain connection strings, API tokens, and passwords. Do not enable it when exporting to a third-party backend unless that backend has sanitization rules in place.

Each tag must be listed explicitly. Wildcards are deliberately not supported: these values become part of every series identity, so adding one is a change that should be made on purpose rather than inherited silently from a future version that knows about more facts.

These tags cost nothing in series cardinality. They are constant per process and the PID tag is already present, so they widen each existing series rather than creating new ones. Values that are unavailable on a given target (e.g. no RID before .NET 9) are omitted rather than exported blank.

Where a monitored application publishes a tag whose name collides with one of Uptime's, Uptime's value wins and the application's is dropped. If the two values differ, a warning is logged once per process.

> It is important to understand that Uptime is not "transparent" to OpenTelemetry Collector endpoints. In the OTel data model, every export batch carries one **Resource**: a set of attributes describing the entity that produced the telemetry. Backends generally treat the Resource's `service.name` as the primary grouping key, and it is what populates their service list. Every batch Uptime sends carries a single Resource identifying **Uptime itself** (`service.name=dotnet-uptime`, plus `host.name` and anything from `[hosttags]`). The monitored applications are identified by attributes on each data point, not by the Resource. That means a backend shows one service called `dotnet-uptime` per host, with metrics from every monitored application _inside_ it.
>
> This is intentional:
>
> - It avoids identity collisions. An application that already exports its own OpenTelemetry metrics uses its own `service.name`. If Uptime also claimed that name, the backend would see two independent producers writing overlapping metrics for one service, at different intervals.
>
> - You can always tell which metrics arrived via Uptime and which the application published itself.
>
> If you would rather see one service per monitored application, many Collector implementations can regroup the data on the fly (or something like `otelcol-contrib` can be configured as an intermediate pipeline Collector), but those configuration details are beyond the scope of this documentation.

### [hosttags] Config Section

Where `[processtags]` describes each monitored process, `[hosttags]` describes the host they run on. These are static `key=value` pairs you define, constant for the whole Uptime instance, and they are emitted as OTel **resource attributes** rather than as tags on each measurement — OTLP transmits resource attributes once per export batch instead of once per data point, which matters at fleet scale.

`host.name` and `os.type` are always emitted whether or not this section exists, because metrics with no host attribution cannot be attributed to anything. Listing either name here overrides the built-in value.

```ini
[hosttags]
environment=QA
datacenter=us-east
node=%machinename%-web
```

Values may contain `%token%` substitutions:

| Token | Value |
|---|---|
| `%machinename%` | Machine name |
| `%fqdn%` | Fully-qualified domain name, falling back to the machine name if DNS cannot answer |
| `%osversion%` | OS description string |
| `%osname%` | `windows`, `linux`, or `darwin` |
| `%uptimeversion%` | This application's version |
| `%env:NAME%` | An environment variable |

An unrecognized token is a configuration error. A referenced environment variable that is not set is reported as a warning by `validate` — which may be running as a different user with a different environment — but service mode refuses to start, because an empty tag value would silently mislabel every metric the host exports.

Note that `%env:NAME%` reads the environment of the Uptime process, not of any monitored process. Under systemd that means whatever the Uptime unit file sets, so an application-specific variable such as `ASPNETCORE_ENVIRONMENT` will not resolve unless it is also set for Uptime itself.

### [otlp] Config Section

The `[otlp]` section lists named OpenTelemetry OTLP push targets. Each name corresponds to its own config section with endpoint settings. Data is pushed to all listed targets simultaneously. Section names listed in `[otlp]` must not conflict with built-in section names (`app`, `include`, `exclude`, `diags`, `otlp`, `http`, `processtags`, `hosttags`).

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
