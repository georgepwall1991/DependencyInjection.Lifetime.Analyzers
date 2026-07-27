using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI009_OpenGenericLifetimeMismatchAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        """;

    private const string OptionsStubs = """
        namespace Microsoft.Extensions.Options
        {
            public interface IOptions<T> { }
            public interface IOptionsSnapshot<T> { }
            public interface IOptionsMonitor<T> { }
        }

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task OpenGenericSingleton_CapturesScopedDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithSpan(16, 9, 16, 75)
                .WithArguments("Repository", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_CapturesTransientDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ITransientService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(ITransientService transient) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class TransientService : ITransientService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithSpan(16, 9, 16, 75)
                .WithArguments("Repository", "transient", "ITransientService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_WithUnresolvableGreedyConstructor_UsesCaptiveFallback_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IUnregisteredDependency { }
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IUnregisteredDependency missing, ISingletonDependency singleton) { }

                public Repository(IScopedDependency scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(23, 9)
                .WithArguments("Repository", "scoped", "IScopedDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_CapturesScopedEnumerableDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IEnumerable<IScopedService> scopedServices) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(18, 9)
                .WithArguments("Repository", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_ClosedSingletonDoesNotHideScopedOpenGenericEnumerable_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ScopedDependency<T> : IDependency<T> { }
            public sealed class SingletonDependency : IDependency<string> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(IEnumerable<IDependency<string>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<>),
                        typeof(ScopedDependency<>));
                    services.AddSingleton<IDependency<string>, SingletonDependency>();
                    {|#0:services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_ClosedAndOpenGenericSingletonEnumerable_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class OpenSingletonDependency<T> : IDependency<T> { }
            public sealed class ClosedSingletonDependency : IDependency<string> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(IEnumerable<IDependency<string>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(
                        typeof(IDependency<>),
                        typeof(OpenSingletonDependency<>));
                    services.AddSingleton<IDependency<string>, ClosedSingletonDependency>();
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_KeyedClosedSingletonDoesNotHideScopedOpenGenericEnumerable_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ScopedDependency<T> : IDependency<T> { }
            public sealed class SingletonDependency : IDependency<string> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    [FromKeyedServices("primary")]
                    IEnumerable<IDependency<string>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped(
                        typeof(IDependency<>),
                        "primary",
                        typeof(ScopedDependency<>));
                    services.AddKeyedSingleton<IDependency<string>, SingletonDependency>(
                        "primary");
                    {|#0:services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>))|};
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80
                .AddPackages([
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0"),
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection", "8.0.0"),
                ]),
        };
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_InapplicableConstrainedOpenGenericEnumerable_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ClassOnlyScopedDependency<T> : IDependency<T>
                where T : class
            {
            }

            public sealed class IntSingletonDependency : IDependency<int> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(IEnumerable<IDependency<int>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<>),
                        typeof(ClassOnlyScopedDependency<>));
                    services.AddSingleton<IDependency<int>, IntSingletonDependency>();
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_ApplicableConstrainedOpenGenericEnumerable_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ClassOnlyScopedDependency<T> : IDependency<T>
                where T : class
            {
            }

            public sealed class StringSingletonDependency : IDependency<string> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(IEnumerable<IDependency<string>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<>),
                        typeof(ClassOnlyScopedDependency<>));
                    services.AddSingleton<IDependency<string>, StringSingletonDependency>();
                    {|#0:services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_UnconstrainedElementCanSatisfyOpenGenericConstraint_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ClassOnlyScopedDependency<T> : IDependency<T>
                where T : class
            {
            }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(IEnumerable<IDependency<T>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<>),
                        typeof(ClassOnlyScopedDependency<>));
                    {|#0:services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_ElementConstraintConflictsWithOpenGenericConstraint_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ClassOnlyScopedDependency<T> : IDependency<T>
                where T : class
            {
            }

            public sealed class Repository<T> : IRepository<T>
                where T : struct
            {
                public Repository(IEnumerable<IDependency<T>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<>),
                        typeof(ClassOnlyScopedDependency<>));
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_DependentConstraintCanMatchOpenConsumer_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<TItem, TPolicy> { }

            public sealed class ScopedDependency<TItem, TPolicy> :
                IDependency<TItem, TPolicy>
                where TItem : TPolicy
            {
            }

            public sealed class Repository<TPolicy> : IRepository<TPolicy>
            {
                public Repository(
                    IEnumerable<IDependency<string, TPolicy>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<,>),
                        typeof(ScopedDependency<,>));
                    {|#0:services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_DependentConstraintCanUseConstructibleBase_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<TItem, TPolicy> { }

            public sealed class ScopedDependency<TItem, TPolicy> :
                IDependency<TItem, TPolicy>
                where TItem : TPolicy
                where TPolicy : new()
            {
            }

            public sealed class Repository<TPolicy> : IRepository<TPolicy>
                where TPolicy : new()
            {
                public Repository(
                    IEnumerable<IDependency<string, TPolicy>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<,>),
                        typeof(ScopedDependency<,>));
                    {|#0:services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_ArrayConstraintSubstitution_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<TCollection, TElement> { }

            public sealed class ScopedDependency<TCollection, TElement> :
                IDependency<TCollection, TElement>
                where TCollection : IEnumerable<TElement[]>
            {
            }

            public sealed class ClosedSingletonDependency :
                IDependency<List<string[]>, string>
            {
            }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    IEnumerable<IDependency<List<string[]>, string>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<,>),
                        typeof(ScopedDependency<,>));
                    services.AddSingleton<
                        IDependency<List<string[]>, string>,
                        ClosedSingletonDependency>();
                    {|#0:services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_OpenArrayArgumentCanSatisfyConcreteInterfaceConstraint_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ScopedDependency<T> : IDependency<T>
                where T : IEnumerable<int>
            {
            }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(IEnumerable<IDependency<T[]>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<>),
                        typeof(ScopedDependency<>));
                    {|#0:services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_NestedContainingTypeConstraintSubstitution_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<TItem, TPolicy> { }

            public class Outer<TPolicy>
            {
                public interface IMarker { }
            }

            public sealed class Marked : Outer<string>.IMarker { }

            public sealed class ScopedDependency<TItem, TPolicy> :
                IDependency<TItem, TPolicy>
                where TItem : Outer<TPolicy>.IMarker
            {
            }

            public sealed class ClosedSingletonDependency :
                IDependency<Marked, string>
            {
            }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    IEnumerable<IDependency<Marked, string>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<,>),
                        typeof(ScopedDependency<,>));
                    services.AddSingleton<
                        IDependency<Marked, string>,
                        ClosedSingletonDependency>();
                    {|#0:services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_DisjointDependentConstraint_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<TItem, TPolicy> { }

            public class ConsumerBase { }
            public class RequiredBase { }

            public sealed class ScopedDependency<TItem, TPolicy> :
                IDependency<TItem, TPolicy>
                where TItem : TPolicy
            {
            }

            public sealed class Repository<T> : IRepository<T>
                where T : ConsumerBase
            {
                public Repository(
                    IEnumerable<IDependency<T, RequiredBase>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<,>),
                        typeof(ScopedDependency<,>));
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_UserDefinedImplicitConversionDoesNotSatisfyClassConstraint_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class Source
            {
                public static implicit operator Target(Source value) => new();
            }

            public class Target { }

            public sealed class ScopedDependency<T> : IDependency<T>
                where T : Target
            {
            }

            public sealed class ClosedSingletonDependency : IDependency<Source> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(IEnumerable<IDependency<Source>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<>),
                        typeof(ScopedDependency<>));
                    services.AddSingleton<IDependency<Source>, ClosedSingletonDependency>();
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_IncompatibleBaseConstraints_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public class RequiredBase { }
            public class ConsumerBase { }

            public sealed class ScopedDependency<T> : IDependency<T>
                where T : RequiredBase
            {
            }

            public sealed class Repository<T> : IRepository<T>
                where T : ConsumerBase
            {
                public Repository(IEnumerable<IDependency<T>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<>),
                        typeof(ScopedDependency<>));
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_ConcreteTypeWithoutPublicConstructorRejectsNewConstraint_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class NeedsPublicConstructorScopedDependency<T> : IDependency<T>
                where T : new()
            {
            }

            public sealed class NoPublicConstructor
            {
                private NoPublicConstructor() { }
            }

            public sealed class ClosedSingletonDependency :
                IDependency<NoPublicConstructor>
            {
            }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    IEnumerable<IDependency<NoPublicConstructor>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<>),
                        typeof(NeedsPublicConstructorScopedDependency<>));
                    services.AddSingleton<
                        IDependency<NoPublicConstructor>,
                        ClosedSingletonDependency>();
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_ConcreteTypeRejectsInterfaceConstraint_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }
            public interface IMarker { }

            public sealed class MarkedScopedDependency<T> : IDependency<T>
                where T : IMarker
            {
            }

            public sealed class ClosedSingletonDependency : IDependency<string> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(IEnumerable<IDependency<string>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<>),
                        typeof(MarkedScopedDependency<>));
                    services.AddSingleton<IDependency<string>, ClosedSingletonDependency>();
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_ConcreteTypeSatisfiesInterfaceConstraint_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }
            public interface IMarker { }

            public sealed class MarkedService : IMarker { }

            public sealed class MarkedScopedDependency<T> : IDependency<T>
                where T : IMarker
            {
            }

            public sealed class ClosedSingletonDependency : IDependency<MarkedService> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    IEnumerable<IDependency<MarkedService>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<>),
                        typeof(MarkedScopedDependency<>));
                    services.AddSingleton<
                        IDependency<MarkedService>,
                        ClosedSingletonDependency>();
                    {|#0:services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_KeyedInapplicableConstrainedOpenGenericEnumerable_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ClassOnlyScopedDependency<T> : IDependency<T>
                where T : class
            {
            }

            public sealed class IntSingletonDependency : IDependency<int> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    [FromKeyedServices("primary")]
                    IEnumerable<IDependency<int>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped(
                        typeof(IDependency<>),
                        "primary",
                        typeof(ClassOnlyScopedDependency<>));
                    services.AddKeyedSingleton<IDependency<int>, IntSingletonDependency>(
                        "primary");
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80
                .AddPackages([
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0"),
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection", "8.0.0"),
                ]),
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_KeyedApplicableConstrainedOpenGenericEnumerable_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ClassOnlyScopedDependency<T> : IDependency<T>
                where T : class
            {
            }

            public sealed class StringSingletonDependency : IDependency<string> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    [FromKeyedServices("primary")]
                    IEnumerable<IDependency<string>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped(
                        typeof(IDependency<>),
                        "primary",
                        typeof(ClassOnlyScopedDependency<>));
                    services.AddKeyedSingleton<IDependency<string>, StringSingletonDependency>(
                        "primary");
                    {|#0:services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>))|};
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80
                .AddPackages([
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0"),
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection", "8.0.0"),
                ]),
        };
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_ImpossibleNestedInvariantConstraint_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }
            public interface IMarker<T> { }

            public sealed class Producer<T> : IMarker<T> { }

            public sealed class ScopedDependency<T> : IDependency<T>
                where T : IMarker<HashSet<int>>
            {
            }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    IEnumerable<IDependency<Producer<List<T>>>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<>),
                        typeof(ScopedDependency<>));
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_ApplicableNestedImplementation_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<TFirst, TSecond> { }
            public interface IDependency<TFirst, TSecond> { }

            public sealed class Outer<TFirst>
            {
                public sealed class ScopedDependency<TSecond> :
                    IDependency<TFirst, TSecond>
                {
                }
            }

            public sealed class Repository<TFirst, TSecond> :
                IRepository<TFirst, TSecond>
            {
                public Repository(
                    IEnumerable<IDependency<string, int>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<,>),
                        typeof(Outer<>.ScopedDependency<>));
                    {|#0:services.AddSingleton(
                        typeof(IRepository<,>),
                        typeof(Repository<,>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_ImplementationBaseConstraintAndConsumerStruct_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public class RequiredBase { }

            public sealed class ScopedDependency<T> : IDependency<T>
                where T : RequiredBase
            {
            }

            public sealed class Repository<T> : IRepository<T>
                where T : struct
            {
                public Repository(IEnumerable<IDependency<T>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<>),
                        typeof(ScopedDependency<>));
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_ImplementationStructConstraintAndConsumerBase_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public class ConsumerBase { }

            public sealed class ScopedDependency<T> : IDependency<T>
                where T : struct
            {
            }

            public sealed class Repository<T> : IRepository<T>
                where T : ConsumerBase
            {
                public Repository(IEnumerable<IDependency<T>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(IDependency<>),
                        typeof(ScopedDependency<>));
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericKeyedSingleton_InheritedKeyedEnumerable_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ScopedDependency<T> : IDependency<T> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    [FromKeyedServices]
                    IEnumerable<IDependency<T>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped(
                        typeof(IDependency<>),
                        "primary",
                        typeof(ScopedDependency<>));
                    {|#0:services.AddKeyedSingleton(
                        typeof(IRepository<>),
                        "primary",
                        typeof(Repository<>))|};
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies =
                AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                    .ReferenceAssembliesWithLatestDi,
        };
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_ServiceDescriptorShape_CapturesTransientDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ITransientService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(ITransientService transient) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.Add(ServiceDescriptor.Singleton(typeof(IRepository<>), typeof(Repository<>)));
                }
            }

            public class TransientService : ITransientService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(16, 9)
                .WithArguments("Repository", "transient", "ITransientService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_TryAddShape_CapturesScopedDependency_ReportsDiagnostic()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.TryAddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(18, 9)
                .WithArguments("Repository", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_RegisteredViaTryAddSingleton_CapturesScopedDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.TryAddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithSpan(18, 9, 18, 78)
                .WithArguments("Repository", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_RegisteredViaServiceDescriptor_CapturesScopedDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.Add(ServiceDescriptor.Singleton(typeof(IRepository<>), typeof(Repository<>)));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithSpan(16, 9, 16, 95)
                .WithArguments("Repository", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task OpenGenericKeyedSingleton_CapturesMatchingScopedDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository([FromKeyedServices("primary")] IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IScopedService, ScopedService>("primary");
                    services.AddKeyedSingleton(typeof(IRepository<>), "primary", typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80
                .AddPackages([
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0"),
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection", "8.0.0"),
                ]),
        };
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithSpan(16, 9, 16, 91)
                .WithArguments("Repository", "scoped", "IScopedService"));

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_WithLongerResolvableConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedDependency scoped) { }

                public Repository(ISingletonDependency singleton, IServiceProvider provider) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_WithOptionalParameterOnLongerSafeConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }
            public interface IOptionalDependency { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedDependency scoped) { }

                public Repository(ISingletonDependency singleton, IServiceProvider provider, IOptionalDependency optional = null) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_WithAmbiguousEquallyGreedyConstructors_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(ISingletonDependency singleton) { }

                public Repository(IScopedDependency scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task OpenGenericSingleton_CapturesSingletonDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISingletonService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(ISingletonService singleton) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonService, SingletonService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class SingletonService : ISingletonService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_CapturesSingletonEnumerableDependency_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface ISingletonService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IEnumerable<ISingletonService> singletons) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonService, SingletonService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class SingletonService : ISingletonService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericScoped_CapturesScopedDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericTransient_CapturesTransientDependency_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ITransientService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(ITransientService transient) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ITransientService, TransientService>();
                    services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
                }
            }

            public class TransientService : ITransientService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ClosedGenericSingleton_CapturesScopedDependency_NoDiagnostic()
    {
        // This should be caught by DI003, not DI009
        // DI009 is specifically for open generics
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton<IRepository<string>, Repository<string>>();
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_NoDependencies_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }

            public class Repository<T> : IRepository<T>
            {
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_WithActivatorUtilitiesConstructor_UsesAttributedConstructor_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public class Repository<T> : IRepository<T>
            {
                [ActivatorUtilitiesConstructor]
                public Repository(ISingletonDependency dep) { }

                public Repository(IScopedDependency dep) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_WithActivatorUtilitiesConstructor_OnCaptiveConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISingletonDependency { }
            public class SingletonDependency : ISingletonDependency { }
            public interface IScopedDependency { }
            public class ScopedDependency : IScopedDependency { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(ISingletonDependency dep) { }

                [ActivatorUtilitiesConstructor]
                public Repository(IScopedDependency dep) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISingletonDependency, SingletonDependency>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(23, 9)
                .WithArguments("Repository", "scoped", "IScopedDependency"));
    }

    [Fact]
    public async Task OpenGenericKeyedSingleton_WithMismatchedScopedKey_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository([FromKeyedServices("secondary")] IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IScopedService, ScopedService>("primary");
                    services.AddKeyedSingleton(typeof(IRepository<>), "primary", typeof(Repository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80
                .AddPackages([
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0"),
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection", "8.0.0"),
                ]),
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_CapturesEnumerableOfOptionsSnapshotWithExplicitSingletonRegistration_ReportsDiagnostic()
    {
        var source = Usings + OptionsStubs + """
            public sealed class MyOptions { }

            public sealed class CustomSnapshot : Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions> { }

            public interface IRepository<T> { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(System.Collections.Generic.IEnumerable<Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions>> snapshots) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions>, CustomSnapshot>();
                    {|#0:services.AddSingleton(typeof(IRepository<>), typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IOptionsSnapshot"));
    }

    [Fact]
    public async Task OpenGenericSingleton_CapturesClosedOptionsSnapshotWithExplicitSingletonRegistration_NoDiagnostic()
    {
        var source = Usings + OptionsStubs + """
            public sealed class MyOptions { }

            public sealed class CustomSnapshot : Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions> { }

            public interface IRepository<T> { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions> snapshot) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions>, CustomSnapshot>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_CapturesOptions_NoDiagnostic()
    {
        var source = Usings + OptionsStubs + """
            public interface IRepository<T> { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(
                    Microsoft.Extensions.Options.IOptions<T> options,
                    Microsoft.Extensions.Options.IOptionsMonitor<T> monitor) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_CapturesOptionsSnapshot_ReportsDiagnostic()
    {
        var source = Usings + OptionsStubs + """
            public interface IRepository<T> { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(Microsoft.Extensions.Options.IOptionsSnapshot<T> snapshot) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IRepository<>), typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IOptionsSnapshot"));
    }

    [Fact]
    public async Task OpenGenericSingleton_CapturesNonGenericOptionsSnapshotInstance_ReportsDiagnostic()
    {
        var source = Usings + OptionsStubs + """
            public sealed class MyOptions { }
            public interface IRepository<T> { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions> snapshot) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IRepository<>), typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IOptionsSnapshot"));
    }

    [Fact]
    public async Task OpenGenericSingleton_IneffectiveTryAddSingletonOnCaptiveImplementation_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IScopedService { }

            public class SafeRepository<T> : IRepository<T>
            {
            }

            public class CaptiveRepository<T> : IRepository<T>
            {
                public CaptiveRepository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(SafeRepository<>));
                    services.TryAddSingleton(typeof(IRepository<>), typeof(CaptiveRepository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_RemovedByRemoveAll_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                    services.RemoveAll(typeof(IRepository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_RemovedByClear_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                    services.Clear();
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_ReactivatedScopedDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IDependency { }

            public sealed class OriginalDependency : IDependency { }
            public sealed class ScopedDependency : IDependency { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(IDependency dependency) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IDependency, OriginalDependency>();
                    services.RemoveAll<IDependency>();
                    services.TryAddScoped<IDependency, ScopedDependency>();
                    {|#0:services.AddSingleton(typeof(IRepository<>), typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_ReactivatedScopedEnumerableDependency_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IDependency { }

            public sealed class OriginalDependency : IDependency { }
            public sealed class ScopedDependency : IDependency { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    System.Collections.Generic.IEnumerable<IDependency> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IDependency, OriginalDependency>();
                    services.RemoveAll<IDependency>();
                    services.TryAddScoped<IDependency, ScopedDependency>();
                    {|#0:services.AddSingleton(typeof(IRepository<>), typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_RemovedClosedOptionsSnapshotUsesKnownScopedLifetime_ReportsDiagnostic()
    {
        var source = Usings +
                     "using Microsoft.Extensions.DependencyInjection.Extensions;\n\n" +
                     OptionsStubs + """
            public sealed class MyOptions { }

            public sealed class CustomSnapshot :
                Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions> { }

            public interface IRepository<T> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions> snapshot) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<
                        Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions>,
                        CustomSnapshot>();
                    services.RemoveAll<
                        Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions>>();
                    {|#0:services.AddSingleton(typeof(IRepository<>), typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IOptionsSnapshot"));
    }

    [Fact]
    public async Task OpenGenericSingleton_ReactivatedSingletonDependency_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IDependency { }

            public sealed class OriginalDependency : IDependency { }
            public sealed class SingletonDependency : IDependency { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(IDependency dependency) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IDependency, OriginalDependency>();
                    services.RemoveAll<IDependency>();
                    services.TryAddSingleton<IDependency, SingletonDependency>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_LaterSingletonOverridesReactivatedScopedDependency_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IDependency { }

            public sealed class OriginalDependency : IDependency { }
            public sealed class ScopedDependency : IDependency { }
            public sealed class FinalDependency : IDependency { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(IDependency dependency) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IDependency, OriginalDependency>();
                    services.RemoveAll<IDependency>();
                    services.TryAddScoped<IDependency, ScopedDependency>();
                    services.AddSingleton<IDependency, FinalDependency>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_ConditionallyRemovedByRemoveAll_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool removeRepository)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    {|#0:services.AddSingleton(typeof(IRepository<>), typeof(Repository<>))|};
                    if (removeRepository)
                    {
                        services.RemoveAll(typeof(IRepository<>));
                    }
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_RemovedInDifferentMethod_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                private readonly IServiceCollection _services;

                public Startup(IServiceCollection services)
                {
                    _services = services;
                }

                public void AddRepository()
                {
                    _services.AddScoped<IScopedService, ScopedService>();
                    {|#0:_services.AddSingleton(typeof(IRepository<>), typeof(Repository<>))|};
                }

                public void RemoveRepository()
                {
                    _services.RemoveAll(typeof(IRepository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_ReturnBeforeRemoveAll_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool keepRepository)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    {|#0:services.AddSingleton(typeof(IRepository<>), typeof(Repository<>))|};
                    if (keepRepository)
                    {
                        return;
                    }

                    services.RemoveAll(typeof(IRepository<>));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IScopedService"));
    }

    [Fact]
    public async Task OpenGenericSingleton_UnkeyedRegistrationWithKeyedReplace_ReportsDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    {|#0:services.AddSingleton(typeof(IRepository<>), typeof(Repository<>))|};
                    services.Replace(ServiceDescriptor.KeyedScoped(typeof(IRepository<>), "primary", typeof(Repository<>)));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80
                .AddPackages([
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0"),
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection", "8.0.0"),
                ]),
        };
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IScopedService"));

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_ReplacedByScopedRegistration_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IRepository<T> { }
            public interface IScopedService { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(IScopedService scoped) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IScopedService, ScopedService>();
                    services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
                    services.Replace(ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(Repository<>)));
                }
            }

            public class ScopedService : IScopedService { }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGenericSingleton_CapturesAddDbContextOptions_ReportsDiagnostic()
    {
        var source = Usings + """
            namespace Microsoft.EntityFrameworkCore
            {
                public sealed class DbContextOptions<TContext> { }
            }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class EntityFrameworkServiceCollectionExtensions
                {
                    public static IServiceCollection AddDbContext<TContext>(
                        this IServiceCollection services,
                        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
                        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
                        where TContext : class
                    {
                        return services;
                    }
                }
            }

            public sealed class AppDbContext { }

            public interface IRepository<T> { }

            public class Repository<T> : IRepository<T>
            {
                public Repository(Microsoft.EntityFrameworkCore.DbContextOptions<AppDbContext> options) { }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddDbContext<AppDbContext>();
                    {|#0:services.AddSingleton(typeof(IRepository<>), typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "DbContextOptions"));
    }

    [Fact]
    public async Task OpenGenericSingleton_NestedOpenServiceAndImplementation_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }

            public sealed class ContractOuter<TFirst>
            {
                public interface IDependency<TSecond> { }
            }

            public sealed class ImplementationOuter<TFirst>
            {
                public sealed class ScopedDependency<TSecond> :
                    ContractOuter<TFirst>.IDependency<TSecond>
                {
                }
            }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    IEnumerable<ContractOuter<string>.IDependency<int>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped(
                        typeof(ContractOuter<>.IDependency<>),
                        typeof(ImplementationOuter<>.ScopedDependency<>));
                    {|#0:services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>))|};
                }
            }
            """;

        await AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));
    }

    [Fact]
    public async Task OpenGenericSingleton_ExactKeySuppressesAnyKeyOpenGenericEnumerable_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ScopedDependency<T> : IDependency<T> { }
            public sealed class ClosedSingletonDependency : IDependency<string> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    [FromKeyedServices("primary")]
                    IEnumerable<IDependency<string>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped(
                        typeof(IDependency<>),
                        KeyedService.AnyKey,
                        typeof(ScopedDependency<>));
                    services.AddKeyedSingleton<
                        IDependency<string>,
                        ClosedSingletonDependency>("primary");
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80
                .AddPackages([
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0"),
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection", "8.0.0"),
                ]),
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_ExactKeySuppressesLaterAnyKeyOpenGenericEnumerable_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ScopedDependency<T> : IDependency<T> { }
            public sealed class ClosedSingletonDependency : IDependency<string> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    [FromKeyedServices("primary")]
                    IEnumerable<IDependency<string>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton<
                        IDependency<string>,
                        ClosedSingletonDependency>("primary");
                    services.AddKeyedScoped(
                        typeof(IDependency<>),
                        KeyedService.AnyKey,
                        typeof(ScopedDependency<>));
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80
                .AddPackages([
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0"),
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection", "8.0.0"),
                ]),
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_AnyKeyOnlyOpenGenericEnumerable_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ScopedDependency<T> : IDependency<T> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    [FromKeyedServices("primary")]
                    IEnumerable<IDependency<string>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped(
                        typeof(IDependency<>),
                        KeyedService.AnyKey,
                        typeof(ScopedDependency<>));
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80
                .AddPackages([
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0"),
                    new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.Extensions.DependencyInjection", "8.0.0"),
                ]),
        };
        await test.RunAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenGenericSingleton_ExactKeySuppressesAnyKeyForSingleService_NoDiagnostic(
        bool anyKeyRegisteredLast)
    {
        var registrations = anyKeyRegisteredLast
            ? """
                    services.AddKeyedSingleton<IDependency, SingletonDependency>("primary");
                    services.AddKeyedScoped<IDependency, ScopedDependency>(KeyedService.AnyKey);
              """
            : """
                    services.AddKeyedScoped<IDependency, ScopedDependency>(KeyedService.AnyKey);
                    services.AddKeyedSingleton<IDependency, SingletonDependency>("primary");
              """;
        var source = (Usings + """
            public interface IRepository<T> { }
            public interface IDependency { }

            public sealed class ScopedDependency : IDependency { }
            public sealed class SingletonDependency : IDependency { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    [FromKeyedServices("primary")]
                    IDependency dependency) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
            // registrations
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """).Replace("// registrations", registrations);

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies =
                AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                    .ReferenceAssembliesWithLatestDi,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericAnyKeySingleton_InheritedConcreteKeyEnumerable_ReportsDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ScopedDependency<T> : IDependency<T> { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    [FromKeyedServices]
                    IEnumerable<IDependency<T>> dependencies) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped(
                        typeof(IDependency<>),
                        "primary",
                        typeof(ScopedDependency<>));
                    {|#0:services.AddKeyedSingleton(
                        typeof(IRepository<>),
                        KeyedService.AnyKey,
                        typeof(Repository<>))|};
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies =
                AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                    .ReferenceAssembliesWithLatestDi,
        };
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IDependency"));

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericAnyKeySingleton_GreedyInheritedConcreteKeyConstructor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IScopedDependency { }

            public sealed class ScopedDependency : IScopedDependency { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository() { }

                public Repository(
                    [FromKeyedServices]
                    IScopedDependency dependency) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IScopedDependency, ScopedDependency>("primary");
                    {|#0:services.AddKeyedSingleton(
                        typeof(IRepository<>),
                        KeyedService.AnyKey,
                        typeof(Repository<>))|};
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies =
                AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                    .ReferenceAssembliesWithLatestDi,
        };
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IScopedDependency"));

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericAnyKeySingleton_MixedInheritedLifetimes_ReportsWorstLifetimeOnce()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IDependency { }

            public sealed class ScopedDependency : IDependency { }
            public sealed class TransientDependency : IDependency { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(
                    [FromKeyedServices]
                    IDependency dependency) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped<IDependency, ScopedDependency>("primary");
                    services.AddKeyedTransient<IDependency, TransientDependency>("secondary");
                    {|#0:services.AddKeyedSingleton(
                        typeof(IRepository<>),
                        KeyedService.AnyKey,
                        typeof(Repository<>))|};
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies =
                AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                    .ReferenceAssembliesWithLatestDi,
        };
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "transient", "IDependency"));

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericAnyKeySingleton_ExactOpenGenericRegistrationShadowsConcreteKey_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ScopedDependency<T> : IDependency<T> { }

            public sealed class AnyKeyRepository<T> : IRepository<T>
            {
                public AnyKeyRepository(
                    [FromKeyedServices]
                    IDependency<T> dependency) { }
            }

            public sealed class ExactRepository<T> : IRepository<T> { }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped(
                        typeof(IDependency<>),
                        "primary",
                        typeof(ScopedDependency<>));
                    services.AddKeyedSingleton(
                        typeof(IRepository<>),
                        KeyedService.AnyKey,
                        typeof(AnyKeyRepository<>));
                    services.AddKeyedSingleton(
                        typeof(IRepository<>),
                        "primary",
                        typeof(ExactRepository<>));
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies =
                AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                    .ReferenceAssembliesWithLatestDi,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericAnyKeySingleton_ShadowedConcreteKeyDoesNotLeakIntoUnknownEnumerable_NoDiagnostic()
    {
        var source = Usings + """
            using System.Collections.Generic;

            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ScopedDependency<T> : IDependency<T> { }

            public sealed class AnyKeyRepository<T> : IRepository<T>
            {
                public AnyKeyRepository(
                    [FromKeyedServices]
                    IEnumerable<IDependency<T>> dependencies) { }
            }

            public sealed class ExactRepository<T> : IRepository<T> { }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped(
                        typeof(IDependency<>),
                        "primary",
                        typeof(ScopedDependency<>));
                    services.AddKeyedSingleton(
                        typeof(IRepository<>),
                        KeyedService.AnyKey,
                        typeof(AnyKeyRepository<>));
                    services.AddKeyedSingleton(
                        typeof(IRepository<>),
                        "primary",
                        typeof(ExactRepository<>));
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies =
                AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                    .ReferenceAssembliesWithLatestDi,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericAnyKeySingleton_LaterAnyKeyRegistrationSuppressesEarlierCaptive_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface IDependency<T> { }

            public sealed class ScopedDependency<T> : IDependency<T> { }

            public sealed class CaptiveRepository<T> : IRepository<T>
            {
                public CaptiveRepository(
                    [FromKeyedServices]
                    IDependency<T> dependency) { }
            }

            public sealed class SafeRepository<T> : IRepository<T> { }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedScoped(
                        typeof(IDependency<>),
                        "primary",
                        typeof(ScopedDependency<>));
                    services.AddKeyedSingleton(
                        typeof(IRepository<>),
                        KeyedService.AnyKey,
                        typeof(CaptiveRepository<>));
                    services.AddKeyedSingleton(
                        typeof(IRepository<>),
                        KeyedService.AnyKey,
                        typeof(SafeRepository<>));
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies =
                AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                    .ReferenceAssembliesWithLatestDi,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericKeyedSingleton_BrokenExactDependencyDoesNotFallBackToAnyKey_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISelector { }
            public interface IMissing { }
            public interface IScopedDependency { }

            public sealed class BrokenSelector : ISelector
            {
                public BrokenSelector(IMissing missing) { }
            }

            public sealed class FallbackSelector : ISelector { }
            public sealed class ScopedDependency : IScopedDependency { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository() { }

                public Repository(
                    [FromKeyedServices] ISelector selector,
                    [FromKeyedServices] IScopedDependency scoped) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedTransient<ISelector, BrokenSelector>("primary");
                    services.AddKeyedSingleton<ISelector, FallbackSelector>(KeyedService.AnyKey);
                    services.AddKeyedScoped<IScopedDependency, ScopedDependency>("primary");
                    services.AddKeyedSingleton(
                        typeof(IRepository<>),
                        "primary",
                        typeof(Repository<>));
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies =
                AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                    .ReferenceAssembliesWithLatestDi,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_ClosedAnyKeyDependencyPrecedesExactKeyOpenGeneric_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISelector<T> { }
            public interface IMissing { }
            public interface IScopedDependency { }

            public sealed class ClosedSelector : ISelector<string> { }

            public sealed class BrokenSelector<T> : ISelector<T>
            {
                public BrokenSelector(IMissing missing) { }
            }

            public sealed class ScopedDependency : IScopedDependency { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository() { }

                public Repository(
                    [FromKeyedServices("primary")] ISelector<string> selector,
                    IScopedDependency scoped) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton<ISelector<string>, ClosedSelector>(
                        KeyedService.AnyKey);
                    services.AddKeyedTransient(
                        typeof(ISelector<>),
                        "primary",
                        typeof(BrokenSelector<>));
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    {|#0:services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>))|};
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies =
                AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                    .ReferenceAssembliesWithLatestDi,
        };
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.OpenGenericLifetimeMismatch)
                .WithLocation(0)
                .WithArguments("Repository", "scoped", "IScopedDependency"));

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_BrokenExactClosedDependencyDoesNotFallBackToOpenGeneric_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISelector<T> { }
            public interface IMissing { }
            public interface IScopedDependency { }

            public sealed class BrokenStringSelector : ISelector<string>
            {
                public BrokenStringSelector(IMissing missing) { }
            }

            public sealed class FallbackSelector<T> : ISelector<T> { }
            public sealed class ScopedDependency : IScopedDependency { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository() { }

                public Repository(
                    ISelector<string> selector,
                    IScopedDependency scoped) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<ISelector<string>, BrokenStringSelector>();
                    services.AddSingleton(typeof(ISelector<>), typeof(FallbackSelector<>));
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies =
                AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                    .ReferenceAssembliesWithLatestDi,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_LaterBrokenExactRegistrationSuppressesEarlierResolvable_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISelector { }
            public interface IMissing { }
            public interface IScopedDependency { }

            public sealed class FallbackSelector : ISelector { }

            public sealed class BrokenSelector : ISelector
            {
                public BrokenSelector(IMissing missing) { }
            }

            public sealed class ScopedDependency : IScopedDependency { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository() { }

                public Repository(
                    ISelector selector,
                    IScopedDependency scoped) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<ISelector, FallbackSelector>();
                    services.AddTransient<ISelector, BrokenSelector>();
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies =
                AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                    .ReferenceAssembliesWithLatestDi,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task OpenGenericSingleton_LaterBrokenAnyKeyRegistrationSuppressesEarlierResolvable_NoDiagnostic()
    {
        var source = Usings + """
            public interface IRepository<T> { }
            public interface ISelector { }
            public interface IMissing { }
            public interface IScopedDependency { }

            public sealed class FallbackSelector : ISelector { }

            public sealed class BrokenSelector : ISelector
            {
                public BrokenSelector(IMissing missing) { }
            }

            public sealed class ScopedDependency : IScopedDependency { }

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository() { }

                public Repository(
                    [FromKeyedServices("primary")] ISelector selector,
                    IScopedDependency scoped) { }
            }

            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton<ISelector, FallbackSelector>(
                        KeyedService.AnyKey);
                    services.AddKeyedTransient<ISelector, BrokenSelector>(
                        KeyedService.AnyKey);
                    services.AddScoped<IScopedDependency, ScopedDependency>();
                    services.AddSingleton(
                        typeof(IRepository<>),
                        typeof(Repository<>));
                }
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<DI009_OpenGenericLifetimeMismatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies =
                AnalyzerVerifier<DI009_OpenGenericLifetimeMismatchAnalyzer>
                    .ReferenceAssembliesWithLatestDi,
        };

        await test.RunAsync();
    }

    #endregion
}
