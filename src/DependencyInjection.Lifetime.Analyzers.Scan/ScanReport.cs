using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DependencyInjection.Lifetime.Analyzers.Scan;

internal sealed record RuleSummary(RuleInfo Rule, int Count, string Severity);

/// <summary>
/// The findings of one scan, deduplicated across target frameworks (a multi-targeted project compiles each
/// file once per framework) and with paths made relative to <see cref="RootDirectory"/> (the git work tree
/// around the scanned path, or the scanned directory itself).
/// </summary>
internal sealed class ScanReport
{
    public const string DocumentationSiteUri = RuleCatalog.DocumentationSiteUri;
    public const string RuleCatalogUri = RuleCatalog.RuleCatalogUri;

    /// <summary>Rules beyond this many get one catalog link instead of a line each.</summary>
    private const int MaxRuleLinks = 10;

    /// <summary>Source lines longer than this are cut short in the findings list.</summary>
    private const int MaxSourceLineLength = 110;

    private static readonly string[] SeverityOrder = ["Error", "Warning", "Info", "Hidden"];

    /// <summary>The <c>partialFingerprints</c> key the scanner writes and reads baselines by.</summary>
    public const string FingerprintKey = "dependencyInjectionLifetimeFingerprint/v1";

    private readonly SourceFiles _sources;
    private readonly IReadOnlyDictionary<Finding, string> _fingerprints;

    private ScanReport(
        ScanReport original,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<Finding> baselineFindings,
        int filteredOut
    )
    {
        RootDirectory = original.RootDirectory;
        Rules = original.Rules;
        _sources = original._sources;
        _fingerprints = original._fingerprints;
        HasBaseline = original.HasBaseline;
        Findings = findings;
        BaselineFindings = baselineFindings;
        FilteredOut = filteredOut;
    }

    public ScanReport(
        string rootDirectory,
        IEnumerable<Finding> findings,
        IReadOnlyDictionary<string, RuleInfo> rules
    )
    {
        RootDirectory = rootDirectory;
        Findings = findings
            .Select(finding => finding with { Path = ToRelativePath(rootDirectory, finding.Path) })
            .DistinctBy(finding => (finding.RuleId, finding.Path, finding.Line, finding.Column))
            .OrderBy(finding => finding.Path, StringComparer.Ordinal)
            .ThenBy(finding => finding.Line)
            .ThenBy(finding => finding.Column)
            .ThenBy(finding => finding.RuleId, StringComparer.Ordinal)
            .ToList();
        Rules = rules;
        _sources = new SourceFiles(rootDirectory);
        _fingerprints = ComputeFingerprints(Findings, _sources);
    }

    public string RootDirectory { get; }

    public IReadOnlyList<Finding> Findings { get; }

    public IReadOnlyDictionary<string, RuleInfo> Rules { get; }

    /// <summary>How many findings <c>--rules</c>, <c>--skip-rules</c> or <c>--exclude</c> left out.</summary>
    public int FilteredOut { get; }

    /// <summary>Whether <see cref="ApplyBaseline"/> ran, so <see cref="Findings"/> holds only new findings.</summary>
    public bool HasBaseline { get; private init; }

    /// <summary>Findings the baseline already had. They stay in the SARIF report but not in the text or the exit code.</summary>
    public IReadOnlyList<Finding> BaselineFindings { get; } = [];

    /// <summary>
    /// A fingerprint that survives the finding moving to another line: the rule, the file, the finding's line of code
    /// without whitespace (or its message, when the file cannot be read), and which occurrence of that combination it is.
    /// </summary>
    public string Fingerprint(Finding finding) => _fingerprints[finding];

    /// <summary>
    /// Splits the findings into new ones (<see cref="Findings"/>) and the ones <paramref name="baseline"/> already had
    /// (<see cref="BaselineFindings"/>).
    /// </summary>
    public ScanReport ApplyBaseline(ScanBaseline baseline)
    {
        var known = Findings
            .Where(finding => baseline.Contains(finding, Fingerprint(finding)))
            .ToHashSet();
        var fresh = Findings.Where(finding => !known.Contains(finding)).ToList();
        return new ScanReport(
            this,
            fresh,
            [.. BaselineFindings, .. Findings.Where(known.Contains)],
            FilteredOut
        )
        {
            HasBaseline = true,
        };
    }

