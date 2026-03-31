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

    // ─── Existing tests (unchanged) ──────────────────────────────────────────────

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

    // ─── ServiceDescriptor.Describe(...) tests ───────────────────────────────────

    [Fact]
    public async Task ServiceDescriptor_Describe_Valid_NoDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.Add(ServiceDescriptor.Describe(typeof(IService), typeof(Service), ServiceLifetime.Singleton));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceDescriptor_Describe_Invalid_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.Add(ServiceDescriptor.Describe(typeof(IService), typeof(string), ServiceLifetime.Singleton));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("String", "IService"));
    }

    [Fact]
    public async Task ServiceDescriptor_New_Valid_NoDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.Add(new ServiceDescriptor(typeof(IService), typeof(Service), ServiceLifetime.Singleton));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceDescriptor_New_Invalid_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.Add(new ServiceDescriptor(typeof(IService), typeof(string), ServiceLifetime.Singleton));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("String", "IService"));
    }

    [Fact]
    public async Task ServiceDescriptor_SingletonFactoryMethod_Valid_NoDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.Add(ServiceDescriptor.Singleton(typeof(IService), typeof(Service)));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceDescriptor_SingletonFactoryMethod_Invalid_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.Add(ServiceDescriptor.Singleton(typeof(IService), typeof(string)));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("String", "IService"));
    }

    // ─── Keyed registration tests ────────────────────────────────────────────────

    [Fact]
    public async Task KeyedRegistration_Valid_NoDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddKeyedSingleton(typeof(IService), ""myKey"", typeof(Service));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.ReferenceAssembliesWithKeyedDi);
    }

    [Fact]
    public async Task KeyedRegistration_Invalid_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddKeyedSingleton(typeof(IService), ""myKey"", typeof(string));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.ReferenceAssembliesWithKeyedDi,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("String", "IService"));
    }

    [Fact]
    public async Task KeyedOpenGeneric_Valid_NoDiagnostic()
    {
        var source = Usings + @"
public interface IRepo<T> {}
public class Repo<T> : IRepo<T> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddKeyedSingleton(typeof(IRepo<>), ""myKey"", typeof(Repo<>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.ReferenceAssembliesWithKeyedDi);
    }

    [Fact]
    public async Task KeyedOpenGeneric_Invalid_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IRepo<T> {}
public class Repo<T> : IRepo<T> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddKeyedSingleton(typeof(IRepo<>), ""myKey"", typeof(List<>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsWithReferencesAsync(
            source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.ReferenceAssembliesWithKeyedDi,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("List", "IRepo"));
    }

    // ─── Open generic projection ordering / arity tests ──────────────────────────

    [Fact]
    public async Task OpenGeneric_SwappedProjection_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IFoo<T1, T2> {}
public class Foo<T2, T1> : IFoo<T1, T2> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IFoo<,>), typeof(Foo<,>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("Foo", "IFoo"));
    }

    [Fact]
    public async Task OpenGeneric_CorrectProjection_NoDiagnostic()
    {
        var source = Usings + @"
public interface IFoo<T1, T2> {}
public class Foo<T1, T2> : IFoo<T1, T2> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IFoo<,>), typeof(Foo<,>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGeneric_ArityMismatch_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IFoo<T1, T2> {}
public class Foo<T> { }

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IFoo<,>), typeof(Foo<>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("Foo", "IFoo"));
    }

    [Fact]
    public async Task OpenGeneric_PartialClosureMismatch_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IFoo<T1, T2> {}
