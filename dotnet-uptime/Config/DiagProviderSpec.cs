
namespace MV10.DotnetUptime;

/// <summary>
/// A diagnostic provider specification from [diags].
/// </summary>
public class DiagProviderSpec(string providerName, string[] counters = null, string processFilter = null)
{
    public string ProviderName { get; } = providerName;
    public string[] Counters { get; } = counters;
    public string ProcessFilter { get; } = processFilter;
}
