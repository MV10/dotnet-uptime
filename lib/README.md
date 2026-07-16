# dotnet-uptime/lib

Various utility classes used by dotnet-uptime and testable from the demo project.

## ProcessScan

The `ProcessHandler.Scan` method manages an externally-owned `ProcessList` dictionary of `DiagProcess` objects, which represents all processes which are "eligible" for monitoring. A process is eligible if it maintains a .NET diagnostics port. How this works is platform-specific. Diagnostic router connections are not supported at this time.

When `Scan` is invoked, the current process list is validated. The scan modifies the process list dictionary and returns two collections: a read-only list of newly-added processes, and a read-only list of removed processes.

The process dictionary is keyed on PID.

### Windows Scanning

The .NET runtime creates a named pipe at `\\.\pipe\dotnet-diagnostic-{pid}`. Discovery is straightforward: enumerate all named pipes in `\\.\pipe\` and regex-match names against `^dotnet-diagnostic-(\d+)$` to extract the PID, then confirm the process is still alive.

### Linux Scanning

The runtime creates a Unix domain socket at `{TMPDIR}/dotnet-diagnostic-{pid}-{disambiguationKey}-socket` (and `TMPDIR` defaults to `/tmp`). Discovery has two phases:

The local scan has the same pattern-matching approach as Windows, but against socket files in the program's own `/tmp`.

Then a "proc" scan which walks every `/proc/{pid}` directory to catch processes the local scan misses:
   - Reads `/proc/{pid}/environ` to find the target's `TMPDIR` which may differ from `/tmp`.
   - Reads `/proc/{pid}/statu`s for the `NSpid:` line; multiple values mean the process is a container (different PID namespace).
   - Containerized process: searches `/proc/{pid}/root/{TMPDIR}/` using the innermost namespace PID.
   - Different `TMPDIR`, same namespace: searches the target's `TMPDIR` using the host PID.

### Command-Line Extraction

Windows command-line extraction requires P/Invoke calls to Win32 APIs to read the command line directly from the target process's memory via the PEB (Process Environment Block). It uses `NtQueryInformationProcess` to locate the PEB, then `ReadProcessMemory` to read the `UNICODE_STRING` command line from `ProcessParameters`. This handles 32- and 64-bit processes. It cannot read elevated processes, but it is recommended to only run Uptime (and the demo) as Administrator.

Linux extraction simply reads and parses `/proc/{pid}/cmdline`, which the kernel provides as null-separated arguments.
