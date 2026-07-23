
using System.Text;
using System.Text.RegularExpressions;

namespace MV10.DotnetUptime;

/// <summary>
/// Replaces secret values in a process command line with a placeholder. Command lines
/// routinely carry connection strings, tokens and passwords; this is mitigation, not a
/// boundary, and cannot catch a positional secret that has no key name or recognizable
/// shape. Operates per argument, so it needs real argv where that is available.
/// </summary>
public static partial class CommandLineRedactor
{
    private const string Placeholder = "***";

    // key names whose value is a secret. Matched against the final segment of a key, so
    // "ConnectionStrings:Db" and "--client-secret" both resolve to the sensitive word.
    [GeneratedRegex(@"(password|passwd|pwd|secret|token|apikey|api[-_]?key|credential|clientsecret|privatekey|accountkey|sas|bearer|connectionstring)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveKey();

    // a value made of "name=value" pairs joined by ; or , (a connection string). Only the
    // sensitive pairs are redacted so Server=/Database= survive to still identify the process.
    [GeneratedRegex(@"([;,]|^)\s*(?<key>[^=;,]+?)\s*=\s*(?<value>[^;,]*)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPair();

    // scheme://user:password@host -- only the password component is replaced
    [GeneratedRegex(@"^(?<prefix>[a-z][a-z0-9+.\-]*://[^:/?#@\s]+:)(?<pw>[^@/?#\s]+)(?<suffix>@)",
        RegexOptions.IgnoreCase)]
    private static partial Regex UrlCredential();

    /// <summary>
    /// Redacts real argv, redacting each argument in place. This is the accurate path:
    /// argument boundaries are preserved, so a secret containing spaces cannot leak by
    /// being re-split. Used on Linux, where /proc/{pid}/cmdline is NUL-separated argv.
    /// </summary>
    public static string Redact(IReadOnlyList<string> argv)
    {
        if (argv is null || argv.Count == 0) return string.Empty;

        var result = new string[argv.Count];
        for (var i = 0; i < argv.Count; i++)
        {
            // a bare sensitive flag (--password, -p) redacts the following value token,
            // which lives in the next argument rather than after a separator in this one
            if (i > 0 && IsBareSensitiveFlag(argv[i - 1]) && !argv[i].StartsWith('-'))
            {
                result[i] = Placeholder;
                continue;
            }

            result[i] = RedactArgument(argv[i]);
        }

        return string.Join(' ', result);
    }

    /// <summary>
    /// Redacts a command line that is only available as a single flattened string, using
    /// quote-aware tokenization to recover approximate arguments. This is the Windows path
    /// (the PEB exposes one string, never argv) and a best effort; a secret containing an
    /// unquoted space cannot be reliably isolated. State this limitation rather than imply parity.
    /// </summary>
    public static string RedactFlattened(string commandLine)
        => string.IsNullOrWhiteSpace(commandLine)
            ? string.Empty
            : Redact(Tokenize(commandLine));

    private static string RedactArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return arg;

        // "key=value" / "key:value"; split on whichever delimiter appears first
        var eq = arg.IndexOf('=');
        var colon = arg.IndexOf(':');

        // a "scheme://" colon is part of a URL value, not a key/value separator; ignoring
        // it keeps the scheme attached so a bare URL token is recognized and redacted
        if (colon >= 0 && colon + 2 < arg.Length && arg[colon + 1] == '/' && arg[colon + 2] == '/')
            colon = -1;

        var split = (eq, colon) switch
        {
            ( < 0, < 0) => -1,
            ( < 0, _) => colon,
            (_, < 0) => eq,
            _ => Math.Min(eq, colon)
        };

        if (split > 0 && split < arg.Length - 1)
        {
            var key = arg[..split];
            var separator = arg[split];
            var value = arg[(split + 1)..];

            // a connection-string value keeps its non-secret pairs, whatever the key is
            if (LooksLikeConnectionString(value))
                return $"{key}{separator}{RedactConnectionString(value)}";

            if (SensitiveKey().IsMatch(LastKeySegment(key)))
                return $"{key}{separator}{Placeholder}";

            return $"{key}{separator}{RedactBareValue(value)}";
        }

        return RedactBareValue(arg);
    }

    private static string RedactBareValue(string value)
    {
        var url = UrlCredential().Match(value);
        return url.Success
            ? $"{url.Groups["prefix"].Value}{Placeholder}{url.Groups["suffix"].Value}{value[url.Length..]}"
            : value;
    }

    // the key of "ConnectionStrings:Db" is the whole thing; sensitivity is decided by the
    // final segment, so a nested configuration key still matches the sensitive word
    private static string LastKeySegment(string key)
    {
        var trimmed = key.TrimStart('-', '/');
        var slash = trimmed.LastIndexOfAny(new[] { ':', '.' });
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    private static bool LooksLikeConnectionString(string value)
        // at least two "name=value" pairs separated by ; or , -- one "a=b" is just a value
        => value.IndexOfAny(new[] { ';', ',' }) > 0 && value.Contains('=')
            && ConnectionStringPair().Matches(value).Count > 1;

    private static string RedactConnectionString(string value)
        => ConnectionStringPair().Replace(value, match =>
        {
            var key = match.Groups["key"].Value;
            if (!SensitiveKey().IsMatch(key)) return match.Value;

            // rebuild the pair with only the value replaced, preserving the leading separator
            var lead = match.Value[..match.Value.IndexOf(match.Groups["key"].Value, StringComparison.Ordinal)];
            return $"{lead}{key}={Placeholder}";
        });

    private static bool IsBareSensitiveFlag(string arg)
    {
        if (arg.Length < 2 || arg[0] != '-') return false;
        // a "key=value" flag is not a bare flag; its value is handled by RedactArgument
        if (arg.IndexOf('=') >= 0) return false;

        var name = arg.TrimStart('-');
        // the common short password flag has no long name to match the table against
        return name.Equals("p", StringComparison.OrdinalIgnoreCase) || SensitiveKey().IsMatch(name);
    }

    /// <summary>
    /// Splits a flattened command line into approximate arguments, honoring double quotes
    /// so a quoted path with spaces stays one token. Windows has no argv to read, so this
    /// is the only tokenization available there.
    /// </summary>
    private static List<string> Tokenize(string commandLine)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }
}
