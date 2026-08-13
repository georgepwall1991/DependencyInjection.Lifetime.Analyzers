using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI038_FrameworkActivatedDependencyAnalyzerTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.AspNetCore.Mvc.RazorPages;

        namespace Microsoft.AspNetCore.Mvc
        {
            public abstract class ControllerBase { }

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class FromServicesAttribute : Attribute
            {
            }
        }

        namespace Microsoft.AspNetCore.Mvc.RazorPages
        {
            public abstract class PageModel { }
        }

        namespace Microsoft.Extensions.DependencyInjection
        {
            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class FromKeyedServicesAttribute : Attribute
            {
                public FromKeyedServicesAttribute() { }

                public FromKeyedServicesAttribute(object? key) { }
            }
        }

        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger<T> { }
        }

        public static class MvcServiceCollectionExtensions
        {
            public static IServiceCollection AddControllers(this IServiceCollection services) => services;
        }

        public interface IOrderService { }

        public sealed class OrderService : IOrderService { }

        public interface IInvoiceService { }

        public sealed class InvoiceService : IInvoiceService { }

        public interface ISearchService { }

        """;

    [Fact]
    public async Task ControllerConstructorMissingDependency_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController({|DI038:IOrderService orders|}) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ControllerConstructorRegisteredDependency_DoesNotReport()
    {
        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                        services.AddScoped<IOrderService, OrderService>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ControllerPrimaryConstructorMissingDependency_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class OrdersController({|DI038:IOrderService orders|}) : ControllerBase
                {
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task PageModelConstructorMissingDependency_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class OrdersPage : PageModel
                {
                    public OrdersPage({|DI038:IOrderService orders|}) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task FromServicesMissingDependency_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class SearchController : ControllerBase
                {
                    public object Get([FromServices] {|DI038:ISearchService search|}) => search;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task FromServicesRegisteredDependency_DoesNotReport()
    {
        var source =
            Usings
            + """
                public sealed class SearchController : ControllerBase
                {
                    public object Get([FromServices] IOrderService orders) => orders;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                        services.AddScoped<IOrderService, OrderService>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task FromKeyedServicesMissingDependency_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class BillingController : ControllerBase
                {
                    public object Get([FromKeyedServices("stripe")] {|DI038:IInvoiceService invoices|}) => invoices;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ControllerLoggerDependency_DoesNotReport()
    {
        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(Microsoft.Extensions.Logging.ILogger<OrdersController> logger) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task OpaqueApplicationServicesWrapper_DoesNotReport()
    {
        const string externalSource = """
            using Microsoft.Extensions.DependencyInjection;

            namespace MyCompany.DependencyInjection;

            public static class ApplicationServiceCollectionExtensions
            {
                public static IServiceCollection AddApplicationServices(this IServiceCollection services) => services;
            }
            """;

        var source =
            Usings
            + """
                using MyCompany.DependencyInjection;

                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddApplicationServices();
                        services.AddControllers();
                    }
                }
                """;

        var diagnostics = await GetDiagnosticsWithExternalReferenceAsync(source, externalSource);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == DiagnosticDescriptors.FrameworkActivatedDependency.Id
        );
    }

    [Fact]
    public async Task ClassLibraryControllerWithoutServiceCollectionUsage_DoesNotReport()
    {
        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task RegisteredControllerIsOwnedByDI015_DoesNotReport()
    {
        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                        services.AddScoped<OrdersController>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task NonControllerConstructor_DoesNotReport()
    {
        var source =
            Usings
            + """
                public sealed class OrdersWorker
                {
                    public OrdersWorker(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task AbstractController_DoesNotReport()
    {
        var source =
            Usings
            + """
                public abstract class OrdersControllerBase : ControllerBase
                {
                    public OrdersControllerBase(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task NameSuffixAloneIsNotAController_DoesNotReport()
    {
        var source =
            Usings
            + """
                public sealed class OrdersController
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task UninvokedWrapperRegistration_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public static class FeatureExtensions
                {
                    public static IServiceCollection AddFeatureDependencies(this IServiceCollection services)
                    {
                        services.AddScoped<IOrderService, OrderService>();
                        return services;
                    }
                }

                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController({|DI038:IOrderService orders|}) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task MicrosoftNamespaceThirdPartyWrapper_DoesNotReport()
    {
        const string externalSource = """
            using Microsoft.Extensions.DependencyInjection;

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class FeatureRegistrationExtensions
                {
                    public static IServiceCollection AddFeatureServices(this IServiceCollection services) => services;
                }
            }
            """;

        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddFeatureServices();
                        services.AddControllers();
                    }
                }
                """;

        var diagnostics = await GetDiagnosticsWithExternalReferenceAsync(source, externalSource);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == DiagnosticDescriptors.FrameworkActivatedDependency.Id
        );
    }

    [Fact]
    public async Task FromServicesOnOrdinaryHelper_DoesNotReport()
    {
        var source =
            Usings
            + """
                public sealed class Helper
                {
                    public void Run([FromServices] IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task FromServicesOnNonAction_DoesNotReport()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc
                {
                    [AttributeUsage(AttributeTargets.Method)]
                    public sealed class NonActionAttribute : Attribute
                    {
                    }
                }

                public sealed class OrdersController : ControllerBase
                {
                    [NonAction]
                    public void Helper([FromServices] IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task InternalController_DoesNotReport()
    {
        var source =
            Usings
            + """
                internal sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task NonControllerAttribute_DoesNotReport()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc
                {
                    [AttributeUsage(AttributeTargets.Class)]
                    public sealed class NonControllerAttribute : Attribute
                    {
                    }
                }

                [NonController]
                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task InheritedFromServicesAction_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public abstract class OrdersControllerBase : ControllerBase
                {
                    public object Get([FromServices] {|DI038:ISearchService search|}) => search;
                }

                public sealed class OrdersController : OrdersControllerBase
                {
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task NestedController_DoesNotReport()
    {
        var source =
            Usings
            + """
                public static class Outer
                {
                    public sealed class OrdersController : ControllerBase
                    {
                        public OrdersController(IOrderService orders) { }
                    }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task OpenGenericController_DoesNotReport()
    {
        var source =
            Usings
            + """
                public sealed class OrdersController<T> : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task InheritedNonController_DoesNotReport()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc
                {
                    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
                    public sealed class NonControllerAttribute : Attribute
                    {
                    }
                }

                [NonController]
                public abstract class HiddenControllerBase : ControllerBase
                {
                }

                public sealed class HiddenController : HiddenControllerBase
                {
                    public HiddenController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task PageModelHelper_DoesNotReport()
    {
        var source =
            Usings
            + """
                public sealed class OrdersPage : PageModel
                {
                    public object Helper([FromServices] ISearchService search) => search;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task PageModelOnGetFromServices_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class OrdersPage : PageModel
                {
                    public object OnGet([FromServices] {|DI038:ISearchService search|}) => search;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task GenericAction_DoesNotReport()
    {
        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public object Get<T>([FromServices] ISearchService search) => search;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task OptionalFromServices_DoesNotReport()
    {
        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public object Get([FromServices] ISearchService? search) => search;
                    public object Post([FromServices] ISearchService missing = null) => missing;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task PageModelNonHandler_DoesNotReport()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc.RazorPages
                {
                    [AttributeUsage(AttributeTargets.Method)]
                    public sealed class NonHandlerAttribute : Attribute
                    {
                    }
                }

                public sealed class OrdersPage : PageModel
                {
                    [NonHandler]
                    public object OnGet([FromServices] ISearchService search) => search;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task StaticThirdPartyRegistrationHelper_DoesNotReport()
    {
        const string externalSource = """
            using Microsoft.Extensions.DependencyInjection;

            namespace ThirdParty
            {
                public static class Feature
                {
                    public static IServiceCollection AddFeature(IServiceCollection services) => services;
                }
            }
            """;

        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        ThirdParty.Feature.AddFeature(services);
                        services.AddControllers();
                    }
                }
                """;

        var diagnostics = await GetDiagnosticsWithExternalReferenceAsync(source, externalSource);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == DiagnosticDescriptors.FrameworkActivatedDependency.Id
        );
    }

    [Fact]
    public async Task PageModelOnOptionsFromServices_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public sealed class OrdersPage : PageModel
                {
                    public object OnOptions([FromServices] {|DI038:ISearchService search|}) => search;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task MvcFrameworkConstructorDependency_DoesNotReport()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc.Routing
                {
                    public interface IUrlHelperFactory { }
                }

                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(Microsoft.AspNetCore.Mvc.Routing.IUrlHelperFactory urls) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task OverriddenNonActionHidesBaseFromServices_DoesNotReport()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc
                {
                    [AttributeUsage(AttributeTargets.Method, Inherited = true)]
                    public sealed class NonActionAttribute : Attribute
                    {
                    }
                }

                public abstract class OrdersControllerBase : ControllerBase
                {
                    public virtual object Get([FromServices] ISearchService search) => search;
                }

                public sealed class OrdersController : OrdersControllerBase
                {
                    [NonAction]
                    public override object Get([FromServices] ISearchService search) => search;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task OverrideWithoutFromServicesHidesBaseFromServices_DoesNotReport()
    {
        var source =
            Usings
            + """
                public abstract class OrdersControllerBase : ControllerBase
                {
                    public virtual object Get([FromServices] ISearchService search) => search;
                }

                public sealed class OrdersController : OrdersControllerBase
                {
                    public override object Get(ISearchService search) => search;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task MostDerivedOverrideFromServices_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public abstract class OrdersControllerBase : ControllerBase
                {
                    public virtual object Get([FromServices] ISearchService search) => search;
                }

                public sealed class OrdersController : OrdersControllerBase
                {
                    public override object Get([FromServices] {|DI038:ISearchService search|}) => search;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ObjectEqualsOverrideFromServices_DoesNotReport()
    {
        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public override bool Equals([FromServices] object other) => false;

                    public override int GetHashCode() => 0;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task HealthCheckServiceConstructor_DoesNotReport()
    {
        var source =
            Usings
            + """
                namespace Microsoft.Extensions.Diagnostics.HealthChecks
                {
                    public abstract class HealthCheckService { }
                }

                public static class HealthCheckServiceCollectionExtensions
                {
                    public static IServiceCollection AddHealthChecks(this IServiceCollection services) => services;
                }

                public sealed class HealthController : ControllerBase
                {
                    public HealthController(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService health) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddHealthChecks();
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task MemoryCacheStillReportsWhenUnregistered()
    {
        var source =
            Usings
            + """
                namespace Microsoft.Extensions.Caching.Memory
                {
                    public interface IMemoryCache { }
                }

                public sealed class CacheController : ControllerBase
                {
                    public CacheController({|DI038:Microsoft.Extensions.Caching.Memory.IMemoryCache cache|}) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task UnmodeledHealthChecksDoesNotHideUserService()
    {
        var source =
            Usings
            + """
                namespace Microsoft.Extensions.Diagnostics.HealthChecks
                {
                    public abstract class HealthCheckService { }
                }

                public static class HealthCheckServiceCollectionExtensions
                {
                    public static IServiceCollection AddHealthChecks(this IServiceCollection services) => services;
                }

                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController({|DI038:IOrderService orders|}) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddHealthChecks();
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task InheritedMetadataFromServicesAction_ReportsOnDerivedController()
    {
        const string externalSource = """
            using System;
            using Microsoft.AspNetCore.Mvc;

            namespace Microsoft.AspNetCore.Mvc
            {
                public abstract class ControllerBase { }

                [AttributeUsage(AttributeTargets.Parameter)]
                public sealed class FromServicesAttribute : Attribute
                {
                }
            }

            public interface ISearchService { }

            public abstract class SharedControllerBase : ControllerBase
            {
                public object Get([FromServices] ISearchService search) => search;
            }
            """;

        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            public sealed class OrdersController : SharedControllerBase
            {
            }

            public static class MvcServiceCollectionExtensions
            {
                public static IServiceCollection AddControllers(this IServiceCollection services) => services;
            }

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddControllers();
                }
            }
            """;

        var diagnostics = await GetDiagnosticsWithExternalReferenceAsync(source, externalSource);
        Assert.Contains(
            diagnostics,
            diagnostic =>
                diagnostic.Id == DiagnosticDescriptors.FrameworkActivatedDependency.Id
                && diagnostic.GetMessage().Contains("ISearchService")
        );
    }

    [Fact]
    public async Task UseServiceProviderFactory_DoesNotReport()
    {
        var source =
            Usings
            + """
                namespace Microsoft.Extensions.Hosting
                {
                    public interface IHostBuilder { }

                    public static class HostingHostBuilderExtensions
                    {
                        public static IHostBuilder UseServiceProviderFactory(
                            this IHostBuilder hostBuilder,
                            object factory
                        ) => hostBuilder;
                    }
                }

                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(
                        IServiceCollection services,
                        Microsoft.Extensions.Hosting.IHostBuilder host
                    )
                    {
                        services.AddControllers();
                        Microsoft.Extensions.Hosting.HostingHostBuilderExtensions.UseServiceProviderFactory(
                            host,
                            new object()
                        );
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ConfigureContainer_DoesNotReport()
    {
        var source =
            Usings
            + """
                namespace Microsoft.Extensions.Hosting
                {
                    public interface IHostBuilder { }

                    public static class HostingHostBuilderExtensions
                    {
                        public static IHostBuilder ConfigureContainer<TContainerBuilder>(
                            this IHostBuilder hostBuilder,
                            System.Action<TContainerBuilder> configure
                        ) => hostBuilder;
                    }
                }

                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(
                        IServiceCollection services,
                        Microsoft.Extensions.Hosting.IHostBuilder host
                    )
                    {
                        services.AddControllers();
                        Microsoft.Extensions.Hosting.HostingHostBuilderExtensions.ConfigureContainer<object>(
                            host,
                            _ => { }
                        );
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CustomControllerActivator_DoesNotReportConstructor()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc.Controllers
                {
                    public interface IControllerActivator { }
                }

                public sealed class CustomActivator : Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator { }

                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                        services.AddSingleton<Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator, CustomActivator>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CustomControllerActivator_StillReportsFromServices()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc.Controllers
                {
                    public interface IControllerActivator { }
                }

                public sealed class CustomActivator : Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator { }

                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }

                    public object Get([FromServices] {|DI038:ISearchService search|}) => search;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                        services.AddSingleton<Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator, CustomActivator>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task UnrelatedConfigureContainerName_StillReports()
    {
        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController({|DI038:IOrderService orders|}) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                        ConfigureContainer();
                    }

                    public static void ConfigureContainer() { }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ControllerActivatorDoesNotSuppressPageModelConstructor()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc.Controllers
                {
                    public interface IControllerActivator { }
                }

                public sealed class CustomActivator : Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator { }

                public sealed class OrdersPage : PageModel
                {
                    public OrdersPage({|DI038:IOrderService orders|}) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                        services.AddSingleton<Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator, CustomActivator>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task OpaqueRegisterWrapper_DoesNotReport()
    {
        const string externalSource = """
            using Microsoft.Extensions.DependencyInjection;

            public static class FeatureRegistrationExtensions
            {
                public static IServiceCollection RegisterApplicationServices(this IServiceCollection services) => services;
            }
            """;

        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.RegisterApplicationServices();
                        services.AddControllers();
                    }
                }
                """;

        var diagnostics = await GetDiagnosticsWithExternalReferenceAsync(source, externalSource);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == DiagnosticDescriptors.FrameworkActivatedDependency.Id
        );
    }

    [Fact]
    public async Task GeneratedRegistrationWrapper_DoesNotReport()
    {
        const string generated = """
            using Microsoft.Extensions.DependencyInjection;

            public static class GeneratedExtensions
            {
                public static IServiceCollection AddGeneratedServices(this IServiceCollection services)
                {
                    services.AddScoped<IOrderService, OrderService>();
                    return services;
                }
            }
            """;

        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddGeneratedServices();
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync([
            ("GeneratedRegistrations.g.cs", generated),
            ("Program.cs", source),
        ]);
    }

    [Fact]
    public async Task CustomPageModelActivator_DoesNotReportConstructor()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc.RazorPages
                {
                    public interface IPageModelActivatorProvider { }
                }

                public sealed class CustomPageActivator : IPageModelActivatorProvider { }

                public sealed class OrdersPage : PageModel
                {
                    public OrdersPage(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                        services.AddSingleton<IPageModelActivatorProvider, CustomPageActivator>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task InheritedNonHandlerOnOverride_DoesNotReport()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc.RazorPages
                {
                    [AttributeUsage(AttributeTargets.Method, Inherited = true)]
                    public sealed class NonHandlerAttribute : Attribute
                    {
                    }
                }

                public abstract class OrdersPageBase : PageModel
                {
                    [NonHandler]
                    public virtual object OnGet([FromServices] ISearchService search) => search;
                }

                public sealed class OrdersPage : OrdersPageBase
                {
                    public override object OnGet([FromServices] ISearchService search) => search;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task KeyedControllerActivator_StillReportsConstructor()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc.Controllers
                {
                    public interface IControllerActivator { }
                }

                public sealed class CustomActivator : Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator { }

                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController({|DI038:IOrderService orders|}) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                        services.AddKeyedSingleton<Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator, CustomActivator>("special");
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.ReferenceAssembliesWithLatestDi
        );
    }

    [Fact]
    public async Task PageActivatorDoesNotSuppressPageModelConstructor()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc.RazorPages
                {
                    public interface IPageActivatorProvider { }
                }

                public sealed class CustomPageActivator : IPageActivatorProvider { }

                public sealed class OrdersPage : PageModel
                {
                    public OrdersPage({|DI038:IOrderService orders|}) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                        services.AddSingleton<IPageActivatorProvider, CustomPageActivator>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CustomControllerFactory_DoesNotReportConstructor()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc.Controllers
                {
                    public interface IControllerFactory { }
                }

                public sealed class CustomFactory : Microsoft.AspNetCore.Mvc.Controllers.IControllerFactory { }

                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                        services.AddSingleton<Microsoft.AspNetCore.Mvc.Controllers.IControllerFactory, CustomFactory>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task CustomPageModelFactory_DoesNotReportConstructor()
    {
        var source =
            Usings
            + """
                namespace Microsoft.AspNetCore.Mvc.RazorPages
                {
                    public interface IPageModelFactoryProvider { }
                }

                public sealed class CustomPageFactory : IPageModelFactoryProvider { }

                public sealed class OrdersPage : PageModel
                {
                    public OrdersPage(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                        services.AddSingleton<IPageModelFactoryProvider, CustomPageFactory>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task OpaqueScanWrapper_DoesNotReport()
    {
        const string externalSource = """
            using Microsoft.Extensions.DependencyInjection;

            public static class AssemblyScanner
            {
                public static IServiceCollection Scan(this IServiceCollection services) => services;
            }
            """;

        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.Scan();
                        services.AddControllers();
                    }
                }
                """;

        var diagnostics = await GetDiagnosticsWithExternalReferenceAsync(source, externalSource);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == DiagnosticDescriptors.FrameworkActivatedDependency.Id
        );
    }

    [Fact]
    public async Task ObliviousFromServices_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                #nullable disable
                public sealed class OrdersController : ControllerBase
                {
                    public object Get([FromServices] {|DI038:ISearchService search|}) => search;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task MicrosoftExtensionsForAcme_StillReports()
    {
        var source =
            Usings
            + """
                namespace Microsoft.ExtensionsForAcme
                {
                    public interface ISearchIndex { }
                }

                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController({|DI038:Microsoft.ExtensionsForAcme.ISearchIndex index|}) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task HiddenBaseFromServicesWithNew_StillReportsBaseAction()
    {
        var source =
            Usings
            + """
                public abstract class OrdersControllerBase : ControllerBase
                {
                    public object Get([FromServices] {|DI038:ISearchService search|}) => search;
                }

                public sealed class OrdersController : OrdersControllerBase
                {
                    public new object Get(ISearchService search) => search;
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ServiceCollectionWithoutMvcActivation_DoesNotReport()
    {
        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(IOrderService orders) { }
                }

                public sealed class Worker { }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<Worker>();
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task KeyedConstructorHighlightsTheMissingKey()
    {
        var source =
            Usings
            + """
                public sealed class OrdersController : ControllerBase
                {
                    public OrdersController(
                        [FromKeyedServices("ok")] IInvoiceService paid,
                        [FromKeyedServices("missing")] {|DI038:IInvoiceService unpaid|})
                    {
                    }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddControllers();
                        services.AddKeyedScoped<IInvoiceService, InvoiceService>("ok");
                    }
                }
                """;

        await AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI038_FrameworkActivatedDependencyAnalyzer>.ReferenceAssembliesWithLatestDi
        );
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsWithExternalReferenceAsync(
        string source,
        string externalSource
    )
    {
        var compilation = CSharpCompilation.Create(
            "di038-under-test",
            [CSharpSyntaxTree.ParseText(source)],
            GetCompilationReferences(CreateExternalReference(externalSource)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new DI038_FrameworkActivatedDependencyAnalyzer()
        );
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static MetadataReference CreateExternalReference(string source)
    {
        var compilation = CSharpCompilation.Create(
            "di038-external",
            [CSharpSyntaxTree.ParseText(source)],
            GetCompilationReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(
            emitResult.Success,
            string.Join(
                "\n",
                emitResult.Diagnostics.Select(static diagnostic => diagnostic.ToString())
            )
        );

        stream.Position = 0;
        return MetadataReference.CreateFromStream(stream);
    }

    private static MetadataReference[] GetCompilationReferences(
        params MetadataReference[] additionalReferences
    )
    {
        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location),
            MetadataReference.CreateFromFile(
                typeof(System.Collections.Generic.IEnumerable<>).Assembly.Location
            ),
            MetadataReference.CreateFromFile(typeof(System.IServiceProvider).Assembly.Location),
            MetadataReference.CreateFromFile(
                typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection)
                    .Assembly
                    .Location
            ),
            MetadataReference.CreateFromFile(
                typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)
                    .Assembly
                    .Location
            ),
            .. additionalReferences,
        ];
    }
}
