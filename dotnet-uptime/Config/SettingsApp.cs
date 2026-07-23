
using Microsoft.Extensions.Logging;

namespace MV10.DotnetUptime;

/// <summary>
/// Service behavior settings from [app].
/// </summary>
public class SettingsApp
{
    /// <summary>
    /// How often process discovery occurs, in milliseconds.
    /// </summary>
    public int ProcessScanIntervalMs { get; set; } = 15000;
    
    /// <summary>
    /// How often diagnostic data is pushed to endpoints, in milliseconds.
    /// </summary>
    public int DiagnosticsIntervalMs { get; set; } = 15000;
    
    /// <summary>
    /// Limit of histogram metrics to track per process.
    /// </summary>
    public int MaxHistograms { get; set; } = 10;
    
    /// <summary>
    /// Limit of time series metrics to track per process.
    /// </summary>
    public int MaxTimeSeries { get; set; } = 1000;
    
    /// <summary>
    /// Gate the severity of emitted log events.
    /// </summary>
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
