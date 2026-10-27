using DependencyInjection.Lifetime.Analyzers.Scan;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

public sealed class TestCodeSuppressorTests
{
    [Fact]
    public void SupportsEveryShippedRuleInCatalogOrder()
    {
        var supported = new TestCodeSuppressor()
            .SupportedSuppressions.Select(item => item.SuppressedDiagnosticId)
            .ToArray();

        Assert.Equal(RuleCatalog.ReportedRules, supported);
    }

    [Fact]
    public void EverySuppressionDescriptorNamesItsRuleAndExplainsTheTestPolicy()
    {
        var suppressions = new TestCodeSuppressor().SupportedSuppressions;

        Assert.Equal(RuleCatalog.ReportedRules.Count, suppressions.Length);
        Assert.Equal(
            suppressions.Length,
            suppressions.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count()
        );
        Assert.Equal(
            suppressions.Length,
            suppressions
                .Select(item => item.SuppressedDiagnosticId)
                .Distinct(StringComparer.Ordinal)
                .Count()
        );

        foreach (var suppression in suppressions)
        {
            Assert.Equal($"DIA-TEST-{suppression.SuppressedDiagnosticId}", suppression.Id);
            Assert.Contains(
                "test code",
                suppression.Justification.ToString(),
                StringComparison.OrdinalIgnoreCase
            );
        }

        Assert.Contains(
            suppressions,
            item => item.SuppressedDiagnosticId == "DI012b" && item.Id == "DIA-TEST-DI012b"
        );
    }

