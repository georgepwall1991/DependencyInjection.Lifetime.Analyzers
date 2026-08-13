using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Mvc
{
    public abstract class ControllerBase { }

    [System.AttributeUsage(System.AttributeTargets.Parameter)]
    public sealed class FromServicesAttribute : System.Attribute { }
}

namespace Microsoft.AspNetCore.Mvc.RazorPages
{
    public abstract class PageModel { }
}

namespace SampleApp.Diagnostics.DI038
{
    using Microsoft.AspNetCore.Mvc;

    public interface IOrderService { }

    public sealed class OrderService : IOrderService { }

    public interface IMissingCatalog { }

    public interface ISearchService { }

    public static class MvcServiceCollectionExtensions
    {
        public static IServiceCollection AddControllers(this IServiceCollection services) =>
            services;
    }

    /// <summary>
    /// ⚠️ BAD: ASP.NET Core activates this controller even though IMissingCatalog is never registered.
    /// </summary>
    public sealed class OrdersController : ControllerBase
    {
        public OrdersController(IMissingCatalog catalog) { }
    }

    /// <summary>
    /// ⚠️ BAD: [FromServices] asks the container for a service that is not registered.
    /// </summary>
    public sealed class SearchController : ControllerBase
    {
        public object Get([FromServices] ISearchService search) => search;
    }

    /// <summary>
    /// ✅ GOOD: the constructor dependency is registered.
    /// </summary>
    public sealed class InvoicesController : ControllerBase
    {
        public InvoicesController(IOrderService orders) { }
    }

    public static class FrameworkActivatedDependencyExamples
    {
        public static void Register(IServiceCollection services)
        {
            services.AddControllers();
            services.AddScoped<IOrderService, OrderService>();
        }
    }
}
