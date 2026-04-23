using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

/// <summary>
/// Deterministic stress tests that verify the shared analysis pipeline completes
/// within a generous timeout for large registration sets. These tests exist as a
/// regression gate, not as a precision benchmark.
/// </summary>
public class PerformanceRegressionTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceRegressionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Generates 200 service registrations with 2-3 constructor dependencies each
    /// (~600 total types) and verifies DI015 completes within a generous timeout.
    /// This catches quadratic or worse regressions in the shared resolution engine.
    /// </summary>
    [Fact]
    public async Task LargeRegistrationSet_200Services_DI015CompletesWithinTimeout()
    {
        const int serviceCount = 200;
        var source = GenerateLargeRegistrationSource(serviceCount, depsPerService: 2);

        var sw = Stopwatch.StartNew();
        // No diagnostics expected — all deps are registered
        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
        sw.Stop();

        _output.WriteLine($"DI015 analyzed {serviceCount} registrations in {sw.ElapsedMilliseconds}ms");
        Assert.True(sw.ElapsedMilliseconds < 30_000,
            $"DI015 took {sw.ElapsedMilliseconds}ms for {serviceCount} registrations, exceeding 30s timeout");
    }

    /// <summary>
    /// Generates diamond dependency patterns at scale where many services share the
    /// same transitive dependency chain. Verifies resolution caching prevents explosion.
    /// </summary>
    [Fact]
    public async Task DependencyResolution_DiamondPattern_CompletesWithinTimeout()
    {
        const int serviceCount = 100;
        var source = GenerateDiamondDependencySource(serviceCount);

        var sw = Stopwatch.StartNew();
        await AnalyzerVerifier<DI015_UnresolvableDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
        sw.Stop();

        _output.WriteLine($"DI015 diamond pattern ({serviceCount} services) completed in {sw.ElapsedMilliseconds}ms");
        Assert.True(sw.ElapsedMilliseconds < 30_000,
            $"Diamond pattern took {sw.ElapsedMilliseconds}ms, exceeding 30s timeout");
    }

    /// <summary>
    /// Verifies DI017 cycle detection scales with large registration sets that
    /// have no cycles (all linear chains). This catches regressions in the
    /// cycle detection walk.
    /// </summary>
    [Fact]
    public async Task CycleDetection_LargeNoCycleSet_CompletesWithinTimeout()
    {
        const int serviceCount = 200;
        var source = GenerateLargeRegistrationSource(serviceCount, depsPerService: 2);

        var sw = Stopwatch.StartNew();
        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
        sw.Stop();

        _output.WriteLine($"DI017 analyzed {serviceCount} registrations in {sw.ElapsedMilliseconds}ms");
        Assert.True(sw.ElapsedMilliseconds < 30_000,
            $"DI017 took {sw.ElapsedMilliseconds}ms, exceeding 30s timeout");
    }

    /// <summary>
    /// Verifies DI017 also scales when many registrations share the same
    /// transitive dependencies. This catches regressions that revisit the
    /// same known-safe graph tails for each root registration.
    /// </summary>
    [Fact]
    public async Task CycleDetection_DiamondPattern_CompletesWithinTimeout()
    {
        const int serviceCount = 150;
        var source = GenerateDiamondDependencySource(serviceCount);

        var sw = Stopwatch.StartNew();
        await AnalyzerVerifier<DI017_CircularDependencyAnalyzer>.VerifyNoDiagnosticsAsync(source);
        sw.Stop();

        _output.WriteLine($"DI017 diamond pattern ({serviceCount} services) completed in {sw.ElapsedMilliseconds}ms");
        Assert.True(sw.ElapsedMilliseconds < 30_000,
            $"DI017 diamond pattern took {sw.ElapsedMilliseconds}ms, exceeding 30s timeout");
    }

    [Fact]
    public async Task UseAfterDispose_ManyExecutableRoots_CompletesWithinTimeout()
    {
        const int methodCount = 150;
        var source = GenerateDI004ManyExecutableRootsSource(methodCount);

        var sw = Stopwatch.StartNew();
        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
        sw.Stop();

        _output.WriteLine($"DI004 analyzed {methodCount} executable roots in {sw.ElapsedMilliseconds}ms");
        Assert.True(sw.ElapsedMilliseconds < 30_000,
            $"DI004 took {sw.ElapsedMilliseconds}ms for {methodCount} executable roots, exceeding 30s timeout");
    }

    /// <summary>
    /// Generates source with N services, each depending on a subset of previously
    /// defined services (no cycles). Services are named Service0..ServiceN-1.
    /// </summary>
    private static string GenerateLargeRegistrationSource(int serviceCount, int depsPerService)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();

        // Generate interfaces and implementations
        for (var i = 0; i < serviceCount; i++)
        {
            sb.AppendLine($"public interface IService{i} {{ }}");
            sb.Append($"public class Service{i} : IService{i} {{ ");

            // Add constructor with dependencies on earlier services
            var depCount = Math.Min(depsPerService, i);
            if (depCount > 0)
            {
                sb.Append($"public Service{i}(");
                for (var d = 0; d < depCount; d++)
                {
                    if (d > 0) sb.Append(", ");
                    sb.Append($"IService{i - 1 - d} d{d}");
                }
                sb.Append(") { } ");
            }

            sb.AppendLine("}");
        }

        // Generate registrations
        sb.AppendLine();
        sb.AppendLine("public class Startup");
        sb.AppendLine("{");
        sb.AppendLine("    public void ConfigureServices(IServiceCollection services)");
        sb.AppendLine("    {");

        for (var i = 0; i < serviceCount; i++)
        {
            sb.AppendLine($"        services.AddScoped<IService{i}, Service{i}>();");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GenerateDI004ManyExecutableRootsSource(int methodCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("public interface IMyService { void DoWork(); }");
        sb.AppendLine("public sealed class MyService : IMyService { public void DoWork() { } }");
        sb.AppendLine("public sealed class Runner");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly IServiceScopeFactory _scopeFactory;");
        sb.AppendLine("    public Runner(IServiceScopeFactory scopeFactory) { _scopeFactory = scopeFactory; }");

        for (var i = 0; i < methodCount; i++)
        {
            sb.AppendLine($"    public void Run{i}()");
            sb.AppendLine("    {");
            sb.AppendLine("        using var scope = _scopeFactory.CreateScope();");
            sb.AppendLine("        var provider = scope.ServiceProvider;");
            sb.AppendLine("        var service = provider.GetRequiredService<IMyService>();");
            sb.AppendLine("        service.DoWork();");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        sb.AppendLine("public sealed class Startup");
        sb.AppendLine("{");
        sb.AppendLine("    public void ConfigureServices(IServiceCollection services)");
        sb.AppendLine("    {");
        sb.AppendLine("        services.AddScoped<IMyService, MyService>();");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a diamond pattern where many top-level services all share the same
    /// set of base dependencies, creating heavy resolution fan-in.
    /// </summary>
    private static string GenerateDiamondDependencySource(int topLevelCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();

        // 5 shared base services
        for (var i = 0; i < 5; i++)
        {
            sb.AppendLine($"public interface IBase{i} {{ }}");
            sb.AppendLine($"public class Base{i} : IBase{i} {{ }}");
        }

        // 10 mid-level services depending on bases
        for (var i = 0; i < 10; i++)
        {
            sb.AppendLine($"public interface IMid{i} {{ }}");
            sb.AppendLine($"public class Mid{i} : IMid{i} {{");
            sb.AppendLine($"    public Mid{i}(IBase{i % 5} b) {{ }}");
            sb.AppendLine("}");
        }

        // N top-level services each depending on 3 mid-level services
        for (var i = 0; i < topLevelCount; i++)
        {
            sb.AppendLine($"public interface ITop{i} {{ }}");
            sb.AppendLine($"public class Top{i} : ITop{i} {{");
            sb.AppendLine($"    public Top{i}(IMid{i % 10} m1, IMid{(i + 3) % 10} m2, IMid{(i + 7) % 10} m3) {{ }}");
            sb.AppendLine("}");
        }

        // Registrations
        sb.AppendLine();
        sb.AppendLine("public class Startup");
        sb.AppendLine("{");
        sb.AppendLine("    public void ConfigureServices(IServiceCollection services)");
        sb.AppendLine("    {");

        for (var i = 0; i < 5; i++)
            sb.AppendLine($"        services.AddSingleton<IBase{i}, Base{i}>();");
        for (var i = 0; i < 10; i++)
            sb.AppendLine($"        services.AddScoped<IMid{i}, Mid{i}>();");
        for (var i = 0; i < topLevelCount; i++)
            sb.AppendLine($"        services.AddTransient<ITop{i}, Top{i}>();");

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
