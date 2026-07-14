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
    public async Task BuildServiceProvider_NullForgivingExplicitDispose_NoDiagnostic()
    {
        var source = Usings + @"
#nullable enable

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = services.BuildServiceProvider();
        provider!.Dispose();
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_NullForgivingResultExplicitDispose_NoDiagnostic()
    {
        var source = Usings + @"
#nullable enable

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        ServiceProvider? provider = services.BuildServiceProvider()!;
        provider.Dispose();
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_ParenthesizedResultExplicitDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        var provider = (services.BuildServiceProvider());
        provider.Dispose();
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_CastResultExplicitDispose_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        var provider = (ServiceProvider)services.BuildServiceProvider();
        provider.Dispose();
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_UserDefinedCastDisposedWrapper_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        var wrapper = (ProviderWrapper)services.BuildServiceProvider();
        wrapper.Dispose();
    }
}

public sealed class ProviderWrapper : IDisposable
{
    public static explicit operator ProviderWrapper(ServiceProvider provider) => new ProviderWrapper();

    public void Dispose() { }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(10, 40));
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
    public async Task BuildServiceProvider_AssignedToField_DisposedInDisposeBoolPattern_NoDiagnostic()
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
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _provider?.Dispose();
        }
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_AssignedToField_DisposedInDisposeBoolPatternWithNullGuard_NoDiagnostic()
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
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_provider != null)
            {
                _provider.Dispose();
            }
        }
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_AssignedToField_DisposedInDisposeBoolPatternWithCombinedNullGuard_NoDiagnostic()
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
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && _provider != null)
        {
            _provider.Dispose();
        }
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_AssignedToField_DisposedOnlyWhenDisposeBoolFalse_ReportsDiagnostic()
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
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            _provider?.Dispose();
        }
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(12, 21));
    }

    [Fact]
    public async Task BuildServiceProvider_AssignedToField_DisposedBehindUnrelatedDisposeBoolCondition_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program : IDisposable
{
    private readonly bool _initialized;
    private ServiceProvider? _provider;

    public Program()
    {
        var services = new ServiceCollection();
        _provider = services.BuildServiceProvider();
        _initialized = true;
    }

    public void Dispose()
    {
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_initialized)
        {
            _provider?.Dispose();
        }
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(13, 21));
    }

    [Fact]
    public async Task BuildServiceProvider_AssignedToField_NullForgivingDisposedInDispose_NoDiagnostic()
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
        _provider!.Dispose();
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
    public async Task BuildServiceProvider_NullForgivingReturned_NoDiagnostic()
    {
        var source = Usings + @"
#nullable enable

public class Program
{
    public IServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        return services.BuildServiceProvider()!;
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_ParenthesizedReturned_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public IServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        return (services.BuildServiceProvider());
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_CastReturned_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public IServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        return (IServiceProvider)services.BuildServiceProvider();
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_UserDefinedCastReturnedWrapper_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public ProviderWrapper CreateProvider()
    {
        var services = new ServiceCollection();
        return (ProviderWrapper)services.BuildServiceProvider();
    }
}

public sealed class ProviderWrapper
{
    public static explicit operator ProviderWrapper(ServiceProvider provider) => new ProviderWrapper();
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(10, 33));
    }

    [Fact]
    public async Task BuildServiceProvider_ImplicitConversionReturnedWrapper_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public ProviderWrapper CreateProvider()
    {
        var services = new ServiceCollection();
        return services.BuildServiceProvider();
    }
}

public sealed class ProviderWrapper
{
    public static implicit operator ProviderWrapper(ServiceProvider provider) => new ProviderWrapper();
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(10, 16));
    }

    [Fact]
    public async Task BuildServiceProvider_ImplicitConversionInitializerDisposedWrapper_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        ProviderWrapper wrapper = services.BuildServiceProvider();
        wrapper.Dispose();
    }
}

public sealed class ProviderWrapper : IDisposable
{
    public static implicit operator ProviderWrapper(ServiceProvider provider) => new ProviderWrapper();

    public void Dispose() { }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(10, 35));
    }

    [Fact]
    public async Task BuildServiceProvider_ImplicitConversionAssignmentDisposedWrapper_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        ProviderWrapper wrapper;
        wrapper = services.BuildServiceProvider();
        wrapper.Dispose();
    }
}

public sealed class ProviderWrapper : IDisposable
{
    public static implicit operator ProviderWrapper(ServiceProvider provider) => new ProviderWrapper();

    public void Dispose() { }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(11, 19));
    }

    [Fact]
    public async Task BuildServiceProvider_DowncastThroughObjectReturnedWrapper_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public ProviderWrapper CreateProvider()
    {
        var services = new ServiceCollection();
        return (ProviderWrapper)(object)services.BuildServiceProvider();
    }
}

