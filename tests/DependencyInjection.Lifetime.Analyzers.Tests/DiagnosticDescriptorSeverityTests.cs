using DependencyInjection.Lifetime.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests;

public class DiagnosticDescriptorSeverityTests
{
    [Theory]
    [InlineData("DI001", DiagnosticSeverity.Warning)]
    [InlineData("DI002", DiagnosticSeverity.Warning)]
    [InlineData("DI003", DiagnosticSeverity.Warning)]
    [InlineData("DI004", DiagnosticSeverity.Warning)]
    [InlineData("DI005", DiagnosticSeverity.Warning)]
    [InlineData("DI006", DiagnosticSeverity.Warning)]
    [InlineData("DI007", DiagnosticSeverity.Info)]
    [InlineData("DI008", DiagnosticSeverity.Warning)]
    [InlineData("DI009", DiagnosticSeverity.Warning)]
    [InlineData("DI010", DiagnosticSeverity.Info)]
    [InlineData("DI011", DiagnosticSeverity.Info)]
    [InlineData("DI012", DiagnosticSeverity.Info)]
    [InlineData("DI013", DiagnosticSeverity.Error)]
    [InlineData("DI014", DiagnosticSeverity.Warning)]
    [InlineData("DI015", DiagnosticSeverity.Warning)]
    [InlineData("DI016", DiagnosticSeverity.Warning)]
    public void DefaultSeverity_MatchesNoiseBudget(string diagnosticId, DiagnosticSeverity expectedSeverity)
    {
        var descriptor = GetDescriptor(diagnosticId);

        Assert.Equal(expectedSeverity, descriptor.DefaultSeverity);
    }

    private static DiagnosticDescriptor GetDescriptor(string diagnosticId) => diagnosticId switch
    {
        DiagnosticIds.ScopeMustBeDisposed => DiagnosticDescriptors.ScopeMustBeDisposed,
        DiagnosticIds.ScopedServiceEscapes => DiagnosticDescriptors.ScopedServiceEscapes,
        DiagnosticIds.CaptiveDependency => DiagnosticDescriptors.CaptiveDependency,
        DiagnosticIds.UseAfterScopeDisposed => DiagnosticDescriptors.UseAfterScopeDisposed,
        DiagnosticIds.AsyncScopeRequired => DiagnosticDescriptors.AsyncScopeRequired,
        DiagnosticIds.StaticProviderCache => DiagnosticDescriptors.StaticProviderCache,
        DiagnosticIds.ServiceLocatorAntiPattern => DiagnosticDescriptors.ServiceLocatorAntiPattern,
        DiagnosticIds.DisposableTransient => DiagnosticDescriptors.DisposableTransient,
        DiagnosticIds.OpenGenericLifetimeMismatch => DiagnosticDescriptors.OpenGenericLifetimeMismatch,
        DiagnosticIds.ConstructorOverInjection => DiagnosticDescriptors.ConstructorOverInjection,
        DiagnosticIds.ServiceProviderInjection => DiagnosticDescriptors.ServiceProviderInjection,
        DiagnosticIds.TryAddIgnored => DiagnosticDescriptors.TryAddIgnored,
        DiagnosticIds.ImplementationTypeMismatch => DiagnosticDescriptors.ImplementationTypeMismatch,
        DiagnosticIds.RootProviderNotDisposed => DiagnosticDescriptors.RootProviderNotDisposed,
        DiagnosticIds.UnresolvableDependency => DiagnosticDescriptors.UnresolvableDependency,
        DiagnosticIds.BuildServiceProviderMisuse => DiagnosticDescriptors.BuildServiceProviderMisuse,
        _ => throw new System.ArgumentOutOfRangeException(nameof(diagnosticId), diagnosticId, null)
    };
}
