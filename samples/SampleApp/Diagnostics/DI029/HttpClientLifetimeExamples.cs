using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace System.Net.Http
{
    // IHttpClientFactory ships in Microsoft.Extensions.Http. This sample stays package-free by
    // declaring the same contract in its own namespace, which the analyzer resolves by metadata name
    // exactly as it resolves the real one. Delete this stub if that package is ever added to
    // SampleApp — two declarations of the same name would then be ambiguous (CS0436).
    public interface IHttpClientFactory
    {
        HttpClient CreateClient(string name);
    }
}

namespace SampleApp.Diagnostics.DI029
{
    public sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }

    public static class Registrations
    {
        public static void Configure(IServiceCollection services)
        {
            services.AddSingleton<IHttpClientFactory, StubHttpClientFactory>();
            services.AddScoped<Bad_HttpClientPerRequest>();
            services.AddScoped<Good_InjectedFactory>();
        }
    }

    // Rule DI029: HttpClient lifetime misuse. A client constructed on a per-invocation path opens its
    // own connection pool, and disposing it strands the socket in TIME_WAIT for minutes — so under
    // load the ephemeral port range runs out. Registering the client as a singleton instead trades
    // that for the opposite defect: one handler that never rotates and never re-resolves DNS.
    // IHttpClientFactory pools handlers and retires them on a rotation interval, giving connection
    // reuse and DNS freshness at once.

    public class Bad_HttpClientPerRequest
    {
        public async Task<string> GetAsync(string url)
        {
            // [DI029] One socket per call, stranded in TIME_WAIT after disposal. The using statement
            // is what causes the leak here, not what prevents it.
            using var http = new HttpClient();
            return await http.GetStringAsync(url);
        }
    }

    public class Good_InjectedFactory
    {
        private readonly IHttpClientFactory _factory;

        public Good_InjectedFactory(IHttpClientFactory factory) => _factory = factory;

        public async Task<string> GetAsync(string url)
        {
            // The factory owns the handler pool and rotates it.
            var http = _factory.CreateClient("default");
            return await http.GetStringAsync(url);
        }
    }
}
