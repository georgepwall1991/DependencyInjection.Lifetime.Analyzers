using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI013_ImplementationTypeMismatchAnalyzerTests
{
    private const string Usings = @"
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
";

    [Fact]
    public async Task ValidRegistration_NoDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IService), typeof(Service));
        services.AddScoped(typeof(Service), typeof(Service));
        services.AddTransient(typeof(Service)); // Self-registration
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ValidOpenGeneric_NoDiagnostic()
    {
        var source = Usings + @"
public interface IRepo<T> {}
public class Repo<T> : IRepo<T> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IRepo<>), typeof(Repo<>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InvalidTypeMismatch_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IService), typeof(string));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source, 
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("String", "IService"));
    }

    [Fact]
    public async Task InvalidOpenGenericMismatch_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IRepo<T> {}
public class Repo<T> : IRepo<T> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IRepo<>), typeof(List<>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("List", "IRepo"));
    }
    
    [Fact]
    public async Task InvalidMixedOpenClosed_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IRepo<T> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IRepo<>), typeof(string));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(12, 9)
                .WithArguments("String", "IRepo"));
    }
}