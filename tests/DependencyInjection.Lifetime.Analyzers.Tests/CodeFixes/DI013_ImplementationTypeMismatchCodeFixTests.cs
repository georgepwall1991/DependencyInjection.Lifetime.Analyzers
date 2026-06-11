using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

public class DI013_ImplementationTypeMismatchCodeFixTests
{
    private const string Usings = """
        using Microsoft.Extensions.DependencyInjection;

        """;

    [Fact]
    public async Task RemoveInvalidRegistration_ConditionalAccessTypeofRegistration_RemovesStatement()
    {
        var source = Usings + """
            public interface IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection? services)
                {
                    services?.AddSingleton(typeof(IService), typeof(WrongService));
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection? services)
                {
                }
            }
            """;

        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithSpan(9, 18, 9, 71)
                    .WithArguments("WrongService", "IService"),
                fixedSource,
                DI013_ImplementationTypeMismatchCodeFixProvider.RemoveInvalidRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task RemoveInvalidRegistration_TypeofRegistration_RemovesStatement()
    {
        var source = Usings + """
            public interface IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IService), typeof(WrongService))|};
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                }
            }
            """;

        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithLocation(0)
                    .WithArguments("WrongService", "IService"),
                fixedSource,
                DI013_ImplementationTypeMismatchCodeFixProvider.RemoveInvalidRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task RemoveInvalidRegistration_InstanceRegistration_RemovesStatement()
    {
        var source = Usings + """
            public interface IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IService), new WrongService())|};
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                }
            }
            """;

        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithLocation(0)
                    .WithArguments("WrongService", "IService"),
                fixedSource,
                DI013_ImplementationTypeMismatchCodeFixProvider.RemoveInvalidRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task RemoveInvalidRegistration_KeyedTypeofRegistration_RemovesStatement()
    {
        var source = Usings + """
            public interface IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddKeyedSingleton(typeof(IService), "key", typeof(WrongService))|};
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                }
            }
            """;

        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyCodeFixWithReferencesAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithLocation(0)
                    .WithArguments("WrongService", "IService"),
                fixedSource,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>.ReferenceAssembliesWithKeyedDi,
                DI013_ImplementationTypeMismatchCodeFixProvider.RemoveInvalidRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task RemoveInvalidRegistration_ServiceDescriptorRegistration_RemovesOuterStatement()
    {
        var source = Usings + """
            public interface IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.Add(ServiceDescriptor.Singleton(typeof(IService), typeof(WrongService)))|};
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                }
            }
            """;

        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithLocation(0)
                    .WithArguments("WrongService", "IService"),
                fixedSource,
                DI013_ImplementationTypeMismatchCodeFixProvider.RemoveInvalidRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task RemoveInvalidRegistration_TopLevelStatement_RemovesGlobalStatement()
    {
        var source = Usings + """
            var services = new ServiceCollection();
            {|#0:services.AddSingleton(typeof(IService), typeof(WrongService))|};

            public interface IService {}
            public sealed class WrongService {}
            """;

        var fixedSource = Usings + """
            var services = new ServiceCollection();

            public interface IService {}
            public sealed class WrongService {}
            """;

        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyCodeFixInConsoleApplicationAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithLocation(0)
                    .WithArguments("WrongService", "IService"),
                fixedSource,
                DI013_ImplementationTypeMismatchCodeFixProvider.RemoveInvalidRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task ReplaceImplementation_TypeofRegistration_RewritesImplementationTypeof()
    {
        var source = Usings + """
            public interface IService {}
            public sealed class SqlService : IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IService), typeof(WrongService))|};
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IService {}
            public sealed class SqlService : IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IService), typeof(global::SqlService));
                }
            }
            """;

        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithLocation(0)
                    .WithArguments("WrongService", "IService"),
                fixedSource,
                DI013_ImplementationTypeMismatchCodeFixProvider.ReplaceImplementationEquivalenceKeyPrefix + "SqlService");
    }

    [Fact]
    public async Task ChangeServiceType_TypeofRegistration_RewritesServiceTypeof()
    {
        var source = Usings + """
            public interface IService {}
            public interface IWrongService {}
            public sealed class WrongService : IWrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IService), typeof(WrongService))|};
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IService {}
            public interface IWrongService {}
            public sealed class WrongService : IWrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(global::IWrongService), typeof(WrongService));
                }
            }
            """;

        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithLocation(0)
                    .WithArguments("WrongService", "IService"),
                fixedSource,
                DI013_ImplementationTypeMismatchCodeFixProvider.ChangeServiceTypeEquivalenceKeyPrefix + "IWrongService");
    }

    [Fact]
    public async Task ChangeServiceType_InstanceRegistration_RewritesServiceTypeof()
    {
        var source = Usings + """
            public interface IService {}
            public interface IWrongService {}
            public sealed class WrongService : IWrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.AddSingleton(typeof(IService), new WrongService())|};
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IService {}
            public interface IWrongService {}
            public sealed class WrongService : IWrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton(typeof(global::IWrongService), new WrongService());
                }
            }
            """;

        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithLocation(0)
                    .WithArguments("WrongService", "IService"),
                fixedSource,
                DI013_ImplementationTypeMismatchCodeFixProvider.ChangeServiceTypeEquivalenceKeyPrefix + "IWrongService");
    }

    [Fact]
    public async Task EmbeddedInstanceRegistration_WithSafeServiceTypeRewrite_RewritesServiceTypeof()
    {
        var source = Usings + """
            public interface IService {}
            public interface IWrongService {}
            public sealed class WrongService : IWrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    if (enabled)
                        {|#0:services.AddSingleton(typeof(IService), new WrongService())|};
                }
            }
            """;

        var fixedSource = Usings + """
            public interface IService {}
            public interface IWrongService {}
            public sealed class WrongService : IWrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    if (enabled)
                        services.AddSingleton(typeof(global::IWrongService), new WrongService());
                }
            }
            """;

        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithLocation(0)
                    .WithArguments("WrongService", "IService"),
                fixedSource,
                DI013_ImplementationTypeMismatchCodeFixProvider.ChangeServiceTypeEquivalenceKeyPrefix + "IWrongService");
    }

    [Fact]
    public async Task RemoveInvalidRegistration_TryAddRegistration_RemovesStatement()
    {
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    {|#0:services.TryAddSingleton(typeof(IService), typeof(WrongService))|};
                }
            }
            """;

        var fixedSource = Usings + """
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                }
            }
            """;

        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithLocation(0)
                    .WithArguments("WrongService", "IService"),
                fixedSource,
                DI013_ImplementationTypeMismatchCodeFixProvider.RemoveInvalidRegistrationEquivalenceKey);
    }

    [Fact]
    public async Task NonStandaloneRegistration_WithNoSafeRewrite_OffersNoFix()
    {
        var source = Usings + """
            public interface IService {}

