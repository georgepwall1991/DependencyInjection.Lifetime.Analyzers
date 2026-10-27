using System.Globalization;
using System.Text;

namespace DependencyInjection.Lifetime.Analyzers.Scan;

/// <summary>
/// The report as GitHub shows it: a Markdown summary for the job's summary page (or any Markdown file), and workflow
/// commands that annotate the pull request's changed lines.
/// </summary>
internal static class GitHubOutput
{
    /// <summary>GitHub shows at most 10 annotations of each level per step and 50 per job, so more only fill the log.</summary>
    public const int MaxAnnotations = 50;

    /// <summary>Characters of Markdown that stay safely under GitHub's 1 MiB job summary limit.</summary>
    public const int MaxJobSummaryLength = 900_000;

    /// <summary>
    /// The Markdown report: the rule table, then each rule's findings with the line of code. With
    /// <paramref name="blobUri"/> (the repository's <c>.../blob/&lt;sha&gt;/</c> URL), each location links to its line.
    /// </summary>
    public static string Markdown(ScanReport report, int findingsPerRule, string? blobUri)
    {
        var text = new StringBuilder();
        var summaries = report.RuleSummaries();
        var files = report
            .Findings.Select(finding => finding.Path)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var what = report.HasBaseline
            ? "new dependency injection lifetime problem"
            : "dependency injection lifetime problem";

        text.AppendLine(
            report.Findings.Count == 0
                ? $"## DI Scan: no {what}s"
                : Invariant(
                    $"## DI Scan: {Plural(report.Findings.Count, what)} ({Plural(summaries.Count, "rule")}, {Plural(files, "file")})"
                )
        );
        text.AppendLine();

        var notes = new List<string>();
        if (report.FilteredOut > 0)
            notes.Add(
                Invariant(
                    $"{Plural(report.FilteredOut, "finding")} left out by `--rules`, `--skip-rules` or `--exclude`."
                )
            );
        if (report.BaselineFindings.Count > 0)
            notes.Add(
                Invariant(
                    $"{Plural(report.BaselineFindings.Count, "finding")} already in the baseline {(report.BaselineFindings.Count == 1 ? "is" : "are")} not listed."
                )
            );
        if (notes.Count > 0)
        {
            text.AppendLine(string.Join(" ", notes));
            text.AppendLine();
        }

        if (report.Findings.Count > 0)
        {
            text.AppendLine("| Rule | Severity | Count | Title |");
            text.AppendLine("| --- | --- | ---: | --- |");
            foreach (var summary in summaries)
            {
                var id = summary.Rule.HelpUri is null
                    ? summary.Rule.Id
                    : $"[{summary.Rule.Id}]({summary.Rule.HelpUri})";
                text.AppendLine(
                    Invariant(
                        $"| {id} | {summary.Severity} | {summary.Count} | {Cell(summary.Rule.Title)} |"
                    )
                );
            }

            if (findingsPerRule > 0)
            {
                text.AppendLine();
                foreach (var summary in summaries)
                {
                    var findings = report
                        .Findings.Where(finding => finding.RuleId == summary.Rule.Id)
                        .ToList();
                    text.AppendLine(
                        Invariant(
                            $"<details><summary><b>{summary.Rule.Id}</b> {Html(summary.Rule.Title)} ({findings.Count})</summary>"
                        )
                    );
                    text.AppendLine();
                    foreach (var finding in findings.Take(findingsPerRule))
                    {
                        var location = ScanReport.Location(finding);
                        var link =
                            blobUri is not null && !Path.IsPathRooted(finding.Path)
                                ? Invariant(
                                    $"[`{location}`]({blobUri}{string.Join('/', finding.Path.Split('/').Select(Uri.EscapeDataString))}{(finding.Line > 0 ? $"#L{finding.Line}" : "")})"
                                )
                                : $"`{location}`";
                        text.AppendLine($"- {link}: {Inline(finding.Message)}");
                        var code = report.SourceLine(finding);
                        if (code is not null)
                        {
                            text.AppendLine("  ```csharp");
                            text.AppendLine("  " + code);
                            text.AppendLine("  ```");
                        }
                    }

                    if (findings.Count > findingsPerRule)
                        text.AppendLine(
                            Invariant(
                                $"- ...and {findings.Count - findingsPerRule} more in the SARIF report."
                            )
                        );
                    text.AppendLine();
                    text.AppendLine("</details>");
                    text.AppendLine();
                }
            }
        }

        text.AppendLine();
        text.AppendLine(
            "Keep these checks on in the editor and every build: `dotnet add package DependencyInjection.Lifetime.Analyzers`. [Rule catalog]("
                + ScanReport.RuleCatalogUri
                + ")"
        );
        return text.ToString();
    }

    /// <summary>
    /// <c>::error</c>, <c>::warning</c> and <c>::notice</c> workflow commands for the most severe findings, which GitHub
    /// shows on the pull request's changed lines. Findings outside the repository have no line to annotate.
    /// </summary>
    public static IEnumerable<string> Annotations(ScanReport report, int max = MaxAnnotations) =>
        report
            .Findings.Where(finding => !Path.IsPathRooted(finding.Path))
            .OrderBy(finding => SeverityRank(finding.Severity))
            .ThenBy(finding => finding.Path, StringComparer.Ordinal)
            .ThenBy(finding => finding.Line)
            .Take(max)
            .Select(finding =>
            {
                var command = finding.Severity switch
                {
                    "Error" => "error",
                    "Warning" => "warning",
                    _ => "notice",
                };
                var properties = new StringBuilder("file=").Append(Property(finding.Path));
                if (finding.Line > 0)
                    properties
                        .Append(",line=")
                        .Append(finding.Line.ToString(CultureInfo.InvariantCulture));
                if (finding.Line > 0 && finding.Column > 0)
                    properties
                        .Append(",col=")
                        .Append(finding.Column.ToString(CultureInfo.InvariantCulture));
                properties
                    .Append(",title=")
                    .Append(Property(finding.RuleId + " " + report.GetRule(finding.RuleId).Title));
                return $"::{command} {properties}::{Data(finding.Message)}";
            });

    /// <summary>The <c>.../blob/&lt;sha&gt;/</c> URL of the checked-out commit, from the variables GitHub Actions sets.</summary>
    public static string? BlobUri(Func<string, string?> environment)
    {
        var server = environment("GITHUB_SERVER_URL");
        var repository = environment("GITHUB_REPOSITORY");
        var sha = environment("GITHUB_SHA");
        return
            string.IsNullOrEmpty(server)
            || string.IsNullOrEmpty(repository)
            || string.IsNullOrEmpty(sha)
            ? null
            : $"{server.TrimEnd('/')}/{repository}/blob/{sha}/";
    }

    private static int SeverityRank(string severity) =>
        severity switch
        {
            "Error" => 0,
            "Warning" => 1,
            "Info" => 2,
            _ => 3,
        };

    // Workflow command escaping: https://github.com/actions/toolkit/blob/main/packages/core/src/command.ts
    private static string Data(string value) =>
        value.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");

    private static string Property(string value) =>
        Data(value).Replace(":", "%3A").Replace(",", "%2C");

    private static string Html(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Inline(string value) => Html(value).Replace("\r", " ").Replace("\n", " ");

    private static string Cell(string value) => Inline(value).Replace("|", "\\|");

    private static string Plural(int count, string noun) =>
        Invariant($"{count} {noun}{(count == 1 ? "" : "s")}");

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
