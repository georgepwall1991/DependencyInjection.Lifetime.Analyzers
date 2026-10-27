using System.Text;
using System.Text.RegularExpressions;

namespace DependencyInjection.Lifetime.Analyzers.Scan;

/// <summary>
/// Which findings a scan reports: <c>--rules</c> keeps only the listed rules, <c>--skip-rules</c> drops rules, and
/// <c>--exclude</c> drops findings in files matching a glob. Paths are matched relative to the report root, with
/// forward slashes, so the same globs work on every OS and match the paths the report prints.
/// </summary>
internal sealed class ScanFilter
{
    private readonly HashSet<string> _rules;
    private readonly HashSet<string> _skippedRules;
    private readonly IReadOnlyList<Regex> _excludes;

    public ScanFilter(
        IEnumerable<string> rules,
        IEnumerable<string> skippedRules,
        IEnumerable<string> excludeGlobs
    )
    {
        _rules = new HashSet<string>(rules, StringComparer.OrdinalIgnoreCase);
        _skippedRules = new HashSet<string>(skippedRules, StringComparer.OrdinalIgnoreCase);
        _excludes = excludeGlobs.Select(GlobToRegex).ToList();
    }

    public bool IsEmpty => _rules.Count == 0 && _skippedRules.Count == 0 && _excludes.Count == 0;

    public bool Includes(Finding finding) =>
        (_rules.Count == 0 || _rules.Contains(finding.RuleId))
        && !_skippedRules.Contains(finding.RuleId)
        && !_excludes.Any(exclude => exclude.IsMatch(finding.Path));

    /// <summary>
    /// Turns a glob into a regular expression over a relative path. <c>*</c> matches within one folder or file name,
    /// <c>**</c> matches across folders, and <c>?</c> matches one character. A glob without a <c>/</c> matches a file
    /// or folder of that name anywhere, so <c>Migrations</c> and <c>*.Designer.cs</c> work on their own.
    /// </summary>
    internal static Regex GlobToRegex(string glob)
    {
        var pattern = glob.Replace('\\', '/').Trim();
        if (pattern.StartsWith("./", StringComparison.Ordinal))
            pattern = pattern[2..];
        if (!pattern.Contains('/'))
            pattern = "**/" + pattern;
        pattern = pattern.TrimEnd('/');

        var regex = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                i++;
                if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                {
                    i++;
                    regex.Append("(?:.*/)?");
                }
                else
                {
                    regex.Append(".*");
                }
            }
            else if (c == '*')
            {
                regex.Append("[^/]*");
            }
            else if (c == '?')
            {
                regex.Append("[^/]");
            }
            else
            {
                regex.Append(Regex.Escape(c.ToString()));
            }
        }

        // A glob naming a folder also covers everything inside it.
        regex.Append("(?:/.*)?$");
        return new Regex(
            regex.ToString(),
            RegexOptions.CultureInvariant
                | (OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None)
        );
    }
}
