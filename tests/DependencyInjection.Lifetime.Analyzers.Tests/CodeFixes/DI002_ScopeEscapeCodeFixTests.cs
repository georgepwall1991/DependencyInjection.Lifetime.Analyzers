using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
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

    #region Add TODO Comment Fix

    [Fact]
    public async Task CodeFix_OffersBothActions_ReturnEscape()
    {
        // Verifies both DI002_AddTodo and DI002_Suppress actions are registered.
        // The AddTodo code path was entirely untested prior to this.
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

        var actions = await GetRegisteredCodeActionsAsync(source);
        Assert.Contains(actions, a => a.EquivalenceKey == "DI002_AddTodo");
        Assert.Contains(actions, a => a.EquivalenceKey == "DI002_Suppress");
    }

    [Fact]
    public async Task CodeFix_OffersBothActions_FieldAssignment()
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

        var actions = await GetRegisteredCodeActionsAsync(source);
        Assert.Contains(actions, a => a.EquivalenceKey == "DI002_AddTodo");
        Assert.Contains(actions, a => a.EquivalenceKey == "DI002_Suppress");
    }

    private static async Task<List<CodeAction>> GetRegisteredCodeActionsAsync(string source)
    {
        var references = new ReferenceAssemblies("net8.0",
                new PackageIdentity("Microsoft.NETCore.App.Ref", "8.0.0"), System.IO.Path.Combine("ref", "net8.0"))
            .AddPackages([new PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0")]);
        var metadataRefs = await references.ResolveAsync(LanguageNames.CSharp, default);
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProject", LanguageNames.CSharp)
            .WithMetadataReferences(metadataRefs);
        var document = project.AddDocument("Test.cs", source);
        var compilation = await document.Project.GetCompilationAsync();
        Assert.NotNull(compilation);

        var analyzer = new DI002_ScopeEscapeAnalyzer();
        var diagnostics = await compilation!
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer))
            .GetAnalyzerDiagnosticsAsync();

        var diagnostic = diagnostics.First(d => d.Id == DiagnosticIds.ScopedServiceEscapes);

        var actions = new List<CodeAction>();
        var codeFix = new DI002_ScopeEscapeCodeFixProvider();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            default);

        await codeFix.RegisterCodeFixesAsync(context);
        return actions;
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
}
