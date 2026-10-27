using Xunit;
using DependencyInjection.Lifetime.Analyzers.Scan;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Scan;

public sealed class ScanCommandTests
{
    [Fact]
    public void Help_PrintsUsageAndSucceeds()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ScanCommand.Run(["--help"], output, error);

        Assert.Equal(ScanCommand.Success, exitCode);
        Assert.Contains("Usage: di-scan", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void Version_PrintsTheToolVersion()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ScanCommand.Run(["--version"], output, error);

        Assert.Equal(ScanCommand.Success, exitCode);
        Assert.StartsWith("di-scan ", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void MissingTarget_IsAUsageError()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ScanCommand.Run(["does-not-exist.sln"], output, error);

        Assert.Equal(ScanCommand.UsageError, exitCode);
        Assert.Contains("Nothing to scan", error.ToString());
    }

    [Fact]
    public void BadBaseline_IsAUsageError()
    {
        using var root = new TemporaryDirectory();
        var target = Path.Combine(root.Path, "App.csproj");
        File.WriteAllText(target, "<Project />");
        var baseline = Path.Combine(root.Path, "baseline.txt");
        File.WriteAllText(baseline, "not a finding");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ScanCommand.Run([target, "--baseline", baseline], output, error);

        Assert.Equal(ScanCommand.UsageError, exitCode);
        Assert.Contains("Cannot read the baseline", error.ToString());
    }

    [Fact]
    public void MissingAnalyzer_IsAScanFailure()
    {
        using var root = new TemporaryDirectory();
        var target = Path.Combine(root.Path, "App.csproj");
        File.WriteAllText(target, "<Project />");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ScanCommand.Run(
            [target, "--sarif", Path.Combine(root.Path, "out.sarif")],
            output,
            error,
            analyzerAssemblyPath: Path.Combine(root.Path, "missing.dll")
        );

        Assert.Equal(ScanCommand.ScanFailed, exitCode);
        Assert.Contains("analyzer is missing", error.ToString());
    }
}
