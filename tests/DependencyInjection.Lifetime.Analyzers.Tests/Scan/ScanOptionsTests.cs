using Xunit;
using DependencyInjection.Lifetime.Analyzers.Scan;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Scan;

public sealed class ScanOptionsTests
{
    [Fact]
    public void NoArguments_ScansTheCurrentDirectoryWithDefaults()
    {
        Assert.True(ScanOptions.TryParse([], out var options, out var error));

        Assert.Null(error);
        Assert.Equal(".", options.Target);
        Assert.Equal("di-scan.sarif", options.SarifPath);
        Assert.Null(options.Configuration);
        Assert.Null(options.Framework);
        Assert.False(options.NoRestore);
        Assert.False(options.Verbose);
        Assert.Equal(10, options.Top);
        Assert.Equal(3, options.FindingsPerRule);
        Assert.Equal(2, options.Context);
    }

    [Fact]
    public void EveryOption_IsParsed()
    {
        Assert.True(
            ScanOptions.TryParse(
                [
                    "src/App.slnx",
                    "--sarif",
                    "out/x.sarif",
                    "-c",
                    "Release",
                    "-f",
                    "net10.0",
                    "--no-restore",
                    "--top",
                    "3",
                    "--findings",
                    "7",
                    "--context",
                    "5",
                    "-v",
                ],
                out var options,
                out var error
            ),
            error
        );

        Assert.Equal("src/App.slnx", options.Target);
        Assert.Equal("out/x.sarif", options.SarifPath);
        Assert.Equal("Release", options.Configuration);
        Assert.Equal("net10.0", options.Framework);
        Assert.True(options.NoRestore);
        Assert.Equal(3, options.Top);
        Assert.Equal(7, options.FindingsPerRule);
        Assert.Equal(5, options.Context);
        Assert.True(options.Verbose);
    }

    [Fact]
    public void FixAndUpdateBaseline_AreParsed()
    {
        Assert.True(
            ScanOptions.TryParse(
                ["src/App.slnx", "--fix", "--update-baseline", "di.baseline"],
                out var options,
                out var error
            ),
            error
        );

        Assert.True(options.Fix);
        Assert.Equal("di.baseline", options.UpdateBaselinePath);
    }

    [Fact]
    public void BaselineAndUpdateBaseline_CannotBeCombined()
    {
        Assert.False(
            ScanOptions.TryParse(
                ["--baseline", "old.sarif", "--update-baseline", "new.txt"],
                out _,
                out var error
            )
        );
        Assert.Contains("Pass one", error);
    }

    [Fact]
    public void Filters_AccumulateAndNormalizeRuleIds()
    {
        Assert.True(
            ScanOptions.TryParse(
                [
                    "--rules",
                    "di003, DI016",
                    "--rules",
                    "DI019",
                    "--skip-rules",
                    "DI007",
                    "--exclude",
                    "tests/**",
                    "--exclude",
                    "Legacy",
                    "--fail-on",
                    "Warning",
                    "--baseline",
                    "base.sarif",
                    "--summary",
                    "s.md",
                    "--no-github",
                    "--html",
                    "r.html",
                ],
                out var options,
                out var error
            ),
            error
        );

        Assert.Equal(["DI003", "DI016", "DI019"], options.Rules);
        Assert.Equal(["DI007"], options.SkippedRules);
        Assert.Equal(["tests/**", "Legacy"], options.Excludes);
        Assert.Equal("Warning", options.FailOn);
        Assert.Equal("base.sarif", options.BaselinePath);
        Assert.Equal("s.md", options.SummaryPath);
        Assert.True(options.NoGitHub);
        Assert.Equal("r.html", options.HtmlPath);
    }

    [Theory]
    [InlineData("error", "Error")]
    [InlineData("warning", "Warning")]
    [InlineData("info", "Info")]
    [InlineData("note", "Info")]
    [InlineData("none", null)]
    public void FailOn_TakesASeverity(string value, string? expected)
    {
        Assert.True(ScanOptions.TryParse(["--fail-on", value], out var options, out _));
        Assert.Equal(expected, options.FailOn);
    }

    [Theory]
    [InlineData("all", int.MaxValue)]
    [InlineData("ALL", int.MaxValue)]
    [InlineData("0", 0)]
    [InlineData("12", 12)]
    public void Findings_TakesACountOrAll(string value, int expected)
    {
        Assert.True(ScanOptions.TryParse(["--findings", value], out var options, out _));
        Assert.Equal(expected, options.FindingsPerRule);
    }

    [Theory]
    [InlineData("--rules", "HCR001")]
    [InlineData("--rules", "DI999")]
    [InlineData("--rules", "DI003,")]
    [InlineData("--skip-rules", "not-a-rule")]
    public void UnknownRuleIds_AreRejected(string option, string value)
    {
        Assert.False(ScanOptions.TryParse([option, value], out _, out var error));
        Assert.Contains("DI rule IDs", error);
    }

    [Fact]
    public void MissingValue_IsRejected()
    {
        Assert.False(ScanOptions.TryParse(["--sarif"], out _, out var error));
        Assert.Equal("--sarif needs a value.", error);
    }

    [Fact]
    public void TwoTargets_AreRejected()
    {
        Assert.False(ScanOptions.TryParse(["a.sln", "b.sln"], out _, out var error));
        Assert.Contains("Only one path", error);
    }

    [Fact]
    public void UnknownOption_IsRejected()
    {
        Assert.False(ScanOptions.TryParse(["--frobnicate"], out _, out var error));
        Assert.Equal("Unknown option '--frobnicate'.", error);
    }
}
