
namespace MV10.DotnetUptime;

/// <summary>
/// Maps [processtags] config names to the attribute names Uptime emits, and extracts
/// those values from a discovered process.
/// </summary>
public static class ProcessTagBuilder
{
    // config name -> emitted attribute name. OTel semantic conventions are used wherever
    // one exists so backends and prebuilt dashboards recognize the attribute; the rest
    // take a "process." prefix for consistency.
    private static readonly Dictionary<string, string> AttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // deliberately NOT "service.name": that identifies the producer of the telemetry
        // at resource scope, which is Uptime. See the collector recipe in README for
        // promoting this to a per-application service.name downstream.
        ["assembly"] = "process.assembly",
        ["filename"] = "process.executable.name",
        ["pathname"] = "process.executable.path",
        ["commandline"] = "process.command_line",
        ["clrversion"] = "process.runtime.version",
        ["arch"] = "process.architecture",
        ["rid"] = "process.runtime.rid",
        ["cookie"] = "process.runtime.cookie",
        ["specifier"] = "process.specifier"
    };

    private static readonly KeyValuePair<string, string>[] None = Array.Empty<KeyValuePair<string, string>>();

    /// <summary>Config names accepted in [processtags], for error messages and docs.</summary>
    public static IEnumerable<string> KnownNames => AttributeNames.Keys;

    public static bool IsKnown(string name) => AttributeNames.ContainsKey(name);

    /// <summary>The attribute name emitted for a config name.</summary>
    public static string AttributeName(string name) => AttributeNames[name];

    /// <summary>
    /// Resolves the selected process facts into tags. Values that are empty on the
    /// target (an unavailable entrypoint assembly, no specifier rule, a RID absent on
    /// runtimes older than .NET 9) are omitted rather than exported blank.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Build(DiagnosticProcess proc, IReadOnlyList<string> selected)
    {
        if (proc is null || selected is null || selected.Count == 0) return None;

        var tags = new List<KeyValuePair<string, string>>(selected.Count);

        foreach (var name in selected)
        {
            var value = name.ToLowerInvariant() switch
            {
                "assembly" => proc.ManagedEntrypointAssemblyName,
                "filename" => proc.Filename,
                "pathname" => proc.Pathname,
                "commandline" => proc.CommandLine,
                "clrversion" => proc.ClrProductVersionString,
                "arch" => proc.ProcessArchitecture,
                "rid" => proc.PortableRuntimeIdentifier,
                "cookie" => proc.RuntimeInstanceCookie == Guid.Empty ? null : proc.RuntimeInstanceCookie.ToString(),
                "specifier" => proc.Specifier,
                _ => null
            };

            if (string.IsNullOrEmpty(value)) continue;

            tags.Add(new KeyValuePair<string, string>(AttributeNames[name], value));
        }

        return tags.Count == 0 ? None : tags;
    }
}
