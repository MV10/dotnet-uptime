# Testing

Locally testing Uptime is easy. The first portion of these instructions are for Linux but Windows users should read this, it is nearly identical for Windows (details are provided towards the end).

At a high level, you will download a small Collector application, add simple config files to both the Collector and Uptime, start the Collector, use Uptime to find a .NET PID running on your system (you must provide that app), and start Uptime pointing to that PID. The Collector will begin streaming all metrics from that app to the console.

## Local push-telemetry test (OTLP → OpenTelemetry Collector)

This scenario demonstrates Uptime _pushing_ OLTP payloads to an endpoint (the Collector).

This is not a demonstration of the dev-oriented Prometheus listener, which is a _pull_ model.

### 1. Get the collector

Download and extract the `otelcol-contrib` archive. You will store this in your home directory in `bin/otel-collector`.

On Linux it is a standalone binary — no service manager or root needed. Note the version numbers (0.109.0) embedded twice in the URL.

```bash
mkdir -p ~/bin/otel-collector && cd ~/bin/otel-collector
curl -sL https://github.com/open-telemetry/opentelemetry-collector-releases/releases/download/v0.109.0/otelcol-contrib_0.109.0_linux_amd64.tar.gz | tar xz otelcol-contrib
```

### 2. Collector config

Create `~/bin/otel-collector/otel-local.yaml`. YAML is whitespace-sensitive and rejects tabs; indent with spaces exactly as shown, or the collector will fail to
start with `expected a map, got 'slice'` errors.

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 127.0.0.1:4317
      http:
        endpoint: 127.0.0.1:4318

exporters:
  debug:
    verbosity: detailed

service:
  pipelines:
    metrics:
      receivers: [otlp]
      exporters: [debug]
```

Validate before running (catches indentation mistakes):

```bash
cd ~/bin/otel-collector
./otelcol-contrib validate --config ./otel-local.yaml
```

### 3. App config

Put the Uptime build into the directory of your choice. The configuration file goes into the same directory as the application (which generally simplifies enterprise-style versioning and distribution). Create `uptime.conf` with this content:

```ini
# pushes System.Runtime counters to a local OTel Collector over OTLP/gRPC
[otlp]
local

[local]
endpoint=http://127.0.0.1:4317
protocol=grpc
```

`[otlp]` lists the active target names; `[local]` is an arbitrary label whose section defines holds the OTel Collector's endpoint. `protocol=grpc` selects `OtlpExportProtocol.Grpc`. (To test the HTTP receiver instead, use `endpoint=http://127.0.0.1:4318` and `protocol=http`.)

The default `[diags]` provider is `System.Runtime` (which is every possible built-in .NET metric), so no `[diags]` section is needed.

### 4. Run the test

Start the collector in one terminal:

```bash
cd ~/bin/otel-collector
./otelcol-contrib --config ./otel-local.yaml
```

Start the target .NET process you want to monitor.

In another terminal, find the PID of the target .NET process (use `dotnet-uptime procs` to list eligible ones), then start Uptime pointing to that PID:

```bash
cd <path-to-Uptime>
./dotnet-uptime <PID>
```

Counter payloads print on the Uptime console **and** appear in the Collector's terminal. Seeing them in the Collector confirms the push path end to end.

Metrics are generally emitted only when there is activity. Some apps will be completely silent unless you're actively using them, so if you're not getting any output, interact with the target application, or try a different one.


## Windows equivalent

The same scenario works on Windows with the Windows Collector build; only the paths and shell differ. This still uses the interactive monitor, so no service
installation is required.

### 1. Get the collector

Download `otelcol-contrib_<ver>_windows_amd64.tar.gz` from the same [releases page](https://github.com/open-telemetry/opentelemetry-collector-releases/releases) and extract `otelcol-contrib.exe`, e.g. to `C:\otel-collector\`.

### 2. Collector config

Save the _identical_ `otel-local.yaml` content shown above to `C:\otel-collector\otel-local.yaml`.

The YAML content is platform-independent (`127.0.0.1` loopback, same ports). Validate:

```powershell
cd C:\otel-collector
otelcol-contrib validate --config otel-local.yaml
```

### 3. App config

Put the Uptime build into the directory of your choice. Just like Linux, the configuration file goes into the same directory. The contents are _identical_ to the `uptime.conf` shown above.

### 4. Run the test

From a command / console / terminal window:

```powershell
cd C:\otel-collector
otelcol-contrib --config otel-local.yaml
```

Start the target .NET process you want to monitor.

In another terminal, find the PID of the target .NET process (use `dotnet-uptime procs` to list eligible ones), then start Uptime pointing to that PID:

```powershell
cd <path-to-Uptime>
dotnet-uptime <PID>
```

As on Linux, pushed counters appear in the Collector's console.
