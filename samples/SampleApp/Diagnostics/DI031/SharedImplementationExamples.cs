using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI031
{
    public interface IFeatureReader
    {
        bool IsEnabled(string feature);
    }

    public interface IFeatureWriter
    {
        void Set(string feature, bool enabled);
    }

    /// <summary>
    /// Holds mutable state, so two instances mean two different answers to the same question.
    /// </summary>
    public sealed class FeatureStore : IFeatureReader, IFeatureWriter
    {
        private bool _enabled;

        public bool IsEnabled(string feature) => _enabled;

        public void Set(string feature, bool enabled) => _enabled = enabled;
    }

    public static class Registrations
    {
        public static void ConfigureBad(IServiceCollection services)
        {
            services.AddSingleton<IFeatureReader, FeatureStore>();

            // DI031: a second descriptor means a second FeatureStore. Writes through
            // IFeatureWriter are invisible to IFeatureReader.
            services.AddSingleton<IFeatureWriter, FeatureStore>();
        }

        public static void ConfigureGood(IServiceCollection services)
        {
            services.AddSingleton<FeatureStore>();
            services.AddSingleton<IFeatureReader>(sp => sp.GetRequiredService<FeatureStore>());
            services.AddSingleton<IFeatureWriter>(sp => sp.GetRequiredService<FeatureStore>());
        }
    }
}
