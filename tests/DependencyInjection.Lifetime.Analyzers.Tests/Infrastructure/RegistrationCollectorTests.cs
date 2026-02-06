using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

public class RegistrationCollectorTests
{
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

        // TryAdd doesn't go into main registrations dictionary
        Assert.Empty(collector.Registrations);
        // But it does go into ordered registrations
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

    #endregion
}
