using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

/// <summary>
/// Tests for DI002 suppression code fix.
/// </summary>
public class DI002_ScopeEscapeCodeFixTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Suppress with Pragma Fix

    [Fact]
    public async Task CodeFix_AddsPragmaSuppress_ReturnEscape()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public IMyService GetService()
                {
                    using var scope = _scopeFactory.CreateScope();
                    return scope.ServiceProvider.GetRequiredService<IMyService>();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        var fixedSource = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public IMyService GetService()
                {
                    using var scope = _scopeFactory.CreateScope();
            #pragma warning disable DI002
                    return scope.ServiceProvider.GetRequiredService<IMyService>();
            #pragma warning restore DI002
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        var expected = CodeFixVerifier<DI002_ScopeEscapeAnalyzer, DI002_ScopeEscapeCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
            .WithLocation(18, 16)
            .WithArguments("return");

        await CodeFixVerifier<DI002_ScopeEscapeAnalyzer, DI002_ScopeEscapeCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI002_Suppress");
    }

    [Fact]
    public async Task CodeFix_AddsPragmaSuppress_FieldAssignment()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IMyService _service;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Initialize()
                {
                    using var scope = _scopeFactory.CreateScope();
                    _service = scope.ServiceProvider.GetRequiredService<IMyService>();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        var fixedSource = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IMyService _service;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Initialize()
                {
                    using var scope = _scopeFactory.CreateScope();
            #pragma warning disable DI002
                    _service = scope.ServiceProvider.GetRequiredService<IMyService>();
            #pragma warning restore DI002
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        var expected = CodeFixVerifier<DI002_ScopeEscapeAnalyzer, DI002_ScopeEscapeCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
            .WithLocation(19, 20)
            .WithArguments("_service");

        await CodeFixVerifier<DI002_ScopeEscapeAnalyzer, DI002_ScopeEscapeCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI002_Suppress");
    }

    [Fact]
    public async Task CodeFix_AddsPragmaSuppress_AliasedReturnEscape()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public IMyService GetService()
                {
                    IMyService escaped;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        escaped = {|#0:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    }

                    return escaped;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        var fixedSource = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public IMyService GetService()
                {
                    IMyService escaped;
                    using (var scope = _scopeFactory.CreateScope())
                    {
            #pragma warning disable DI002
                        escaped = scope.ServiceProvider.GetRequiredService<IMyService>();
            #pragma warning restore DI002
                    }

                    return escaped;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        var expected = CodeFixVerifier<DI002_ScopeEscapeAnalyzer, DI002_ScopeEscapeCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
            .WithLocation(0)
            .WithArguments("return");

        await CodeFixVerifier<DI002_ScopeEscapeAnalyzer, DI002_ScopeEscapeCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI002_Suppress");
    }

    #endregion

    #region Property Assignment Sink

    [Fact]
    public async Task CodeFix_AddsPragmaSuppress_PropertyAssignment()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                public IMyService Service { get; set; }

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Initialize()
                {
                    using var scope = _scopeFactory.CreateScope();
                    Service = scope.ServiceProvider.GetRequiredService<IMyService>();
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        var fixedSource = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;
                public IMyService Service { get; set; }

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Initialize()
                {
                    using var scope = _scopeFactory.CreateScope();
            #pragma warning disable DI002
                    Service = scope.ServiceProvider.GetRequiredService<IMyService>();
            #pragma warning restore DI002
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        var expected = CodeFixVerifier<DI002_ScopeEscapeAnalyzer, DI002_ScopeEscapeCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
            .WithLocation(19, 19)
            .WithArguments("Service");

        await CodeFixVerifier<DI002_ScopeEscapeAnalyzer, DI002_ScopeEscapeCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI002_Suppress");
    }

    #endregion

    #region Ref/Out and Delegate Sinks

    [Fact]
    public async Task CodeFix_AddsPragmaSuppress_RefParameterAliasEscape()
    {
        var source = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Resolve(ref IMyService escaped)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = {|#0:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                    escaped = service;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        var fixedSource = Usings + """
            public interface IMyService { }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Resolve(ref IMyService escaped)
                {
                    using var scope = _scopeFactory.CreateScope();
            #pragma warning disable DI002
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
            #pragma warning restore DI002
                    escaped = service;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IMyService, ScopedMyService>();
                }
            }

            public class ScopedMyService : IMyService { }
            """;

        var expected = CodeFixVerifier<DI002_ScopeEscapeAnalyzer, DI002_ScopeEscapeCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
            .WithLocation(0)
            .WithArguments("escaped");

        await CodeFixVerifier<DI002_ScopeEscapeAnalyzer, DI002_ScopeEscapeCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI002_Suppress");
    }

    [Fact]
    public async Task CodeFix_AddsPragmaSuppress_CapturedDelegateReturnEscape()
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

                public Action GetWork()
                {
                    Action work;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = {|#0:scope.ServiceProvider.GetRequiredService<IMyService>()|};
                        work = () => service.DoWork();
                    }

                    return work;
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

                public Action GetWork()
                {
                    Action work;
                    using (var scope = _scopeFactory.CreateScope())
                    {
            #pragma warning disable DI002
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
            #pragma warning restore DI002
                        work = () => service.DoWork();
                    }

                    return work;
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

        var expected = CodeFixVerifier<DI002_ScopeEscapeAnalyzer, DI002_ScopeEscapeCodeFixProvider>
            .Diagnostic(DiagnosticDescriptors.ScopedServiceEscapes)
            .WithLocation(0)
            .WithArguments("return");

        await CodeFixVerifier<DI002_ScopeEscapeAnalyzer, DI002_ScopeEscapeCodeFixProvider>
            .VerifyCodeFixAsync(source, expected, fixedSource, "DI002_Suppress");
    }

    #endregion
}
