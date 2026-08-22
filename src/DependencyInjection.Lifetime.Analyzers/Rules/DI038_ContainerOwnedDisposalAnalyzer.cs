using System;
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
/// Analyzer that detects a consumer disposing a service the container owns. The container
/// disposes every singleton and scoped service it creates, so a consumer that disposes a
/// constructor-injected dependency — or the result of a <c>GetService</c>/<c>GetRequiredService</c>
/// call — tears down an instance that other consumers still share.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI038_ContainerOwnedDisposalAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.ContainerOwnedInjectedServiceDisposed,
            DiagnosticDescriptors.ContainerOwnedResolvedServiceDisposed);

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
            var memberCandidates = new ConcurrentQueue<InjectedMemberDisposal>();
            var parameterCandidates = new ConcurrentQueue<InjectedParameterDisposal>();
            var resolvedCandidates = new ConcurrentQueue<ResolvedServiceDisposal>();
            var frameworkExtensionLocations =
                new ConcurrentDictionary<(string FilePath, int Start, int Length), byte>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
                    registrationCollector.AnalyzeInvocation(invocation, syntaxContext.SemanticModel);

                    // The collector models AddMemoryCache/AddLogging as instance-backed
                    // because the framework implementation type is opaque — but the container
                    // does create and dispose MemoryCache and LoggerFactory, so their locations
                    // must not be mistaken for real pre-built instances. AddHttpContextAccessor
                    // and AddHttpClient register implementations the container never disposes
                    // (neither is IDisposable), so they deliberately stay out.
                    if (TryGetInvokedMethodNameText(invocation, out var invokedName) &&
                        invokedName is "AddMemoryCache" or "AddLogging")
                    {
                        var location = invocation.GetLocation();
                        frameworkExtensionLocations.TryAdd(
                            (
                                location.SourceTree?.FilePath ?? string.Empty,
                                location.SourceSpan.Start,
                                location.SourceSpan.Length),
                            0);
                    }

                    AnalyzeDisposeInvocation(
                        invocation,
                        syntaxContext.SemanticModel,
                        wellKnownTypes,
                        memberCandidates,
                        parameterCandidates,
                        resolvedCandidates);
                },
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var declaration = (LocalDeclarationStatementSyntax)syntaxContext.Node;
                    if (declaration.UsingKeyword.IsKind(SyntaxKind.None))
                    {
                        return;
                    }

                    CollectUsingDeclarationCandidates(
                        declaration.Declaration,
                        syntaxContext.SemanticModel,
                        wellKnownTypes,
                        resolvedCandidates);
                },
                SyntaxKind.LocalDeclarationStatement);

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var usingStatement = (UsingStatementSyntax)syntaxContext.Node;
                    if (usingStatement.Expression is not null &&
                        UnwrapReceiver(usingStatement.Expression, syntaxContext.SemanticModel, wellKnownTypes)
                            is InvocationExpressionSyntax expressionInvocation &&
                        TryGetResolvedServiceType(
                            expressionInvocation,
                            syntaxContext.SemanticModel,
                            wellKnownTypes,
                            out var expressionServiceType))
                    {
                        resolvedCandidates.Enqueue(new ResolvedServiceDisposal(
                            expressionServiceType,
                            expressionInvocation.GetLocation()));
                    }

                    if (usingStatement.Declaration is not null)
                    {
                        CollectUsingDeclarationCandidates(
                            usingStatement.Declaration,
                            syntaxContext.SemanticModel,
                            wellKnownTypes,
                            resolvedCandidates);
                    }
                },
                SyntaxKind.UsingStatement);

            compilationContext.RegisterCompilationEndAction(endContext =>
            {
                // A registration removed by a later unconditional Clear/RemoveAll/Replace in the
                // same flow never reaches the provider, so neither ownership proof may count it.
                var allRegistrations = registrationCollector.AllRegistrations.ToList();
                var registrationCandidates = registrationCollector.RegistrationCandidates.ToList();
                var definitelyRemoved = DefinitelyRemovedRegistrationSet.Create(
                    endContext.Compilation,
                    allRegistrations,
                    registrationCandidates,
                    registrationCollector.OrderedMutations);
                var registrations = definitelyRemoved
                    .GetEffectiveRegistrations(allRegistrations, registrationCandidates)
                    .ToImmutableArray();
                var semanticModels = new Dictionary<SyntaxTree, SemanticModel>();
                var reportedLocations = new HashSet<(string FilePath, int Start, int Length)>();

                foreach (var candidate in resolvedCandidates)
                {
                    var lifetime = GetProvenContainerLifetime(
                        candidate.ServiceType,
                        registrations,
                        lifetimeClassifier,
                        wellKnownTypes,
                        frameworkExtensionLocations);
                    if (lifetime != ServiceLifetime.Singleton)
                    {
                        continue;
                    }

                    Report(
                        endContext,
                        DiagnosticDescriptors.ContainerOwnedResolvedServiceDisposed,
                        candidate.Location,
                        reportedLocations,
                        candidate.ServiceType.Name);
                }

                foreach (var candidate in parameterCandidates)
                {
                    var lifetime = GetProvenContainerLifetime(
                        candidate.Parameter.Type,
                        registrations,
                        lifetimeClassifier,
                        wellKnownTypes,
                        frameworkExtensionLocations);
                    if (lifetime is not (ServiceLifetime.Singleton or ServiceLifetime.Scoped))
                    {
                        continue;
                    }

                    if (!ConsumerIsTornDownBeforeDependency(
                            candidate.ContainingType,
                            lifetime.Value,
                            registrations))
                    {
                        continue;
                    }

                    Report(
                        endContext,
                        DiagnosticDescriptors.ContainerOwnedInjectedServiceDisposed,
                        candidate.Location,
                        reportedLocations,
                        candidate.Parameter.Type.Name,
                        LifetimeWord(lifetime.Value));
                }

                foreach (var candidate in memberCandidates)
                {
                    var memberType = GetMemberType(candidate.Member);
                    var lifetime = GetProvenContainerLifetime(
                        memberType,
                        registrations,
                        lifetimeClassifier,
                        wellKnownTypes,
                        frameworkExtensionLocations);
                    if (lifetime is not (ServiceLifetime.Singleton or ServiceLifetime.Scoped))
                    {
                        continue;
                    }

                    if (!ConsumerIsTornDownBeforeDependency(
                            candidate.ContainingType,
                            lifetime.Value,
                            registrations))
                    {
                        continue;
                    }

                    if (!IsMemberAssignedOnlyFromConstructorParameters(
                            candidate.Member,
                            candidate.ContainingType,
                            memberType,
                            endContext.Compilation,
                            semanticModels))
                    {
                        continue;
                    }

                    Report(
                        endContext,
                        DiagnosticDescriptors.ContainerOwnedInjectedServiceDisposed,
                        candidate.Location,
                        reportedLocations,
                        memberType!.Name,
                        LifetimeWord(lifetime.Value));
                }
            });
        });
    }

    private static void Report(
        CompilationAnalysisContext context,
        DiagnosticDescriptor descriptor,
        Location location,
        HashSet<(string FilePath, int Start, int Length)> reportedLocations,
        params object[] messageArgs)
    {
        var key = (
            location.SourceTree?.FilePath ?? string.Empty,
            location.SourceSpan.Start,
            location.SourceSpan.Length);
        if (!reportedLocations.Add(key))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, messageArgs));
    }

    private static string LifetimeWord(ServiceLifetime lifetime) =>
        lifetime == ServiceLifetime.Singleton ? "singleton" : "scoped";

    private static ITypeSymbol? GetMemberType(ISymbol member) =>
        member switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };

    private static void AnalyzeDisposeInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        ConcurrentQueue<InjectedMemberDisposal> memberCandidates,
        ConcurrentQueue<InjectedParameterDisposal> parameterCandidates,
        ConcurrentQueue<ResolvedServiceDisposal> resolvedCandidates)
    {
        if (!TryGetDisposeReceiver(invocation, semanticModel, wellKnownTypes, out var receiverExpression))
        {
            return;
        }

        var receiver = UnwrapReceiver(receiverExpression, semanticModel, wellKnownTypes);
        var reportLocation =
            invocation.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
            conditionalAccess.WhenNotNull == invocation
                ? conditionalAccess.GetLocation()
                : invocation.GetLocation();

        // `provider.GetRequiredService<T>().Dispose()` — disposal of a freshly resolved service.
        if (receiver is InvocationExpressionSyntax resolutionInvocation)
        {
            if (TryGetResolvedServiceType(
                    resolutionInvocation,
                    semanticModel,
                    wellKnownTypes,
                    out var resolvedServiceType))
            {
                resolvedCandidates.Enqueue(new ResolvedServiceDisposal(
                    resolvedServiceType,
                    reportLocation));
            }

            return;
        }

        var receiverSymbol = semanticModel.GetSymbolInfo(receiver).Symbol;

        // `if (_owns) _dep.Dispose()` with an ownership flag the constructor supplied is the
        // dual-use leaveOpen idiom: the container path fills the default (non-owning) value, so
        // the guarded call never runs for container-built instances.
        if (receiverSymbol is IParameterSymbol or IFieldSymbol or IPropertySymbol &&
            IsGuardedByConstructionProvidedFlag(invocation, semanticModel))
        {
            return;
        }

        switch (receiverSymbol)
        {
            case IParameterSymbol parameter:
                if (IsQualifyingConstructorParameter(parameter) &&
                    !IsParameterReassigned(parameter))
                {
                    parameterCandidates.Enqueue(new InjectedParameterDisposal(
                        parameter,
                        parameter.ContainingType,
                        reportLocation));
                }

                break;

            // Only a this-rooted access proves the disposed member belongs to the instance the
            // container built; `other._dep.Dispose()` may target a manually composed object
            // whose whole graph the caller owns.
            case IFieldSymbol field:
                if (!field.IsStatic &&
                    field.ContainingType is not null &&
                    IsThisRootedMemberAccess(receiver))
                {
                    memberCandidates.Enqueue(new InjectedMemberDisposal(
                        field,
                        field.ContainingType,
                        reportLocation));
                }

                break;

            case IPropertySymbol property:
                if (!property.IsStatic &&
                    property.ContainingType is not null &&
                    IsThisRootedMemberAccess(receiver))
                {
                    memberCandidates.Enqueue(new InjectedMemberDisposal(
                        property,
                        property.ContainingType,
                        reportLocation));
                }

                break;

            case ILocalSymbol local:
                AnalyzeLocalDisposal(local, invocation, semanticModel, wellKnownTypes, resolvedCandidates);
                break;
        }
    }

    private static bool IsThisRootedMemberAccess(ExpressionSyntax receiver) =>
        receiver is IdentifierNameSyntax ||
        receiver is MemberAccessExpressionSyntax thisAccess &&
        thisAccess.Expression is ThisExpressionSyntax;

    /// <summary>
    /// True when the disposal sits under an <c>if</c> whose condition reads a Boolean the
    /// constructor supplied — a `bool owns` parameter, or a bool member assigned from one.
    /// A latch flag like `_disposed` is assigned in methods, not from constructor parameters,
    /// so it never suppresses.
    /// </summary>
    private static bool IsGuardedByConstructionProvidedFlag(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        for (SyntaxNode? node = invocation.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case MethodDeclarationSyntax:
                case ConstructorDeclarationSyntax:
                case AccessorDeclarationSyntax:
                case LocalFunctionStatementSyntax:
                case AnonymousFunctionExpressionSyntax:
                case PropertyDeclarationSyntax:
                    return false;

                case IfStatementSyntax ifStatement
                    when ConditionReadsConstructionProvidedFlag(ifStatement.Condition, semanticModel):
                    return true;
            }
        }

        return false;
    }

    private static bool ConditionReadsConstructionProvidedFlag(
        ExpressionSyntax condition,
        SemanticModel semanticModel)
    {
        foreach (var identifier in condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            switch (symbol)
            {
                case IParameterSymbol parameter
                    when parameter.Type.SpecialType == SpecialType.System_Boolean &&
                         IsQualifyingConstructorParameter(parameter):
                    return true;

                case IFieldSymbol field
                    when field.Type.SpecialType == SpecialType.System_Boolean &&
                         !field.IsStatic &&
                         IsBooleanMemberAssignedFromConstructorParameter(field, semanticModel):
                    return true;

                case IPropertySymbol property
                    when property.Type.SpecialType == SpecialType.System_Boolean &&
                         !property.IsStatic &&
                         IsBooleanMemberAssignedFromConstructorParameter(property, semanticModel):
                    return true;
            }
        }

        return false;
    }

    private static bool IsBooleanMemberAssignedFromConstructorParameter(
        ISymbol member,
        SemanticModel semanticModel)
    {
        foreach (var reference in member.ContainingType.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not TypeDeclarationSyntax typeDeclaration ||
                typeDeclaration.SyntaxTree != semanticModel.SyntaxTree)
            {
                continue;
            }

            foreach (var node in typeDeclaration.DescendantNodes())
            {
                ExpressionSyntax? value = node switch
                {
                    AssignmentExpressionSyntax assignment
                        when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                             IsAssignmentToMember(assignment.Left, member, semanticModel) =>
                        assignment.Right,
                    EqualsValueClauseSyntax initializer
                        when initializer.Parent is VariableDeclaratorSyntax declarator &&
                             SymbolEqualityComparer.Default.Equals(
                                 semanticModel.GetDeclaredSymbol(declarator),
                                 member) =>
                        initializer.Value,
                    _ => null,
                };

                if (value is null)
                {
                    continue;
                }

                if (StripTrivialWrappers(value) is IdentifierNameSyntax valueIdentifier &&
                    semanticModel.GetSymbolInfo(valueIdentifier).Symbol is IParameterSymbol parameter &&
                    parameter.Type.SpecialType == SpecialType.System_Boolean &&
                    IsQualifyingConstructorParameter(parameter))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Matches a zero-argument <c>Dispose()</c> or <c>DisposeAsync()</c> call and returns the
    /// receiver expression it is invoked on. Conditional-access (`x?.Dispose()`) is supported for
    /// the direct single-step form only.
    /// </summary>
    private static bool TryGetDisposeReceiver(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        out ExpressionSyntax receiverExpression)
    {
        receiverExpression = null!;

        if (invocation.ArgumentList.Arguments.Count != 0)
        {
            return false;
        }

        SimpleNameSyntax? methodName;
        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax memberAccess
                when memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                methodName = memberAccess.Name;
                receiverExpression = memberAccess.Expression;
                break;

            case MemberBindingExpressionSyntax memberBinding
                when invocation.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
                     conditionalAccess.WhenNotNull == invocation:
                methodName = memberBinding.Name;
                receiverExpression = conditionalAccess.Expression;
                break;

            default:
                return false;
        }

        var name = methodName.Identifier.ValueText;
        if (name is not ("Dispose" or "DisposeAsync"))
        {
            return false;
        }

        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol ||
            methodSymbol.Parameters.Length != 0)
        {
            return false;
        }

        // A method that merely shares the Dispose/DisposeAsync name is an ordinary method: the
        // container only disposes instances through the actual disposal interfaces, so the
        // consumer calling such a method is not tearing down container-run disposal.
        return name == "Dispose"
            ? ImplementsDisposalInterfaceMember(methodSymbol, wellKnownTypes.IDisposable, "Dispose")
            : ImplementsDisposalInterfaceMember(
                methodSymbol,
                wellKnownTypes.IAsyncDisposable,
                "DisposeAsync");
    }

    private static bool ImplementsDisposalInterfaceMember(
        IMethodSymbol method,
        INamedTypeSymbol? disposalInterface,
        string memberName)
    {
        if (disposalInterface is null)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(method.ContainingType, disposalInterface))
        {
            return true;
        }

        var interfaceMember = disposalInterface
            .GetMembers(memberName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(candidate => candidate.Parameters.Length == 0);
        if (interfaceMember is null ||
            method.ContainingType.FindImplementationForInterfaceMember(interfaceMember)
                is not IMethodSymbol implementation)
        {
            return false;
        }

        // The bound method and the mapped implementation may sit at different points of one
        // override chain (a virtual Dispose(bool)-pattern base and its override).
        for (var candidate = method; candidate is not null; candidate = candidate.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, implementation))
            {
                return true;
            }
        }

        for (var candidate = implementation;
            candidate is not null;
            candidate = candidate.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, method))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Unwraps parentheses, null-forgiving operators, and casts to the disposal interfaces, so
    /// `((IDisposable)_dep).Dispose()` and `(_dep as IAsyncDisposable)?.DisposeAsync()` resolve to
    /// the underlying dependency reference.
    /// </summary>
    private static ExpressionSyntax UnwrapReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;

                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    continue;

                case CastExpressionSyntax cast
                    when IsDisposalInterface(semanticModel.GetTypeInfo(cast.Type).Type, wellKnownTypes):
                    expression = cast.Expression;
                    continue;

                case BinaryExpressionSyntax binary
                    when binary.IsKind(SyntaxKind.AsExpression) &&
                         IsDisposalInterface(semanticModel.GetTypeInfo(binary.Right).Type, wellKnownTypes):
                    expression = binary.Left;
                    continue;

                default:
                    return expression;
            }
        }
    }

    private static bool IsDisposalInterface(ITypeSymbol? type, WellKnownTypes wellKnownTypes) =>
        type is not null &&
        (SymbolEqualityComparer.Default.Equals(type, wellKnownTypes.IDisposable) ||
         SymbolEqualityComparer.Default.Equals(type, wellKnownTypes.IAsyncDisposable));

    private static bool IsQualifyingConstructorParameter(IParameterSymbol parameter)
    {
        // `this.Dispose()` binds to the implicit `this` parameter of the enclosing method; it is
        // not an injected dependency. Defense-in-depth: the self-type's equal lifetime rank also
        // blocks it downstream, but that coupling is incidental rather than designed.
        if (parameter.IsThis)
        {
            return false;
        }

        if (parameter.ContainingSymbol is not IMethodSymbol method ||
            method.MethodKind != MethodKind.Constructor ||
            method.IsStatic)
        {
            return false;
        }

        // A keyed dependency lives in a different registration slot than the unkeyed lifetime
        // proof below inspects, so [FromKeyedServices] parameters stay out.
        return !HasFromKeyedServicesAttribute(parameter);
    }

    private static bool HasFromKeyedServicesAttribute(IParameterSymbol parameter) =>
        parameter.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.Name == "FromKeyedServicesAttribute");

    /// <summary>
    /// A constructor parameter reassigned before the disposal no longer holds the
    /// container-provided instance, so any assignment to it anywhere in the constructor —
    /// or, for a primary constructor, anywhere in the declaring type body — bails out.
    /// </summary>
    private static bool IsParameterReassigned(IParameterSymbol parameter)
    {
        foreach (var reference in parameter.ContainingSymbol.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax();
            foreach (var node in declaration.DescendantNodes())
            {
                switch (node)
                {
                    case AssignmentExpressionSyntax assignment
                        when assignment.Left is IdentifierNameSyntax identifier &&
                             identifier.Identifier.ValueText == parameter.Name:
                        return true;

                    case AssignmentExpressionSyntax deconstruction
                        when deconstruction.Left is TupleExpressionSyntax tuple &&
                             tuple.DescendantNodes()
                                 .OfType<IdentifierNameSyntax>()
                                 .Any(name => name.Identifier.ValueText == parameter.Name):
                        return true;

                    case ArgumentSyntax argument
                        when !argument.RefOrOutKeyword.IsKind(SyntaxKind.None) &&
                             argument.Expression is IdentifierNameSyntax argumentIdentifier &&
                             argumentIdentifier.Identifier.ValueText == parameter.Name:
                        return true;

                    case RefExpressionSyntax refAlias
                        when refAlias.Expression is IdentifierNameSyntax refIdentifier &&
                             refIdentifier.Identifier.ValueText == parameter.Name:
                        return true;
                }
            }
        }

        return false;
    }

    private static void AnalyzeLocalDisposal(
        ILocalSymbol local,
        InvocationExpressionSyntax disposeInvocation,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        ConcurrentQueue<ResolvedServiceDisposal> resolvedCandidates)
    {
        if (local.DeclaringSyntaxReferences.Length != 1 ||
            local.DeclaringSyntaxReferences[0].GetSyntax() is not VariableDeclaratorSyntax declarator ||
            declarator.Initializer is null)
        {
            return;
        }

        // A `using var` declaration already reports at the declaration site; a second report at
        // an explicit Dispose call on the same local would be noise.
        if (declarator.Parent?.Parent is LocalDeclarationStatementSyntax localDeclaration &&
            !localDeclaration.UsingKeyword.IsKind(SyntaxKind.None))
        {
            return;
        }

        if (declarator.Parent?.Parent is UsingStatementSyntax)
        {
            return;
        }

        if (UnwrapReceiver(declarator.Initializer.Value, semanticModel, wellKnownTypes)
                is not InvocationExpressionSyntax resolutionInvocation ||
            !TryGetResolvedServiceType(
                resolutionInvocation,
                semanticModel,
                wellKnownTypes,
                out var serviceType))
        {
            return;
        }

        if (IsLocalReassigned(local, declarator, semanticModel))
        {
            return;
        }

        resolvedCandidates.Enqueue(new ResolvedServiceDisposal(
            serviceType,
            disposeInvocation.GetLocation()));
    }

    /// <summary>
    /// Any write to the local anywhere in its declaring member — a reassignment, a compound
    /// assignment, or a `ref`/`out` argument — means the disposed value may not be the resolved
    /// instance, so the candidate is dropped.
    /// </summary>
    private static bool IsLocalReassigned(
        ILocalSymbol local,
        VariableDeclaratorSyntax declarator,
        SemanticModel semanticModel)
    {
        SyntaxNode? boundary = declarator.FirstAncestorOrSelf<MemberDeclarationSyntax>(
            member => member is not GlobalStatementSyntax);
        boundary ??= declarator.SyntaxTree.GetRoot();

        foreach (var node in boundary.DescendantNodes())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax assignment
                    when assignment.Left is IdentifierNameSyntax identifier &&
                         identifier.Identifier.ValueText == local.Name &&
                         SymbolEqualityComparer.Default.Equals(
                             semanticModel.GetSymbolInfo(identifier).Symbol,
                             local):
                    return true;

                case ArgumentSyntax argument
                    when !argument.RefOrOutKeyword.IsKind(SyntaxKind.None) &&
                         argument.Expression is IdentifierNameSyntax argumentIdentifier &&
                         argumentIdentifier.Identifier.ValueText == local.Name &&
                         SymbolEqualityComparer.Default.Equals(
                             semanticModel.GetSymbolInfo(argumentIdentifier).Symbol,
                             local):
                    return true;

                case AssignmentExpressionSyntax deconstruction
                    when deconstruction.Left is TupleExpressionSyntax tuple &&
                         tuple.DescendantNodes()
                             .OfType<IdentifierNameSyntax>()
                             .Any(name =>
                                 name.Identifier.ValueText == local.Name &&
                                 SymbolEqualityComparer.Default.Equals(
                                     semanticModel.GetSymbolInfo(name).Symbol,
                                     local)):
                    return true;

                case RefExpressionSyntax refAlias
                    when refAlias.Expression is IdentifierNameSyntax refIdentifier &&
                         refIdentifier.Identifier.ValueText == local.Name &&
                         SymbolEqualityComparer.Default.Equals(
                             semanticModel.GetSymbolInfo(refIdentifier).Symbol,
                             local):
                    return true;
            }
        }

        return false;
    }

    private static void CollectUsingDeclarationCandidates(
        VariableDeclarationSyntax declaration,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        ConcurrentQueue<ResolvedServiceDisposal> resolvedCandidates)
    {
        foreach (var declarator in declaration.Variables)
        {
            if (declarator.Initializer is not null &&
                UnwrapReceiver(declarator.Initializer.Value, semanticModel, wellKnownTypes)
                    is InvocationExpressionSyntax resolutionInvocation &&
                TryGetResolvedServiceType(
                    resolutionInvocation,
                    semanticModel,
                    wellKnownTypes,
                    out var serviceType))
            {
                resolvedCandidates.Enqueue(new ResolvedServiceDisposal(
                    serviceType,
                    resolutionInvocation.GetLocation()));
            }
        }
    }

    /// <summary>
    /// Recognizes the exact framework generic <c>GetService&lt;T&gt;</c> /
    /// <c>GetRequiredService&lt;T&gt;</c> extensions on <c>IServiceProvider</c>. User-defined
    /// helpers with the same names stay silent.
    /// </summary>
    private static bool TryGetResolvedServiceType(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        out ITypeSymbol serviceType)
    {
        serviceType = null!;

        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method ||
            method.Name is not ("GetService" or "GetRequiredService") ||
            !method.IsGenericMethod ||
            method.TypeArguments.Length != 1 ||
            !method.IsExtensionMethod)
        {
            return false;
        }

        var definition = method.ReducedFrom ?? method;
        if (definition.Parameters.Length == 0 ||
            !wellKnownTypes.IsServiceProvider(definition.Parameters[0].Type))
        {
            return false;
        }

        if (!IsFrameworkServiceResolutionExtension(method, semanticModel.Compilation))
        {
            return false;
        }

        // The framework extension binds on any IServiceProvider, including hand-rolled providers
        // whose results are caller-owned. Only a receiver whose natural type is the framework
        // provider interface (or Microsoft's own container class) proves the instance came from
        // the container the registrations describe.
        ExpressionSyntax? receiverExpression =
            method.ReducedFrom is not null &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess
                ? memberAccess.Expression
                : invocation.ArgumentList.Arguments.Count > 0
                    ? invocation.ArgumentList.Arguments[0].Expression
                    : null;
        if (receiverExpression is null)
        {
            return false;
        }

        var strippedReceiver = StripTrivialWrappers(receiverExpression);
        var receiverType = semanticModel.GetTypeInfo(strippedReceiver).Type;
        if (!wellKnownTypes.IsServiceProvider(receiverType) &&
            !IsMicrosoftServiceProviderClass(receiverType))
        {
            return false;
        }

        // A provider built and owned by the resolving member itself is a throwaway container:
        // its one consumer disposes everything in the same frame, so disposing the resolution
        // early is a contractually idempotent double dispose, not a shared-instance defect.
        if (IsThrowawayProviderReceiver(strippedReceiver, semanticModel))
        {
            return false;
        }

        serviceType = method.TypeArguments[0];
        return true;
    }

    private static bool IsThrowawayProviderReceiver(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel)
    {
        if (receiverExpression is InvocationExpressionSyntax directBuild)
        {
            return IsBuildServiceProviderInvocation(directBuild, semanticModel);
        }

        if (receiverExpression is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol local &&
            local.DeclaringSyntaxReferences.Length == 1 &&
            local.DeclaringSyntaxReferences[0].GetSyntax() is VariableDeclaratorSyntax declarator &&
            declarator.Initializer is not null &&
            StripTrivialWrappers(declarator.Initializer.Value)
                is InvocationExpressionSyntax initializerInvocation)
        {
            return IsBuildServiceProviderInvocation(initializerInvocation, semanticModel);
        }

        return false;
    }

    private static bool IsBuildServiceProviderInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel) =>
        semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
        method.Name == "BuildServiceProvider" &&
        method.ContainingType.ContainingNamespace.ToDisplayString() ==
            "Microsoft.Extensions.DependencyInjection";

    private static bool IsMicrosoftServiceProviderClass(ITypeSymbol? type) =>
        type?.Name == "ServiceProvider" &&
        type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";

    private static bool IsFrameworkServiceResolutionExtension(IMethodSymbol method, Compilation compilation)
    {
        if (method.ContainingType.Name != "ServiceProviderServiceExtensions" ||
            method.ContainingType.ContainingNamespace.ToDisplayString() !=
            "Microsoft.Extensions.DependencyInjection")
        {
            return false;
        }

        if (!method.ContainingType.Locations.Any(location => location.IsInSource))
        {
            return method.ContainingAssembly.Name == "Microsoft.Extensions.DependencyInjection.Abstractions";
        }

        // A source-declared stand-in for the framework type only counts when the real assembly is
        // not referenced (analyzer test harnesses); with the real framework present, the stand-in
        // is a user-defined helper.
        return !compilation.SourceModule.ReferencedAssemblySymbols.Any(assembly =>
            assembly.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions") is not null);
    }

    /// <summary>
    /// Proves the container both constructs the consumer (so its constructor arguments are
    /// container-supplied) and tears it down strictly before the dependency's owner does. A
    /// consumer with the same lifetime as its dependency is co-disposed with it — the same
    /// benign teardown double-dispose the resolved tier's scoped exclusion and DI026's
    /// same-scope reasoning already accept — so only a strictly shorter-lived consumer reports:
    /// its Dispose runs while the shared instance is still live for everyone else.
    /// </summary>
    private static bool ConsumerIsTornDownBeforeDependency(
        INamedTypeSymbol containingType,
        ServiceLifetime dependencyLifetime,
        ImmutableArray<ServiceRegistration> registrations)
    {
        var sawConsumerRegistration = false;

        foreach (var registration in registrations)
        {
            // Only the registration that wins its service-type slot decides how the consumer is
            // built: a type-based default overridden by a later factory is factory-built at
            // runtime, and a factory's constructor arguments may be caller-owned.
            if (!IsSlotWinner(registration, registrations))
            {
                continue;
            }

            var isConsumerConstruction =
                !registration.HasImplementationInstance &&
                registration.ImplementationType is not null &&
                SymbolEqualityComparer.Default.Equals(registration.ImplementationType, containingType);
            var isConsumerSlot =
                SymbolEqualityComparer.Default.Equals(registration.ServiceType, containingType) ||
                isConsumerConstruction;

            if (!isConsumerSlot)
            {
                continue;
            }

            if (!isConsumerConstruction)
            {
                // The winning registration for this consumer is a factory or an instance, so
                // container construction of the disposing type is not proven for this slot.
                continue;
            }

            sawConsumerRegistration = true;
            if (LifetimeRank(registration.Lifetime) >= LifetimeRank(dependencyLifetime))
            {
                return false;
            }
        }

        return sawConsumerRegistration;
    }

    /// <summary>
    /// Mirrors the collector's effective-slot selection: within one service-type slot the last
    /// non-prepended registration wins single resolution.
    /// </summary>
    private static bool IsSlotWinner(
        ServiceRegistration registration,
        ImmutableArray<ServiceRegistration> registrations)
    {
        ServiceRegistration? winner = null;
        ServiceRegistration? first = null;
        foreach (var candidate in registrations)
        {
            if (candidate.IsKeyed != registration.IsKeyed ||
                !Equals(candidate.Key, registration.Key) ||
                !SymbolEqualityComparer.Default.Equals(candidate.ServiceType, registration.ServiceType))
            {
                continue;
            }

            first ??= candidate;
            if (!candidate.PrependToCollection)
            {
                winner = candidate;
            }
        }

        return ReferenceEquals(winner ?? first, registration);
    }

    private static int LifetimeRank(ServiceLifetime lifetime) =>
        lifetime switch
        {
            ServiceLifetime.Transient => 0,
            ServiceLifetime.Scoped => 1,
            _ => 2,
        };

    /// <summary>
    /// Proves the container owns instances of <paramref name="serviceType"/>: every unkeyed
    /// registration of the type agrees on a singleton or scoped lifetime and none hands the
    /// container a pre-built instance (disposing a pre-built instance deliberately is DI033's
    /// documented remediation, not a defect). Falls back to framework-known lifetimes when the
    /// type has no source registration.
    /// </summary>
    private static ServiceLifetime? GetProvenContainerLifetime(
        ITypeSymbol? serviceType,
        ImmutableArray<ServiceRegistration> registrations,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        WellKnownTypes wellKnownTypes,
        ConcurrentDictionary<(string FilePath, int Start, int Length), byte> frameworkExtensionLocations)
    {
        if (serviceType is not INamedTypeSymbol namedType)
        {
            return null;
        }

        // Scopes and providers have their own disposal rules (DI001, DI014); they are never
        // this rule's finding.
        if (wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(namedType) ||
            wellKnownTypes.IsServiceScope(namedType) ||
            wellKnownTypes.IsAsyncServiceScope(namedType) ||
            wellKnownTypes.IsServiceCollection(namedType))
        {
            return null;
        }

        INamedTypeSymbol? openGeneric = null;
        if (namedType.IsGenericType && !namedType.IsUnboundGenericType)
        {
            openGeneric = namedType.ConstructUnboundGenericType();
        }

        ServiceLifetime? provenLifetime = null;
        var sawRegistration = false;
        foreach (var registration in registrations)
        {
            if (registration.IsKeyed)
            {
                continue;
            }

            var matches =
                SymbolEqualityComparer.Default.Equals(registration.ServiceType, namedType) ||
                openGeneric is not null &&
                SymbolEqualityComparer.Default.Equals(registration.ServiceType, openGeneric);
            if (!matches)
            {
                continue;
            }

            sawRegistration = true;

            if (registration.HasImplementationInstance &&
                !IsFrameworkExtensionRegistration(registration, frameworkExtensionLocations))
            {
                return null;
            }

            if (registration.Lifetime is not (ServiceLifetime.Singleton or ServiceLifetime.Scoped))
            {
                return null;
            }

            if (provenLifetime is null)
            {
                provenLifetime = registration.Lifetime;
            }
            else if (provenLifetime != registration.Lifetime)
            {
                // Mixed singleton/scoped registrations of the same service type leave the
                // shared-instance claim ambiguous.
                return null;
            }
        }

        if (sawRegistration)
        {
            return provenLifetime;
        }

        // The classifier knows many framework lifetimes, but only MemoryCache and LoggerFactory
        // among them are implementations the container actually disposes; the rest either are
        // not disposable or are host-registered instances the container never tears down.
        if (lifetimeClassifier.TryGetLifetime(namedType, isKeyed: false, out var knownLifetime) &&
            knownLifetime == ServiceLifetime.Singleton &&
            (wellKnownTypes.IsMemoryCache(namedType) || wellKnownTypes.IsLoggerFactory(namedType)))
        {
            return knownLifetime;
        }

        return null;
    }

    private static bool IsFrameworkExtensionRegistration(
        ServiceRegistration registration,
        ConcurrentDictionary<(string FilePath, int Start, int Length), byte> frameworkExtensionLocations)
    {
        var key = (
            registration.Location.SourceTree?.FilePath ?? string.Empty,
            registration.Location.SourceSpan.Start,
            registration.Location.SourceSpan.Length);
        return frameworkExtensionLocations.ContainsKey(key);
    }

    /// <summary>
    /// Extracts the invoked method's simple name from the syntax alone, without binding.
    /// </summary>
    private static bool TryGetInvokedMethodNameText(
        InvocationExpressionSyntax invocation,
        out string name)
    {
        name = null!;
        SimpleNameSyntax? nameSyntax = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            SimpleNameSyntax simpleName => simpleName,
            _ => null,
        };

        if (nameSyntax is null)
        {
            return false;
        }

        name = nameSyntax.Identifier.ValueText;
        return true;
    }

    /// <summary>
    /// Proves the member holds a container-supplied instance: every assignment to it, in every
    /// declaration of the type, is either a direct reference to a constructor parameter of the
    /// dependency's own type or a null/default clear-out. A field initializer, a factory call, an
    /// object creation, or any other computed value means the consumer may own the instance.
    /// </summary>
    private static bool IsMemberAssignedOnlyFromConstructorParameters(
        ISymbol member,
        INamedTypeSymbol containingType,
        ITypeSymbol? memberType,
        Compilation compilation,
        Dictionary<SyntaxTree, SemanticModel> semanticModels)
    {
        if (memberType is null)
        {
            return false;
        }

        if (member is IPropertySymbol property &&
            !IsAutoProperty(property))
        {
            return false;
        }

        // The assignment scan only sees the containing type's own declarations, so it is complete
        // only for members that cannot be assigned from outside the type: a private or readonly
        // field, or a property without an externally accessible setter.
        if (!IsAssignmentScanComplete(member))
        {
            return false;
        }

        var sawConstructorParameterAssignment = false;

        foreach (var reference in containingType.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not TypeDeclarationSyntax typeDeclaration)
            {
                return false;
            }

            var semanticModel = GetSemanticModel(compilation, typeDeclaration.SyntaxTree, semanticModels);

            // A constructor that chains with `: this(...)` can route caller-created arguments
            // into the assigning constructor, so ownership is no longer provable.
            foreach (var constructor in typeDeclaration.Members.OfType<ConstructorDeclarationSyntax>())
            {
                if (constructor.Initializer is not null &&
                    constructor.Initializer.IsKind(SyntaxKind.ThisConstructorInitializer))
                {
                    return false;
                }
            }

            foreach (var node in typeDeclaration.DescendantNodes())
            {
                switch (node)
                {
                    // A field or property initializer runs during construction, so an initializer
                    // that references a primary-constructor parameter is a container-supplied
                    // assignment; any other non-null initializer value means the consumer may own
                    // the instance.
                    case EqualsValueClauseSyntax initializer
                        when initializer.Parent is VariableDeclaratorSyntax fieldDeclarator &&
                             SymbolEqualityComparer.Default.Equals(
                                 semanticModel.GetDeclaredSymbol(fieldDeclarator),
                                 member):
                        if (!ClassifyAssignedValue(
                                initializer.Value,
                                containingType,
                                memberType,
                                semanticModel,
                                ref sawConstructorParameterAssignment))
                        {
                            return false;
                        }

                        break;

                    case EqualsValueClauseSyntax initializer
                        when initializer.Parent is PropertyDeclarationSyntax propertyDeclaration &&
                             SymbolEqualityComparer.Default.Equals(
                                 semanticModel.GetDeclaredSymbol(propertyDeclaration),
                                 member):
                        if (!ClassifyAssignedValue(
                                initializer.Value,
                                containingType,
                                memberType,
                                semanticModel,
                                ref sawConstructorParameterAssignment))
                        {
                            return false;
                        }

                        break;

                    case AssignmentExpressionSyntax assignment
                        when IsAssignmentToMember(assignment.Left, member, semanticModel):
                        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                        {
                            return false;
                        }

                        if (!ClassifyAssignedValue(
                                assignment.Right,
                                containingType,
                                memberType,
                                semanticModel,
                                ref sawConstructorParameterAssignment))
                        {
                            return false;
                        }

                        break;

                    // A deconstruction (`(_dep, x) = ...`) writes the member with a value the
                    // proof cannot classify, and a `ref`/`out` argument hands the member to a
                    // callee that can rebind it.
                    case AssignmentExpressionSyntax deconstruction
                        when deconstruction.Left is TupleExpressionSyntax tuple &&
                             TupleWritesMember(tuple, member, semanticModel):
                        return false;

                    case ArgumentSyntax argument
                        when !argument.RefOrOutKeyword.IsKind(SyntaxKind.None) &&
                             IsAssignmentToMember(argument.Expression, member, semanticModel):
                        return false;

                    // A `ref` alias (`ref var r = ref _dep`) can rebind the member through
                    // another name, and a null-conditional assignment (`other?._dep = ...`)
                    // writes it through a receiver this scan cannot classify.
                    case RefExpressionSyntax refAlias
                        when IsAssignmentToMember(refAlias.Expression, member, semanticModel):
                        return false;

                    case AssignmentExpressionSyntax conditionalWrite
                        when conditionalWrite.Left is ConditionalAccessExpressionSyntax conditionalLeft &&
                             SymbolEqualityComparer.Default.Equals(
                                 semanticModel.GetSymbolInfo(conditionalLeft.WhenNotNull).Symbol,
                                 member):
                        return false;

                    // C# 14 parses `other?._dep = value` with the assignment nested inside the
                    // conditional access, so the left side is a member binding.
                    case AssignmentExpressionSyntax nestedConditionalWrite
                        when nestedConditionalWrite.Left is MemberBindingExpressionSyntax memberBinding &&
                             SymbolEqualityComparer.Default.Equals(
                                 semanticModel.GetSymbolInfo(memberBinding).Symbol,
                                 member):
                        return false;
                }
            }
        }

        return sawConstructorParameterAssignment;
    }

    /// <summary>
    /// Classifies a value assigned to the guarded member. Returns false when the value breaks the
    /// ownership proof; null/default clear-outs are ignored, and a direct reference to a
    /// qualifying constructor parameter of the dependency's own type records the proof.
    /// </summary>
    private static bool ClassifyAssignedValue(
        ExpressionSyntax valueExpression,
        INamedTypeSymbol containingType,
        ITypeSymbol memberType,
        SemanticModel semanticModel,
        ref bool sawConstructorParameterAssignment)
    {
        var value = StripTrivialWrappers(valueExpression);
        if (IsNullOrDefault(value))
        {
            return true;
        }

        if (value is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbolInfo(identifier).Symbol is IParameterSymbol parameter &&
            IsQualifyingConstructorParameter(parameter) &&
            // A parameter rebound anywhere in the constructor may hold a consumer-owned value
            // (the wrap-then-store decorator idiom) by the time it reaches the member.
            !IsParameterReassigned(parameter) &&
            SymbolEqualityComparer.Default.Equals(
                parameter.ContainingSymbol.ContainingType,
                containingType) &&
            SymbolEqualityComparer.Default.Equals(parameter.Type, memberType))
        {
            sawConstructorParameterAssignment = true;
            return true;
        }

        return false;
    }

    private static bool IsAssignmentScanComplete(ISymbol member)
    {
        switch (member)
        {
            case IFieldSymbol field:
                return field.IsReadOnly || field.DeclaredAccessibility == Accessibility.Private;

            case IPropertySymbol property:
                if (property.DeclaredAccessibility == Accessibility.Private)
                {
                    return true;
                }

                // An init-only setter is still assignable from outside the type (object
                // initializers, `with` clones), which this scan never sees.
                var setter = property.SetMethod;
                return setter is null ||
                       !setter.IsInitOnly &&
                       setter.DeclaredAccessibility == Accessibility.Private;

            default:
                return false;
        }
    }

    private static bool TupleWritesMember(
        TupleExpressionSyntax tuple,
        ISymbol member,
        SemanticModel semanticModel)
    {
        foreach (var argument in tuple.Arguments)
        {
            if (argument.Expression is TupleExpressionSyntax nested)
            {
                if (TupleWritesMember(nested, member, semanticModel))
                {
                    return true;
                }

                continue;
            }

            if (IsAssignmentToMember(argument.Expression, member, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAssignmentToMember(
        ExpressionSyntax left,
        ISymbol member,
        SemanticModel semanticModel)
    {
        var target = StripTrivialWrappers(left);
        if (target is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(
            semanticModel.GetSymbolInfo(target).Symbol,
            member);
    }

    private static ExpressionSyntax StripTrivialWrappers(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;

                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    continue;

                default:
                    return expression;
            }
        }
    }

    private static bool IsNullOrDefault(ExpressionSyntax expression)
    {
        var value = StripTrivialWrappers(expression);
        return value.IsKind(SyntaxKind.NullLiteralExpression) ||
               value.IsKind(SyntaxKind.DefaultLiteralExpression) ||
               value is DefaultExpressionSyntax;
    }

    private static bool IsAutoProperty(IPropertySymbol property)
    {
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not PropertyDeclarationSyntax declaration)
            {
                return false;
            }

            if (declaration.ExpressionBody is not null || declaration.AccessorList is null)
            {
                return false;
            }

            foreach (var accessor in declaration.AccessorList.Accessors)
            {
                if (accessor.Body is not null || accessor.ExpressionBody is not null)
                {
                    return false;
                }
            }
        }

        return property.DeclaringSyntaxReferences.Length > 0;
    }

    private static SemanticModel GetSemanticModel(
        Compilation compilation,
        SyntaxTree syntaxTree,
        Dictionary<SyntaxTree, SemanticModel> semanticModels)
    {
        if (semanticModels.TryGetValue(syntaxTree, out var cached))
        {
            return cached;
        }

        // Compilation-end reporting needs semantic answers for trees whose per-tree analysis has
        // already finished; this mirrors the established compilation-end suppression used by
        // DI003/DI016/DI036.
        #pragma warning disable RS1030
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        #pragma warning restore RS1030
        semanticModels[syntaxTree] = semanticModel;
        return semanticModel;
    }

    private sealed class InjectedMemberDisposal
    {
        public InjectedMemberDisposal(ISymbol member, INamedTypeSymbol containingType, Location location)
        {
            Member = member;
            ContainingType = containingType;
            Location = location;
        }

        public ISymbol Member { get; }

        public INamedTypeSymbol ContainingType { get; }

        public Location Location { get; }
    }

    private sealed class InjectedParameterDisposal
    {
        public InjectedParameterDisposal(
            IParameterSymbol parameter,
            INamedTypeSymbol containingType,
            Location location)
        {
            Parameter = parameter;
            ContainingType = containingType;
            Location = location;
        }

        public IParameterSymbol Parameter { get; }

        public INamedTypeSymbol ContainingType { get; }

        public Location Location { get; }
    }

    private sealed class ResolvedServiceDisposal
    {
        public ResolvedServiceDisposal(ITypeSymbol serviceType, Location location)
        {
            ServiceType = serviceType;
            Location = location;
        }

        public ITypeSymbol ServiceType { get; }

        public Location Location { get; }
    }
}
