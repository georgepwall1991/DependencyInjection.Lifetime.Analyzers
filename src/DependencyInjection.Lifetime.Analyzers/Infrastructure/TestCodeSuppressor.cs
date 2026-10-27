using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers;

/// <summary>
/// Suppresses DI lifetime diagnostics in test code. Tests deliberately build invalid containers and
/// captive graphs to prove the analyzer, so reporting them in test projects is noise. The detector
/// uses the same conventions as the HttpClient analyzers: test directories, test project names,
/// test type and namespace names, test base types, and test attributes. It is a suppressor rather
/// than a check in every analyzer so one rule cannot forget it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TestCodeSuppressor : DiagnosticSuppressor
{
    private static readonly ImmutableArray<SuppressionDescriptor> SuppressionDescriptors =
        CreateSuppressions();

    private static readonly string[] TestPathSegments =
    [
        "acceptance",
        "test",
        "testapps",
        "testassets",
        "tests",
    ];

    private static readonly string[] TestAttributeNames =
    [
        "Fact",
        "Theory",
        "Test",
        "TestCase",
        "TestCaseSource",
        "TestClass",
        "TestMethod",
        "DataTestMethod",
        "TestFixture",
        "SetUp",
        "TearDown",
        "OneTimeSetUp",
        "OneTimeTearDown",
        "TestInitialize",
        "TestCleanup",
        "ClassInitialize",
        "ClassCleanup",
        "AssemblyInitialize",
        "AssemblyCleanup",
    ];

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions =>
        SuppressionDescriptors;

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (TryGetSuppression(diagnostic, context.Compilation, out var descriptor))
            {
                context.ReportSuppression(Suppression.Create(descriptor, diagnostic));
            }
        }
    }

    /// <summary>
    /// Decides whether one reported diagnostic belongs to a shipped DI rule and sits in test code,
    /// and returns the suppression descriptor to report. An analyzer can report a DI-shaped ID this
    /// package does not ship (for example, a third-party analyzer using DI999); that is not a reason
    /// to throw, so the lookup is a null-checked <c>FirstOrDefault</c>.
    /// </summary>
    internal static bool TryGetSuppression(
        Diagnostic diagnostic,
        Compilation compilation,
        out SuppressionDescriptor descriptor
    )
    {
        descriptor = null!;
        if (
            diagnostic.Location.SourceTree is not { } tree
            || !IsDiRule(diagnostic.Id)
            || !IsTestContext(tree, diagnostic.Location, compilation.AssemblyName)
        )
        {
            return false;
        }

        var found = SuppressionDescriptors.FirstOrDefault(item =>
            item.SuppressedDiagnosticId == diagnostic.Id
        );
        if (found is null)
        {
            return false;
        }

        descriptor = found;
        return true;
    }

    internal static bool IsTestContext(SyntaxTree tree, Location location, string? assemblyName)
    {
        if (IsTestFilePath(tree.FilePath) || IsTestAssemblyName(assemblyName))
            return true;

        var root = tree.GetRoot();
        var node = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);

        var type = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (
            type is not null
            && (
                IsTestTypeName(type.Identifier.ValueText)
                || HasTestAttribute(type.AttributeLists)
                || type.BaseList?.Types.Any(baseType =>
                    IsTestTypeName(SimpleTypeName(baseType.Type))
                ) == true
            )
        )
        {
            return true;
        }

        var namespaceDeclaration = node.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();
        if (
            namespaceDeclaration is not null
            && NameLooksLikeTestNamespace(namespaceDeclaration.Name)
        )
        {
            return true;
        }

        return node.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>() is { } method
            && HasTestAttribute(method.AttributeLists);
    }

    internal static bool IsDiRule(string id) =>
        id.StartsWith("DI", StringComparison.Ordinal)
        && id.Length >= 5
        && char.IsDigit(id[2])
        && char.IsDigit(id[3])
        && char.IsDigit(id[4]);

    private static ImmutableArray<SuppressionDescriptor> CreateSuppressions()
    {
        var ids = typeof(DiagnosticIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Where(field => field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(IsDiRule)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal);

        var builder = ImmutableArray.CreateBuilder<SuppressionDescriptor>();
        foreach (var id in ids)
        {
            builder.Add(
                new SuppressionDescriptor(
                    $"DIA-TEST-{id}",
                    id,
                    "DI lifetime diagnostics are not reported in test code; tests intentionally construct invalid lifetimes."
                )
            );
        }

        return builder.ToImmutable();
    }

    private static bool IsTestFilePath(string path)
    {
        if (path.Length == 0)
        {
            return false;
        }

        if (
            path.IndexOf("test", StringComparison.OrdinalIgnoreCase) < 0
            && path.IndexOf("acceptance", StringComparison.OrdinalIgnoreCase) < 0
        )
        {
            return false;
        }

        var segments = path.Split(
            ['/', '\\'],
            StringSplitOptions.RemoveEmptyEntries
        );
        return segments.Any(segment =>
            TestPathSegments.Any(testSegment =>
                segment.Equals(testSegment, StringComparison.OrdinalIgnoreCase)
            )
        );
    }

    private static bool IsTestAssemblyName(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            return false;

        return assemblyName!
            .Split('.')
            .Any(segment =>
                segment.Equals("Testing", StringComparison.Ordinal)
                || segment.EndsWith("Test", StringComparison.Ordinal)
                || segment.EndsWith("Tests", StringComparison.Ordinal)
            );
    }

    private static bool IsTestTypeName(string name) =>
        name.EndsWith("Test", StringComparison.Ordinal)
        || name.EndsWith("Tests", StringComparison.Ordinal);

    private static string SimpleTypeName(TypeSyntax type) =>
        type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            _ => type.ToString(),
        };

    private static bool NameLooksLikeTestNamespace(NameSyntax name) =>
        name switch
        {
            IdentifierNameSyntax identifier => IsTestNamespaceSegment(
                identifier.Identifier.ValueText
            ),
            QualifiedNameSyntax qualified => NameLooksLikeTestNamespace(qualified.Left)
                || NameLooksLikeTestNamespace(qualified.Right),
            AliasQualifiedNameSyntax aliasQualified => NameLooksLikeTestNamespace(
                aliasQualified.Name
            ),
            _ => false,
        };

    private static bool IsTestNamespaceSegment(string segment) =>
        segment.Equals("Testing", StringComparison.Ordinal)
        || segment.EndsWith("Test", StringComparison.Ordinal)
        || segment.EndsWith("Tests", StringComparison.Ordinal);

    private static bool HasTestAttribute(SyntaxList<AttributeListSyntax> attributeLists) =>
        attributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => IsTestAttributeName(attribute.Name));

    private static bool IsTestAttributeName(NameSyntax name)
    {
        var text = name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            _ => name.ToString(),
        };

        if (text.EndsWith("Attribute", StringComparison.Ordinal))
            text = text.Substring(0, text.Length - "Attribute".Length);

        return TestAttributeNames.Contains(text, StringComparer.Ordinal);
    }
}
