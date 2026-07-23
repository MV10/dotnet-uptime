
using Microsoft.Extensions.Logging;

namespace MV10.DotnetUptime;

/// <summary>
/// Service behavior settings from [app].
/// </summary>
public class AppConfig
{
    public int ProcessScanIntervalMs { get; set; } = 15000;
    public int DiagnosticsIntervalMs { get; set; } = 15000;
    public int MaxHistograms { get; set; } = 10;
    public int MaxTimeSeries { get; set; } = 1000;
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Warning;

    /// <summary>
    /// Whether the `summary` command is disabled, requires an elevated caller (moving the
    /// control pipe to a root-only directory on Linux), or is open to any caller. Defaults
    /// to disabled, so an unconfigured service exposes no process command lines.
    /// </summary>
    public SummaryCommandMode SummaryCommand { get; set; } = SummaryCommandMode.Disabled;

    /// <summary>
    /// When true (the default), secret values in the exported `process.command_line` tag are
    /// redacted. Turning it off exports raw command lines, which routinely carry credentials.
    /// </summary>
    public bool RedactPayload { get; set; } = true;
}
