using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

public class DI004_UseAfterDisposeCodeFixTests
{
    private const string Usings = """
        using Microsoft.Extensions.DependencyInjection;

        """;

    [Fact]
    public async Task MoveUseIntoScope_ImmediateExpressionStatement_MovesStatementIntoUsingBlock()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }
                    {|#0:service.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        var fixedSource = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        service.DoWork();
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                    .WithLocation(0)
                    .WithArguments("service"),
                fixedSource,
                "DI004_MoveUseIntoScope");
    }

    [Fact]
    public async Task Suppress_PragmaWrapsContextDependentReturn()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public IMyService GetService()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    return {|#0:service|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        var fixedSource = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public IMyService GetService()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

            #pragma warning disable DI004
                    return service;
            #pragma warning restore DI004
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                    .WithLocation(0)
                    .WithArguments("service"),
                fixedSource,
                "DI004_Suppress");
    }

    [Fact]
    public async Task MoveUseIntoScope_PreservesLeadingComment()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }
                    // Use immediately after resolving.
                    {|#0:service.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        var fixedSource = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        // Use immediately after resolving.
                        service.DoWork();
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                    .WithLocation(0)
                    .WithArguments("service"),
                fixedSource,
                "DI004_MoveUseIntoScope");
    }

    [Fact]
    public async Task MoveUseIntoScope_NotOfferedWhenImmediateUsingDidNotCreateDiagnosticService()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService firstService;
                    using (var firstScope = _scopeFactory.CreateScope())
                    {
                        firstService = firstScope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    using (var secondScope = _scopeFactory.CreateScope())
                    {
                        var secondService = secondScope.ServiceProvider.GetRequiredService<IMyService>();
                        secondService.DoWork();
                    }

                    firstService.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(
                source,
                CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                    .WithArguments("firstService"),
                "DI004_MoveUseIntoScope");
    }

    [Fact]
    public async Task MoveUseIntoScope_AwaitUsingStatement_MovesStatementIntoUsingBlock()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public async System.Threading.Tasks.Task ProcessWorkAsync()
                {
                    IMyService service;
                    await using (var scope = _scopeFactory.CreateAsyncScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }
                    {|#0:service.DoWork()|};
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        var fixedSource = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public async System.Threading.Tasks.Task ProcessWorkAsync()
                {
                    IMyService service;
                    await using (var scope = _scopeFactory.CreateAsyncScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        service.DoWork();
                    }
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                    .WithLocation(0)
                    .WithArguments("service"),
                fixedSource,
                "DI004_MoveUseIntoScope");
    }

    [Fact]
    public async Task MoveUseIntoScope_ImmediateInvocationArgument_MovesCallIntoUsingBlock()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }
                    Use({|#0:service|});
                }

                private static void Use(IMyService service)
                {
                    service.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        var fixedSource = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        Use(service);
                    }
                }

                private static void Use(IMyService service)
                {
                    service.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                    .WithLocation(0)
                    .WithArguments("service"),
                fixedSource,
                "DI004_MoveUseIntoScope");
    }

    [Fact]
    public async Task MoveUseIntoScope_NotOfferedForEscapeAssignment()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IMyService? _service;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }
                    _service = service;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(
                source,
                CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                    .WithArguments("service"),
                "DI004_MoveUseIntoScope");
    }

    [Fact]
    public async Task MoveUseIntoScope_NotOfferedWhenImmediateUsingOnlyAssignsInNestedFunction()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var firstScope = _scopeFactory.CreateScope())
                    {
                        service = firstScope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    using (var secondScope = _scopeFactory.CreateScope())
                    {
                        void Reassign()
                        {
                            service = secondScope.ServiceProvider.GetRequiredService<IMyService>();
                        }
                    }

                    service.DoWork();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(
                source,
                CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                    .WithArguments("service"),
                "DI004_MoveUseIntoScope");
    }

    [Fact]
    public async Task MoveUseIntoScope_NotOfferedForReturn()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public IMyService GetService()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    return service;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService
            {
                public void DoWork() { }
            }
            """;

        await CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(
                source,
                CodeFixVerifier<DI004_UseAfterDisposeAnalyzer, DI004_UseAfterDisposeCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed),
                "DI004_MoveUseIntoScope");
    }
}