public sealed class ProviderWrapper
{
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(10, 41));
    }

    [Fact]
    public async Task BuildServiceProvider_DowncastFromInterfaceDisposedWrapper_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        var wrapper = (ProviderWrapper)(IServiceProvider)services.BuildServiceProvider();
        wrapper.Dispose();
    }
}

public sealed class ProviderWrapper : IServiceProvider, IDisposable
{
    public object GetService(Type serviceType) => null!;

    public void Dispose() { }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(10, 58));
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

    [Fact]
    public async Task ConditionalAccessBuild_DisposedInFinally_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Run(IServiceCollection? services)
    {
        var provider = services?.BuildServiceProvider();
        try
        {
            provider?.GetService(typeof(string));
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
    public async Task ConditionalAccessBuild_ReassignedPredeclaredThenDisposed_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Run(IServiceCollection? services)
    {
        ServiceProvider? provider = null;
        provider = services?.BuildServiceProvider();
        provider?.Dispose();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ConditionalAccessBuild_Returned_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public ServiceProvider? Create(IServiceCollection? services)
    {
        return services?.BuildServiceProvider();
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ConditionalAccessBuild_ArrowReturned_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public ServiceProvider? Create(IServiceCollection? services) => services?.BuildServiceProvider();
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ConditionalAccessBuild_NeverDisposed_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Run(IServiceCollection? services)
    {
        var provider = services?{|#0:.BuildServiceProvider()|};
        provider?.GetService(typeof(string));
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>
                .Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(0));
    }

    [Fact]
    public async Task ConditionalAccessBuild_UsingVar_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Run(IServiceCollection? services)
    {
        using var provider = services?.BuildServiceProvider();
        provider?.GetService(typeof(string));
    }
}";

        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TrackedLocal_ReturnedLater_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public IServiceProvider Create()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        services.AddSingleton<object>();
        return provider;
    }
}";
        // Returning the tracked local transfers ownership to the caller, exactly like
        // returning the creation expression directly.
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TrackedLocal_ReassignedBeforeReturn_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public IServiceProvider Create()
    {
        var services = new ServiceCollection();
        var provider = [|services.BuildServiceProvider()|];
        provider = services.BuildServiceProvider();
        return provider;
    }
}";
        // The returned value is the second provider; the first creation still leaks.
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateAndDispose_InsideSameLoopIteration_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(int count)
    {
        var services = new ServiceCollection();
        while (count-- > 0)
        {
            var provider = services.BuildServiceProvider();
            provider.Dispose();
        }
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateInLoop_ContinueBeforeDispose_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(int count)
    {
        var services = new ServiceCollection();
        while (count-- > 0)
        {
            var provider = [|services.BuildServiceProvider()|];
            if (count % 2 == 0)
            {
                continue;
            }

            provider.Dispose();
        }
    }
}";
        // The continue skips the dispose for that iteration — the same-construct
        // exemption must not hide it.
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TrackedLocal_ReturnedOnOneBranchOnly_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public IServiceProvider Create(bool giveAway)
    {
        var services = new ServiceCollection();
        var provider = [|services.BuildServiceProvider()|];
        if (giveAway)
        {
            return provider;
        }

        return null;
    }
}";
        // Ownership transfers only on the returning branch; the fall-through path drops
        // the provider undisposed (Codex review regression).
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TrackedLocal_ThrowBeforeReturn_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public IServiceProvider Create(bool fail)
    {
        var services = new ServiceCollection();
        var provider = [|services.BuildServiceProvider()|];
        if (fail)
        {
            throw new InvalidOperationException();
        }

        return provider;
    }
}";
        // The throw path leaks the provider before ownership transfers
        // (Codex review regression).
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TrackedLocal_DisposeThenThrowBeforeReturn_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public IServiceProvider Create(bool fail)
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        if (fail)
        {
            provider.Dispose();
            throw new InvalidOperationException();
        }

        return provider;
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TrackedLocal_UnrelatedConditionalDispose_ThrowBeforeReturn_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public IServiceProvider Create(bool cleanup, bool fail)
    {
        var services = new ServiceCollection();
        var provider = [|services.BuildServiceProvider()|];
        if (cleanup)
        {
            provider.Dispose();
        }

        if (fail)
        {
            throw new InvalidOperationException();
        }

        return provider;
    }
}";
        // The dispose sits in a branch the throw does not share: cleanup == false &&
        // fail == true still leaks (Codex review regression).
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TrackedLocal_NestedLoopBreakBeforeReturn_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public IServiceProvider Create(int count)
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        for (var i = 0; i < count; i++)
        {
            if (i == 1)
            {
                break;
            }
        }

        return provider;
    }
}";
        // The break exits the nested loop only; execution still reaches the return
        // (Codex review regression).
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TrackedLocal_AssignedInLoop_ReturnedAfterLoop_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public IServiceProvider Create(bool again)
    {
        var services = new ServiceCollection();
        IServiceProvider provider = null;
        while (again)
        {
            provider = [|services.BuildServiceProvider()|];
        }

        return provider;
    }
}";
        // Every iteration before the last overwrites and leaks a root provider; only the
        // final instance transfers to the caller (Codex review regression).
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateInSwitchSection_GotoCaseBeforeDispose_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(int kind)
    {
        var services = new ServiceCollection();
        switch (kind)
        {
            case 1:
                var provider = [|services.BuildServiceProvider()|];
                if (kind > 0)
                {
                    goto default;
                }

                provider.Dispose();
                break;
            default:
                break;
        }
    }
}";
        // The goto jumps past the dispose; the same-construct exemption must not hide it
        // (Codex review regression).
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateInLoop_ConditionalReturnBeforeDispose_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(int count, bool fail)
    {
        var services = new ServiceCollection();
        while (count-- > 0)
        {
            var provider = [|services.BuildServiceProvider()|];
            if (fail)
            {
                return;
            }

            provider.Dispose();
        }
    }
}";
        // The conditional return leaks the provider before the same-iteration dispose runs
        // (Codex review regression).
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CreateInLoop_DisposeThenReturn_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(int count, bool fail)
    {
        var services = new ServiceCollection();
        while (count-- > 0)
        {
            var provider = services.BuildServiceProvider();
            if (fail)
            {
                provider.Dispose();
                return;
            }

            provider.Dispose();
        }
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_ConditionalResultDisposed_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool create)
    {
        var services = new ServiceCollection();
        var provider = create ? services.BuildServiceProvider() : null;
        provider?.Dispose();
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ConditionalAccessBuild_CoalesceResultDisposed_NoDiagnostic()
    {
        var source = Usings + @"
#nullable enable

public class Program
{
    public void Main(ServiceCollection? services)
    {
        var provider = services?.BuildServiceProvider()
            ?? new ServiceCollection().BuildServiceProvider();
        provider.Dispose();
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_ConditionalResultReturned_NoDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public ServiceProvider CreateProvider(bool create, ServiceProvider fallback)
    {
        var services = new ServiceCollection();
        return create ? services.BuildServiceProvider() : fallback;
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ConditionalAccessBuild_CoalesceResultReturned_NoDiagnostic()
    {
        var source = Usings + @"
#nullable enable

public class Program
{
    public ServiceProvider CreateProvider(ServiceCollection? services)
    {
        return services?.BuildServiceProvider()
            ?? new ServiceCollection().BuildServiceProvider();
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_ConditionalResultDisposedOnlyOnUnrelatedBranch_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool create, bool cleanup)
    {
        var services = new ServiceCollection();
        var provider = create ? [|services.BuildServiceProvider()|] : null;
        if (cleanup)
        {
            provider?.Dispose();
        }
    }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_ConditionalUserDefinedConversionDisposedWrapper_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public void Main(bool create)
    {
        var services = new ServiceCollection();
        ProviderWrapper wrapper = create
            ? [|services.BuildServiceProvider()|]
            : new ProviderWrapper();
        wrapper.Dispose();
    }
}

public sealed class ProviderWrapper : IDisposable
{
    public static implicit operator ProviderWrapper(ServiceProvider provider) => new ProviderWrapper();

    public void Dispose() { }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BuildServiceProvider_ConditionalUserDefinedConversionReturned_ReportsDiagnostic()
    {
        var source = Usings + @"
public class Program
{
    public ProviderWrapper CreateProvider(bool create)
    {
        var services = new ServiceCollection();
        return create
            ? [|services.BuildServiceProvider()|]
            : new ProviderWrapper();
    }
}

public sealed class ProviderWrapper
{
    public static implicit operator ProviderWrapper(ServiceProvider provider) => new ProviderWrapper();
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ConditionalAccessBuild_CoalesceUserDefinedConversionDisposedWrapper_ReportsDiagnostic()
    {
        var source = Usings + @"
#nullable enable

public class Program
{
    public void Main(ServiceCollection? services)
    {
        ProviderWrapper wrapper = services?[|.BuildServiceProvider()|]
            ?? new ProviderWrapper();
        wrapper.Dispose();
    }
}

public sealed class ProviderWrapper : IDisposable
{
    public static implicit operator ProviderWrapper(ServiceProvider provider) => new ProviderWrapper();

    public void Dispose() { }
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ConditionalAccessBuild_CoalesceUserDefinedConversionReturned_ReportsDiagnostic()
    {
        var source = Usings + @"
#nullable enable

public class Program
{
    public ProviderWrapper CreateProvider(ServiceCollection? services)
    {
        return services?[|.BuildServiceProvider()|]
            ?? new ProviderWrapper();
    }
}

public sealed class ProviderWrapper
{
    public static implicit operator ProviderWrapper(ServiceProvider provider) => new ProviderWrapper();
}";
        await AnalyzerVerifier<DI014_RootProviderNotDisposedAnalyzer>.VerifyDiagnosticsAsync(source);
    }
}
