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
        var serviceScopeVariables = new Dictionary<ILocalSymbol, ILocalSymbol>(SymbolEqualityComparer.Default);
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
                    out var lifetime,
                    out var scopeSymbol) ||
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
                serviceScopeVariables[localSymbol] = scopeSymbol;
            }

            if (consumption.Parent is AssignmentExpressionSyntax assignment &&
                assignment.Left is IdentifierNameSyntax assignmentIdentifier &&
                semanticModel.GetSymbolInfo(assignmentIdentifier).Symbol is ILocalSymbol assignedLocalSymbol)
            {
                serviceVariables[assignedLocalSymbol] = invocation;
                serviceScopeVariables[assignedLocalSymbol] = scopeSymbol;
            }

            if (consumption.Parent is ReturnStatementSyntax returnStatement &&
                !ReturnTransfersScope(
                    returnStatement.Expression,
                    scopeSymbol,
                    semanticModel,
                    invocation.Span,
                    trackedLocal: null,
                    returnStatement,
                    executableBody,
                    returnStatement.SpanStart))
            {
                ReportDiagnostic(context, invocation, "return", reportedSpans);
            }

            // return (resolution, x); / return new { Service = resolution }; — the composite
            // hands the service out with the return.
            if (IsInsideReturnedComposite(consumption) &&
                GetEnclosingReturnStatement(consumption) is { } compositeReturnStatement &&
                !ReturnTransfersScope(
                    compositeReturnStatement.Expression,
                    scopeSymbol,
                    semanticModel,
                    invocation.Span,
                    trackedLocal: null,
                    compositeReturnStatement,
                    executableBody,
                    compositeReturnStatement.SpanStart))
            {
                ReportDiagnostic(context, invocation, "return", reportedSpans);
            }

            if (consumption.Parent is AssignmentExpressionSyntax fieldAssignment &&
                TryGetAssignmentTargetSymbol(fieldAssignment.Left, semanticModel, out var fieldTargetSymbol) &&
                fieldTargetSymbol is IFieldSymbol fieldSymbol &&
                AssignmentTargetOutlivesScope(fieldAssignment.Left, semanticModel, executableBody, fieldAssignment))
            {
                ReportDiagnostic(context, invocation, fieldSymbol.Name, reportedSpans);
            }

            if (consumption.Parent is AssignmentExpressionSyntax propertyAssignment &&
                TryGetAssignmentTargetSymbol(propertyAssignment.Left, semanticModel, out var propertyTargetSymbol) &&
                propertyTargetSymbol is IPropertySymbol propertySymbol &&
                AssignmentTargetOutlivesScope(propertyAssignment.Left, semanticModel, executableBody, propertyAssignment))
            {
                ReportDiagnostic(context, invocation, propertySymbol.Name, reportedSpans);
            }

            if (consumption.Parent is AssignmentExpressionSyntax parameterAssignment &&
                TryGetAssignmentTargetSymbol(parameterAssignment.Left, semanticModel, out var parameterTargetSymbol) &&
                parameterTargetSymbol is IParameterSymbol parameterSymbol &&
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
            // argument to a mutation method on a field/property-held container or caller-owned
            // collection parameter that outlives the scope.
            if (consumption.Parent is ArgumentSyntax argumentShape &&
                argumentShape.Parent is ArgumentListSyntax argumentList &&
                argumentList.Parent is InvocationExpressionSyntax mutationInvocation &&
                TryGetEscapingCollectionMutation(mutationInvocation, semanticModel, out var directContainerName))
            {
                ReportDiagnostic(context, invocation, directContainerName, reportedSpans);
            }
        }

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            TrackServiceAlias(node, semanticModel, serviceVariables, serviceScopeVariables);
            TrackDelegateCapture(node, semanticModel, serviceVariables, capturedDelegateVariables);

            if (node is ReturnStatementSyntax returnStmt &&
                TryGetTrackedLocalReference(
                    returnStmt.Expression,
                    semanticModel,
                    serviceVariables,
                    out var sourceInvocation,
                    out var returnLocal) &&
                SourceCanReachSink(sourceInvocation, returnStmt, returnLocal, semanticModel) &&
                !ReturnTransfersTrackedScope(
                    returnStmt.Expression,
                    sourceInvocation,
                    returnLocal,
                    serviceScopeVariables,
                    semanticModel,
                    returnStmt,
                    executableBody,
                    returnStmt.SpanStart))
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

            // return (service, x); / return new { Service = service }; — composite construction
            // smuggles the tracked service (or a capturing delegate) out with the return.
            if (node is ReturnStatementSyntax { Expression: { } returnExpression } compositeReturn &&
                ReturnExpressionContainsComposite(returnExpression))
            {
                ReportCompositeReturnEscapes(
                    context,
                    returnExpression,
                    returnExpression,
                    semanticModel,
                    scopeVariables,
                    providerAliases,
                    registrationCollector,
                    wellKnownTypes,
                    serviceVariables,
                    serviceScopeVariables,
                    capturedDelegateVariables,
                    reportedSpans,
                    compositeReturn,
                    executableBody,
                    compositeReturn.SpanStart);
            }

            if (node is AssignmentExpressionSyntax fieldAssignment &&
                TryGetTrackedLocalReference(
                    fieldAssignment.Right,
                    semanticModel,
                    serviceVariables,
                    out var source,
                    out var fieldAssignmentLocal) &&
                SourceCanReachSink(source, fieldAssignment, fieldAssignmentLocal, semanticModel) &&
                TryGetAssignmentTargetSymbol(fieldAssignment.Left, semanticModel, out var trackedFieldTargetSymbol) &&
                trackedFieldTargetSymbol is IFieldSymbol field &&
                AssignmentTargetOutlivesScope(fieldAssignment.Left, semanticModel, executableBody, fieldAssignment))
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
                TryGetAssignmentTargetSymbol(delegateFieldAssignment.Left, semanticModel, out var delegateFieldTargetSymbol) &&
                delegateFieldTargetSymbol is IFieldSymbol delegateField &&
                AssignmentTargetOutlivesScope(
                    delegateFieldAssignment.Left,
                    semanticModel,
                    executableBody,
                    delegateFieldAssignment))
            {
                ReportDiagnostic(context, delegateFieldSource, delegateField.Name, reportedSpans);
            }

            if (node is AssignmentExpressionSyntax propertyAssignment &&
                TryGetTrackedLocalReference(
                    propertyAssignment.Right,
                    semanticModel,
                    serviceVariables,
                    out var propertySource,
                    out var propertyAssignmentLocal) &&
                SourceCanReachSink(propertySource, propertyAssignment, propertyAssignmentLocal, semanticModel) &&
                TryGetAssignmentTargetSymbol(propertyAssignment.Left, semanticModel, out var trackedPropertyTargetSymbol) &&
                trackedPropertyTargetSymbol is IPropertySymbol property &&
                AssignmentTargetOutlivesScope(propertyAssignment.Left, semanticModel, executableBody, propertyAssignment))
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
                TryGetAssignmentTargetSymbol(delegatePropertyAssignment.Left, semanticModel, out var delegatePropertyTargetSymbol) &&
                delegatePropertyTargetSymbol is IPropertySymbol delegateProperty &&
                AssignmentTargetOutlivesScope(
                    delegatePropertyAssignment.Left,
                    semanticModel,
                    executableBody,
                    delegatePropertyAssignment))
            {
                ReportDiagnostic(context, delegatePropertySource, delegateProperty.Name, reportedSpans);
            }

            if (node is AssignmentExpressionSyntax parameterAssignment &&
                TryGetTrackedLocalReference(
                    parameterAssignment.Right,
                    semanticModel,
                    serviceVariables,
                    out var parameterSource,
                    out var parameterAssignmentLocal) &&
                SourceCanReachSink(parameterSource, parameterAssignment, parameterAssignmentLocal, semanticModel) &&
                TryGetAssignmentTargetSymbol(parameterAssignment.Left, semanticModel, out var trackedParameterTargetSymbol) &&
                trackedParameterTargetSymbol is IParameterSymbol parameter &&
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
                TryGetAssignmentTargetSymbol(delegateParameterAssignment.Left, semanticModel, out var delegateParameterTargetSymbol) &&
                delegateParameterTargetSymbol is IParameterSymbol delegateParameter &&
                IsEscapingParameter(delegateParameter))
            {
                ReportDiagnostic(context, delegateParameterSource, delegateParameter.Name, reportedSpans);
            }

            // _cache.Add(service) / _cache.Insert(0, service) — a tracked service or capturing
            // delegate handed to a mutation method on a field/property-held container or
            // caller-owned collection parameter.
            if (node is InvocationExpressionSyntax mutationCall &&
                TryGetEscapingCollectionMutation(mutationCall, semanticModel, out var containerName))
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
    private static bool TryGetEscapingCollectionMutation(
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
        if (receiver is IFieldSymbol or IPropertySymbol or IParameterSymbol)
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

    /// <summary>
    /// Walks tuple-argument and anonymous-object-member parents: true when the expression's
    /// outermost composite construction is the operand of a return statement.
    /// </summary>
    private static bool IsInsideReturnedComposite(SyntaxNode node)
    {
        var current = node.Parent;
        var sawComposite = false;
        while (current is not null)
        {
            switch (current)
            {
                case ArgumentSyntax { Parent: TupleExpressionSyntax tuple }:
                    sawComposite = true;
                    current = tuple.Parent;
                    continue;
                case AnonymousObjectMemberDeclaratorSyntax { Parent: AnonymousObjectCreationExpressionSyntax anonymous }:
                    sawComposite = true;
                    current = anonymous.Parent;
                    continue;
                case ConditionalExpressionSyntax conditional
                    when conditional.WhenTrue == current || conditional.WhenFalse == current:
                    current = conditional.Parent;
                    continue;
                case BinaryExpressionSyntax binary
                    when binary.IsKind(SyntaxKind.CoalesceExpression) &&
                         (binary.Left == current || binary.Right == current):
                    current = binary.Parent;
                    continue;
                case ReturnStatementSyntax:
                    return sawComposite;
                default:
                    return false;
            }
        }

        return false;
    }

    private static ReturnStatementSyntax? GetEnclosingReturnStatement(SyntaxNode node)
    {
        for (SyntaxNode? current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is ReturnStatementSyntax returnStatement)
            {
                return returnStatement;
            }

            if (current is StatementSyntax or MemberDeclarationSyntax)
            {
                break;
            }
        }

        return null;
    }

    private static bool ReturnTransfersTrackedScope(
        ExpressionSyntax? returnExpression,
        InvocationExpressionSyntax sourceInvocation,
        ILocalSymbol trackedLocal,
        Dictionary<ILocalSymbol, ILocalSymbol> serviceScopeVariables,
        SemanticModel semanticModel,
        ReturnStatementSyntax returnStatement,
        SyntaxNode executableBody,
        int returnSpanStart) =>
        serviceScopeVariables.TryGetValue(trackedLocal, out var scopeLocal) &&
        ReturnTransfersScope(
            returnExpression,
            scopeLocal,
            semanticModel,
            sourceInvocation.Span,
            trackedLocal,
            returnStatement,
            executableBody,
            returnSpanStart);

    private static bool ReturnTransfersScope(
        ExpressionSyntax? returnExpression,
        ILocalSymbol scopeLocal,
        SemanticModel semanticModel,
        TextSpan sourceInvocationSpan,
        ILocalSymbol? trackedLocal,
        ReturnStatementSyntax returnStatement,
        SyntaxNode executableBody,
        int returnSpanStart)
    {
        if (returnExpression is null ||
            ScopeLocalIsUsingOwned(scopeLocal) ||
            ScopeLocalIsDisposedBeforeEscape(scopeLocal, executableBody, semanticModel, returnStatement, returnSpanStart))
        {
            return false;
        }

        return ReturnExpressionTransfersScopeForSource(
            returnExpression,
            scopeLocal,
            semanticModel,
            sourceInvocationSpan,
            trackedLocal);
    }

    private static bool ReturnExpressionTransfersScopeForSource(
        ExpressionSyntax returnExpression,
        ILocalSymbol scopeLocal,
        SemanticModel semanticModel,
        TextSpan sourceInvocationSpan,
        ILocalSymbol? trackedLocal)
    {
        returnExpression = UnwrapParentheses(returnExpression);

        if (returnExpression is ConditionalExpressionSyntax conditional)
        {
            var trueContainsSource = ReturnExpressionContainsSource(
                conditional.WhenTrue,
                sourceInvocationSpan,
                trackedLocal,
                semanticModel);
            var falseContainsSource = ReturnExpressionContainsSource(
                conditional.WhenFalse,
                sourceInvocationSpan,
                trackedLocal,
                semanticModel);

            if (trueContainsSource || falseContainsSource)
            {
                return (!trueContainsSource ||
                        ReturnExpressionTransfersScopeForSource(
                            conditional.WhenTrue,
                            scopeLocal,
                            semanticModel,
                            sourceInvocationSpan,
                            trackedLocal)) &&
                       (!falseContainsSource ||
                        ReturnExpressionTransfersScopeForSource(
                            conditional.WhenFalse,
                            scopeLocal,
                            semanticModel,
                            sourceInvocationSpan,
                            trackedLocal));
            }
        }

        if (returnExpression is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.CoalesceExpression))
        {
            var leftContainsSource = ReturnExpressionContainsSource(
                binary.Left,
                sourceInvocationSpan,
                trackedLocal,
                semanticModel);
            var rightContainsSource = ReturnExpressionContainsSource(
                binary.Right,
                sourceInvocationSpan,
                trackedLocal,
                semanticModel);

            if (leftContainsSource || rightContainsSource)
            {
                return (!leftContainsSource ||
                        ReturnExpressionTransfersScopeForSource(
                            binary.Left,
                            scopeLocal,
                            semanticModel,
                            sourceInvocationSpan,
                            trackedLocal)) &&
                       (!rightContainsSource ||
                        ReturnExpressionTransfersScopeForSource(
                            binary.Right,
                            scopeLocal,
                            semanticModel,
                            sourceInvocationSpan,
                            trackedLocal));
            }
        }

        return ExpressionTransfersScope(returnExpression, scopeLocal, semanticModel, sourceInvocationSpan);
    }

    private static bool ExpressionTransfersScope(
        ExpressionSyntax expression,
        ILocalSymbol scopeLocal,
        SemanticModel semanticModel,
        TextSpan sourceInvocationSpan)
    {
        expression = UnwrapParentheses(expression);
        switch (expression)
        {
            case TupleExpressionSyntax tuple:
                return tuple.Arguments.Any(argument =>
                    ExpressionTransfersScope(argument.Expression, scopeLocal, semanticModel, sourceInvocationSpan));
            case AnonymousObjectCreationExpressionSyntax anonymous:
                return anonymous.Initializers.Any(initializer =>
                    ExpressionTransfersScope(initializer.Expression, scopeLocal, semanticModel, sourceInvocationSpan));
            case ConditionalExpressionSyntax conditional:
                return ExpressionTransfersScope(conditional.WhenTrue, scopeLocal, semanticModel, sourceInvocationSpan) &&
                       ExpressionTransfersScope(conditional.WhenFalse, scopeLocal, semanticModel, sourceInvocationSpan);
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression):
                return ExpressionTransfersScope(binary.Left, scopeLocal, semanticModel, sourceInvocationSpan) &&
                       ExpressionTransfersScope(binary.Right, scopeLocal, semanticModel, sourceInvocationSpan);
        }

        foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (sourceInvocationSpan.Contains(identifier.SpanStart) ||
                !ScopeIdentifierTransfersValue(identifier) ||
                !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier).Symbol,
                    scopeLocal))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool ReturnExpressionContainsSource(
        ExpressionSyntax expression,
        TextSpan sourceInvocationSpan,
        ILocalSymbol? trackedLocal,
        SemanticModel semanticModel)
    {
        if (sourceInvocationSpan.Length > 0 &&
            expression.Span.Contains(sourceInvocationSpan.Start) &&
            expression.Span.Contains(sourceInvocationSpan.End - 1))
        {
            return true;
        }

        return trackedLocal is not null &&
               expression
                   .DescendantNodesAndSelf()
                   .OfType<IdentifierNameSyntax>()
                   .Any(identifier =>
                       SymbolEqualityComparer.Default.Equals(
                           semanticModel.GetSymbolInfo(identifier).Symbol,
                           trackedLocal));
    }

    private static bool ReturnExpressionContainsComposite(ExpressionSyntax? expression)
    {
        expression = expression is null ? null : UnwrapParentheses(expression);
        return expression switch
        {
            TupleExpressionSyntax or AnonymousObjectCreationExpressionSyntax => true,
            ConditionalExpressionSyntax conditional =>
                ReturnExpressionContainsComposite(conditional.WhenTrue) ||
                ReturnExpressionContainsComposite(conditional.WhenFalse),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression) =>
                ReturnExpressionContainsComposite(binary.Left) ||
                ReturnExpressionContainsComposite(binary.Right),
            _ => false
        };
    }

    private static bool ScopeLocalIsDisposedBeforeEscape(
        ILocalSymbol scopeLocal,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        ReturnStatementSyntax returnStatement,
        int returnSpanStart) =>
        ScopeLocalIsDisposedBefore(scopeLocal, executableBody, semanticModel, returnSpanStart) ||
        ScopeLocalIsDisposedByEnclosingFinally(scopeLocal, semanticModel, returnStatement);

    private static bool ScopeLocalIsUsingOwned(ILocalSymbol scopeLocal)
    {
        foreach (var syntaxReference in scopeLocal.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not VariableDeclaratorSyntax variable)
            {
                continue;
            }

            if (variable.Parent?.Parent is LocalDeclarationStatementSyntax localDeclaration &&
                (localDeclaration.UsingKeyword != default || localDeclaration.AwaitKeyword != default))
            {
                return true;
            }

            if (variable.Parent?.Parent is UsingStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ScopeLocalIsDisposedBefore(
        ILocalSymbol scopeLocal,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        int beforeSpanStart)
    {
        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node is InvocationExpressionSyntax invocation &&
                invocation.SpanStart < beforeSpanStart &&
                IsDisposeInvocationForLocal(invocation, scopeLocal, semanticModel) &&
                DisposalCanReachReturn(invocation, beforeSpanStart))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ScopeLocalIsDisposedByEnclosingFinally(
        ILocalSymbol scopeLocal,
        SemanticModel semanticModel,
        ReturnStatementSyntax returnStatement)
    {
        foreach (var tryStatement in returnStatement.Ancestors().OfType<TryStatementSyntax>())
        {
            if (tryStatement.Finally is not { } finallyClause ||
                !TryStatementBodyContainsReturn(tryStatement, returnStatement))
            {
                continue;
            }

            if (FinallyDefinitelyDisposesScope(finallyClause, scopeLocal, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryStatementBodyContainsReturn(
        TryStatementSyntax tryStatement,
        ReturnStatementSyntax returnStatement) =>
        tryStatement.Block.Span.Contains(returnStatement.SpanStart) ||
        tryStatement.Catches.Any(catchClause => catchClause.Block.Span.Contains(returnStatement.SpanStart));

    private static bool FinallyDefinitelyDisposesScope(
        FinallyClauseSyntax finallyClause,
        ILocalSymbol scopeLocal,
        SemanticModel semanticModel)
    {
        foreach (var statement in finallyClause.Block.Statements)
        {
            if (statement is ExpressionStatementSyntax expressionStatement &&
                IsDirectDisposeExpression(expressionStatement.Expression, scopeLocal, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDirectDisposeExpression(
        ExpressionSyntax expression,
        ILocalSymbol scopeLocal,
        SemanticModel semanticModel)
    {
        expression = UnwrapParentheses(expression);
        if (expression is AwaitExpressionSyntax awaitExpression)
        {
            expression = UnwrapParentheses(awaitExpression.Expression);
        }

        return expression is InvocationExpressionSyntax invocation &&
               IsDisposeInvocationForLocal(invocation, scopeLocal, semanticModel);
    }

    private static bool DisposalCanReachReturn(InvocationExpressionSyntax disposeInvocation, int returnSpanStart)
    {
        foreach (var ifStatement in disposeInvocation.Ancestors().OfType<IfStatementSyntax>())
        {
            var branch = ifStatement.Statement.Span.Contains(disposeInvocation.SpanStart)
                ? ifStatement.Statement
                : ifStatement.Else?.Statement.Span.Contains(disposeInvocation.SpanStart) == true
                    ? ifStatement.Else.Statement
                    : null;

            if (branch is not null &&
                returnSpanStart > ifStatement.Span.End &&
                BranchExitsAfterDispose(branch, disposeInvocation.SpanStart))
            {
                return false;
            }
        }

        return true;
    }

    private static bool BranchExitsAfterDispose(StatementSyntax branch, int disposeSpanStart)
    {
        if (branch is BlockSyntax block)
        {
            return block.Statements.Any(statement =>
                statement.SpanStart > disposeSpanStart &&
                statement is ReturnStatementSyntax or ThrowStatementSyntax);
        }

        return branch.SpanStart > disposeSpanStart &&
               branch is ReturnStatementSyntax or ThrowStatementSyntax;
    }

    private static bool ScopeIdentifierTransfersValue(IdentifierNameSyntax identifier)
    {
        SyntaxNode current = identifier;
        while (true)
        {
            switch (current.Parent)
            {
                case ParenthesizedExpressionSyntax parenthesized
                    when parenthesized.Expression == current:
                    current = parenthesized;
                    continue;
                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) &&
                         postfix.Operand == current:
                    current = postfix;
                    continue;
                case CastExpressionSyntax cast
                    when cast.Expression == current:
                    current = cast;
                    continue;
                case BinaryExpressionSyntax binary
                    when binary.IsKind(SyntaxKind.CoalesceExpression) &&
                         (binary.Left == current || binary.Right == current):
                    current = binary;
                    continue;
                case ConditionalExpressionSyntax conditional
                    when conditional.WhenTrue == current || conditional.WhenFalse == current:
                    current = conditional;
                    continue;
                default:
                    break;
            }

            break;
        }

        return current.Parent switch
        {
            ArgumentSyntax argument when argument.Expression == current &&
                                         argument.Parent is TupleExpressionSyntax => true,
            AnonymousObjectMemberDeclaratorSyntax member when member.Expression == current => true,
            ReturnStatementSyntax returnStatement when returnStatement.Expression == current => true,
            _ => false
        };
    }

    private static bool SourceCanReachSink(
        InvocationExpressionSyntax sourceInvocation,
        SyntaxNode sink,
        ILocalSymbol carriedLocal,
        SemanticModel semanticModel) =>
        LocalCanReachSink(sourceInvocation, sink, carriedLocal, semanticModel);

    private static bool LocalCanReachSink(
        SyntaxNode source,
        SyntaxNode sink,
        ILocalSymbol carriedLocal,
        SemanticModel semanticModel) =>
        source.SpanStart < sink.SpanStart ||
        LocalCanReachEarlierSinkOnLoopBackEdge(source, sink, carriedLocal, semanticModel);

    private static bool LocalCanReachEarlierSinkOnLoopBackEdge(
        SyntaxNode source,
        SyntaxNode sink,
        ILocalSymbol carriedLocal,
        SemanticModel semanticModel)
    {
        if (source.SpanStart <= sink.SpanStart)
        {
            return false;
        }

        var loop = FindCommonLoop(source, sink);
        if (loop is null)
        {
            return false;
        }

        return !LocalDeclaredInsideLoopBeforeSink(carriedLocal, loop, sink, semanticModel) &&
               !LocalAssignedBeforeSinkInLoop(carriedLocal, loop, sink, semanticModel) &&
               !LoopDefinitelyExitsAfterSource(loop, source, semanticModel);
    }

    private static SyntaxNode? FindCommonLoop(SyntaxNode source, SyntaxNode sink)
    {
        var sourceLoops = new HashSet<SyntaxNode>(source.Ancestors().Where(IsLoopStatement));

        return sink
            .AncestorsAndSelf()
            .FirstOrDefault(ancestor => sourceLoops.Contains(ancestor));
    }

    private static bool LocalDeclaredInsideLoopBeforeSink(
        ILocalSymbol local,
        SyntaxNode loop,
        SyntaxNode sink,
        SemanticModel semanticModel)
    {
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is VariableDeclaratorSyntax declarator &&
                loop.Span.Contains(declarator.SpanStart) &&
                declarator.SpanStart < sink.SpanStart &&
                !LocalDeclarationCopiesLoopCarriedValue(declarator, loop, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LocalDeclarationCopiesLoopCarriedValue(
        VariableDeclaratorSyntax declarator,
        SyntaxNode loop,
        SemanticModel semanticModel)
    {
        if (declarator.Initializer?.Value is not { } initializer)
        {
            return false;
        }

        initializer = UnwrapParentheses(initializer);
        if (initializer is not IdentifierNameSyntax identifier ||
            semanticModel.GetSymbolInfo(identifier).Symbol is not ILocalSymbol sourceLocal)
        {
            return false;
        }

        foreach (var reference in sourceLocal.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is VariableDeclaratorSyntax sourceDeclarator &&
                loop.Span.Contains(sourceDeclarator.SpanStart) &&
                sourceDeclarator.SpanStart < declarator.SpanStart)
            {
                return false;
            }
        }

        return true;
    }

    private static bool LocalAssignedBeforeSinkInLoop(
        ILocalSymbol local,
        SyntaxNode loop,
        SyntaxNode sink,
        SemanticModel semanticModel) =>
        loop.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                assignment.SpanStart < sink.SpanStart &&
                assignment.Left is IdentifierNameSyntax identifier &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier).Symbol,
                    local));

    private static bool LoopConditionDefinitelyStopsAfterSource(
        SyntaxNode loop,
        SyntaxNode source,
        SemanticModel semanticModel)
    {
        if (!TryGetLoopConditionSymbol(loop, semanticModel, out var conditionSymbol))
        {
            return false;
        }

        return loop.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                assignment.SpanStart > source.SpanStart &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Right.IsKind(SyntaxKind.FalseLiteralExpression) &&
                AssignmentIsUnconditionalLoopBodyStatement(loop, assignment) &&
                assignment.Left is IdentifierNameSyntax identifier &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier).Symbol,
                    conditionSymbol));
    }

    private static bool LoopDefinitelyExitsAfterSource(
        SyntaxNode loop,
        SyntaxNode source,
        SemanticModel semanticModel) =>
        LoopConditionDefinitelyStopsAfterSource(loop, source, semanticModel) ||
        loop.DescendantNodes()
            .OfType<StatementSyntax>()
            .Any(statement =>
                statement.SpanStart > source.SpanStart &&
                statement is BreakStatementSyntax or ReturnStatementSyntax or ThrowStatementSyntax &&
                StatementIsUnconditionalLoopBodyStatement(loop, statement));

    private static bool AssignmentIsUnconditionalLoopBodyStatement(
        SyntaxNode loop,
        AssignmentExpressionSyntax assignment) =>
        assignment.FirstAncestorOrSelf<StatementSyntax>() is { } statement &&
        StatementIsUnconditionalLoopBodyStatement(loop, statement);

    private static bool StatementIsUnconditionalLoopBodyStatement(
        SyntaxNode loop,
        StatementSyntax statement)
    {
        var body = loop switch
        {
            WhileStatementSyntax whileStatement => whileStatement.Statement,
            DoStatementSyntax doStatement => doStatement.Statement,
            ForStatementSyntax forStatement => forStatement.Statement,
            _ => null
        };

        return body switch
        {
            BlockSyntax block => statement.Parent == block,
            StatementSyntax singleStatement => statement == singleStatement,
            _ => false
        };
    }

    private static bool TryGetLoopConditionSymbol(
        SyntaxNode loop,
        SemanticModel semanticModel,
        out ISymbol conditionSymbol)
    {
        conditionSymbol = null!;

        var condition = loop switch
        {
            WhileStatementSyntax whileStatement => whileStatement.Condition,
            DoStatementSyntax doStatement => doStatement.Condition,
            ForStatementSyntax forStatement => forStatement.Condition,
            _ => null
        };

        condition = condition is null ? null : UnwrapParentheses(condition);
        if (condition is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol or IParameterSymbol)
        {
            conditionSymbol = semanticModel.GetSymbolInfo(identifier).Symbol!;
            return true;
        }

        return false;
    }

    private static bool IsLoopStatement(SyntaxNode node) =>
        node is ForStatementSyntax or
                ForEachStatementSyntax or
                ForEachVariableStatementSyntax or
                WhileStatementSyntax or
                DoStatementSyntax;

    /// <summary>
    /// Reports every tracked service local or capturing delegate referenced inside a returned
    /// tuple or anonymous object, recursing through nested composites.
    /// </summary>
    private static void ReportCompositeReturnEscapes(
        CompilationAnalysisContext context,
        ExpressionSyntax composite,
        ExpressionSyntax returnedComposite,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeVariables,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        Dictionary<ILocalSymbol, ILocalSymbol> serviceScopeVariables,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> capturedDelegateVariables,
        HashSet<TextSpan> reportedSpans,
        ReturnStatementSyntax returnStatement,
        SyntaxNode executableBody,
        int returnSpanStart)
    {
        IEnumerable<ExpressionSyntax> elements = composite switch
        {
            TupleExpressionSyntax tuple => tuple.Arguments.Select(argument => argument.Expression),
            AnonymousObjectCreationExpressionSyntax anonymous =>
                anonymous.Initializers.Select(initializer => initializer.Expression),
            ConditionalExpressionSyntax conditional => [conditional.WhenTrue, conditional.WhenFalse],
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression) => [binary.Left, binary.Right],
            _ => Enumerable.Empty<ExpressionSyntax>()
        };

        foreach (var element in elements)
        {
            if (ReturnExpressionContainsComposite(element))
            {
                ReportCompositeReturnEscapes(
                    context,
                    element,
                    element is ConditionalExpressionSyntax ||
                    element is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.CoalesceExpression)
                        ? element
                        : returnedComposite,
                    semanticModel,
                    scopeVariables,
                    providerAliases,
                    registrationCollector,
                    wellKnownTypes,
                    serviceVariables,
                    serviceScopeVariables,
                    capturedDelegateVariables,
                    reportedSpans,
                    returnStatement,
                    executableBody,
                    returnSpanStart);
                continue;
            }

            if (TryGetInlineCompositeResolution(
                    element,
                    semanticModel,
                    scopeVariables,
                    providerAliases,
                    registrationCollector,
                    wellKnownTypes,
                    out var inlineSource,
                    out var inlineScope) &&
                !ReturnTransfersScope(
                    returnedComposite,
                    inlineScope,
                    semanticModel,
                    inlineSource.Span,
                    trackedLocal: null,
                    returnStatement,
                    executableBody,
                    returnSpanStart))
            {
                ReportDiagnostic(context, inlineSource, "return", reportedSpans);
            }
            else if (TryGetTrackedLocalReference(
                    element,
                    semanticModel,
                    serviceVariables,
                    out var trackedSource,
                    out var trackedLocal))
            {
                if (SourceCanReachSink(trackedSource, composite, trackedLocal, semanticModel) &&
                    !ReturnTransfersTrackedScope(
                        returnedComposite,
                        trackedSource,
                        trackedLocal,
                        serviceScopeVariables,
                        semanticModel,
                        returnStatement,
                        executableBody,
                        returnSpanStart))
                {
                    ReportDiagnostic(context, trackedSource, "return", reportedSpans);
                }
            }
            else if (TryGetCapturedDelegateSource(
                         element, semanticModel, serviceVariables, capturedDelegateVariables, out var delegateSource))
            {
                ReportDiagnostic(context, delegateSource, "return", reportedSpans);
            }
        }
    }

    private static bool TryGetInlineCompositeResolution(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> scopeVariables,
        Dictionary<ILocalSymbol, ILocalSymbol> providerAliases,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        out InvocationExpressionSyntax sourceInvocation,
        out ILocalSymbol scopeSymbol)
    {
        sourceInvocation = null!;
        scopeSymbol = null!;

        foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (TryGetResolutionLifetime(
                    invocation,
                    semanticModel,
                    scopeVariables,
                    providerAliases,
                    registrationCollector,
                    wellKnownTypes,
                    out var lifetime,
                    out scopeSymbol) &&
                ShouldReportScopedEscape(lifetime))
            {
                sourceInvocation = invocation;
                return true;
            }
        }

        return false;
    }

    private static bool IsEscapingParameter(IParameterSymbol parameter) =>
        parameter.RefKind is RefKind.Ref or RefKind.Out;

    private static bool AssignmentTargetOutlivesScope(
        ExpressionSyntax target,
        SemanticModel semanticModel,
        SyntaxNode executableBody,
        AssignmentExpressionSyntax assignment)
    {
        target = UnwrapParentheses(target);

        if (target is IdentifierNameSyntax identifier)
        {
            return semanticModel.GetSymbolInfo(identifier).Symbol is IFieldSymbol or IPropertySymbol;
        }

        if (target is MemberAccessExpressionSyntax memberAccess)
        {
            return ReceiverRootOutlivesScope(memberAccess.Expression, semanticModel, executableBody, assignment);
        }

        if (target is MemberBindingExpressionSyntax memberBinding &&
            TryGetConditionalAccessReceiver(memberBinding) is { } conditionalReceiver)
        {
            return ReceiverRootOutlivesScope(conditionalReceiver, semanticModel, executableBody, assignment);
        }

        if (target is ElementAccessExpressionSyntax elementAccess)
        {
            return ReceiverRootOutlivesScope(elementAccess.Expression, semanticModel, executableBody, assignment);
        }

        return false;
    }

    private static bool TryGetAssignmentTargetSymbol(
        ExpressionSyntax target,
        SemanticModel semanticModel,
        out ISymbol symbol)
    {
        symbol = null!;

        var symbolInfo = semanticModel.GetSymbolInfo(target);
        if (symbolInfo.Symbol is { } directSymbol)
        {
            symbol = directSymbol;
            return true;
        }

        if (target is MemberBindingExpressionSyntax memberBinding &&
            semanticModel.GetSymbolInfo(memberBinding.Name).Symbol is { } memberBindingSymbol)
        {
            symbol = memberBindingSymbol;
            return true;
        }

        return false;
    }

    private static bool ReceiverRootOutlivesScope(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        SyntaxNode executableBody,
        AssignmentExpressionSyntax assignment)
    {
        var root = GetReceiverRoot(receiverExpression);
        if (root is ThisExpressionSyntax or BaseExpressionSyntax)
        {
            return true;
        }

        var rootSymbol = semanticModel.GetSymbolInfo(root).Symbol;
        if (rootSymbol is ILocalSymbol localSymbol)
        {
            if (!ReceiverIsDirectRootReference(receiverExpression, root))
            {
                return true;
            }

            return LocalReceiverOutlivesScope(localSymbol, executableBody, semanticModel, assignment);
        }

        if (rootSymbol is IFieldSymbol or IPropertySymbol or IParameterSymbol or INamedTypeSymbol)
        {
            return true;
        }

        // Fully-qualified static receivers can have a namespace root (`MyApp`) while the full
        // receiver expression (`MyApp.Globals`) binds to the static type.
        return semanticModel.GetSymbolInfo(receiverExpression).Symbol is INamedTypeSymbol;
    }

    private static bool LocalReceiverOutlivesScope(
        ILocalSymbol localSymbol,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment)
    {
        var root = ResolveFreshLocalRoot(localSymbol, executableBody, semanticModel, assignment);
        if (!root.HasValue)
        {
            return true;
        }

        return LocalEscapesAroundAssignment(root.Value, executableBody, semanticModel, assignment);
    }

    private enum LocalValueKind
    {
        FreshObject,
        Alias,
        Null,
        Other
    }

    private readonly struct FreshLocalRoot
    {
        public FreshLocalRoot(ILocalSymbol local, int originSpanStart)
        {
            Local = local;
            OriginSpanStart = originSpanStart;
        }

        public ILocalSymbol Local { get; }

        public int OriginSpanStart { get; }
    }

    private static FreshLocalRoot? ResolveFreshLocalRoot(
        ILocalSymbol localSymbol,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment)
    {
        var visited = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        return ResolveFreshLocalRoot(
            localSymbol,
            executableBody,
            semanticModel,
            assignment.SpanStart,
            assignment,
            visited);
    }

    private static FreshLocalRoot? ResolveFreshLocalRoot(
        ILocalSymbol localSymbol,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        int beforeSpanStart,
        AssignmentExpressionSyntax sink,
        HashSet<ILocalSymbol> visited)
    {
        if (!visited.Add(localSymbol))
        {
            return null;
        }

        if (!TryGetLatestLocalValueBefore(
                localSymbol,
                executableBody,
                semanticModel,
                beforeSpanStart,
                out var valueKind,
                out var aliasSource,
                out var valueOrigin) ||
            valueOrigin is null ||
            !ValueDefinitelyReachesSink(valueOrigin, sink))
        {
            return null;
        }

        return valueKind switch
        {
            LocalValueKind.FreshObject => new FreshLocalRoot(localSymbol, valueOrigin.SpanStart),
            LocalValueKind.Alias when aliasSource is not null =>
                ResolveFreshLocalRoot(
                    aliasSource,
                    executableBody,
                    semanticModel,
                    valueOrigin.SpanStart,
                    sink,
                    visited),
            _ => null
        };
    }

    private static bool TryGetLatestLocalValueBefore(
        ILocalSymbol localSymbol,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        int beforeSpanStart,
        out LocalValueKind valueKind,
        out ILocalSymbol? aliasSource,
        out SyntaxNode? valueOrigin)
    {
        AssignmentExpressionSyntax? latestAssignment = null;
        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node.SpanStart >= beforeSpanStart ||
                node is not AssignmentExpressionSyntax assignment ||
                assignment.Left is not IdentifierNameSyntax leftIdentifier ||
                !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(leftIdentifier).Symbol,
                    localSymbol))
            {
                continue;
            }

            if (latestAssignment is null || assignment.SpanStart > latestAssignment.SpanStart)
            {
                latestAssignment = assignment;
            }
        }

        if (latestAssignment is not null)
        {
            valueOrigin = latestAssignment;
            return TryClassifyAssignmentValue(
                localSymbol,
                executableBody,
                latestAssignment,
                semanticModel,
                out valueKind,
                out aliasSource);
        }

        foreach (var declarationReference in localSymbol.DeclaringSyntaxReferences)
        {
            if (declarationReference.GetSyntax() is VariableDeclaratorSyntax declarator &&
                declarator.SpanStart < beforeSpanStart &&
                declarator.Initializer?.Value is { } initializer)
            {
                valueOrigin = declarator;
                return TryClassifyLocalValue(initializer, semanticModel, out valueKind, out aliasSource);
            }
        }

        valueKind = LocalValueKind.Other;
        aliasSource = null;
        valueOrigin = null;
        return false;
    }

    private static bool TryClassifyAssignmentValue(
        ILocalSymbol localSymbol,
        SyntaxNode executableBody,
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        out LocalValueKind valueKind,
        out ILocalSymbol? aliasSource)
    {
        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return TryClassifyLocalValue(assignment.Right, semanticModel, out valueKind, out aliasSource);
        }

        if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
            TryClassifyLocalValue(assignment.Right, semanticModel, out var rightValueKind, out _) &&
            rightValueKind == LocalValueKind.FreshObject &&
            TryGetLatestLocalValueBefore(
                localSymbol,
                executableBody,
                semanticModel,
                assignment.SpanStart,
                out var previousValueKind,
                out _,
                out var previousOrigin) &&
            previousOrigin is not null &&
            previousValueKind is LocalValueKind.FreshObject or LocalValueKind.Null)
        {
            valueKind = LocalValueKind.FreshObject;
            aliasSource = null;
            return true;
        }

        valueKind = LocalValueKind.Other;
        aliasSource = null;
        return true;
    }

    private static bool ValueDefinitelyReachesSink(SyntaxNode valueOrigin, AssignmentExpressionSyntax sink)
    {
        if (valueOrigin.SpanStart >= sink.SpanStart)
        {
            return false;
        }

        var valueStatement = valueOrigin.FirstAncestorOrSelf<StatementSyntax>();
        var sinkStatement = sink.FirstAncestorOrSelf<StatementSyntax>();
        if (valueStatement is null || sinkStatement is null)
        {
            return false;
        }

        if (valueStatement == sinkStatement)
        {
            return valueOrigin.SpanStart < sink.SpanStart;
        }

        return valueStatement.Parent switch
        {
            BlockSyntax block => StatementListDefinitelyPrecedesSink(
                block.Statements,
                valueStatement,
                sinkStatement),
            SwitchSectionSyntax switchSection => StatementListDefinitelyPrecedesSink(
                switchSection.Statements,
                valueStatement,
                sinkStatement),
            _ => false
        };
    }

    private static bool StatementListDefinitelyPrecedesSink(
        SyntaxList<StatementSyntax> statements,
        StatementSyntax valueStatement,
        StatementSyntax sinkStatement)
    {
        var valueIndex = -1;
        var sinkIndex = -1;

        for (var index = 0; index < statements.Count; index++)
        {
            var statement = statements[index];
            if (statement == valueStatement)
            {
                valueIndex = index;
            }

            if (statement == sinkStatement || statement.Span.Contains(sinkStatement.SpanStart))
            {
                sinkIndex = index;
            }
        }

        return valueIndex >= 0 && sinkIndex >= 0 && valueIndex < sinkIndex;
    }

    private static bool TryClassifyLocalValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out LocalValueKind valueKind,
        out ILocalSymbol? aliasSource)
    {
        expression = UnwrapParentheses(expression);
        if (expression is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax)
        {
            valueKind = LocalValueKind.FreshObject;
            aliasSource = null;
            return true;
        }

        if (expression.IsKind(SyntaxKind.NullLiteralExpression))
        {
            valueKind = LocalValueKind.Null;
            aliasSource = null;
            return true;
        }

        if (TryGetLocalAliasSource(expression, semanticModel, out aliasSource))
        {
            valueKind = LocalValueKind.Alias;
            return true;
        }

        valueKind = LocalValueKind.Other;
        aliasSource = null;
        return true;
    }

    private static bool LocalEscapesAroundAssignment(
        FreshLocalRoot root,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        AssignmentExpressionSyntax scopedAssignment)
    {
        var aliases = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)
        {
            root.Local
        };
        var containerAliases = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node == scopedAssignment || node.Span.End <= root.OriginSpanStart)
            {
                continue;
            }

            var beforeScopedAssignment = node.SpanStart < scopedAssignment.SpanStart;
            if (node is ReturnStatementSyntax returnStatement &&
                (!beforeScopedAssignment ||
                 LocalCanReachSink(scopedAssignment, returnStatement, root.Local, semanticModel)) &&
                (ExpressionRetainsEscapingHolderValue(
                        returnStatement.Expression,
                        aliases,
                        scopedAssignment.Left,
                        semanticModel,
                        includeAssignedScopedSlot: true) ||
                    ExpressionRetainsLocalValue(
                        returnStatement.Expression,
                        containerAliases,
                        semanticModel)))
            {
                return true;
            }

            if (node is AssignmentExpressionSyntax assignment &&
                (ExpressionRetainsEscapingHolderValue(
                        assignment.Right,
                        aliases,
                        scopedAssignment.Left,
                        semanticModel,
                        includeAssignedScopedSlot: !beforeScopedAssignment) ||
                    ExpressionRetainsLocalValue(
                        assignment.Right,
                        containerAliases,
                        semanticModel)) &&
                AssignmentSinkDefinitelyOutlivesScope(assignment.Left, semanticModel))
            {
                return true;
            }

            if (node is AssignmentExpressionSyntax localElementAssignment &&
                TryGetLocalElementAssignment(localElementAssignment, semanticModel, out var elementContainerLocal) &&
                ExpressionRetainsEscapingHolderValue(
                    localElementAssignment.Right,
                    aliases,
                    scopedAssignment.Left,
                    semanticModel,
                    includeAssignedScopedSlot: !beforeScopedAssignment))
            {
                var currentContainerAliases = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
                AddLocalAndCurrentAliases(
                    currentContainerAliases,
                    elementContainerLocal,
                    executableBody,
                    semanticModel,
                    localElementAssignment.SpanStart);

                if (LocalValueEscapedBefore(
                        currentContainerAliases,
                        executableBody,
                        semanticModel,
                        localElementAssignment.SpanStart))
                {
                    return true;
                }

                containerAliases.UnionWith(currentContainerAliases);
            }

            if (node is InvocationExpressionSyntax mutationCall &&
                TryGetEscapingCollectionMutation(mutationCall, semanticModel, out _) &&
                mutationCall.ArgumentList.Arguments.Any(argument =>
                    ExpressionRetainsEscapingHolderValue(
                        argument.Expression,
                        aliases,
                        scopedAssignment.Left,
                        semanticModel,
                        includeAssignedScopedSlot: !beforeScopedAssignment) ||
                    ExpressionRetainsLocalValue(
                        argument.Expression,
                        containerAliases,
                        semanticModel)))
            {
                return true;
            }

            if (node is InvocationExpressionSyntax localMutationCall &&
                TryGetLocalCollectionMutation(localMutationCall, semanticModel, out var containerLocal) &&
                localMutationCall.ArgumentList.Arguments.Any(argument =>
                    ExpressionRetainsEscapingHolderValue(
                        argument.Expression,
                        aliases,
                        scopedAssignment.Left,
                        semanticModel,
                        includeAssignedScopedSlot: !beforeScopedAssignment)))
            {
                var currentContainerAliases = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
                AddLocalAndCurrentAliases(
                    currentContainerAliases,
                    containerLocal,
                    executableBody,
                    semanticModel,
                    localMutationCall.SpanStart);

                if (LocalValueEscapedBefore(
                        currentContainerAliases,
                        executableBody,
                        semanticModel,
                        localMutationCall.SpanStart))
                {
                    return true;
                }

                containerAliases.UnionWith(currentContainerAliases);
            }

            TrackLocalAliases(node, aliases, semanticModel);
            TrackLocalAliases(node, containerAliases, semanticModel);
        }

        return false;
    }

    private static bool TryGetLocalElementAssignment(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        out ILocalSymbol containerLocal)
    {
        containerLocal = null!;

        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            assignment.Left is not ElementAccessExpressionSyntax elementAccess)
        {
            return false;
        }

        var root = GetReceiverRoot(elementAccess.Expression);
        if (semanticModel.GetSymbolInfo(root).Symbol is not ILocalSymbol localSymbol)
        {
            return false;
        }

        containerLocal = localSymbol;
        return true;
    }

    private static void TrackLocalAliases(
        SyntaxNode node,
        HashSet<ILocalSymbol> aliases,
        SemanticModel semanticModel)
    {
        if (node is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(variable) is ILocalSymbol declaredLocal &&
                    TryGetLocalAliasSource(variable.Initializer?.Value, semanticModel, out var aliasSource) &&
                    aliases.Contains(aliasSource))
                {
                    aliases.Add(declaredLocal);
                }
            }

            return;
        }

        if (node is AssignmentExpressionSyntax assignment &&
            assignment.Left is IdentifierNameSyntax leftIdentifier &&
            semanticModel.GetSymbolInfo(leftIdentifier).Symbol is ILocalSymbol leftLocal)
        {
            if (TryGetLocalAliasSource(assignment.Right, semanticModel, out var aliasSource) &&
                aliases.Contains(aliasSource))
            {
                aliases.Add(leftLocal);
            }
            else
            {
                aliases.Remove(leftLocal);
            }
        }
    }

    private static bool LocalValueEscapedBefore(
        HashSet<ILocalSymbol> localAliases,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        int beforeSpanStart)
    {
        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node.SpanStart >= beforeSpanStart)
            {
                continue;
            }

            if (node is ReturnStatementSyntax returnStatement &&
                ExpressionRetainsCurrentLocalValue(
                    returnStatement.Expression,
                    localAliases,
                    executableBody,
                    semanticModel,
                    beforeSpanStart,
                    returnStatement.SpanStart))
            {
                return true;
            }

            if (node is AssignmentExpressionSyntax assignment &&
                ExpressionRetainsCurrentLocalValue(
                    assignment.Right,
                    localAliases,
                    executableBody,
                    semanticModel,
                    beforeSpanStart,
                    assignment.SpanStart) &&
                AssignmentSinkDefinitelyOutlivesScope(assignment.Left, semanticModel))
            {
                return true;
            }

            if (node is InvocationExpressionSyntax mutationCall &&
                TryGetEscapingCollectionMutation(mutationCall, semanticModel, out _) &&
                mutationCall.ArgumentList.Arguments.Any(argument =>
                    ExpressionRetainsCurrentLocalValue(
                        argument.Expression,
                        localAliases,
                        executableBody,
                        semanticModel,
                        beforeSpanStart,
                        mutationCall.SpanStart)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExpressionRetainsCurrentLocalValue(
        ExpressionSyntax? expression,
        HashSet<ILocalSymbol> localAliases,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        int currentBeforeSpanStart,
        int sinkSpanStart)
    {
        if (expression is null ||
            !ExpressionRetainsLocalValue(expression, localAliases, semanticModel))
        {
            return false;
        }

        return expression.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier =>
                semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol localSymbol &&
                localAliases.Contains(localSymbol) &&
                LocalValueAtSinkStillCurrent(
                    localSymbol,
                    executableBody,
                    semanticModel,
                    sinkSpanStart,
                    currentBeforeSpanStart));
    }

    private static bool LocalValueAtSinkStillCurrent(
        ILocalSymbol localSymbol,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        int sinkSpanStart,
        int currentBeforeSpanStart)
    {
        if (!TryGetLatestLocalValueBefore(
                localSymbol,
                executableBody,
                semanticModel,
                sinkSpanStart,
                out _,
                out _,
                out var sinkOrigin) ||
            !TryGetLatestLocalValueBefore(
                localSymbol,
                executableBody,
                semanticModel,
                currentBeforeSpanStart,
                out _,
                out _,
                out var currentOrigin))
        {
            return false;
        }

        return sinkOrigin is not null &&
               currentOrigin is not null &&
               sinkOrigin == currentOrigin;
    }

    private static void AddLocalAndCurrentAliases(
        HashSet<ILocalSymbol> aliases,
        ILocalSymbol localSymbol,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        int beforeSpanStart)
    {
        var root = ResolveCurrentAliasRoot(
            localSymbol,
            executableBody,
            semanticModel,
            beforeSpanStart,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));

        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node.SpanStart >= beforeSpanStart ||
                node is not LocalDeclarationStatementSyntax localDeclaration)
            {
                continue;
            }

            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol declaredLocal)
                {
                    continue;
                }

                var declaredRoot = ResolveCurrentAliasRoot(
                    declaredLocal,
                    executableBody,
                    semanticModel,
                    beforeSpanStart,
                    new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));

                if (SymbolEqualityComparer.Default.Equals(declaredRoot, root))
                {
                    aliases.Add(declaredLocal);
                }
            }
        }

        aliases.Add(localSymbol);
    }

    private static ILocalSymbol ResolveCurrentAliasRoot(
        ILocalSymbol localSymbol,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        int beforeSpanStart,
        HashSet<ILocalSymbol> visited)
    {
        if (!visited.Add(localSymbol))
        {
            return localSymbol;
        }

        if (TryGetLatestLocalValueBefore(
                localSymbol,
                executableBody,
                semanticModel,
                beforeSpanStart,
                out var valueKind,
                out var aliasSource,
                out _) &&
            valueKind == LocalValueKind.Alias &&
            aliasSource is not null)
        {
            return ResolveCurrentAliasRoot(
                aliasSource,
                executableBody,
                semanticModel,
                beforeSpanStart,
                visited);
        }

        return localSymbol;
    }

    private static bool TryGetLocalCollectionMutation(
        InvocationExpressionSyntax call,
        SemanticModel semanticModel,
        out ILocalSymbol containerLocal)
    {
        containerLocal = null!;

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

        if (semanticModel.GetSymbolInfo(call).Symbol is not IMethodSymbol method ||
            !(method.ReturnsVoid ||
              method.ReturnType.SpecialType is SpecialType.System_Boolean or SpecialType.System_Int32))
        {
            return false;
        }

        if (semanticModel.GetTypeInfo(receiverExpression).Type is not { } receiverType ||
            !IsEnumerableLike(receiverType))
        {
            return false;
        }

        var root = GetReceiverRoot(receiverExpression);
        if (semanticModel.GetSymbolInfo(root).Symbol is not ILocalSymbol localSymbol)
        {
            return false;
        }

        containerLocal = localSymbol;
        return true;
    }

    private static bool AssignmentSinkDefinitelyOutlivesScope(ExpressionSyntax target, SemanticModel semanticModel)
    {
        target = UnwrapParentheses(target);

        if (target is IdentifierNameSyntax identifier)
        {
            return semanticModel.GetSymbolInfo(identifier).Symbol switch
            {
                IFieldSymbol or IPropertySymbol => true,
                IParameterSymbol parameter => IsEscapingParameter(parameter),
                _ => false
            };
        }

        if (target is MemberAccessExpressionSyntax memberAccess)
        {
            return ReceiverRootDefinitelyOutlivesScope(memberAccess.Expression, semanticModel);
        }

        if (target is ElementAccessExpressionSyntax elementAccess)
        {
            return ReceiverRootDefinitelyOutlivesScope(elementAccess.Expression, semanticModel);
        }

        return false;
    }

    private static bool ReceiverRootDefinitelyOutlivesScope(ExpressionSyntax receiverExpression, SemanticModel semanticModel)
    {
        var root = GetReceiverRoot(receiverExpression);
        if (root is ThisExpressionSyntax or BaseExpressionSyntax)
        {
            return true;
        }

        if (semanticModel.GetSymbolInfo(root).Symbol
            is IFieldSymbol or IPropertySymbol or IParameterSymbol or INamedTypeSymbol)
        {
            return true;
        }

        return semanticModel.GetSymbolInfo(receiverExpression).Symbol is INamedTypeSymbol;
    }

    private static bool ExpressionReferencesLocal(
        ExpressionSyntax? expression,
        ILocalSymbol localSymbol,
        SemanticModel semanticModel)
    {
        var locals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)
        {
            localSymbol
        };

        return ExpressionReferencesAnyLocal(expression, locals, semanticModel);
    }

    private static bool ExpressionReferencesAnyLocal(
        ExpressionSyntax? expression,
        HashSet<ILocalSymbol> locals,
        SemanticModel semanticModel)
    {
        if (expression is null)
        {
            return false;
        }

        return expression
            .DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier =>
                semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol referencedLocal &&
                locals.Contains(referencedLocal));
    }

    private static bool SyntaxReferencesAnyLocal(
        SyntaxNode node,
        HashSet<ILocalSymbol> locals,
        SemanticModel semanticModel) =>
        node.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier =>
                semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol referencedLocal &&
                locals.Contains(referencedLocal));

    private static bool ExpressionRetainsEscapingHolderValue(
        ExpressionSyntax? expression,
        HashSet<ILocalSymbol> holderAliases,
        ExpressionSyntax scopedAssignmentTarget,
        SemanticModel semanticModel,
        bool includeAssignedScopedSlot)
    {
        if (expression is null)
        {
            return false;
        }

        expression = UnwrapTransparentValueExpression(expression);

        if (expression is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol referencedLocal &&
            holderAliases.Contains(referencedLocal))
        {
            return true;
        }

        if (includeAssignedScopedSlot &&
            (IsAssignedScopedSlotReference(
                expression,
                holderAliases,
                scopedAssignmentTarget,
                semanticModel) ||
            IsAssignedScopedSlotConditionalAccess(
                expression,
                holderAliases,
                scopedAssignmentTarget,
                semanticModel)))
        {
            return true;
        }

        return expression switch
        {
            ConditionalExpressionSyntax conditional =>
                ExpressionRetainsEscapingHolderValue(
                    conditional.WhenTrue,
                    holderAliases,
                    scopedAssignmentTarget,
                    semanticModel,
                    includeAssignedScopedSlot) ||
                ExpressionRetainsEscapingHolderValue(
                    conditional.WhenFalse,
                    holderAliases,
                    scopedAssignmentTarget,
                    semanticModel,
                    includeAssignedScopedSlot),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression) =>
                ExpressionRetainsEscapingHolderValue(
                    binary.Left,
                    holderAliases,
                    scopedAssignmentTarget,
                    semanticModel,
                    includeAssignedScopedSlot) ||
                ExpressionRetainsEscapingHolderValue(
                    binary.Right,
                    holderAliases,
                    scopedAssignmentTarget,
                    semanticModel,
                    includeAssignedScopedSlot),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AsExpression) =>
                ExpressionRetainsEscapingHolderValue(
                    binary.Left,
                    holderAliases,
                    scopedAssignmentTarget,
                    semanticModel,
                    includeAssignedScopedSlot),
            AssignmentExpressionSyntax assignment =>
                ExpressionRetainsEscapingHolderValue(
                    assignment.Right,
                    holderAliases,
                    scopedAssignmentTarget,
                    semanticModel,
                    includeAssignedScopedSlot),
            AnonymousFunctionExpressionSyntax anonymousFunction =>
                SyntaxReferencesAnyLocal(anonymousFunction, holderAliases, semanticModel),
            TupleExpressionSyntax tuple =>
                tuple.Arguments.Any(argument =>
                    ExpressionRetainsEscapingHolderValue(
                        argument.Expression,
                        holderAliases,
                        scopedAssignmentTarget,
                        semanticModel,
                        includeAssignedScopedSlot)),
            AnonymousObjectCreationExpressionSyntax anonymousObject =>
                anonymousObject.Initializers.Any(initializer =>
                    ExpressionRetainsEscapingHolderValue(
                        initializer.Expression,
                        holderAliases,
                        scopedAssignmentTarget,
                        semanticModel,
                        includeAssignedScopedSlot)),
            ObjectCreationExpressionSyntax objectCreation =>
                ArgumentListRetainsEscapingHolderValue(
                    objectCreation.ArgumentList,
                    holderAliases,
                    scopedAssignmentTarget,
                    semanticModel,
                    includeAssignedScopedSlot) ||
                InitializerRetainsEscapingHolderValue(
                    objectCreation.Initializer,
                    holderAliases,
                    scopedAssignmentTarget,
                    semanticModel,
                    includeAssignedScopedSlot),
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation =>
                ArgumentListRetainsEscapingHolderValue(
                    implicitObjectCreation.ArgumentList,
                    holderAliases,
                    scopedAssignmentTarget,
                    semanticModel,
                    includeAssignedScopedSlot) ||
                InitializerRetainsEscapingHolderValue(
                    implicitObjectCreation.Initializer,
                    holderAliases,
                    scopedAssignmentTarget,
                    semanticModel,
                    includeAssignedScopedSlot),
            ArrayCreationExpressionSyntax arrayCreation =>
                InitializerRetainsEscapingHolderValue(
                    arrayCreation.Initializer,
                    holderAliases,
                    scopedAssignmentTarget,
                    semanticModel,
                    includeAssignedScopedSlot),
            ImplicitArrayCreationExpressionSyntax implicitArrayCreation =>
                InitializerRetainsEscapingHolderValue(
                    implicitArrayCreation.Initializer,
                    holderAliases,
                    scopedAssignmentTarget,
                    semanticModel,
                    includeAssignedScopedSlot),
            _ => false
        };
    }

    private static bool ExpressionRetainsLocalValue(
        ExpressionSyntax? expression,
        HashSet<ILocalSymbol> localAliases,
        SemanticModel semanticModel)
    {
        if (expression is null)
        {
            return false;
        }

        expression = UnwrapTransparentValueExpression(expression);

        if (expression is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol referencedLocal &&
            localAliases.Contains(referencedLocal))
        {
            return true;
        }

        return expression switch
        {
            ConditionalExpressionSyntax conditional =>
                ExpressionRetainsLocalValue(conditional.WhenTrue, localAliases, semanticModel) ||
                ExpressionRetainsLocalValue(conditional.WhenFalse, localAliases, semanticModel),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression) =>
                ExpressionRetainsLocalValue(binary.Left, localAliases, semanticModel) ||
                ExpressionRetainsLocalValue(binary.Right, localAliases, semanticModel),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AsExpression) =>
                ExpressionRetainsLocalValue(binary.Left, localAliases, semanticModel),
            AssignmentExpressionSyntax assignment =>
                ExpressionRetainsLocalValue(assignment.Right, localAliases, semanticModel),
            TupleExpressionSyntax tuple =>
                tuple.Arguments.Any(argument =>
                    ExpressionRetainsLocalValue(argument.Expression, localAliases, semanticModel)),
            AnonymousObjectCreationExpressionSyntax anonymousObject =>
                anonymousObject.Initializers.Any(initializer =>
                    ExpressionRetainsLocalValue(initializer.Expression, localAliases, semanticModel)),
            ObjectCreationExpressionSyntax objectCreation =>
                ArgumentListRetainsLocalValue(objectCreation.ArgumentList, localAliases, semanticModel) ||
                InitializerRetainsLocalValue(objectCreation.Initializer, localAliases, semanticModel),
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation =>
                ArgumentListRetainsLocalValue(
                    implicitObjectCreation.ArgumentList,
                    localAliases,
                    semanticModel) ||
                InitializerRetainsLocalValue(
                    implicitObjectCreation.Initializer,
                    localAliases,
                    semanticModel),
            ArrayCreationExpressionSyntax arrayCreation =>
                InitializerRetainsLocalValue(arrayCreation.Initializer, localAliases, semanticModel),
            ImplicitArrayCreationExpressionSyntax implicitArrayCreation =>
                InitializerRetainsLocalValue(implicitArrayCreation.Initializer, localAliases, semanticModel),
            _ => false
        };
    }

    private static bool IsAssignedScopedSlotReference(
        ExpressionSyntax expression,
        HashSet<ILocalSymbol> holderAliases,
        ExpressionSyntax scopedAssignmentTarget,
        SemanticModel semanticModel)
    {
        expression = UnwrapTransparentValueExpression(expression);
        scopedAssignmentTarget = UnwrapTransparentValueExpression(scopedAssignmentTarget);

        if (expression is not MemberAccessExpressionSyntax and not ElementAccessExpressionSyntax)
        {
            return false;
        }

        var root = GetReceiverRoot(expression);
        return semanticModel.GetSymbolInfo(root).Symbol is ILocalSymbol rootLocal &&
               holderAliases.Contains(rootLocal) &&
               SymbolEqualityComparer.Default.Equals(
                   semanticModel.GetSymbolInfo(expression).Symbol,
                   semanticModel.GetSymbolInfo(scopedAssignmentTarget).Symbol);
    }

    private static bool IsAssignedScopedSlotConditionalAccess(
        ExpressionSyntax expression,
        HashSet<ILocalSymbol> holderAliases,
        ExpressionSyntax scopedAssignmentTarget,
        SemanticModel semanticModel)
    {
        if (expression is not ConditionalAccessExpressionSyntax conditionalAccess)
        {
            return false;
        }

        var root = GetReceiverRoot(conditionalAccess.Expression);
        return semanticModel.GetSymbolInfo(root).Symbol is ILocalSymbol rootLocal &&
               holderAliases.Contains(rootLocal) &&
               SymbolEqualityComparer.Default.Equals(
                   semanticModel.GetSymbolInfo(conditionalAccess.WhenNotNull).Symbol,
                   semanticModel.GetSymbolInfo(scopedAssignmentTarget).Symbol);
    }

    private static bool ArgumentListRetainsEscapingHolderValue(
        ArgumentListSyntax? argumentList,
        HashSet<ILocalSymbol> holderAliases,
        ExpressionSyntax scopedAssignmentTarget,
        SemanticModel semanticModel,
        bool includeAssignedScopedSlot) =>
        argumentList?.Arguments.Any(argument =>
            ExpressionRetainsEscapingHolderValue(
                argument.Expression,
                holderAliases,
                scopedAssignmentTarget,
                semanticModel,
                includeAssignedScopedSlot)) == true;

    private static bool InitializerRetainsEscapingHolderValue(
        InitializerExpressionSyntax? initializer,
        HashSet<ILocalSymbol> holderAliases,
        ExpressionSyntax scopedAssignmentTarget,
        SemanticModel semanticModel,
        bool includeAssignedScopedSlot) =>
        initializer?.Expressions.Any(expression =>
            ExpressionRetainsEscapingHolderValue(
                expression,
                holderAliases,
                scopedAssignmentTarget,
                semanticModel,
                includeAssignedScopedSlot)) == true;

    private static bool ArgumentListRetainsLocalValue(
        ArgumentListSyntax? argumentList,
        HashSet<ILocalSymbol> localAliases,
        SemanticModel semanticModel) =>
        argumentList?.Arguments.Any(argument =>
            ExpressionRetainsLocalValue(argument.Expression, localAliases, semanticModel)) == true;

    private static bool InitializerRetainsLocalValue(
        InitializerExpressionSyntax? initializer,
        HashSet<ILocalSymbol> localAliases,
        SemanticModel semanticModel) =>
        initializer?.Expressions.Any(expression =>
            ExpressionRetainsLocalValue(expression, localAliases, semanticModel)) == true;

    private static bool TryGetLocalAliasSource(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        out ILocalSymbol aliasSource)
    {
        aliasSource = null!;

        if (expression is null)
        {
            return false;
        }

        expression = UnwrapParentheses(expression);
        if (expression is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol localSymbol)
        {
            aliasSource = localSymbol;
            return true;
        }

        return false;
    }

    private static bool ExpressionContainsInvocation(
        ExpressionSyntax expression,
        InvocationExpressionSyntax invocation) =>
        expression == invocation ||
        expression.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(candidate => candidate == invocation);

    private static bool ReceiverIsDirectRootReference(ExpressionSyntax receiverExpression, ExpressionSyntax root) =>
        UnwrapTransparentValueExpression(receiverExpression) == root;

    private static ExpressionSyntax GetReceiverRoot(ExpressionSyntax expression)
    {
        while (true)
        {
            expression = UnwrapTransparentValueExpression(expression);

            switch (expression)
            {
                case MemberAccessExpressionSyntax memberAccess:
                    expression = memberAccess.Expression;
                    continue;
                case ElementAccessExpressionSyntax elementAccess:
                    expression = elementAccess.Expression;
                    continue;
                case ConditionalAccessExpressionSyntax conditionalAccess:
                    expression = conditionalAccess.Expression;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static ExpressionSyntax UnwrapTransparentValueExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            expression = UnwrapParentheses(expression);

            switch (expression)
            {
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AsExpression):
                    expression = binary.Left;
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

    /// <summary>
    /// Returns the expression whose parent decides how the resolved service is consumed: the
    /// invocation itself, or the outermost enclosing wrapper whose result escapes.
    /// </summary>
    private static SyntaxNode GetConsumptionExpression(InvocationExpressionSyntax invocation)
    {
        SyntaxNode current = invocation;
        while (true)
        {
            switch (current.Parent)
            {
                case ParenthesizedExpressionSyntax parenthesized when parenthesized.Expression == current:
                    current = parenthesized;
                    continue;
                case CastExpressionSyntax cast when cast.Expression == current:
                    current = cast;
                    continue;
                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) && postfix.Operand == current:
                    current = postfix;
                    continue;
                case ConditionalAccessExpressionSyntax conditionalAccess when conditionalAccess.WhenNotNull == current:
                    current = conditionalAccess;
                    continue;
                case ConditionalExpressionSyntax conditional
                    when conditional.WhenTrue == current || conditional.WhenFalse == current:
                    current = conditional;
                    continue;
                case BinaryExpressionSyntax binary
                    when binary.IsKind(SyntaxKind.CoalesceExpression) &&
                         (binary.Left == current || binary.Right == current):
                    current = binary;
                    continue;
                case BinaryExpressionSyntax binary
                    when binary.IsKind(SyntaxKind.AsExpression) && binary.Left == current:
                    current = binary;
                    continue;
                default:
                    return current;
            }
        }
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
            else if (node is LocalDeclarationStatementSyntax manualLocalDecl)
            {
                foreach (var variable in manualLocalDecl.Declaration.Variables)
                {
                    if (UnwrapToInvocation(variable.Initializer?.Value) is { } invocation &&
                        IsCreateScopeInvocation(invocation, semanticModel, wellKnownTypes) &&
                        semanticModel.GetDeclaredSymbol(variable) is ILocalSymbol localSymbol &&
                        ScopeLocalIsManuallyDisposed(localSymbol, executableBody, semanticModel, variable.SpanStart))
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

    private static bool ScopeLocalIsManuallyDisposed(
        ILocalSymbol scopeLocal,
        SyntaxNode executableBody,
        SemanticModel semanticModel,
        int declarationSpanStart)
    {
        foreach (var node in ExecutableSyntaxHelper.EnumerateSameBoundaryNodes(executableBody))
        {
            if (node is InvocationExpressionSyntax invocation &&
                invocation.SpanStart > declarationSpanStart &&
                IsDisposeInvocationForLocal(invocation, scopeLocal, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDisposeInvocationForLocal(
        InvocationExpressionSyntax invocation,
        ILocalSymbol scopeLocal,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol ||
            methodSymbol.Name is not ("Dispose" or "DisposeAsync") ||
            methodSymbol.Parameters.Length != 0)
        {
            return false;
        }

        ExpressionSyntax? receiver = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            MemberBindingExpressionSyntax memberBinding => TryGetConditionalAccessReceiver(memberBinding),
            _ => null,
        };

        return receiver is IdentifierNameSyntax identifier &&
               SymbolEqualityComparer.Default.Equals(
                   semanticModel.GetSymbolInfo(identifier).Symbol,
                   scopeLocal);
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
        out ServiceLifetime? lifetime,
        out ILocalSymbol scopeSymbol)
    {
        lifetime = null;
        scopeSymbol = null!;

        if (!TryGetResolvedServiceInfo(
                invocation,
                semanticModel,
                scopeVariables,
                providerAliases,
                wellKnownTypes,
                out var serviceType,
                out var key,
                out var isKeyed,
                out scopeSymbol))
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
        out bool isKeyed,
        out ILocalSymbol scopeSymbol)
    {
        serviceType = null;
        key = null;
        isKeyed = false;
        scopeSymbol = null!;

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
                out scopeSymbol))
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
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        Dictionary<ILocalSymbol, ILocalSymbol> serviceScopeVariables)
    {
        if (node is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (variable.Initializer?.Value is not IdentifierNameSyntax identifier ||
                    semanticModel.GetSymbolInfo(identifier).Symbol is not ILocalSymbol sourceLocal ||
                    !serviceVariables.TryGetValue(sourceLocal, out var sourceInvocation) ||
                    !SourceCanReachSink(sourceInvocation, variable, sourceLocal, semanticModel) ||
                    semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol aliasLocal)
                {
                    continue;
                }

                serviceVariables[aliasLocal] = sourceInvocation;
                if (serviceScopeVariables.TryGetValue(sourceLocal, out var sourceScope))
                {
                    serviceScopeVariables[aliasLocal] = sourceScope;
                }
            }
        }

        if (node is AssignmentExpressionSyntax assignment &&
            assignment.Left is IdentifierNameSyntax leftIdentifier &&
            semanticModel.GetSymbolInfo(leftIdentifier).Symbol is ILocalSymbol leftLocal)
        {
            if (serviceVariables.TryGetValue(leftLocal, out var existingInvocation) &&
                ExpressionContainsInvocation(assignment.Right, existingInvocation))
            {
                return;
            }

            if (assignment.Right is not IdentifierNameSyntax rightIdentifier ||
                semanticModel.GetSymbolInfo(rightIdentifier).Symbol is not ILocalSymbol rightLocal ||
                !serviceVariables.TryGetValue(rightLocal, out var invocation) ||
                !SourceCanReachSink(invocation, assignment, rightLocal, semanticModel))
            {
                serviceVariables.Remove(leftLocal);
                serviceScopeVariables.Remove(leftLocal);
                return;
            }

            serviceVariables[leftLocal] = invocation;
            if (serviceScopeVariables.TryGetValue(rightLocal, out var rightScope))
            {
                serviceScopeVariables[leftLocal] = rightScope;
            }
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
        out InvocationExpressionSyntax sourceInvocation) =>
        TryGetTrackedLocalReference(
            expression,
            semanticModel,
            serviceVariables,
            out sourceInvocation,
            out _);

    private static bool TryGetTrackedLocalReference(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        Dictionary<ILocalSymbol, InvocationExpressionSyntax> serviceVariables,
        out InvocationExpressionSyntax sourceInvocation,
        out ILocalSymbol referencedLocal)
    {
        sourceInvocation = null!;
        referencedLocal = null!;

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return TryGetTrackedLocalReference(
                parenthesized.Expression,
                semanticModel,
                serviceVariables,
                out sourceInvocation,
                out referencedLocal);
        }

        if (expression is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol localSymbol &&
            serviceVariables.TryGetValue(localSymbol, out sourceInvocation))
        {
            referencedLocal = localSymbol;
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
