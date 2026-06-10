using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects when scoped services escape their scope lifetime.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI002_ScopeEscapeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ScopedServiceEscapes);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var wellKnownTypes = WellKnownTypes.Create(compilationContext.Compilation);
            if (wellKnownTypes is null)
            {
                return;
            }

            var registrationCollector = RegistrationCollector.Create(compilationContext.Compilation);
            if (registrationCollector is null)
            {
                return;
            }

            var executableRoots = new ConcurrentBag<SyntaxNode>();
            var semanticModelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    registrationCollector.AnalyzeInvocation(
                        (InvocationExpressionSyntax)syntaxContext.Node,
                        syntaxContext.SemanticModel);

                    semanticModelsByTree.TryAdd(
                        syntaxContext.SemanticModel.SyntaxTree,
                        syntaxContext.SemanticModel);
                },
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    executableRoots.Add(syntaxContext.Node);
                    semanticModelsByTree.TryAdd(
                        syntaxContext.SemanticModel.SyntaxTree,
                        syntaxContext.SemanticModel);
                },
                ExecutableSyntaxHelper.ExecutableRootKinds);

            compilationContext.RegisterCompilationEndAction(
                endContext =>
                {
                    foreach (var executableRoot in executableRoots)
                    {
                        if (!semanticModelsByTree.TryGetValue(executableRoot.SyntaxTree, out var semanticModel))
                        {
                            continue;
                        }

                        AnalyzeExecutableRoot(
                            endContext,
                            executableRoot,
                            semanticModel,
                            wellKnownTypes,
                            registrationCollector);
                    }
                });
        });
    }

    private static void AnalyzeExecutableRoot(
        CompilationAnalysisContext context,
        SyntaxNode executableRoot,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        RegistrationCollector registrationCollector)
    {
        if (!ExecutableSyntaxHelper.TryGetExecutableBody(executableRoot, out var executableBody))
        {
            return;
        }

        var scopeVariables = CollectScopeVariables(executableBody, semanticModel, wellKnownTypes);
        if (scopeVariables.Count == 0)
        {
            return;
        }

        var providerAliases = CollectScopeProviderAliases(executableBody, semanticModel, scopeVariables);
        var serviceVariables = new Dictionary<ILocalSymbol, InvocationExpressionSyntax>(SymbolEqualityComparer.Default);
        var capturedDelegateVariables = new Dictionary<ILocalSymbol, InvocationExpressionSyntax>(SymbolEqualityComparer.Default);
        var reportedSpans = new HashSet<TextSpan>();

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node is not InvocationExpressionSyntax invocation ||
                !TryGetResolutionLifetime(
                    invocation,
                    semanticModel,
                    scopeVariables,
                    providerAliases,
                    registrationCollector,
                    wellKnownTypes,
                    out var lifetime) ||
                !ShouldReportScopedEscape(lifetime))
            {
                continue;
            }

            // A conditional-access resolution (`scope?.ServiceProvider.GetRequiredService<T>()`)
            // hangs its consumption shape (initializer, assignment, return) off the enclosing
            // ConditionalAccessExpressionSyntax rather than the invocation itself.
            var consumption = GetConsumptionExpression(invocation);

            if (consumption.Parent is EqualsValueClauseSyntax equalsValue &&
                equalsValue.Parent is VariableDeclaratorSyntax declarator &&
                semanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol localSymbol)
            {
                serviceVariables[localSymbol] = invocation;
            }

            if (consumption.Parent is AssignmentExpressionSyntax assignment &&
                assignment.Left is IdentifierNameSyntax assignmentIdentifier &&
                semanticModel.GetSymbolInfo(assignmentIdentifier).Symbol is ILocalSymbol assignedLocalSymbol)
            {
                serviceVariables[assignedLocalSymbol] = invocation;
            }

            if (consumption.Parent is ReturnStatementSyntax)
            {
                ReportDiagnostic(context, invocation, "return", reportedSpans);
            }

            if (consumption.Parent is AssignmentExpressionSyntax fieldAssignment &&
                semanticModel.GetSymbolInfo(fieldAssignment.Left).Symbol is IFieldSymbol fieldSymbol)
            {
                ReportDiagnostic(context, invocation, fieldSymbol.Name, reportedSpans);
            }

            if (consumption.Parent is AssignmentExpressionSyntax propertyAssignment &&
                semanticModel.GetSymbolInfo(propertyAssignment.Left).Symbol is IPropertySymbol propertySymbol)
            {
                ReportDiagnostic(context, invocation, propertySymbol.Name, reportedSpans);
            }

            if (consumption.Parent is AssignmentExpressionSyntax parameterAssignment &&
                semanticModel.GetSymbolInfo(parameterAssignment.Left).Symbol is IParameterSymbol parameterSymbol &&
                IsEscapingParameter(parameterSymbol))
            {
                ReportDiagnostic(context, invocation, parameterSymbol.Name, reportedSpans);
            }

            // _publisher.Changed += scope.ServiceProvider.GetRequiredService<T>().Handle — a
            // method group taken directly on the resolution, subscribed to an event whose owner
            // outlives the scope.
            if (consumption.Parent is MemberAccessExpressionSyntax inlineMethodGroup &&
                inlineMethodGroup.Expression == consumption &&
                inlineMethodGroup.Parent is AssignmentExpressionSyntax inlineSubscription &&
                inlineSubscription.IsKind(SyntaxKind.AddAssignmentExpression) &&
                inlineSubscription.Right == inlineMethodGroup &&
                semanticModel.GetSymbolInfo(inlineMethodGroup).Symbol is IMethodSymbol &&
                semanticModel.GetSymbolInfo(inlineSubscription.Left).Symbol is IEventSymbol inlineEventSymbol &&
                EventReceiverOutlivesScope(inlineSubscription.Left, semanticModel))
            {
                ReportDiagnostic(context, invocation, inlineEventSymbol.Name, reportedSpans);
            }

            // _cache.Add(scope.ServiceProvider.GetRequiredService<T>()) — the resolution is an
            // argument to a mutation method on a field/property-held container that outlives
            // the scope.
            if (consumption.Parent is ArgumentSyntax argumentShape &&
                argumentShape.Parent is ArgumentListSyntax argumentList &&
                argumentList.Parent is InvocationExpressionSyntax mutationInvocation &&
                TryGetFieldCollectionMutation(mutationInvocation, semanticModel, out var directContainerName))
            {
                ReportDiagnostic(context, invocation, directContainerName, reportedSpans);
            }
        }

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            TrackServiceAlias(node, semanticModel, serviceVariables);
            TrackDelegateCapture(node, semanticModel, serviceVariables, capturedDelegateVariables);

            if (node is ReturnStatementSyntax returnStmt &&
                TryGetTrackedLocalReference(returnStmt.Expression, semanticModel, serviceVariables, out var sourceInvocation))
            {
                ReportDiagnostic(context, sourceInvocation, "return", reportedSpans);
            }

            if (node is ReturnStatementSyntax delegateReturnStmt &&
                TryGetCapturedDelegateSource(
                    delegateReturnStmt.Expression,
                    semanticModel,
                    serviceVariables,
                    capturedDelegateVariables,
                    out var delegateReturnSource))
            {
                ReportDiagnostic(context, delegateReturnSource, "return", reportedSpans);
            }

            if (node is AssignmentExpressionSyntax fieldAssignment &&
                TryGetTrackedLocalReference(fieldAssignment.Right, semanticModel, serviceVariables, out var source) &&
                semanticModel.GetSymbolInfo(fieldAssignment.Left).Symbol is IFieldSymbol field)
            {
                ReportDiagnostic(context, source, field.Name, reportedSpans);
            }

            if (node is AssignmentExpressionSyntax delegateFieldAssignment &&
                TryGetCapturedDelegateSource(
                    delegateFieldAssignment.Right,
                    semanticModel,
                    serviceVariables,
                    capturedDelegateVariables,
                    out var delegateFieldSource) &&
                semanticModel.GetSymbolInfo(delegateFieldAssignment.Left).Symbol is IFieldSymbol delegateField)
            {
                ReportDiagnostic(context, delegateFieldSource, delegateField.Name, reportedSpans);
            }

            if (node is AssignmentExpressionSyntax propertyAssignment &&
                TryGetTrackedLocalReference(propertyAssignment.Right, semanticModel, serviceVariables, out var propertySource) &&
                semanticModel.GetSymbolInfo(propertyAssignment.Left).Symbol is IPropertySymbol property)
            {
                ReportDiagnostic(context, propertySource, property.Name, reportedSpans);
            }

            if (node is AssignmentExpressionSyntax delegatePropertyAssignment &&
                TryGetCapturedDelegateSource(
                    delegatePropertyAssignment.Right,
                    semanticModel,
                    serviceVariables,
                    capturedDelegateVariables,
                    out var delegatePropertySource) &&
                semanticModel.GetSymbolInfo(delegatePropertyAssignment.Left).Symbol is IPropertySymbol delegateProperty)
            {
                ReportDiagnostic(context, delegatePropertySource, delegateProperty.Name, reportedSpans);
            }

            if (node is AssignmentExpressionSyntax parameterAssignment &&
                TryGetTrackedLocalReference(parameterAssignment.Right, semanticModel, serviceVariables, out var parameterSource) &&
                semanticModel.GetSymbolInfo(parameterAssignment.Left).Symbol is IParameterSymbol parameter &&
                IsEscapingParameter(parameter))
            {
                ReportDiagnostic(context, parameterSource, parameter.Name, reportedSpans);
            }

            if (node is AssignmentExpressionSyntax delegateParameterAssignment &&
                TryGetCapturedDelegateSource(
                    delegateParameterAssignment.Right,
                    semanticModel,
                    serviceVariables,
                    capturedDelegateVariables,
                    out var delegateParameterSource) &&
                semanticModel.GetSymbolInfo(delegateParameterAssignment.Left).Symbol is IParameterSymbol delegateParameter &&
                IsEscapingParameter(delegateParameter))
            {
                ReportDiagnostic(context, delegateParameterSource, delegateParameter.Name, reportedSpans);
            }

            // _cache.Add(service) / _cache.Insert(0, service) — a tracked service or capturing
            // delegate handed to a mutation method on a field/property-held container.
            if (node is InvocationExpressionSyntax mutationCall &&
                TryGetFieldCollectionMutation(mutationCall, semanticModel, out var containerName))
            {
                foreach (var argument in mutationCall.ArgumentList.Arguments)
                {
                    // The resolution must precede the mutation in document order — a local
                    // reassigned to a scoped resolution only after the Add call escaped its
                    // previous (untracked) value.
                    if (TryGetTrackedLocalReference(
                            argument.Expression, semanticModel, serviceVariables, out var collectionSource) &&
                        collectionSource.SpanStart < mutationCall.SpanStart)
                    {
                        ReportDiagnostic(context, collectionSource, containerName, reportedSpans);
                    }
                    else if (TryGetCapturedDelegateSource(
                                 argument.Expression,
                                 semanticModel,
                                 serviceVariables,
                                 capturedDelegateVariables,
                                 out var delegateCollectionSource) &&
                             delegateCollectionSource.SpanStart < mutationCall.SpanStart)
                    {
                        ReportDiagnostic(context, delegateCollectionSource, containerName, reportedSpans);
                    }
                }
            }

            // publisher.Changed += service.Handle / += capturingDelegate — subscribing a handler
            // bound to the scoped service onto an event whose owner outlives the scope keeps the
            // service reachable (and invocable) after disposal.
            if (node is AssignmentExpressionSyntax subscription &&
                subscription.IsKind(SyntaxKind.AddAssignmentExpression) &&
                semanticModel.GetSymbolInfo(subscription.Left).Symbol is IEventSymbol eventSymbol &&
                EventReceiverOutlivesScope(subscription.Left, semanticModel) &&
                TryGetSubscribedServiceSource(
                    subscription.Right,
                    semanticModel,
                    serviceVariables,
                    capturedDelegateVariables,
                    out var subscriptionSource) &&
                subscriptionSource.SpanStart < subscription.SpanStart)
            {
                ReportDiagnostic(context, subscriptionSource, eventSymbol.Name, reportedSpans);
            }
        }
    }

    private static readonly ImmutableHashSet<string> CollectionMutationMethodNames =
        ImmutableHashSet.Create("Add", "Insert", "Enqueue", "Push", "TryAdd");

    /// <summary>
    /// Matches mutation calls on containers held by fields or properties — storage that outlives
    /// the scope. Local containers stay quiet: they live and die with the scope unless they
    /// escape through one of the other sinks.
    /// </summary>
    private static bool TryGetFieldCollectionMutation(
        InvocationExpressionSyntax call,
        SemanticModel semanticModel,
        out string containerName)
    {
        containerName = string.Empty;

        // `_cache.Add(...)` is a MemberAccessExpressionSyntax; `_cache?.Add(...)` is a
        // MemberBindingExpressionSyntax whose receiver is the enclosing conditional access.
        SimpleNameSyntax methodName;
        ExpressionSyntax receiverExpression;
        if (call.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            methodName = memberAccess.Name;
            receiverExpression = memberAccess.Expression;
        }
        else if (call.Expression is MemberBindingExpressionSyntax memberBinding &&
                 FindOwningConditionalAccess(call) is { } conditionalAccess)
        {
            methodName = memberBinding.Name;
            receiverExpression = conditionalAccess.Expression;
        }
        else
        {
            return false;
        }

        if (!CollectionMutationMethodNames.Contains(methodName.Identifier.ValueText))
        {
            return false;
        }

        // Mutating collection methods return void (List.Add, Insert, Enqueue, Push), bool
        // (ConcurrentDictionary.TryAdd), or int (non-generic IList.Add). Value-returning Add
        // shapes (ImmutableList.Add, fluent builders) hand back a new value instead of storing
        // into the receiver, so the discarded result does not retain the service.
        if (semanticModel.GetSymbolInfo(call).Symbol is not IMethodSymbol method ||
            !(method.ReturnsVoid ||
              method.ReturnType.SpecialType is SpecialType.System_Boolean or SpecialType.System_Int32))
        {
            return false;
        }

        // The receiver must actually be a collection — an ordinary field-held object with a
        // method named Insert/Add (a repository persisting data) is a method argument, which
        // this rule documents as out of scope.
        if (semanticModel.GetTypeInfo(receiverExpression).Type is not { } receiverType ||
            !IsEnumerableLike(receiverType))
        {
            return false;
        }

        // The chain's ROOT must outlive the scope: wrapper.Items dies with a scope-local
        // wrapper even though Items is a property.
        var root = receiverExpression;
        while (root is MemberAccessExpressionSyntax nestedAccess)
        {
            root = nestedAccess.Expression;
        }

        if (root != receiverExpression &&
            root is not ThisExpressionSyntax &&
            semanticModel.GetSymbolInfo(root).Symbol
                is not (IFieldSymbol or IPropertySymbol or IParameterSymbol or INamedTypeSymbol))
        {
            return false;
        }

        var receiver = semanticModel.GetSymbolInfo(receiverExpression).Symbol;
        if (receiver is IFieldSymbol or IPropertySymbol)
        {
            containerName = receiver.Name;
            return true;
        }

        return false;
    }

    /// <summary>The nearest conditional access whose WhenNotNull contains the given node.</summary>
    private static ConditionalAccessExpressionSyntax? FindOwningConditionalAccess(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is ConditionalAccessExpressionSyntax conditionalAccess &&
                conditionalAccess.WhenNotNull.Span.Contains(node.Span))
            {
                return conditionalAccess;
            }
        }

        return null;
    }

    private static bool IsEnumerableLike(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Collections_IEnumerable)
        {
            return true;
        }

        if (type is INamedTypeSymbol named && named.ConstructedFrom.SpecialType ==
            SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return true;
        }

        return type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable);
    }

    /// <summary>
    /// The subscription escapes only when the event's owner outlives the scope: the enclosing
    /// instance (identifier/this form), or a receiver held by a field, property, or parameter.
    /// A publisher local to the scope dies with it.
    /// </summary>
    private static bool EventReceiverOutlivesScope(ExpressionSyntax left, SemanticModel semanticModel)
    {
        if (left is MemberAccessExpressionSyntax memberAccess)
        {
            // Classify the ROOT of the receiver chain: wrapper.Publisher.Changed dies with a
            // scope-local wrapper even though Publisher is a property.
            var root = memberAccess.Expression;
            while (root is MemberAccessExpressionSyntax nested)
            {
                root = nested.Expression;
            }

            if (root is ThisExpressionSyntax)
            {
                return true;
            }

            // A type receiver is a static event: it outlives every scope.
            return semanticModel.GetSymbolInfo(root).Symbol
                is IFieldSymbol or IPropertySymbol or IParameterSymbol or INamedTypeSymbol;
        }

        // Bare identifier: an event on the enclosing instance itself.
        return left is IdentifierNameSyntax;
    }

    /// <summary>
    /// The subscribed handler carries the scoped service when it is a method group on a tracked
    /// service local (service.Handle) or a previously captured delegate local.
    /// </summary>
    private static bool TryGetSubscribedServiceSource(
        ExpressionSyntax right,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> capturedDelegateVariables,
        out InvocationExpressionSyntax source)
    {
        // Method groups on tracked service locals are recognized (with an IMethodSymbol gate,
        // so delegate-valued properties that do not retain the service stay quiet) by the
        // shared captured-delegate classification.
        return TryGetCapturedDelegateSource(
            right, semanticModel, serviceVariables, capturedDelegateVariables, out source);
    }

    private static bool IsEscapingParameter(IParameterSymbol parameter) =>
        parameter.RefKind is RefKind.Ref or RefKind.Out;

    /// <summary>
    /// Returns the expression whose parent decides how the resolved service is consumed: the
    /// invocation itself, or the outermost enclosing conditional access that evaluates it
    /// (`scope?.ServiceProvider.GetRequiredService<T>()`).
    /// </summary>
    private static SyntaxNode GetConsumptionExpression(InvocationExpressionSyntax invocation)
    {
        SyntaxNode current = invocation;
        while (current.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
               conditionalAccess.WhenNotNull == current)
        {
            current = conditionalAccess;
        }

        return current;
    }

    /// <summary>
    /// Unwraps a conditional-access creation expression (`factory?.CreateScope()`) to the
    /// invocation it evaluates, or returns the expression itself when it already is one.
    /// </summary>
    private static InvocationExpressionSyntax? UnwrapToInvocation(ExpressionSyntax? expression) =>
        expression switch
        {
            InvocationExpressionSyntax invocation => invocation,
            ConditionalAccessExpressionSyntax conditionalAccess => UnwrapToInvocation(conditionalAccess.WhenNotNull),
            _ => null,
        };

    /// <summary>
    /// Resolves the receiver that a conditional-access member binding is evaluated against: for
    /// `scope?.ServiceProvider` the `.ServiceProvider` binding's receiver is `scope`. The owning
    /// conditional access is the nearest ancestor whose <c>WhenNotNull</c> contains the binding --
    /// the binding can also be the <c>Expression</c> of an inner chained conditional access, which
    /// is not its owner.
    /// </summary>
    private static ExpressionSyntax? TryGetConditionalAccessReceiver(MemberBindingExpressionSyntax memberBinding)
    {
        for (SyntaxNode? node = memberBinding.Parent; node is not null; node = node.Parent)
        {
            if (node is ConditionalAccessExpressionSyntax conditionalAccess &&
                conditionalAccess.WhenNotNull.Span.Contains(memberBinding.Span))
            {
                return conditionalAccess.Expression;
            }

            if (node is StatementSyntax or MemberDeclarationSyntax)
            {
                break;
            }
        }

        return null;
    }

    private static HashSet<ILocalSymbol> CollectScopeVariables(
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        var scopeVariables = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node is LocalDeclarationStatementSyntax localDecl &&
                (localDecl.UsingKeyword != default || localDecl.AwaitKeyword != default))
            {
                foreach (var variable in localDecl.Declaration.Variables)
                {
                    if (UnwrapToInvocation(variable.Initializer?.Value) is { } invocation &&
                        IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes) &&
                        semanticModel.GetDeclaredSymbol(variable) is ILocalSymbol localSymbol)
                    {
                        scopeVariables.Add(localSymbol);
                    }
                }
            }

            if (node is UsingStatementSyntax usingStmt &&
                TryGetScopeSymbolFromUsingStatement(usingStmt, semanticModel, wellKnownTypes, out var scopeSymbol))
            {
                scopeVariables.Add(scopeSymbol);
            }
        }

        return scopeVariables;
    }

    private static Dictionary<ILocalSymbol, ILocalSymbol> CollectScopeProviderAliases(
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeVariables)
    {
        var providerAliases = new Dictionary<ILocalSymbol, ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node is LocalDeclarationStatementSyntax localDeclaration)
            {
                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    if (semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol)
                    {
                        continue;
                    }

                    if (variable.Initializer?.Value is null ||
                        !TryResolveProviderScope(
                            variable.Initializer.Value,
                            semanticModel,
                            scopeVariables,
                            providerAliases,
                            out var scopeSymbol))
                    {
                        providerAliases.Remove(localSymbol);
                        continue;
                    }

                    providerAliases[localSymbol] = scopeSymbol;
                }
            }

            if (node is AssignmentExpressionSyntax assignment &&
                assignment.Left is IdentifierNameSyntax assignmentIdentifier &&
                semanticModel.GetSymbolInfo(assignmentIdentifier).Symbol is ILocalSymbol assignmentLocal)
            {
                if (!TryResolveProviderScope(
                        assignment.Right,
                        semanticModel,
                        scopeVariables,
                        providerAliases,
                        out var resolvedScope))
                {
                    providerAliases.Remove(assignmentLocal);
                    continue;
                }

                providerAliases[assignmentLocal] = resolvedScope;
            }
        }

        return providerAliases;
    }

    private static bool TryGetScopeSymbolFromUsingStatement(
        UsingStatementSyntax usingStmt,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        out ILocalSymbol scopeSymbol)
    {
        scopeSymbol = null!;

        if (usingStmt.Declaration is not null)
        {
            foreach (var variable in usingStmt.Declaration.Variables)
            {
                if (UnwrapToInvocation(variable.Initializer?.Value) is { } invocation &&
                    IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes) &&
                    semanticModel.GetDeclaredSymbol(variable) is ILocalSymbol localSymbol)
                {
                    scopeSymbol = localSymbol;
                    return true;
                }
            }
        }

        return TryGetExistingScopeSymbol(usingStmt.Expression, semanticModel, wellKnownTypes, out scopeSymbol);
    }

    private static bool TryGetExistingScopeSymbol(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        out ILocalSymbol scopeSymbol)
    {
        scopeSymbol = null!;

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return TryGetExistingScopeSymbol(parenthesized.Expression, semanticModel, wellKnownTypes, out scopeSymbol);
        }

        if (expression is not IdentifierNameSyntax identifierName ||
            semanticModel.GetSymbolInfo(identifierName).Symbol is not ILocalSymbol localSymbol)
        {
            return false;
        }

        foreach (var syntaxReference in localSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is VariableDeclaratorSyntax { Initializer.Value: { } initializer } &&
                UnwrapToInvocation(initializer) is { } invocation &&
                IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes))
            {
                scopeSymbol = localSymbol;
                return true;
            }
        }

        return false;
    }

    private static bool IsCreateScopeInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        if (methodSymbol.Name is not ("CreateScope" or "CreateAsyncScope"))
        {
            return false;
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var containingType = sourceMethod.ContainingType;

        if (containingType is null)
        {
            return false;
        }

        if (wellKnownTypes.IsServiceScopeFactory(containingType))
        {
            return true;
        }

        return containingType.Name == "ServiceProviderServiceExtensions" &&
               containingType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }

    private static bool TryGetResolutionLifetime(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeVariables,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        out ServiceLifetime? lifetime)
    {
        lifetime = null;

        if (!TryGetResolvedServiceInfo(
                invocation,
                semanticModel,
                scopeVariables,
                providerAliases,
                wellKnownTypes,
                out var serviceType,
                out var key,
                out var isKeyed))
        {
            return false;
        }

        if (serviceType is null)
        {
            return true;
        }

        lifetime = registrationCollector.GetLifetime(serviceType, key, isKeyed);
        return true;
    }

    private static bool TryGetResolvedServiceInfo(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeVariables,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases,
        WellKnownTypes wellKnownTypes,
        out ITypeSymbol? serviceType,
        out object? key,
        out bool isKeyed)
    {
        serviceType = null;
        key = null;
        isKeyed = false;

        // The provider is the receiver of the resolution member itself: the member-access
        // expression, or -- for `provider?.GetRequiredService<T>()` member bindings -- the
        // enclosing conditional access's expression.
        ExpressionSyntax? providerExpression = invocation.Expression switch
        {
            MemberAccessExpressionSyntax outerMember => outerMember.Expression,
            MemberBindingExpressionSyntax when invocation.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
                                               conditionalAccess.WhenNotNull == invocation => conditionalAccess.Expression,
            _ => null,
        };

        if (providerExpression is null ||
            !TryResolveProviderScope(
                providerExpression,
                semanticModel,
                scopeVariables,
                providerAliases,
                out _))
        {
            return false;
        }

        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        if (!IsServiceResolutionMethod(methodSymbol, wellKnownTypes))
        {
            return false;
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        isKeyed = sourceMethod.Name is "GetKeyedService" or "GetRequiredKeyedService";

        serviceType = GetResolvedServiceType(invocation, methodSymbol, semanticModel);
        if (isKeyed)
        {
            key = ExtractKey(invocation, methodSymbol, semanticModel);
        }

        return true;
    }

    private static bool TryResolveProviderScope(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeVariables,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases,
        out ILocalSymbol scopeSymbol)
    {
        scopeSymbol = null!;

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return TryResolveProviderScope(
                parenthesized.Expression,
                semanticModel,
                scopeVariables,
                providerAliases,
                out scopeSymbol);
        }

        // `scope?.ServiceProvider` evaluates to the scope's provider when not null.
        if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
        {
            return TryResolveProviderScope(
                conditionalAccess.WhenNotNull,
                semanticModel,
                scopeVariables,
                providerAliases,
                out scopeSymbol);
        }

        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "ServiceProvider" &&
            memberAccess.Expression is IdentifierNameSyntax scopeIdentifier &&
            semanticModel.GetSymbolInfo(scopeIdentifier).Symbol is ILocalSymbol directScope &&
            scopeVariables.Contains(directScope))
        {
            scopeSymbol = directScope;
            return true;
        }

        // Conditional-access form: in `scope?.ServiceProvider.GetRequiredService<T>()` the
        // provider surfaces as a `.ServiceProvider` member binding whose receiver is the
        // enclosing conditional access's expression.
        if (expression is MemberBindingExpressionSyntax memberBinding &&
            memberBinding.Name.Identifier.Text == "ServiceProvider" &&
            TryGetConditionalAccessReceiver(memberBinding) is IdentifierNameSyntax bindingScopeIdentifier &&
            semanticModel.GetSymbolInfo(bindingScopeIdentifier).Symbol is ILocalSymbol bindingScope &&
            scopeVariables.Contains(bindingScope))
        {
            scopeSymbol = bindingScope;
            return true;
        }

        if (expression is IdentifierNameSyntax identifierName &&
            semanticModel.GetSymbolInfo(identifierName).Symbol is ILocalSymbol providerLocal &&
            providerAliases.TryGetValue(providerLocal, out var resolvedScope))
        {
            scopeSymbol = resolvedScope;
            return true;
        }

        return false;
    }

    private static void TrackServiceAlias(
        SyntaxNode node,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables)
    {
        if (node is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (variable.Initializer?.Value is not IdentifierNameSyntax identifier ||
                    semanticModel.GetSymbolInfo(identifier).Symbol is not ILocalSymbol sourceLocal ||
                    !serviceVariables.TryGetValue(sourceLocal, out var sourceInvocation) ||
                    semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol aliasLocal)
                {
                    continue;
                }

                serviceVariables[aliasLocal] = sourceInvocation;
            }
        }

        if (node is AssignmentExpressionSyntax assignment &&
            assignment.Left is IdentifierNameSyntax leftIdentifier &&
            semanticModel.GetSymbolInfo(leftIdentifier).Symbol is ILocalSymbol leftLocal)
        {
            if (assignment.Right is InvocationExpressionSyntax directInvocation &&
                serviceVariables.TryGetValue(leftLocal, out var existingInvocation) &&
                existingInvocation == directInvocation)
            {
                return;
            }

            if (assignment.Right is not IdentifierNameSyntax rightIdentifier ||
                semanticModel.GetSymbolInfo(rightIdentifier).Symbol is not ILocalSymbol rightLocal ||
                !serviceVariables.TryGetValue(rightLocal, out var invocation))
            {
                serviceVariables.Remove(leftLocal);
                return;
            }

            serviceVariables[leftLocal] = invocation;
        }
    }

    private static void TrackDelegateCapture(
        SyntaxNode node,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> capturedDelegateVariables)
    {
        if (node is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol delegateLocal)
                {
                    continue;
                }

                if (TryGetCapturedDelegateSource(
                    variable.Initializer?.Value,
                    semanticModel,
                    serviceVariables,
                    capturedDelegateVariables,
                    out var sourceInvocation))
                {
                    capturedDelegateVariables[delegateLocal] = sourceInvocation;
                }
            }
        }

        if (node is AssignmentExpressionSyntax assignment &&
            assignment.Left is IdentifierNameSyntax leftIdentifier &&
            semanticModel.GetSymbolInfo(leftIdentifier).Symbol is ILocalSymbol assignedDelegate)
        {
            if (TryGetCapturedDelegateSource(
                assignment.Right,
                semanticModel,
                serviceVariables,
                capturedDelegateVariables,
                out var sourceInvocation))
            {
                capturedDelegateVariables[assignedDelegate] = sourceInvocation;
                return;
            }

            capturedDelegateVariables.Remove(assignedDelegate);
        }
    }

    private static bool TryGetTrackedLocalReference(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        out InvocationExpressionSyntax sourceInvocation)
    {
        sourceInvocation = null!;

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return TryGetTrackedLocalReference(parenthesized.Expression, semanticModel, serviceVariables, out sourceInvocation);
        }

        if (expression is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol localSymbol &&
            serviceVariables.TryGetValue(localSymbol, out sourceInvocation))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetCapturedDelegateSource(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> capturedDelegateVariables,
        out InvocationExpressionSyntax sourceInvocation)
    {
        sourceInvocation = null!;

        if (expression is null)
        {
            return false;
        }

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return TryGetCapturedDelegateSource(
                parenthesized.Expression,
                semanticModel,
                serviceVariables,
                capturedDelegateVariables,
                out sourceInvocation);
        }

        if (expression is AnonymousFunctionExpressionSyntax anonymousFunction)
        {
            return TryFindCapturedServiceSource(anonymousFunction, semanticModel, serviceVariables, out sourceInvocation);
        }

        if (expression is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol localSymbol &&
            capturedDelegateVariables.TryGetValue(localSymbol, out sourceInvocation))
        {
            return true;
        }

        // A method group on a tracked service local (service.Handle) converts to a delegate
        // bound to the scoped instance — the same capture as a lambda closing over it. Unlike a
        // lambda, the receiver binds at CONVERSION time, so the resolution must precede the
        // method group in document order; a delegate created before the local was reassigned to
        // the scoped resolution is still bound to the previous instance.
        if (expression is MemberAccessExpressionSyntax methodGroup &&
            methodGroup.Expression is IdentifierNameSyntax receiverIdentifier &&
            semanticModel.GetSymbolInfo(methodGroup).Symbol is IMethodSymbol &&
            semanticModel.GetSymbolInfo(receiverIdentifier).Symbol is ILocalSymbol receiverLocal &&
            serviceVariables.TryGetValue(receiverLocal, out sourceInvocation) &&
            sourceInvocation.SpanStart < methodGroup.SpanStart)
        {
            return true;
        }

        sourceInvocation = null!;
        return false;
    }

    private static bool TryFindCapturedServiceSource(
        AnonymousFunctionExpressionSyntax anonymousFunction,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        out InvocationExpressionSyntax sourceInvocation)
    {
        foreach (var identifier in anonymousFunction.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol localSymbol &&
                serviceVariables.TryGetValue(localSymbol, out sourceInvocation))
            {
                return true;
            }
        }

        sourceInvocation = null!;
        return false;
    }

    private static bool IsServiceResolutionMethod(IMethodSymbol methodSymbol, WellKnownTypes wellKnownTypes)
    {
        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var methodName = sourceMethod.Name;
        if (methodName is not ("GetService" or "GetRequiredService" or "GetKeyedService" or "GetRequiredKeyedService"))
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
                wellKnownTypes.IsKeyedServiceProvider(receiverType))
            {
                return true;
            }
        }

        return IsSystemIServiceProvider(containingType) ||
               wellKnownTypes.IsKeyedServiceProvider(containingType);
    }

    private static bool IsSystemIServiceProvider(ITypeSymbol type)
    {
        return type.Name == "IServiceProvider" &&
               type.ContainingNamespace.ToDisplayString() == "System";
    }

    private static ITypeSymbol? GetResolvedServiceType(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel)
    {
        if (methodSymbol.IsGenericMethod && methodSymbol.TypeArguments.Length > 0)
        {
            return methodSymbol.TypeArguments[0];
        }

        var serviceTypeExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "serviceType") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "type");
        if (serviceTypeExpression is TypeOfExpressionSyntax typeOfExpression)
        {
            return semanticModel.GetTypeInfo(typeOfExpression.Type).Type;
        }

        return null;
    }

    private static object? ExtractKey(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel)
    {
        var keyExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "serviceKey") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "key");

        if (keyExpression is null && invocation.ArgumentList.Arguments.Count >= 2)
        {
            keyExpression = invocation.ArgumentList.Arguments[1].Expression;
        }

        if (keyExpression is null)
        {
            return null;
        }

        var constant = semanticModel.GetConstantValue(keyExpression);
        return constant.HasValue ? constant.Value : null;
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

            var argumentIndex = isReducedExtension ? i - 1 : i;
            if (argumentIndex >= 0 && argumentIndex < invocation.ArgumentList.Arguments.Count)
            {
                return invocation.ArgumentList.Arguments[argumentIndex].Expression;
            }
        }

        return null;
    }

    private static bool ShouldReportScopedEscape(ServiceLifetime? lifetime)
    {
        // DI002 targets scoped services escaping their scope. If lifetime is unknown,
        // skip reporting to avoid false positives when registration metadata is incomplete.
        return lifetime is ServiceLifetime.Scoped;
    }

    private static void ReportDiagnostic(
        CompilationAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string escapeTarget,
        HashSet<TextSpan> reportedSpans)
    {
        if (!reportedSpans.Add(invocation.Span))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.ScopedServiceEscapes,
            invocation.GetLocation(),
            escapeTarget);

        context.ReportDiagnostic(diagnostic);
    }
}
