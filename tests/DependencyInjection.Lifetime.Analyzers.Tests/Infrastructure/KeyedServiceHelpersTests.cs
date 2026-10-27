using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

public class KeyedServiceHelpersTests
{
    private const string Source = """
        using Microsoft.Extensions.DependencyInjection;

        public interface IDep { }

        public sealed class Keyed
        {
            public Keyed([FromKeyedServices("alpha")] IDep dep) { }
        }

        public sealed class NumericKey
        {
            public NumericKey([FromKeyedServices(42)] IDep dep) { }
        }

        public sealed class UnknownKey
        {
            public UnknownKey([FromKeyedServices] IDep dep) { }
        }

        public sealed class Unkeyed
        {
            public Unkeyed(IDep dep) { }
        }

        public sealed class ServiceKey
        {
            public ServiceKey([ServiceKey] string key) { }
        }

        public sealed class OtherNamespace
        {
            public OtherNamespace([Custom.FromKeyedServices("alpha")] IDep dep) { }
        }

        public sealed class OtherServiceKey
        {
            public OtherServiceKey([Custom.ServiceKey] string key) { }
        }

        namespace Custom
        {
            public sealed class FromKeyedServicesAttribute : System.Attribute
            {
                public FromKeyedServicesAttribute(string key) { }
            }

            public sealed class ServiceKeyAttribute : System.Attribute { }
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
            typeof(Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute)
                .Assembly
                .Location
        ),
    ];

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create(
            "KeyedServiceHelpersTests",
            [CSharpSyntaxTree.ParseText(Source)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

    private static IParameterSymbol Parameter(CSharpCompilation compilation, string typeName) =>
        compilation.GetTypeByMetadataName(typeName)!.InstanceConstructors.Single().Parameters[0];

    [Fact]
    public void GetServiceKey_UnkeyedParameter_ReturnsNotKeyed()
    {
        var compilation = CreateCompilation();

        var request = KeyedServiceHelpers.GetServiceKey(
            Parameter(compilation, "Unkeyed"),
            inheritedKey: null,
            hasInheritedKey: false,
            inheritedKeyLiteral: null
        );

        Assert.Null(request.Key);
        Assert.False(request.IsKeyed);
        Assert.False(request.IsUnknown);
        Assert.Null(request.KeyLiteral);
    }

    [Fact]
    public void GetServiceKey_StringKey_ReturnsTheKeyAndLiteral()
    {
        var compilation = CreateCompilation();

        var request = KeyedServiceHelpers.GetServiceKey(
            Parameter(compilation, "Keyed"),
            inheritedKey: null,
            hasInheritedKey: false,
            inheritedKeyLiteral: null
        );

        Assert.Equal("alpha", request.Key);
        Assert.True(request.IsKeyed);
        Assert.False(request.IsUnknown);
        Assert.Equal("@\"alpha\"", request.KeyLiteral);
    }

    [Fact]
    public void GetServiceKey_NumericKey_ReturnsTheLiteral()
    {
        var compilation = CreateCompilation();

        var request = KeyedServiceHelpers.GetServiceKey(
            Parameter(compilation, "NumericKey"),
            inheritedKey: null,
            hasInheritedKey: false,
            inheritedKeyLiteral: null
        );

        Assert.Equal(42, request.Key);
        Assert.True(request.IsKeyed);
        Assert.Equal("42", request.KeyLiteral);
    }

    [Fact]
    public void GetServiceKey_NoArgument_UsesTheInheritedKey()
    {
        var compilation = CreateCompilation();

        var request = KeyedServiceHelpers.GetServiceKey(
            Parameter(compilation, "UnknownKey"),
            inheritedKey: "inherited",
            hasInheritedKey: true,
            inheritedKeyLiteral: "@\"inherited\""
        );

        Assert.Equal("inherited", request.Key);
        Assert.True(request.IsKeyed);
        Assert.False(request.IsUnknown);
        Assert.Equal("@\"inherited\"", request.KeyLiteral);
    }

    [Fact]
    public void GetServiceKey_NoArgumentWithoutInheritedKey_IsUnknown()
    {
        var compilation = CreateCompilation();

        var request = KeyedServiceHelpers.GetServiceKey(
            Parameter(compilation, "UnknownKey"),
            inheritedKey: null,
            hasInheritedKey: false,
            inheritedKeyLiteral: null
        );

        Assert.Null(request.Key);
        Assert.True(request.IsKeyed);
        Assert.True(request.IsUnknown);
        Assert.Null(request.KeyLiteral);
    }

    [Fact]
    public void GetServiceKey_CustomAttribute_IsIgnored()
    {
        var compilation = CreateCompilation();

        var request = KeyedServiceHelpers.GetServiceKey(
            Parameter(compilation, "OtherNamespace"),
            inheritedKey: null,
            hasInheritedKey: false,
            inheritedKeyLiteral: null
        );

        Assert.False(request.IsKeyed);
        Assert.False(request.IsUnknown);
    }

    [Fact]
    public void IsServiceKeyParameter_ServiceKey_ReturnsTrue()
    {
        var compilation = CreateCompilation();

        Assert.True(
            KeyedServiceHelpers.IsServiceKeyParameter(Parameter(compilation, "ServiceKey"))
        );
    }

    [Fact]
    public void IsServiceKeyParameter_Unkeyed_ReturnsFalse()
    {
        var compilation = CreateCompilation();

        Assert.False(KeyedServiceHelpers.IsServiceKeyParameter(Parameter(compilation, "Unkeyed")));
    }

    [Fact]
    public void IsServiceKeyParameter_CustomAttribute_ReturnsFalse()
    {
        var compilation = CreateCompilation();

        Assert.False(
            KeyedServiceHelpers.IsServiceKeyParameter(Parameter(compilation, "OtherServiceKey"))
        );
    }

    [Fact]
    public void GetServiceKey_Overload_ReturnsKeyAndKeyedStatus()
    {
        var compilation = CreateCompilation();

        Assert.Equal(
            ("alpha", true),
            KeyedServiceHelpers.GetServiceKey(Parameter(compilation, "Keyed"))
        );
        Assert.Equal(
            ((object?)null, false),
            KeyedServiceHelpers.GetServiceKey(Parameter(compilation, "Unkeyed"))
        );
    }
}
