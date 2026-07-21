
namespace MV10.DotnetUptime;

/// <summary>
/// Thrown for config file errors; all errors found during a parse are reported together.
/// </summary>
public class ConfigException : Exception
{
    /// <summary>
    /// Every problem found during the parse, in the order encountered.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    public ConfigException(string message)
        : base(message)
    {
        Errors = new[] { message };
    }

    public ConfigException(IReadOnlyList<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors;
    }
}

/// <summary>
/// Thrown when uptime.conf does not exist. Interactive commands may fall back to
/// defaults, but service mode treats this as fatal.
/// </summary>
public class ConfigMissingException(string path)
    : ConfigException($"Config file not found: {path}");
