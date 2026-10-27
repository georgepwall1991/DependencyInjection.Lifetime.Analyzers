using Xunit;
using System.Text.Json;
using DependencyInjection.Lifetime.Analyzers.Scan;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Scan;

public sealed class ScanReportTests
{
    [Fact]
    public void Constructor_DeduplicatesAcrossFrameworksAndOrdersByPath()
    {
        using var root = new TemporaryDirectory();
        var report = new ScanReport(
            root.Path,
            [
                new Finding("DI003", "Warning", "b", "b.cs", 2, 1),
                new Finding("DI003", "Warning", "a", "a.cs", 3, 1),
                new Finding("DI003", "Warning", "a", "a.cs", 3, 1),
            ],
            new Dictionary<string, RuleInfo>()
        );

        Assert.Equal(2, report.Findings.Count);
        Assert.Equal("a.cs", report.Findings[0].Path);
        Assert.Equal("b.cs", report.Findings[1].Path);
    }

    [Fact]
    public void RenderText_ShowsLocationSeveritySourceAndDocs()
    {
        using var root = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(root.Path, "Program.cs"),
            """
            var services = new ServiceCollection();
            services.AddSingleton<Captive>();
            """
        );

        var report = new ScanReport(
            root.Path,
            [new Finding("DI003", "Warning", "captive", "Program.cs", 2, 5)],
            new Dictionary<string, RuleInfo>()
        );

        var text = report.RenderText(10, "di-scan.sarif", 3, 1);