    /// <summary>The report without the findings <paramref name="filter"/> leaves out.</summary>
    public ScanReport Filter(ScanFilter filter)
    {
        if (filter.IsEmpty)
            return this;

        var kept = Findings.Where(filter.Includes).ToList();
        var keptBaseline = BaselineFindings.Where(filter.Includes).ToList();
        var filteredOut =
            FilteredOut + Findings.Count - kept.Count + BaselineFindings.Count - keptBaseline.Count;
        return new ScanReport(this, kept, keptBaseline, filteredOut);
    }

    /// <summary>How many findings are at least as severe as <paramref name="severity"/> ("Error", "Warning" or "Info").</summary>
    public int CountAtLeast(string severity) =>
        Findings.Count(finding => SeverityRank(finding.Severity) <= SeverityRank(severity));

    /// <summary>Rules that reported, most severe first, then most frequent.</summary>
    public IReadOnlyList<RuleSummary> RuleSummaries() =>
        Findings
            .GroupBy(finding => finding.RuleId, StringComparer.Ordinal)
            .Select(group =>
            {
                var severity = group
                    .Select(finding => finding.Severity)
                    .OrderBy(SeverityRank)
                    .First();
                return new RuleSummary(GetRule(group.Key), group.Count(), severity);
            })
            .OrderBy(summary => SeverityRank(summary.Severity))
            .ThenByDescending(summary => summary.Count)
            .ThenBy(summary => summary.Rule.Id, StringComparer.Ordinal)
            .ToList();

    public IReadOnlyList<(string Path, int Count)> TopFiles(int count) =>
        Findings
            .GroupBy(finding => finding.Path, StringComparer.Ordinal)
            .Select(group => (group.Key, group.Count()))
            .OrderByDescending(file => file.Item2)
            .ThenBy(file => file.Key, StringComparer.Ordinal)
            .Take(Math.Max(0, count))
            .ToList();

    public RuleInfo GetRule(string id) =>
        Rules.TryGetValue(id, out var rule) ? rule : RuleCatalog.Get(id);

    public string RenderText(
        int topFiles,
        string? sarifPath,
        int findingsPerRule = 0,
        int context = 0
    )
    {
        var text = new StringBuilder();
        var summaries = RuleSummaries();

        if (Findings.Count == 0)
        {
            text.AppendLine(
                HasBaseline
                    ? "DI Scan found no new dependency injection lifetime problems."
                    : "DI Scan found no dependency injection lifetime problems."
            );
            if (FilteredOut > 0)
                text.AppendLine(
                    Invariant(
                        $"{Plural(FilteredOut, "finding")} left out by --rules, --skip-rules or --exclude."
                    )
                );
            AppendBaselineNote(text);
        }
        else
        {
            var files = Findings
                .Select(finding => finding.Path)
                .Distinct(StringComparer.Ordinal)
                .Count();
            text.AppendLine(
                Invariant(
                    $"DI Scan found {Plural(Findings.Count, HasBaseline ? "new problem" : "problem")} ({Plural(summaries.Count, "rule")}, {Plural(files, "file")})."
                )
            );
            if (FilteredOut > 0)
                text.AppendLine(
                    Invariant(
                        $"{Plural(FilteredOut, "more finding")} left out by --rules, --skip-rules or --exclude."
                    )
                );
            AppendBaselineNote(text);
            text.AppendLine();

            var titleWidth = Math.Min(60, summaries.Max(summary => summary.Rule.Title.Length));
            text.AppendLine(Invariant($"  {"Rule",-7} {"Severity",-8} {"Count",5}  Title"));
            foreach (var summary in summaries)
            {
                text.AppendLine(
                    Invariant(
                        $"  {summary.Rule.Id,-7} {summary.Severity,-8} {summary.Count,5}  {Truncate(summary.Rule.Title, titleWidth)}"
                    )
                );
            }

            if (findingsPerRule > 0)
                RenderFindings(text, summaries, findingsPerRule, context);

            var top = TopFiles(topFiles);
            if (top.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Most affected files:");
                foreach (var (path, count) in top)
                    text.AppendLine(Invariant($"  {count,5}  {path}"));
            }

            text.AppendLine();
            text.AppendLine("What each rule means and how to fix it:");
            foreach (var summary in summaries.Take(MaxRuleLinks))
                text.AppendLine(
                    Invariant($"  {summary.Rule.Id}  {summary.Rule.HelpUri ?? RuleCatalogUri}")
                );
            if (summaries.Count > MaxRuleLinks)
                text.AppendLine(Invariant($"  Every rule: {RuleCatalogUri}"));
        }

        if (sarifPath is not null)
        {
            text.AppendLine();
            text.AppendLine(Invariant($"SARIF report: {sarifPath}"));
        }

        text.AppendLine();
        text.AppendLine(
            "Keep these checks on in the editor and CI: dotnet add package DependencyInjection.Lifetime.Analyzers"
        );
        return text.ToString();
    }

