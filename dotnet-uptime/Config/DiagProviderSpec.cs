
namespace MV10.DotnetUptime;

/// <summary>
/// A diagnostic provider specification from [diags]. The provider name may be a literal
/// meter name, a namespace-prefix wildcard ("System.Net.*"), or a bare "*" for all meters.
/// </summary>
public class DiagProviderSpec
{
    public string ProviderName { get; }
    public string[] Counters { get; }
    public string ProcessFilter { get; }

    /// <summary>True when the provider name is "*" or ends with ".*".</summary>
    public bool IsWildcard { get; }

    // for "System.Net.*": prefix is "System.Net." and baseName is "System.Net" (matches the
    // namespace root meter itself as well as everything under it); bare "*" leaves prefix empty
    private readonly string prefix;
    private readonly string baseName;

    public DiagProviderSpec(string providerName, string[] counters = null, string processFilter = null)
    {
        ProviderName = providerName;
        Counters = counters;
        ProcessFilter = processFilter;

        if (providerName == "*")
        {
            IsWildcard = true;
            prefix = string.Empty;
        }
        else if (providerName.EndsWith(".*", StringComparison.Ordinal))
        {
            IsWildcard = true;
            prefix = providerName.Substring(0, providerName.Length - 1);
            baseName = providerName.Substring(0, providerName.Length - 2);
        }
    }

    /// <summary>
    /// Whether this spec applies to a process with the given executable filename. Specs
    /// without a process filter apply to every process.
    /// </summary>
    public bool MatchesProcess(string filename)
    {
        if (string.IsNullOrEmpty(ProcessFilter)) return true;
        return string.Equals(ProcessFilter, filename, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether this spec's provider name matches the given meter/provider name, honoring wildcards.
    /// </summary>
    public bool MatchesProvider(string name)
    {
        if (!IsWildcard)
            return string.Equals(ProviderName, name, StringComparison.OrdinalIgnoreCase);

        if (prefix.Length == 0)
            return true; // bare "*"

        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(baseName, name, StringComparison.OrdinalIgnoreCase);
    }
}
