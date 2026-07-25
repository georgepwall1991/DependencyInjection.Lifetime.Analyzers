using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI029_HttpClientLifetimeAnalyzerTests
{
    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using System.Net.Http;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        """;

    private static Task VerifyAsync(string source) =>
        AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.ReferenceAssembliesWithFrameworkExtensions
        );

    private static Task VerifySilentAsync(string source) =>
        AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.ReferenceAssembliesWithFrameworkExtensions
        );

    // ----------------------------------------------------------------
    // Tier A -- socket exhaustion, positives
    // ----------------------------------------------------------------

    [Fact]
    public async Task RegisteredScopedService_NewHttpClientInMethod_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    public async Task<string> GetAsync(string url)
                    {
                        var http = [|new HttpClient()|];
                        return await http.GetStringAsync(url);
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewHttpClientInUsingDeclaration_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    public async Task<string> GetAsync(string url)
                    {
                        using var http = [|new HttpClient()|];
                        return await http.GetStringAsync(url);
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewHttpClientInUsingStatement_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    public async Task<string> GetAsync(string url)
                    {
                        using (var http = [|new HttpClient()|])
                        {
                            return await http.GetStringAsync(url);
                        }
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RegisteredService_ImplicitNewHttpClient_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    public async Task<string> GetAsync(string url)
                    {
                        HttpClient http = [|new()|];
                        return await http.GetStringAsync(url);
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewHttpClientDirectMemberAccess_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    public Task<string> GetAsync(string url) =>
                        [|new HttpClient()|].GetStringAsync(url);
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RegisteredTransientService_NewHttpClientInConstructor_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<ApiClient>();
                }

                public class ApiClient
                {
                    private readonly HttpClient _http;

                    public ApiClient()
                    {
                        _http = [|new HttpClient()|];
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RegisteredSingletonService_NewHttpClientInLoopBody_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<ApiClient>();
                }

                public class ApiClient
                {
                    public async Task WarmAsync(string[] urls)
                    {
                        foreach (var url in urls)
                        {
                            var http = [|new HttpClient()|];
                            await http.GetStringAsync(url);
                        }
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewHttpClientHandler_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    public void Configure()
                    {
                        var handler = [|new HttpClientHandler()|];
                        handler.UseCookies = false;
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewSocketsHttpHandler_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    public void Configure()
                    {
                        var handler = [|new SocketsHttpHandler()|];
                        handler.MaxConnectionsPerServer = 4;
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewHttpClientWrappingNewHandler_ReportsOutermostOnly()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    public async Task<string> GetAsync(string url)
                    {
                        var http = [|new HttpClient(new SocketsHttpHandler())|];
                        return await http.GetStringAsync(url);
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewHttpClientInLocalFunction_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    public Task<string> GetAsync(string url)
                    {
                        return LoadAsync();

                        async Task<string> LoadAsync()
                        {
                            var http = [|new HttpClient()|];
                            return await http.GetStringAsync(url);
                        }
                    }
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewHttpClientInBaseClassOfRegisteredImplementation_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public abstract class ApiClientBase
                {
                    protected async Task<string> LoadAsync(string url)
                    {
                        var http = [|new HttpClient()|];
                        return await http.GetStringAsync(url);
                    }
                }

                public class ApiClient : ApiClientBase
                {
                    public Task<string> GetAsync(string url) => LoadAsync(url);
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TypedHttpClientImplementation_NewHttpClientInMethod_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddHttpClient<ApiClient>();
                }

                public class ApiClient
                {
                    private readonly HttpClient _injected;

                    public ApiClient(HttpClient injected) => _injected = injected;

                    public async Task<string> GetOtherAsync(string url)
                    {
                        var http = [|new HttpClient()|];
                        return await http.GetStringAsync(url);
                    }
                }
                """;

        await VerifyAsync(source);
    }

    // ----------------------------------------------------------------
    // Tier A -- silence
    // ----------------------------------------------------------------

    [Fact]
    public async Task UnregisteredHelperType_NewHttpClient_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    public string Name => "api";
                }

                public class TestHelper
                {
                    public async Task<string> GetAsync(string url)
                    {
                        var http = new HttpClient();
                        return await http.GetStringAsync(url);
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task NoRegistrationsInCompilation_NewHttpClient_Silent()
    {
        var source =
            Prelude
            + """
                public class ApiClient
                {
                    public async Task<string> GetAsync(string url)
                    {
                        var http = new HttpClient();
                        return await http.GetStringAsync(url);
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task TopLevelMain_NewHttpClient_Silent()
    {
        var source = """
            using System.Net.Http;

            var http = new HttpClient();
            var body = await http.GetStringAsync("https://example.com");
            System.Console.WriteLine(body);
            """;

        await AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.VerifyNoDiagnosticsAsConsoleApplicationWithReferencesAsync(
            source,
            AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.ReferenceAssembliesWithFrameworkExtensions
        );
    }

    [Fact]
    public async Task ConsoleApplicationMainMethod_NewHttpClient_Silent()
    {
        var source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public static class Program
            {
                public static async Task Main()
                {
                    var http = new HttpClient();
                    await http.GetStringAsync("https://example.com");
                }
            }
            """;

        await AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.VerifyNoDiagnosticsAsConsoleApplicationWithReferencesAsync(
            source,
            AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.ReferenceAssembliesWithFrameworkExtensions
        );
    }

    [Fact]
    public async Task RegisteredScopedService_NewHttpClientInConstructor_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    private readonly HttpClient _http;

                    public ApiClient()
                    {
                        _http = new HttpClient();
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task RegisteredSingletonService_NewHttpClientInConstructor_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<ApiClient>();
                }

                public class ApiClient
                {
                    private readonly HttpClient _http;

                    public ApiClient()
                    {
                        _http = new HttpClient();
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewHttpClientAssignedToInstanceField_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    private HttpClient? _http;

                    public void Init()
                    {
                        _http = new HttpClient();
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewHttpClientReturned_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    public HttpClient Create() => new HttpClient();
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewHttpClientPassedAsArgument_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    public void Init() => Store(new HttpClient());

                    private void Store(HttpClient http) { }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewHttpClientWithInjectedHandler_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    private readonly HttpMessageHandler _handler;

                    public ApiClient(HttpMessageHandler handler) => _handler = handler;

                    public async Task<string> GetAsync(string url)
                    {
                        var http = new HttpClient(_handler);
                        return await http.GetStringAsync(url);
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewHttpClientWithDisposeHandlerFalse_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    private static readonly SocketsHttpHandler Shared = new SocketsHttpHandler();

                    public async Task<string> GetAsync(string url)
                    {
                        var http = new HttpClient(Shared, disposeHandler: false);
                        return await http.GetStringAsync(url);
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewHttpClientWithNonConstantDisposeHandler_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    private readonly bool _dispose;

                    public ApiClient(bool dispose) => _dispose = dispose;

                    public async Task<string> GetAsync(string url)
                    {
                        var http = new HttpClient(new SocketsHttpHandler(), _dispose);
                        return await http.GetStringAsync(url);
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task RegisteredService_IHttpClientFactoryCreateClient_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddHttpClient();
                        services.AddScoped<ApiClient>();
                    }
                }

                public class ApiClient
                {
                    private readonly IHttpClientFactory _factory;

                    public ApiClient(IHttpClientFactory factory) => _factory = factory;

                    public async Task<string> GetAsync(string url)
                    {
                        var http = _factory.CreateClient();
                        return await http.GetStringAsync(url);
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task TypedClient_InjectedHttpClientUsedInMethod_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddHttpClient<ApiClient>();
                }

                public class ApiClient
                {
                    private readonly HttpClient _http;

                    public ApiClient(HttpClient http) => _http = http;

                    public Task<string> GetAsync(string url) => _http.GetStringAsync(url);
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task DerivedHttpClientCreation_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class PooledClient : HttpClient { }

                public class ApiClient
                {
                    public async Task<string> GetAsync(string url)
                    {
                        var http = new PooledClient();
                        return await http.GetStringAsync(url);
                    }
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task RegisteredService_NewHttpClient_WithoutHttpClientFactoryReference_Silent()
    {
        // Default reference set: no Microsoft.Extensions.Http, so IHttpClientFactory does not
        // resolve and the socket-exhaustion tier has no actionable remedy to point at.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<ApiClient>();
                }

                public class ApiClient
                {
                    public async Task<string> GetAsync(string url)
                    {
                        var http = new HttpClient();
                        return await http.GetStringAsync(url);
                    }
                }
                """;

        await AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    // ----------------------------------------------------------------
    // Tier B leg 1 -- container singleton, positives
    // ----------------------------------------------------------------

    [Fact]
    public async Task AddSingletonHttpClient_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        [|services.AddSingleton<HttpClient>()|];
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task AddSingletonNewHttpClientInstance_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        [|services.AddSingleton(new HttpClient())|];
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task AddSingletonHttpClientFactoryLambda_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        [|services.AddSingleton<HttpClient>(sp => new HttpClient())|];
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceDescriptorSingletonHttpClient_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        [|services.Add(ServiceDescriptor.Singleton<HttpClient, HttpClient>())|];
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task AddKeyedSingletonHttpClient_Reports()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        [|services.AddKeyedSingleton<HttpClient>("github")|];
                }
                """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task AddSingletonHttpClient_WithoutHttpClientFactoryReference_StillReports()
    {
        // The registration tier is not gated on IHttpClientFactory: an explicit singleton HttpClient
        // is a process-lifetime handler regardless of which packages are referenced.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        [|services.AddSingleton<HttpClient>()|];
                }
                """;

        await AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    // ----------------------------------------------------------------
    // Tier B leg 1 -- silence
    // ----------------------------------------------------------------

    [Fact]
    public async Task AddTransientHttpClient_Silent()
    {
        // DI008's disposable-transient finding. DI029 must not double-report it.
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddTransient<HttpClient>();
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task AddScopedHttpClient_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddScoped<HttpClient>();
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task AddSingletonDerivedHttpClient_Silent()
    {
        var source =
            Prelude
            + """
                public class PooledClient : HttpClient { }

                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddSingleton<PooledClient>();
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task AddHttpClientTypedClient_Silent()
    {
        var source =
            Prelude
            + """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services) =>
                        services.AddHttpClient<ApiClient>();
                }

                public class ApiClient
                {
                    private readonly HttpClient _http;

                    public ApiClient(HttpClient http) => _http = http;
                }
                """;

        await VerifySilentAsync(source);
    }

    // ----------------------------------------------------------------
    // Tier B leg 2 -- static member
    // ----------------------------------------------------------------

    [Fact]
    public async Task StaticHttpClientField_Reports()
    {
        var source =
            Prelude
            + """
                public class ApiClient
                {
                    private static HttpClient {|#0:_http|} = null!;
                }
                """;

        await AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.ReferenceAssembliesWithFrameworkExtensions,
            AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>
                .Diagnostic(
                    DependencyInjection
                        .Lifetime
                        .Analyzers
                        .DiagnosticDescriptors
                        .HttpClientStaleDnsStaticMember
                )
                .WithLocation(0)
                .WithArguments("_http", "field")
        );
    }

    [Fact]
    public async Task StaticReadonlyHttpClientFieldWithInitializer_ReportsOnceOnDeclaration()
    {
        // The construction sits in a static initializer, which the socket-exhaustion tier excludes,
        // so exactly one diagnostic lands -- on the declaration, not on the `new`.
        var source =
            Prelude
            + """
                public class ApiClient
                {
                    private static readonly HttpClient {|#0:Shared|} = new HttpClient();
                }
                """;

        await AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.ReferenceAssembliesWithFrameworkExtensions,
            AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>
                .Diagnostic(
                    DependencyInjection
                        .Lifetime
                        .Analyzers
                        .DiagnosticDescriptors
                        .HttpClientStaleDnsStaticMember
                )
                .WithLocation(0)
                .WithArguments("Shared", "field")
        );
    }

    [Fact]
    public async Task StaticHttpClientProperty_Reports()
    {
        var source =
            Prelude
            + """
                public class ApiClient
                {
                    public static HttpClient {|#0:Shared|} { get; } = null!;
                }
                """;

        await AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.ReferenceAssembliesWithFrameworkExtensions,
            AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>
                .Diagnostic(
                    DependencyInjection
                        .Lifetime
                        .Analyzers
                        .DiagnosticDescriptors
                        .HttpClientStaleDnsStaticMember
                )
                .WithLocation(0)
                .WithArguments("Shared", "property")
        );
    }

    [Fact]
    public async Task InstanceHttpClientField_Silent()
    {
        var source =
            Prelude
            + """
                public class ApiClient
                {
                    private readonly HttpClient _http = null!;
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task StaticLazyOfHttpClientField_Silent()
    {
        var source =
            Prelude
            + """
                public class ApiClient
                {
                    private static readonly Lazy<HttpClient> Shared = new Lazy<HttpClient>();
                }
                """;

        await VerifySilentAsync(source);
    }

    [Fact]
    public async Task StaticHttpClientField_WithoutHttpClientFactoryReference_Silent()
    {
        var source =
            Prelude
            + """
                public class ApiClient
                {
                    private static HttpClient _http = null!;
                }
                """;

        await AnalyzerVerifier<DI029_HttpClientLifetimeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
}
