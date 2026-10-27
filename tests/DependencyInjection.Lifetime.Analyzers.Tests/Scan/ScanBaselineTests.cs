using Xunit;
using DependencyInjection.Lifetime.Analyzers.Scan;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Scan;

public sealed class ScanBaselineTests
{
    [Fact]
    public void TextBaseline_MatchesRuleAndExactLocation()
    {
        var baseline = ScanBaseline.Parse(
            """
            # repository: example/App
            # commit: 0123456789abcdef0123456789abcdef01234567
            DI003 src/App/Startup.cs:42:17
            DI016 src/App/Startup.cs:88:9
            """
        );

        Assert.Equal(2, baseline.Count);
        Assert.True(
            baseline.Contains(
                new Finding("DI003", "Warning", "", "src/App/Startup.cs", 42, 17),
                "unused"
            )
        );
        Assert.True(
            baseline.Contains(
                new Finding("DI003", "Warning", "", "src\\App\\Startup.cs", 42, 17),
                "unused"
            )
        );
        Assert.False(
            baseline.Contains(
                new Finding("DI003", "Warning", "", "src/App/Startup.cs", 43, 17),
                "unused"
            )
        );
    }

    [Fact]
    public void TextBaseline_MatchesTheDi012bSubRuleCaseInsensitively()
    {
        var baseline = ScanBaseline.Parse("DI012b src/App/Startup.cs:10:5\n");

        Assert.True(
            baseline.Contains(
                new Finding("DI012b", "Info", "", "src/App/Startup.cs", 10, 5),
                "unused"
            )
        );
        Assert.True(
            baseline.Contains(
                new Finding("di012B", "Info", "", "src/App/Startup.cs", 10, 5),
                "unused"
            )
        );
    }

    [Fact]
    public void TextBaseline_RejectsAMalformedLine()
    {
        var exception = Assert.Throws<FormatException>(() =>
            ScanBaseline.Parse("DI003 src/App/Startup.cs")
        );

        Assert.Contains("Line 1", exception.Message);
    }

    [Fact]
    public void SarifBaseline_MatchesByFingerprintAcrossMovedLines()
    {
        using var root = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(root.Path, "Program.cs"),
            """
            services.AddSingleton<A>();
            services.AddSingleton<B>();
            """
        );

        var report = new ScanReport(
            root.Path,
            [new Finding("DI003", "Warning", "captive", "Program.cs", 1, 1)],
            new Dictionary<string, RuleInfo>()
        );
        using var stream = new MemoryStream();
        report.WriteSarif(stream, "1.0.0");
        var baseline = ScanBaseline.Parse(System.Text.Encoding.UTF8.GetString(stream.ToArray()));

        Assert.Equal(1, baseline.Count);
        Assert.True(
            baseline.Contains(
                new Finding("DI003", "Warning", "captive", "Program.cs", 99, 7),
                report.Fingerprint(report.Findings[0])
            )
        );
        Assert.False(
            baseline.Contains(
                new Finding("DI016", "Warning", "build provider", "Program.cs", 99, 7),
                "different"
            )
        );
    }

    [Fact]
    public void SarifBaseline_SkipsResultsMarkedAbsent()
    {
        const string sarif = """
            {
              "version": "2.1.0",
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "DI003",
                      "baselineState": "absent",
                      "message": { "text": "captive" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "Program.cs" },
                            "region": { "startLine": 1, "startColumn": 1 }
                          }
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """;

        var baseline = ScanBaseline.Parse(sarif);
        Assert.Equal(0, baseline.Count);
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "di-scan-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
