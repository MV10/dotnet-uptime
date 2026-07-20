# Process Discovery

This document describes how dotnet-uptime finds monitorable .NET processes and gathers the
information needed to open a diagnostics session against each one. It also explains the one
scenario that is deliberately **not** supported: routed diagnostic ports created by
`dotnet-dsrouter`.

## Background: the .NET diagnostic port

Every modern .NET runtime publishes a **diagnostic port** that external tools use to issue
IPC commands and open EventPipe sessions. The transport is platform-specific:

| Platform | Transport | Default location / name |
| --- | --- | --- |
| Windows | Named pipe | `\\.\pipe\dotnet-diagnostic-{pid}` |
| Linux   | Unix domain socket | `{TMPDIR}/dotnet-diagnostic-{pid}-{disambiguator}-socket` |

The presence of a diagnostic port is what makes a process _eligible_: a process without one
is either not a .NET runtime or has diagnostics disabled, and is skipped. Discovery is
essentially the act of enumerating these ports, mapping each back to a PID, and confirming a
live CLR is on the other end.

## Discovery scenarios

Discovery runs two complementary scans and unions the PIDs they produce. The first covers the
common case; the second exists only on Linux to reach processes the first cannot see.

### 1. Local, same-namespace processes

The local scan enumerates the platform IPC root (`\\.\pipe\` on Windows, the system temp
directory on Linux), matches each entry against the diagnostic-port name pattern, and extracts
the PID from the name. This is the standard case:

- A normal .NET app running directly on the host.
- On Windows, this is the _only_ scan — named pipes are namespace-flat, so every local port is
  visible here. (This _should_ include Windows-hosted containers, but I am unable to test this
  so any reports via repository Issue or PRs to address any problems would be appreciated.)

Each candidate PID is confirmed to still exist before it is accepted.

### 2. Linux `/proc`-based processes

On Linux a second scan walks `/proc` to find processes whose socket does not appear in the
local temp-directory scan. It handles two sub-cases, distinguished by whether the process lives
in a different PID namespace:

- **Containerized (cross-namespace) processes.** When a process reports an `NSpid` with more
  than one entry, it runs in its own PID namespace. Its diagnostic socket lives under the
  container's temp directory — reachable from the host at `/proc/{hostpid}/root/{TMPDIR}` — and
  is named with the process's **namespace** PID (frequently `1`), not the host PID. The scan
  resolves the socket at that cross-namespace path.

- **Same-namespace processes with a non-default `TMPDIR`.** A host process launched with a
  custom `TMPDIR` publishes its socket outside the system temp directory, so the local scan
  misses it. The actual `TMPDIR` is recovered from the process environment, and the socket is
  resolved there.

The namespace PID and the 64-hex-character container ID (read from the process cgroup path,
covering both the cgroupfs and systemd drivers) are carried forward so container-origin metrics
can be tagged with `container.pid` and `container.id`.

## Per-process data collected for diagnostics

Once a candidate PID is identified, discovery gathers everything required to filter the process
and later open a session against it.

### Command line and process identity

The full command line is the primary human-facing identifier and the input to the name/pattern
matching rules. It is retrieved platform-specifically:

- **Linux:** read the process command line from `/proc`, joining the null-separated arguments.
- **Windows:** there is no `/proc` equivalent, so the command line is read out of the target's
  process environment block (PEB) via the native process-query and memory-read APIs. The 32/64-bit
  permutations (WOW64) are handled, and a 64-bit target cannot be read from a 32-bit monitor.

From the command line, the first token (honoring quoted paths) is taken as the executable
pathname, and the filename is its leaf. The filename drives rule matching; an optional
**specifier** (for example, the IIS application pool hosted by a given `w3wp.exe`) is pulled
from the command line via a per-rule regex to disambiguate multiple instances of the same
executable.

### Runtime information (IPC `GetProcessInfo`)

Discovery connects to the diagnostic port and issues the runtime's `GetProcessInfo` command,
negotiating the protocol version down from v3 → v2 → v1. This both **confirms a live CLR** (an
empty runtime-instance cookie means nothing valid answered, and the candidate is rejected) and
returns metadata used to describe the process:

| Field | Meaning | Availability |
| --- | --- | --- |
| Runtime instance cookie | Unique per CLR instance; distinguishes PID reuse | v1+ |
| Process architecture | e.g. `x64`, `arm64` | v1+ |
| Managed entrypoint assembly name | e.g. `MyWebApp` | v2+ |
| CLR product version | May include prerelease labels | v2+ |
| Portable runtime identifier | e.g. `linux-x64`; empty before .NET 9 | v3 (version ≥ 1) |

The IPC framing (the `DOTNET_IPC_V1` magic, the fixed-size header, and the length-prefixed
UTF-16LE strings) follows the diagnostic IPC protocol documented in the `dotnet/diagnostics`
repository.

### Socket path for the EventPipe session (Linux)

Confirming eligibility and opening the metrics session are two different connections, and on
Linux the second one cannot rely on connecting by PID alone. Resolving a socket from the
**host** PID under the **host** temp directory fails for containerized targets, so the socket
path is resolved explicitly — applying the same `TMPDIR` and cross-namespace logic used during
discovery — and the session connects to that path in **connect** mode.

Connect mode is required. Absent it, the connection defaults to **listen** mode, in which the
monitor would wait for the runtime to connect to _it_; connect mode makes the monitor connect
to the runtime's existing socket. On Windows the named-pipe client is used directly.

## Unsupported: routed diagnostic ports (dsrouter)

`dotnet-dsrouter` is a Microsoft-supplied proxy that bridges scenarios where a diagnostics tool
cannot reach a runtime's native diagnostic port directly. Its primary purpose is **mobile
platforms** — Android, iOS, and similar targets — where the runtime and the tooling sit on
different sides of a device or network boundary and are connected over TCP.

dsrouter works by _reversing_ the connection model. Instead of the monitor connecting to a
socket or pipe the runtime already published, dsrouter (or the runtime, configured with
`DOTNET_DiagnosticPorts`) establishes a listening endpoint, and the two sides connect over TCP.
The runtime effectively calls out to the router.

dotnet-uptime does not support this. Its entire discovery model assumes a **connect**-mode
port that the runtime has already published locally:

- It discovers processes by enumerating on-disk named pipes and Unix sockets — a routed TCP
  endpoint leaves nothing to enumerate.
- It always connects _to_ the target and never listens for a runtime to call in, so it cannot
  participate in the reverse handshake.
- It maps every port back to a host PID via the filesystem and `/proc`; a routed port has no
  such local PID mapping.

Because of this, targets reachable only through a routed diagnostic port — principally the
mobile platforms dsrouter exists to serve — will not be found.
