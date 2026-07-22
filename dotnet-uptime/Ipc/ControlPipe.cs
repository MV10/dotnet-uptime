
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

    // Used when elevatedsummary is true. The directory is the access control; the socket
    // itself is world-writable because the library opens it that way by design. This must
    // stay a compile-time constant: a rooted pipe name is used verbatim as the socket
    // pathname and the runtime unlinks whatever already exists there.
    private const string SecureDirectory = "/run/dotnet-uptime";
    private const string SecurePipeName = "/run/dotnet-uptime/control";

    // 0700
    private const UnixFileMode SecureDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// The pipe name both ends must use. Derived from configuration only, never from the
    /// calling process's privileges: the service and an unprivileged client would otherwise
    /// disagree and the client would silently report that no service is running.
    /// </summary>
    public static string Name(ConfigParser config)
        => config.App.RequireElevatedSummary && OperatingSystem.IsLinux()
            ? SecurePipeName
            : OpenPipeName;

    /// <summary>
    /// Creates the root-only directory holding the control pipe when elevatedsummary is on.
    /// No-op otherwise. Returns false with a message the caller should report before refusing
    /// to start; the alternative is serving a credential-bearing pipe from an open location.
    /// </summary>
    public static bool TryPrepareDirectory(ConfigParser config, out string error)
    {
        error = null;

        // Windows named pipes live in the kernel object namespace, so there is no directory
        // to protect and elevatedsummary remains a caller-side guardrail only
        if (!config.App.RequireElevatedSummary || !OperatingSystem.IsLinux()) return true;

        if (!Environment.IsPrivilegedProcess)
        {
            error = $"[app] elevatedsummary is true, which places the control pipe in {SecureDirectory} "
                + "and requires the service to run as root. Set elevatedsummary to false to run unprivileged.";
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
