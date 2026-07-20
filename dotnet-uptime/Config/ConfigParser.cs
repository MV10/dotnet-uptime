
using System.Diagnostics;
using MV10.DotnetUptime.Processes;

namespace MV10.DotnetUptime;

/// <summary>
/// Parsed configuration from uptime.conf.
/// </summary>
public class ConfigParser
{
    public AppConfig App { get; } = new();
    public ProcessRuleType RuleType { get; private set; }
    public IReadOnlyDictionary<string, ProcessRule> Rules { get; private set; } = new Dictionary<string, ProcessRule>();
    public List<DiagProviderSpec> DiagProviders { get; } = new();
    public List<string> OtlpTargetNames { get; } = new();
    public Dictionary<string, OtlpEndpointConfig> OtlpEndpoints { get; } = new();
    public HttpEndpointConfig HttpEndpoint { get; private set; }

    private static readonly HashSet<string> ReservedSections = new(StringComparer.OrdinalIgnoreCase)
        { "app", "include", "exclude", "diags", "otlp", "http" };

    /// <summary>
    /// Loads and parses uptime.conf from the same directory as the running executable.
    /// </summary>
    public static ConfigParser Load()
    {
        var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
        var path = Path.Combine(exeDir, "uptime.conf");
        if (!File.Exists(path)) throw new ConfigMissingException(path);
        return Parse(File.ReadAllLines(path));
    }

    /// <summary>
    /// Parses config from lines (for testability).
    /// </summary>
    public static ConfigParser Parse(string[] lines)
    {
        var config = new ConfigParser();
        var sections = ReadSections(lines);

        ParseAppSection(config, sections);
        ParseRulesSection(config, sections);
        ParseDiagsSection(config, sections);
        ParseOtlpSections(config, sections);
        ParseHttpSection(config, sections);

        return config;
    }

