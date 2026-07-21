
using System.Text.RegularExpressions;

namespace MV10.DotnetUptime;

// ---------------------------------------------------------------------------
// TEMPORARY WORKAROUND - remove when the .NET runtime is fixed.
//
// https://github.com/dotnet/diagnostics/issues/5935
//
// MetricsEventSource flattens an instrument's tag set into a single string of
// the form "key=value,key=value" with NO escaping, so a comma or equals sign
// inside a tag value is indistinguishable from the delimiters. Verified against
// .NET 10: the tags
//
//     plain=simple  comma=a,b,c  equals=x=1  url=/api/items?filter=red,blue&sort=name
//
// arrive on the wire as
//
//     comma=a,b,c,equals=x=1,plain=simple,url=/api/items?filter=red,blue&sort=name
//
// Nothing downstream can recover the original tags with certainty. Everything in
// this file is heuristic recovery of data the runtime should not have made
// ambiguous in the first place. Once the runtime emits escaped or structured
// tags, delete the heuristic and parse the format directly.
// ---------------------------------------------------------------------------

/// <summary>
/// Recovers individual tags from the single delimited string emitted by
/// MetricsEventSource. See the workaround notes above.
/// </summary>
public static class CounterTagParser
{
    private static readonly KeyValuePair<string, string>[] None = Array.Empty<KeyValuePair<string, string>>();

    // a segment only begins a new tag when the text before its first '=' looks like
    // an attribute key; anything else (a URL fragment, a bare list element) must be
    // a continuation of the previous value that was split on an embedded comma
    private static readonly Regex PlausibleKey = new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    /// <summary>
    /// Splits the raw tag string into individual key/value pairs, rejoining segments
    /// that were split on a comma occurring inside a value.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Parse(string rawTags)
    {
        if (string.IsNullOrEmpty(rawTags)) return None;

        var results = new List<KeyValuePair<string, string>>();

        foreach (var segment in rawTags.Split(','))
        {
            var eqIndex = segment.IndexOf('=');
            var startsNewTag = eqIndex > 0 && PlausibleKey.IsMatch(segment.Substring(0, eqIndex));

            if (startsNewTag)
            {
                results.Add(new KeyValuePair<string, string>(
                    segment.Substring(0, eqIndex),
                    segment.Substring(eqIndex + 1)));
                continue;
            }

            // a comma inside the previous tag's value; stitch it back together.
            // with no previous tag there is nothing to attach to, so the fragment
            // is unrecoverable and dropped
            if (results.Count == 0) continue;

            var previous = results[^1];
            results[^1] = new KeyValuePair<string, string>(
                previous.Key,
                $"{previous.Value},{segment}");
        }

        return results.Count == 0 ? None : results;
    }
}
