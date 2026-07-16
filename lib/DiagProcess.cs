
namespace MV10.DotnetUptime.Lib;

/// <summary>
/// Details of a process which exposes a .NET diagnostics port.
/// </summary>
public class DiagProcess(int pid, string pathname, string filename, string specifier, string commandLine, DateTime added)
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
}
