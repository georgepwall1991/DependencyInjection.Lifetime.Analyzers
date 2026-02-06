using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI014_RootProviderNotDisposedAnalyzerTests
{
    private const string Usings = @"
using Microsoft.Extensions.DependencyInjection;
using System;
";

    [Fact]
    public async Task BuildServiceProvider_InUsingStatement_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        using (var provider = services.BuildServiceProvider())
        {
        }
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_InUsingDeclaration_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
    
    [Fact]
    public async Task BuildServiceProvider_ExplicitDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        provider.Dispose();
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_DisposedInsideLambda_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        Action action = () =>
        {
            var services = new ServiceCollection();
            var provider = services.BuildServiceProvider();
            provider.Dispose();
        };
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_Returned_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public IServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        return services.BuildServiceProvider();
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_NotDisposed_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(10, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_InsideUsingBody_NotDisposed_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        using (var disposable = new DummyDisposable())
        {
            var provider = services.BuildServiceProvider();
        }
    }
}

public sealed class DummyDisposable : IDisposable
{
    public void Dispose() { }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(12, 28));
    }

    [Fact]
    public async Task BuildServiceProvider_DisposeCallBeforeCreation_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        IServiceProvider? provider = null;
        (provider as IDisposable)?.Dispose();
        provider = services.BuildServiceProvider();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(12, 20));
    }

    [Fact]
    public async Task BuildServiceProvider_ShadowedVariableDisposed_OuterProviderStillReported()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        {
            var provider2 = services.BuildServiceProvider();
            provider2.Dispose();
        }
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(10, 24));
    }
}
