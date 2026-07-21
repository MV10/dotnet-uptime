
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MV10.DotnetUptime;

/// <summary>
/// Resolves %token% substitutions in [hosttags] values. Built-in tokens read facts
/// about this machine; %env:NAME% reads an environment variable of the Uptime process
/// (not the monitored process).
/// </summary>
public static class HostTagResolver
{
    private static readonly Regex TokenPattern = new(@"%([^%]+)%", RegexOptions.Compiled);

    private static readonly string[] BuiltInTokens =
        { "machinename", "fqdn", "osversion", "osname", "uptimeversion" };

    /// <summary>Built-in token names, for error messages and docs.</summary>
    public static IEnumerable<string> KnownTokens => BuiltInTokens;

    /// <summary>
    /// Substitutes every %token% in the value. Unknown built-in tokens are reported in
    /// <paramref name="errors"/> (always a config mistake). An unset environment variable
    /// is reported in <paramref name="unresolvedEnvVars"/> instead, because `validate` may
    /// run as a different user than the service and would otherwise fail spuriously.
    /// </summary>
    public static string Resolve(string value, List<string> errors, List<string> unresolvedEnvVars)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains('%')) return value;

        return TokenPattern.Replace(value, match =>
        {
            var token = match.Groups[1].Value;

            if (token.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            {
                var name = token.Substring(4);
                var resolved = Environment.GetEnvironmentVariable(name);
                if (string.IsNullOrEmpty(resolved))
                {
                    unresolvedEnvVars.Add(name);
                    return string.Empty;
                }
                return resolved;
            }

            switch (token.ToLowerInvariant())
            {
                case "machinename":
                    return Environment.MachineName;
                case "fqdn":
                    return GetFullyQualifiedName();
                case "osversion":
                    return RuntimeInformation.OSDescription;
                case "osname":
                    return OperatingSystemName();
                case "uptimeversion":
                    return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
                default:
                    errors.Add($"Unknown token %{token}% - valid tokens are "
                        + $"{string.Join(", ", BuiltInTokens.Select(t => $"%{t}%"))}, and %env:NAME%");
                    return string.Empty;
            }
        });
    }

    /// <summary>OTel semantic convention value for os.type.</summary>
    public static string OperatingSystemName()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        if (OperatingSystem.IsMacOS()) return "darwin";
        return "other";
    }

    private static string GetFullyQualifiedName()
    {
        // a DNS lookup can fail or be slow on an isolated host; the machine name is
        // always available and is a reasonable answer when the domain is unknown
        try
        {
            var hostName = Dns.GetHostEntry(string.Empty).HostName;
            if (!string.IsNullOrEmpty(hostName)) return hostName;
        }
        catch
        {
        }

        return Environment.MachineName;
    }
}
