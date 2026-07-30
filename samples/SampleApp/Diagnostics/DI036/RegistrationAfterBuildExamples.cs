using System;
using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI036
{
    public interface IAuditTrail
    {
        void Record(string action);
    }

    public sealed class AuditTrail : IAuditTrail
    {
        public void Record(string action) { }
    }

    public static class Registrations
    {
        public static IServiceProvider ConfigureBad()
        {
            var services = new ServiceCollection();
            var provider = services.BuildServiceProvider();

            // DI036: the provider already holds its own copy of the descriptor list, so this
            // audit trail never resolves from it.
            services.AddSingleton<IAuditTrail, AuditTrail>();

            return provider;
        }

        public static IServiceProvider ConfigureGood()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAuditTrail, AuditTrail>();

            // Building last means every registration reaches the container.
            return services.BuildServiceProvider();
        }
    }
}
