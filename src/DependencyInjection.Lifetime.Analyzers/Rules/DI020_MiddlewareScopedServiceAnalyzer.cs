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
/// Analyzer that detects scoped services injected into Middleware constructors.
/// Middleware are typically singletons, so injecting scoped services into the constructor
/// captures them for the application lifetime.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI020_MiddlewareScopedServiceAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.MiddlewareScopedService);

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

            var middlewareUsages = new ConcurrentBag<MiddlewareUsage>();

            // Identify classes used in UseMiddleware, capturing any explicit activation arguments
            // the call supplies (UseMiddleware forwards them to ActivatorUtilities).
            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
                    registrationCollector.AnalyzeInvocation(invocation, syntaxContext.SemanticModel);

                    if (TryGetMiddlewareUsage(invocation, syntaxContext.SemanticModel, out var usage))
                    {
                        middlewareUsages.Add(usage);
                    }
                },
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeMiddlewareLifetime(
                    endContext,
                    registrationCollector,
                    wellKnownTypes,
                    middlewareUsages.ToImmutableArray()));
        });
    }

    private static bool TryGetMiddlewareUsage(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out MiddlewareUsage usage)
    {
        usage = default;
        var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null || symbol.Name != "UseMiddleware")
        {
            return false;
        }

        // Must be called on IApplicationBuilder, IEndpointRouteBuilder, or IConventionBuilder.
        var originalSymbol = symbol.ReducedFrom ?? symbol;
        var receiverType = symbol.ReducedFrom is not null
            ? originalSymbol.Parameters.FirstOrDefault()?.Type
            : invocation.Expression is MemberAccessExpressionSyntax memberAccess
                ? semanticModel.GetTypeInfo(memberAccess.Expression).Type
                : null;
        if (receiverType is null)
        {
            return false;
        }

        var ns = receiverType.ContainingNamespace?.ToString();
        if (ns is null)
        {
            return false;
        }

        bool isAspnetBuilder = (receiverType.Name == "IApplicationBuilder" && ns == "Microsoft.AspNetCore.Builder") ||
                               (receiverType.Name == "IEndpointRouteBuilder" && ns == "Microsoft.AspNetCore.Routing") ||
                               (receiverType.Name == "IConventionBuilder" && ns == "Microsoft.AspNetCore.Builder");

        if (!isAspnetBuilder)
        {
            return false;
        }

        INamedTypeSymbol? middlewareType;
        IEnumerable<ArgumentSyntax> explicitArguments;
        if (symbol.IsGenericMethod)
        {
            middlewareType = symbol.TypeArguments[0] as INamedTypeSymbol;
            explicitArguments = invocation.ArgumentList.Arguments;
        }
        else if (invocation.ArgumentList.Arguments.Count > 0 &&
                 invocation.ArgumentList.Arguments[0].Expression is TypeOfExpressionSyntax typeOfExpression)
        {
            middlewareType = semanticModel.GetTypeInfo(typeOfExpression.Type).Type as INamedTypeSymbol;
            explicitArguments = invocation.ArgumentList.Arguments.Skip(1);
        }
        else
        {
            return false;
        }

        if (middlewareType is null)
        {
            return false;
        }

        var argumentTypes = explicitArguments
            .Select(argument => semanticModel.GetTypeInfo(argument.Expression).Type)
            .ToImmutableArray();
        usage = new MiddlewareUsage(middlewareType, argumentTypes);
        return true;
    }

    private static void AnalyzeMiddlewareLifetime(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        ImmutableArray<MiddlewareUsage> middlewareUsages)
    {
        var imiddlewareType = context.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IMiddleware");
        var requestDelegateType = context.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.RequestDelegate");

        // Conventional middleware is constructed once from the root provider, so any constructor
        // dependency whose activation graph reaches a scoped service is captured for the application
        // lifetime -- the same root-scoped capture DI019 detects. Reuse its hardened graph walker so
        // both direct and transitive scoped captures are reported consistently and without the
        // false positives that ad-hoc lifetime checks risk.
        var scopedGraph = new ScopedDependencyGraph(registrationCollector, wellKnownTypes, context.Compilation);
        var resolutionEngine = new DependencyResolutionEngine(registrationCollector, wellKnownTypes);

        // Collect every explicit-argument shape each middleware type is activated with: different
        // UseMiddleware calls can pass different argument types (even with the same arity) and so
        // select different constructors.
        var argumentShapesByType = new Dictionary<INamedTypeSymbol, List<ImmutableArray<ITypeSymbol?>>>(SymbolEqualityComparer.Default);
        foreach (var usage in middlewareUsages)
        {
            if (!argumentShapesByType.TryGetValue(usage.Type, out var shapes))
            {
                shapes = new List<ImmutableArray<ITypeSymbol?>>();
                argumentShapesByType[usage.Type] = shapes;
            }

            shapes.Add(usage.ArgumentTypes);
        }

        foreach (var pair in argumentShapesByType)
        {
            var type = pair.Key;
            if (!MiddlewareHelpers.IsMiddlewareClass(type))
            {
                continue;
            }

            // Skip factory-based middleware (IMiddleware) as they are resolved per request and can be scoped.
            if (imiddlewareType is not null && type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, imiddlewareType)))
            {
                continue;
            }

            // ASP.NET Core activates middleware through ActivatorUtilities, which picks the greediest
            // constructor whose parameters are all satisfiable from DI plus the supplied arguments.
            // Each observed argument shape can select a different constructor, so consider them all and
            // analyze each distinct selected constructor once (so non-selectable overloads stay quiet
            // and no real capture is hidden by argument-shape ordering).
            var selectedConstructors = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var argumentTypes in pair.Value)
            {
                var selected = SelectActivatedConstructor(
                    type, argumentTypes, resolutionEngine, requestDelegateType, context.Compilation);
                if (selected is not null)
                {
                    selectedConstructors.Add(selected);
                }
            }

            foreach (var constructor in selectedConstructors)
            {
                AnalyzeConstructorDependencies(context, type, constructor, scopedGraph, requestDelegateType);
            }
        }
    }

    private static void AnalyzeConstructorDependencies(
        CompilationAnalysisContext context,
        INamedTypeSymbol middlewareType,
        IMethodSymbol constructor,
        ScopedDependencyGraph scopedGraph,
        INamedTypeSymbol? requestDelegateType)
    {
        foreach (var parameter in constructor.Parameters)
        {
            if (requestDelegateType is not null &&
                SymbolEqualityComparer.Default.Equals(parameter.Type, requestDelegateType))
            {
                continue;
            }

            // An `IEnumerable<T>` parameter resolves to every registration of T, so unwrap it and
            // ask the graph to inspect the element registrations -- otherwise a middleware that
            // injects `IEnumerable<IScopedService>` would capture the scoped elements undetected.
            var dependencyType = UnwrapEnumerableDependency(parameter.Type, out var isEnumerable);
            var (key, isKeyed) = KeyedServiceHelpers.GetServiceKey(parameter);
            if (!scopedGraph.TryFindScopedDependency(
                    dependencyType,
                    key,
                    isKeyed,
                    isEnumerableRequest: isEnumerable,
                    out var scopedMatch))
            {
                continue;
            }

            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.MiddlewareScopedService,
                parameter.Locations[0],
                middlewareType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                scopedMatch.ScopedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            context.ReportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// Mirrors <c>ActivatorUtilities</c> constructor selection for middleware: among the candidate
    /// constructors, keep those whose parameters are all either container-supplied or coverable by a
    /// distinct explicit <c>UseMiddleware</c> argument, and return the greediest such constructor.
    /// </summary>
    private static IMethodSymbol? SelectActivatedConstructor(
        INamedTypeSymbol middlewareType,
        ImmutableArray<ITypeSymbol?> suppliedArgumentTypes,
        DependencyResolutionEngine resolutionEngine,
        INamedTypeSymbol? requestDelegateType,
        Compilation compilation)
    {
        IMethodSymbol? selected = null;
        foreach (var constructor in ConstructorSelection.GetConstructorsToAnalyze(middlewareType))
        {
            if (!IsConstructorSelectable(constructor, suppliedArgumentTypes, resolutionEngine, requestDelegateType, compilation))
            {
                continue;
            }

            if (selected is null || constructor.Parameters.Length > selected.Parameters.Length)
            {
                selected = constructor;
            }
        }

        return selected;
    }

    private static bool IsConstructorSelectable(
        IMethodSymbol constructor,
        ImmutableArray<ITypeSymbol?> suppliedArgumentTypes,
        DependencyResolutionEngine resolutionEngine,
        INamedTypeSymbol? requestDelegateType,
        Compilation compilation)
    {
        var parameters = constructor.Parameters;
        var filledByArgument = new bool[parameters.Length];

        // ActivatorUtilities binds the explicit UseMiddleware arguments to constructor parameters
        // first -- in order, to the next assignable unfilled parameter -- and only then fills the
        // remaining parameters from DI. Every supplied argument must find a parameter, or the
        // constructor is not a candidate.
        foreach (var argumentType in suppliedArgumentTypes)
        {
            var matched = -1;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (!filledByArgument[i] &&
                    IsArgumentAssignableToParameter(argumentType, parameters[i].Type, compilation))
                {
                    matched = i;
                    break;
                }
            }

            if (matched < 0)
            {
                return false;
            }

            filledByArgument[matched] = true;
        }

        // Every parameter not bound to an explicit argument must be supplied by the container.
        for (var i = 0; i < parameters.Length; i++)
        {
            if (!filledByArgument[i] &&
                !IsContainerSuppliedParameter(parameters[i], resolutionEngine, requestDelegateType))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsContainerSuppliedParameter(
        IParameterSymbol parameter,
        DependencyResolutionEngine resolutionEngine,
        INamedTypeSymbol? requestDelegateType)
    {
        if (parameter.HasExplicitDefaultValue || parameter.IsOptional)
        {
            return true;
        }

        if (requestDelegateType is not null &&
            SymbolEqualityComparer.Default.Equals(parameter.Type, requestDelegateType))
        {
            return true;
        }

        var (key, isKeyed) = KeyedServiceHelpers.GetServiceKey(parameter);
        return resolutionEngine.ResolveServiceRequest(
                parameter.Type,
                key,
                isKeyed,
                assumeFrameworkServicesRegistered: true)
            .IsResolvable;
    }

    private static bool IsArgumentAssignableToParameter(
        ITypeSymbol? argumentType,
        ITypeSymbol parameterType,
        Compilation compilation)
    {
        // An argument whose type could not be resolved is treated as a wildcard so a genuine scoped
        // capture is never hidden by an unrecognized argument expression.
        if (argumentType is null)
        {
            return true;
        }

        return SymbolEqualityComparer.Default.Equals(argumentType, parameterType) ||
               compilation.HasImplicitConversion(argumentType, parameterType);
    }

    private readonly struct MiddlewareUsage
    {
        public MiddlewareUsage(INamedTypeSymbol type, ImmutableArray<ITypeSymbol?> argumentTypes)
        {
            Type = type;
            ArgumentTypes = argumentTypes;
        }

        public INamedTypeSymbol Type { get; }

        public ImmutableArray<ITypeSymbol?> ArgumentTypes { get; }
    }

    private static ITypeSymbol UnwrapEnumerableDependency(ITypeSymbol type, out bool isEnumerable)
    {
        isEnumerable = false;
        if (type is INamedTypeSymbol { Name: "IEnumerable", IsGenericType: true } namedType &&
            namedType.ContainingNamespace.ToDisplayString() == "System.Collections.Generic")
        {
            isEnumerable = true;
            return namedType.TypeArguments[0];
        }

        return type;
    }
}