public class Foo<T> : IFoo<T, string> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // IFoo<,> expects 2 open params; Foo<> only provides 1, so IFoo<T, string> is a partial match
        // but the service registration is fully open — this is a mismatch.
        services.AddSingleton(typeof(IFoo<,>), typeof(Foo<>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(15, 9)
                .WithArguments("Foo", "IFoo"));
    }

    [Fact]
    public async Task OpenGeneric_PartialClosure_ValidMatch_NoDiagnostic()
    {
        var source = Usings + @"
public interface IFoo<T> {}
public class Foo<T> : IFoo<T> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IFoo<>), typeof(Foo<>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGeneric_ViaBaseType_Valid_NoDiagnostic()
    {
        var source = Usings + @"
public abstract class Base<T> {}
public class Derived<T> : Base<T> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(Base<>), typeof(Derived<>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGeneric_ViaBaseType_Invalid_ReportsDiagnostic()
    {
        var source = Usings + @"
public abstract class Base<T> {}
public class Derived<T> : Base<string> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(Base<>), typeof(Derived<>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("Derived", "Base"));
    }

    [Fact]
    public async Task OpenGeneric_ThreeTypeParams_Swapped_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IBar<T1, T2, T3> {}
public class Bar<T1, T3, T2> : IBar<T1, T2, T3> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IBar<,,>), typeof(Bar<,,>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("Bar", "IBar"));
    }

    [Fact]
    public async Task OpenGeneric_ThreeTypeParams_Correct_NoDiagnostic()
    {
        var source = Usings + @"
public interface IBar<T1, T2, T3> {}
public class Bar<T1, T2, T3> : IBar<T1, T2, T3> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IBar<,,>), typeof(Bar<,,>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    // ─── Closed generic assignability tests ──────────────────────────────────────

    [Fact]
    public async Task ClosedGeneric_ViaInterface_Valid_NoDiagnostic()
    {
        var source = Usings + @"
public interface IService<T> {}
public class Service<T> : IService<T> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IService<string>), typeof(Service<string>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ClosedGeneric_ViaBaseClass_Valid_NoDiagnostic()
    {
        var source = Usings + @"
public abstract class Base<T> {}
public class Derived<T> : Base<T> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(Base<int>), typeof(Derived<int>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ClosedGeneric_Mismatch_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IService<T> {}
public class Service<T> : IService<T> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IService<string>), typeof(Service<int>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("Service", "IService"));
    }

    // ─── Factory and instance registration tests (should stay silent) ────────────

    [Fact]
    public async Task FactoryRegistration_NoDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IService), sp => new Service());
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GenericFactoryRegistration_NoDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IService>(sp => new Service());
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceDescriptor_Factory_NoDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.Add(ServiceDescriptor.Describe(typeof(IService), sp => new Service(), ServiceLifetime.Singleton));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InstanceRegistration_NoDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IService), new Service());
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task InstanceRegistration_Invalid_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IService), new object());
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("Object", "IService"));
    }

    [Fact]
    public async Task ServiceDescriptor_InstanceRegistration_NoDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.Add(ServiceDescriptor.Singleton(typeof(IService), new Service()));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceDescriptor_InstanceRegistration_Invalid_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.Add(ServiceDescriptor.Singleton(typeof(IService), new object()));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("Object", "IService"));
    }

    [Fact]
    public async Task TryAddSingleton_Valid_NoDiagnostic()
    {
        var source = Usings + @"
using Microsoft.Extensions.DependencyInjection.Extensions;
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton(typeof(IService), typeof(Service));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task TryAddSingleton_Invalid_ReportsDiagnostic()
    {
        var source = Usings + @"
using Microsoft.Extensions.DependencyInjection.Extensions;
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton(typeof(IService), typeof(string));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(14, 9)
                .WithArguments("String", "IService"));
    }

    [Fact]
    public async Task AddSingleton_ServiceOnly_NoDiagnostic()
    {
        var source = Usings + @"
public class Service {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<Service>();
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    // ─── Base type / inheritance tests ───────────────────────────────────────────

    [Fact]
    public async Task ImplementationInheritsBaseClass_NoDiagnostic()
    {
        var source = Usings + @"
public abstract class Base {}
public class Derived : Base {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(Base), typeof(Derived));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ImplementationImplementsInterface_NoDiagnostic()
    {
        var source = Usings + @"
public interface IService {}
public class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IService), typeof(Service));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ImplementationImplementsMultipleInterfaces_NoDiagnostic()
    {
        var source = Usings + @"
public interface IService1 {}
public interface IService2 {}
public class Service : IService1, IService2 {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IService1), typeof(Service));
        services.AddSingleton(typeof(IService2), typeof(Service));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    // ─── Open generic via non-interface base with different type param mapping ───

    [Fact]
    public async Task OpenGeneric_BaseTypeWithDifferentParamOrder_ReportsDiagnostic()
    {
        var source = Usings + @"
public abstract class Base<T1, T2> {}
public class Derived<T2, T1> : Base<T1, T2> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(Base<,>), typeof(Derived<,>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("Derived", "Base"));
    }

    [Fact]
    public async Task OpenGeneric_BaseTypeWithCorrectParamOrder_NoDiagnostic()
    {
        var source = Usings + @"
public abstract class Base<T1, T2> {}
public class Derived<T1, T2> : Base<T1, T2> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(Base<,>), typeof(Derived<,>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    // ─── Open generic: implementation type is not generic ────────────────────────

    [Fact]
    public async Task OpenGeneric_ServiceIsOpen_ImplementationIsNotGeneric_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IFoo<T> {}
public class Foo : IFoo<int> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IFoo<>), typeof(Foo));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("Foo", "IFoo"));
    }

    // ─── Open generic: service is not generic but implementation is ───────────────

    [Fact]
    public async Task OpenGeneric_ServiceIsNotGeneric_ImplementationIsGeneric_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IFoo {}
