using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

public class RegistrationCollectorTests
{
    private const string EfCoreStubs = """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
            public class DbContextOptions<TContext> where TContext : DbContext { }
            public class DbContextOptionsBuilder { }
            public interface IDbContextFactory<TContext> where TContext : DbContext
            {
                TContext CreateDbContext();
            }
        }

        namespace Microsoft.Extensions.DependencyInjection
        {
            public static class EntityFrameworkServiceCollectionExtensions
            {
                public static IServiceCollection AddDbContext<TContext>(
                    this IServiceCollection services,
                    ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
                    ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
                    where TContext : Microsoft.EntityFrameworkCore.DbContext => services;

                public static IServiceCollection AddDbContext<TContextService, TContextImplementation>(
                    this IServiceCollection services,
                    ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
                    ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
                    where TContextService : class
                    where TContextImplementation : Microsoft.EntityFrameworkCore.DbContext, TContextService => services;

                public static IServiceCollection AddDbContextFactory<TContext>(
                    this IServiceCollection services,
                    System.Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder>? optionsAction = null,
                    ServiceLifetime lifetime = ServiceLifetime.Singleton)
                    where TContext : Microsoft.EntityFrameworkCore.DbContext => services;

                public static IServiceCollection AddDbContextFactory<TContext, TFactory>(
                    this IServiceCollection services,
                    System.Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder>? optionsAction = null,
                    ServiceLifetime lifetime = ServiceLifetime.Singleton)
                    where TContext : Microsoft.EntityFrameworkCore.DbContext
                    where TFactory : class, Microsoft.EntityFrameworkCore.IDbContextFactory<TContext> => services;

                public static IServiceCollection AddDbContextPool<TContext>(
                    this IServiceCollection services,
                    System.Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction,
                    int poolSize = 1024)
                    where TContext : Microsoft.EntityFrameworkCore.DbContext => services;

                public static IServiceCollection AddDbContextPool<TContextService, TContextImplementation>(
                    this IServiceCollection services,
                    System.Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction,
                    int poolSize = 1024)
                    where TContextService : class
                    where TContextImplementation : Microsoft.EntityFrameworkCore.DbContext, TContextService => services;

                public static IServiceCollection AddPooledDbContextFactory<TContext>(
                    this IServiceCollection services,
                    System.Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction,
                    int poolSize = 1024)
                    where TContext : Microsoft.EntityFrameworkCore.DbContext => services;
            }
        }

        """;

