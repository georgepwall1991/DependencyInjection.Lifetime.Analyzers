using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects ASP.NET Core convention-based middleware whose constructor captures
/// a scoped or transient service. Middleware is activated by <c>ActivatorUtilities</c> at startup
/// via <c>app.UseMiddleware&lt;T&gt;()</c> and held in the request pipeline for the application's
/// lifetime, so any scoped or transient dependency captured in the constructor becomes a captive
/// dependency. The fix is to move the dependency to a parameter of <c>Invoke</c> or
/// <c>InvokeAsync</c>, which the pipeline resolves per-request from
/// <c>HttpContext.RequestServices</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI020_MiddlewareCaptiveDependencyAnalyzer : DiagnosticAnalyzer
{
    internal const string DependencyLifetimePropertyName = "DependencyLifetime";
    internal const string InvokeMethodNamePropertyName = "InvokeMethodName";
    internal const string ParameterNamePropertyName = "ParameterName";

    private readonly struct ReportedDiagnosticKey : System.IEquatable<ReportedDiagnosticKey>
    {
        public ReportedDiagnosticKey(
            INamedTypeSymbol middleware,
            ITypeSymbol dependencyType,
            object? key,
            bool isKeyed,
            string parameterName)
        {
            Middleware = middleware;
            DependencyType = dependencyType;
            Key = key;
            IsKeyed = isKeyed;
            ParameterName = parameterName;
        }

        public INamedTypeSymbol Middleware { get; }
        public ITypeSymbol DependencyType { get; }
        public object? Key { get; }
        public bool IsKeyed { get; }
        public string ParameterName { get; }

        public bool Equals(ReportedDiagnosticKey other)
        {
            return SymbolEqualityComparer.Default.Equals(Middleware, other.Middleware) &&
                   SymbolEqualityComparer.Default.Equals(DependencyType, other.DependencyType) &&
                   Equals(Key, other.Key) &&
                   IsKeyed == other.IsKeyed &&
                   ParameterName == other.ParameterName;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = SymbolEqualityComparer.Default.GetHashCode(Middleware);
                hashCode = (hashCode * 397) ^ SymbolEqualityComparer.Default.GetHashCode(DependencyType);
                hashCode = (hashCode * 397) ^ (Key?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ IsKeyed.GetHashCode();
                hashCode = (hashCode * 397) ^ ParameterName.GetHashCode();
                return hashCode;
            }
        }

        public override bool Equals(object? obj) =>
            obj is ReportedDiagnosticKey other && Equals(other);
    }

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.MiddlewareCaptiveDependency);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var registrationCollector = RegistrationCollector.Create(compilationContext.Compilation);
            if (registrationCollector is null)
            {
                return;
            }

            var wellKnownTypes = WellKnownTypes.Create(compilationContext.Compilation);
            if (wellKnownTypes is null)
            {
                return;
            }

            var lifetimeClassifier = new KnownServiceLifetimeClassifier(wellKnownTypes);
            var candidates = new ConcurrentDictionary<INamedTypeSymbol, byte>(SymbolEqualityComparer.Default);

            // First pass: collect all registrations.
            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => registrationCollector.AnalyzeInvocation(
                    (InvocationExpressionSyntax)syntaxContext.Node,
                    syntaxContext.SemanticModel),
                SyntaxKind.InvocationExpression);

            // Second pass: gather candidate middleware classes (dedup across partial declarations).
            // Skip metadata types - we can only diagnose source-defined middleware.
            compilationContext.RegisterSymbolAction(
                symbolContext =>
                {
                    if (symbolContext.Symbol is INamedTypeSymbol type &&
                        type.DeclaringSyntaxReferences.Length > 0 &&
                        MiddlewareDetection.IsConventionMiddlewareCandidate(type))
                    {
                        candidates.TryAdd(type, 0);
                    }
                },
                SymbolKind.NamedType);

            // Final pass: analyze candidates after registrations are fully collected.
            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeMiddlewareCandidates(
                    endContext,
                    candidates,
                    registrationCollector,
                    lifetimeClassifier,
                    wellKnownTypes));
        });
    }

    private static void AnalyzeMiddlewareCandidates(
        CompilationAnalysisContext context,
        ConcurrentDictionary<INamedTypeSymbol, byte> candidates,
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        WellKnownTypes wellKnownTypes)
    {
        var reportedDiagnostics = new HashSet<ReportedDiagnosticKey>();

        foreach (var middleware in candidates.Keys)
        {
            AnalyzeMiddleware(
                context,
                middleware,
                registrationCollector,
                lifetimeClassifier,
                wellKnownTypes,
                reportedDiagnostics);
        }
    }

    private static void AnalyzeMiddleware(
        CompilationAnalysisContext context,
        INamedTypeSymbol middleware,
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        WellKnownTypes wellKnownTypes,
        HashSet<ReportedDiagnosticKey> reportedDiagnostics)
    {
        var invokeMethod = MiddlewareDetection.GetInvokeMethod(middleware);
        if (invokeMethod is null)
        {
            return;
        }

        var invokeName = invokeMethod.Name;

        foreach (var constructor in ConstructorSelection.GetConstructorsToAnalyze(middleware))
        {
            foreach (var parameter in constructor.Parameters)
            {
                if (ShouldSkipParameter(parameter, wellKnownTypes))
                {
                    continue;
                }

                var parameterType = UnwrapEnumerableDependency(parameter.Type, out var isEnumerable);
                var serviceKey = KeyedServiceHelpers.GetServiceKey(
                    parameter,
                    inheritedKey: null,
                    hasInheritedKey: false,
                    inheritedKeyLiteral: null);

                if (serviceKey.IsUnknown ||
                    SyntaxValueHelpers.IsKeyedServiceAnyKey(serviceKey.Key))
                {
                    continue;
                }

                if (!TryGetCapturedShorterLifetime(
                        registrationCollector,
                        lifetimeClassifier,
                        parameterType,
                        serviceKey.Key,
                        serviceKey.IsKeyed,
                        isEnumerable,
                        out var dependencyLifetime))
                {
                    continue;
                }

                Report(
                    context,
                    middleware,
                    parameter,
                    parameterType,
                    dependencyLifetime,
                    invokeName,
                    serviceKey.Key,
                    serviceKey.IsKeyed,
                    reportedDiagnostics);
            }
        }
    }

    private static bool ShouldSkipParameter(IParameterSymbol parameter, WellKnownTypes wellKnownTypes)
    {
        var type = parameter.Type;

        if (MiddlewareDetection.IsRequestDelegateParameter(parameter))
        {
            return true;
        }

        // Provider / scope-factory parameters are DI011's territory and never scoped consumers.
        if (wellKnownTypes.IsServiceProvider(type) ||
            wellKnownTypes.IsServiceScopeFactory(type) ||
            wellKnownTypes.IsKeyedServiceProvider(type))
        {
            return true;
        }

        // Singleton framework abstractions.
        if (wellKnownTypes.IsLogger(type) ||
            wellKnownTypes.IsConfiguration(type))
        {
            return true;
        }

        return false;
    }

    private static ITypeSymbol UnwrapEnumerableDependency(ITypeSymbol dependencyType, out bool isEnumerableDependency)
    {
        isEnumerableDependency = false;
        if (dependencyType is INamedTypeSymbol { IsGenericType: true } namedType &&
            namedType.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            isEnumerableDependency = true;
            return namedType.TypeArguments[0];
        }

        return dependencyType;
    }

    private static bool TryGetCapturedShorterLifetime(
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        ITypeSymbol dependencyType,
        object? key,
        bool isKeyed,
        bool isEnumerableDependency,
        out ServiceLifetime dependencyLifetime)
    {
        if (isEnumerableDependency &&
            TryGetEnumerableShorterLifetime(
                registrationCollector,
                dependencyType,
                key,
                isKeyed,
                out dependencyLifetime))
        {
            return true;
        }

        var registeredLifetime = registrationCollector.GetLifetime(dependencyType, key, isKeyed);
        if (registeredLifetime is null &&
            lifetimeClassifier.TryGetLifetime(dependencyType, isKeyed, out var knownLifetime))
        {
            registeredLifetime = knownLifetime;
        }

        if (registeredLifetime is { } resolved &&
            IsShorterLifetimeThanSingleton(resolved))
        {
            dependencyLifetime = resolved;
            return true;
        }

        dependencyLifetime = default;
        return false;
    }

    private static bool TryGetEnumerableShorterLifetime(
        RegistrationCollector registrationCollector,
        ITypeSymbol dependencyType,
        object? key,
        bool isKeyed,
        out ServiceLifetime dependencyLifetime)
    {
        foreach (var registration in registrationCollector.AllRegistrations)
        {
            if (registration.IsKeyed != isKeyed ||
                !Equals(registration.Key, key))
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(registration.ServiceType, dependencyType) &&
                !MatchesOpenGeneric(registration.ServiceType, dependencyType))
            {
                continue;
            }

            if (IsShorterLifetimeThanSingleton(registration.Lifetime))
            {
                dependencyLifetime = registration.Lifetime;
                return true;
            }
        }

        dependencyLifetime = default;
        return false;
    }

    private static bool MatchesOpenGeneric(ITypeSymbol registrationServiceType, ITypeSymbol dependencyType)
    {
        if (dependencyType is not INamedTypeSymbol namedDependencyType ||
            !namedDependencyType.IsGenericType ||
            namedDependencyType.IsUnboundGenericType)
        {
            return false;
        }

        var openDependencyType = namedDependencyType.ConstructUnboundGenericType();
        return SymbolEqualityComparer.Default.Equals(registrationServiceType, openDependencyType);
    }

    private static bool IsShorterLifetimeThanSingleton(ServiceLifetime lifetime) =>
        lifetime is ServiceLifetime.Scoped or ServiceLifetime.Transient;

    private static void Report(
        CompilationAnalysisContext context,
        INamedTypeSymbol middleware,
        IParameterSymbol parameter,
        ITypeSymbol dependencyType,
        ServiceLifetime dependencyLifetime,
        string invokeMethodName,
        object? key,
        bool isKeyed,
        HashSet<ReportedDiagnosticKey> reportedDiagnostics)
    {
        var reportKey = new ReportedDiagnosticKey(
            middleware,
            dependencyType,
            key,
            isKeyed,
            parameter.Name);
        if (!reportedDiagnostics.Add(reportKey))
        {
            return;
        }

        var location = GetParameterLocation(parameter);
        if (location is null)
        {
            return;
        }

        var lifetimeName = dependencyLifetime.ToString().ToLowerInvariant();

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DependencyLifetimePropertyName, lifetimeName)
            .Add(InvokeMethodNamePropertyName, invokeMethodName)
            .Add(ParameterNamePropertyName, parameter.Name);

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.MiddlewareCaptiveDependency,
            location,
            additionalLocations: null,
            properties: properties,
            middleware.Name,
            lifetimeName,
            dependencyType.Name,
            invokeMethodName);

        context.ReportDiagnostic(diagnostic);
    }

    private static Location? GetParameterLocation(IParameterSymbol parameter)
    {
        foreach (var reference in parameter.DeclaringSyntaxReferences)
        {
            return reference.GetSyntax().GetLocation();
        }

        return parameter.Locations.FirstOrDefault();
    }
}
