
namespace MV10.DotnetUptime;

/// <summary>
/// HTTP scrape endpoint settings from [http].
/// </summary>
public class HttpEndpointConfig
{
    public string Type { get; set; } = "prometheus";
    public string Endpoint { get; set; }
}
