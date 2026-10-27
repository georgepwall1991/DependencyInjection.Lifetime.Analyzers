using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

public class ImplementationAssignabilityTests
{
    private const string Source = """
        public interface IService { }
        public sealed class Service : IService { }
        public sealed class Convertible
        {
            public static implicit operator Service(Convertible value) => new Service();
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
            typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location
        ),
    ];

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create(
            "ImplementationAssignabilityTests",
            [CSharpSyntaxTree.ParseText(Source)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

    [Fact]
    public void IdentityConversion_IsCompatible()
    {
        var compilation = CreateCompilation();
        var service = compilation.GetTypeByMetadataName("Service")!;

        Assert.True(
            ImplementationAssignability.IsClosedTypeCompatible(compilation, service, service)
        );
    }

    [Fact]
    public void ReferenceConversion_IsCompatible()
    {
        var compilation = CreateCompilation();

        Assert.True(
            ImplementationAssignability.IsClosedTypeCompatible(
                compilation,
                compilation.GetTypeByMetadataName("IService")!,
                compilation.GetTypeByMetadataName("Service")!
            )
        );
    }

    [Fact]
    public void BoxingToObject_IsCompatible()
    {
        var compilation = CreateCompilation();

        Assert.True(
            ImplementationAssignability.IsClosedTypeCompatible(
                compilation,
                compilation.GetSpecialType(SpecialType.System_Object),
                compilation.GetSpecialType(SpecialType.System_Int32)
            )
        );
    }

    [Fact]
    public void BoxingToInterface_IsCompatible()
    {
        var compilation = CreateCompilation();

        Assert.True(
            ImplementationAssignability.IsClosedTypeCompatible(
                compilation,
                compilation.GetTypeByMetadataName("System.IComparable")!,
                compilation.GetSpecialType(SpecialType.System_Int32)
            )
        );
    }

    [Fact]
    public void NullableOfSameUnderlyingType_IsCompatible()
    {
        var compilation = CreateCompilation();
        var nullableInt = compilation
            .GetSpecialType(SpecialType.System_Nullable_T)
            .Construct(compilation.GetSpecialType(SpecialType.System_Int32));

        Assert.True(
            ImplementationAssignability.IsClosedTypeCompatible(
                compilation,
                nullableInt,
                compilation.GetSpecialType(SpecialType.System_Int32)
            )
        );
    }

    [Fact]
    public void NullableOfDifferentUnderlyingType_IsNotCompatible()
    {
        var compilation = CreateCompilation();
        var nullableLong = compilation
            .GetSpecialType(SpecialType.System_Nullable_T)
            .Construct(compilation.GetSpecialType(SpecialType.System_Int64));

        Assert.False(
            ImplementationAssignability.IsClosedTypeCompatible(
                compilation,
                nullableLong,
                compilation.GetSpecialType(SpecialType.System_Int32)
            )
        );
    }

    [Fact]
    public void NonNullableGenericWithMatchingTypeArgument_IsNotCompatible()
    {
        var compilation = CreateCompilation();
        var listOfInt = compilation
            .GetTypeByMetadataName("System.Collections.Generic.List`1")!
            .Construct(compilation.GetSpecialType(SpecialType.System_Int32));

        Assert.False(
            ImplementationAssignability.IsClosedTypeCompatible(
                compilation,
                listOfInt,
                compilation.GetSpecialType(SpecialType.System_Int32)
            )
        );
    }

    [Fact]
    public void UserDefinedImplicitConversion_IsNotCompatible()
    {
        var compilation = CreateCompilation();

        Assert.False(
            ImplementationAssignability.IsClosedTypeCompatible(
                compilation,
                compilation.GetTypeByMetadataName("Service")!,
                compilation.GetTypeByMetadataName("Convertible")!
            )
        );
    }

    [Fact]
    public void UnrelatedTypes_AreNotCompatible()
    {
        var compilation = CreateCompilation();

        Assert.False(
            ImplementationAssignability.IsClosedTypeCompatible(
                compilation,
                compilation.GetTypeByMetadataName("IService")!,
                compilation.GetSpecialType(SpecialType.System_String)
            )
        );
    }

    [Fact]
    public void NonCSharpCompilation_IsNotCompatible()
    {
        var compilation = VisualBasicCompilation.Create(
            "ImplementationAssignabilityTests",
            [VisualBasicSyntaxTree.ParseText("Public Class Test\nEnd Class")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        Assert.False(
            ImplementationAssignability.IsClosedTypeCompatible(
                compilation,
                compilation.GetSpecialType(SpecialType.System_Object),
                compilation.GetSpecialType(SpecialType.System_String)
            )
        );
    }
}
