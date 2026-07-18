
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MV10.DotnetUptime.Processes;

/// <summary>
/// A helper class for finding and managing processes with a .NET diagnostics port.
/// </summary>
public class ProcessHandler
{
    private static readonly string IpcRootPath =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\\.\pipe\" : Path.GetTempPath();

    private static readonly string DiagnosticsPortPattern =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? @"^dotnet-diagnostic-(\d+)$"
            : @"^dotnet-diagnostic-(\d+)-(\d+)-socket$";

    // the 64-hex-character container ID Docker/containerd embed in cgroup paths
    private static readonly Regex ContainerIdPattern = new(@"[0-9a-f]{64}", RegexOptions.Compiled);

    /// <summary>
    /// Finds processes exposing a .NET diagnostic port and updates the externally-managed list
    /// of known processes. Returns read-only lists of processes that were added or removed.
    /// </summary>
    public (IReadOnlyList<DiagnosticProcess> AddedProcesses, IReadOnlyList<DiagnosticProcess> RemovedProcesses) Scan(Dictionary<int, DiagnosticProcess> knownProcesses, IReadOnlyDictionary<string, ProcessRule> rules = null, ProcessRuleType ruleType = default)
    {
        if (knownProcesses is null) throw new ArgumentNullException(nameof(knownProcesses));

        var discoveredPids = new HashSet<int>();

        foreach (int pid in GetLocalPublishedProcesses())
            discoveredPids.Add(pid);

        foreach (int pid in GetProcPublishedProcesses())
            discoveredPids.Add(pid);

        var added = new List<DiagnosticProcess>();
        var removed = new List<DiagnosticProcess>();
        var now = DateTime.UtcNow;

        foreach (int pid in discoveredPids)
        {
            if (knownProcesses.ContainsKey(pid)) continue;

            var commandLine = GetCommandLine(pid);
            if (string.IsNullOrEmpty(commandLine)) continue;

            var pathname = ExtractPathname(commandLine);
            var filename = Path.GetFileName(pathname);

            var specifier = string.Empty;
            if (rules is not null)
            {
                if (rules.TryGetValue(filename, out ProcessRule rule))
                {
                    specifier = rule.FindSpecifier(commandLine);
                    var matches = rule.SpecifierRegex is null || !string.IsNullOrEmpty(specifier);
                    // skip in Exclude mode if everything does match
                    // skip in Include mode if anything doesn't match
                    if (matches && ruleType == ProcessRuleType.Exclude) continue;
                    if (!matches && ruleType == ProcessRuleType.Include) continue;
                }
                else
                {
                    // for Exclude mode, no rule implies inclusion
                    // for Include mode, every process has to be identified by a rule
                    if (ruleType == ProcessRuleType.Include) continue;
                }
            }

            var runtimeInfo = DiagnosticIpc.GetProcessInfo(pid);

            // no valid CLR runtime on the other end of the diagnostic port
            if (runtimeInfo.RuntimeInstanceCookie == Guid.Empty) continue;

            var proc = new DiagnosticProcess(
                pid, pathname, filename, specifier, commandLine, now,
                runtimeInfo.RuntimeInstanceCookie,
                runtimeInfo.ProcessArchitecture,
                runtimeInfo.ManagedEntrypointAssemblyName,
                runtimeInfo.ClrProductVersionString,
                runtimeInfo.PortableRuntimeIdentifier);
            knownProcesses[pid] = proc;
            added.Add(proc);
        }

        foreach (var kvp in knownProcesses)
        {
            if (!discoveredPids.Contains(kvp.Key))
                removed.Add(kvp.Value);
        }

        foreach (var proc in removed)
            knownProcesses.Remove(proc.PID);

        return (added, removed);
    }

    /// <summary>
    /// Discovers .NET processes with diagnostic sockets in the local IPC root path.
    /// </summary>
    private static List<int> GetLocalPublishedProcesses()
    {
        var pids = new List<int>();

        string[] files;
        try
        {
            files = Directory.GetFiles(IpcRootPath);
        }
        catch (UnauthorizedAccessException)
        {
            return pids;
        }

        foreach (var port in files)
        {
            var fileName = new FileInfo(port).Name;
            Match match = Regex.Match(fileName, DiagnosticsPortPattern);
            if (!match.Success) continue;

            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId)) continue;

            if (!ProcessExists(processId)) continue;

