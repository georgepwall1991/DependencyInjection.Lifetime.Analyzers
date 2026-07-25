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
        MetadataReference.CreateFromFile(
            typeof(Microsoft.Extensions.DependencyInjection.IServiceScope).Assembly.Location
        ),
        // DI028/DI029/DI030 match BCL symbols that are not in the DI assemblies above.
        MetadataReference.CreateFromFile(
            typeof(System.Threading.CancellationToken).Assembly.Location
        ),
        MetadataReference.CreateFromFile(typeof(System.Net.Http.HttpClient).Assembly.Location),
        MetadataReference.CreateFromFile(
            typeof(System.Collections.Generic.List<int>).Assembly.Location
        ),
        MetadataReference.CreateFromFile(
            typeof(System.Collections.Concurrent.ConcurrentDictionary<int, int>).Assembly.Location
        ),
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
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
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
        var factoryType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"
        );

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
        var scopeType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceScope"
        );

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
        var asyncScopeType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.AsyncServiceScope"
        );

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
        var factoryType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"
        );

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
        var factoryType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"
        );

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

    #region Cancellation Symbol Tests (DI028)

    [Fact]
    public void Create_WithBclReferences_ResolvesCancellationSymbols()
    {
        var compilation = CreateCompilation("public class Test { }");

        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        Assert.NotNull(wellKnownTypes.CancellationToken);
        Assert.NotNull(wellKnownTypes.CancellationTokenSource);
        Assert.NotNull(wellKnownTypes.CancellationTokenRegistration);
    }

    [Fact]
    public void IsCancellationToken_WithCancellationToken_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");

        Assert.True(wellKnownTypes.IsCancellationToken(type));
    }

    [Fact]
    public void IsCancellationToken_WithOtherType_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("System.Threading.CancellationTokenSource");

        Assert.False(wellKnownTypes.IsCancellationToken(type));
    }

    [Fact]
    public void IsCancellationToken_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        Assert.False(wellKnownTypes.IsCancellationToken(null));
    }

    [Fact]
    public void IsCancellationTokenSource_WithCancellationTokenSource_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("System.Threading.CancellationTokenSource");

        Assert.True(wellKnownTypes.IsCancellationTokenSource(type));
    }

    [Fact]
    public void IsCancellationTokenSource_WithOtherType_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("System.Object");

        Assert.False(wellKnownTypes.IsCancellationTokenSource(type));
    }

    [Fact]
    public void IsCancellationTokenSource_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        Assert.False(wellKnownTypes.IsCancellationTokenSource(null));
    }

    [Fact]
    public void IsCancellationTokenRegistration_WithRegistration_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName(
            "System.Threading.CancellationTokenRegistration"
        );

        Assert.True(wellKnownTypes.IsCancellationTokenRegistration(type));
    }

    [Fact]
    public void IsCancellationTokenRegistration_WithOtherType_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");

        Assert.False(wellKnownTypes.IsCancellationTokenRegistration(type));
    }

    [Fact]
    public void IsCancellationTokenRegistration_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        Assert.False(wellKnownTypes.IsCancellationTokenRegistration(null));
    }

    #endregion

    #region Source-Declared Fallback Tests (DI028)

    private const string FrameworkStubSource = """
        namespace Microsoft.Extensions.Options
        {
            public interface IOptionsMonitor<T> { T CurrentValue { get; } }
            public interface IOptions<T> { T Value { get; } }
        }
        namespace Microsoft.Extensions.Primitives
        {
            public interface IChangeToken { bool HasChanged { get; } }
            public static class ChangeToken { }
            public sealed class ReloadToken : IChangeToken { public bool HasChanged => false; }
        }
        namespace Microsoft.Extensions.Hosting
        {
            public interface IHostApplicationLifetime { void StopApplication(); }
        }
        namespace NotTheFramework
        {
            public static class ChangeToken { }
            public interface IHostApplicationLifetime { }
        }
        """;

    [Fact]
    public void IsOptionsMonitor_WithSourceDeclaredStub_ReturnsTrue()
    {
        var compilation = CreateCompilation(FrameworkStubSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Options.IOptionsMonitor`1"
        );

        Assert.True(wellKnownTypes.IsOptionsMonitor(type));
    }

    [Fact]
    public void IsOptionsMonitor_WithOptionsRatherThanMonitor_ReturnsFalse()
    {
        var compilation = CreateCompilation(FrameworkStubSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("Microsoft.Extensions.Options.IOptions`1");

        Assert.False(wellKnownTypes.IsOptionsMonitor(type));
    }

    [Fact]
    public void IsOptionsMonitor_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation(FrameworkStubSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        Assert.False(wellKnownTypes.IsOptionsMonitor(null));
    }

    [Fact]
    public void ImplementsChangeToken_WithSourceDeclaredInterface_ReturnsTrue()
    {
        var compilation = CreateCompilation(FrameworkStubSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Primitives.IChangeToken"
        );

        Assert.True(wellKnownTypes.ImplementsChangeToken(type));
    }

    [Fact]
    public void ImplementsChangeToken_WithImplementingType_ReturnsTrue()
    {
        var compilation = CreateCompilation(FrameworkStubSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("Microsoft.Extensions.Primitives.ReloadToken");

        Assert.True(wellKnownTypes.ImplementsChangeToken(type));
    }

    [Fact]
    public void ImplementsChangeToken_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation(FrameworkStubSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        Assert.False(wellKnownTypes.ImplementsChangeToken(null));
    }

    [Fact]
    public void IsChangeTokenStaticHelper_WithSourceDeclaredHelper_ReturnsTrue()
    {
        var compilation = CreateCompilation(FrameworkStubSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("Microsoft.Extensions.Primitives.ChangeToken");

        Assert.True(wellKnownTypes.IsChangeTokenStaticHelper(type));
    }

    [Fact]
    public void IsChangeTokenStaticHelper_WithSameNameInOtherNamespace_ReturnsFalse()
    {
        var compilation = CreateCompilation(FrameworkStubSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("NotTheFramework.ChangeToken");

        Assert.False(wellKnownTypes.IsChangeTokenStaticHelper(type));
    }

    [Fact]
    public void IsChangeTokenStaticHelper_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation(FrameworkStubSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        Assert.False(wellKnownTypes.IsChangeTokenStaticHelper(null));
    }

    [Fact]
    public void IsHostApplicationLifetimeIncludingSourceDeclared_WithStub_ReturnsTrue()
    {
        var compilation = CreateCompilation(FrameworkStubSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Hosting.IHostApplicationLifetime"
        );

        Assert.True(wellKnownTypes.IsHostApplicationLifetimeIncludingSourceDeclared(type));
    }

    [Fact]
    public void IsHostApplicationLifetimeIncludingSourceDeclared_WithOtherNamespace_ReturnsFalse()
    {
        var compilation = CreateCompilation(FrameworkStubSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("NotTheFramework.IHostApplicationLifetime");

        Assert.False(wellKnownTypes.IsHostApplicationLifetimeIncludingSourceDeclared(type));
    }

    [Fact]
    public void IsHostApplicationLifetimeIncludingSourceDeclared_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation(FrameworkStubSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        Assert.False(wellKnownTypes.IsHostApplicationLifetimeIncludingSourceDeclared(null));
    }

    #endregion

    #region HttpClient Symbol Tests (DI029)

    private const string HttpDerivedSource = """
        using System.Net.Http;
        public sealed class MyHttpClient : HttpClient { }
        public sealed class MyHandler : HttpClientHandler { }
        """;

    [Fact]
    public void Create_WithHttpReferences_ResolvesHttpSymbols()
    {
        var compilation = CreateCompilation("public class Test { }");

        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        Assert.NotNull(wellKnownTypes.HttpClient);
        Assert.NotNull(wellKnownTypes.HttpMessageHandler);
        Assert.NotNull(wellKnownTypes.HttpClientHandler);
    }

    [Fact]
    public void IsHttpClient_WithHttpClient_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("System.Net.Http.HttpClient");

        Assert.True(wellKnownTypes.IsHttpClient(type));
    }

    [Fact]
    public void IsHttpClient_WithDerivedType_ReturnsFalse()
    {
        var compilation = CreateCompilation(HttpDerivedSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("MyHttpClient");

        Assert.False(wellKnownTypes.IsHttpClient(type));
    }

    [Fact]
    public void IsHttpClient_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        Assert.False(wellKnownTypes.IsHttpClient(null));
    }

    [Fact]
    public void IsRotatableHandler_WithHttpClientHandler_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("System.Net.Http.HttpClientHandler");

        Assert.True(wellKnownTypes.IsRotatableHandler(type));
    }

    [Fact]
    public void IsRotatableHandler_WithAbstractMessageHandler_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("System.Net.Http.HttpMessageHandler");

        Assert.False(wellKnownTypes.IsRotatableHandler(type));
    }

    [Fact]
    public void IsRotatableHandler_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        Assert.False(wellKnownTypes.IsRotatableHandler(null));
    }

    [Fact]
    public void DerivesFromHttpMessageHandler_WithUserDerivedHandler_ReturnsTrue()
    {
        var compilation = CreateCompilation(HttpDerivedSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("MyHandler");

        Assert.True(wellKnownTypes.DerivesFromHttpMessageHandler(type));
    }

    [Fact]
    public void DerivesFromHttpMessageHandler_WithUnrelatedType_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("System.Object");

        Assert.False(wellKnownTypes.DerivesFromHttpMessageHandler(type));
    }

    [Fact]
    public void DerivesFromHttpMessageHandler_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        Assert.False(wellKnownTypes.DerivesFromHttpMessageHandler(null));
    }

    [Fact]
    public void IsSocketOwningCreationType_WithHttpClient_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("System.Net.Http.HttpClient");

        Assert.True(wellKnownTypes.IsSocketOwningCreationType(type));
    }

    [Fact]
    public void IsSocketOwningCreationType_WithHttpClientHandler_ReturnsTrue()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var type = compilation.GetTypeByMetadataName("System.Net.Http.HttpClientHandler");

        Assert.True(wellKnownTypes.IsSocketOwningCreationType(type));
    }

    [Fact]
    public void IsSocketOwningCreationType_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        Assert.False(wellKnownTypes.IsSocketOwningCreationType(null));
    }

    #endregion

    #region Collection And Cache Symbol Tests (DI030)

    [Fact]
    public void Create_WithCollectionReferences_ResolvesCollectionSymbols()
    {
        var compilation = CreateCompilation("public class Test { }");

        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        Assert.NotNull(wellKnownTypes.ConcurrentDictionaryOfTKeyTValue);
        Assert.NotNull(wellKnownTypes.DictionaryOfTKeyTValue);
        Assert.NotNull(wellKnownTypes.ListOfT);
        Assert.NotNull(wellKnownTypes.HashSetOfT);
        Assert.NotNull(wellKnownTypes.QueueOfT);
        Assert.NotNull(wellKnownTypes.ConcurrentBagOfT);
        Assert.NotNull(wellKnownTypes.ConcurrentQueueOfT);
    }

    private const string CollectionFieldSource = """
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        public sealed class Holder
        {
            public ConcurrentDictionary<string, int> Cache = new();
            public List<string> Items = new();
            public IDictionary<string, int> ViaInterface = new Dictionary<string, int>();
            public string Plain = "";
        }
        """;

    private static ITypeSymbol FieldType(Compilation compilation, string fieldName)
    {
        var holder = compilation.GetTypeByMetadataName("Holder")!;
        return holder.GetMembers(fieldName).OfType<IFieldSymbol>().Single().Type;
    }

    [Fact]
    public void IsUnboundedGrowthCollection_WithConcurrentDictionary_ReturnsTrueAndValueType()
    {
        var compilation = CreateCompilation(CollectionFieldSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        var result = wellKnownTypes.IsUnboundedGrowthCollection(
            FieldType(compilation, "Cache"),
            out var elementType
        );

        Assert.True(result);
        Assert.Equal("Int32", elementType!.Name);
    }

    [Fact]
    public void IsUnboundedGrowthCollection_WithList_ReturnsTrueAndElementType()
    {
        var compilation = CreateCompilation(CollectionFieldSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        var result = wellKnownTypes.IsUnboundedGrowthCollection(
            FieldType(compilation, "Items"),
            out var elementType
        );

        Assert.True(result);
        Assert.Equal("String", elementType!.Name);
    }

    [Fact]
    public void IsUnboundedGrowthCollection_WithInterfaceTypedField_ReturnsFalse()
    {
        var compilation = CreateCompilation(CollectionFieldSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        var result = wellKnownTypes.IsUnboundedGrowthCollection(
            FieldType(compilation, "ViaInterface"),
            out var elementType
        );

        Assert.False(result);
        Assert.Null(elementType);
    }

    [Fact]
    public void IsUnboundedGrowthCollection_WithNonGenericType_ReturnsFalse()
    {
        var compilation = CreateCompilation(CollectionFieldSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        var result = wellKnownTypes.IsUnboundedGrowthCollection(
            FieldType(compilation, "Plain"),
            out var elementType
        );

        Assert.False(result);
        Assert.Null(elementType);
    }

    [Fact]
    public void IsUnboundedGrowthCollection_WithNull_ReturnsFalse()
    {
        var compilation = CreateCompilation(CollectionFieldSource);
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;

        var result = wellKnownTypes.IsUnboundedGrowthCollection(null, out var elementType);

        Assert.False(result);
        Assert.Null(elementType);
    }

    // The caching abstractions are not referenced by this test project. That is itself the case
    // worth pinning: every caching predicate must answer false rather than throw when the package
    // is absent, which is what lets DI030 Tier B bail out cleanly. Real-symbol coverage for Tier B
    // lives in DI030_UnboundedSingletonCacheAnalyzerTests, which uses the framework reference set.
    [Fact]
    public void CachingPredicates_WhenPackageAbsent_ReturnFalse()
    {
        var compilation = CreateCompilation("public class Test { }");
        var wellKnownTypes = WellKnownTypes.Create(compilation)!;
        var objectType = compilation.GetTypeByMetadataName("System.Object");

        Assert.Null(wellKnownTypes.MemoryCacheEntryOptions);
        Assert.Null(wellKnownTypes.ICacheEntry);
        Assert.Null(wellKnownTypes.MemoryCacheOptions);
        Assert.False(wellKnownTypes.IsMemoryCacheEntryOptions(objectType));
        Assert.False(wellKnownTypes.IsCacheEntry(objectType));
        Assert.False(wellKnownTypes.IsMemoryCacheOptions(objectType));
        Assert.False(wellKnownTypes.ImplementsMemoryCache(objectType));
        Assert.False(wellKnownTypes.IsMemoryCacheEntryOptions(null));
        Assert.False(wellKnownTypes.IsCacheEntry(null));
        Assert.False(wellKnownTypes.IsMemoryCacheOptions(null));
        Assert.False(wellKnownTypes.ImplementsMemoryCache(null));
    }

    #endregion
}