    private static readonly MetadataReference[] DiReferences =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(IServiceProvider).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceScope).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions).Assembly.Location)
    ];

    private static (Compilation compilation, SemanticModel semanticModel, InvocationExpressionSyntax[] invocations)
        CreateCompilationWithInvocations(string source, bool includeDiReferences = true)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = includeDiReferences
            ? DiReferences
            : [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var invocations = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .ToArray();

        return (compilation, semanticModel, invocations);
    }

    private static (Compilation compilation, FileAnalysis[] files) CreateCompilationWithInvocations(
        params (string filePath, string source)[] files)
    {
        var syntaxTrees = files
            .Select(file => CSharpSyntaxTree.ParseText(file.source, path: file.filePath))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            DiReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var fileAnalyses = syntaxTrees
            .Select(tree => new FileAnalysis(
                tree,
                compilation.GetSemanticModel(tree),
                tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray()))
            .ToArray();

        return (compilation, fileAnalyses);
    }

    #region Create Tests

    [Fact]
    public void Create_WithDIReferences_ReturnsInstance()
    {
        var source = "public class Test { }";
        var (compilation, _, _) = CreateCompilationWithInvocations(source);

        var collector = RegistrationCollector.Create(compilation);

        Assert.NotNull(collector);
    }

    [Fact]
    public void Create_WithoutDIReferences_ReturnsNull()
    {
        var source = "public class Test { }";
        var (compilation, _, _) = CreateCompilationWithInvocations(source, includeDiReferences: false);

        var collector = RegistrationCollector.Create(compilation);

        Assert.Null(collector);
    }

    #endregion

    #region AnalyzeInvocation - Basic Registrations

    [Fact]
    public void AnalyzeInvocation_AddSingleton_RecordsRegistration()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public interface IMyService { }
            public class MyService : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Single(collector.Registrations);
        var registration = collector.Registrations.First();
        Assert.Equal("IMyService", registration.ServiceType.Name);
        Assert.NotNull(registration.ImplementationType);
        Assert.Equal("MyService", registration.ImplementationType.Name);
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
    }

    [Fact]
    public void AnalyzeInvocation_AddScoped_RecordsRegistration()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public interface IMyService { }
            public class MyService : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Single(collector.Registrations);
        var registration = collector.Registrations.First();
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }

    [Fact]
    public void AnalyzeInvocation_AddTransient_RecordsRegistration()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public interface IMyService { }
            public class MyService : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddTransient<IMyService, MyService>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Single(collector.Registrations);
        var registration = collector.Registrations.First();
        Assert.Equal(ServiceLifetime.Transient, registration.Lifetime);
    }

    #endregion

    #region AnalyzeInvocation - TryAdd Methods

    [Fact]
    public void AnalyzeInvocation_TryAddSingleton_RecordsAsTryAdd()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;
            public interface IMyService { }
            public class MyService : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.TryAddSingleton<IMyService, MyService>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        // Effective TryAdd registrations participate in the main registration graph.
        Assert.Single(collector.Registrations);
        Assert.Equal("IMyService", collector.Registrations.First().ServiceType.Name);

        // Ordered registrations still retain TryAdd provenance for DI012.
        Assert.Single(collector.OrderedRegistrations);
        var orderedReg = collector.OrderedRegistrations.First();
        Assert.True(orderedReg.IsTryAdd);
        Assert.Equal(ServiceLifetime.Singleton, orderedReg.Lifetime);
    }

    [Fact]
    public void AnalyzeInvocation_TryAddScoped_RecordsAsTryAdd()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;
            public interface IMyService { }
            public class MyService : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.TryAddScoped<IMyService, MyService>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Single(collector.OrderedRegistrations);
        var orderedReg = collector.OrderedRegistrations.First();
        Assert.True(orderedReg.IsTryAdd);
        Assert.Equal(ServiceLifetime.Scoped, orderedReg.Lifetime);
    }

    [Fact]
    public void AnalyzeInvocation_TryAddSingleton_WhenShadowed_DoesNotEnterMainRegistrations()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;
            public interface IMyService { }
            public class FirstService : IMyService { }
            public class SecondService : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, FirstService>();
                    services.TryAddSingleton<IMyService, SecondService>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Single(collector.Registrations);
        Assert.Equal("FirstService", collector.Registrations.First().ImplementationType?.Name);

        Assert.Equal(2, collector.OrderedRegistrations.Count());
        Assert.Contains(collector.OrderedRegistrations, registration => registration.IsTryAdd);
    }

    #endregion

    #region AnalyzeInvocation - Generic Type Extraction

    [Fact]
    public void AnalyzeInvocation_GenericSingleTypeArg_ExtractsType()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public class MyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<MyService>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Single(collector.Registrations);
        var registration = collector.Registrations.First();
        Assert.Equal("MyService", registration.ServiceType.Name);
        Assert.NotNull(registration.ImplementationType);
        Assert.Equal("MyService", registration.ImplementationType.Name);
    }

    [Fact]
    public void AnalyzeInvocation_GenericDoubleTypeArg_ExtractsBothTypes()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public interface IMyService { }
            public class MyService : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Single(collector.Registrations);
        var registration = collector.Registrations.First();
        Assert.Equal("IMyService", registration.ServiceType.Name);
        Assert.NotNull(registration.ImplementationType);
        Assert.Equal("MyService", registration.ImplementationType.Name);
    }

    [Fact]
    public void AnalyzeInvocation_GenericSingleTypeArgWithImplementationInstance_TracksInstanceType()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public interface IMyService { }
            public class MyService : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(new MyService());
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Single(collector.Registrations);
        var registration = collector.Registrations.First();
        Assert.Equal("IMyService", registration.ServiceType.Name);
        Assert.NotNull(registration.ImplementationType);
        Assert.Equal("MyService", registration.ImplementationType.Name);
        Assert.True(registration.HasImplementationInstance);
    }

    [Fact]
    public void AnalyzeInvocation_GenericSingleTypeArgWithNamedImplementationInstance_TracksInstanceType()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public interface IMyService { }
            public class MyService : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(implementationInstance: new MyService());
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Single(collector.Registrations);
        var registration = collector.Registrations.First();
        Assert.Equal("IMyService", registration.ServiceType.Name);
        Assert.NotNull(registration.ImplementationType);
        Assert.Equal("MyService", registration.ImplementationType.Name);
        Assert.True(registration.HasImplementationInstance);
    }

    #endregion

    #region AnalyzeInvocation - typeof Pattern

    [Fact]
    public void AnalyzeInvocation_TypeofPattern_TracksRegistration()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public interface IMyService { }
            public class MyService : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IMyService), typeof(MyService));
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Single(collector.OrderedRegistrations);
        Assert.Single(collector.Registrations);

        var registration = collector.Registrations.First();
        Assert.Equal("IMyService", registration.ServiceType.Name);
        Assert.NotNull(registration.ImplementationType);
        Assert.Equal("MyService", registration.ImplementationType!.Name);
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
    }

    [Fact]
    public void AnalyzeInvocation_TypeofPatternSingleArg_TracksRegistration()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public class MyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton(typeof(MyService));
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Single(collector.OrderedRegistrations);
        Assert.Single(collector.Registrations);

        var registration = collector.Registrations.First();
        Assert.Equal("MyService", registration.ServiceType.Name);
        Assert.NotNull(registration.ImplementationType);
        Assert.Equal("MyService", registration.ImplementationType!.Name);
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
    }

    [Fact]
    public void AnalyzeInvocation_TypeofPatternWithImplementationInstance_TracksInstanceType()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public interface IMyService { }
            public class MyService : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IMyService), new MyService());
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Single(collector.OrderedRegistrations);
        Assert.Single(collector.Registrations);

        var registration = collector.Registrations.First();
        Assert.Equal("IMyService", registration.ServiceType.Name);
        Assert.NotNull(registration.ImplementationType);
        Assert.Equal("MyService", registration.ImplementationType!.Name);
        Assert.True(registration.HasImplementationInstance);
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
    }

    #endregion

    #region AnalyzeInvocation - Non-Extension Methods

    [Fact]
    public void AnalyzeInvocation_NonExtensionMethod_NoRegistration()
    {
        var source = """
            public class Startup
            {
                public void AddSingleton() { }
                public void Configure()
                {
                    AddSingleton();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Empty(collector.Registrations);
        Assert.Empty(collector.OrderedRegistrations);
    }

    [Fact]
    public void AnalyzeInvocation_NonServiceCollectionExtension_NoRegistration()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            public class Startup
            {
                public void Configure()
                {
                    var list = new List<int>();
                    list.First();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Empty(collector.Registrations);
    }

    #endregion

    #region GetLifetime Tests

    [Fact]
    public void GetLifetime_RegisteredType_ReturnsCorrectLifetime()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public interface IMyService { }
            public class MyService : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var serviceType = compilation.GetTypeByMetadataName("IMyService");
        var lifetime = collector.GetLifetime(serviceType);

        Assert.Equal(ServiceLifetime.Scoped, lifetime);
    }

    [Fact]
    public void GetLifetime_UnregisteredType_ReturnsNull()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public interface IMyService { }
            public class MyService : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        // Query for a type that wasn't registered
        var objectType = compilation.GetTypeByMetadataName("System.Object");
        var lifetime = collector.GetLifetime(objectType);

        Assert.Null(lifetime);
    }

    [Fact]
    public void GetLifetime_NullType_ReturnsNull()
    {
        var source = "public class Test { }";
        var (compilation, _, _) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        var lifetime = collector.GetLifetime(null);

        Assert.Null(lifetime);
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContext_DefaultsToScopedAndRegistersOptions()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext
            {
                public MyDbContext(DbContextOptions<MyDbContext> options) { }
            }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContext<MyDbContext>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var contextType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var optionsType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1")!
            .Construct(contextType);

        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(contextType));
        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(optionsType));
        Assert.Equal(2, collector.Registrations.Count());
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContext_HonorsExplicitContextLifetime()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContext<MyDbContext>(ServiceLifetime.Singleton);
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var contextType = compilation.GetTypeByMetadataName("MyDbContext")!;

        Assert.Equal(ServiceLifetime.Singleton, collector.GetLifetime(contextType));
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContext_ServiceImplementationOverload_RecordsServiceAndImplementation()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public interface IMyDbContext { }
            public class MyDbContext : DbContext, IMyDbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContext<IMyDbContext, MyDbContext>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var serviceType = compilation.GetTypeByMetadataName("IMyDbContext")!;
        var implementationType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var registration = collector.Registrations.Single(registration =>
            SymbolEqualityComparer.Default.Equals(registration.ServiceType, serviceType));
        var implementationRegistration = collector.Registrations.Single(registration =>
            SymbolEqualityComparer.Default.Equals(registration.ServiceType, implementationType));

        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.Equal("MyDbContext", registration.ImplementationType?.Name);
        Assert.Equal(ServiceLifetime.Scoped, implementationRegistration.Lifetime);
        Assert.Null(implementationRegistration.ImplementationType);
        Assert.True(implementationRegistration.HasImplementationInstance);
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContext_ServiceImplementationOverload_DoesNotOverrideExistingImplementationRegistration()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public interface IMyDbContext { }
            public class MyDbContext : DbContext, IMyDbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<MyDbContext>();
                    services.AddDbContext<IMyDbContext, MyDbContext>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var serviceType = compilation.GetTypeByMetadataName("IMyDbContext")!;
        var implementationType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var implementationRegistration = collector.Registrations.Single(registration =>
            SymbolEqualityComparer.Default.Equals(registration.ServiceType, implementationType));

        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(serviceType));
        Assert.Equal(ServiceLifetime.Singleton, implementationRegistration.Lifetime);
        Assert.Equal("MyDbContext", implementationRegistration.ImplementationType?.Name);
        Assert.False(implementationRegistration.HasImplementationInstance);
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContext_LaterRegistrationPreservesExistingTryAddRegistrations()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContext<MyDbContext>();
                    services.AddDbContext<MyDbContext>(ServiceLifetime.Singleton, ServiceLifetime.Singleton);
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var contextType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var optionsType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1")!
            .Construct(contextType);

        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(contextType));
        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(optionsType));
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContextFactory_RegistersScopedContextAndSingletonFactoryOptions()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext
            {
                public MyDbContext(DbContextOptions<MyDbContext> options) { }
            }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContextFactory<MyDbContext>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var contextType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var optionsType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1")!
            .Construct(contextType);
        var factoryType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IDbContextFactory`1")!
            .Construct(contextType);

        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(contextType));
        Assert.Equal(ServiceLifetime.Singleton, collector.GetLifetime(optionsType));
        Assert.Equal(ServiceLifetime.Singleton, collector.GetLifetime(factoryType));
        Assert.Equal(3, collector.Registrations.Count());
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContextFactory_HonorsExplicitFactoryAndOptionsLifetime()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContextFactory<MyDbContext>(lifetime: ServiceLifetime.Scoped);
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var contextType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var optionsType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1")!
            .Construct(contextType);
        var factoryType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IDbContextFactory`1")!
            .Construct(contextType);

        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(contextType));
        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(optionsType));
        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(factoryType));
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContextFactory_TransientLifetimeMakesContextTransient()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContextFactory<MyDbContext>(lifetime: ServiceLifetime.Transient);
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var contextType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var optionsType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1")!
            .Construct(contextType);
        var factoryType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IDbContextFactory`1")!
            .Construct(contextType);

        Assert.Equal(ServiceLifetime.Transient, collector.GetLifetime(contextType));
        Assert.Equal(ServiceLifetime.Transient, collector.GetLifetime(optionsType));
        Assert.Equal(ServiceLifetime.Transient, collector.GetLifetime(factoryType));
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContextFactory_DoesNotOverrideExistingContextRegistration()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<MyDbContext>();
                    services.AddDbContextFactory<MyDbContext>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var contextType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var optionsType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1")!
            .Construct(contextType);
        var factoryType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IDbContextFactory`1")!
            .Construct(contextType);
        var contextRegistration = collector.Registrations.Single(registration =>
            SymbolEqualityComparer.Default.Equals(registration.ServiceType, contextType));

        Assert.Equal(ServiceLifetime.Singleton, contextRegistration.Lifetime);
        Assert.Equal("MyDbContext", contextRegistration.ImplementationType?.Name);
        Assert.Equal(ServiceLifetime.Singleton, collector.GetLifetime(optionsType));
        Assert.Equal(ServiceLifetime.Singleton, collector.GetLifetime(factoryType));
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContextFactory_DoesNotOverrideExistingOptionsOrFactoryRegistrations()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<DbContextOptions<MyDbContext>>();
                    services.AddScoped<IDbContextFactory<MyDbContext>>();
                    services.AddDbContextFactory<MyDbContext>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var contextType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var optionsType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1")!
            .Construct(contextType);
        var factoryType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IDbContextFactory`1")!
            .Construct(contextType);

        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(contextType));
        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(optionsType));
        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(factoryType));
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContextFactory_CustomFactoryRecordsImplementation()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }
            public sealed class MyDbContextFactory : IDbContextFactory<MyDbContext>
            {
                public MyDbContext CreateDbContext() => new MyDbContext();
            }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContextFactory<MyDbContext, MyDbContextFactory>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var contextType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var factoryType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IDbContextFactory`1")!
            .Construct(contextType);
        var registration = collector.Registrations.Single(registration =>
            SymbolEqualityComparer.Default.Equals(registration.ServiceType, factoryType));

        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
        Assert.Equal("MyDbContextFactory", registration.ImplementationType?.Name);
        Assert.False(registration.HasImplementationInstance);
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContextPool_RegistersScopedContextAndSingletonOptions()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext
            {
                public MyDbContext(DbContextOptions<MyDbContext> options) { }
            }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContextPool<MyDbContext>(_ => { });
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var contextType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var optionsType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1")!
            .Construct(contextType);

        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(contextType));
        Assert.Equal(ServiceLifetime.Singleton, collector.GetLifetime(optionsType));
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContextPool_DoesNotOverrideExistingOptionsRegistration()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<DbContextOptions<MyDbContext>>();
                    services.AddDbContextPool<MyDbContext>(_ => { });
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var contextType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var optionsType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1")!
            .Construct(contextType);

        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(contextType));
        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(optionsType));
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContextPool_ServiceImplementationOverload_RecordsServiceAndImplementation()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public interface IMyDbContext { }
            public class MyDbContext : DbContext, IMyDbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddDbContextPool<IMyDbContext, MyDbContext>(_ => { });
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var serviceType = compilation.GetTypeByMetadataName("IMyDbContext")!;
        var implementationType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var registration = collector.Registrations.Single(registration =>
            SymbolEqualityComparer.Default.Equals(registration.ServiceType, serviceType));
        var implementationRegistration = collector.Registrations.Single(registration =>
            SymbolEqualityComparer.Default.Equals(registration.ServiceType, implementationType));

        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.Equal("MyDbContext", registration.ImplementationType?.Name);
        Assert.Equal(ServiceLifetime.Scoped, implementationRegistration.Lifetime);
        Assert.Null(implementationRegistration.ImplementationType);
        Assert.True(implementationRegistration.HasImplementationInstance);
    }

    [Fact]
    public void AnalyzeInvocation_AddDbContextPool_ServiceImplementationOverload_DoesNotOverrideExistingImplementationRegistration()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public interface IMyDbContext { }
            public class MyDbContext : DbContext, IMyDbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<MyDbContext>();
                    services.AddDbContextPool<IMyDbContext, MyDbContext>(_ => { });
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var serviceType = compilation.GetTypeByMetadataName("IMyDbContext")!;
        var implementationType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var implementationRegistration = collector.Registrations.Single(registration =>
            SymbolEqualityComparer.Default.Equals(registration.ServiceType, implementationType));

        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(serviceType));
        Assert.Equal(ServiceLifetime.Singleton, implementationRegistration.Lifetime);
        Assert.Equal("MyDbContext", implementationRegistration.ImplementationType?.Name);
        Assert.False(implementationRegistration.HasImplementationInstance);
    }

    [Fact]
    public void AnalyzeInvocation_AddPooledDbContextFactory_RegistersScopedContextAndSingletonFactoryOptions()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddPooledDbContextFactory<MyDbContext>(_ => { });
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var contextType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var optionsType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1")!
            .Construct(contextType);
        var factoryType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IDbContextFactory`1")!
            .Construct(contextType);

        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(contextType));
        Assert.Equal(ServiceLifetime.Singleton, collector.GetLifetime(optionsType));
        Assert.Equal(ServiceLifetime.Singleton, collector.GetLifetime(factoryType));
    }

    [Fact]
    public void AnalyzeInvocation_AddPooledDbContextFactory_DoesNotOverrideExistingContextRegistration()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<MyDbContext>();
                    services.AddPooledDbContextFactory<MyDbContext>(_ => { });
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var contextType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var optionsType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1")!
            .Construct(contextType);
        var factoryType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IDbContextFactory`1")!
            .Construct(contextType);
        var contextRegistration = collector.Registrations.Single(registration =>
            SymbolEqualityComparer.Default.Equals(registration.ServiceType, contextType));

        Assert.Equal(ServiceLifetime.Singleton, contextRegistration.Lifetime);
        Assert.Equal("MyDbContext", contextRegistration.ImplementationType?.Name);
        Assert.Equal(ServiceLifetime.Singleton, collector.GetLifetime(optionsType));
        Assert.Equal(ServiceLifetime.Singleton, collector.GetLifetime(factoryType));
    }

    [Fact]
    public void AnalyzeInvocation_AddPooledDbContextFactory_DoesNotOverrideExistingOptionsOrFactoryRegistrations()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            """ + EfCoreStubs + """
            public class MyDbContext : DbContext { }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<DbContextOptions<MyDbContext>>();
                    services.AddScoped<IDbContextFactory<MyDbContext>>();
                    services.AddPooledDbContextFactory<MyDbContext>(_ => { });
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var contextType = compilation.GetTypeByMetadataName("MyDbContext")!;
        var optionsType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1")!
            .Construct(contextType);
        var factoryType = compilation
            .GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IDbContextFactory`1")!
            .Construct(contextType);

        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(contextType));
        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(optionsType));
        Assert.Equal(ServiceLifetime.Scoped, collector.GetLifetime(factoryType));
    }

    #endregion

    #region TryGetRegistration Tests

    [Fact]
    public void TryGetRegistration_Registered_ReturnsTrue()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public interface IMyService { }
            public class MyService : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var serviceType = compilation.GetTypeByMetadataName("IMyService")!;
        var result = collector.TryGetRegistration(serviceType, null, false, out var registration);

        Assert.True(result);
        Assert.NotNull(registration);
        Assert.Equal("IMyService", registration.ServiceType.Name);
    }

    [Fact]
    public void TryGetRegistration_NotRegistered_ReturnsFalse()
    {
        var source = "public class Test { }";
        var (compilation, _, _) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        var objectType = compilation.GetTypeByMetadataName("System.Object")!;
        var result = collector.TryGetRegistration(objectType, null, false, out var registration);

        Assert.False(result);
        Assert.Null(registration);
    }

    #endregion

    #region OrderedRegistrations Tests

    [Fact]
    public void OrderedRegistrations_MaintainsOrder()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public interface IService1 { }
            public interface IService2 { }
            public interface IService3 { }
            public class Service1 : IService1 { }
            public class Service2 : IService2 { }
            public class Service3 : IService3 { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IService1, Service1>();
                    services.AddScoped<IService2, Service2>();
                    services.AddTransient<IService3, Service3>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var orderedRegistrations = collector.OrderedRegistrations.OrderBy(r => r.Order).ToList();

        Assert.Equal(3, orderedRegistrations.Count);
        Assert.Equal("IService1", orderedRegistrations[0].ServiceType.Name);
        Assert.Equal("IService2", orderedRegistrations[1].ServiceType.Name);
        Assert.Equal("IService3", orderedRegistrations[2].ServiceType.Name);
    }

    [Fact]
    public void OrderedRegistrations_IncludesTryAddMethods()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;
            public interface IService1 { }
            public interface IService2 { }
            public class Service1 : IService1 { }
            public class Service2 : IService2 { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IService1, Service1>();
                    services.TryAddSingleton<IService2, Service2>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var orderedRegistrations = collector.OrderedRegistrations.OrderBy(r => r.Order).ToList();

        Assert.Equal(2, orderedRegistrations.Count);
        Assert.False(orderedRegistrations[0].IsTryAdd);
        Assert.True(orderedRegistrations[1].IsTryAdd);
    }

    [Fact]
    public void AnalyzeInvocation_OpenGenericWithTypeofPattern_TracksRegistration()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            public interface IRepository<T> { }
            public class Repository<T> : IRepository<T> { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        Assert.Single(collector.OrderedRegistrations);
        Assert.Single(collector.Registrations);

        var registration = collector.Registrations.First();
        Assert.Equal("IRepository", registration.ServiceType.Name);
        Assert.True(registration.ServiceType.IsUnboundGenericType);
        Assert.NotNull(registration.ImplementationType);
        Assert.Equal("Repository", registration.ImplementationType!.Name);
        Assert.True(registration.ImplementationType.IsUnboundGenericType);
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
    }

    #endregion

    #region Duplicate Registration Tests

    [Fact]
    public void AnalyzeInvocation_DuplicateRegistration_LaterOverrides()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;
            public interface IMyService { }
            public class Service1 : IMyService { }
            public class Service2 : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, Service1>();
                    services.AddSingleton<IMyService, Service2>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations)
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        // Only one registration in main dictionary (later overrides)
        Assert.Single(collector.Registrations);
        var registration = collector.Registrations.First();
        Assert.NotNull(registration.ImplementationType);
        Assert.Equal("Service2", registration.ImplementationType.Name);

        // But ordered registrations has both
        Assert.Equal(2, collector.OrderedRegistrations.Count());
    }

    [Fact]
    public void AnalyzeInvocation_TryAddAfterAdd_PreservesEarlierEffectiveRegistrationWhenCollectedOutOfOrder()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;
            public interface IMyService { }
            public class Service1 : IMyService { }
            public class Service2 : IMyService { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, Service1>();
                    services.TryAddTransient<IMyService, Service2>();
                }
            }
            """;
        var (compilation, semanticModel, invocations) = CreateCompilationWithInvocations(source);
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var invocation in invocations.Reverse())
        {
            collector.AnalyzeInvocation(invocation, semanticModel);
        }

        var registration = Assert.Single(collector.Registrations);
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
        Assert.Equal("Service1", registration.ImplementationType?.Name);
    }

    [Fact]
    public void AnalyzeInvocation_DuplicateRegistration_StableAcrossFiles()
    {
        var sharedTypes = """
            using Microsoft.Extensions.DependencyInjection;
            public interface IMyService { }
            public class ServiceA : IMyService { }
            public class ServiceB : IMyService { }
            """;

        var sourceB = """
            using Microsoft.Extensions.DependencyInjection;

            public static class RegistrationB
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, ServiceB>();
                }
            }
            """;

        var sourceA = """
            using Microsoft.Extensions.DependencyInjection;

            public static class RegistrationA
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, ServiceA>();
                }
            }
            """;

        var (compilation, files) = CreateCompilationWithInvocations(
            ("Common.cs", sharedTypes),
            ("B.cs", sourceB),
            ("A.cs", sourceA));
        var collector = RegistrationCollector.Create(compilation)!;

        foreach (var file in files.Reverse())
        {
            foreach (var invocation in file.Invocations)
            {
                collector.AnalyzeInvocation(invocation, file.SemanticModel);
            }
        }

        var ordered = OrderedRegistrationOrdering.SortBySourceLocation(collector.OrderedRegistrations).ToArray();

        Assert.Equal("A.cs", ordered[0].Location.GetLineSpan().Path);
        Assert.Equal("B.cs", ordered[1].Location.GetLineSpan().Path);
    }

    private sealed record FileAnalysis(
        SyntaxTree Tree,
        SemanticModel SemanticModel,
        InvocationExpressionSyntax[] Invocations);

    #endregion
}
