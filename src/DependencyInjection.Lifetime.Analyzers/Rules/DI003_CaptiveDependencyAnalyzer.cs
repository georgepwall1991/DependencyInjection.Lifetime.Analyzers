using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects captive dependencies - when a singleton captures a scoped or transient dependency.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI003_CaptiveDependencyAnalyzer : DiagnosticAnalyzer
{
    internal const string DependencyLifetimePropertyName = "DependencyLifetime";

    private readonly struct ReportedDiagnosticKey : System.IEquatable<ReportedDiagnosticKey>
    {
        public int RegistrationStart { get; }
        public ITypeSymbol DependencyType { get; }
        public object? Key { get; }
        public bool IsKeyed { get; }

        public ReportedDiagnosticKey(
            int registrationStart,
            ITypeSymbol dependencyType,
            object? key,
            bool isKeyed)
        {
            RegistrationStart = registrationStart;
            DependencyType = dependencyType;
            Key = key;
            IsKeyed = isKeyed;
        }

        public bool Equals(ReportedDiagnosticKey other)
        {
            return RegistrationStart == other.RegistrationStart &&
                   SymbolEqualityComparer.Default.Equals(DependencyType, other.DependencyType) &&
                   Equals(Key, other.Key) &&
                   IsKeyed == other.IsKeyed;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = RegistrationStart;
                hashCode = (hashCode * 397) ^ SymbolEqualityComparer.Default.GetHashCode(DependencyType);
                hashCode = (hashCode * 397) ^ (Key?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ IsKeyed.GetHashCode();
                return hashCode;
            }
        }

        public override bool Equals(object? obj) =>
            obj is ReportedDiagnosticKey other && Equals(other);
    }

    private sealed class FactoryKeyContext
    {
        public static readonly FactoryKeyContext None = new(
            keyParameters: ImmutableArray<IParameterSymbol>.Empty,
            inheritedKey: null);

        public FactoryKeyContext(
            ImmutableArray<IParameterSymbol> keyParameters,
            object? inheritedKey)
        {
            KeyParameters = keyParameters;
            InheritedKey = inheritedKey;
        }

        public ImmutableArray<IParameterSymbol> KeyParameters { get; }

        public object? InheritedKey { get; }
    }

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.CaptiveDependency);

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

            var lifetimeClassifier = new KnownServiceLifetimeClassifier(
                WellKnownTypes.Create(compilationContext.Compilation));

            // First pass: collect all registrations
            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => registrationCollector.AnalyzeInvocation(
                    (InvocationExpressionSyntax)syntaxContext.Node,
                    syntaxContext.SemanticModel),
                SyntaxKind.InvocationExpression);

            // Second pass: check for captive dependencies at compilation end
            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeCaptiveDependencies(endContext, registrationCollector, lifetimeClassifier));
        });
    }

    private static void AnalyzeCaptiveDependencies(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier)
    {
        var semanticModelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();
        var reportedDiagnostics = new HashSet<ReportedDiagnosticKey>();

        foreach (var registration in registrationCollector.AllRegistrations)
        {
            // Limit DI003 to singleton consumers. Scoped -> transient is common and generally safe,
            // so warning on it creates more noise than signal.
            if (registration.Lifetime != ServiceLifetime.Singleton)
            {
                continue;
            }

            if (registration.FactoryExpression != null)
            {
                AnalyzeFactoryRegistration(
                    context,
                    registration,
                    registrationCollector,
                    lifetimeClassifier,
                    semanticModelsByTree,
                    reportedDiagnostics);
            }
            else if (!registration.HasImplementationInstance && registration.ImplementationType != null)
            {
                AnalyzeConstructorRegistration(
                    context,
                    registration,
                    registrationCollector,
                    lifetimeClassifier,
                    reportedDiagnostics);
            }
        }
    }

    private static void AnalyzeFactoryRegistration(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        HashSet<ReportedDiagnosticKey> reportedDiagnostics)
    {
        var factory = registration.FactoryExpression;
        if (factory == null)
        {
            return;
        }

        var semanticModel = GetSemanticModel(factory.SyntaxTree, context.Compilation, semanticModelsByTree);
        var invocations = FactoryAnalysis.GetFactoryInvocations(factory, semanticModel);
        var factoryKeyContext = CreateFactoryKeyContext(factory, semanticModel, registration);

        foreach (var invocation in invocations)
        {
            var invocationSemanticModel = GetSemanticModel(invocation.SyntaxTree, context.Compilation, semanticModelsByTree);
            var symbolInfo = invocationSemanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
            var methodName = sourceMethod.Name;
            bool isKeyedResolution =
                methodName is "GetKeyedService" or "GetRequiredKeyedService" or "GetKeyedServices";
            bool isEnumerableResolution = methodName is "GetServices" or "GetKeyedServices";

            if (!IsServiceResolutionMethod(methodSymbol))
            {
                if (FactoryAnalysis.TryGetActivatorUtilitiesImplementationType(
                        invocation,
                        invocationSemanticModel,
                        out var implementationType,
                        out var hasExplicitConstructorArguments) &&
                    !hasExplicitConstructorArguments)
                {
                    AnalyzeActivatorUtilitiesConstruction(
                        context,
                        registration,
                        registrationCollector,
                        lifetimeClassifier,
                        implementationType,
                        invocation.GetLocation(),
                        reportedDiagnostics);
                }

                continue;
            }

            var dependencyType = GetResolvedDependencyType(invocation, methodSymbol, invocationSemanticModel);
            if (dependencyType == null)
            {
                continue;
            }

            // A factory that creates and disposes its own scope performs one-time scoped
            // setup: the resolved instance does not outlive the factory call, so nothing is
            // captured by the longer-lived registration. The suppression requires both a
            // disposal proof for the scope and a non-escape proof for the resolved instance
            // (derived values like `seed.Config` may flow into the product; the instance
            // itself may not). Undisposed factory scopes stay reported — they (and their
            // scoped instances) live as long as the product.
            if (IsResolutionFromFactoryOwnedDisposedScope(invocation, factory, invocationSemanticModel) &&
                !ResolvedInstanceEscapesFactory(invocation, GetFactoryAnalysisContainer(invocation, factory), invocationSemanticModel))
            {
                continue;
            }

            object? key = null;
            bool isKeyed = false;
            if (isKeyedResolution)
            {
                if (!TryExtractKeyFromResolution(
                        invocation,
                        methodSymbol,
                        invocationSemanticModel,
                        factoryKeyContext,
                        out key))
                {
                    continue;
                }

                isKeyed = true;
            }

            if (!TryGetCaptiveDependencyLifetime(
                    registration.Lifetime,
                    registrationCollector,
                    lifetimeClassifier,
                    dependencyType,
                    key,
                    isKeyed,
                    isEnumerableResolution,
                    out var dependencyLifetime))
            {
                continue;
            }

            ReportDiagnostic(
                context,
                registration,
                invocation.GetLocation(),
                registration.ServiceType.Name,
                dependencyType,
                dependencyLifetime,
                key,
                isKeyed,
                reportedDiagnostics);
        }
    }

    private static bool IsResolutionFromFactoryOwnedDisposedScope(
        InvocationExpressionSyntax resolution,
        ExpressionSyntax factoryExpression,
        SemanticModel semanticModel)
    {
        if (resolution.Expression is not MemberAccessExpressionSyntax resolutionAccess)
        {
            return false;
        }

        // Method-group factories resolve inside the target method's body in another part
        // of the tree; analyze within that body rather than the registration-site
        // expression.
        var factory = GetFactoryAnalysisContainer(resolution, factoryExpression);

        // A resolution inside a nested lambda/local function runs after the factory body
        // completes — by then the factory's own scope is disposed, and the delegate
        // captures it. Only same-execution-context resolutions qualify as one-time setup.
        if (IsInsideNestedFunction(resolution, factory))
        {
            return false;
        }


        // `scope.ServiceProvider.Get*Service<T>()` — unwrap the provider hop.
        var scopeExpression = resolutionAccess.Expression;
        if (scopeExpression is MemberAccessExpressionSyntax { Name.Identifier.Text: "ServiceProvider" } providerAccess)
        {
            scopeExpression = providerAccess.Expression;
        }

        while (scopeExpression is ParenthesizedExpressionSyntax parenthesized)
        {
            scopeExpression = parenthesized.Expression;
        }

        while (scopeExpression is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppression)
        {
            scopeExpression = suppression.Operand;
        }

        if (scopeExpression is not IdentifierNameSyntax scopeIdentifier ||
            semanticModel.GetSymbolInfo(scopeIdentifier).Symbol is not ILocalSymbol scopeLocal ||
            scopeLocal.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax declarator ||
            !factory.Span.Contains(declarator.Span) ||
            declarator.Initializer?.Value is not { } initializer)
        {
            return false;
        }

        var creation = UnwrapToInvocation(initializer);
        if (creation is null || !IsScopeCreationInvocation(creation, semanticModel))
        {
            return false;
        }

        // Any explicit dispose of the scope before the last use of the resolved value means
        // the service is consumed from an already-disposed scope — never a safe setup, no
        // matter how the scope is otherwise disposed (including a redundant early Dispose
        // on a `using var` scope).
        var lastUsePosition = GetLastResolvedUsePosition(resolution, factory, semanticModel);
        foreach (var earlyCandidate in factory.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (earlyCandidate.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Dispose" or "DisposeAsync" } earlyAccess &&
                earlyAccess.Expression is IdentifierNameSyntax earlyIdentifier &&
                earlyCandidate.SpanStart < lastUsePosition &&
                !IsInsideNestedFunction(earlyCandidate, factory) &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(earlyIdentifier).Symbol, scopeLocal))
            {
                return false;
            }
        }

        // Disposal proof: `using var scope = ...`, `using (var scope = ...)`, or an explicit
        // scope.Dispose()/DisposeAsync() inside the factory.
        if (declarator.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>() is { } declarationStatement &&
            declarationStatement.UsingKeyword != default)
        {
            return true;
        }

        if (declarator.FirstAncestorOrSelf<UsingStatementSyntax>() is { } usingStatement &&
            usingStatement.Declaration?.Variables.Contains(declarator) == true)
        {
            return true;
        }

        // The explicit dispose must run after the resolution and after every use of the
        // resolved value: a scope disposed before (or between) uses is not a safe one-time
        // setup — the service is consumed from an already-disposed scope.
        foreach (var candidate in factory.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (candidate.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Dispose" or "DisposeAsync" } disposeAccess &&
                disposeAccess.Expression is IdentifierNameSyntax disposedIdentifier &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(disposedIdentifier).Symbol, scopeLocal) &&
                candidate.SpanStart >= lastUsePosition &&
                (disposeAccess.Name.Identifier.Text != "DisposeAsync" || IsAwaited(candidate)) &&
                ExplicitDisposeDominatesResolution(candidate, resolution, factory) &&
                !HasExitBetween(resolution, candidate, factory))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetLastResolvedUsePosition(
        InvocationExpressionSyntax resolution,
        SyntaxNode factory,
        SemanticModel semanticModel)
    {
        var lastPosition = resolution.Span.End;

        var current = (ExpressionSyntax)resolution;
        while (current.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax ||
               current.Parent is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression })
        {
            current = (ExpressionSyntax)current.Parent;
        }

        if (current.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } &&
            semanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol resolvedLocal)
        {
            foreach (var identifier in factory.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (identifier.SpanStart > declarator.Span.End &&
                    identifier.Span.End > lastPosition &&
                    SymbolEqualityComparer.Default.Equals(
                        semanticModel.GetSymbolInfo(identifier).Symbol, resolvedLocal))
                {
                    lastPosition = identifier.Span.End;
                }
            }
        }

        return lastPosition;
    }

    private static bool HasExitBetween(SyntaxNode start, SyntaxNode end, SyntaxNode factory)
    {
        // A return/throw/jump between the resolution and the dispose means a path exists
        // on which the factory finishes with the scope undisposed.
        foreach (var descendant in factory.DescendantNodes())
        {
            if (descendant is not (ReturnStatementSyntax or ThrowStatementSyntax or ThrowExpressionSyntax
                or GotoStatementSyntax or ContinueStatementSyntax or BreakStatementSyntax) ||
                descendant.SpanStart <= start.Span.End ||
                descendant.Span.End >= end.SpanStart ||
                IsInsideNestedFunction(descendant, factory))
            {
                continue;
            }

            // An exit inside a try whose finally performs the dispose cannot bypass it —
            // finally always runs (`try { ... return ...; } finally { scope.Dispose(); }`).
            var guardedByFinally = false;
            for (SyntaxNode? current = descendant.Parent; current is not null && current != factory; current = current.Parent)
            {
                if (current is TryStatementSyntax { Finally.Block: { } finallyBlock } &&
                    finallyBlock.Span.Contains(end.Span))
                {
                    guardedByFinally = true;
                    break;
                }
            }

            if (guardedByFinally)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsInsideNestedFunction(SyntaxNode node, SyntaxNode factory)
    {
        for (SyntaxNode? current = node.Parent; current is not null && current != factory; current = current.Parent)
        {
            if (current is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAwaited(InvocationExpressionSyntax invocation)
    {
        SyntaxNode current = invocation;
        while (current.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized;
        }

        return current.Parent is AwaitExpressionSyntax;
    }

    private static bool IsMethodGroupUse(MemberAccessExpressionSyntax memberAccess, SemanticModel semanticModel)
    {
        // `seed.DoWork` handed somewhere as a delegate captures the instance as the
        // delegate target — that is the instance escaping, not a derived value.
        if (memberAccess.Parent is InvocationExpressionSyntax invocation && invocation.Expression == memberAccess)
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
        return symbolInfo.Symbol is IMethodSymbol ||
               symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().Any();
    }

    private static bool ExplicitDisposeDominatesResolution(
        InvocationExpressionSyntax disposeInvocation,
        InvocationExpressionSyntax resolution,
        SyntaxNode factory)
    {
        // The explicit dispose only proves cleanup when it runs on every path the
        // resolution runs on: not inside a nested lambda/local function (which may never
        // execute), and not behind a branch the resolution does not share. `finally`
        // clauses pass — they always run.
        for (SyntaxNode? current = disposeInvocation.Parent; current is not null && current != factory; current = current.Parent)
        {
            if (current is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return false;
            }

            if (current is IfStatementSyntax or
                ElseClauseSyntax or
                SwitchStatementSyntax or
                SwitchSectionSyntax or
                ConditionalExpressionSyntax or
                ForStatementSyntax or
                ForEachStatementSyntax or
                ForEachVariableStatementSyntax or
                WhileStatementSyntax or
                DoStatementSyntax or
                CatchClauseSyntax)
            {
                if (!current.Span.Contains(resolution.Span))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ResolvedInstanceEscapesFactory(
        InvocationExpressionSyntax resolution,
        SyntaxNode factory,
        SemanticModel semanticModel)
    {
        // Inline use: the resolution's own parent decides. A member access on the result
        // (`...GetRequiredService<T>().Config`) yields a derived value; anything else
        // (argument, return, assignment) hands the instance onward.
        var current = (ExpressionSyntax)resolution;
        while (current.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax ||
               current.Parent is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression })
        {
            current = (ExpressionSyntax)current.Parent;
        }

        if (current.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } &&
            semanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol resolvedLocal)
        {
            // Tracked local: the instance escapes when any later use is not a plain
            // member-access receiver.
            foreach (var identifier in factory.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (identifier.SpanStart <= declarator.Span.End ||
                    !SymbolEqualityComparer.Default.Equals(
                        semanticModel.GetSymbolInfo(identifier).Symbol, resolvedLocal))
                {
                    continue;
                }

                // A use inside a nested lambda/local function captures the instance in a
                // closure that may outlive the factory call — that is an escape even when
                // the use itself is receiver-only.
                if (IsInsideNestedFunction(identifier, factory))
                {
                    return true;
                }

                var isReceiverOnly =
                    (identifier.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Expression == identifier &&
                     !IsMethodGroupUse(memberAccess, semanticModel) &&
                     IsSafeDerivedValue(memberAccess, semanticModel)) ||
                    (identifier.Parent is ConditionalAccessExpressionSyntax conditionalAccess && conditionalAccess.Expression == identifier &&
                     IsSafeDerivedValue(conditionalAccess, semanticModel));
                if (!isReceiverOnly)
                {
                    return true;
                }
            }

            return false;
        }

        return current.Parent is not MemberAccessExpressionSyntax accessOnResult ||
               accessOnResult.Expression != current ||
               IsMethodGroupUse(accessOnResult, semanticModel) ||
               !IsSafeDerivedValue(accessOnResult, semanticModel);
    }

    private static bool IsSafeDerivedValue(ExpressionSyntax access, SemanticModel semanticModel)
    {
        // Only values that provably cannot carry the scoped instance (value types and
        // strings) count as derived: a member returning a reference type may hand back the
        // instance itself or scoped state reachable from it.
        var resultExpression = access;
        if (resultExpression.Parent is InvocationExpressionSyntax invocation && invocation.Expression == resultExpression)
        {
            resultExpression = invocation;
        }

        var resultType = semanticModel.GetTypeInfo(resultExpression).Type;
        if (resultType is null)
        {
            return false;
        }

        // Primitives, enums, and strings cannot carry the scoped instance. Arbitrary
        // structs can (a struct field may hold the service), so they do not qualify.
        return resultType.TypeKind == TypeKind.Enum ||
               (resultType.SpecialType != SpecialType.None &&
                resultType.SpecialType != SpecialType.System_Object);
    }

    private static SyntaxNode GetFactoryAnalysisContainer(InvocationExpressionSyntax resolution, ExpressionSyntax factoryExpression)
    {
        if (factoryExpression.SyntaxTree == resolution.SyntaxTree &&
            factoryExpression.Span.Contains(resolution.Span))
        {
            return factoryExpression;
        }

        foreach (var ancestor in resolution.Ancestors())
        {
            if (ancestor is MethodDeclarationSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax)
            {
                return ancestor;
            }
        }

        return factoryExpression;
    }

    private static InvocationExpressionSyntax? UnwrapToInvocation(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case AwaitExpressionSyntax awaitExpression:
                    expression = awaitExpression.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppression:
                    expression = (ExpressionSyntax)suppression.Operand;
                    continue;
                case InvocationExpressionSyntax invocation:
                    return invocation;
                default:
                    return null;
            }
        }
    }

    private static bool IsScopeCreationInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method ||
            method.Name is not ("CreateScope" or "CreateAsyncScope"))
        {
            return false;
        }

        var sourceMethod = method.ReducedFrom ?? method;
        var containingType = sourceMethod.ContainingType;

        // Restrict to the framework's own scope-creation surface: a user CreateScope
        // extension declared inside the MEDI namespace for discoverability may return a
        // wrapper whose ServiceProvider is the root provider.
        return containingType is { Name: "IServiceScopeFactory" or "ServiceProviderServiceExtensions" } &&
               containingType.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }

    private static ITypeSymbol? GetResolvedDependencyType(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel)
    {
        if (methodSymbol.IsGenericMethod && methodSymbol.TypeArguments.Length > 0)
        {
            return methodSymbol.TypeArguments[0];
        }

        // Non-generic overloads pass the service type as a System.Type argument.
        var serviceTypeExpression = GetInvocationArgumentExpression(invocation, methodSymbol, "serviceType");
        if (serviceTypeExpression is TypeOfExpressionSyntax typeOfExpression)
        {
            return semanticModel.GetTypeInfo(typeOfExpression.Type).Type;
        }

        return null;
    }

    private static bool IsServiceResolutionMethod(IMethodSymbol methodSymbol)
    {
        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var methodName = sourceMethod.Name;
        if (methodName is not ("GetService" or "GetRequiredService" or "GetServices" or
            "GetKeyedService" or "GetRequiredKeyedService" or "GetKeyedServices"))
        {
            return false;
        }

        var containingType = sourceMethod.ContainingType;
        if (containingType is null)
        {
            return false;
        }

        if (containingType.Name == "ServiceProviderServiceExtensions" &&
            containingType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
        {
            return true;
        }

        if (sourceMethod.IsExtensionMethod && sourceMethod.Parameters.Length > 0)
        {
            var receiverType = sourceMethod.Parameters[0].Type;
            if (IsSystemIServiceProvider(receiverType) ||
                IsKeyedServiceProvider(receiverType))
            {
                return true;
            }
        }

        return IsSystemIServiceProvider(containingType) ||
               IsKeyedServiceProvider(containingType);
    }

    private static bool IsSystemIServiceProvider(ITypeSymbol type) =>
        type.Name == "IServiceProvider" &&
        type.ContainingNamespace.ToDisplayString() == "System";

    private static bool IsKeyedServiceProvider(ITypeSymbol type) =>
        type.Name == "IKeyedServiceProvider" &&
        type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";

    private static bool TryExtractKeyFromResolution(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        FactoryKeyContext factoryKeyContext,
        out object? key)
    {
        key = null;
        var keyExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "serviceKey") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "key");

        // Fallback for simplified test stubs or unusual signatures.
        if (keyExpression is null)
        {
            if (invocation.ArgumentList.Arguments.Count == 1)
            {
                keyExpression = invocation.ArgumentList.Arguments[0].Expression;
            }
            else if (invocation.ArgumentList.Arguments.Count >= 2)
            {
                keyExpression = invocation.ArgumentList.Arguments[1].Expression;
            }
        }

        if (keyExpression is null)
        {
            return false;
        }

        if (TryGetInheritedFactoryKey(keyExpression, semanticModel, factoryKeyContext, out key))
        {
            return true;
        }

        return SyntaxValueHelpers.TryExtractServiceKeyValue(keyExpression, semanticModel, out key, out _) &&
               !SyntaxValueHelpers.IsKeyedServiceAnyKey(key);
    }

    private static ExpressionSyntax? GetInvocationArgumentExpression(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        string parameterName)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon?.Name.Identifier.Text == parameterName)
            {
                return argument.Expression;
            }
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var isReducedExtension = methodSymbol.ReducedFrom is not null;

        for (var i = 0; i < sourceMethod.Parameters.Length; i++)
        {
            if (sourceMethod.Parameters[i].Name != parameterName)
            {
                continue;
            }

            // Reduced extension method calls omit the receiver argument from the invocation argument list.
            var argumentIndex = isReducedExtension ? i - 1 : i;
            if (argumentIndex >= 0 && argumentIndex < invocation.ArgumentList.Arguments.Count)
            {
                return invocation.ArgumentList.Arguments[argumentIndex].Expression;
            }
        }

        return null;
    }

    private static void AnalyzeConstructorRegistration(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        HashSet<ReportedDiagnosticKey> reportedDiagnostics)
    {
        var implementationType = registration.ImplementationType!;
        var constructors = ConstructorSelection.GetConstructorsToAnalyze(implementationType);

        foreach (var constructor in constructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                var parameterType = UnwrapEnumerableDependency(parameter.Type, out var isEnumerableDependency);
                var serviceKey = GetServiceKey(parameter, registration);
                if (serviceKey.IsUnknown ||
                    SyntaxValueHelpers.IsKeyedServiceAnyKey(serviceKey.Key))
                {
                    continue;
                }

                if (TryGetCaptiveDependencyLifetime(
                        registration.Lifetime,
                        registrationCollector,
                        lifetimeClassifier,
                        parameterType,
                        serviceKey.Key,
                        serviceKey.IsKeyed,
                        isEnumerableDependency,
                        out var dependencyLifetime))
                {
                    ReportDiagnostic(
                        context,
                        registration,
                        registration.Location,
                        implementationType.Name,
                        parameterType,
                        dependencyLifetime,
                        serviceKey.Key,
                        serviceKey.IsKeyed,
                        reportedDiagnostics);
                }
            }
        }
    }

    private static void AnalyzeActivatorUtilitiesConstruction(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        INamedTypeSymbol implementationType,
        Location diagnosticLocation,
        HashSet<ReportedDiagnosticKey> reportedDiagnostics)
    {
        var constructors = ConstructorSelection.GetConstructorsToAnalyze(implementationType);
        foreach (var constructor in constructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                var parameterType = UnwrapEnumerableDependency(parameter.Type, out var isEnumerableDependency);
                var serviceKey = GetServiceKey(parameter);
                if (serviceKey.IsUnknown ||
                    SyntaxValueHelpers.IsKeyedServiceAnyKey(serviceKey.Key) ||
                    !TryGetCaptiveDependencyLifetime(
                        registration.Lifetime,
                        registrationCollector,
                        lifetimeClassifier,
                        parameterType,
                        serviceKey.Key,
                        serviceKey.IsKeyed,
                        isEnumerableDependency,
                        out var dependencyLifetime))
                {
                    continue;
                }

                ReportDiagnostic(
                    context,
                    registration,
                    diagnosticLocation,
                    registration.ServiceType.Name,
                    parameterType,
                    dependencyLifetime,
                    serviceKey.Key,
                    serviceKey.IsKeyed,
                    reportedDiagnostics);
            }
        }
    }

    private static void ReportDiagnostic(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        Location location,
        string consumerName,
        ITypeSymbol dependencyType,
        ServiceLifetime dependencyLifetime,
        object? key,
        bool isKeyed,
        HashSet<ReportedDiagnosticKey> reportedDiagnostics)
    {
        var reportKey = new ReportedDiagnosticKey(
            registration.Location.SourceSpan.Start,
            dependencyType,
            key,
            isKeyed);
        if (!reportedDiagnostics.Add(reportKey))
        {
            return;
        }

        var lifetimeName = dependencyLifetime.ToString().ToLowerInvariant();
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DependencyLifetimePropertyName, lifetimeName);
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.CaptiveDependency,
            location,
            additionalLocations: null,
            properties: properties,
            consumerName,
            lifetimeName,
            dependencyType.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static SemanticModel GetSemanticModel(
        SyntaxTree syntaxTree,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree)
    {
        #pragma warning disable RS1030
        return semanticModelsByTree.GetOrAdd(syntaxTree, tree => compilation.GetSemanticModel(tree));
        #pragma warning restore RS1030
    }

    private static KeyedServiceHelpers.ServiceKeyRequest GetServiceKey(
        IParameterSymbol parameter,
        ServiceRegistration registration) =>
        KeyedServiceHelpers.GetServiceKey(
            parameter,
            registration.IsKeyed ? registration.Key : null,
            registration.IsKeyed,
            registration.IsKeyed ? registration.KeyLiteral : null);

    private static KeyedServiceHelpers.ServiceKeyRequest GetServiceKey(IParameterSymbol parameter) =>
        KeyedServiceHelpers.GetServiceKey(
            parameter,
            inheritedKey: null,
            hasInheritedKey: false,
            inheritedKeyLiteral: null);

    private static bool TryGetCaptiveDependencyLifetime(
        ServiceLifetime consumerLifetime,
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        ITypeSymbol dependencyType,
        object? key,
        bool isKeyed,
        bool isEnumerableDependency,
        out ServiceLifetime dependencyLifetime)
    {
        if (isEnumerableDependency &&
            TryGetEnumerableCaptiveDependencyLifetime(
                consumerLifetime,
                registrationCollector,
                dependencyType,
                key,
                isKeyed,
                out dependencyLifetime))
        {
            return true;
        }

        var registeredLifetime = GetDependencyLifetime(
            registrationCollector,
            lifetimeClassifier,
            dependencyType,
            key,
            isKeyed);
        if (registeredLifetime is not null &&
            IsCaptiveDependency(consumerLifetime, registeredLifetime.Value))
        {
            dependencyLifetime = registeredLifetime.Value;
            return true;
        }

        dependencyLifetime = default;
        return false;
    }

    private static bool TryGetEnumerableCaptiveDependencyLifetime(
        ServiceLifetime consumerLifetime,
        RegistrationCollector registrationCollector,
        ITypeSymbol dependencyType,
        object? key,
        bool isKeyed,
        out ServiceLifetime dependencyLifetime)
    {
        foreach (var registration in GetMatchingRegistrations(
                     registrationCollector,
                     dependencyType,
                     key,
                     isKeyed))
        {
            if (!IsCaptiveDependency(consumerLifetime, registration.Lifetime))
            {
                continue;
            }

            dependencyLifetime = registration.Lifetime;
            return true;
        }

        dependencyLifetime = default;
        return false;
    }

    private static IEnumerable<ServiceRegistration> GetMatchingRegistrations(
        RegistrationCollector registrationCollector,
        ITypeSymbol dependencyType,
        object? key,
        bool isKeyed)
    {
        foreach (var registration in registrationCollector.AllRegistrations)
        {
            if (registration.IsKeyed != isKeyed ||
                !Equals(registration.Key, key))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(registration.ServiceType, dependencyType))
            {
                yield return registration;
                continue;
            }

            if (dependencyType is not INamedTypeSymbol namedDependencyType ||
                !namedDependencyType.IsGenericType ||
                namedDependencyType.IsUnboundGenericType)
            {
                continue;
            }

            var openDependencyType = namedDependencyType.ConstructUnboundGenericType();
            if (SymbolEqualityComparer.Default.Equals(registration.ServiceType, openDependencyType))
            {
                yield return registration;
            }
        }
    }

    private static ServiceLifetime? GetDependencyLifetime(
        RegistrationCollector registrationCollector,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        ITypeSymbol dependencyType,
        object? key,
        bool isKeyed)
    {
        var registeredLifetime = registrationCollector.GetLifetime(dependencyType, key, isKeyed);
        if (registeredLifetime is not null)
        {
            return registeredLifetime;
        }

        return lifetimeClassifier.TryGetLifetime(dependencyType, isKeyed, out var knownLifetime)
            ? knownLifetime
            : null;
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

    private static FactoryKeyContext CreateFactoryKeyContext(
        ExpressionSyntax factoryExpression,
        SemanticModel semanticModel,
        ServiceRegistration registration)
    {
        if (!registration.IsKeyed ||
            SyntaxValueHelpers.IsKeyedServiceAnyKey(registration.Key))
        {
            return FactoryKeyContext.None;
        }

        var keyParameters = ImmutableArray.CreateBuilder<IParameterSymbol>();
        foreach (var possibleFactoryExpression in FactoryAnalysis.ResolveFactoryExpressions(factoryExpression, semanticModel))
        {
            if (TryGetFactoryKeyParameter(possibleFactoryExpression, semanticModel, out var keyParameter))
            {
                keyParameters.Add(keyParameter);
            }
        }

        return keyParameters.Count == 0
            ? FactoryKeyContext.None
            : new FactoryKeyContext(keyParameters.ToImmutable(), registration.Key);
    }

    private static bool TryGetFactoryKeyParameter(
        ExpressionSyntax factoryExpression,
        SemanticModel semanticModel,
        out IParameterSymbol keyParameter)
    {
        keyParameter = null!;
        factoryExpression = UnwrapExpression(factoryExpression);

        switch (factoryExpression)
        {
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambda
                when parenthesizedLambda.ParameterList.Parameters.Count >= 2:
                return TryGetParameterSymbol(
                    parenthesizedLambda.ParameterList.Parameters[1],
                    semanticModel,
                    out keyParameter);

            case AnonymousMethodExpressionSyntax anonymousMethod
                when anonymousMethod.ParameterList?.Parameters.Count >= 2:
                return TryGetParameterSymbol(
                    anonymousMethod.ParameterList.Parameters[1],
                    semanticModel,
                    out keyParameter);

            case IdentifierNameSyntax or MemberAccessExpressionSyntax:
                var symbolInfo = semanticModel.GetSymbolInfo(factoryExpression);
                var methodSymbol = symbolInfo.Symbol as IMethodSymbol ??
                                   symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                if (methodSymbol?.Parameters.Length >= 2)
                {
                    keyParameter = methodSymbol.Parameters[1];
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private static bool TryGetParameterSymbol(
        ParameterSyntax parameterSyntax,
        SemanticModel semanticModel,
        out IParameterSymbol parameterSymbol)
    {
        if (semanticModel.GetDeclaredSymbol(parameterSyntax) is IParameterSymbol symbol)
        {
            parameterSymbol = symbol;
            return true;
        }

        parameterSymbol = null!;
        return false;
    }

    private static bool TryGetInheritedFactoryKey(
        ExpressionSyntax keyExpression,
        SemanticModel semanticModel,
        FactoryKeyContext factoryKeyContext,
        out object? key)
    {
        key = null;
        if (factoryKeyContext.KeyParameters.IsDefaultOrEmpty ||
            semanticModel.GetSymbolInfo(keyExpression).Symbol is not IParameterSymbol parameterSymbol ||
            !factoryKeyContext.KeyParameters.Any(keyParameter => SymbolEqualityComparer.Default.Equals(parameterSymbol, keyParameter)))
        {
            return false;
        }

        key = factoryKeyContext.InheritedKey;
        return true;
    }

    private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesizedExpression:
                    expression = parenthesizedExpression.Expression;
                    continue;
                case CastExpressionSyntax castExpression:
                    expression = castExpression.Expression;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static bool IsCaptiveDependency(ServiceLifetime consumerLifetime, ServiceLifetime dependencyLifetime)
    {
        return consumerLifetime == ServiceLifetime.Singleton &&
               dependencyLifetime is ServiceLifetime.Scoped or ServiceLifetime.Transient;
    }
}