public class Foo<T> : IFoo {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IFoo), typeof(Foo<>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("Foo", "IFoo"));
    }

    // ─── Open generic: service is open but implementation is closed generic ───────

    [Fact]
    public async Task OpenGenericService_ClosedGenericImpl_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IFoo<T> {}
public class Foo<T> : IFoo<T> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IFoo<>), typeof(Foo<int>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("Foo", "IFoo"));
    }

    // ─── Open generic: closed generic service with open generic impl ──────────────

    [Fact]
    public async Task ClosedGenericService_OpenGenericImpl_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IFoo<T> {}
public class Foo<T> : IFoo<T> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IFoo<string>), typeof(Foo<>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("Foo", "IFoo"));
    }

    // ─── Open generic: implementation has more type params than service ──────────

    [Fact]
    public async Task OpenGeneric_ImplHasMoreTypeParams_ReportsDiagnostic()
    {
        var source = Usings + @"
public interface IFoo<T> {}
public class Foo<T1, T2> : IFoo<T1> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IFoo<>), typeof(Foo<,>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyDiagnosticsAsync(source,
            AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>
                .Diagnostic(DiagnosticDescriptors.ImplementationTypeMismatch)
                .WithLocation(13, 9)
                .WithArguments("Foo", "IFoo"));
    }

    // ─── Open generic: nested generic interface ──────────────────────────────────

    [Fact]
    public async Task OpenGeneric_NestedGenericInterface_NoDiagnostic()
    {
        var source = Usings + @"
public interface IOuter<T> { interface IInner { } }
public class Outer<T> : IOuter<T>
{
    public class Inner : IOuter<T>.IInner { }
}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // This is a complex scenario — the analyzer should not crash
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    // ─── Open generic: implementation implements service through multiple inheritance chain ──

    [Fact]
    public async Task OpenGeneric_DeepInheritanceChain_NoDiagnostic()
    {
        var source = Usings + @"
public interface IFoo<T> {}
public abstract class FooBase<T> : IFoo<T> {}
public class FooImpl<T> : FooBase<T> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IFoo<>), typeof(FooImpl<>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task OpenGeneric_DeepInheritanceChain_SwappedParams_NoDiagnostic()
    {
        // FooImpl<T1, T2> : FooBase<T2, T1> : IFoo<T1, T2>
        // At runtime, FooImpl<string, int> -> FooBase<int, string> -> IFoo<string, int>
        // This IS a valid mapping — the names are misleading but the ordinals work out.
        var source = Usings + @"
public interface IFoo<T1, T2> {}
public abstract class FooBase<T2, T1> : IFoo<T1, T2> {}
public class FooImpl<T1, T2> : FooBase<T2, T1> {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IFoo<,>), typeof(FooImpl<,>));
    }
}";
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    // ─── Edge case: abstract implementation (DI018 territory, but DI013 should stay silent) ──

    [Fact]
    public async Task AbstractImplementation_NoDiagnostic_DI013()
    {
        var source = Usings + @"
public interface IService {}
public abstract class Service : IService {}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(IService), typeof(Service));
    }
}";
        // DI013 should not fire — Service does implement IService.
        // DI018 would fire for non-instantiable, but that's a different rule.
        await AnalyzerVerifier<DI013_ImplementationTypeMismatchAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
}
