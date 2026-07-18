
namespace MV10.DotnetUptime;

/// <summary>
/// Thrown for config file errors; message is reported to the console.
/// </summary>
public class ConfigException(string message) : Exception(message);

/// <summary>
/// Thrown when uptime.conf does not exist. Interactive commands may fall back to
/// defaults, but service mode treats this as fatal.
/// </summary>
public class ConfigMissingException(string path)
    : ConfigException($"Config file not found: {path}");