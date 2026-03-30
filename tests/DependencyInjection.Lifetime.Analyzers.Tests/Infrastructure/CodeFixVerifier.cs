using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

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
            .AddPackages([
                new PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "6.0.0"),
                new PackageIdentity("Microsoft.Extensions.DependencyInjection", "6.0.0")
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
        test.CodeFixTestBehaviors |= CodeFixTestBehaviors.SkipFixAllInDocumentCheck
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
    /// Verifies that a specific code fix equivalence key is not offered for the given diagnostic.
    /// </summary>
    public static async Task VerifyCodeFixNotOfferedAsync(
        string source,
        DiagnosticResult expected,
        string codeActionEquivalenceKey)
    {
        await VerifyCodeFixNotOfferedAsync(
            source,
            diagnostic => diagnostic.Id == expected.Id,
            codeActionEquivalenceKey);
    }

    /// <summary>
    /// Verifies that a specific code fix equivalence key is not offered using custom reference assemblies.
    /// </summary>
    public static async Task VerifyCodeFixNotOfferedWithReferencesAsync(
        string source,
        DiagnosticResult expected,
        ReferenceAssemblies references,
        string codeActionEquivalenceKey)
    {
        var document = CreateDocument(source, references);
        var compilation = await document.Project.GetCompilationAsync();
        Assert.NotNull(compilation);

        var analyzer = new TAnalyzer();
        var diagnostics = await compilation!
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer))
            .GetAnalyzerDiagnosticsAsync();

        var diagnostic = diagnostics.FirstOrDefault(d => d.Id == expected.Id);
        Assert.NotNull(diagnostic);

        var actions = new List<CodeAction>();
        var codeFix = new TCodeFix();
        var context = new CodeFixContext(
            document,
            diagnostic!,
            (action, _) => actions.Add(action),
            default);

        await codeFix.RegisterCodeFixesAsync(context);

        Assert.DoesNotContain(actions, action => action.EquivalenceKey == codeActionEquivalenceKey);
    }

    /// <summary>
    /// Verifies that a specific code fix equivalence key is not offered for the selected diagnostic.
    /// </summary>
    public static async Task VerifyCodeFixNotOfferedAsync(
        string source,
        Func<Diagnostic, bool> diagnosticSelector,
        string codeActionEquivalenceKey)
    {
        var document = CreateDocument(source);
        var compilation = await document.Project.GetCompilationAsync();
        Assert.NotNull(compilation);

        var analyzer = new TAnalyzer();
        var diagnostics = await compilation!
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer))
            .GetAnalyzerDiagnosticsAsync();

        var diagnostic = diagnostics.FirstOrDefault(diagnosticSelector);
        Assert.NotNull(diagnostic);

        var actions = new List<CodeAction>();
        var codeFix = new TCodeFix();
        var context = new CodeFixContext(
            document,
            diagnostic!,
            (action, _) => actions.Add(action),
            default);

        await codeFix.RegisterCodeFixesAsync(context);

        Assert.DoesNotContain(actions, action => action.EquivalenceKey == codeActionEquivalenceKey);
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

        // Some analyzers (e.g., DI003/DI009/DI010+) report at compilation end.
        // Allow code fixes to target these non-local diagnostics.
        test.CodeFixTestBehaviors |= CodeFixTestBehaviors.SkipLocalDiagnosticCheck;

        return test;
    }

    private static CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier> CreateTest(string source, string fixedSource, ReferenceAssemblies references)
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = references,
        };

        test.CodeFixTestBehaviors |= CodeFixTestBehaviors.SkipLocalDiagnosticCheck;

        return test;
    }

    /// <summary>
    /// Verifies a code fix using custom reference assemblies (e.g., keyed DI 8.0.0).
    /// </summary>
    public static async Task VerifyCodeFixWithReferencesAsync(
        string source,
        DiagnosticResult expected,
        string fixedSource,
        ReferenceAssemblies references,
        string codeActionEquivalenceKey)
    {
        var test = CreateTest(source, fixedSource, references);
        test.ExpectedDiagnostics.Add(expected);
        test.CodeActionEquivalenceKey = codeActionEquivalenceKey;
        await test.RunAsync();
    }

    private static Document CreateDocument(string source)
    {
        return CreateDocument(source, GetMetadataReferences());
    }

    private static Document CreateDocument(string source, ReferenceAssemblies references)
    {
        return CreateDocument(source, references.ResolveAsync(LanguageNames.CSharp, default).GetAwaiter().GetResult());
    }

    private static Document CreateDocument(string source, IEnumerable<MetadataReference> metadataReferences)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();

        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "TestProject",
            "TestProject",
            LanguageNames.CSharp,
            metadataReferences: metadataReferences,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));

        var solution = workspace.CurrentSolution.AddProject(projectInfo);
        var documentId = DocumentId.CreateNewId(projectId);
        solution = solution.AddDocument(documentId, "Test.cs", source);

        return solution.GetDocument(documentId)!;
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(System.IO.Path.PathSeparator)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList() ?? [];

        trustedPlatformAssemblies.Add(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location));
        return trustedPlatformAssemblies;
    }
}
