using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

/// <summary>
/// Helper class for verifying code fixes in tests.
/// </summary>
public static class CodeFixVerifier<TAnalyzer, TCodeFix>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    /// <summary>
    /// Custom reference assemblies combining Net60 with DI.Abstractions as a NuGet package.
    /// </summary>
    private static readonly ReferenceAssemblies ReferenceAssembliesWithDi =
        ReferenceAssemblies.Net.Net60
            .AddPackages([new PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "6.0.0")]);

    /// <summary>
    /// Verifies that the code fix transforms the source code as expected.
    /// </summary>
    /// <param name="source">The source code with the diagnostic.</param>
    /// <param name="expected">The expected diagnostic.</param>
    /// <param name="fixedSource">The expected source after the fix is applied.</param>
    public static async Task VerifyCodeFixAsync(string source, DiagnosticResult expected, string fixedSource)
    {
        await VerifyCodeFixAsync(source, [expected], fixedSource, codeActionIndex: null);
    }

    /// <summary>
    /// Verifies that the code fix transforms the source code as expected.
    /// </summary>
    /// <param name="source">The source code with the diagnostic.</param>
    /// <param name="expected">The expected diagnostics.</param>
    /// <param name="fixedSource">The expected source after the fix is applied.</param>
    public static async Task VerifyCodeFixAsync(string source, DiagnosticResult[] expected, string fixedSource)
    {
        await VerifyCodeFixAsync(source, expected, fixedSource, codeActionIndex: null);
    }

    /// <summary>
    /// Verifies that a specific code fix (by index) transforms the source code as expected.
    /// Use this when a diagnostic has multiple fix options.
    /// </summary>
    /// <param name="source">The source code with the diagnostic.</param>
    /// <param name="expected">The expected diagnostic.</param>
    /// <param name="fixedSource">The expected source after the fix is applied.</param>
    /// <param name="codeActionIndex">The index of the code action to apply (0-based).</param>
    public static async Task VerifyCodeFixAsync(string source, DiagnosticResult expected, string fixedSource, int codeActionIndex)
    {
        await VerifyCodeFixAsync(source, [expected], fixedSource, codeActionIndex);
    }

    /// <summary>
    /// Verifies that a specific code fix (by equivalence key) transforms the source code as expected.
    /// </summary>
    /// <param name="source">The source code with the diagnostic.</param>
    /// <param name="expected">The expected diagnostic.</param>
    /// <param name="fixedSource">The expected source after the fix is applied.</param>
    /// <param name="codeActionEquivalenceKey">The equivalence key of the code action to apply.</param>
    public static async Task VerifyCodeFixAsync(string source, DiagnosticResult expected, string fixedSource, string codeActionEquivalenceKey)
    {
        var test = CreateTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(expected);
        test.CodeActionEquivalenceKey = codeActionEquivalenceKey;
        await test.RunAsync();
    }

    /// <summary>
    /// Verifies that a specific code fix (by equivalence key) transforms the source code as expected,
    /// with expected diagnostics remaining in the fixed state.
    /// </summary>
    /// <param name="source">The source code with the diagnostic.</param>
    /// <param name="expected">The expected diagnostic.</param>
    /// <param name="fixedSource">The expected source after the fix is applied.</param>
    /// <param name="codeActionEquivalenceKey">The equivalence key of the code action to apply.</param>
    /// <param name="fixedStateDiagnostics">Expected diagnostics in the fixed state.</param>
    public static async Task VerifyCodeFixAsync(
        string source,
        DiagnosticResult expected,
        string fixedSource,
        string codeActionEquivalenceKey,
        params DiagnosticResult[] fixedStateDiagnostics)
    {
        var test = CreateTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(expected);
        test.CodeActionEquivalenceKey = codeActionEquivalenceKey;
        test.FixedState.ExpectedDiagnostics.AddRange(fixedStateDiagnostics);
        await test.RunAsync();
    }

    /// <summary>
    /// Verifies that a code fix transforms the source code as expected, applying the fix only once.
    /// Use this for code fixes that add acknowledgment but don't remove the underlying diagnostic.
    /// </summary>
    /// <param name="source">The source code with the diagnostic.</param>
    /// <param name="expected">The expected diagnostic.</param>
    /// <param name="fixedSource">The expected source after the fix is applied.</param>
    /// <param name="codeActionEquivalenceKey">The equivalence key of the code action to apply.</param>
    /// <param name="fixedStateDiagnostics">Expected diagnostics in the fixed state.</param>
    public static async Task VerifyNonRemovingCodeFixAsync(
        string source,
        DiagnosticResult expected,
        string fixedSource,
        string codeActionEquivalenceKey,
        params DiagnosticResult[] fixedStateDiagnostics)
    {
        var test = CreateTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(expected);
        test.CodeActionEquivalenceKey = codeActionEquivalenceKey;
        test.FixedState.ExpectedDiagnostics.AddRange(fixedStateDiagnostics);
        // Skip the FixAll check which applies fixes iteratively
        test.CodeFixTestBehaviors = CodeFixTestBehaviors.SkipFixAllInDocumentCheck
                                    | CodeFixTestBehaviors.SkipFixAllInProjectCheck
                                    | CodeFixTestBehaviors.SkipFixAllInSolutionCheck;
        // Set iterations to explicit 1 to prevent re-application
        test.NumberOfFixAllIterations = 0;
        test.NumberOfIncrementalIterations = 1;
        await test.RunAsync();
    }

    /// <summary>
    /// Verifies that no code fix is offered for the given source.
    /// </summary>
    /// <param name="source">The source code with the diagnostic.</param>
    /// <param name="expected">The expected diagnostic.</param>
    public static async Task VerifyNoCodeFixOfferedAsync(string source, DiagnosticResult expected)
    {
        var test = CreateTest(source, source);
        test.ExpectedDiagnostics.Add(expected);
        test.CodeFixTestBehaviors = CodeFixTestBehaviors.SkipFixAllCheck;
        await test.RunAsync();
    }

    /// <summary>
    /// Creates a diagnostic result for the given descriptor.
    /// </summary>
    public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
    {
        return new DiagnosticResult(descriptor);
    }

    private static async Task VerifyCodeFixAsync(string source, DiagnosticResult[] expected, string fixedSource, int? codeActionIndex)
    {
        var test = CreateTest(source, fixedSource);
        test.ExpectedDiagnostics.AddRange(expected);

        if (codeActionIndex.HasValue)
        {
            test.CodeActionIndex = codeActionIndex.Value;
        }

        await test.RunAsync();
    }

    private static CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier> CreateTest(string source, string fixedSource)
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssembliesWithDi
        };

        return test;
    }
}
