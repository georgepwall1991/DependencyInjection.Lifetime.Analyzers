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
/// Analyzer that detects hosted services creating an IServiceScope (or resolving a scoped
/// service) once before their long-running execution loop instead of once per iteration.
/// The hoisted scope keeps the same scoped instances (DbContexts, units of work) alive for
/// the whole process lifetime.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI024_HostedServiceScopePerIterationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.HostedServiceScopePerIteration,
            DiagnosticDescriptors.HostedServiceScopedServicePerIteration);

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

            var hoistedScopes = new ConcurrentBag<HoistedScopeCandidate>();
            var hoistedServices = new ConcurrentBag<HoistedServiceCandidate>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => registrationCollector.AnalyzeInvocation(
                    (InvocationExpressionSyntax)syntaxContext.Node, syntaxContext.SemanticModel),
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzeMethod(syntaxContext, wellKnownTypes, hoistedScopes, hoistedServices),
                SyntaxKind.MethodDeclaration);

            compilationContext.RegisterCompilationEndAction(endContext =>
            {
                foreach (var candidate in hoistedScopes)
                {
                    // Suppress only when every observed use of the scope is a resolution of a
                    // service proven Singleton: hoisting the scope is then behaviorally identical
                    // to per-iteration scopes. Any scoped/unproven resolution (or a use we cannot
                    // see through) keeps the report.
                    if (!candidate.HasNonResolutionUse &&
                        candidate.ResolvedServiceTypes.Length > 0 &&
                        candidate.ResolvedServiceTypes.All(type =>
                            registrationCollector.GetLifetime(type) == Infrastructure.ServiceLifetime.Singleton))
                    {
                        continue;
                    }

                    endContext.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.HostedServiceScopePerIteration,
                        candidate.Location,
                        additionalLocations: new[] { candidate.LoopLocation },
                        candidate.MethodName));
                }

                foreach (var candidate in hoistedServices)
                {
                    // Only flag when the registration provably makes the service scoped; unknown
                    // lifetimes stay silent (registrations may live in another assembly).
                    if (registrationCollector.GetLifetime(candidate.ServiceType) != Infrastructure.ServiceLifetime.Scoped)
                    {
                        continue;
                    }

                    endContext.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.HostedServiceScopedServicePerIteration,
                        candidate.Location,
                        additionalLocations: new[] { candidate.LoopLocation },
                        candidate.ServiceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        candidate.MethodName));
                }
            });
        });
    }

    private static void AnalyzeMethod(
        SyntaxNodeAnalysisContext context,
        WellKnownTypes wellKnownTypes,
        ConcurrentBag<HoistedScopeCandidate> hoistedScopes,
        ConcurrentBag<HoistedServiceCandidate> hoistedServices)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (method.Body is null)
        {
            return;
        }

        if (!IsHostedExecutionMethod(method, context.SemanticModel))
        {
            return;
        }

        foreach (var loop in FindOutermostLongRunningLoops(method.Body, context.SemanticModel))
        {
            AnalyzeLoop(context, wellKnownTypes, method, method.Body, loop, hoistedScopes, hoistedServices);
        }
    }

    private static void AnalyzeLoop(
        SyntaxNodeAnalysisContext context,
        WellKnownTypes wellKnownTypes,
        MethodDeclarationSyntax method,
        BlockSyntax methodBody,
        StatementSyntax loop,
        ConcurrentBag<HoistedScopeCandidate> hoistedScopes,
        ConcurrentBag<HoistedServiceCandidate> hoistedServices)
    {
        var semanticModel = context.SemanticModel;
        var loopBody = GetLoopBody(loop);
        if (loopBody is null)
        {
            return;
        }

        // Scope locals declared before the loop, in a block enclosing it.
        var scopeLocals = new Dictionary<ILocalSymbol, InvocationExpressionSyntax>(SymbolEqualityComparer.Default);

        // Locals holding a service resolved before the loop: local -> (service type, the
        // resolution invocation, the hoisted scope local it was resolved from, if any).
        var serviceLocals = new Dictionary<ILocalSymbol, (ITypeSymbol ServiceType, InvocationExpressionSyntax Invocation, ILocalSymbol? SourceScope)>(SymbolEqualityComparer.Default);

        foreach (var declarator in GetLocalsDeclaredBeforeLoop(methodBody, loop))
        {
            if (declarator.Initializer?.Value is not { } initializer ||
                semanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol local)
            {
                continue;
            }

            var initializerInvocation = UnwrapInvocation(initializer);
            if (initializerInvocation is null)
            {
                continue;
            }

            if (IsScopeCreation(initializerInvocation, semanticModel, wellKnownTypes))
            {
                scopeLocals[local] = initializerInvocation;
            }
            else if (TryGetResolution(initializerInvocation, semanticModel, out var serviceType))
            {
                var sourceScope = FindReferencedScopeLocal(initializerInvocation, semanticModel, scopeLocals);
                serviceLocals[local] = (serviceType, initializerInvocation, sourceScope);
            }
        }

        if (scopeLocals.Count == 0 && serviceLocals.Count == 0)
        {
            return;
        }

        var scopeUsage = new Dictionary<ILocalSymbol, ScopeUsage>(SymbolEqualityComparer.Default);
        var serviceUsedInLoop = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var reassignedInLoop = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var identifier in loopBody.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (semanticModel.GetSymbolInfo(identifier).Symbol is not ILocalSymbol local)
            {
                continue;
            }

            var isScopeLocal = scopeLocals.ContainsKey(local);
            var isServiceLocal = serviceLocals.ContainsKey(local);
            if (!isScopeLocal && !isServiceLocal)
            {
                continue;
            }

            if (IsAssignmentTarget(identifier))
            {
                reassignedInLoop.Add(local);
                continue;
            }

            if (isServiceLocal)
            {
                serviceUsedInLoop.Add(local);
                continue;
            }

            if (!scopeUsage.TryGetValue(local, out var usage))
            {
                usage = new ScopeUsage();
                scopeUsage[local] = usage;
            }

            // A scope identifier used as the root of `scope.ServiceProvider.Get*Service<T>()`
            // contributes a typed resolution; any other shape (passed to a method, disposed,
            // provider handed elsewhere) is an opaque use.
            if (TryGetEnclosingResolution(identifier, semanticModel, out var resolvedType))
            {
                usage.ResolvedTypes.Add(resolvedType);
            }
            else
            {
                usage.HasNonResolutionUse = true;
            }
        }

        // Service locals used inside the loop: attribute the use to their source scope (the
        // hoisted scope is what must move), or report the hoisted service itself when it was
        // resolved from a provider that is not a hoisted scope.
        foreach (var local in serviceUsedInLoop)
        {
            var (serviceType, invocation, sourceScope) = serviceLocals[local];
            if (reassignedInLoop.Contains(local))
            {
                continue;
            }

            if (sourceScope is not null)
            {
                if (!scopeUsage.TryGetValue(sourceScope, out var usage))
                {
                    usage = new ScopeUsage();
                    scopeUsage[sourceScope] = usage;
                }

                usage.ResolvedTypes.Add(serviceType);
            }
            else
            {
                hoistedServices.Add(new HoistedServiceCandidate(
                    invocation.GetLocation(),
                    loop.GetLocation(),
                    serviceType,
                    method.Identifier.Text));
            }
        }

        foreach (var pair in scopeUsage)
        {
            if (reassignedInLoop.Contains(pair.Key))
            {
                continue;
            }

            hoistedScopes.Add(new HoistedScopeCandidate(
                scopeLocals[pair.Key].GetLocation(),
                loop.GetLocation(),
                pair.Value.ResolvedTypes.ToImmutableArray(),
                pair.Value.HasNonResolutionUse,
                method.Identifier.Text));
        }
    }

    private static InvocationExpressionSyntax? UnwrapInvocation(ExpressionSyntax expression)
    {
        return expression switch
        {
            InvocationExpressionSyntax invocation => invocation,
            AwaitExpressionSyntax awaitExpression => UnwrapInvocation(awaitExpression.Expression),
            CastExpressionSyntax cast => UnwrapInvocation(cast.Expression),
            ParenthesizedExpressionSyntax parenthesized => UnwrapInvocation(parenthesized.Expression),
            _ => null
        };
    }

    private static bool IsAssignmentTarget(IdentifierNameSyntax identifier)
    {
        return identifier.Parent is AssignmentExpressionSyntax assignment &&
               assignment.Left == identifier;
    }

    private static ILocalSymbol? FindReferencedScopeLocal(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> scopeLocals)
    {
        foreach (var identifier in invocation.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol local &&
                scopeLocals.ContainsKey(local))
            {
                return local;
            }
        }

        return null;
    }

    private static bool TryGetEnclosingResolution(
        IdentifierNameSyntax identifier,
        SemanticModel semanticModel,
        out ITypeSymbol resolvedType)
    {
        resolvedType = null!;
        for (SyntaxNode? current = identifier.Parent; current is not null; current = current.Parent)
        {
            if (current is StatementSyntax or LambdaExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return false;
            }

            if (current is InvocationExpressionSyntax invocation &&
                TryGetResolution(invocation, semanticModel, out var serviceType))
            {
                resolvedType = serviceType;
                return true;
            }
        }

        return false;
    }

    private static bool IsScopeCreation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol ||
            methodSymbol.Name is not ("CreateScope" or "CreateAsyncScope"))
        {
            return false;
        }

        var containingType = methodSymbol.ContainingType;
        if (containingType is null)
        {
            return false;
        }

        return wellKnownTypes.IsServiceScopeFactory(containingType) ||
               (containingType.Name == "ServiceProviderServiceExtensions" &&
                containingType.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection");
    }

    private static bool TryGetResolution(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out ITypeSymbol serviceType)
    {
        serviceType = null!;
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol ||
            methodSymbol.Name is not ("GetRequiredService" or "GetService") ||
            !methodSymbol.IsGenericMethod ||
            methodSymbol.TypeArguments.Length != 1)
        {
            return false;
        }

        var containingType = methodSymbol.ContainingType;
        if (containingType?.Name != "ServiceProviderServiceExtensions" ||
            containingType.ContainingNamespace?.ToDisplayString() != "Microsoft.Extensions.DependencyInjection")
        {
            return false;
        }

        serviceType = methodSymbol.TypeArguments[0];
        return true;
    }

    private static IEnumerable<VariableDeclaratorSyntax> GetLocalsDeclaredBeforeLoop(
        BlockSyntax methodBody,
        StatementSyntax loop)
    {
        // Walk from the loop up to the method body; in every enclosing block, locals declared in
        // statements that precede the loop's enclosing statement are in scope at the loop.
        SyntaxNode current = loop;
        while (current != methodBody.Parent && current.Parent is not null)
        {
            // using (var scope = ...) { while (...) ... } — the using declaration's locals are
            // in scope for (and created once outside) the loop.
            if (current.Parent is UsingStatementSyntax { Declaration: { } usingDeclaration } usingStatement &&
                usingStatement.Statement.Span.Contains(loop.Span))
            {
                foreach (var declarator in usingDeclaration.Variables)
                {
                    yield return declarator;
                }
            }

            if (current.Parent is BlockSyntax block)
            {
                foreach (var statement in block.Statements)
                {
                    if (statement == current)
                    {
                        break;
                    }

                    if (statement is LocalDeclarationStatementSyntax localDeclaration)
                    {
                        foreach (var declarator in localDeclaration.Declaration.Variables)
                        {
                            yield return declarator;
                        }
                    }
                }
            }

            if (current.Parent == methodBody)
            {
                yield break;
            }

            current = current.Parent;
        }
    }

    private static StatementSyntax? GetLoopBody(StatementSyntax loop)
    {
        return loop switch
        {
            WhileStatementSyntax whileStatement => whileStatement.Statement,
            DoStatementSyntax doStatement => doStatement.Statement,
            ForStatementSyntax forStatement => forStatement.Statement,
            _ => null
        };
    }

    private static IEnumerable<StatementSyntax> FindOutermostLongRunningLoops(
        BlockSyntax methodBody,
        SemanticModel semanticModel)
    {
        var qualifying = methodBody
            .DescendantNodes(node => node is not (LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax))
            .OfType<StatementSyntax>()
            .Where(statement => IsLongRunningLoop(statement, semanticModel))
            .ToList();

        return qualifying.Where(loop =>
            !qualifying.Any(other => other != loop && GetLoopBody(other)?.Span.Contains(loop.Span) == true));
    }

    private static bool IsLongRunningLoop(StatementSyntax statement, SemanticModel semanticModel)
    {
        return statement switch
        {
            WhileStatementSyntax whileStatement => IsLongRunningCondition(whileStatement.Condition, semanticModel),
            DoStatementSyntax doStatement => IsLongRunningCondition(doStatement.Condition, semanticModel),
            ForStatementSyntax forStatement =>
                forStatement.Condition is null ||
                forStatement.Condition.IsKind(SyntaxKind.TrueLiteralExpression),
            _ => false
        };
    }

    private static bool IsLongRunningCondition(ExpressionSyntax condition, SemanticModel semanticModel)
    {
        switch (condition)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression):
                return true;

            // while (!token.IsCancellationRequested)
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } negation
                when IsCancellationRequestedAccess(negation.Operand, semanticModel):
                return true;

            // while (await timer.WaitForNextTickAsync(token))
            case AwaitExpressionSyntax awaitExpression
                when UnwrapInvocation(awaitExpression.Expression) is { } invocation &&
                     GetInvokedName(invocation) == "WaitForNextTickAsync":
                return true;

            default:
                return false;
        }
    }

    private static string? GetInvokedName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null
        };
    }

    private static bool IsCancellationRequestedAccess(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        if (expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "IsCancellationRequested" } memberAccess)
        {
            return false;
        }

        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        return receiverType is { Name: "CancellationToken" } &&
               receiverType.ContainingNamespace?.ToDisplayString() == "System.Threading";
    }

    private static bool IsHostedExecutionMethod(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        if (semanticModel.GetDeclaredSymbol(method) is not IMethodSymbol symbol ||
            symbol.ContainingType is not INamedTypeSymbol containingType)
        {
            return false;
        }

        // BackgroundService.ExecuteAsync(CancellationToken) override.
        if (symbol is { Name: "ExecuteAsync", IsOverride: true, Parameters.Length: 1 } &&
            IsCancellationToken(symbol.Parameters[0].Type))
        {
            for (var baseType = containingType.BaseType; baseType is not null; baseType = baseType.BaseType)
            {
                if (baseType.Name == "BackgroundService" &&
                    baseType.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.Hosting")
                {
                    return true;
                }
            }
        }

        // IHostedService.StartAsync / IHostedLifecycleService start methods: the long-running
        // entry points. StopAsync and the stopping/stopped callbacks run once at shutdown.
        if (symbol.Name is "StartAsync" or "StartingAsync" or "StartedAsync")
        {
            foreach (var iface in containingType.AllInterfaces)
            {
                if (iface.Name is not ("IHostedService" or "IHostedLifecycleService") ||
                    iface.ContainingNamespace?.ToDisplayString() != "Microsoft.Extensions.Hosting")
                {
                    continue;
                }

                foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    if (member.Name == symbol.Name &&
                        SymbolEqualityComparer.Default.Equals(
                            containingType.FindImplementationForInterfaceMember(member), symbol))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsCancellationToken(ITypeSymbol type)
    {
        return type.Name == "CancellationToken" &&
               type.ContainingNamespace?.ToDisplayString() == "System.Threading";
    }

    private sealed class ScopeUsage
    {
        public List<ITypeSymbol> ResolvedTypes { get; } = new();

        public bool HasNonResolutionUse { get; set; }
    }

    private readonly struct HoistedScopeCandidate
    {
        public HoistedScopeCandidate(
            Location location,
            Location loopLocation,
            ImmutableArray<ITypeSymbol> resolvedServiceTypes,
            bool hasNonResolutionUse,
            string methodName)
        {
            Location = location;
            LoopLocation = loopLocation;
            ResolvedServiceTypes = resolvedServiceTypes;
            HasNonResolutionUse = hasNonResolutionUse;
            MethodName = methodName;
        }

        public Location Location { get; }

        public Location LoopLocation { get; }

        public ImmutableArray<ITypeSymbol> ResolvedServiceTypes { get; }

        public bool HasNonResolutionUse { get; }

        public string MethodName { get; }
    }

    private readonly struct HoistedServiceCandidate
    {
        public HoistedServiceCandidate(
            Location location,
            Location loopLocation,
            ITypeSymbol serviceType,
            string methodName)
        {
            Location = location;
            LoopLocation = loopLocation;
            ServiceType = serviceType;
            MethodName = methodName;
        }

        public Location Location { get; }

        public Location LoopLocation { get; }

        public ITypeSymbol ServiceType { get; }

        public string MethodName { get; }
    }
}