    [Theory]
    [InlineData("acceptance/Foo.cs")]
    [InlineData("test/Foo.cs")]
    [InlineData("testapps/Foo.cs")]
    [InlineData("testassets/Foo.cs")]
    [InlineData("tests/Foo.cs")]
    [InlineData("src/tests/Foo.cs")]
    [InlineData("src/TESTS/Foo.cs")]
    [InlineData("tests\\Foo.cs")]
    [InlineData("C:\\repo\\tests\\Foo.cs")]
    [InlineData("src/acceptance/Foo.cs")]
    [InlineData("src/testapps/Foo.cs")]
    [InlineData("src/testassets/Foo.cs")]
    [InlineData("src/test/Foo.cs")]
    [InlineData("src/Test/Foo.cs")]
    [InlineData("src/TestApps/Foo.cs")]
    public void IsTestFilePath_RecognizesEveryConvention(string path)
    {
        Assert.True(IsTestContext(path, "App", "public class Sample { public void M() { } }"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("src/Foo.cs")]
    [InlineData("src/Contest/Foo.cs")]
    [InlineData("src/TestingGrounds/Foo.cs")]
    [InlineData("src/Testable/Foo.cs")]
    [InlineData("src/Testing/Foo.cs")]
    public void IsTestFilePath_DoesNotMatchLookalikes(string path)
    {
        Assert.False(IsTestContext(path, "App", "public class Sample { public void M() { } }"));
    }

    [Theory]
    [InlineData("MyApp.Tests")]
    [InlineData("MyApp.Test")]
    [InlineData("MyApp.Testing")]
    [InlineData("MyApp.UnitTests")]
    [InlineData("MyApp.IntegrationTest")]
    [InlineData("MyApp.Tests.Unit")]
    [InlineData("Tests")]
    [InlineData("Test")]
    [InlineData("Testing")]
    public void IsTestAssemblyName_RecognizesEveryConvention(string assemblyName)
    {
        Assert.True(
            IsTestContext("src/Foo.cs", assemblyName, "public class Sample { public void M() { } }")
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MyApp")]
    [InlineData("MyApp.Contest")]
    [InlineData("MyApp.Latest")]
    [InlineData("MyApp.Testable")]
    [InlineData("MyApp.TestsGround")]
    public void IsTestAssemblyName_DoesNotMatchLookalikes(string? assemblyName)
    {
        Assert.False(
            IsTestContext("src/Foo.cs", assemblyName, "public class Sample { public void M() { } }")
        );
    }

    [Theory]
    [InlineData("FooTest")]
    [InlineData("FooTests")]
    [InlineData("Test")]
    [InlineData("Tests")]
    public void IsTestTypeName_RecognizesTestSuffixes(string typeName)
    {
        Assert.True(
            IsTestContext(
                "src/Foo.cs",
                "App",
                $"public class {typeName} {{ public void M() {{ }} }}"
            )
        );
    }

    [Theory]
    [InlineData("TestFoo")]
    [InlineData("Foo")]
    [InlineData("footest")]
    [InlineData("FooTestable")]
    public void IsTestTypeName_DoesNotMatchLookalikes(string typeName)
    {
        Assert.False(
            IsTestContext(
                "src/Foo.cs",
                "App",
                $"public class {typeName} {{ public void M() {{ }} }}"
            )
        );
    }

    [Theory]
    [InlineData("Fact")]
    [InlineData("Theory")]
    [InlineData("Test")]
    [InlineData("TestCase")]
    [InlineData("TestCaseSource")]
    [InlineData("TestClass")]
    [InlineData("TestMethod")]
    [InlineData("DataTestMethod")]
    [InlineData("TestFixture")]
    [InlineData("SetUp")]
    [InlineData("TearDown")]
    [InlineData("OneTimeSetUp")]
    [InlineData("OneTimeTearDown")]
    [InlineData("TestInitialize")]
    [InlineData("TestCleanup")]
    [InlineData("ClassInitialize")]
    [InlineData("ClassCleanup")]
    [InlineData("AssemblyInitialize")]
    [InlineData("AssemblyCleanup")]
    [InlineData("FactAttribute")]
    public void IsTestMethodAttribute_RecognizesEveryFrameworkConvention(string attribute)
    {
        Assert.True(
            IsTestContext(
                "src/Foo.cs",
                "App",
                $"public class Sample {{ [{attribute}] public void M() {{ }} }}"
            )
        );
    }

    [Theory]
    [InlineData("Fact")]
    [InlineData("Theory")]
    [InlineData("Test")]
    [InlineData("TestClass")]
    [InlineData("TestFixture")]
    [InlineData("SetUp")]
    [InlineData("TestInitialize")]
    public void IsTestTypeAttribute_RecognizesEveryFrameworkConvention(string attribute)
    {
        Assert.True(
            IsTestContext(
                "src/Foo.cs",
                "App",
                $"[{attribute}] public class Sample {{ public void M() {{ }} }}"
            )
        );
    }

    [Theory]
    [InlineData("[Xunit.Fact]")]
    [InlineData("[global::Xunit.Fact]")]
    [InlineData("[Xunit.FactAttribute]")]
    public void IsTestMethodAttribute_RecognizesQualifiedNames(string attribute)
    {
        Assert.True(
            IsTestContext(
                "src/Foo.cs",
                "App",
                $"public class Sample {{ {attribute} public void M() {{ }} }}"
            )
        );
    }

    [Theory]
    [InlineData("[Obsolete]")]
    [InlineData("[SomeAttribute<int>]")]
    [InlineData("[Xunit.SkippableFact]")]
    public void IsTestMethodAttribute_DoesNotMatchLookalikes(string attribute)
    {
        Assert.False(
            IsTestContext(
                "src/Foo.cs",
                "App",
                $"public class Sample {{ {attribute} public void M() {{ }} }}"
            )
        );
    }

    [Theory]
    [InlineData("public class Sample : IntegrationTests { public void M() { } }")]
    [InlineData("public class Sample : Some.IntegrationTests { public void M() { } }")]
    [InlineData("public class Sample : global::Some.IntegrationTests { public void M() { } }")]
    [InlineData(
        "public class Sample : IntegrationTests, System.IDisposable { public void M() { } }"
    )]
    public void IsTestContext_RecognizesTestBaseTypes(string source)
    {
        Assert.True(IsTestContext("src/Foo.cs", "App", source));
    }

    [Theory]
    [InlineData("public class Sample : Base { public void M() { } }")]
    [InlineData("public class Sample : Some.Base { public void M() { } }")]
    [InlineData("public class Sample : global::Some.Base { public void M() { } }")]
    public void IsTestContext_DoesNotMatchLookalikeBaseTypes(string source)
    {
        Assert.False(IsTestContext("src/Foo.cs", "App", source));
    }

    [Theory]
    [InlineData("namespace My.Tests; public class Sample { public void M() { } }")]
    [InlineData("namespace My.Testing; public class Sample { public void M() { } }")]
    [InlineData("namespace My.UnitTests; public class Sample { public void M() { } }")]
    [InlineData("namespace My.IntegrationTest; public class Sample { public void M() { } }")]
    [InlineData("namespace Tests; public class Sample { public void M() { } }")]
    [InlineData("namespace Test; public class Sample { public void M() { } }")]
    [InlineData("namespace Testing; public class Sample { public void M() { } }")]
    [InlineData("namespace My { namespace Tests { public class Sample { public void M() { } } } }")]
    public void IsTestContext_RecognizesTestNamespaces(string source)
    {
        Assert.True(IsTestContext("src/Foo.cs", "App", source));
    }

    [Theory]
    [InlineData("namespace My; public class Sample { public void M() { } }")]
    [InlineData("namespace My.Contest; public class Sample { public void M() { } }")]
    [InlineData("namespace My.Testable; public class Sample { public void M() { } }")]
    [InlineData("namespace My.Latest; public class Sample { public void M() { } }")]
    [InlineData("namespace Foo<T>; public class Sample { public void M() { } }")]
    [InlineData("namespace My { namespace App { public class Sample { public void M() { } } } }")]
    public void IsTestContext_DoesNotMatchLookalikeNamespaces(string source)
    {
        Assert.False(IsTestContext("src/Foo.cs", "App", source));
    }

    [Theory]
    [InlineData("DI001", true)]
    [InlineData("DI012b", true)]
    [InlineData("DI999", true)]
    [InlineData("DI0123", true)]
    [InlineData("DI000", true)]
    [InlineData("", false)]
    [InlineData("D", false)]
    [InlineData("DI", false)]
    [InlineData("DI0", false)]
    [InlineData("DI01", false)]
    [InlineData("DIA01", false)]
    [InlineData("DI0A1", false)]
    [InlineData("DI01A", false)]
    [InlineData("HCR001", false)]
    [InlineData("XX001", false)]
    [InlineData("di003", false)]
    public void IsDiRule_AcceptsOnlyDiRuleIds(string id, bool expected)
    {
        Assert.Equal(expected, TestCodeSuppressor.IsDiRule(id));
    }

    [Fact]
    public void TryGetSuppression_SuppressesTestAssembly()
    {
        var (diagnostic, compilation) = CreateDiagnostic(
            "App.Tests",
            "src/Sample.cs",
            "public class Sample { public void M() { } }",
            DiagnosticDescriptors.CaptiveDependency
        );

        Assert.True(TestCodeSuppressor.TryGetSuppression(diagnostic, compilation, out var descriptor));
        Assert.Equal("DI003", descriptor.SuppressedDiagnosticId);
        Assert.Equal("DIA-TEST-DI003", descriptor.Id);
    }

    [Fact]
    public void TryGetSuppression_SuppressesTestType()
    {
        var (diagnostic, compilation) = CreateDiagnostic(
            "App",
            "src/Sample.cs",
            "public class SampleTests { public void M() { } }",
            DiagnosticDescriptors.CaptiveDependency
        );

        Assert.True(TestCodeSuppressor.TryGetSuppression(diagnostic, compilation, out _));
    }

    [Fact]
    public void TryGetSuppression_SuppressesTestFile()
    {
        var (diagnostic, compilation) = CreateDiagnostic(
            "App",
            "tests/Sample.cs",
            "public class Sample { public void M() { } }",
            DiagnosticDescriptors.CaptiveDependency
        );

        Assert.True(TestCodeSuppressor.TryGetSuppression(diagnostic, compilation, out _));
    }

    [Fact]
    public void TryGetSuppression_SuppressesTestMethod()
    {
        var (diagnostic, compilation) = CreateDiagnostic(
            "App",
            "src/Sample.cs",
            "public class Sample { [Fact] public void M() { } }",
            DiagnosticDescriptors.CaptiveDependency
        );

        Assert.True(TestCodeSuppressor.TryGetSuppression(diagnostic, compilation, out _));
    }

    [Fact]
    public void TryGetSuppression_SuppressesTestNamespace()
    {
        var (diagnostic, compilation) = CreateDiagnostic(
            "App",
            "src/Sample.cs",
            "namespace My.Tests; public class Sample { public void M() { } }",
            DiagnosticDescriptors.CaptiveDependency
        );

        Assert.True(TestCodeSuppressor.TryGetSuppression(diagnostic, compilation, out _));
    }

    [Fact]
    public void TryGetSuppression_SuppressesTestBaseType()
    {
        var (diagnostic, compilation) = CreateDiagnostic(
            "App",
            "src/Sample.cs",
            "public class Sample : IntegrationTests { public void M() { } }",
            DiagnosticDescriptors.CaptiveDependency
        );

        Assert.True(TestCodeSuppressor.TryGetSuppression(diagnostic, compilation, out _));
    }

    [Fact]
    public void TryGetSuppression_ReturnsTheSubRuleDescriptor()
    {
        var (diagnostic, compilation) = CreateDiagnostic(
            "App.Tests",
            "src/Sample.cs",
            "public class Sample { public void M() { } }",
            DiagnosticDescriptors.DuplicateRegistration,
            messageArguments: ["Service", "line 1"]
        );

        Assert.True(TestCodeSuppressor.TryGetSuppression(diagnostic, compilation, out var descriptor));
        Assert.Equal("DI012b", descriptor.SuppressedDiagnosticId);
        Assert.Equal("DIA-TEST-DI012b", descriptor.Id);
    }

    [Fact]
    public void TryGetSuppression_LeavesProductionDiagnosticsAlone()
    {
        var (diagnostic, compilation) = CreateDiagnostic(
            "App",
            "src/Sample.cs",
            "public class Sample { public void M() { } }",
            DiagnosticDescriptors.CaptiveDependency
        );

        Assert.False(TestCodeSuppressor.TryGetSuppression(diagnostic, compilation, out _));
    }

    [Fact]
    public void TryGetSuppression_DoesNotSuppressNonDiDiagnostics()
    {
        var descriptor = new DiagnosticDescriptor(
            "XX001",
            "Other analyzer",
            "Other analyzer message",
            "Test",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );
        var (diagnostic, compilation) = CreateDiagnostic(
            "App.Tests",
            "tests/Sample.cs",
            "public class Sample { public void M() { } }",
            descriptor
        );

        Assert.False(TestCodeSuppressor.TryGetSuppression(diagnostic, compilation, out _));
    }

    [Fact]
    public void TryGetSuppression_DoesNotThrowForAnUnshippedDiShapedId()
    {
        var descriptor = new DiagnosticDescriptor(
            "DI999",
            "Third-party DI analyzer",
            "Third-party DI analyzer message",
            "Test",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );
        var (diagnostic, compilation) = CreateDiagnostic(
            "App.Tests",
            "tests/Sample.cs",
            "public class Sample { public void M() { } }",
            descriptor
        );

        Assert.False(TestCodeSuppressor.TryGetSuppression(diagnostic, compilation, out _));
    }

    [Fact]
    public void TryGetSuppression_DoesNotSuppressDiagnosticsWithoutASourceTree()
    {
        var (diagnostic, compilation) = CreateDiagnostic(
            "App.Tests",
            "tests/Sample.cs",
            "public class Sample { public void M() { } }",
            DiagnosticDescriptors.CaptiveDependency,
            noLocation: true
        );

        Assert.False(TestCodeSuppressor.TryGetSuppression(diagnostic, compilation, out _));
    }

    private static bool IsTestContext(string path, string? assemblyName, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: path);
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First();
        return TestCodeSuppressor.IsTestContext(
            tree,
            method.Identifier.GetLocation(),
            assemblyName
        );
    }

    private static (Diagnostic Diagnostic, CSharpCompilation Compilation) CreateDiagnostic(
        string assemblyName,
        string path,
        string source,
        DiagnosticDescriptor descriptor,
        object?[]? messageArguments = null,
        bool noLocation = false
    )
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: path);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First();
        var location = noLocation ? Location.None : method.Identifier.GetLocation();
        var arguments = messageArguments
            ?? (descriptor.Id == DiagnosticDescriptors.CaptiveDependency.Id
                ? ["Sample", "scoped", "IScopedService"]
                : []);

        return (Diagnostic.Create(descriptor, location, arguments), compilation);
    }
}
