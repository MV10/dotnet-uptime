
using System.Text.RegularExpressions;

namespace MV10.DotnetUptime.Lib;

/// <summary>
/// Details of a rule for process inclusion or exclusion.
/// </summary>
public class ProcessRule
{
    /// <summary>
    /// The executable filename to match.
    /// </summary>
    public string Filename { get; }

    /// <summary>
    /// An optional .NET regex that returns a Specifier value which
    /// must be found in order to match.
    /// </summary>
    public Regex SpecifierRegex { get; }

    /// <summary>
    /// ctor
    /// </summary>
    public ProcessRule(string filename, string specifierRegex)
    {
        if (string.IsNullOrEmpty(filename)) throw new ArgumentNullException(nameof(filename));
        Filename = filename;

        if (!string.IsNullOrWhiteSpace(specifierRegex))
        {
            SpecifierRegex = new Regex(specifierRegex, RegexOptions.Compiled | RegexOptions.ExplicitCapture);
            if (!SpecifierRegex.GetGroupNames().Contains("Specifier"))
            {
                throw new InvalidOperationException($"The Specifier regex \"{filename}\" does not contain a named capture group 'Specifier'. The pattern must include (?<Specifier>...) or (?'Specifier'...).");
            }
        }
    }

    /// <summary>
    /// If SpecifierRegex exists, the commandline will be tested against it and if
    /// the Specifier group matched, that value is returned, otherwise returns an empty string.
    /// </summary>
    public string FindSpecifier(string commandline)
    {
        if (string.IsNullOrWhiteSpace(commandline)) return string.Empty;

        var match = SpecifierRegex.Match(commandline);
        
        if (match.Success && match.Groups["Specifier"].Success)
        {
            return match.Groups["Specifier"].Value;
        }
        
        return string.Empty;
    }
}