            pids.Add(processId);
        }

        return pids;
    }

    /// <summary>
    /// Discovers .NET processes via /proc that aren't found by the local scan.
    /// Finds cross-namespace processes and same-namespace processes with different TMPDIR.
    /// </summary>
    private static List<int> GetProcPublishedProcesses()
    {
        var pids = new List<int>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return pids;

        IEnumerable<string> procEntries;
        try
        {
            procEntries = Directory.EnumerateDirectories("/proc");
        }
        catch (UnauthorizedAccessException)
        {
            return pids;
        }

        foreach (var procEntry in procEntries)
        {
            if (!int.TryParse(Path.GetFileName(procEntry), NumberStyles.Integer, CultureInfo.InvariantCulture, out int hostPid)) continue;

            if (!ProcessExists(hostPid)) continue;

            var targetTmpDir = GetProcessTmpDir(hostPid);

            if (TryGetNamespacePid(hostPid, out int nsPid))
            {
                var crossNsDir = Path.Combine($"/proc/{hostPid}/root", targetTmpDir.TrimStart(Path.DirectorySeparatorChar));
                if (TryResolveAddress(crossNsDir, nsPid)) pids.Add(hostPid);
            }
            else if (!string.Equals(targetTmpDir, IpcRootPath, StringComparison.Ordinal))
            {
                if (TryResolveAddress(targetTmpDir, hostPid)) pids.Add(hostPid);
            }
        }

        return pids;
    }

    /// <summary>
    /// Checks whether a diagnostic socket file exists for the given PID in the search directory.
    /// </summary>
    private static bool TryResolveAddress(string searchDirectory, int pid)
    {
        try
        {
            var pattern = $"dotnet-diagnostic-{pid}-*-socket";
            return Directory.GetFiles(searchDirectory, pattern).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads /proc/{pid}/status for the NSpid line to detect cross-namespace processes.
    /// Returns true if the process is in a different PID namespace.
    /// </summary>
    internal static bool TryGetNamespacePid(int hostPid, out int nsPid)
    {
        nsPid = hostPid;

        string[] lines;
        try
        {
            lines = File.ReadAllLines($"/proc/{hostPid}/status");
        }
        catch
        {
            return false;
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("NSpid:\t", StringComparison.Ordinal))
            {
                string[] parts = line.Substring(7).Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1 && int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPid))
                {
                    nsPid = parsedPid;
                    return true;
                }
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the TMPDIR for a target process by reading /proc/{pid}/environ.
    /// Falls back to the platform temp directory if TMPDIR is not set or environ cannot be read.
    /// </summary>
    internal static string GetProcessTmpDir(int hostPid)
    {
        var fallback = Path.GetTempPath();

        byte[] environData;
        try
        {
            environData = File.ReadAllBytes($"/proc/{hostPid}/environ");
        }
        catch
        {
            return fallback;
        }

        if (environData.Length == 0)
            return fallback;

        var environ = Encoding.UTF8.GetString(environData);
        foreach (var envVar in environ.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (envVar.StartsWith("TMPDIR=", StringComparison.Ordinal)) return envVar.Substring(7);
        }

        return fallback;
    }

    /// <summary>
    /// Reads the container ID from /proc/{pid}/cgroup by matching the 64-hex-character
    /// identifier Docker and containerd embed in the cgroup path (covering both the
    /// cgroupfs and systemd drivers). Returns null for host processes or when no
    /// container ID is present.
    /// </summary>
    internal static string GetContainerId(int hostPid)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines($"/proc/{hostPid}/cgroup");
        }
        catch
        {
            return null;
        }

        foreach (var line in lines)
        {
            var match = ContainerIdPattern.Match(line);
            if (match.Success) return match.Value;
        }

        return null;
    }

    /// <summary>
    /// Gets the full command line for a process, using platform-specific techniques.
    /// </summary>
    private static string GetCommandLine(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return GetCommandLineWindows(pid);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return GetCommandLineLinux(pid);

        return string.Empty;
    }

    /// <summary>
    /// Reads /proc/{pid}/cmdline and joins the null-separated arguments with spaces.
    /// </summary>
    private static string GetCommandLineLinux(int pid)
    {
        try
        {
            var raw = File.ReadAllText($"/proc/{pid}/cmdline");
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            return raw.Replace('\0', ' ').TrimEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Reads the command line from the process's PEB via NtQueryInformationProcess and ReadProcessMemory.
    /// </summary>
    private static string GetCommandLineWindows(int pid)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch
        {
            return string.Empty;
        }

        try
        {
            var processHandle = process.Handle;

            var processBitness = GetProcessBitness(processHandle);
            if (processBitness == 64 && !Environment.Is64BitProcess) return string.Empty;

            var pPeb = processBitness == 64 ? GetPeb64(processHandle) : GetPeb32(processHandle);
            var offset = processBitness == 64 ? 0x20 : 0x10;
            var unicodeStringOffset = processBitness == 64 ? 0x70 : 0x40;

            if (!ReadIntPtr(processHandle, unchecked(pPeb + offset), out IntPtr ptr)) return string.Empty;

            int commandLineLength;
            IntPtr commandLineBuffer;

            if ((processBitness == 64 && Environment.Is64BitProcess) ||
                (processBitness == 32 && !Environment.Is64BitProcess))
            {
                var unicodeString = default(Win32.UNICODE_STRING);
                if (!Win32.ReadProcessMemory(processHandle, ptr + unicodeStringOffset, ref unicodeString, new IntPtr(Marshal.SizeOf(unicodeString)), IntPtr.Zero)) return string.Empty;

                commandLineLength = unicodeString.Length;
                commandLineBuffer = unicodeString.Buffer;
            }
            else
            {
                var unicodeString32 = default(Win32.UNICODE_STRING_32);
                if (!Win32.ReadProcessMemory(processHandle, ptr + unicodeStringOffset, ref unicodeString32, new IntPtr(Marshal.SizeOf(unicodeString32)), IntPtr.Zero)) return string.Empty;

                commandLineLength = unicodeString32.Length;
                commandLineBuffer = new IntPtr(unicodeString32.Buffer);
            }

            byte[] commandLine = new byte[commandLineLength];
            if (!Win32.ReadProcessMemory(processHandle, commandLineBuffer, commandLine, new IntPtr(commandLineLength), IntPtr.Zero)) return string.Empty;

            return Encoding.Unicode.GetString(commandLine);
        }
        catch (Win32Exception)
        {
            return string.Empty;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static bool ReadIntPtr(IntPtr hProcess, IntPtr ptr, out IntPtr readPtr)
    {
        var dataSize = new IntPtr(IntPtr.Size);
        var resLen = IntPtr.Zero;
        if (!Win32.ReadProcessMemory(hProcess, ptr, out readPtr, dataSize, ref resLen))
            return false;
        return resLen == dataSize;
    }

    private static IntPtr GetPebNative(IntPtr hProcess)
    {
        var pbi = default(Win32.ProcessInformation);
        var pbiSize = Marshal.SizeOf(pbi);
        Win32.NtQueryInformationProcess(hProcess, Win32.ProcessBasicInformation, ref pbi, pbiSize, out int resLen);
        if (resLen != pbiSize) throw new Win32Exception("NtQueryInformationProcess failed: " + Marshal.GetLastWin32Error());
        return pbi.PebBaseAddress;
    }

    private static IntPtr GetPeb64(IntPtr hProcess) => GetPebNative(hProcess);

    private static IntPtr GetPeb32(IntPtr hProcess)
    {
        if (Environment.Is64BitProcess)
        {
            var ptr = IntPtr.Zero;
            var resLen = 0;
            var pbiSize = IntPtr.Size;
            Win32.NtQueryInformationProcess(hProcess, Win32.ProcessWow64Information, ref ptr, pbiSize, ref resLen);
            if (resLen != pbiSize) throw new Win32Exception("NtQueryInformationProcess failed: " + Marshal.GetLastWin32Error());
            return ptr;
        }
        return GetPebNative(hProcess);
    }

    private static int GetProcessBitness(IntPtr hProcess)
    {
        if (Environment.Is64BitOperatingSystem)
        {
            if (!Win32.IsWow64Process(hProcess, out bool wow64)) return 32;
            return wow64 ? 32 : 64;
        }
        return 32;
    }

    /// <summary>
    /// Extracts the first token from a command line string as the process pathname.
    /// Handles quoted paths.
    /// </summary>
    private static string ExtractPathname(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return string.Empty;

        var trimmed = commandLine.TrimStart();
        if (trimmed.StartsWith('"'))
        {
            int close = trimmed.IndexOf('"', 1);
            return close > 0 ? trimmed.Substring(1, close - 1) : trimmed.TrimStart('"');
        }

        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed.Substring(0, space) : trimmed;
    }

    private static bool ProcessExists(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