        Assert.Contains("Program.cs:2:5", text);
        Assert.Contains("DI003", text);
        Assert.Contains("Warning", text);
        Assert.Contains(">     2 | services.AddSingleton<Captive>();", text);
        Assert.Contains("docs/rules/DI003.md", text);
        Assert.Contains("SARIF report: di-scan.sarif", text);
    }

    [Fact]
    public void ApplyBaseline_MovesKnownFindingsOutOfTheFailingSet()
    {
        using var root = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(root.Path, "Program.cs"), "one\ntwo\n");
        var report = new ScanReport(
            root.Path,
            [
                new Finding("DI003", "Warning", "known", "Program.cs", 1, 1),
                new Finding("DI016", "Warning", "new", "Program.cs", 2, 1),
            ],
            new Dictionary<string, RuleInfo>()
        );
        var baseline = ScanBaseline.Parse("DI003 Program.cs:1:1\n");

        var filtered = report.ApplyBaseline(baseline);

        Assert.True(filtered.HasBaseline);
        Assert.Single(filtered.Findings);
        Assert.Equal("DI016", filtered.Findings[0].RuleId);
        Assert.Single(filtered.BaselineFindings);
        Assert.Equal(0, filtered.CountAtLeast("Error"));
        Assert.Equal(1, filtered.CountAtLeast("Warning"));
    }

    [Fact]
    public void WriteSarif_IncludesRuleMetadataFingerprintAndBaselineState()
    {
        using var root = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(root.Path, "Program.cs"), "one\ntwo\n");
        var report = new ScanReport(
            root.Path,
            [new Finding("DI003", "Warning", "captive", "Program.cs", 1, 1)],
            new Dictionary<string, RuleInfo>()
        );
        var baseline = ScanBaseline.Parse("DI003 Program.cs:1:1\n");
        var filtered = report.ApplyBaseline(baseline);

        using var stream = new MemoryStream();
        filtered.WriteSarif(stream, "3.7.8");

        using var document = JsonDocument.Parse(stream.ToArray());
        var run = document.RootElement.GetProperty("runs")[0];
        var rule = run.GetProperty("tool").GetProperty("driver").GetProperty("rules")[0];
        Assert.Equal("DI003", rule.GetProperty("id").GetString());
        Assert.Equal(
            "warning",
            rule.GetProperty("defaultConfiguration").GetProperty("level").GetString()
        );
        Assert.Contains("docs/rules/DI003.md", rule.GetProperty("helpUri").GetString());

        var result = run.GetProperty("results")[0];
        Assert.Equal("unchanged", result.GetProperty("baselineState").GetString());
        Assert.True(
            result
                .GetProperty("partialFingerprints")
                .TryGetProperty(ScanReport.FingerprintKey, out _)
        );
    }

    [Fact]
    public void Fingerprint_IsStableWhenTheFindingMovesWithItsSourceLine()
    {
        using var root = new TemporaryDirectory();
        var path = Path.Combine(root.Path, "Program.cs");
        File.WriteAllText(
            path,
            """
            // header
            // header
            services.AddSingleton<A>();
            """
        );
        var first = new ScanReport(
            root.Path,
            [new Finding("DI003", "Warning", "captive", "Program.cs", 3, 1)],
            new Dictionary<string, RuleInfo>()
        );

        File.WriteAllText(
            path,
            """
            // new header
            // header
            // header
            services.AddSingleton<A>();
            """
        );
        var moved = new ScanReport(
            root.Path,
            [new Finding("DI003", "Warning", "captive", "Program.cs", 4, 1)],
            new Dictionary<string, RuleInfo>()
        );

        Assert.Equal(first.Fingerprint(first.Findings[0]), moved.Fingerprint(moved.Findings[0]));
    }

    [Fact]
    public void Fingerprint_ChangesWhenTheFindingMovesToDifferentCode()
    {
        using var root = new TemporaryDirectory();
        var path = Path.Combine(root.Path, "Program.cs");
        File.WriteAllText(path, "services.AddSingleton<A>();\nservices.AddSingleton<B>();\n");
        var first = new ScanReport(
            root.Path,
            [new Finding("DI003", "Warning", "captive", "Program.cs", 1, 1)],
            new Dictionary<string, RuleInfo>()
        );
        var different = new ScanReport(
            root.Path,
            [new Finding("DI003", "Warning", "captive", "Program.cs", 2, 1)],
            new Dictionary<string, RuleInfo>()
        );

        Assert.NotEqual(
            first.Fingerprint(first.Findings[0]),
            different.Fingerprint(different.Findings[0])
        );
    }

    [Fact]
    public void TextBaseline_RoundTripsThroughTheParser()
    {
        using var root = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(root.Path, "Program.cs"), "one\ntwo\n");
        var report = new ScanReport(
            root.Path,
            [new Finding("DI003", "Warning", "captive", "Program.cs", 2, 5)],
            new Dictionary<string, RuleInfo>()
        );
        var writer = new StringWriter();

        report.WriteTextBaseline(writer, "3.9.3");
        var baseline = ScanBaseline.Parse(writer.ToString());

        Assert.Equal(1, baseline.Count);
        Assert.True(
            baseline.Contains(
                new Finding("DI003", "Warning", "captive", "Program.cs", 2, 5),
                "unused"
            )
        );
    }

    [Fact]
    public void HtmlReport_IncludesFindingAndSurroundingSource()
    {
        using var root = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(root.Path, "Program.cs"),
            """
            var services = new ServiceCollection();
            services.AddSingleton<Captive>();
            """
        );
        var report = new ScanReport(
            root.Path,
            [new Finding("DI003", "Warning", "captive <script>", "Program.cs", 2, 5)],
            new Dictionary<string, RuleInfo>()
        );

        var html = HtmlReport.Render(report, "3.7.8", root.Path, DateTimeOffset.UnixEpoch);

        Assert.Contains("DI003", html);
        Assert.Contains("Program.cs:2:5", html);
        Assert.Contains("captive &lt;script&gt;", html);
        Assert.Contains("services.AddSingleton&lt;Captive&gt;();", html);
        Assert.DoesNotContain("<script>captive", html);
    }

    [Fact]
    public void MarkdownReport_IncludesRuleTableAndSourceLine()
    {
        using var root = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(root.Path, "Program.cs"), "one\ntwo\n");
        var report = new ScanReport(
            root.Path,
            [new Finding("DI003", "Warning", "captive", "Program.cs", 2, 1)],
            new Dictionary<string, RuleInfo>()
        );

        var markdown = GitHubOutput.Markdown(report, 3, null);

        Assert.Contains("DI003", markdown);
        Assert.Contains("| Warning | 1 |", markdown);
        Assert.Contains("Program.cs:2:1", markdown);
        Assert.Contains("two", markdown);
    }

    [Fact]
    public void CountAtLeast_RespectsSeverityOrder()
    {
        using var root = new TemporaryDirectory();
        var report = new ScanReport(
            root.Path,
            [
                new Finding("DI013", "Error", "", "a.cs", 1, 1),
                new Finding("DI003", "Warning", "", "a.cs", 2, 1),
                new Finding("DI007", "Info", "", "a.cs", 3, 1),
            ],
            new Dictionary<string, RuleInfo>()
        );

        Assert.Equal(1, report.CountAtLeast("Error"));
        Assert.Equal(2, report.CountAtLeast("Warning"));
        Assert.Equal(3, report.CountAtLeast("Info"));
    }
}
