using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects scoped services resolved from a root service provider.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI019_RootScopedResolutionAnalyzer : DiagnosticAnalyzer
{
    internal const string ScopedDependencyTypeNamePropertyName = "ScopedDependencyTypeName";
    internal const string ResolutionPathPropertyName = "ResolutionPath";

    private readonly struct InvocationObservation
    {
        public InvocationObservation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            Invocation = invocation;
            SemanticModel = semanticModel;
        }

        public InvocationExpressionSyntax Invocation { get; }

        public SemanticModel SemanticModel { get; }
    }

    private sealed class ProviderFacts
    {
        public ProviderFacts()
        {
            Facts = [];
            SymbolsWithFacts = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            EarliestDeferredWritePositions = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);
            RefAliasFacts = new Dictionary<ISymbol, List<RefAliasFact>>(SymbolEqualityComparer.Default);
        }

        public List<ProviderFact> Facts { get; }

        private HashSet<ISymbol> SymbolsWithFacts { get; }

        private Dictionary<ISymbol, int> EarliestDeferredWritePositions { get; }

        private Dictionary<ISymbol, List<RefAliasFact>> RefAliasFacts { get; }

        public void Add(ProviderFact fact)
        {
            Facts.Add(fact);
            SymbolsWithFacts.Add(fact.Symbol);
        }

        public bool HasFact(ISymbol symbol) => SymbolsWithFacts.Contains(symbol);

        public void MarkDeferredWrite(ISymbol symbol, int position)
        {
            if (!EarliestDeferredWritePositions.TryGetValue(symbol, out var existingPosition) ||
                position < existingPosition)
            {
                EarliestDeferredWritePositions[symbol] = position;
            }
        }

        public void SetRefAlias(
            ISymbol alias,
            IEnumerable<ISymbol> referents,
            int position)
        {
            var storageSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var referent in referents)
            {
                storageSymbols.UnionWith(ResolveStorageSymbols(referent, position));
            }

            AddRefAliasFact(alias, position, storageSymbols);
        }

        public void AddPossibleRefAliases(
            ISymbol alias,
            IEnumerable<ISymbol> referents,
            int position)
        {
            var possibleReferents = ResolveStorageSymbols(alias, position);
            foreach (var referent in referents)
            {
                possibleReferents.UnionWith(ResolveStorageSymbols(referent, position));
            }

            AddRefAliasFact(alias, position, possibleReferents);
        }

        public HashSet<ISymbol> ResolveStorageSymbols(ISymbol symbol, int position)
        {
            var storageSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var pending = new Stack<ISymbol>();
            pending.Push(symbol);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }

                if (TryGetLatestRefAliasFactBefore(current, position, out var referents))
                {
                    foreach (var referent in referents)
                    {
                        pending.Push(referent);
                    }
                }
                else
                {
                    storageSymbols.Add(current);
                }
            }

            if (storageSymbols.Count == 0)
            {
                storageSymbols.Add(symbol);
            }

            return storageSymbols;
        }

        private void AddRefAliasFact(
            ISymbol alias,
            int position,
            HashSet<ISymbol> referents)
        {
            if (!RefAliasFacts.TryGetValue(alias, out var aliasFacts))
            {
                aliasFacts = [];
                RefAliasFacts[alias] = aliasFacts;
            }

            aliasFacts.Add(new RefAliasFact(position, referents));
        }

        private bool TryGetLatestRefAliasFactBefore(
            ISymbol alias,
            int position,
            out HashSet<ISymbol> referents)
        {
            RefAliasFact? latest = null;
            if (RefAliasFacts.TryGetValue(alias, out var aliasFacts))
            {
                foreach (var aliasFact in aliasFacts)
                {
                    if (aliasFact.Position >= position ||
                        latest is not null && aliasFact.Position <= latest.Value.Position)
                    {
                        continue;
                    }

                    latest = aliasFact;
                }
            }

            if (latest is null)
            {
                referents = [];
                return false;
            }

            referents = latest.Value.Referents;
            return true;
        }

        public bool TryGetLatestFactBefore(
            ISymbol symbol,
            int position,
            out bool isRootProvider,
            out bool isPathStableForConditionalJoin)
        {
            ProviderFact? latest = null;
            foreach (var fact in Facts)
            {
                if (fact.Position >= position ||
                    !SymbolEqualityComparer.Default.Equals(fact.Symbol, symbol))
                {
                    continue;
                }

                if (latest is null || fact.Position > latest.Value.Position)
                {
                    latest = fact;
                }
            }

            if (latest is null)
            {
                isRootProvider = false;
                isPathStableForConditionalJoin = false;
                return false;
            }

            if (!latest.Value.IsRootProvider.HasValue)
            {
                isRootProvider = false;
                isPathStableForConditionalJoin = false;
                return false;
            }

            isRootProvider = latest.Value.IsRootProvider.Value;
            isPathStableForConditionalJoin =
                latest.Value.IsPathStableForConditionalJoin &&
                (!EarliestDeferredWritePositions.TryGetValue(symbol, out var deferredPosition) ||
                 deferredPosition >= position);
            return true;
        }
    }

    private readonly struct RefAliasFact
    {
        public RefAliasFact(int position, HashSet<ISymbol> referents)
        {
            Position = position;
            Referents = referents;
        }

        public int Position { get; }

        public HashSet<ISymbol> Referents { get; }
    }

    private readonly struct ProviderFact
    {
        public ProviderFact(
            ISymbol symbol,
            int position,
            bool? isRootProvider,
            bool isPathStableForConditionalJoin)
        {
            Symbol = symbol;
            Position = position;
            IsRootProvider = isRootProvider;
            IsPathStableForConditionalJoin = isPathStableForConditionalJoin;
        }

        public ISymbol Symbol { get; }

        public int Position { get; }

        public bool? IsRootProvider { get; }

        public bool IsPathStableForConditionalJoin { get; }
    }

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.RootScopedResolution);

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

            var invocations = new ConcurrentQueue<InvocationObservation>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
                    registrationCollector.AnalyzeInvocation(invocation, syntaxContext.SemanticModel);
                    invocations.Enqueue(new InvocationObservation(invocation, syntaxContext.SemanticModel));
                },
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterCompilationEndAction(endContext =>
                AnalyzeRootScopedResolutions(
                    endContext,
                    registrationCollector,
                    wellKnownTypes,
                    invocations.ToImmutableArray()));
        });
    }

    private static void AnalyzeRootScopedResolutions(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        ImmutableArray<InvocationObservation> invocations)
    {
        var providerFactsByTree = BuildProviderFactsByTree(context.Compilation, wellKnownTypes);
        var singletonImplementationTypes = GetSingletonImplementationTypes(registrationCollector);
        var scopedGraph = new ScopedDependencyGraph(
            registrationCollector,
            wellKnownTypes,
            context.Compilation);

        foreach (var observation in invocations)
        {
            var invocation = observation.Invocation;
            var semanticModel = observation.SemanticModel;
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol ||
                !TryGetServiceResolutionRequest(
                    invocation,
                    methodSymbol,
                    semanticModel,
                    wellKnownTypes,
                    out var serviceType,
                    out var key,
                    out var isKeyed,
                    out var isEnumerableRequest) ||
                !TryGetResolutionReceiver(invocation, methodSymbol, semanticModel, out var receiver))
            {
                continue;
            }

            if (IsInServiceRegistrationFactoryContext(invocation, semanticModel))
            {
                continue;
            }

            var providerFacts = providerFactsByTree.TryGetValue(invocation.SyntaxTree, out var facts)
                ? facts
                : new ProviderFacts();

            if (!IsRootProviderReceiver(
                    receiver,
                    invocation,
                    semanticModel,
                    wellKnownTypes,
                    providerFacts,
                    singletonImplementationTypes))
            {
                continue;
            }

            if (!scopedGraph.TryFindScopedDependency(
                    serviceType,
                    key,
                    isKeyed,
                    isEnumerableRequest,
                    out var scopedMatch))
            {
                continue;
            }

            ReportDiagnostic(
                context,
                invocation.GetLocation(),
                serviceType,
                scopedMatch);
        }
    }

    private static Dictionary<SyntaxTree, ProviderFacts> BuildProviderFactsByTree(
        Compilation compilation,
        WellKnownTypes wellKnownTypes)
    {
        var factsByTree = new Dictionary<SyntaxTree, ProviderFacts>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            #pragma warning disable RS1030
            var semanticModel = compilation.GetSemanticModel(tree);
            #pragma warning restore RS1030
            var facts = new ProviderFacts();
            var root = tree.GetRoot();
            var gotoSkippedAssignmentPositions = GetGotoSkippedAssignmentPositions(root);
            var backwardGotoExecutableBoundaries = GetBackwardGotoExecutableBoundaries(root);

            foreach (var node in root.DescendantNodes())
            {
                ISymbol? symbol;
                ExpressionSyntax expression;
                int writePosition;
                var forceInvalidation = false;
                switch (node)
                {
                    case VariableDeclaratorSyntax
                    {
                        Initializer.Value: RefExpressionSyntax refExpression
                    } variable:
                        expression = refExpression.Expression;
                        var alias = semanticModel.GetDeclaredSymbol(variable);
                        var referents = GetRefReferentSymbols(expression, semanticModel).ToArray();
                        if (alias is not null && referents.Length > 0)
                        {
                            facts.SetRefAlias(alias, referents, refExpression.Span.End);
                        }

                        continue;
                    case AssignmentExpressionSyntax refConditionalAssignment
                        when refConditionalAssignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                             Unwrap(refConditionalAssignment.Left) is ConditionalExpressionSyntax:
                        InvalidateRefTargetFacts(
                            refConditionalAssignment.Left,
                            semanticModel,
                            facts,
                            refConditionalAssignment.Right.Span.End);
                        continue;
                    case AssignmentExpressionSyntax refAssignment
                        when refAssignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                             refAssignment.Right is RefExpressionSyntax refAssignmentExpression:
                        var refAlias = semanticModel.GetSymbolInfo(refAssignment.Left).Symbol;
                        var refReferents = GetRefReferentSymbols(
                                refAssignmentExpression.Expression,
                                semanticModel)
                            .ToArray();
                        if (refAlias is not null && refReferents.Length > 0)
                        {
                            if (IsPathStableForConditionalJoin(
                                    refAssignment.Right,
                                    refAlias,
                                    gotoSkippedAssignmentPositions,
                                    backwardGotoExecutableBoundaries))
                            {
                                facts.SetRefAlias(
                                    refAlias,
                                    refReferents,
                                    refAssignment.Right.Span.End);
                            }
                            else
                            {
                                facts.AddPossibleRefAliases(
                                    refAlias,
                                    refReferents,
                                    refAssignment.Right.Span.End);
                            }
                        }

                        continue;
                    case AssignmentExpressionSyntax deconstruction
                        when deconstruction.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                             deconstruction.Left is TupleExpressionSyntax tuple:
                        foreach (var target in GetDeconstructionTargets(tuple))
                        {
                            var targetSymbol = semanticModel.GetSymbolInfo(Unwrap(target)).Symbol;
                            if (targetSymbol is not null)
                            {
                                foreach (var storageSymbol in facts.ResolveStorageSymbols(
                                             targetSymbol,
                                             target.SpanStart))
                                {
                                    var deferredWritePosition =
                                        GetDeferredWriteReachabilityPosition(
                                            target,
                                            storageSymbol);
                                    if (deferredWritePosition.HasValue)
                                    {
                                        facts.MarkDeferredWrite(
                                            storageSymbol,
                                            deferredWritePosition.Value);
                                    }

                                    InvalidateProviderFact(
                                        storageSymbol,
                                        deconstruction.Right.Span.End,
                                        facts);
                                }
                            }
                        }

                        continue;
                    case VariableDeclaratorSyntax { Initializer.Value: { } initializer } variable:
                        symbol = semanticModel.GetDeclaredSymbol(variable);
                        expression = initializer;
                        writePosition = initializer.SpanStart;
                        break;
                    case AssignmentExpressionSyntax assignment
                        when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
                        symbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
                        expression = assignment.Right;
                        writePosition = assignment.Right.Span.End;
                        break;
                    case AssignmentExpressionSyntax assignment
                        when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression):
                        symbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
                        expression = assignment.Right;
                        writePosition = assignment.Right.Span.End;
                        forceInvalidation = true;
                        break;
                    case ArgumentSyntax argument
                        when argument.RefKindKeyword.Kind() is
                            SyntaxKind.RefKeyword or SyntaxKind.OutKeyword:
                        InvalidateRefTargetFacts(
                            argument.Expression,
                            semanticModel,
                            facts,
                            argument.Parent?.Parent?.Span.End ?? argument.Span.End);
                        continue;
                    default:
                        continue;
                }

                if (symbol is null)
                {
                    continue;
                }

                foreach (var storageSymbol in facts.ResolveStorageSymbols(
                             symbol,
                             expression.SpanStart))
                {
                    var deferredPosition = GetDeferredWriteReachabilityPosition(
                        expression,
                        storageSymbol);
                    if (deferredPosition.HasValue)
                    {
                        facts.MarkDeferredWrite(storageSymbol, deferredPosition.Value);
                    }

                    if (forceInvalidation)
                    {
                        InvalidateProviderFact(storageSymbol, writePosition, facts);
                        continue;
                    }

                    AddProviderFact(
                        storageSymbol,
                        expression,
                        writePosition,
                        semanticModel,
                        wellKnownTypes,
                        facts,
                        gotoSkippedAssignmentPositions,
                        backwardGotoExecutableBoundaries);
                }
            }

            factsByTree[tree] = facts;
        }

        return factsByTree;
    }

    private static void InvalidateRefTargetFacts(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        ProviderFacts facts,
        int writePosition)
    {
        var storageSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var referent in GetRefReferentSymbols(expression, semanticModel))
        {
            storageSymbols.UnionWith(facts.ResolveStorageSymbols(
                referent,
                expression.SpanStart));
        }

        foreach (var storageSymbol in storageSymbols)
        {
            var deferredPosition = GetDeferredWriteReachabilityPosition(
                expression,
                storageSymbol);
            if (deferredPosition.HasValue)
            {
                facts.MarkDeferredWrite(storageSymbol, deferredPosition.Value);
            }

            InvalidateProviderFact(storageSymbol, writePosition, facts);
        }
    }

    private static void AddProviderFact(
        ISymbol symbol,
        ExpressionSyntax expression,
        int position,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        ProviderFacts facts,
        ImmutableHashSet<int> gotoSkippedAssignmentPositions,
        ImmutableHashSet<SyntaxNode> backwardGotoExecutableBoundaries)
    {
        var isPathStableForConditionalJoin =
            symbol is not IFieldSymbol &&
            symbol is not IPropertySymbol &&
            IsPathStableForConditionalJoin(
                expression,
                symbol,
                gotoSkippedAssignmentPositions,
                backwardGotoExecutableBoundaries) &&
            IsReferencedProviderFactPathStableForConditionalJoin(
                expression,
                position,
                semanticModel,
                facts);
        if (IsScopedProviderExpression(expression, semanticModel, wellKnownTypes, facts, position))
        {
            facts.Add(new ProviderFact(
                symbol,
                position,
                isRootProvider: false,
                isPathStableForConditionalJoin));
            return;
        }

        if (IsRootProviderExpression(expression, semanticModel, wellKnownTypes, facts, position))
        {
            facts.Add(new ProviderFact(
                symbol,
                position,
                isRootProvider: true,
                isPathStableForConditionalJoin));
            return;
        }

        InvalidateProviderFact(symbol, position, facts);
    }

    private static void InvalidateProviderFact(
        ISymbol symbol,
        int position,
        ProviderFacts facts)
    {
        if (facts.HasFact(symbol))
        {
            facts.Add(new ProviderFact(
                symbol,
                position,
                isRootProvider: null,
                isPathStableForConditionalJoin: false));
        }
    }

    private static bool IsPathStableForConditionalJoin(
        ExpressionSyntax expression,
        ISymbol writtenSymbol,
        ImmutableHashSet<int> gotoSkippedAssignmentPositions,
        ImmutableHashSet<SyntaxNode> backwardGotoExecutableBoundaries)
    {
        if (backwardGotoExecutableBoundaries.Contains(GetExecutableBoundary(expression)))
        {
            return false;
        }

        if (expression.Parent is not AssignmentExpressionSyntax assignment)
        {
            return true;
        }

        return !gotoSkippedAssignmentPositions.Contains(assignment.SpanStart) &&
               IsPathStableWithinOwningDeferredBoundary(assignment, writtenSymbol);
    }

    private static bool IsPathStableWithinOwningDeferredBoundary(
        AssignmentExpressionSyntax assignment,
        ISymbol writtenSymbol)
    {
        foreach (var ancestor in assignment.Ancestors())
        {
            if (IsDeferredBoundaryOwnedBySymbol(ancestor, writtenSymbol))
            {
                return true;
            }

            if (IsControlFlowDependentProviderWrite(ancestor))
            {
                return false;
            }
        }

        return true;
    }

    private static ImmutableHashSet<int> GetGotoSkippedAssignmentPositions(SyntaxNode root)
    {
        var labels = root.DescendantNodes().OfType<LabeledStatementSyntax>().ToArray();
        if (labels.Length == 0)
        {
            return ImmutableHashSet<int>.Empty;
        }

        var assignments = root.DescendantNodes().OfType<AssignmentExpressionSyntax>().ToArray();
        var builder = ImmutableHashSet.CreateBuilder<int>();
        foreach (var gotoStatement in root.DescendantNodes().OfType<GotoStatementSyntax>())
        {
            if (gotoStatement.Expression is not IdentifierNameSyntax target)
            {
                continue;
            }

            var label = labels.FirstOrDefault(candidate =>
                candidate.Identifier.ValueText == target.Identifier.ValueText &&
                ReferenceEquals(
                    GetExecutableBoundary(candidate),
                    GetExecutableBoundary(gotoStatement)));
            if (label is null || label.SpanStart <= gotoStatement.Span.End)
            {
                continue;
            }

            foreach (var assignment in assignments)
            {
                if (assignment.SpanStart >= gotoStatement.Span.End &&
                    assignment.Span.End <= label.SpanStart &&
                    ReferenceEquals(
                        GetExecutableBoundary(assignment),
                        GetExecutableBoundary(gotoStatement)))
                {
                    builder.Add(assignment.SpanStart);
                }
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<SyntaxNode> GetBackwardGotoExecutableBoundaries(
        SyntaxNode root)
    {
        var labels = root.DescendantNodes().OfType<LabeledStatementSyntax>().ToArray();
        var builder = ImmutableHashSet.CreateBuilder<SyntaxNode>();
        foreach (var gotoStatement in root.DescendantNodes().OfType<GotoStatementSyntax>())
        {
            if (gotoStatement.Expression is not IdentifierNameSyntax target)
            {
                continue;
            }

            var boundary = GetExecutableBoundary(gotoStatement);
            if (labels.Any(candidate =>
                    candidate.SpanStart < gotoStatement.SpanStart &&
                    candidate.Identifier.ValueText == target.Identifier.ValueText &&
                    ReferenceEquals(GetExecutableBoundary(candidate), boundary)))
            {
                builder.Add(boundary);
            }
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<ExpressionSyntax> GetDeconstructionTargets(
        ExpressionSyntax expression)
    {
        if (expression is TupleExpressionSyntax tuple)
        {
            foreach (var argument in tuple.Arguments)
            {
                foreach (var target in GetDeconstructionTargets(argument.Expression))
                {
                    yield return target;
                }
            }

            yield break;
        }

        yield return expression;
    }

    private static SyntaxNode GetExecutableBoundary(SyntaxNode node) =>
        node.AncestorsAndSelf().FirstOrDefault(static ancestor =>
            ancestor is BaseMethodDeclarationSyntax or
                AccessorDeclarationSyntax or
                LocalFunctionStatementSyntax or
                AnonymousFunctionExpressionSyntax) ??
        node.SyntaxTree.GetRoot();

    private static bool IsReferencedProviderFactPathStableForConditionalJoin(
        ExpressionSyntax expression,
        int position,
        SemanticModel semanticModel,
        ProviderFacts providerFacts)
    {
        expression = Unwrap(expression);
        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        return symbol is null ||
               !TryGetProviderFactBefore(
                   providerFacts,
                   symbol,
                   position,
                   out _,
                   out var isPathStableForConditionalJoin) ||
               isPathStableForConditionalJoin;
    }

    private static bool TryGetProviderFactBefore(
        ProviderFacts providerFacts,
        ISymbol symbol,
        int position,
        out bool isRootProvider,
        out bool isPathStableForConditionalJoin)
    {
        bool? commonRootProvider = null;
        isPathStableForConditionalJoin = true;
        foreach (var storageSymbol in providerFacts.ResolveStorageSymbols(symbol, position))
        {
            if (!providerFacts.TryGetLatestFactBefore(
                    storageSymbol,
                    position,
                    out var storageIsRootProvider,
                    out var storageIsPathStableForConditionalJoin) ||
                commonRootProvider.HasValue &&
                commonRootProvider.Value != storageIsRootProvider)
            {
                isRootProvider = false;
                isPathStableForConditionalJoin = false;
                return false;
            }

            commonRootProvider = storageIsRootProvider;
            isPathStableForConditionalJoin &= storageIsPathStableForConditionalJoin;
        }

        isRootProvider = commonRootProvider.GetValueOrDefault();
        return commonRootProvider.HasValue;
    }

    private static int? GetDeferredWriteReachabilityPosition(
        SyntaxNode node,
        ISymbol writtenSymbol)
    {
        int? earliestPosition = null;
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            if (ancestor is LocalFunctionStatementSyntax localFunction)
            {
                if (!IsDeclaredWithin(writtenSymbol, localFunction))
                {
                    return 0;
                }

                continue;
            }

            if (ancestor is not AnonymousFunctionExpressionSyntax and
                not QueryExpressionSyntax)
            {
                continue;
            }

            if (IsDeclaredWithin(writtenSymbol, ancestor))
            {
                continue;
            }

            if (!earliestPosition.HasValue || ancestor.SpanStart < earliestPosition.Value)
            {
                earliestPosition = ancestor.SpanStart;
            }
        }

        return earliestPosition;
    }

    private static bool IsDeclaredWithin(ISymbol symbol, SyntaxNode boundary) =>
        symbol.DeclaringSyntaxReferences.Any(reference =>
            ReferenceEquals(reference.SyntaxTree, boundary.SyntaxTree) &&
            boundary.Span.Contains(reference.Span));

    private static bool IsDeferredBoundaryOwnedBySymbol(
        SyntaxNode boundary,
        ISymbol symbol) =>
        (boundary is LocalFunctionStatementSyntax or
            AnonymousFunctionExpressionSyntax or
            QueryExpressionSyntax) &&
        IsDeclaredWithin(symbol, boundary);

    private static IEnumerable<ISymbol> GetRefReferentSymbols(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        expression = Unwrap(expression);
        if (expression is RefExpressionSyntax refExpression)
        {
            foreach (var symbol in GetRefReferentSymbols(refExpression.Expression, semanticModel))
            {
                yield return symbol;
            }

            yield break;
        }

        if (expression is ConditionalExpressionSyntax conditionalExpression)
        {
            foreach (var symbol in GetRefReferentSymbols(
                         conditionalExpression.WhenTrue,
                         semanticModel))
            {
                yield return symbol;
            }

            foreach (var symbol in GetRefReferentSymbols(
                         conditionalExpression.WhenFalse,
                         semanticModel))
            {
                yield return symbol;
            }

            yield break;
        }

        var referent = semanticModel.GetSymbolInfo(expression).Symbol;
        if (referent is not null)
        {
            yield return referent;
        }
    }

    private static bool IsControlFlowDependentProviderWrite(SyntaxNode ancestor) =>
        ancestor is IfStatementSyntax or
            SwitchStatementSyntax or
            SwitchExpressionSyntax or
            ForStatementSyntax or
            CommonForEachStatementSyntax or
            WhileStatementSyntax or
            DoStatementSyntax or
            TryStatementSyntax or
            AnonymousFunctionExpressionSyntax or
            LocalFunctionStatementSyntax or
            QueryExpressionSyntax or
            ConditionalExpressionSyntax ||
        ancestor is BinaryExpressionSyntax binary &&
            binary.Kind() is SyntaxKind.LogicalAndExpression or
                SyntaxKind.LogicalOrExpression or
                SyntaxKind.CoalesceExpression;

    private static ImmutableHashSet<INamedTypeSymbol> GetSingletonImplementationTypes(
        RegistrationCollector registrationCollector)
    {
        var builder = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var registration in registrationCollector.AllRegistrations)
        {
            if (registration.Lifetime == ServiceLifetime.Singleton &&
                !registration.HasImplementationInstance &&
                registration.ImplementationType is not null)
            {
                builder.Add(registration.ImplementationType);
            }
        }

        return builder.ToImmutable();
    }

    private static bool TryGetServiceResolutionRequest(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        out ITypeSymbol serviceType,
        out object? key,
        out bool isKeyed,
        out bool isEnumerableRequest)
    {
        serviceType = null!;
        key = null;
        isKeyed = false;
        isEnumerableRequest = false;

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        if (!IsServiceResolutionMethod(sourceMethod, semanticModel.Compilation, wellKnownTypes))
        {
            return false;
        }

        if (!TryGetResolvedServiceType(invocation, methodSymbol, semanticModel, out serviceType))
        {
            return false;
        }

        isEnumerableRequest = sourceMethod.Name is "GetServices" or "GetKeyedServices";
        if (!isEnumerableRequest &&
            TryUnwrapEnumerableServiceType(serviceType, out var elementType))
        {
            serviceType = elementType;
            isEnumerableRequest = true;
        }

        isKeyed = sourceMethod.Name is "GetKeyedService" or "GetRequiredKeyedService" or "GetKeyedServices";
        if (isKeyed &&
            !TryExtractKeyFromResolution(invocation, methodSymbol, semanticModel, out key))
        {
            return false;
        }

        return true;
    }

    private static bool TryUnwrapEnumerableServiceType(
        ITypeSymbol serviceType,
        out ITypeSymbol elementType)
    {
        if (serviceType is INamedTypeSymbol
            {
                IsGenericType: true
            } namedType &&
            (namedType.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T ||
             namedType.Name == "IEnumerable" &&
             namedType.ContainingNamespace.ToDisplayString() == "System.Collections.Generic"))
        {
            elementType = namedType.TypeArguments[0];
            return true;
        }

        elementType = serviceType;
        return false;
    }

    private static bool IsServiceResolutionMethod(
        IMethodSymbol sourceMethod,
        Compilation compilation,
        WellKnownTypes wellKnownTypes)
    {
        if (sourceMethod.Name is not ("GetService" or "GetRequiredService" or "GetServices" or
            "GetKeyedService" or "GetRequiredKeyedService" or "GetKeyedServices"))
        {
            return false;
        }

        if (sourceMethod.IsExtensionMethod && sourceMethod.Parameters.Length > 0)
        {
            if (!IsFrameworkServiceResolutionExtension(sourceMethod, compilation))
            {
                return false;
            }

            var receiverType = sourceMethod.Parameters[0].Type;
            return wellKnownTypes.IsServiceProvider(receiverType) ||
                   wellKnownTypes.IsKeyedServiceProvider(receiverType);
        }

        return wellKnownTypes.IsServiceProvider(sourceMethod.ContainingType) ||
               wellKnownTypes.IsKeyedServiceProvider(sourceMethod.ContainingType) ||
               IsConcreteServiceProvider(sourceMethod.ContainingType);
    }

    private static bool IsFrameworkServiceResolutionExtension(
        IMethodSymbol method,
        Compilation compilation)
    {
        var expectedTypeName = method.Name is "GetKeyedService" or "GetRequiredKeyedService" or "GetKeyedServices"
            ? "ServiceProviderKeyedServiceExtensions"
            : "ServiceProviderServiceExtensions";

        if (method.ContainingType.Name != expectedTypeName ||
            method.ContainingType.ContainingNamespace.ToDisplayString() !=
            "Microsoft.Extensions.DependencyInjection")
        {
            return false;
        }

        if (!method.ContainingType.Locations.Any(location => location.IsInSource))
        {
            return method.ContainingAssembly.Name == "Microsoft.Extensions.DependencyInjection.Abstractions";
        }

        var metadataName = $"Microsoft.Extensions.DependencyInjection.{expectedTypeName}";
        return !compilation.SourceModule.ReferencedAssemblySymbols.Any(
            assembly => assembly.GetTypeByMetadataName(metadataName) is not null);
    }

    private static bool IsConcreteServiceProvider(ITypeSymbol? type) =>
        type?.Name == "ServiceProvider" &&
        type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";

    private static bool TryGetResolvedServiceType(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        out ITypeSymbol serviceType)
    {
        serviceType = null!;

        if (methodSymbol.IsGenericMethod && methodSymbol.TypeArguments.Length > 0)
        {
            serviceType = methodSymbol.TypeArguments[0];
            return true;
        }

        var typeExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "serviceType") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "type");

        if (typeExpression is null && invocation.ArgumentList.Arguments.Count > 0)
        {
            typeExpression = invocation.ArgumentList.Arguments[0].Expression;
        }

        if (typeExpression is not TypeOfExpressionSyntax typeOfExpression)
        {
            return false;
        }

        serviceType = semanticModel.GetTypeInfo(typeOfExpression.Type).Type!;
        return serviceType is not null;
    }

    private static bool TryExtractKeyFromResolution(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        out object? key)
    {
        key = null;

        var keyExpression =
            GetInvocationArgumentExpression(invocation, methodSymbol, "serviceKey") ??
            GetInvocationArgumentExpression(invocation, methodSymbol, "key");

        if (keyExpression is null && invocation.ArgumentList.Arguments.Count >= 2)
        {
            keyExpression = invocation.ArgumentList.Arguments[1].Expression;
        }

        if (keyExpression is null)
        {
            return false;
        }

        return SyntaxValueHelpers.TryExtractServiceKeyValue(keyExpression, semanticModel, out key, out _) &&
               !SyntaxValueHelpers.IsKeyedServiceAnyKey(key);
    }

    private static bool TryGetResolutionReceiver(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        out ExpressionSyntax receiver)
    {
        receiver = null!;

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        if (methodSymbol.ReducedFrom is null &&
            sourceMethod.IsExtensionMethod &&
            semanticModel.GetOperation(invocation) is IInvocationOperation invocationOperation)
        {
            foreach (var argument in invocationOperation.Arguments)
            {
                if (argument.Parameter?.Ordinal == 0 && argument.Syntax is ArgumentSyntax argumentSyntax)
                {
                    receiver = argumentSyntax.Expression;
                    return true;
                }
            }
        }

        // The provider is the receiver of the resolution member itself. Check the member access
        // first: in `host?.Services.GetRequiredService<T>()` the conditional access's WhenNotNull is
        // the whole invocation, but the provider is the `.Services` member binding, not `host`.
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            receiver = memberAccess.Expression;
            return true;
        }

        if (invocation.Expression is MemberBindingExpressionSyntax &&
            invocation.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
            conditionalAccess.WhenNotNull == invocation)
        {
            receiver = conditionalAccess.Expression;
            return true;
        }

        if (!sourceMethod.IsExtensionMethod || invocation.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        receiver = invocation.ArgumentList.Arguments[0].Expression;
        return true;
    }

    private static bool IsRootProviderReceiver(
        ExpressionSyntax receiver,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        ProviderFacts providerFacts,
        ImmutableHashSet<INamedTypeSymbol> singletonImplementationTypes)
    {
        var position = invocation.SpanStart;
        if (IsScopedProviderExpression(receiver, semanticModel, wellKnownTypes, providerFacts, position))
        {
            return false;
        }

        if (IsRootProviderExpression(receiver, semanticModel, wellKnownTypes, providerFacts, position))
        {
            return true;
        }

        if (!wellKnownTypes.IsAnyServiceProvider(semanticModel.GetTypeInfo(receiver).Type))
        {
            return false;
        }

        if (!IsInsideSingletonImplementation(invocation, semanticModel, singletonImplementationTypes))
        {
            return false;
        }

        var symbol = semanticModel.GetSymbolInfo(receiver).Symbol;
        return symbol is IFieldSymbol or IPropertySymbol or IParameterSymbol or ILocalSymbol;
    }

    private static bool IsRootProviderExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        ProviderFacts providerFacts,
        int position,
        bool requirePathStableFact = false)
    {
        expression = Unwrap(expression);

        if (expression is ConditionalExpressionSyntax conditionalExpression)
        {
            return IsRootProviderExpression(
                       conditionalExpression.WhenTrue,
                       semanticModel,
                       wellKnownTypes,
                       providerFacts,
                       position,
                       requirePathStableFact: true) &&
                   IsRootProviderExpression(
                       conditionalExpression.WhenFalse,
                       semanticModel,
                       wellKnownTypes,
                       providerFacts,
                       position,
                       requirePathStableFact: true);
        }

        if (expression is InvocationExpressionSyntax invocation &&
            IsBuildServiceProviderInvocation(invocation, semanticModel))
        {
            return true;
        }

        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        if (symbol is not null &&
            TryGetProviderFactBefore(
                providerFacts,
                symbol,
                position,
                out var isRootProvider,
                out var isPathStableForConditionalJoin))
        {
            if (requirePathStableFact && !isPathStableForConditionalJoin)
            {
                return false;
            }

            return isRootProvider;
        }

        if (expression is MemberAccessExpressionSyntax rootProviderProperty &&
            IsKnownRootProviderProperty(rootProviderProperty, semanticModel, wellKnownTypes))
        {
            return true;
        }

        // Conditional-access form: `host?.Services.GetRequiredService<T>()` exposes the `.Services`
        // receiver as a MemberBindingExpressionSyntax whose owner is the conditional access's
        // expression.
        if (expression is MemberBindingExpressionSyntax rootProviderBinding &&
            IsKnownRootProviderProperty(rootProviderBinding, semanticModel, wellKnownTypes))
        {
            return true;
        }

        return false;
    }

    private static bool IsKnownRootProviderProperty(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        if (!wellKnownTypes.IsServiceProvider(semanticModel.GetTypeInfo(memberAccess).Type))
        {
            return false;
        }

        var ownerType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        return IsKnownRootProviderPropertyName(memberAccess.Name.Identifier.Text, ownerType);
    }

    private static bool IsKnownRootProviderProperty(
        MemberBindingExpressionSyntax memberBinding,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        if (!wellKnownTypes.IsServiceProvider(semanticModel.GetTypeInfo(memberBinding).Type))
        {
            return false;
        }

        if (TryGetConditionalAccessReceiver(memberBinding) is not { } owner)
        {
            return false;
        }

        var ownerType = semanticModel.GetTypeInfo(owner).Type;
        return IsKnownRootProviderPropertyName(memberBinding.Name.Identifier.Text, ownerType);
    }

    private static bool IsKnownRootProviderPropertyName(string propertyName, ITypeSymbol? ownerType) =>
        propertyName switch
        {
            "Services" => IsKnownRootServicesOwner(ownerType),
            "ApplicationServices" => IsNamedOrImplements(
                ownerType,
                "Microsoft.AspNetCore.Builder",
                "IApplicationBuilder"),
            "ServiceProvider" => IsNamedOrImplements(
                ownerType,
                "Microsoft.AspNetCore.Routing",
                "IEndpointRouteBuilder"),
            _ => false
        };

    /// <summary>
    /// Resolves the receiver that a conditional-access member binding is evaluated against: for
    /// `host?.Services` the `.Services` binding's receiver is `host`. The owning conditional access
    /// is the nearest ancestor whose <c>WhenNotNull</c> contains the binding -- the binding can also
    /// be the <c>Expression</c> of an inner chained conditional access (`host?.Services?.X`), which
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

    private static bool IsKnownRootServicesOwner(ITypeSymbol? ownerType)
    {
        if (ownerType is INamedTypeSymbol namedOwner)
        {
            return IsKnownTypeOrBaseType(namedOwner, "Microsoft.AspNetCore.Builder", "WebApplication") ||
                   IsKnownTypeOrBaseType(namedOwner, "Microsoft.AspNetCore.Mvc.Testing", "WebApplicationFactory") ||
                   IsKnownTypeOrBaseType(namedOwner, "Microsoft.AspNetCore.TestHost", "TestServer") ||
                   IsNamedOrImplements(namedOwner, "Microsoft.Extensions.Hosting", "IHost") ||
                   IsNamedOrImplements(namedOwner, "Microsoft.AspNetCore.Hosting", "IWebHost");
        }

        if (ownerType is ITypeParameterSymbol typeParameter)
        {
            return typeParameter.ConstraintTypes.Any(IsKnownRootServicesOwner);
        }

        return false;
    }

    private static bool IsNamedOrImplements(
        ITypeSymbol? type,
        string namespaceName,
        string typeName)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        return IsKnownType(namedType, namespaceName, typeName) ||
               namedType.AllInterfaces.Any(iface => IsKnownType(iface, namespaceName, typeName));
    }

    private static bool IsKnownType(
        INamedTypeSymbol type,
        string namespaceName,
        string typeName) =>
        type.Name == typeName &&
        type.ContainingNamespace.ToDisplayString() == namespaceName;

    private static bool IsKnownTypeOrBaseType(
        INamedTypeSymbol type,
        string namespaceName,
        string typeName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (IsKnownType(current, namespaceName, typeName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsScopedProviderExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        ProviderFacts providerFacts,
        int position)
    {
        expression = Unwrap(expression);

        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        if (symbol is not null &&
            TryGetProviderFactBefore(
                providerFacts,
                symbol,
                position,
                out var isRootProvider,
                out _))
        {
            return !isRootProvider;
        }

        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Name.Identifier.Text == "RequestServices" &&
                wellKnownTypes.IsServiceProvider(semanticModel.GetTypeInfo(expression).Type))
            {
                return true;
            }

            if (memberAccess.Name.Identifier.Text == "ServiceProvider")
            {
                var ownerType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
                return wellKnownTypes.IsServiceScope(ownerType) ||
                       wellKnownTypes.IsAsyncServiceScope(ownerType);
            }
        }

        // Conditional-access form: `httpContext?.RequestServices...` / `scope?.ServiceProvider...`
        // expose the scoped-provider property as a MemberBindingExpressionSyntax.
        if (expression is MemberBindingExpressionSyntax memberBinding)
        {
            if (memberBinding.Name.Identifier.Text == "RequestServices" &&
                wellKnownTypes.IsServiceProvider(semanticModel.GetTypeInfo(expression).Type))
            {
                return true;
            }

            if (memberBinding.Name.Identifier.Text == "ServiceProvider" &&
                TryGetConditionalAccessReceiver(memberBinding) is { } scopeOwner)
            {
                var ownerType = semanticModel.GetTypeInfo(scopeOwner).Type;
                return wellKnownTypes.IsServiceScope(ownerType) ||
                       wellKnownTypes.IsAsyncServiceScope(ownerType);
            }
        }

        return false;
    }

    private static bool IsBuildServiceProviderInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        return sourceMethod.Name == "BuildServiceProvider" &&
               sourceMethod.IsExtensionMethod &&
               sourceMethod.Parameters.Length > 0 &&
               sourceMethod.Parameters[0].Type.Name == "IServiceCollection" &&
               sourceMethod.Parameters[0].Type.ContainingNamespace.ToDisplayString() ==
               "Microsoft.Extensions.DependencyInjection";
    }

    private static bool IsInsideSingletonImplementation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ImmutableHashSet<INamedTypeSymbol> singletonImplementationTypes)
    {
        foreach (var typeDeclaration in invocation.Ancestors().OfType<TypeDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(typeDeclaration) is INamedTypeSymbol typeSymbol &&
                singletonImplementationTypes.Contains(typeSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInServiceRegistrationFactoryContext(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        for (var node = invocation.Parent; node is not null; node = node.Parent)
        {
            if (node is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            {
                return IsFactoryRegistrationContext(node, semanticModel);
            }

            if (node is MethodDeclarationSyntax or TypeDeclarationSyntax)
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsFactoryRegistrationContext(SyntaxNode lambda, SemanticModel semanticModel)
    {
        for (var parent = lambda.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is InvocationExpressionSyntax parentInvocation &&
                semanticModel.GetSymbolInfo(parentInvocation).Symbol is IMethodSymbol parentMethod)
            {
                var sourceMethod = parentMethod.ReducedFrom ?? parentMethod;
                if ((sourceMethod.Name.StartsWith("Add") || sourceMethod.Name == "TryAdd") &&
                    sourceMethod.IsExtensionMethod &&
                    sourceMethod.Parameters.Length > 0 &&
                    sourceMethod.Parameters[0].Type.Name == "IServiceCollection" &&
                    sourceMethod.Parameters[0].Type.ContainingNamespace.ToDisplayString() ==
                    "Microsoft.Extensions.DependencyInjection")
                {
                    return true;
                }
            }

            if (parent is MethodDeclarationSyntax or TypeDeclarationSyntax)
            {
                break;
            }
        }

        return false;
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

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
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
                case PostfixUnaryExpressionSyntax postfixUnaryExpression
                    when postfixUnaryExpression.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfixUnaryExpression.Operand;
                    continue;
                case ConditionalAccessExpressionSyntax conditionalAccessExpression:
                    // `app?.Services` classifies like its WhenNotNull member binding: the value the
                    // expression produces (when not null) is that member's value.
                    expression = conditionalAccessExpression.WhenNotNull;
                    continue;
                case BinaryExpressionSyntax binaryExpression
                    when binaryExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                         binaryExpression.Right is ThrowExpressionSyntax:
                    expression = binaryExpression.Left;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static void ReportDiagnostic(
        CompilationAnalysisContext context,
        Location location,
        ITypeSymbol requestedType,
        ScopedDependencyGraph.ScopedDependencyMatch scopedMatch)
    {
        var requestedTypeName = requestedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var scopedTypeName = scopedMatch.ScopedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var resolutionPath = FormatResolutionPath(scopedMatch);
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(ScopedDependencyTypeNamePropertyName, scopedTypeName)
            .Add(ResolutionPathPropertyName, resolutionPath);

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.RootScopedResolution,
            location,
            additionalLocations: null,
            properties: properties,
            requestedTypeName,
            resolutionPath);

        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Renders the activation chain from the requested service to the captured scoped service as
    /// <c>Requested -&gt; Intermediate -&gt; Scoped</c>. This turns an otherwise opaque transitive
    /// warning into a precise map of exactly how the root provider reaches a scoped dependency.
    /// </summary>
    private static string FormatResolutionPath(
        ScopedDependencyGraph.ScopedDependencyMatch scopedMatch)
    {
        var path = scopedMatch.Path;
        if (path.IsDefaultOrEmpty)
        {
            return scopedMatch.ScopedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }

        return string.Join(
            " -> ",
            path.Select(node => node.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }
}
