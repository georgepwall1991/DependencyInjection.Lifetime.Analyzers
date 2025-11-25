using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

public class WellKnownTypesTests
{
    private static readonly MetadataReference[] DiReferences =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(IServiceProvider).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceScope).Assembly.Location)
    ];

    private static Compilation CreateCompilation(string source, bool includeDiReferences = true)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = includeDiReferences
            ? DiReferences
            : [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    #region Create Tests

    [Fact]
    public void Create_WithDIReferences_ReturnsInstance()
    {
        var compilation = CreateCompilation("public class Test { }");

        var wellKnownTypes = WellKnownTypes.Create(compilation);

        Assert.NotNull(wellKnownTypes);
        Assert.NotNull(wellKnownTypes.IServiceProvider);
        Assert.NotNull(wellKnownTypes.IServiceScopeFactory);
    }

    [Fact]
    public void Create_WithoutDIReferences_ReturnsNull()
    {
        var compilation = CreateCompilation("public class Test { }", includeDiReferences: false);

        var wellKnownTypes = WellKnownTypes.Create(compilation);

        Assert.Null(wellKnownTypes);
    }

    #endregion

    #region IsServiceProvider Tests

    [Fact]
    public void IsServiceProvider_WithIServiceProvider_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var serviceProviderType = compilation.GetTypeByMetadataName("System.IServiceProvider");

        var result = wellKnownTypes.IsServiceProvider(serviceProviderType);

        Assert.True(result);
    }

    [Fact]
    public void IsServiceProvider_WithOtherType_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var objectType = compilation.GetTypeByMetadataName("System.Object");

        var result = wellKnownTypes.IsServiceProvider(objectType);

        Assert.False(result);
    }

    [Fact]
    public void IsServiceProvider_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        var result = wellKnownTypes.IsServiceProvider(null);

        Assert.False(result);
    }

    #endregion

    #region IsServiceScopeFactory Tests

    [Fact]
    public void IsServiceScopeFactory_WithIServiceScopeFactory_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var factoryType = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceScopeFactory");

        var result = wellKnownTypes.IsServiceScopeFactory(factoryType);

        Assert.True(result);
    }

    [Fact]
    public void IsServiceScopeFactory_WithOtherType_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var objectType = compilation.GetTypeByMetadataName("System.Object");

        var result = wellKnownTypes.IsServiceScopeFactory(objectType);

        Assert.False(result);
    }

    #endregion

    #region IsServiceScope Tests

    [Fact]
    public void IsServiceScope_WithIServiceScope_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var scopeType = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceScope");

        var result = wellKnownTypes.IsServiceScope(scopeType);

        Assert.True(result);
    }

    [Fact]
    public void IsServiceScope_WithOtherType_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var objectType = compilation.GetTypeByMetadataName("System.Object");

        var result = wellKnownTypes.IsServiceScope(objectType);

        Assert.False(result);
    }

    #endregion

    #region IsAsyncServiceScope Tests

    [Fact]
    public void IsAsyncServiceScope_WithAsyncServiceScope_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var asyncScopeType = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.AsyncServiceScope");

        var result = wellKnownTypes.IsAsyncServiceScope(asyncScopeType);

        Assert.True(result);
    }

    [Fact]
    public void IsAsyncServiceScope_WithOtherType_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var objectType = compilation.GetTypeByMetadataName("System.Object");

        var result = wellKnownTypes.IsAsyncServiceScope(objectType);

        Assert.False(result);
    }

    #endregion

    #region Combined Check Tests

    [Fact]
    public void IsServiceProviderOrFactory_WithServiceProvider_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var serviceProviderType = compilation.GetTypeByMetadataName("System.IServiceProvider");

        var result = wellKnownTypes.IsServiceProviderOrFactory(serviceProviderType);

        Assert.True(result);
    }

    [Fact]
    public void IsServiceProviderOrFactory_WithScopeFactory_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var factoryType = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceScopeFactory");

        var result = wellKnownTypes.IsServiceProviderOrFactory(factoryType);

        Assert.True(result);
    }

    [Fact]
    public void IsServiceProviderOrFactory_WithOtherType_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var objectType = compilation.GetTypeByMetadataName("System.Object");

        var result = wellKnownTypes.IsServiceProviderOrFactory(objectType);

        Assert.False(result);
    }

    [Fact]
    public void IsServiceProviderOrFactoryOrKeyed_WithServiceProvider_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var serviceProviderType = compilation.GetTypeByMetadataName("System.IServiceProvider");

        var result = wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(serviceProviderType);

        Assert.True(result);
    }

    [Fact]
    public void IsServiceProviderOrFactoryOrKeyed_WithScopeFactory_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var factoryType = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceScopeFactory");

        var result = wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(factoryType);

        Assert.True(result);
    }

    [Fact]
    public void IsAnyServiceProvider_WithServiceProvider_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var serviceProviderType = compilation.GetTypeByMetadataName("System.IServiceProvider");

        var result = wellKnownTypes.IsAnyServiceProvider(serviceProviderType);

        Assert.True(result);
    }

    [Fact]
    public void IsAnyServiceProvider_WithOtherType_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var objectType = compilation.GetTypeByMetadataName("System.Object");

        var result = wellKnownTypes.IsAnyServiceProvider(objectType);

        Assert.False(result);
    }

    #endregion

    #region IDisposable Tests

    [Fact]
    public void ImplementsIDisposable_WithDisposableType_ReturnsTrue()
    {
        var source = """
            using System;
            public class DisposableClass : IDisposable
            {
                public void Dispose() { }
            }
            """;
        var compilation = CreateCompilation(source);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var disposableClass = compilation.GetTypeByMetadataName("DisposableClass");

        var result = wellKnownTypes.ImplementsIDisposable(disposableClass);

        Assert.True(result);
    }

    [Fact]
    public void ImplementsIDisposable_WithNonDisposableType_ReturnsFalse()
    {
        var source = "public class NonDisposableClass { }";
        var compilation = CreateCompilation(source);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var nonDisposableClass = compilation.GetTypeByMetadataName("NonDisposableClass");

        var result = wellKnownTypes.ImplementsIDisposable(nonDisposableClass);

        Assert.False(result);
    }

    [Fact]
    public void ImplementsIDisposable_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        var result = wellKnownTypes.ImplementsIDisposable(null);

        Assert.False(result);
    }

    #endregion

    #region IAsyncDisposable Tests

    [Fact]
    public void ImplementsIAsyncDisposable_WithAsyncDisposableType_ReturnsTrue()
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            public class AsyncDisposableClass : IAsyncDisposable
            {
                public ValueTask DisposeAsync() => default;
            }
            """;
        var compilation = CreateCompilation(source);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var asyncDisposableClass = compilation.GetTypeByMetadataName("AsyncDisposableClass");

        var result = wellKnownTypes.ImplementsIAsyncDisposable(asyncDisposableClass);

        Assert.True(result);
    }

    [Fact]
    public void ImplementsIAsyncDisposable_WithNonAsyncDisposableType_ReturnsFalse()
    {
        var source = "public class NonAsyncDisposableClass { }";
        var compilation = CreateCompilation(source);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var nonAsyncDisposableClass = compilation.GetTypeByMetadataName("NonAsyncDisposableClass");

        var result = wellKnownTypes.ImplementsIAsyncDisposable(nonAsyncDisposableClass);

        Assert.False(result);
    }

    [Fact]
    public void ImplementsIAsyncDisposable_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        var result = wellKnownTypes.ImplementsIAsyncDisposable(null);

        Assert.False(result);
    }

    #endregion

    #region GetDisposableInterfaceName Tests

    [Fact]
    public void GetDisposableInterfaceName_WithIDisposable_ReturnsIDisposable()
    {
        var source = """
            using System;
            public class DisposableClass : IDisposable
            {
                public void Dispose() { }
            }
            """;
        var compilation = CreateCompilation(source);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var disposableClass = compilation.GetTypeByMetadataName("DisposableClass");

        var result = wellKnownTypes.GetDisposableInterfaceName(disposableClass);

        Assert.Equal("IDisposable", result);
    }

    [Fact]
    public void GetDisposableInterfaceName_WithIAsyncDisposable_ReturnsIAsyncDisposable()
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            public class AsyncDisposableClass : IAsyncDisposable
            {
                public ValueTask DisposeAsync() => default;
            }
            """;
        var compilation = CreateCompilation(source);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var asyncDisposableClass = compilation.GetTypeByMetadataName("AsyncDisposableClass");

        var result = wellKnownTypes.GetDisposableInterfaceName(asyncDisposableClass);

        Assert.Equal("IAsyncDisposable", result);
    }

    [Fact]
    public void GetDisposableInterfaceName_WithBothInterfaces_ReturnsIAsyncDisposable()
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            public class BothDisposableClass : IDisposable, IAsyncDisposable
            {
                public void Dispose() { }
                public ValueTask DisposeAsync() => default;
            }
            """;
        var compilation = CreateCompilation(source);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var bothDisposableClass = compilation.GetTypeByMetadataName("BothDisposableClass");

        var result = wellKnownTypes.GetDisposableInterfaceName(bothDisposableClass);

        // IAsyncDisposable takes priority
        Assert.Equal("IAsyncDisposable", result);
    }

    [Fact]
    public void GetDisposableInterfaceName_WithNeitherInterface_ReturnsNull()
    {
        var source = "public class NonDisposableClass { }";
        var compilation = CreateCompilation(source);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var nonDisposableClass = compilation.GetTypeByMetadataName("NonDisposableClass");

        var result = wellKnownTypes.GetDisposableInterfaceName(nonDisposableClass);

        Assert.Null(result);
    }

    [Fact]
    public void GetDisposableInterfaceName_WithNull_ReturnsNull()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        var result = wellKnownTypes.GetDisposableInterfaceName(null);

        Assert.Null(result);
    }

    #endregion
}
