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
    public async Task BuildServiceProvider_AssignedToField_DisposedInDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program : IDisposable
{
    private ServiceProvider? _provider;

    public Program()
    {
        var services = new ServiceCollection();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider?.Dispose();
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_AssignedToProperty_DisposedInDisposeAsync_NoDiagnostic()
    {
        var source = @"
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

public class Program : IAsyncDisposable
{
    private ServiceProvider Provider { get; set; }

    public Program()
    {
        var services = new ServiceCollection();
        Provider = services.BuildServiceProvider();
    }

    public ValueTask DisposeAsync()
    {
        return Provider.DisposeAsync();
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
    public async Task BuildServiceProvider_DisposedOnlyInsideLambda_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        Action action = () => provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithSpan(10, 24, 10, 55));
    }

    [Fact]
    public async Task BuildServiceProvider_AssignedToField_DisposedInOtherMethod_StillReported()
    {
        var source = Usings + @"
public class Program
{
    private ServiceProvider? _provider;

    public void Main()
    {
        var services = new ServiceCollection();
        _provider = services.BuildServiceProvider();
    }

    public void Stop()
    {
        _provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(12, 21));
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
    public async Task BuildServiceProvider_DisposedOnlyConditionally_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool shouldDispose)
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        if (shouldDispose)
        {
            provider.Dispose();
        }
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(10, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_DisposedOnlyInCatch_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        try
        {
            var service = provider.GetService(typeof(object));
        }
        catch
        {
            provider.Dispose();
        }
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(10, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_InterveningReassignment_FirstProviderStillReported()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        provider = services.BuildServiceProvider();
        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(10, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_IfElseOuterAssignmentsDisposedAfterBranch_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchReturnsBeforeSharedDispose_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            return;
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(13, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchThrowsBeforeSharedDispose_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            throw new InvalidOperationException();
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(13, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_NestedIfReturnBeforeSharedDispose_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary, bool fail)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            if (fail)
            {
                return;
            }
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(13, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchDisposesBeforeReturnAndSharedDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            provider.Dispose();
            return;
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchDisposeAsyncBeforeReturnAndSharedDispose_NoDiagnostic()
    {
        var source = @"
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

public class Program
{
    public async Task Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            await provider.DisposeAsync();
            return;
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        await provider.DisposeAsync();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchDisposesBeforeNestedReturnAndSharedDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary, bool done)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            provider.Dispose();
            if (done)
            {
                return;
            }
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchReturnsBeforeFinallyDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        try
        {
            if (usePrimary)
            {
                provider = services.BuildServiceProvider();
                return;
            }
        }
        finally
        {
            provider?.Dispose();
        }
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_CatchBranchReturnsBeforeFinallyDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        try
        {
            throw new InvalidOperationException();
        }
        catch
        {
            provider = services.BuildServiceProvider();
            return;
        }
        finally
        {
            provider?.Dispose();
        }
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_CatchReturnProtectedByFinallyDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        provider = services.BuildServiceProvider();
        try
        {
            throw new InvalidOperationException();
        }
        catch
        {
            return;
        }
        finally
        {
            provider.Dispose();
        }
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchLocalFinallyDisposesBeforeReturn_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            try
            {
                return;
            }
            finally
            {
                provider.Dispose();
            }
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchConditionalDisposeBeforeReturn_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            provider?.Dispose();
            return;
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchCaughtThrowBeforeSharedDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            try
            {
                throw new InvalidOperationException();
            }
            catch
            {
            }
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchExactTypedCaughtThrowBeforeSharedDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            try
            {
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException)
            {
            }
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchBaseTypedCaughtThrowBeforeSharedDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            try
            {
                throw new InvalidOperationException();
            }
            catch (Exception)
            {
            }
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchRethrowCaughtByOuterExceptionCatchBeforeSharedDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        try
        {
            if (usePrimary)
            {
                provider = services.BuildServiceProvider();
                try
                {
                    throw new InvalidOperationException();
                }
                catch
                {
                    throw;
                }
            }
        }
        catch (Exception)
        {
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchTypedRethrowCaughtByOuterTypedCatchBeforeSharedDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        try
        {
            if (usePrimary)
            {
                provider = services.BuildServiceProvider();
                try
                {
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
            }
        }
        catch (InvalidOperationException)
        {
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_NestedIfElseCreatesOrReturnsBeforeSharedDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary, bool create)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            if (create)
            {
                provider = services.BuildServiceProvider();
            }
            else
            {
                return;
            }
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchThrowCaughtOutsideBeforeSharedDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        try
        {
            if (usePrimary)
            {
                provider = services.BuildServiceProvider();
                throw new InvalidOperationException();
            }
        }
        catch
        {
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchConditionalFinallyBeforeReturn_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary, bool cleanup)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            try
            {
                return;
            }
            finally
            {
                if (cleanup)
                {
                    provider.Dispose();
                }
            }
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(13, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchFinallyDisposeOnlyInLocalFunction_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            try
            {
                return;
            }
            finally
            {
                void Cleanup()
                {
                    provider.Dispose();
                }
            }
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(13, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchOuterCatchWithNestedReturnBeforeSharedDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        try
        {
            if (usePrimary)
            {
                provider = services.BuildServiceProvider();
                throw new InvalidOperationException();
            }
        }
        catch
        {
            int GetValue()
            {
                return 42;
            }
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchCatchDisposesBeforeReturn_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            try
            {
                throw new InvalidOperationException();
            }
            catch
            {
                provider.Dispose();
                return;
            }
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchCatchNestedBranchDisposesBeforeReturn_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary, bool done)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            try
            {
                throw new InvalidOperationException();
            }
            catch
            {
                if (done)
                {
                    provider.Dispose();
                    return;
                }
            }
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_FirstMatchingCatchReturnsBeforeSharedDispose_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            try
            {
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException)
            {
                return;
            }
            catch (Exception)
            {
            }
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(13, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_FilteredCatchReturnsBeforeSharedDispose_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool fail)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        try
        {
            provider = services.BuildServiceProvider();
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (fail)
        {
            return;
        }
        catch (Exception)
        {
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(13, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_FilteredCatchCanBypassBeforeSharedDispose_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool cleanup)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        try
        {
            provider = services.BuildServiceProvider();
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (cleanup)
        {
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(13, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_FilteredCatchBypassCaughtBeforeSharedDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool cleanup)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        try
        {
            provider = services.BuildServiceProvider();
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (cleanup)
        {
        }
        catch (Exception)
        {
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_SwitchSectionAssignmentDisposedAfterSwitch_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(int mode)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        switch (mode)
        {
            case 1:
                provider = services.BuildServiceProvider();
                break;
            case 2:
                return;
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_TryAssignmentCatchReturnsBeforeSharedDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        try
        {
            provider = services.BuildServiceProvider();
        }
        catch
        {
            return;
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_SwitchGotoReassignmentBeforeSharedDispose_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(int mode)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        switch (mode)
        {
            case 1:
                provider = services.BuildServiceProvider();
                break;
            case 2:
                provider = services.BuildServiceProvider();
                goto case 1;
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(17, 28));
    }

    [Fact]
    public async Task BuildServiceProvider_SwitchGotoConstantReassignmentBeforeSharedDispose_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    private const int One = 1;

    public void Main(int mode)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        switch (mode)
        {
            case 1:
                provider = services.BuildServiceProvider();
                break;
            case 2:
                provider = services.BuildServiceProvider();
                goto case One;
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(19, 28));
    }

    [Fact]
    public async Task BuildServiceProvider_SwitchGotoDifferentSectionBeforeSharedDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(int mode)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        switch (mode)
        {
            case 1:
                provider = services.BuildServiceProvider();
                goto case 3;
            case 2:
                provider = services.BuildServiceProvider();
                break;
            case 3:
                break;
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_SwitchGotoTargetDisposesBeforeReassignment_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(int mode)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        switch (mode)
        {
            case 1:
                provider?.Dispose();
                provider = services.BuildServiceProvider();
                break;
            case 2:
                provider = services.BuildServiceProvider();
                goto case 1;
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_IfReturnBetweenCreationAndSharedDispose_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool fail)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        provider = services.BuildServiceProvider();
        if (fail)
        {
            return;
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(11, 20));
    }

    [Fact]
    public async Task BuildServiceProvider_IfBranchNonNullGuardDisposesBeforeReturn_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool usePrimary)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (usePrimary)
        {
            provider = services.BuildServiceProvider();
            if (provider != null)
            {
                provider.Dispose();
            }

            return;
        }
        else
        {
            provider = services.BuildServiceProvider();
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_NullGuardReturnBeforeDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        provider = services.BuildServiceProvider();
        if (provider is null)
        {
            return;
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_AssignedInsideNullGuardThenReturns_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        if (provider is null)
        {
            provider = services.BuildServiceProvider();
            return;
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(13, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_InnerBlockAssignmentReturnBeforeSharedDispose_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool fail)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        {
            provider = services.BuildServiceProvider();
        }

        if (fail)
        {
            return;
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(12, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_LoopOuterAssignmentDisposedAfterLoop_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool keepCreating)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        while (keepCreating)
        {
            provider = services.BuildServiceProvider();
            keepCreating = false;
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(13, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_LoopBodyBreaksAfterAssignmentDisposedAfterLoop_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool keepCreating)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        while (keepCreating)
        {
            provider = services.BuildServiceProvider();
            break;
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_LoopBodyCanContinueBeforeBreakDisposedAfterLoop_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool keepCreating, bool skipBreak)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        while (keepCreating)
        {
            provider = services.BuildServiceProvider();
            if (skipBreak)
            {
                continue;
            }

            break;
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(13, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_LoopBodySwitchContinueBeforeBreakDisposedAfterLoop_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool keepCreating, int mode)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        while (keepCreating)
        {
            provider = services.BuildServiceProvider();
            switch (mode)
            {
                case 1:
                    continue;
                default:
                    break;
            }

            break;
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(13, 24));
    }

    [Fact]
    public async Task BuildServiceProvider_ForInitializerAssignmentDisposedAfterLoop_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool keepRunning)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        for (provider = services.BuildServiceProvider(); keepRunning; keepRunning = false)
        {
        }

        provider.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_ForInitializerInsideOuterLoopDisposedAfterLoop_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool keepCreating, bool keepRunning)
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        while (keepCreating)
        {
            for (provider = services.BuildServiceProvider(); keepRunning; keepRunning = false)
            {
            }

            keepCreating = false;
        }

        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(13, 29));
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

    [Fact]
    public async Task BuildServiceProvider_AssignedInsideTryDisposedInFinally_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = null;
        try
        {
            provider = services.BuildServiceProvider();
        }
        finally
        {
            provider?.Dispose();
        }
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_DisposedBehindNullGuard_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        if (provider is not null)
        {
            provider.Dispose();
        }
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
}
