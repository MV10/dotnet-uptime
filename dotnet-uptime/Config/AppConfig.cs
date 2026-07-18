
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
    public bool ExcludeSelf { get; set; } = true;
}