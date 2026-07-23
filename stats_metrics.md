# Self-Monitoring Metrics

Uptime publishes metrics about its own operation on a dedicated `Meter`, separate from the metrics it collects on behalf of monitored applications. 

Because they are published on a real `System.Diagnostics.Metrics.Meter`, they travel two paths without any special handling: the interactive `stats` command which connects to the running service exactly the way `<PID>` monitoring connects to any other process, and out to the configured OTLP targets, but only when the service is actually monitoring itself. Refer to the _Configuration_ documentation in the repository's README for details.

## Meter

| | |
|---|---|
| Meter name | `dotnet-uptime.self` |

This is intentionally differentiated from `dotnet-uptime` which transmits re-emitted metrics from monitored applications (the primary functionality of Uptime).

## Instruments

| Instrument | Type | Unit | Description |
|---|---|---|---|
| `uptime.processes.monitored` | ObservableGauge | `{process}` | Processes currently monitored |
| `uptime.processes.filtered` | ObservableGauge | `{process}` | Discovered processes that are not being monitored |
| `uptime.sessions.active` | ObservableGauge | `{session}` | Sessions actively reading from a monitored process |
| `uptime.sessions.started` | Counter | `{session}` | Sessions started since service start |
| `uptime.sessions.ended` | Counter | `{session}` | Sessions ended normally |
| `uptime.sessions.failed` | Counter | `{session}` | Sessions ended by a connection error |
| `uptime.discovery.duration` | Histogram | `ms` | Time taken by one process-scan pass |
| `uptime.measurements.received` | Counter | `{measurement}` | Counter payloads received from monitored processes |
| `uptime.export.duration` | Histogram | `ms` | Time taken by one export attempt |
| `uptime.export.attempts` | Counter | `{export}` | Export attempts |
| `uptime.export.failures` | Counter | `{failure}` | Export attempts that failed |

### Instrument Notes

- `processes.monitored` and `sessions.active` are normally equal. A gap between them means a session has died but has not yet been reconciled away, indicating something is wrong.
- `processes.filtered` counts processes that were discovered but are not monitored, either because your `[include]`/`[exclude]` rules excluded them or because the diagnostic port answered without reporting a usable runtime. Comparing it to `processes.monitored` shows how much your rules are filtering out.
- `sessions.started` / `ended` / `failed` track ongoing process-handling counts.
- `discovery.duration` indicates how long each process scan takes, which grows with the number of processes on the host.
- `measurements.received` is the clearest indicator that data is actually flowing (rather than merely that sessions exist).
- `export.duration` is the one to watch for capacity. Compare it against the export interval (config `diags` setting): a push taking twelve seconds on a fifteen-second interval is one traffic increase away from falling permanently behind, and nothing else here reveals that.
- `export.attempts` and `export.failures` together give a success ratio per endpoint.

## Semantics

Counters are cumulative and monotonic, resetting only when the service restarts. Rates are the backend's job. A service running for weeks would otherwise need per-interval deltas that lose information whenever a scrape is missed.

That describes what is *exported*. The interactive `stats` command sees something different: the change during each collection interval rather than the running total. A counter not incremented since `stats` connected does not appear at all, and one that stops being incremented reports zero.

Only the export instruments are tagged. The other eight are a single time series each, so self-monitoring costs a fixed amount per host no matter how many processes are monitored. Scaling the monitor's own overhead with its workload would defeat the purpose.

The `export.*` instruments carry a `target` tag naming the `[otlp]` section. With several targets configured there is otherwise no way to tell which endpoint is slow or failing.

Values change at discovery cadence, not collection cadence. The gauges update once per process-scan interval (config `pscan` setting). `stats` requests a one-second refresh and will legitimately display unchanged numbers between passes. This is expected, not a stall.

## Collecting these metrics

Uptime is not special-cased. It is an ordinary .NET process, so its own metrics are collected only when both of the following hold:

1. Uptime is not excluded by the configured rules. The sample `uptime.conf` ships with `[exclude] dotnet-uptime`, so **by default it is excluded** and these metrics are not exported anywhere.
2. `dotnet-uptime.self` is listed in `[diags]`, exactly as any other custom meter must be.

To export them, remove the exclusion and add the meter:

```ini
[diags]
System.Runtime
dotnet-uptime.self: dotnet-uptime
```

The process filter scopes the self meter to Uptime alone, so other monitored applications are unaffected.

Removing the exclusion also collects Uptime's own `System.Runtime` metrics — memory, GC, thread pool — which answers whether the monitor itself is healthy across a fleet. That costs roughly sixty additional series per host, which is why it is opt-in.

The `stats` command needs none of this. It reads the meter directly from the running service, so it works even when the service excludes itself and lists nothing in `[diags]`.

## Relationship to other output

Exported self-metrics arrive under the same resource attributes as everything else Uptime sends — `service.name=dotnet-uptime`, `host.name`, and any `[hosttags]`. The meter name is what separates them.

`stats` shows this meter and nothing else. Process inventory and per-session detail belong to the `summary` command, so `stats` answers "how many processes, and is the service healthy," never "which ones."
