using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

/// <summary>
/// Tests for DI002 code fix: Scope escape fixes.
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

    #endregion
}