    /// <summary>
    /// Writes the current findings as a text baseline that <c>--baseline</c> reads back. One finding
    /// per line, sorted ordinally, with comment headers that explain where it came from.
    /// </summary>
    public void WriteTextBaseline(TextWriter writer, string toolVersion)
    {
        writer.WriteLine("# di-scan text baseline");
        writer.WriteLine($"# tool: di-scan {toolVersion}");
        writer.WriteLine("# regenerate: di-scan <path> --update-baseline <this-file>");
        foreach (var finding in Findings.OrderBy(finding => finding.Path, StringComparer.Ordinal)
                     .ThenBy(finding => finding.Line)
                     .ThenBy(finding => finding.Column)
                     .ThenBy(finding => finding.RuleId, StringComparer.Ordinal))
        {
            writer.WriteLine($"{finding.RuleId} {Location(finding)}");
        }
    }

    /// <summary>
    /// Lists where each rule reported, up to <paramref name="perRule"/> findings a rule, with the message and the
    /// surrounding source, so the terminal report can be acted on without opening the SARIF file.
    /// </summary>
    private void RenderFindings(
        StringBuilder text,
        IReadOnlyList<RuleSummary> summaries,
        int perRule,
        int context
    )
    {
        text.AppendLine();
        text.AppendLine("Findings:");
        foreach (var summary in summaries)
        {
            text.AppendLine();
            text.AppendLine(Invariant($"  {summary.Rule.Id}  {summary.Rule.Title}"));
            var findings = Findings.Where(finding => finding.RuleId == summary.Rule.Id).ToList();
            foreach (var finding in findings.Take(perRule))
            {
                text.AppendLine(Invariant($"    {Location(finding)}"));
                if (finding.Message.Length > 0)
                    text.AppendLine(Invariant($"      {finding.Message}"));
                var source = SourceContext(finding, context);
                if (source.Count == 0)
                {
                    var code = SourceLine(finding);
                    if (code is not null)
                        text.AppendLine(Invariant($"      {finding.Line} | {code}"));
                }
                else
                {
                    foreach (var (number, line) in source)
                    {
                        var marker = number == finding.Line ? ">" : " ";
                        text.AppendLine(
                            Invariant(
                                $"      {marker} {number,5} | {Truncate(line, MaxSourceLineLength)}"
                            )
                        );
                    }
                }
            }

            if (findings.Count > perRule)
                text.AppendLine(
                    Invariant($"    ...and {findings.Count - perRule} more in the SARIF report.")
                );
        }
    }

    private void AppendBaselineNote(StringBuilder text)
    {
        if (BaselineFindings.Count > 0)
            text.AppendLine(
                Invariant(
                    $"{Plural(BaselineFindings.Count, "finding")} already in the baseline {(BaselineFindings.Count == 1 ? "is" : "are")} not listed; the SARIF report keeps {(BaselineFindings.Count == 1 ? "it" : "them")}."
                )
            );
    }

