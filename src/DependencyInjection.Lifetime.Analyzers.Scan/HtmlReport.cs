using System.Globalization;
using System.Net;
using System.Text;

namespace DependencyInjection.Lifetime.Analyzers.Scan;

/// <summary>
/// The report as one self-contained HTML page: no scripts or styles from elsewhere, so it can be attached to a ticket,
/// mailed or kept as a build artifact and still open anywhere. It lists every finding with the code around it, and a
/// search box and severity toggles narrow the list.
/// </summary>
internal static class HtmlReport
{
    /// <summary>Lines of code shown above and below each finding.</summary>
    private const int ContextLines = 2;

    public static string Render(
        ScanReport report,
        string toolVersion,
        string scannedPath,
        DateTimeOffset scannedAt
    )
    {
        var summaries = report.RuleSummaries();
        var files = report
            .Findings.Select(finding => finding.Path)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var severities = summaries
            .Select(summary => summary.Severity)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var name = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(scannedPath))
        );
        var html = new StringBuilder();

        html.Append(
            """
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            """
        );
        html.AppendLine();
        html.AppendLine($"<title>DI Scan report: {E(name)}</title>");
        html.AppendLine($"<style>{Styles}</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("<header>");
        html.AppendLine(
            $"<p class=\"eyebrow\">DI Scan {E(toolVersion)} &middot; dependency injection lifetime scan</p>"
        );
        html.AppendLine($"<h1>{E(name)}</h1>");
        html.AppendLine(
            $"<p class=\"meta\">{E(scannedPath)} &middot; scanned {E(scannedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture))}</p>"
        );
        html.AppendLine("</header>");
        html.AppendLine("<main>");

        html.AppendLine("<section class=\"stats\">");
        Stat(html, report.Findings.Count, report.HasBaseline ? "new problems" : "problems");
        Stat(html, summaries.Count, "rules");
        Stat(html, files, "files");
        foreach (var severity in new[] { "Error", "Warning" })
        {
            var count = report.Findings.Count(finding => finding.Severity == severity);
            if (count > 0)
                Stat(
                    html,
                    count,
                    severity == "Error" ? "errors" : "warnings",
                    severity.ToLowerInvariant()
                );
        }
        html.AppendLine("</section>");

        var notes = new List<string>();
        if (report.FilteredOut > 0)
            notes.Add(
                $"{Count(report.FilteredOut, "finding")} left out by --rules, --skip-rules or --exclude."
            );
        if (report.BaselineFindings.Count > 0)
            notes.Add(
                $"{Count(report.BaselineFindings.Count, "finding")} already in the baseline {(report.BaselineFindings.Count == 1 ? "is" : "are")} not listed."
            );
        if (notes.Count > 0)
            html.AppendLine($"<p class=\"note\">{E(string.Join(" ", notes))}</p>");

        if (report.Findings.Count == 0)
        {
            html.AppendLine(
                $"<p class=\"empty\">DI Scan found no {(report.HasBaseline ? "new " : "")}dependency injection lifetime problems.</p>"
            );
        }
        else
        {
            html.AppendLine("<section class=\"rules\">");
            html.AppendLine("<h2>Rules that fired</h2>");
            html.AppendLine(
                "<table><thead><tr><th>Rule</th><th>Severity</th><th class=\"num\">Count</th><th>Title</th></tr></thead><tbody>"
            );
            foreach (var summary in summaries)
            {
                html.AppendLine(
                    $"<tr><td><a href=\"#{E(summary.Rule.Id)}\">{E(summary.Rule.Id)}</a></td><td>{Badge(summary.Severity)}</td><td class=\"num\">{summary.Count}</td><td>{E(summary.Rule.Title)}</td></tr>"
                );
            }
            html.AppendLine("</tbody></table>");
            html.AppendLine("</section>");

            html.AppendLine("<section class=\"findings\">");
            html.AppendLine("<div class=\"toolbar\">");
            html.AppendLine("<h2>Findings</h2>");
            html.AppendLine(
                "<input id=\"search\" type=\"search\" placeholder=\"Filter by file, rule or message\" aria-label=\"Filter findings\">"
            );
            foreach (var severity in severities)
                html.AppendLine(
                    $"<label class=\"toggle\"><input type=\"checkbox\" data-severity=\"{E(severity)}\" checked> {E(severity)}</label>"
                );
            html.AppendLine("<span id=\"shown\" class=\"shown\"></span>");
            html.AppendLine("</div>");

            foreach (var summary in summaries)
            {
                var findings = report
                    .Findings.Where(finding => finding.RuleId == summary.Rule.Id)
                    .ToList();
                html.AppendLine($"<details class=\"rule\" id=\"{E(summary.Rule.Id)}\" open>");
                html.AppendLine(
                    $"<summary><span class=\"id\">{E(summary.Rule.Id)}</span> {E(summary.Rule.Title)} {Badge(summary.Severity)} <span class=\"count\">{findings.Count}</span></summary>"
                );
                if (summary.Rule.Description is not null || summary.Rule.HelpUri is not null)
                {
                    html.Append("<p class=\"about\">");
                    if (summary.Rule.Description is not null)
                        html.Append(E(summary.Rule.Description)).Append(' ');
                    if (summary.Rule.HelpUri is not null)
                        html.Append(
                            $"<a href=\"{E(summary.Rule.HelpUri)}\">What it catches and how to fix it</a>"
                        );
                    html.AppendLine("</p>");
                }

                foreach (var finding in findings)
                {
                    var search =
                        $"{finding.RuleId} {finding.Path} {finding.Message}".ToLowerInvariant();
                    html.AppendLine(
                        $"<article class=\"finding\" data-severity=\"{E(finding.Severity)}\" data-search=\"{E(search)}\">"
                    );
                    html.AppendLine(
                        $"<p class=\"where\"><code>{E(ScanReport.Location(finding))}</code></p>"
                    );
                    html.AppendLine($"<p class=\"message\">{E(finding.Message)}</p>");
                    var context = report.SourceContext(finding, ContextLines);
                    if (context.Count > 0)
                    {
                        var indent = context
                            .Where(line => line.Text.Trim().Length > 0)
                            .Select(line => line.Text.Length - line.Text.TrimStart().Length)
                            .DefaultIfEmpty(0)
                            .Min();
                        html.Append("<pre>");
                        foreach (var (number, text) in context)
                        {
                            var code = text.Length >= indent ? text[indent..] : text.TrimStart();
                            html.Append(
                                $"<span class=\"line{(number == finding.Line ? " hit" : "")}\"><span class=\"ln\">{number}</span>{E(code)}</span>"
                            );
                        }
                        html.AppendLine("</pre>");
                    }
                    html.AppendLine("</article>");
                }

                html.AppendLine("</details>");
            }
            html.AppendLine("</section>");
        }

        html.AppendLine("</main>");
        html.AppendLine("<footer>");
        html.AppendLine(
            $"<p>Keep these checks on in the editor and every build: <code>dotnet add package DependencyInjection.Lifetime.Analyzers</code>. <a href=\"{ScanReport.RuleCatalogUri}\">Rule catalog</a></p>"
        );
        html.AppendLine("</footer>");
        html.AppendLine($"<script>{Script}</script>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return html.ToString();
    }

    private static void Stat(StringBuilder html, int value, string label, string? tone = null) =>
        html.AppendLine(
            $"<div class=\"stat{(tone is null ? "" : " " + tone)}\"><span class=\"value\">{value.ToString("N0", CultureInfo.InvariantCulture)}</span><span class=\"label\">{E(label)}</span></div>"
        );

    private static string Badge(string severity) =>
        $"<span class=\"badge {E(severity.ToLowerInvariant())}\">{E(severity)}</span>";

    private static string Count(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? "" : "s")}");

    private static string E(string value) => WebUtility.HtmlEncode(value);

    private const string Styles = """
        :root { --bg: #ffffff; --fg: #1d1f24; --muted: #5d6470; --line: #e3e6eb; --panel: #f6f7f9; --accent: #6b3fa0;
          --error: #b42318; --warning: #9a5b00; --info: #1d5fa8; --hit: #fff4c2; color-scheme: light dark; }
        @media (prefers-color-scheme: dark) {
          :root { --bg: #16181d; --fg: #e6e8ec; --muted: #9aa1ad; --line: #2c3038; --panel: #1e2128; --accent: #b692e8;
            --error: #ff8a80; --warning: #f5c26b; --info: #8cb8ff; --hit: #3d3514; }
        }
        * { box-sizing: border-box; }
        body { margin: 0; background: var(--bg); color: var(--fg); font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
        header, main, footer { max-width: 1040px; margin: 0 auto; padding: 0 16px; }
        header { padding-top: 32px; }
        .eyebrow { margin: 0; color: var(--accent); font-weight: 600; font-size: 13px; letter-spacing: .02em; }
        h1 { margin: 4px 0; font-size: 28px; }
        h2 { font-size: 18px; margin: 0; }
        .meta, .note, .about, footer { color: var(--muted); }
        .meta { margin: 0 0 24px; overflow-wrap: anywhere; }
        .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: 12px; margin-bottom: 16px; }
        .stat { background: var(--panel); border: 1px solid var(--line); border-radius: 10px; padding: 12px 14px; }
        .stat .value { display: block; font-size: 26px; font-weight: 700; font-variant-numeric: tabular-nums; }
        .stat .label { color: var(--muted); font-size: 13px; }
        .stat.error .value { color: var(--error); } .stat.warning .value { color: var(--warning); }
        .rules { margin: 24px 0; overflow-x: auto; }
        table { border-collapse: collapse; width: 100%; margin-top: 8px; }
        th, td { text-align: left; padding: 6px 10px; border-bottom: 1px solid var(--line); vertical-align: top; }
        th { font-size: 13px; color: var(--muted); font-weight: 600; }
        .num { text-align: right; font-variant-numeric: tabular-nums; }
        a { color: var(--accent); }
        .badge { display: inline-block; font-size: 12px; font-weight: 600; padding: 1px 8px; border-radius: 999px; border: 1px solid currentColor; }
        .badge.error { color: var(--error); } .badge.warning { color: var(--warning); } .badge.info, .badge.hidden { color: var(--info); }
        .toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 10px 16px; position: sticky; top: 0; background: var(--bg); padding: 12px 0; border-bottom: 1px solid var(--line); z-index: 1; }
        #search { flex: 1 1 220px; min-width: 0; padding: 7px 10px; border: 1px solid var(--line); border-radius: 8px; background: var(--panel); color: var(--fg); font: inherit; }
        .toggle { font-size: 14px; white-space: nowrap; } .shown { color: var(--muted); font-size: 13px; }
        details.rule { border: 1px solid var(--line); border-radius: 10px; margin: 14px 0; background: var(--bg); }
        details.rule > summary { cursor: pointer; padding: 12px 14px; font-weight: 600; display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
        details.rule > summary .id { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; color: var(--accent); }
        details.rule > summary .count { margin-left: auto; color: var(--muted); font-variant-numeric: tabular-nums; }
        .about { margin: 0 14px 8px; font-size: 14px; }
        .finding { border-top: 1px solid var(--line); padding: 10px 14px; }
        .finding p { margin: 0 0 6px; } .where code { overflow-wrap: anywhere; }
        code, pre { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 13px; }
        pre { margin: 6px 0 0; background: var(--panel); border: 1px solid var(--line); border-radius: 8px; padding: 8px 0; overflow-x: auto; }
        pre .line { display: block; padding: 0 12px 0 0; white-space: pre; }
        pre .line.hit { background: var(--hit); }
        pre .ln { display: inline-block; width: 4.5em; padding-right: 12px; text-align: right; color: var(--muted); user-select: none; }
        .empty { font-size: 18px; margin: 32px 0; }
        footer { padding: 24px 16px 40px; font-size: 14px; }
        [hidden] { display: none !important; }
        @media (max-width: 600px) { .rules th:nth-child(2), .rules td:nth-child(2) { display: none; } h1 { font-size: 24px; } }
        """;

    private const string Script = """
        (function () {
          var search = document.getElementById('search');
          if (!search) return;
          var toggles = Array.prototype.slice.call(document.querySelectorAll('input[data-severity]'));
          var findings = Array.prototype.slice.call(document.querySelectorAll('.finding'));
          var rules = Array.prototype.slice.call(document.querySelectorAll('details.rule'));
          var shown = document.getElementById('shown');
          function apply() {
            var terms = search.value.toLowerCase().split(/\s+/).filter(Boolean);
            var on = {};
            toggles.forEach(function (t) { on[t.getAttribute('data-severity')] = t.checked; });
            var count = 0;
            findings.forEach(function (f) {
              var text = f.getAttribute('data-search');
              var visible = on[f.getAttribute('data-severity')] !== false && terms.every(function (term) { return text.indexOf(term) >= 0; });
              f.hidden = !visible;
              if (visible) count++;
            });
            rules.forEach(function (r) { r.hidden = !r.querySelector('.finding:not([hidden])'); });
            shown.textContent = count === findings.length ? '' : count + ' of ' + findings.length + ' shown';
          }
          search.addEventListener('input', apply);
          toggles.forEach(function (t) { t.addEventListener('change', apply); });
        })();
        """;
}
