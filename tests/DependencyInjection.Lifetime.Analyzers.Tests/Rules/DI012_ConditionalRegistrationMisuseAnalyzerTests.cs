using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI012_ConditionalRegistrationMisuseAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.DependencyInjection.Extensions;

        """;

    private const string KeyedSupport = """
        namespace Microsoft.Extensions.DependencyInjection
        {
            public static class ServiceCollectionServiceExtensions
            {
                public static IServiceCollection AddKeyedSingleton<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                    where TService : class where TImplementation : class, TService => services;

                public static IServiceCollection TryAddKeyedSingleton<TService, TImplementation>(this IServiceCollection services, object? serviceKey)
                    where TService : class where TImplementation : class, TService => services;
            }
        }

        """;

    private const string EfCoreStubs = """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
            public class DbContextOptionsBuilder { }
        }

        namespace Microsoft.Extensions.DependencyInjection
        {
            public static class EntityFrameworkServiceCollectionExtensions
            {
                public static IServiceCollection AddDbContextFactory<TContext>(
                    this IServiceCollection services,
                    System.Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder>? optionsAction = null,
                    ServiceLifetime lifetime = ServiceLifetime.Singleton)
                    where TContext : Microsoft.EntityFrameworkCore.DbContext => services;

                public static IServiceCollection AddPooledDbContextFactory<TContext>(
                    this IServiceCollection services,
                    System.Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction,
                    int poolSize = 1024)
                    where TContext : Microsoft.EntityFrameworkCore.DbContext => services;
            }
        }

        """;

    #region Should Report Diagnostic - TryAdd After Add


    [Fact]
    public async Task AddAddThenReplace_StillReportsForLeftoverDuplicate()
    {
        // Replace removes only ONE matching descriptor: with two prior Adds, one survives and
        // the replacement is still another override of it.
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class ReplacementService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService1>();
                    services.AddScoped<IMyService, MyService2>();
                    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.Replace(
                        services,
                        ServiceDescriptor.Scoped<IMyService, ReplacementService>());
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(14, 9)
                .WithArguments("IMyService", "line 13"),
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(15, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task AddThenReplace_DoesNotReportDuplicate()
    {
        // Replace removes the earlier descriptor before adding its own — intentional override
        // semantics, not an accidental duplicate.
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }
            public class ReplacementService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.Replace(
                        services,
                        ServiceDescriptor.Scoped<IMyService, ReplacementService>());
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task PriorAddThenBranchHelperReplaceThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class BaseService : IMyService { }
            public class ReplacementService : IMyService { }
            public class FallbackService : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection ReplaceMyService(this IServiceCollection services)
                {
                    services.Replace(ServiceDescriptor.Singleton<IMyService, ReplacementService>());
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    services.AddSingleton<IMyService, BaseService>();
                    if (enabled)
                    {
                        services.ReplaceMyService();
                    }

                    services.TryAddSingleton<IMyService, FallbackService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(28, 9)
                .WithArguments("IMyService", "line 22"));
    }

    [Fact]
    public async Task GuardedHelperAddThenHelperReplaceThenTryAdd_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class PrimaryService : IMyService { }
            public class ReplacementService : IMyService { }
            public class FallbackService : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddMyService(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, PrimaryService>();
                    return services;
                }

                public static IServiceCollection ReplaceMyService(this IServiceCollection services)
                {
                    services.Replace(ServiceDescriptor.Singleton<IMyService, ReplacementService>());
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    if (enabled)
                    {
                        services.AddMyService();
                        services.ReplaceMyService();
                    }

                    services.TryAddSingleton<IMyService, FallbackService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task PriorAddThenBranchReplaceElseAdd_ReportsElseBranchDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class BaseService : IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    services.AddSingleton<IMyService, BaseService>();
                    if (usePrimary)
                    {
                        services.Replace(ServiceDescriptor.Singleton<IMyService, MyService1>());
                    }
                    else
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(20, 13)
                .WithArguments("IMyService", "line 13"));
    }

    [Fact]
    public async Task SplitComplementaryAddReplaceThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    if (!usePrimary)
                    {
                        services.Replace(ServiceDescriptor.Singleton<IMyService, MyService2>());
                    }

                    services.TryAddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(23, 9)
                .WithArguments("IMyService", "line 15"));
    }

    [Fact]
    public async Task SplitComplementaryAddTryAddThenFinalTryAdd_ReportsFinalIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    if (!usePrimary)
                    {
                        services.TryAddSingleton<IMyService, MyService2>();
                    }

                    services.TryAddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(23, 9)
                .WithArguments("IMyService", "line 15"));
    }

    [Fact]
    public async Task AddRemoveAllThenTryAdd_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                    services.RemoveAll<IMyService>();
                    services.TryAddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GuardedAddThenUnconditionalTryAddFallback_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GuardedAddThenTryAddFallbackThenComplementAdd_DoesNotReportDuplicateAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    services.TryAddSingleton<IMyService, MyService2>();

                    if (!usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService3>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task IfBranchAddElseBranchTryAddFallback_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        services.TryAddSingleton<IMyService, MyService2>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task IfBranchAddElseBranchRepeatedTryAddFallback_ReportsSecondIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        services.TryAddSingleton<IMyService, MyService2>();
                        services.TryAddSingleton<IMyService, MyService3>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(20, 13)
                .WithArguments("IMyService", "line 19"));
    }

    [Fact]
    public async Task GuardedAddFallbackComplementAddThenSecondTryAdd_ReportsSecondTryAddIgnored()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }
            public class MyService4 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    services.TryAddSingleton<IMyService, MyService2>();

                    if (!usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService3>();
                    }

                    services.TryAddSingleton<IMyService, MyService4>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(26, 9)
                .WithArguments("IMyService", "line 16"));
    }

    [Fact]
    public async Task ComplementarySiblingGuardsWithMutationInFirstBranch_ReportDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    if (enabled)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                        enabled = false;
                    }

                    if (!enabled)
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(20, 13)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task ComplementarySiblingLocalGuardWithLocalFunctionMutation_ReportDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    void Flip()
                    {
                        enabled = false;
                    }

                    if (enabled)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    Flip();

                    if (!enabled)
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(26, 13)
                .WithArguments("IMyService", "line 19"));
    }

    [Fact]
    public async Task ComplementarySiblingMemberGuardWithCallMutation_ReportDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                private bool _usePrimary = true;

                public void ConfigureServices(IServiceCollection services)
                {
                    if (_usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                        DisablePrimary();
                    }

                    if (!_usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }
                }

                private void DisablePrimary()
                {
                    _usePrimary = false;
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(22, 13)
                .WithArguments("IMyService", "line 16"));
    }

    [Fact]
    public async Task ComplementarySiblingBooleanComparisonGuards_DoNotReportDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    if (enabled == true)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    if (!enabled)
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ComplementarySiblingGuardsThenOriginalGuardAdd_ReportsDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    if (enabled)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    if (!enabled)
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }

                    if (enabled)
                    {
                        services.AddSingleton<IMyService, MyService3>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(25, 13)
                .WithArguments("IMyService", "line 20"));
    }

    [Fact]
    public async Task GuardedAddThenGuardedTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    if (usePrimary)
                    {
                        services.TryAddSingleton<IMyService, MyService2>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(19, 13)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenUnconditionalAddThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    services.AddSingleton<IMyService, MyService2>();
                    services.TryAddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(18, 9)
                .WithArguments("IMyService", "line 15"),
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(19, 9)
                .WithArguments("IMyService", "line 15"));
    }

    [Fact]
    public async Task NestedKnownTrueAddAndElseAddThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        if (true)
                        {
                            services.AddSingleton<IMyService, MyService1>();
                        }
                    }
                    else
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }

                    services.TryAddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(25, 9)
                .WithArguments("IMyService", "line 22"));
    }

    [Fact]
    public async Task NestedExhaustiveBranchAddsThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }
            public class MyService4 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary, bool useSecondary)
                {
                    if (usePrimary)
                    {
                        if (useSecondary)
                        {
                            services.AddSingleton<IMyService, MyService1>();
                        }
                        else
                        {
                            services.AddSingleton<IMyService, MyService2>();
                        }
                    }
                    else
                    {
                        services.AddSingleton<IMyService, MyService3>();
                    }

                    services.TryAddSingleton<IMyService, MyService4>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(30, 9)
                .WithArguments("IMyService", "line 27"));
    }

    [Fact]
    public async Task GuardedAddThenDominatingGuardedAddThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary, bool useSecondary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    if (useSecondary)
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }
                    else
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(20, 13)
                .WithArguments("IMyService", "line 15"),
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(27, 9)
                .WithArguments("IMyService", "line 15"));
    }

    [Fact]
    public async Task MultipleGuardedAddsThenUnconditionalTryAddFallback_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary, bool useSecondary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    if (useSecondary)
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }

                    services.TryAddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(20, 13)
                .WithArguments("IMyService", "line 15"));
    }

    [Fact]
    public async Task GuardedAddThenElseReturnThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(21, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenElseThrowExpressionThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        _ = true ? throw new InvalidOperationException() : 0;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(21, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenComplementGuardReturnThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    if (!usePrimary)
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(22, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenNestedExitingElseThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary, bool failFast)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        if (failFast)
                        {
                            return;
                        }
                        else
                        {
                            throw new InvalidOperationException();
                        }
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(28, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenElseGotoPastTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        goto Done;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                Done:
                    return;
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(21, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenElseGotoPastTryAdd_IgnoresSameLabelInEarlierMethod()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void Other()
                {
                Done:
                    return;
                }

                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        goto Done;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                Done:
                    return;
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(27, 9)
                .WithArguments("IMyService", "line 20"));
    }

    [Fact]
    public async Task GuardedAddThenElseGotoBeforeTryAdd_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        goto Continue;
                    }

                Continue:
                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GuardedAddThenElseInfiniteLoopGotoBeforeTryAdd_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        while (true)
                        {
                            goto Continue;
                        }
                    }

                Continue:
                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GuardedAddThenElseGotoReturningLabelBeforeTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        goto Done;
                    }

                Done:
                    return;

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(24, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenElseGotoLabelThenReturnBeforeTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        goto Done;
                    }

                Done:
                    ;
                    return;

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(25, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenElseTryFinallyReturnThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        try
                        {
                            return;
                        }
                        finally
                        {
                        }
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(27, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenBooleanComparisonComplementExitThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    if (usePrimary == false)
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(22, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenRelationalComplementExitThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, int mode)
                {
                    if (mode < 10)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    if (mode >= 10)
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(22, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenUnstableComplementExitThenTryAdd_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                private bool Next() => DateTime.UtcNow.Ticks % 2 == 0;

                public void ConfigureServices(IServiceCollection services)
                {
                    if (Next())
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    if (!Next())
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GuardedAddThenReassignedComplementExitThenTryAdd_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    usePrimary = !usePrimary;
                    if (!usePrimary)
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GuardedAddThenMemberReassignedComplementExitThenTryAdd_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                private bool _usePrimary;

                public void ConfigureServices(IServiceCollection services)
                {
                    if (_usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    this._usePrimary = true;
                    if (!_usePrimary)
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ComplementarySiblingFactoryMutation_DoesNotReportDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    if (enabled)
                    {
                        services.AddSingleton<IMyService>(_ =>
                        {
                            enabled = false;
                            return new MyService1();
                        });
                    }

                    if (!enabled)
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GuardedAddThenUnrelatedMemberReassignedComplementExitThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public bool usePrimary;

                public void ConfigureServices(IServiceCollection services, bool usePrimary, Startup other)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    other.usePrimary = true;
                    if (!usePrimary)
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(25, 9)
                .WithArguments("IMyService", "line 16"));
    }

    [Fact]
    public async Task GuardedAddThenRefMutatedComplementExitThenTryAdd_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    Mutate(ref usePrimary);
                    if (!usePrimary)
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }

                private static void Mutate(ref bool value)
                {
                    value = true;
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GuardedAddThenElseInfiniteLoopThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        while (true)
                        {
                        }
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(23, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenElseNestedConstTrueExitThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        const bool shouldExit = true;
                        if (shouldExit)
                        {
                            return;
                        }
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(25, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task OuterConstTrueGuardedAddInNestedBlockThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    const bool enabled = true;
                    {
                        if (enabled)
                        {
                            services.AddSingleton<IMyService, MyService1>();
                        }
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(20, 9)
                .WithArguments("IMyService", "line 16"));
    }

    [Fact]
    public async Task GuardedAddThenElseReturnWithTrailingLocalFunctionThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        return;
                        void Local() { }
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(22, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenElseReturnWithUnreachableStatementThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        return;
                        Console.WriteLine("unreachable");
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(22, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenElseIfTrueExitThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else if (true)
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(21, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenElseExhaustiveSwitchExitThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary, int mode)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        switch (mode)
                        {
                            case 0:
                                return;
                            default:
                                throw new InvalidOperationException();
                        }
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(27, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenElseSwitchReturnWithUnreachableBreakThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary, int mode)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        switch (mode)
                        {
                            case 0:
                                return;
                                break;
                            default:
                                throw new InvalidOperationException();
                        }
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(28, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenElseExhaustiveSwitchExpressionExitThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary, int mode)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        object value;
                        value = mode switch
                        {
                            0 => throw new InvalidOperationException(),
                            _ => throw new InvalidOperationException()
                        };
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(26, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddFallbackThenSecondTryAdd_ReportsSecondTryAddIgnored()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                    services.TryAddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(19, 9)
                .WithArguments("IMyService", "line 15"));
    }

    [Fact]
    public async Task ElseIfAddWithRegisteredThenAndExitingElseThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, int mode)
                {
                    if (mode == 1)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else if (mode == 2)
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }
                    else
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(26, 9)
                .WithArguments("IMyService", "line 19"));
    }

    [Fact]
    public async Task ElseIfOnlyReachablePathRegisteredThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, int mode)
                {
                    if (mode == 1)
                    {
                        return;
                    }
                    else if (mode == 2)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(25, 9)
                .WithArguments("IMyService", "line 18"));
    }

    [Fact]
    public async Task ElseIfComplementExitsWithoutFinalElseThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    if (enabled)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else if (!enabled)
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(21, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task ElseIfUnstableComplementAddsWithoutFinalElseThenTryAdd_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                private bool Next() => DateTime.UtcNow.Ticks % 2 == 0;

                public void ConfigureServices(IServiceCollection services)
                {
                    if (Next())
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else if (!Next())
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }

                    services.TryAddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ElseIfLaterComplementAddsThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }
            public class MyService4 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary, bool useSecondary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else if (useSecondary)
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }
                    else if (!useSecondary)
                    {
                        services.AddSingleton<IMyService, MyService3>();
                    }

                    services.TryAddSingleton<IMyService, MyService4>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(27, 9)
                .WithArguments("IMyService", "line 24"));
    }

    [Fact]
    public async Task ConstantTrueGuardedAddThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    if (true)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(17, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task ConstTrueGuardedAddThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    const bool enabled = true;
                    if (enabled)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(18, 9)
                .WithArguments("IMyService", "line 15"));
    }

    [Fact]
    public async Task ElseIfBinaryComplementExitsWithoutFinalElseThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, int mode)
                {
                    if (mode == 1)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else if (mode != 1)
                    {
                        return;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(21, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task LoopedGuardedAddThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool[] flags)
                {
                    foreach (var flag in flags)
                    {
                        if (flag)
                        {
                            services.AddSingleton<IMyService, MyService1>();
                        }

                        services.TryAddSingleton<IMyService, MyService2>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(19, 13)
                .WithArguments("IMyService", "line 16"));
    }

    [Fact]
    public async Task GuardedAddThenLoopedTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary, bool[] flags)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    foreach (var flag in flags)
                    {
                        services.TryAddSingleton<IMyService, MyService2>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(19, 13)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task DuplicateBranchThenOppositeBranchThenReplace_ReportsLeftoverDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }
            public class ReplacementService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                        services.AddSingleton<IMyService, MyService2>();
                    }
                    else
                    {
                        services.AddSingleton<IMyService, MyService3>();
                    }

                    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.Replace(
                        services,
                        ServiceDescriptor.Singleton<IMyService, ReplacementService>());
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(17, 13)
                .WithArguments("IMyService", "line 16"),
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(24, 9)
                .WithArguments("IMyService", "line 21"));
    }

    [Fact]
    public async Task MutuallyExclusiveIfElseAdds_DoNotReportDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NestedAddInsideOuterMutuallyExclusiveIfElse_DoNotReportDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary, bool enabled)
                {
                    if (usePrimary)
                    {
                        if (enabled)
                        {
                            services.AddSingleton<IMyService, MyService1>();
                        }
                    }
                    else
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MutuallyExclusiveIfElseIfAdds_DoNotReportDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, int mode)
                {
                    if (mode == 1)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else if (mode == 2)
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MutuallyExclusiveIfElseIfElseAdds_DoNotReportDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, int mode)
                {
                    if (mode == 1)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else if (mode == 2)
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }
                    else
                    {
                        services.AddSingleton<IMyService, MyService3>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ExhaustiveIfElseWithMiddleElseIfGapThenTryAdd_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, int mode)
                {
                    if (mode == 1)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else if (mode == 2)
                    {
                    }
                    else
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }

                    services.TryAddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task LoopedIfElseAdds_ReportDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool[] flags)
                {
                    foreach (var usePrimary in flags)
                    {
                        if (usePrimary)
                        {
                            services.AddSingleton<IMyService, MyService1>();
                        }
                        else
                        {
                            services.AddSingleton<IMyService, MyService2>();
                        }
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(20, 17)
                .WithArguments("IMyService", "line 16"));
    }

    [Fact]
    public async Task TryAddSingleton_AfterAddSingleton_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                    services.TryAddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(12, 9)
                .WithArguments("IMyService", "line 11"));
    }

    [Fact]
    public async Task TryAddAfterExhaustiveIfElseAdds_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }

                    services.TryAddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(22, 9)
                .WithArguments("IMyService", "line 19"));
    }

    [Fact]
    public async Task TryAddScoped_AfterAddScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService>();
                    services.TryAddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(12, 9)
                .WithArguments("IMyService", "line 11"));
    }

    [Fact]
    public async Task TryAddTransient_AfterAddTransient_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddTransient<IMyService, MyService>();
                    services.TryAddTransient<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(12, 9)
                .WithArguments("IMyService", "line 11"));
    }

    [Fact]
    public async Task TryAddScoped_AfterAddSingleton_SameServiceType_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                    services.TryAddScoped<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(12, 9)
                .WithArguments("IMyService", "line 11"));
    }

    [Fact]
    public async Task TryAddSingleton_AfterAddSingleton_FactoryShape_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(_ => new MyService1());
                    services.TryAddSingleton<IMyService>(_ => new MyService2());
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(13, 9)
                .WithArguments("IMyService", "line 12"));
    }

    [Fact]
    public async Task TryAddSingleton_AfterAddSingleton_InstanceShape_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService>(new MyService1());
                    services.TryAddSingleton<IMyService>(new MyService2());
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(13, 9)
                .WithArguments("IMyService", "line 12"));
    }

    [Fact]
    public async Task TryAddServiceDescriptor_AfterAddServiceDescriptor_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.Add(new ServiceDescriptor(typeof(IMyService), typeof(MyService1), ServiceLifetime.Singleton));
                    services.TryAdd(new ServiceDescriptor(typeof(IMyService), typeof(MyService2), ServiceLifetime.Singleton));
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(13, 9)
                .WithArguments("IMyService", "line 12"));
    }

    [Fact]
    public async Task TryAddSingleton_InInvokedWrapperAfterDirectAdd_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddFallback(this IServiceCollection services)
                {
                    services.TryAddSingleton<IMyService, MyService2>();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    services.AddFallback();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(12, 9)
                .WithArguments("IMyService", "line 21"));
    }

    [Fact]
    public async Task TryAddSingleton_AfterInvokedWrapperAdd_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddPrimary(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddPrimary();
                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(22, 9)
                .WithArguments("IMyService", "line 12"));
    }

    [Fact]
    public async Task TryAddSingleton_AfterAddSingleton_ThroughLocalAlias_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    var alias = services;
                    services.AddSingleton<IMyService, MyService>();
                    alias.TryAddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(13, 9)
                .WithArguments("IMyService", "line 12"));
    }

    [Fact]
    public async Task TryAddSingleton_AfterInvokedHelperMethodAdd_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    AddPrimary(services);
                    services.TryAddSingleton<IMyService, MyService2>();
                }

                private static void AddPrimary(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(13, 9)
                .WithArguments("IMyService", "line 18"));
    }

    [Fact]
    public async Task TryAddSingleton_InInvokedHelperInsideGuardedAddBranch_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddFallback(this IServiceCollection services)
                {
                    services.TryAddSingleton<IMyService, MyService2>();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                        services.AddFallback();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(12, 9)
                .WithArguments("IMyService", "line 23"));
    }

    [Fact]
    public async Task TryAddSingleton_InInvokedHelperAfterCallerBranchExit_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddFallback(this IServiceCollection services)
                {
                    services.TryAddSingleton<IMyService, MyService2>();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        return;
                    }

                    services.AddFallback();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(12, 9)
                .WithArguments("IMyService", "line 23"));
    }

    [Fact]
    public async Task TryAddSingleton_AfterInvokedLocalFunctionAdd_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    AddPrimary(services);
                    services.TryAddSingleton<IMyService, MyService2>();
                    return;

                    static void AddPrimary(IServiceCollection services)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(13, 9)
                .WithArguments("IMyService", "line 18"));
    }

    #endregion

    #region Should Report Diagnostic - Duplicate Add

    [Fact]
    public async Task DuplicateAddSingleton_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    services.AddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(13, 9)
                .WithArguments("IMyService", "line 12"));
    }

    [Fact]
    public async Task DuplicateAddScoped_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, MyService1>();
                    services.AddScoped<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(13, 9)
                .WithArguments("IMyService", "line 12"));
    }

    [Fact]
    public async Task TripleAddSingleton_ReportsMultipleDiagnostics()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    services.AddSingleton<IMyService, MyService2>();
                    services.AddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(14, 9)
                .WithArguments("IMyService", "line 13"),
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(15, 9)
                .WithArguments("IMyService", "line 13"));
    }

    [Fact]
    public async Task DuplicateAddSingleton_SplitAcrossDirectCallAndInvokedWrapper_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddFallback(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService2>();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    services.AddFallback();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(12, 9)
                .WithArguments("IMyService", "line 21"));
    }

    [Fact]
    public async Task DuplicateAddSingleton_RepeatedInvokedBranchWrapper_ReportsDiagnostics()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddBranch(this IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }

                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary, bool useSecondary)
                {
                    services.AddBranch(usePrimary);
                    services.AddBranch(useSecondary);
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(14, 13)
                .WithArguments("IMyService", "line 18"),
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(18, 13)
                .WithArguments("IMyService", "line 18"));
    }

    [Fact]
    public async Task DuplicateAddSingleton_RepeatedInvokedNestedBranchWrapper_ReportsDiagnostics()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddBranch(this IServiceCollection services, bool usePrimary, bool enabled)
                {
                    if (usePrimary)
                    {
                        if (enabled)
                        {
                            services.AddSingleton<IMyService, MyService1>();
                        }
                    }
                    else
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }

                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary, bool useSecondary, bool enabled)
                {
                    services.AddBranch(usePrimary, enabled);
                    services.AddBranch(useSecondary, enabled);
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(16, 17)
                .WithArguments("IMyService", "line 21"),
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(21, 13)
                .WithArguments("IMyService", "line 21"));
    }

    [Fact]
    public async Task DuplicateAddSingleton_LoopedInvokedBranchWrapper_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddBranch(this IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }

                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool[] flags)
                {
                    foreach (var flag in flags)
                    {
                        services.AddBranch(flag);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(18, 13)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task DuplicateAddSingleton_LoopedInvokedWrapperThenDirectAdd_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddPrimary(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool[] flags)
                {
                    foreach (var flag in flags)
                    {
                        services.AddPrimary();
                    }

                    services.AddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(26, 9)
                .WithArguments("IMyService", "line 12"));
    }

    [Fact]
    public async Task DuplicateAddSingleton_NestedLoopedInvokedBranchWrapper_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddBranch(this IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }

                    return services;
                }

                public static IServiceCollection AddAll(this IServiceCollection services, bool[] flags)
                {
                    foreach (var flag in flags)
                    {
                        services.AddBranch(flag);
                    }

                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool[] flags)
                {
                    services.AddAll(flags);
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(18, 13)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task GuardedAddThenElseUsingReturnThenTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary, IDisposable gate)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        using (gate)
                        {
                            return;
                        }
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(24, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task TryAddInsideInvokedHelperAfterReturningBranch_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddMaybe(this IServiceCollection services, bool enabled)
                {
                    if (enabled)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        return services;
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    services.AddMaybe(enabled);
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(21, 9)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task TryAddInsideLoopedInvokedHelperComplementaryGuard_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddEither(this IServiceCollection services, bool flag)
                {
                    if (flag)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }

                    if (!flag)
                    {
                        services.TryAddSingleton<IMyService, MyService2>();
                    }

                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool[] flags)
                {
                    foreach (var flag in flags)
                    {
                        services.AddEither(flag);
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(19, 13)
                .WithArguments("IMyService", "line 14"));
    }

    [Fact]
    public async Task IfElseInvokedHelperAddsThenCallerTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddPrimary(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    return services;
                }

                public static IServiceCollection AddSecondary(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService2>();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddPrimary();
                    }
                    else
                    {
                        services.AddSecondary();
                    }

                    services.TryAddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(37, 9)
                .WithArguments("IMyService", "line 19"));
    }

    [Fact]
    public async Task ComplementarySiblingInvokedHelperAddsThenCallerTryAdd_ReportsIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddPrimary(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    return services;
                }

                public static IServiceCollection AddSecondary(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService2>();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    if (enabled)
                    {
                        services.AddPrimary();
                    }

                    if (!enabled)
                    {
                        services.AddSecondary();
                    }

                    services.TryAddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.TryAddIgnored)
                .WithLocation(38, 9)
                .WithArguments("IMyService", "line 13"));
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task TryAddSingleton_BeforeAddSingleton_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddSingleton<IMyService, MyService>();
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        // TryAdd before Add is valid - TryAdd registers first, then Add would override
        // but we don't report TryAdd in this case since it wasn't ignored
        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TryAddSingleton_InInvokedWrapperBeforeDirectAdd_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddFallback(this IServiceCollection services)
                {
                    services.TryAddSingleton<IMyService, MyService1>();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddFallback();
                    services.AddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InvokedHelperAddsFromOppositeBranches_DoNotReportDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddPrimary(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    return services;
                }

                public static IServiceCollection AddSecondary(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService2>();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddPrimary();
                    }
                    else
                    {
                        services.AddSecondary();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InvokedNestedHelperAddsFromOppositeWrapperBranches_DoNotReportDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddPrimary(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    return services;
                }

                public static IServiceCollection AddSecondary(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService2>();
                    return services;
                }

                public static IServiceCollection AddEither(this IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddPrimary();
                    }
                    else
                    {
                        services.AddSecondary();
                    }

                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    services.AddEither(usePrimary);
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InvokedNestedHelperPreservesOuterWrapperBranch_DoesNotReportDuplicate()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddPrimary(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    return services;
                }

                public static IServiceCollection AddMiddle(this IServiceCollection services)
                {
                    services.AddPrimary();
                    return services;
                }

                public static IServiceCollection AddSecondary(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService2>();
                    return services;
                }

                public static IServiceCollection AddOuter(this IServiceCollection services, bool enabled)
                {
                    if (enabled)
                    {
                        services.AddMiddle();
                    }
                    else
                    {
                        services.AddSecondary();
                    }

                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    services.AddOuter(enabled);
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GuardedNestedInvokedHelperAddThenCallerTryAddFallback_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddPrimary(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    return services;
                }

                public static IServiceCollection AddOuter(this IServiceCollection services)
                {
                    services.AddPrimary();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    if (enabled)
                    {
                        services.AddOuter();
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GuardedInvokedExhaustiveHelperThenCallerTryAddFallback_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public class MyService3 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddEither(this IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        services.AddSingleton<IMyService, MyService2>();
                    }

                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled, bool usePrimary)
                {
                    if (enabled)
                    {
                        services.AddEither(usePrimary);
                    }

                    services.TryAddSingleton<IMyService, MyService3>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GuardedInvokedHelperAddThenCallerTryAddFallback_DoesNotReportIgnoredTryAdd()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddPrimary(this IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    if (enabled)
                    {
                        services.AddPrimary();
                    }

                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GuardedAddInInvokedWrapperReturnFallbackThenCallerTryAdd_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddMaybe(this IServiceCollection services, bool usePrimary)
                {
                    if (usePrimary)
                    {
                        services.AddSingleton<IMyService, MyService1>();
                    }
                    else
                    {
                        return services;
                    }

                    return services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool usePrimary)
                {
                    services.AddMaybe(usePrimary);
                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingleAddSingleton_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SingleTryAddSingleton_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddSingleton<IMyService, MyService>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DifferentServiceTypes_NoDiagnostic()
    {
        var source = Usings + """
            public interface IService1 { }
            public interface IService2 { }
            public class Service1 : IService1 { }
            public class Service2 : IService2 { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IService1, Service1>();
                    services.AddSingleton<IService2, Service2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task MultipleTryAdd_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.TryAddSingleton<IMyService, MyService1>();
                    services.TryAddSingleton<IMyService, MyService2>();
                }
            }
            """;

        // Multiple TryAdd calls are fine - only the first takes effect
        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DuplicateRegistrations_OnDifferentServiceCollectionInstances_NoDiagnostic()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices()
                {
                    IServiceCollection primary = new ServiceCollection();
                    IServiceCollection fallback = new ServiceCollection();

                    primary.AddSingleton<IMyService, MyService1>();
                    fallback.AddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DuplicateRegistrations_InDifferentMethods_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigurePrimary(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                }

                public void ConfigureFallback(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService2>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ExplicitContextBeforeAddDbContextFactory_NoDuplicateDiagnostic()
    {
        var source = Usings + EfCoreStubs + """
            public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<MyDbContext>();
                    services.AddDbContextFactory<MyDbContext>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ExplicitContextBeforeAddPooledDbContextFactory_NoDuplicateDiagnostic()
    {
        var source = Usings + EfCoreStubs + """
            public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<MyDbContext>();
                    services.AddPooledDbContextFactory<MyDbContext>(_ => { });
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task KeyedRegistrations_WithDifferentKeys_NoDiagnostic()
    {
        var source = Usings + KeyedSupport + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton<IMyService, MyService1>("A");
                    services.AddKeyedSingleton<IMyService, MyService2>("B");
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task KeyedRegistrations_DuplicateSameKey_ReportsDiagnostic()
    {
        var source = Usings + KeyedSupport + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton<IMyService, MyService1>("A");
                    services.AddKeyedSingleton<IMyService, MyService2>("A");
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(24, 9)
                .WithArguments("IMyService", "line 23"));
    }

    [Fact]
    public async Task KeyedNullAndUnkeyedRegistrations_DoNotCrossTrigger()
    {
        var source = Usings + KeyedSupport + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                    services.AddKeyedSingleton<IMyService, MyService2>(null);
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task KeyedNullRegistrations_DuplicateSameNullKey_ReportsDiagnostic()
    {
        var source = Usings + KeyedSupport + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddKeyedSingleton<IMyService, MyService1>(null);
                    services.AddKeyedSingleton<IMyService, MyService2>(null);
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithLocation(24, 9)
                .WithArguments("IMyService", "line 23"));
    }

    [Fact]
    public async Task DuplicateRegistrations_AcrossFiles_UsesStableSourceOrdering()
    {
        var sharedTypes = """
            using Microsoft.Extensions.DependencyInjection;

            public interface IMyService { }
            public class ServiceA : IMyService { }
            public class ServiceB : IMyService { }

            public static class ServiceCollectionHolder
            {
                public static IServiceCollection Services { get; } = new ServiceCollection();
            }
            """;

        var laterAlphabetically = """
            using Microsoft.Extensions.DependencyInjection;

            public static class RegistrationB
            {
                public static void Configure()
                {
                    ServiceCollectionHolder.Services.AddSingleton<IMyService, ServiceB>();
                }
            }
            """;

        var earlierAlphabetically = """
            using Microsoft.Extensions.DependencyInjection;

            public static class RegistrationA
            {
                public static void Configure()
                {
                    ServiceCollectionHolder.Services.AddSingleton<IMyService, ServiceA>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyDiagnosticsAsync(
            [
                ("Common.cs", sharedTypes),
                ("B.cs", laterAlphabetically),
                ("A.cs", earlierAlphabetically)
            ],
            AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>
                .Diagnostic(DiagnosticDescriptors.DuplicateRegistration)
                .WithSpan("B.cs", 7, 9, 7, 78)
                .WithArguments("IMyService", "line 7"));
    }

    [Fact]
    public async Task TryAddSingleton_AfterOpaqueHelperBarrier_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService { }
            public class MyService1 : IMyService { }
            public class MyService2 : IMyService { }
            public interface IRegistrar
            {
                void Configure(IServiceCollection services);
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, IRegistrar registrar)
                {
                    AddPrimary(services);
                    registrar.Configure(services);
                    services.TryAddSingleton<IMyService, MyService2>();
                }

                private static void AddPrimary(IServiceCollection services)
                {
                    services.AddSingleton<IMyService, MyService1>();
                }
            }
            """;

        await AnalyzerVerifier<DI012_ConditionalRegistrationMisuseAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
