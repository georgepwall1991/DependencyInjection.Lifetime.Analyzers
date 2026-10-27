using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

public class ConstructorSelectionTests
{
    private const string Source = """
        using Microsoft.Extensions.DependencyInjection;

        public interface IDep { }
        public sealed class Dep : IDep { }

        public sealed class SinglePublic
        {
            public SinglePublic() { }
        }

        public sealed class MultiplePublic
        {
            public MultiplePublic() { }
            public MultiplePublic(IDep dep) { }
        }

        public sealed class Attributed
        {
            [ActivatorUtilitiesConstructor]
            public Attributed(IDep dep) { }

            public Attributed() { }

            public Attributed(IDep first, IDep second) { }
        }

        public sealed class NoPublic
        {
            private NoPublic() { }
        }

        public sealed class OnlyParameterized
        {
            public OnlyParameterized(IDep dep) { }
        }

        public sealed class WithStaticConstructor
        {
            static WithStaticConstructor() { }

            public WithStaticConstructor() { }
        }

        public sealed class TwoAtMax
        {
            public TwoAtMax(IDep first, IDep second) { }
            public TwoAtMax(IDep first, string second) { }
            public TwoAtMax() { }
        }

        namespace Custom
        {
            public sealed class ActivatorUtilitiesConstructorAttribute : System.Attribute { }
        }

        public sealed class CustomAttributed
        {
            [Custom.ActivatorUtilitiesConstructor]
            public CustomAttributed(IDep dep) { }

            public CustomAttributed() { }
        }
        """;

    private static readonly MetadataReference[] References =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location),
        MetadataReference.CreateFromFile(
            Path.Combine(
                Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "System.Runtime.dll"
            )
        ),
        MetadataReference.CreateFromFile(
            typeof(System.Collections.Generic.IEnumerable<>).Assembly.Location
        ),
        MetadataReference.CreateFromFile(
            typeof(Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructorAttribute)
                .Assembly
                .Location
        ),
    ];

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create(
            "ConstructorSelectionTests",
            [CSharpSyntaxTree.ParseText(Source)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

    private static INamedTypeSymbol Type(CSharpCompilation compilation, string name) =>
        compilation.GetTypeByMetadataName(name)!;

    [Fact]
    public void GetConstructorsToAnalyze_ReturnsPublicInstanceConstructors()
    {
        var compilation = CreateCompilation();

        var constructors = ConstructorSelection
            .GetConstructorsToAnalyze(Type(compilation, "MultiplePublic"))
            .ToList();

        Assert.Equal(2, constructors.Count);
        Assert.All(
            constructors,
            constructor => Assert.True(constructor.DeclaredAccessibility == Accessibility.Public)
        );
        Assert.All(constructors, constructor => Assert.False(constructor.IsStatic));
    }

    [Fact]
    public void GetConstructorsToAnalyze_ReturnsEmptyWhenThereAreNoPublicConstructors()
    {
        var compilation = CreateCompilation();

        Assert.Empty(ConstructorSelection.GetConstructorsToAnalyze(Type(compilation, "NoPublic")));
    }

    [Fact]
    public void GetConstructorsToAnalyze_PrefersTheAttributedConstructor()
    {
        var compilation = CreateCompilation();

        var constructors = ConstructorSelection
            .GetConstructorsToAnalyze(Type(compilation, "Attributed"))
            .ToList();

        var constructor = Assert.Single(constructors);
        Assert.Single(constructor.Parameters);
        Assert.Contains(
            constructor.GetAttributes(),
            attribute => attribute.AttributeClass?.Name == "ActivatorUtilitiesConstructorAttribute"
        );
    }

    [Fact]
    public void GetConstructorsToAnalyze_IgnoresAnAttributeFromAnotherNamespace()
    {
        var compilation = CreateCompilation();

        var constructors = ConstructorSelection
            .GetConstructorsToAnalyze(Type(compilation, "CustomAttributed"))
            .ToList();

        Assert.Collection(constructors, _ => { }, _ => { });
    }

    [Fact]
    public void GetLikelyActivationConstructors_ChoosesTheHighestParameterCount()
    {
        var compilation = CreateCompilation();

        var constructors = ConstructorSelection
            .GetLikelyActivationConstructors(Type(compilation, "MultiplePublic"), _ => true)
            .ToList();

        var constructor = Assert.Single(constructors);
        Assert.Single(constructor.Parameters);
    }

    [Fact]
    public void GetConstructorsToAnalyze_IgnoresStaticConstructors()
    {
        var compilation = CreateCompilation();

        var constructor = Assert.Single(
            ConstructorSelection.GetConstructorsToAnalyze(Type(compilation, "WithStaticConstructor"))
        );

        Assert.False(constructor.IsStatic);
    }

    [Fact]
    public void GetLikelyActivationConstructors_FallsBackToTheParameterlessConstructor()
    {
        var compilation = CreateCompilation();

        var constructor = Assert.Single(
            ConstructorSelection.GetLikelyActivationConstructors(
                Type(compilation, "MultiplePublic"),
                _ => false
            )
        );

        Assert.Empty(constructor.Parameters);
    }

    [Fact]
    public void GetLikelyActivationConstructors_ReturnsEmptyWhenNoConstructorIsResolvable()
    {
        var compilation = CreateCompilation();

        Assert.Empty(
            ConstructorSelection.GetLikelyActivationConstructors(
                Type(compilation, "OnlyParameterized"),
                _ => false
            )
        );
    }

    [Fact]
    public void GetLikelyActivationConstructors_PrefersTheAttributedConstructorEvenWhenItIsNotResolvable()
    {
        var compilation = CreateCompilation();

        var constructors = ConstructorSelection
            .GetLikelyActivationConstructors(Type(compilation, "Attributed"), _ => false)
            .ToList();

        var constructor = Assert.Single(constructors);
        Assert.Single(constructor.Parameters);
    }

    [Fact]
    public void GetLikelyActivationConstructors_ReturnsEveryConstructorTiedAtTheHighestParameterCount()
    {
        var compilation = CreateCompilation();

        var constructors = ConstructorSelection
            .GetLikelyActivationConstructors(Type(compilation, "TwoAtMax"), _ => true)
            .ToList();

        Assert.Collection(
            constructors,
            constructor => Assert.Equal(2, constructor.Parameters.Length),
            constructor => Assert.Equal(2, constructor.Parameters.Length)
        );
    }

    [Fact]
    public void GetLikelyActivationConstructors_ReturnsEmptyWhenThereAreNoPublicConstructors()
    {
        var compilation = CreateCompilation();

        Assert.Empty(
            ConstructorSelection.GetLikelyActivationConstructors(
                Type(compilation, "NoPublic"),
                _ => true
            )
        );
    }
}
