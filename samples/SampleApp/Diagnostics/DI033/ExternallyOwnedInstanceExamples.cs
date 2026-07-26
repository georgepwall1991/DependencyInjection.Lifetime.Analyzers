using System;
using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI033
{
    public interface IMetricsSink
    {
        void Record(string name, double value);
    }

    public sealed class MetricsSink : IMetricsSink, IDisposable
    {
        public void Record(string name, double value) { }

        public void Dispose() { }
    }

    public static class Registrations
    {
        public static void Configure(IServiceCollection services)
        {
            // DI033: the container disposes only what it creates, so this sink's file handles
            // stay open until the caller disposes it.
            services.AddSingleton<IMetricsSink>(new MetricsSink());
        }

        public static void ConfigureGood(IServiceCollection services)
        {
            // Handing the type over makes the container the owner, disposal included.
            services.AddSingleton<IMetricsSink, MetricsSink>();
        }
    }
}
