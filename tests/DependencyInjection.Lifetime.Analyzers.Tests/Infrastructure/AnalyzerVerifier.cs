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
    /// Custom reference assemblies combining Net60 with DI.Abstractions as a NuGet package.
    /// </summary>
    private static readonly ReferenceAssemblies ReferenceAssembliesWithDi =
        ReferenceAssemblies.Net.Net60
            .AddPackages([
                new PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "6.0.0"),
                new PackageIdentity("Microsoft.Extensions.DependencyInjection", "6.0.0")
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
    /// Verifies that the analyzer produces the expected diagnostics.
    /// </summary>
    public static async Task VerifyDiagnosticsAsync(string source, params DiagnosticResult[] expected)
    {
        var test = CreateTest(source);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    /// <summary>
    /// Creates a diagnostic result for the given descriptor at the specified location.
    /// </summary>
    public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
    {
        return new DiagnosticResult(descriptor);
    }

    private static CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> CreateTest(string source)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssembliesWithDi
        };

        return test;
    }
}
