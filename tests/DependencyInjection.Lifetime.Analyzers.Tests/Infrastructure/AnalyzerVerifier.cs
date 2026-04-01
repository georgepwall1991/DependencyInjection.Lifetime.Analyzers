using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

/// <summary>
/// Helper class for verifying analyzer diagnostics in tests.
/// </summary>
public static class AnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    /// <summary>
    /// Reference assemblies using .NET 6 with the DI 6.0 packages.
    /// </summary>
    public static ReferenceAssemblies ReferenceAssembliesWithDi60 { get; } =
        ReferenceAssemblies.Net.Net60
            .AddPackages([
                new PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "6.0.0"),
                new PackageIdentity("Microsoft.Extensions.DependencyInjection", "6.0.0")
            ]);

    /// <summary>
    /// Reference assemblies using .NET 8 with the DI 8.0 packages.
    /// </summary>
    public static ReferenceAssemblies ReferenceAssembliesWithDi80 { get; } =
        ReferenceAssemblies.Net.Net80
            .AddPackages([
                new PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0"),
                new PackageIdentity("Microsoft.Extensions.DependencyInjection", "8.0.0")
            ]);

    /// <summary>
    /// Reference assemblies using .NET 8 with the latest DI packages used by the test project.
    /// </summary>
    public static ReferenceAssemblies ReferenceAssembliesWithLatestDi { get; } =
        ReferenceAssemblies.Net.Net80
            .AddPackages([
                new PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "10.0.2"),
                new PackageIdentity("Microsoft.Extensions.DependencyInjection", "10.0.2")
            ]);

    /// <summary>
    /// Reference assemblies with DI 8.0.0 for keyed service support.
    /// Only Abstractions is referenced to avoid duplicate extension method ambiguity.
    /// </summary>
    public static ReferenceAssemblies ReferenceAssembliesWithKeyedDi { get; } =
        ReferenceAssemblies.Net.Net80
            .AddPackages([
                new PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0")
            ]);

    /// <summary>
    /// Reference assemblies with the latest keyed DI APIs used by the test project.
    /// Only Abstractions is referenced to avoid duplicate extension method ambiguity.
    /// </summary>
    public static ReferenceAssemblies ReferenceAssembliesWithLatestKeyedDi { get; } =
        ReferenceAssemblies.Net.Net80
            .AddPackages([
                new PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "10.0.2")
            ]);

    /// <summary>
    /// Verifies that the analyzer produces no diagnostics for the given source.
    /// </summary>
    public static async Task VerifyNoDiagnosticsAsync(string source)
    {
        var test = CreateTest(source);
        await test.RunAsync();
    }

    /// <summary>
    /// Verifies that the analyzer produces no diagnostics for the given source and editorconfig.
    /// </summary>
    public static async Task VerifyNoDiagnosticsAsync(string source, string editorConfig)
    {
        var test = CreateTest(source, editorConfig);
        await test.RunAsync();
    }

    /// <summary>
    /// Verifies that the analyzer produces the expected diagnostics.
    /// </summary>
    public static async Task VerifyDiagnosticsAsync(string source, params DiagnosticResult[] expected)
    {
        var test = CreateTest(source);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    /// <summary>
    /// Verifies that the analyzer produces the expected diagnostics for the given source and editorconfig.
    /// </summary>
    public static async Task VerifyDiagnosticsAsync(string source, string editorConfig, params DiagnosticResult[] expected)
    {
        var test = CreateTest(source, editorConfig);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    /// <summary>
    /// Verifies that the analyzer produces the expected diagnostics across multiple source files.
    /// </summary>
    public static async Task VerifyDiagnosticsAsync((string filename, string source)[] sources, params DiagnosticResult[] expected)
    {
        var test = CreateTest(sources);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    /// <summary>
    /// Verifies diagnostics using custom reference assemblies (e.g., keyed DI 8.0.0).
    /// </summary>
    public static async Task VerifyDiagnosticsWithReferencesAsync(string source, ReferenceAssemblies references, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = references,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    /// <summary>
    /// Verifies no diagnostics using custom reference assemblies (e.g., keyed DI 8.0.0).
    /// </summary>
    public static async Task VerifyNoDiagnosticsWithReferencesAsync(string source, ReferenceAssemblies references)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = references,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Creates a diagnostic result for the given descriptor at the specified location.
    /// </summary>
    public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
    {
        return new DiagnosticResult(descriptor);
    }

    private static CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> CreateTest(
        string source,
        string? editorConfig = null)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssembliesWithDi60
        };

        if (!string.IsNullOrWhiteSpace(editorConfig))
        {
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", editorConfig));
        }

        return test;
    }

    private static CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> CreateTest(
        (string filename, string source)[] sources)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssembliesWithDi60
        };

        foreach (var (filename, source) in sources)
        {
            test.TestState.Sources.Add((filename, source));
        }

        return test;
    }
}
