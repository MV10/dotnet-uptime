
namespace MV10.DotnetUptime.Processes;

/// <summary>
/// Data returned by the runtime's GetProcessInfo IPC command.
/// </summary>
public class RuntimeProcessInfo
{
    public Guid RuntimeInstanceCookie { get; set; }
    public string ProcessArchitecture { get; set; } = string.Empty;
    public string ManagedEntrypointAssemblyName { get; set; } = string.Empty;
    public string ClrProductVersionString { get; set; } = string.Empty;
    public string PortableRuntimeIdentifier { get; set; } = string.Empty;
}