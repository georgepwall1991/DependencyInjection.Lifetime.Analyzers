using System.Text.Json;
using DependencyInjection.Lifetime.Analyzers.Scan;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Scan;

public sealed class ScanEndToEndTests
{
    [Fact]
    public void Scanner_BuildsATempProjectAndReportsDi003()
    {
        using var root = new TemporaryDirectory();
        WriteProject(root.Path);

        var sarif = Path.Combine(root.Path, "out.sarif");
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = ScanCommand.Run(
            [
                root.Path,
                "--rules",
                "DI003",
                "--fail-on",
                "warning",
                "--no-github",
                "--findings",
                "1",
                "--context",
                "1",
                "--sarif",
                sarif,
            ],
            output,
            error,
            analyzerAssemblyPath: typeof(DiagnosticDescriptors).Assembly.Location
        );

        Assert.True(File.Exists(sarif), error.ToString());
        Assert.Equal(ScanCommand.FindingsFound, exitCode);
        Assert.Contains("DI003", output.ToString());
        Assert.Contains("Program.cs:", output.ToString());

        using var document = JsonDocument.Parse(File.ReadAllText(sarif));
        var results = document.RootElement.GetProperty("runs")[0].GetProperty("results");
        Assert.Equal(1, results.GetArrayLength());
        Assert.Equal("DI003", results[0].GetProperty("ruleId").GetString());
    }

    [Fact]
    public void Scanner_TextBaselineSuppressesKnownFindings()
    {
        using var root = new TemporaryDirectory();
        WriteProject(root.Path);

        var baseline = Path.Combine(root.Path, "baseline.txt");
        var output = new StringWriter();
        var error = new StringWriter();
        var firstExit = ScanCommand.Run(
            [
                root.Path,
                "--rules",
                "DI003",
                "--no-github",
                "--update-baseline",
                baseline,
            ],
            output,
            error,
            analyzerAssemblyPath: typeof(DiagnosticDescriptors).Assembly.Location
        );
        Assert.Equal(ScanCommand.Success, firstExit);
        Assert.True(File.Exists(baseline));
        Assert.Contains("DI003", File.ReadAllText(baseline));

        var secondSarif = Path.Combine(root.Path, "second.sarif");
        output = new StringWriter();
        error = new StringWriter();
        var secondExit = ScanCommand.Run(
            [
                root.Path,
                "--rules",
                "DI003",
                "--fail-on",
                "warning",
                "--no-github",
                "--baseline",
                baseline,
                "--sarif",
                secondSarif,
            ],
            output,
            error,
            analyzerAssemblyPath: typeof(DiagnosticDescriptors).Assembly.Location
        );

        Assert.True(
            secondExit == ScanCommand.Success,
            $"baseline: {File.ReadAllText(baseline)}\noutput: {output}\nerror: {error}\nsarif: {File.ReadAllText(secondSarif)}"
        );
        Assert.Contains("no new dependency injection lifetime problems", output.ToString());
    }

    [Fact]
    public void Scanner_FixAppliesTheRuleCodeFix()
    {
        using var root = new TemporaryDirectory();
        WriteProject(root.Path);

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = ScanCommand.Run(
            [
                root.Path,
                "--fix",
                "--rules",
                "DI003",
                "--no-github",
                "--sarif",
                Path.Combine(root.Path, "fixed.sarif"),
            ],
            output,
            error,
            analyzerAssemblyPath: typeof(DiagnosticDescriptors).Assembly.Location
        );

        Assert.True(exitCode == ScanCommand.Success, $"{output}\n{error}");
        var source = File.ReadAllText(Path.Combine(root.Path, "Program.cs"));
        Assert.Contains("AddScoped<CaptiveService>()", source);
        Assert.DoesNotContain("AddSingleton<CaptiveService>()", source);
    }

    [Fact]
    public void Scanner_SuppressesFindingsInTestProjects()
    {
        using var root = new TemporaryDirectory();
        WriteProject(root.Path, assemblyName: "App.Tests");

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = ScanCommand.Run(
            [
                root.Path,
                "--rules",
                "DI003",
                "--fail-on",
                "warning",
                "--no-github",
                "--sarif",
                Path.Combine(root.Path, "test.sarif"),
            ],
            output,
            error,
            analyzerAssemblyPath: typeof(DiagnosticDescriptors).Assembly.Location
        );

        Assert.True(exitCode == ScanCommand.Success, $"{output}\n{error}");
        Assert.Contains("no dependency injection lifetime problems", output.ToString());
    }

    private static void WriteProject(string directory, string? assemblyName = null)
    {
        var assemblyNameProperty = assemblyName is null
            ? ""
            : $"    <AssemblyName>{assemblyName}</AssemblyName>\n";
        File.WriteAllText(
            Path.Combine(directory, "App.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
            {assemblyNameProperty}  </PropertyGroup>
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
              </ItemGroup>
            </Project>
            """
        );
        File.WriteAllText(
            Path.Combine(directory, "Program.cs"),
            """
            using Microsoft.Extensions.DependencyInjection;

            public interface IScopedService { }
            public sealed class ScopedService : IScopedService { }
            public sealed class CaptiveService
            {
                public CaptiveService(IScopedService scoped) { }
            }

            public static class Program
            {
                public static void Main()
                {
                    var services = new ServiceCollection();
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<CaptiveService>();
                }
            }
            """
        );
    }
}
