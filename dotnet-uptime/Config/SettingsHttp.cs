
namespace MV10.DotnetUptime;

/// <summary>
/// HTTP scrape endpoint settings from [http].
/// </summary>
public class SettingsHttp
{
    /// <summary>
    /// The protocol (currently only prometheus is valid / defined).
    /// </summary>
    public string Type { get; set; } = "prometheus";
    
    /// <summary>
    /// The endpoint itself (http://localhost:port)
    /// </summary>
    public string Endpoint { get; set; }
}
