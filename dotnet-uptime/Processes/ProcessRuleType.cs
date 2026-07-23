
namespace MV10.DotnetUptime;

/// <summary>
/// Identifies what type of rules are used in uptime.conf
/// </summary>
public enum ProcessRuleType
{
    /// <summary>
    /// Rules were defined in a config [include] section
    /// </summary>
    Include = 0,

    /// <summary>
    /// Rules were defined in a config [exclude] section
    /// </summary>
    Exclude = 1
}
