
using CommandLineSwitchPipe;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MV10.DotnetUptime;

/// <summary>
/// Resolves the control pipe's name and prepares its directory. Both the service and
/// any client must derive the name the same way, which is why it comes from config
/// rather than from the running process's privileges.
/// </summary>
public static class ControlPipe
{
    // A bare name; the runtime places the socket at Path.GetTempPath() + "CoreFxPipe_" + name.
    // Never the library default, which hashes the executable path and would give two installs
    // separate pipes -- they must collide so a second service instance can be detected.
    private const string OpenPipeName = "dotnet-uptime";

    // Used when summarycommand is elevated. The directory is the access control; the socket
    // itself is world-writable because the library opens it that way by design. This must
    // stay a compile-time constant: a rooted pipe name is used verbatim as the socket
    // pathname and the runtime unlinks whatever already exists there.
    private const string SecureDirectory = "/run/dotnet-uptime";
    private const string SecurePipeName = "/run/dotnet-uptime/control";

    // 0700
    private const UnixFileMode SecureDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// The pipe name both ends must use. Derived from configuration only, never from the
    /// calling process's privileges: the service and an unprivileged client would otherwise
    /// disagree and the client would silently report that no service is running.
    /// </summary>
    public static string Name(ConfigParser config)
        => config.SettingsApp.SummaryCommand == SummaryCommandMode.Elevated && OperatingSystem.IsLinux()
            ? SecurePipeName
            : OpenPipeName;

    /// <summary>
    /// Applies the pipe settings shared by the service and by any process looking for it.
    /// Both ends must agree, so this is the only place they are set.
    /// </summary>
    public static void Configure(ConfigParser config, ILoggerFactory loggerFactory = null)
    {
        CommandLineSwitchServer.Options.PipeName = Name(config);
        CommandLineSwitchServer.Options.LoggerFactory = loggerFactory;

        // command lines carry connection strings and tokens; the TCP transport is
        // documented as having no security whatsoever, so it stays disabled
        CommandLineSwitchServer.Options.Advanced.UnsecuredPort = 0;

        // the library defaults to ASCII, which silently replaces non-ASCII characters
        // with question marks and would corrupt paths and command lines
        CommandLineSwitchServer.Options.Advanced.Encoding = Encoding.UTF8;

        // a listener that faults after coming up is retried, but one that can never be
        // established is terminal; either way the library would forcibly exit the process,
        // taking metrics collection down with the control channel, so the failure is
        // surfaced as an exception for ControlPipeService to report and absorb
        CommandLineSwitchServer.Options.Advanced.AutoRestartServer = true;
        CommandLineSwitchServer.Options.Advanced.ExitOnServerFailure = false;
    }

    /// <summary>
    /// True when a service instance is listening on the control pipe. Proves a responsive
    /// listener rather than mere process existence, so a hung service still counts.
    /// </summary>
    public static bool IsServiceRunning(ConfigParser config)
    {
        Configure(config);
        return CommandLineSwitchServer.TryConnect().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Sends a command to the running service and returns its reply. False means no
    /// service was listening; the caller reports that rather than a blank response.
    /// </summary>
    public static bool TrySend(ConfigParser config, string[] args, out string response)
    {
        Configure(config);
        var delivered = CommandLineSwitchServer.TrySendArgs(args).GetAwaiter().GetResult();
        response = CommandLineSwitchServer.QueryResponse;
        return delivered;
    }

    /// <summary>
    /// Creates the root-only directory holding the control pipe when summarycommand is
    /// elevated. No-op otherwise. Returns false with a message the caller should report
    /// before refusing to start; the alternative is serving a credential-bearing pipe
    /// from an open location.
    /// </summary>
    public static bool TryPrepareDirectory(ConfigParser config, out string error)
    {
        error = null;

        // Windows named pipes live in the kernel object namespace, so there is no directory
        // to protect and elevated remains a caller-side guardrail only
        if (config.SettingsApp.SummaryCommand != SummaryCommandMode.Elevated || !OperatingSystem.IsLinux()) return true;

        if (!Environment.IsPrivilegedProcess)
        {
            error = $"[app] summarycommand is elevated, which places the control pipe in {SecureDirectory} "
                + "and requires the service to run as root. Use summarycommand=enabled to run unprivileged.";
            return false;
        }

        try
        {
            var directory = new DirectoryInfo(SecureDirectory);

            if (directory.Exists)
            {
                // CreateDirectory does not alter an existing directory's mode, and silently
                // widening someone else's directory would be worse than refusing to start
                if (directory.UnixFileMode != SecureDirectoryMode)
                {
                    error = $"{SecureDirectory} exists with permissions {directory.UnixFileMode} "
                        + $"instead of {SecureDirectoryMode}. Remove it or correct its permissions.";
                    return false;
                }
            }
            else
            {
                // the mode is applied as the directory is created, so there is no window
                // in which the pipe's directory exists while still being world-accessible
                Directory.CreateDirectory(SecureDirectory, SecureDirectoryMode);
            }
        }
        catch (Exception ex)
        {
            error = $"Cannot prepare {SecureDirectory} for the control pipe: {ex.Message}";
            return false;
        }

        return true;
    }
}