    internal static string Location(Finding finding) =>
        finding.Line <= 0 ? finding.Path
        : finding.Column <= 0 ? Invariant($"{finding.Path}:{finding.Line}")
        : Invariant($"{finding.Path}:{finding.Line}:{finding.Column}");

    /// <summary>
    /// The finding's line and up to <paramref name="context"/> lines either side, untrimmed, or an empty list when the
    /// file cannot be read.
    /// </summary>
    internal IReadOnlyList<(int Number, string Text)> SourceContext(Finding finding, int context)
    {
        var lines = new List<(int, string)>();
        if (_sources.Line(finding.Path, finding.Line) is null)
            return lines;

        for (
            var number = Math.Max(1, finding.Line - context);
            number <= finding.Line + context;
            number++
        )
        {
            var text = _sources.Line(finding.Path, number);
            if (text is null)
                break;
            lines.Add((number, text));
        }

        return lines;
    }

    /// <summary>The finding's line of code, trimmed and shortened, or null when the file cannot be read.</summary>
    internal string? SourceLine(Finding finding)
    {
        var code = _sources.Line(finding.Path, finding.Line)?.Trim();
        return string.IsNullOrEmpty(code) ? null : Truncate(code, MaxSourceLineLength);
    }

    private static Dictionary<Finding, string> ComputeFingerprints(
        IReadOnlyList<Finding> findings,
        SourceFiles sources
    )
    {
        var fingerprints = new Dictionary<Finding, string>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            var code = sources.Line(finding.Path, finding.Line);
            var anchor = string.IsNullOrWhiteSpace(code)
                ? "message:" + finding.Message
                : "code:" + string.Concat(code.Where(c => !char.IsWhiteSpace(c)));
            var key = finding.RuleId + "\n" + finding.Path + "\n" + anchor;
            occurrences[key] = occurrences.TryGetValue(key, out var seen) ? seen + 1 : 1;
            var hash = SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    key + "\n" + occurrences[key].ToString(CultureInfo.InvariantCulture)
                )
            );
            fingerprints[finding] = Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
        }

        return fingerprints;
    }

    /// <summary>
    /// Writes one SARIF 2.1.0 run holding every finding, with file locations relative to <see cref="RootDirectory"/>
    /// (the <c>%SRCROOT%</c> base), so it can be uploaded to GitHub code scanning as is.
    /// </summary>
    public void WriteSarif(Stream stream, string toolVersion)
    {
        var results = Findings
            .Select(finding => (Finding: finding, State: "new"))
            .Concat(BaselineFindings.Select(finding => (Finding: finding, State: "unchanged")))
            .OrderBy(result => result.Finding.Path, StringComparer.Ordinal)
            .ThenBy(result => result.Finding.Line)
            .ThenBy(result => result.Finding.Column)
            .ThenBy(result => result.Finding.RuleId, StringComparer.Ordinal)
            .ToList();
        var reportedRules = results
            .Select(result => result.Finding.RuleId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var ruleIndex = reportedRules
            .Select((id, index) => (id, index))
            .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);

        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("$schema", "https://json.schemastore.org/sarif-2.1.0.json");
        writer.WriteString("version", "2.1.0");
        writer.WriteStartArray("runs");
        writer.WriteStartObject();

        writer.WriteStartObject("tool");
        writer.WriteStartObject("driver");
        writer.WriteString("name", "DependencyInjection.Lifetime.Analyzers");
        writer.WriteString("version", toolVersion);
        writer.WriteString("semanticVersion", toolVersion);
        writer.WriteString("informationUri", DocumentationSiteUri);
        writer.WriteStartArray("rules");
        foreach (var id in reportedRules)
        {
            var rule = GetRule(id);
            writer.WriteStartObject();
            writer.WriteString("id", rule.Id);
            writer.WriteStartObject("shortDescription");
            writer.WriteString("text", rule.Title);
            writer.WriteEndObject();
            if (rule.Description is not null)
            {
                writer.WriteStartObject("fullDescription");
                writer.WriteString("text", rule.Description);
                writer.WriteEndObject();
            }
            if (rule.HelpUri is not null)
            {
                // GitHub code scanning shows help.markdown on each alert's page.
                writer.WriteString("helpUri", rule.HelpUri);
                writer.WriteStartObject("help");
                writer.WriteString(
                    "text",
                    (rule.Description is null ? "" : rule.Description + " ")
                        + "How to fix it: "
                        + rule.HelpUri
                );
                writer.WriteString(
                    "markdown",
                    (rule.Description is null ? "" : rule.Description + "\n\n")
                        + Invariant(
                            $"[{rule.Id}: what it catches and how to fix it]({rule.HelpUri})"
                        )
                );
                writer.WriteEndObject();
            }
            writer.WriteStartObject("defaultConfiguration");
            writer.WriteString("level", ToSarifLevel(rule.Severity));
            writer.WriteEndObject();
            writer.WriteStartObject("properties");
            writer.WriteStartArray("tags");
            writer.WriteStringValue("dependency-injection");
            writer.WriteStringValue("di");
            writer.WriteStringValue("lifetime");
            if (rule.Category is not null)
                writer.WriteStringValue(rule.Category.ToLowerInvariant());
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteStartObject("originalUriBaseIds");
        writer.WriteStartObject("%SRCROOT%");
        writer.WriteString("uri", new Uri(EnsureTrailingSeparator(RootDirectory)).AbsoluteUri);
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteStartArray("results");
        foreach (var (finding, state) in results)
        {
            writer.WriteStartObject();
            writer.WriteString("ruleId", finding.RuleId);
            writer.WriteNumber("ruleIndex", ruleIndex[finding.RuleId]);
            writer.WriteString("level", ToSarifLevel(finding.Severity));
            writer.WriteStartObject("message");
            writer.WriteString("text", finding.Message);
            writer.WriteEndObject();
            writer.WriteStartObject("partialFingerprints");
            writer.WriteString(FingerprintKey, Fingerprint(finding));
            writer.WriteEndObject();
            if (HasBaseline)
                writer.WriteString("baselineState", state);
            writer.WriteStartArray("locations");
            writer.WriteStartObject();
            writer.WriteStartObject("physicalLocation");
            writer.WriteStartObject("artifactLocation");
            if (Path.IsPathRooted(finding.Path))
            {
                writer.WriteString("uri", new Uri(finding.Path).AbsoluteUri);
            }
            else
            {
                writer.WriteString(
                    "uri",
                    string.Join('/', finding.Path.Split('/').Select(Uri.EscapeDataString))
                );
                writer.WriteString("uriBaseId", "%SRCROOT%");
            }
            writer.WriteEndObject();
            if (finding.Line > 0)
            {
                writer.WriteStartObject("region");
                writer.WriteNumber("startLine", finding.Line);
                if (finding.Column > 0)
                    writer.WriteNumber("startColumn", finding.Column);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>Makes a path relative to the report root with forward slashes; paths outside it stay absolute.</summary>
    internal static string ToRelativePath(string rootDirectory, string path)
    {
        var fullPath = Path.IsPathRooted(path)
            ? PathUtil.Canonicalize(path)
            : PathUtil.Canonicalize(Path.Combine(rootDirectory, path));
        var relative = Path.GetRelativePath(PathUtil.Canonicalize(rootDirectory), fullPath);
        if (
            relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative)
        )
            return fullPath;

        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string EnsureTrailingSeparator(string directory) =>
        Path.EndsInDirectorySeparator(directory)
            ? directory
            : directory + Path.DirectorySeparatorChar;

    private static int SeverityRank(string severity)
    {
        var index = Array.IndexOf(SeverityOrder, severity);
        return index < 0 ? SeverityOrder.Length : index;
    }

    private static string ToSarifLevel(string severity) =>
        severity switch
        {
            "Error" => "error",
            "Warning" => "warning",
            "Info" => "note",
            _ => "none",
        };

    private static string Plural(int count, string noun) =>
        Invariant($"{count} {noun}{(count == 1 ? "" : "s")}");

    private static string Truncate(string text, int width) =>
        text.Length <= width ? text : text[..(width - 1)] + "…";

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