    private static Dictionary<string, List<string>> ReadSections(string[] lines)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string currentSection = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // strip inline comments
            var commentIndex = line.IndexOf('#');
            if (commentIndex >= 0)
                line = line.Substring(0, commentIndex).TrimEnd();

            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line.Substring(1, line.Length - 2).Trim();
                if (string.IsNullOrEmpty(currentSection)) throw new ConfigException("Empty section name");
                if (!sections.ContainsKey(currentSection)) sections[currentSection] = new List<string>();
                continue;
            }

            if (currentSection is null) throw new ConfigException($"Setting outside of a section: {line}");

            sections[currentSection].Add(line);
        }

        return sections;
    }

    private static void ParseAppSection(ConfigParser config, Dictionary<string, List<string>> sections)
    {
        if (!sections.TryGetValue("app", out var lines)) return;

        foreach (var line in lines)
        {
            var (key, value) = SplitKeyValue(line);
            switch (key)
            {
                case "pscan":
                    config.App.ProcessScanIntervalMs = ParseInt(value, key, 1000, int.MaxValue);
                    break;
                case "diags":
                    config.App.DiagnosticsIntervalMs = ParseInt(value, key, 1000, int.MaxValue);
                    break;
                case "maxhistograms":
                    config.App.MaxHistograms = ParseInt(value, key, 1, int.MaxValue);
                    break;
                case "maxtimeseries":
                    config.App.MaxTimeSeries = ParseInt(value, key, 1, int.MaxValue);
                    break;
                case "excludeself":
                    config.App.ExcludeSelf = ParseBool(value, key);
                    break;
                default:
                    throw new ConfigException($"Unknown [app] setting: {key}");
            }
        }
    }

    private static void ParseRulesSection(ConfigParser config, Dictionary<string, List<string>> sections)
    {
        bool hasInclude = sections.ContainsKey("include");
        bool hasExclude = sections.ContainsKey("exclude");

        if (hasInclude && hasExclude)
            throw new ConfigException("[include] and [exclude] are mutually exclusive");

        if (!hasInclude && !hasExclude) return;

        config.RuleType = hasInclude ? ProcessRuleType.Include : ProcessRuleType.Exclude;
        var sectionName = hasInclude ? "include" : "exclude";
        var lines = sections[sectionName];
        var rules = new Dictionary<string, ProcessRule>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            // format: filename[: specifier_regex] - everything after the first colon is
            // the regex (which may itself contain colons); an app name with a colon
            // therefore cannot be referenced, but that is vanishingly rare
            string filename;
            string regex = null;

            var colonIndex = line.IndexOf(':');
            if (colonIndex >= 0)
            {
                filename = line.Substring(0, colonIndex).Trim();
                regex = line.Substring(colonIndex + 1).Trim();
                if (string.IsNullOrEmpty(regex))
                    throw new ConfigException($"Empty specifier regex in [{sectionName}] rule: {line}");
            }
            else
            {
                filename = line.Trim();
            }

            if (string.IsNullOrEmpty(filename))
                throw new ConfigException($"Empty filename in [{sectionName}] rule");

            if (rules.ContainsKey(filename))
                throw new ConfigException($"Duplicate [{sectionName}] rule for: {filename}");

            rules[filename] = new ProcessRule(filename, regex);
        }

        config.Rules = rules;
    }

    private static void ParseDiagsSection(ConfigParser config, Dictionary<string, List<string>> sections)
    {
        if (!sections.TryGetValue("diags", out var lines) || lines.Count == 0)
        {
            // default to System.Runtime
            config.DiagProviders.Add(new DiagProviderSpec("System.Runtime"));
            return;
        }

        foreach (var line in lines)
        {
            // format: ProviderName[counter1,counter2]: processfilter
            var remaining = line;
            string processFilter = null;

            var colonIndex = remaining.IndexOf(':');
            if (colonIndex >= 0)
            {
                // only treat as process filter if not inside brackets
                var bracketStart = remaining.IndexOf('[');
                var bracketEnd = remaining.IndexOf(']');
                if (colonIndex > bracketEnd || bracketStart < 0)
                {
                    processFilter = remaining.Substring(colonIndex + 1).Trim();
                    remaining = remaining.Substring(0, colonIndex).Trim();
                    if (string.IsNullOrEmpty(processFilter))
                        throw new ConfigException($"Empty process filter in [diags]: {line}");
                }
            }

            string providerName;
            string[] counters = null;

            var bStart = remaining.IndexOf('[');
            if (bStart >= 0)
            {
                var bEnd = remaining.IndexOf(']', bStart);
                if (bEnd < 0)
                    throw new ConfigException($"Unclosed bracket in [diags]: {line}");
                providerName = remaining.Substring(0, bStart).Trim();
                var counterList = remaining.Substring(bStart + 1, bEnd - bStart - 1);
                counters = counterList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (counters.Length == 0)
                    throw new ConfigException($"Empty counter list in [diags]: {line}");
            }
            else
            {
                providerName = remaining.Trim();
            }

            if (string.IsNullOrEmpty(providerName))
                throw new ConfigException($"Empty provider name in [diags]: {line}");

            config.DiagProviders.Add(new DiagProviderSpec(providerName, counters, processFilter));
        }
    }

    private static void ParseOtlpSections(ConfigParser config, Dictionary<string, List<string>> sections)
    {
        if (!sections.TryGetValue("otlp", out var targetNames)) return;

        foreach (var name in targetNames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            var trimmed = name.Trim();
            if (ReservedSections.Contains(trimmed))
                throw new ConfigException($"OTLP target name conflicts with reserved section: {trimmed}");

            config.OtlpTargetNames.Add(trimmed);

            if (!sections.TryGetValue(trimmed, out var endpointLines))
                throw new ConfigException($"OTLP target [{trimmed}] listed in [otlp] but section not found");

            var endpoint = new OtlpEndpointConfig();
            foreach (var line in endpointLines)
            {
                var (key, value) = SplitKeyValue(line);
                switch (key)
                {
                    case "endpoint":
                        endpoint.Endpoint = value;
                        break;
                    case "protocol":
                        if (value != "grpc" && value != "http")
                            throw new ConfigException($"[{trimmed}] protocol must be 'grpc' or 'http', got: {value}");
                        endpoint.Protocol = value;
                        break;
                    case "header":
                        endpoint.RawHeader = value;
                        break;
                    case "timeout":
                        endpoint.TimeoutMs = ParseInt(value, $"[{trimmed}] timeout", 1, int.MaxValue);
                        break;
                    default:
                        throw new ConfigException($"Unknown [{trimmed}] setting: {key}");
                }
            }

            if (string.IsNullOrEmpty(endpoint.Endpoint))
                throw new ConfigException($"[{trimmed}] is missing required 'endpoint' setting");

            config.OtlpEndpoints[trimmed] = endpoint;
        }
    }

    private static void ParseHttpSection(ConfigParser config, Dictionary<string, List<string>> sections)
    {
        if (!sections.TryGetValue("http", out var lines)) return;

        var http = new HttpEndpointConfig();
        foreach (var line in lines)
        {
            var (key, value) = SplitKeyValue(line);
            switch (key)
            {
                case "type":
                    if (value != "prometheus")
                        throw new ConfigException($"[http] type must be 'prometheus', got: {value}");
                    http.Type = value;
                    break;
                case "endpoint":
                    http.Endpoint = value;
                    break;
                default:
                    throw new ConfigException($"Unknown [http] setting: {key}");
            }
        }

        if (string.IsNullOrEmpty(http.Endpoint))
            throw new ConfigException("[http] is missing required 'endpoint' setting");

        if (!Uri.TryCreate(http.Endpoint, UriKind.Absolute, out var uri))
            throw new ConfigException($"[http] endpoint is not a valid URL: {http.Endpoint}");

        if (!uri.IsLoopback)
            throw new ConfigException("[http] endpoint must be a localhost address (the Prometheus listener has no security)");

        config.HttpEndpoint = http;
    }

    private static (string key, string value) SplitKeyValue(string line)
    {
        var eqIndex = line.IndexOf('=');
        if (eqIndex < 0)
            throw new ConfigException($"Expected key=value, got: {line}");
        var key = line.Substring(0, eqIndex).Trim().ToLowerInvariant();
        var value = line.Substring(eqIndex + 1).Trim();
        if (string.IsNullOrEmpty(key))
            throw new ConfigException($"Empty key in: {line}");
        return (key, value);
    }

    private static int ParseInt(string value, string name, int min, int max)
    {
        if (!int.TryParse(value, out int result))
            throw new ConfigException($"{name} must be an integer, got: {value}");
        if (result < min || result > max)
            throw new ConfigException($"{name} must be between {min} and {max}, got: {result}");
        return result;
    }

    private static bool ParseBool(string value, string name)
    {
        if (bool.TryParse(value, out bool result)) return result;
        throw new ConfigException($"{name} must be 'true' or 'false', got: {value}");
    }
}