            public class Startup
            {
                public IServiceCollection ConfigureServices(IServiceCollection services)
                {
                    return {|#0:services.AddSingleton(typeof(IService), typeof(string))|};
                }
            }
            """;

        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyNoCodeFixOfferedAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithLocation(0)
                    .WithArguments("String", "IService"));
    }

    [Fact]
    public async Task EmbeddedInvalidRegistration_WithNoSafeRewrite_OffersNoFix()
    {
        var source = Usings + """
            public interface IService {}
            public sealed class WrongService {}

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services, bool enabled)
                {
                    if (enabled)
                        {|#0:services.AddSingleton(typeof(IService), typeof(WrongService))|};
                }
            }
            """;

        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyNoCodeFixOfferedAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithLocation(0)
                    .WithArguments("WrongService", "IService"));
    }

    [Fact]
    public async Task ReplaceImplementation_OnlyOpenGenericCandidate_OffersNoFix()
    {
        var source = Usings + """
            public interface IService {}
            public sealed class WrongService {}
            public class GenericService<T> : IService {}

            public class Startup
            {
                public IServiceCollection ConfigureServices(IServiceCollection services)
                {
                    return {|#0:services.AddSingleton(typeof(IService), typeof(WrongService))|};
                }
            }
            """;

        // A generic type definition cannot be rendered as a typeof argument
        // (typeof(GenericService<T>) does not compile with an undefined T), so it
        // must never be offered as an implementation candidate.
        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyNoCodeFixOfferedAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithLocation(0)
                    .WithArguments("WrongService", "IService"));
    }

    [Fact]
    public async Task ReplaceImplementation_OnlyStructCandidate_OffersNoFix()
    {
        var source = Usings + """
            public interface IService {}
            public sealed class WrongService {}
            public struct StructService : IService {}

            public class Startup
            {
                public IServiceCollection ConfigureServices(IServiceCollection services)
                {
                    return {|#0:services.AddSingleton(typeof(IService), typeof(WrongService))|};
                }
            }
            """;

        // A struct candidate compiles in the typeof form but crashes at container
        // resolution (no activatable constructor) and violates the class constraint
        // in the generic form, so it must never be offered.
        await CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
            .VerifyNoCodeFixOfferedAsync(
                source,
                CodeFixVerifier<DI013_ImplementationTypeMismatchAnalyzer, DI013_ImplementationTypeMismatchCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                    .WithLocation(0)
                    .WithArguments("WrongService", "IService"));
    }

    [Fact]
    public void FixAllProvider_IsDisabled()
    {
        var provider = new DI013_ImplementationTypeMismatchCodeFixProvider();
        Assert.Null(provider.GetFixAllProvider());
    }
}
