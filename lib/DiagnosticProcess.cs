
namespace MV10.DotnetUptime.Lib;

/// <summary>
/// Details of a process which exposes a .NET diagnostics port.
/// </summary>
public class DiagnosticProcess(
    int pid,
    string pathname,
    string filename,
    string specifier,
    string commandLine,
    DateTime added,
    Guid runtimeInstanceCookie,
    string processArchitecture,
    string managedEntrypointAssemblyName,
    string clrProductVersionString,
    string portableRuntimeIdentifier)
{
    /// <summary>
    /// Process ID assigned by the OS.
    /// </summary>
    public int PID { get; } = pid;

    /// <summary>
    /// Application pathname from the command line
    /// (such as c:\windows\system32\inetserv\w3wp.exe).
    /// </summary>
    public string Pathname { get; } = pathname;

    /// <summary>
    /// Application name from the command line (such as w3wp.exe).
    /// </summary>
    public string Filename { get; } = filename;

    /// <summary>
    /// Optional element from the command line to differentiate multiple
    /// instances of the same application (for example, IIS w3wp processes
    /// can be identified by the application pool they're hosting); empty string
    /// when no specifier regex is associated with the application name.
    /// </summary>
    public string Specifier { get; } = specifier;

    /// <summary>
    /// Command used to launch the process including arguments.
    /// </summary>
    public string CommandLine { get; } = commandLine;

    /// <summary>
    /// Timestamp when the Scan operation first identified this process.
    /// </summary>
    public DateTime Added { get; } = added;

    /// <summary>
    /// Unique identifier for this CLR runtime instance; distinguishes PID reuse.
    /// </summary>
    public Guid RuntimeInstanceCookie { get; } = runtimeInstanceCookie;

    /// <summary>
    /// Target process architecture (such as x64 or arm64).
    /// </summary>
    public string ProcessArchitecture { get; } = processArchitecture;

    /// <summary>
    /// Name of the managed entrypoint assembly (such as MyWebApp).
    /// </summary>
    public string ManagedEntrypointAssemblyName { get; } = managedEntrypointAssemblyName;

    /// <summary>
    /// CLR product version string, may include prerelease labels.
    /// </summary>
    public string ClrProductVersionString { get; } = clrProductVersionString;

    /// <summary>
    /// Portable runtime identifier (such as linux-x64); empty on runtimes older than .NET 9.
    /// </summary>
    public string PortableRuntimeIdentifier { get; } = portableRuntimeIdentifier;
}
