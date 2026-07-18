
namespace MV10.DotnetUptime;

/// <summary>
/// OTLP push endpoint settings from a named section.
/// </summary>
public class OtlpEndpointConfig
{
    public string Endpoint { get; set; }
    public string Protocol { get; set; } = "grpc";
    public string RawHeader { get; set; }
    public int TimeoutMs { get; set; } = 10000;

    /// <summary>
    /// Parses semicolon-delimited Key:Value header pairs.
    /// </summary>
    public Dictionary<string, string> GetHeaders()
    {
        var headers = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(RawHeader)) return headers;

        foreach (var pair in RawHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colonIndex = pair.IndexOf(':');
            if (colonIndex <= 0)
                throw new ConfigException($"Invalid header format (expected Key:Value): {pair}");
            headers[pair.Substring(0, colonIndex).Trim()] = pair.Substring(colonIndex + 1).Trim();
        }

        return headers;
    }
}
