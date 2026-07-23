
namespace MV10.DotnetUptime;

/// <summary>
/// Controls the `summary` control-pipe command. The control pipe itself is created in
/// every mode, since it also backs single-instance detection and interactive suppression.
/// </summary>
public enum SummaryCommandMode
{
    /// <summary>
    /// The command is refused with a message. Nothing about monitored processes is
    /// returned, the only posture that fully closes the remotely-reachable Windows pipe.
    /// </summary>
    Disabled,

    /// <summary>
    /// The command requires an elevated caller. On Linux the pipe moves to a root-only
    /// directory (a real boundary, and the service must run as root); on Windows the
    /// caller's Administrator check is a guardrail only.
    /// </summary>
    Elevated,

    /// <summary>
    /// The command is available to any caller that can reach the pipe.
    /// </summary>
    Enabled
}
