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
                syntaxContext => AnalyzeType(syntaxContext, wellKnownTypes, hoistedScopes, hoistedServices),
                SyntaxKind.ClassDeclaration,
                SyntaxKind.RecordDeclaration);

            compilationContext.RegisterCompilationEndAction(endContext =>
            {
                // A field hoisted above loops in two execution methods (or above nested loops)
                // can produce one candidate per loop; merge by report location so one creation
                // site yields one diagnostic, with suppression judged across all observed uses.
                var mergedScopes = hoistedScopes
                    .GroupBy(candidate => candidate.Location)
                    .Select(group => new HoistedScopeCandidate(
                        group.Key,
                        group.OrderBy(candidate => candidate.LoopLocation.SourceSpan.Start).First().LoopLocation,
                        group.SelectMany(candidate => candidate.ResolvedServiceTypes).ToImmutableArray(),
                        group.Any(candidate => candidate.HasNonResolutionUse),
                        group.First().MethodName));

                foreach (var candidate in mergedScopes)
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

                foreach (var candidate in hoistedServices
                             .GroupBy(c => c.Location)
                             .Select(group => group.OrderBy(c => c.LoopLocation.SourceSpan.Start).First()))
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

    private static void AnalyzeType(
        SyntaxNodeAnalysisContext context,
        WellKnownTypes wellKnownTypes,
        ConcurrentBag<HoistedScopeCandidate> hoistedScopes,
        ConcurrentBag<HoistedServiceCandidate> hoistedServices)
    {
        var typeDeclaration = (TypeDeclarationSyntax)context.Node;
        var executionMethods = typeDeclaration.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(member => member.Body is not null && IsHostedExecutionMethod(member, context.SemanticModel))
            .ToList();
        if (executionMethods.Count == 0)
        {
            return;
        }

        var fields = CollectFieldCandidates(typeDeclaration, context.SemanticModel, executionMethods, wellKnownTypes);

        foreach (var method in executionMethods)
        {
            var qualifyingLoops = FindLongRunningLoops(method.Body!, context.SemanticModel);
            foreach (var loop in qualifyingLoops)
            {
                // A long-running loop nested inside another long-running loop (channel drain inside
                // a cancellation loop) is analyzed against locals declared inside the enclosing
                // loop's body only: locals hoisted above the enclosing loop are that loop's report.
                var enclosing = qualifyingLoops
                    .Where(other => other != loop && GetLoopBody(other)?.Span.Contains(loop.Span) == true)
                    .OrderByDescending(other => other.SpanStart)
                    .FirstOrDefault();

                // The boundary may be any statement (a using statement wrapping the nested loop
                // declares a scope that spans the inner drain); the declaration walk understands
                // using-statement variables.
                var boundary = enclosing is null ? method.Body : GetLoopBody(enclosing);
                if (boundary is null)
                {
                    continue;
                }

                AnalyzeLoop(context, wellKnownTypes, method, boundary, loop, fields, hoistedScopes, hoistedServices);
            }
        }
    }

    /// <summary>
    /// Collects fields of the hosted type that provably hold a hoisted scope or resolved
    /// service: every assignment (including the field initializer) is an invocation of the
    /// expected shape, and every assignment site lives in a field initializer, a constructor,
    /// or a hosted execution method. An assignment anywhere else (a helper method, a property)
    /// disqualifies the field — the helper may run per iteration.
    /// </summary>
    private static FieldCandidates CollectFieldCandidates(
        TypeDeclarationSyntax typeDeclaration,
        SemanticModel semanticModel,
        List<MethodDeclarationSyntax> executionMethods,
        WellKnownTypes wellKnownTypes)
    {
        if (semanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol typeSymbol)
        {
            return FieldCandidates.Empty;
        }

        // The type may be partial with the field, its assignments, or other execution methods
        // in different parts (and different files); collect across every declaration.
        var parts = new List<(TypeDeclarationSyntax Part, SemanticModel Model)>();
        foreach (var reference in typeSymbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not TypeDeclarationSyntax part)
            {
                continue;
            }

            // Foreign partial declarations need their own tree's model (same pattern as
            // DI001/DI019 cross-tree lookups).
            #pragma warning disable RS1030
            var model = part.SyntaxTree == semanticModel.SyntaxTree
                ? semanticModel
                : semanticModel.Compilation.GetSemanticModel(part.SyntaxTree);
            #pragma warning restore RS1030
            parts.Add((part, model));
        }

        var allExecutionMethods = new HashSet<MethodDeclarationSyntax>(executionMethods);
        foreach (var (part, model) in parts)
        {
            foreach (var member in part.Members.OfType<MethodDeclarationSyntax>())
            {
                if (member.Body is not null && IsHostedExecutionMethod(member, model))
                {
                    allExecutionMethods.Add(member);
                }
            }
        }

        var assignmentSites = new Dictionary<IFieldSymbol, List<FieldSite>>(SymbolEqualityComparer.Default);

        foreach (var (part, model) in parts)
        {
            foreach (var declarator in part.Members
                         .OfType<FieldDeclarationSyntax>()
                         .SelectMany(field => field.Declaration.Variables))
            {
                if (declarator.Initializer?.Value is not { } initializer ||
                    model.GetDeclaredSymbol(declarator) is not IFieldSymbol field)
                {
                    continue;
                }

                AddSite(field, declarator, UnwrapInvocation(initializer), model, lazyCoalesce: false);
            }

            foreach (var assignment in part.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (GetAssignedFieldSymbol(assignment.Left, model) is not { } field ||
                    !SymbolEqualityComparer.Default.Equals(field.ContainingType, typeSymbol))
                {
                    continue;
                }

                // A clear (`_scope = null` / `= null!` / `= default` / `= default(T)`) never
                // creates an instance: it is neutral for qualification (teardown in StopAsync
                // must not disqualify the field) but competes for dominance — a clear that runs
                // between the creation and the loop means the created instance never feeds it.
                if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                    IsNullOrDefaultClear(assignment.Right))
                {
                    AddSite(field, assignment, invocation: null, model, lazyCoalesce: false, isClear: true);
                    continue;
                }

                // ??= is lazy hoisting: the field is created once and reused, same as =.
                AddSite(field, assignment,
                    assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                    assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression)
                        ? UnwrapInvocation(assignment.Right)
                        : null,
                    model,
                    assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression));
            }
        }

        var scopeFields = new Dictionary<ISymbol, FieldEntry>(SymbolEqualityComparer.Default);
        var serviceFields = new Dictionary<ISymbol, FieldEntry>(SymbolEqualityComparer.Default);

        foreach (var pair in assignmentSites)
        {
            var sites = pair.Value;
            var creations = sites.Where(site => !site.IsClear).ToList();
            if (creations.Count == 0 ||
                creations.Any(site => site.Invocation is null) ||
                !creations.All(site => IsHoistedAssignmentSite(site.Site, allExecutionMethods)))
            {
                continue;
            }

            if (creations.All(site => IsScopeCreation(site.Invocation!, site.Model, wellKnownTypes)))
            {
                scopeFields[pair.Key] = new FieldEntry(sites, null, null);
                continue;
            }

            ITypeSymbol? serviceType = null;
            var allResolutions = true;
            foreach (var site in creations)
            {
                if (!TryGetResolution(site.Invocation!, site.Model, out var resolved) ||
                    (serviceType is not null && !SymbolEqualityComparer.Default.Equals(serviceType, resolved)))
                {
                    allResolutions = false;
                    break;
                }

                serviceType = resolved;
            }

            if (allResolutions && serviceType is not null)
            {
                serviceFields[pair.Key] = new FieldEntry(sites, serviceType, null);
            }
        }

        // Attribute service fields resolved from a hoisted scope field once all scope fields
        // are known; each resolution site's own semantic model resolves its identifiers.
        foreach (var key in serviceFields.Keys.ToList())
        {
            var entry = serviceFields[key];
            var sourceScopes = entry.Sites
                .Select(site => site.Invocation is null
                    ? null
                    : FindReferencedScopeFieldSymbol(site.Invocation, site.Model, scopeFields))
                .ToList();
            serviceFields[key] = entry.WithSiteSourceScopes(sourceScopes);
        }

        return new FieldCandidates(scopeFields, serviceFields);

        void AddSite(IFieldSymbol field, SyntaxNode site, InvocationExpressionSyntax? invocation, SemanticModel model, bool lazyCoalesce, bool isClear = false)
        {
            if (!assignmentSites.TryGetValue(field, out var list))
            {
                list = new List<FieldSite>();
                assignmentSites[field] = list;
            }

            list.Add(new FieldSite(site, invocation, model, lazyCoalesce, GetLifecycleRank(site), isClear));
        }
    }

    private static ISymbol? FindReferencedScopeFieldSymbol(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        Dictionary<ISymbol, FieldEntry> scopeFields)
    {
        foreach (var identifier in invocation.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (semanticModel.GetSymbolInfo(identifier).Symbol is { } symbol &&
                symbol is ILocalSymbol or IFieldSymbol &&
                scopeFields.ContainsKey(symbol))
            {
                return symbol;
            }
        }

        return null;
    }

    /// <summary>
    /// Picks the assignment site that actually feeds the loop, or null when none dominates it.
    /// A site dominates when it provably runs before the loop's iterations consume the field:
    /// textually before the loop in the same method, an earlier-or-equal lifecycle stage in
    /// another method (constructor, field initializer, StartingAsync before a StartAsync loop),
    /// or a lazy `??=` inside the loop. Among dominating sites the closest creation wins: the
    /// latest same-method site before the loop, then an in-loop `??=`, then the latest-stage
    /// cross-method site. A creation that only runs after the loop (later in the method, or a
    /// later lifecycle stage like StartedAsync relative to a StartAsync loop) never dominates.
    /// </summary>
    private static int? TryGetDominatingAssignment(
        List<FieldSite> sites,
        MethodDeclarationSyntax method,
        StatementSyntax loop)
    {
        var loopRank = GetLifecycleRank(loop);
        int? best = null;
        var bestIsClear = false;
        var bestScore = (-1, -1);

        for (var index = 0; index < sites.Count; index++)
        {
            var site = sites[index];
            (int Tier, int Position)? score = null;

            if (site.Site.SyntaxTree != method.SyntaxTree || !method.Span.Contains(site.Site.Span))
            {
                if (site.LifecycleRank <= loopRank)
                {
                    score = (0, site.LifecycleRank);
                }
            }
            else if (site.Site.Span.End <= loop.SpanStart)
            {
                score = (2, site.Site.Span.End);
            }
            else if (loop.Span.Contains(site.Site.Span) && site.LazyCoalesce && !site.IsClear)
            {
                score = (1, site.Site.Span.Start);
            }

            if (score is not { } value)
            {
                continue;
            }

            // A clear competes for dominance: if the closest pre-loop write is a clear, the
            // created instance never reaches the loop. On ordering ties (same lifecycle stage
            // in different methods) the clear wins to stay conservative.
            var comparison = (value.Tier, value.Position).CompareTo(bestScore);
            if (comparison > 0 || (comparison == 0 && site.IsClear))
            {
                bestScore = (value.Tier, value.Position);
                best = index;
                bestIsClear = site.IsClear;
            }
        }

        return bestIsClear ? null : best;
    }

    /// <summary>
    /// Host lifecycle ordering for a node's containing member: constructors and field
    /// initializers run first, then StartingAsync, StartAsync, StartedAsync. ExecuteAsync is
    /// started during StartAsync but runs concurrently for the host lifetime, so every start
    /// stage can feed its loop. Unknown members rank last (cannot prove ordering).
    /// </summary>
    private static int GetLifecycleRank(SyntaxNode node)
    {
        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case VariableDeclaratorSyntax when current.Parent?.Parent is FieldDeclarationSyntax:
                case ConstructorDeclarationSyntax:
                    return 0;

                case MethodDeclarationSyntax method:
                    return method.Identifier.Text switch
                    {
                        "StartingAsync" => 1,
                        "StartAsync" => 2,
                        "StartedAsync" => 3,
                        "ExecuteAsync" => 4,
                        _ => int.MaxValue
                    };
            }
        }

        return int.MaxValue;
    }

    private readonly struct FieldSite
    {
        public FieldSite(SyntaxNode site, InvocationExpressionSyntax? invocation, SemanticModel model, bool lazyCoalesce, int lifecycleRank, bool isClear)
        {
            Site = site;
            Invocation = invocation;
            Model = model;
            LazyCoalesce = lazyCoalesce;
            LifecycleRank = lifecycleRank;
            IsClear = isClear;
        }

        public SyntaxNode Site { get; }

        public InvocationExpressionSyntax? Invocation { get; }

        public SemanticModel Model { get; }

        public bool LazyCoalesce { get; }

        public int LifecycleRank { get; }

        public bool IsClear { get; }
    }

    private static bool IsNullOrDefaultClear(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal =>
                literal.IsKind(SyntaxKind.NullLiteralExpression) ||
                literal.IsKind(SyntaxKind.DefaultLiteralExpression),
            DefaultExpressionSyntax => true,
            PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppressed =>
                IsNullOrDefaultClear(suppressed.Operand),
            ParenthesizedExpressionSyntax parenthesized => IsNullOrDefaultClear(parenthesized.Expression),
            CastExpressionSyntax cast => IsNullOrDefaultClear(cast.Expression),
            _ => false
        };
    }

    private static bool IsHoistedAssignmentSite(SyntaxNode site, HashSet<MethodDeclarationSyntax> executionMethods)
    {
        for (SyntaxNode? current = site; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case VariableDeclaratorSyntax when site == current:
                case ConstructorDeclarationSyntax:
                    return true;

                case MethodDeclarationSyntax method:
                    return executionMethods.Contains(method);

                case AnonymousFunctionExpressionSyntax:
                case LocalFunctionStatementSyntax:
                case AccessorDeclarationSyntax:
                case ArrowExpressionClauseSyntax when current.Parent is PropertyDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    private static IFieldSymbol? GetAssignedFieldSymbol(ExpressionSyntax left, SemanticModel semanticModel)
    {
        var target = left switch
        {
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } memberAccess => (ExpressionSyntax)memberAccess.Name,
            _ => left
        };

        return target is IdentifierNameSyntax
            ? semanticModel.GetSymbolInfo(target).Symbol as IFieldSymbol
            : null;
    }

    private static void AnalyzeLoop(
        SyntaxNodeAnalysisContext context,
        WellKnownTypes wellKnownTypes,
        MethodDeclarationSyntax method,
        SyntaxNode boundary,
        StatementSyntax loop,
        FieldCandidates fields,
        ConcurrentBag<HoistedScopeCandidate> hoistedScopes,
        ConcurrentBag<HoistedServiceCandidate> hoistedServices)
    {
        var semanticModel = context.SemanticModel;
        var loopBody = GetLoopBody(loop);
        if (loopBody is null)
        {
            return;
        }

        // Scope symbols hoisted above the loop: locals declared before it plus provably-hoisted
        // fields. All visible scope symbols participate in service-to-scope attribution, but
        // only locals declared within the boundary (and fields) are this loop's report
        // candidates: a scope local hoisted above the enclosing long-running loop is that
        // loop's report, and field candidates reported by multiple loops merge by location.
        var scopeLocals = new Dictionary<ISymbol, InvocationExpressionSyntax>(SymbolEqualityComparer.Default);
        var candidateScopes = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        // Symbols holding a service resolved before the loop: symbol -> (service type, the
        // resolution invocation, the hoisted scope symbol it was resolved from, if any).
        var serviceLocals = new Dictionary<ISymbol, (ITypeSymbol ServiceType, InvocationExpressionSyntax Invocation, ISymbol? SourceScope)>(SymbolEqualityComparer.Default);

        var methodBody = method.Body!;
        var declarations = new List<(ILocalSymbol Local, InvocationExpressionSyntax Invocation, bool WithinBoundary)>();
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

            declarations.Add((local, initializerInvocation,
                boundary == methodBody || boundary.Span.Contains(declarator.Span)));
        }

        // Two passes: the upward block walk yields inner-block declarators before outer ones,
        // so all scope symbols must be known before service resolutions are attributed to them.
        foreach (var (local, invocation, withinBoundary) in declarations)
        {
            if (IsScopeCreation(invocation, semanticModel, wellKnownTypes))
            {
                scopeLocals[local] = invocation;
                if (withinBoundary)
                {
                    candidateScopes.Add(local);
                }
            }
        }

        foreach (var pair in fields.ScopeFields)
        {
            if (TryGetDominatingAssignment(pair.Value.Sites, method, loop) is not { } index)
            {
                continue;
            }

            scopeLocals[pair.Key] = pair.Value.Sites[index].Invocation!;
            candidateScopes.Add(pair.Key);
        }

        foreach (var (local, invocation, withinBoundary) in declarations)
        {
            if (withinBoundary &&
                !scopeLocals.ContainsKey(local) &&
                TryGetResolution(invocation, semanticModel, out var serviceType))
            {
                var sourceScope = FindReferencedScopeLocal(invocation, semanticModel, scopeLocals);
                serviceLocals[local] = (serviceType, invocation, sourceScope);
            }
        }

        foreach (var pair in fields.ServiceFields)
        {
            if (TryGetDominatingAssignment(pair.Value.Sites, method, loop) is not { } index)
            {
                continue;
            }

            // Source-scope attribution was resolved at collection time with each resolution
            // site's own semantic model (sites may live in another partial declaration).
            serviceLocals[pair.Key] = (
                pair.Value.ServiceType!,
                pair.Value.Sites[index].Invocation!,
                pair.Value.SiteSourceScopes?[index]);
        }

        if (candidateScopes.Count == 0 && serviceLocals.Count == 0)
        {
            return;
        }

        var scopeUsage = new Dictionary<ISymbol, ScopeUsage>(SymbolEqualityComparer.Default);
        var serviceUsedInLoop = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var reassignedInLoop = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var identifier in loopBody.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (semanticModel.GetSymbolInfo(identifier).Symbol is not (ILocalSymbol or IFieldSymbol) ||
                semanticModel.GetSymbolInfo(identifier).Symbol is not { } local)
            {
                continue;
            }

            var isScopeLocal = candidateScopes.Contains(local);
            var isServiceLocal = serviceLocals.ContainsKey(local);
            if (!isScopeLocal && !isServiceLocal)
            {
                continue;
            }

            if (IsAssignmentTarget(identifier))
            {
                // A null/default clear (error branch, teardown inside the loop) does not give
                // the next iteration a fresh instance; only a real reassignment counts as
                // dispose-and-recreate.
                var assignedValue = GetEnclosingAssignmentValue(identifier);
                if (assignedValue is null || !IsNullOrDefaultClear(assignedValue))
                {
                    reassignedInLoop.Add(local);
                }

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
                if (!candidateScopes.Contains(sourceScope))
                {
                    // The service flows from a scope hoisted above the enclosing long-running
                    // loop; that loop's analysis owns the report on the scope itself.
                    continue;
                }

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

    private static ExpressionSyntax? GetEnclosingAssignmentValue(IdentifierNameSyntax identifier)
    {
        return identifier.Parent switch
        {
            AssignmentExpressionSyntax assignment when assignment.Left == identifier => assignment.Right,
            MemberAccessExpressionSyntax { Parent: AssignmentExpressionSyntax outer } memberAccess
                when memberAccess.Name == identifier && outer.Left == memberAccess => outer.Right,
            _ => null
        };
    }

    private static bool IsAssignmentTarget(IdentifierNameSyntax identifier)
    {
        // Only a simple `=` replaces the value (dispose-and-recreate); `??=` inside the loop is
        // lazy hoisting — the first iteration's instance is reused forever — and must not count
        // as a per-iteration reassignment.
        if (identifier.Parent is AssignmentExpressionSyntax assignment &&
            assignment.Left == identifier)
        {
            return assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);
        }

        // this._scope = ...
        return identifier.Parent is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } memberAccess &&
               memberAccess.Name == identifier &&
               memberAccess.Parent is AssignmentExpressionSyntax outerAssignment &&
               outerAssignment.Left == memberAccess &&
               outerAssignment.IsKind(SyntaxKind.SimpleAssignmentExpression);
    }

    private static ISymbol? FindReferencedScopeLocal(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        Dictionary<ISymbol, InvocationExpressionSyntax> scopeLocals)
    {
        foreach (var identifier in invocation.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (semanticModel.GetSymbolInfo(identifier).Symbol is { } symbol &&
                symbol is ILocalSymbol or IFieldSymbol &&
                scopeLocals.ContainsKey(symbol))
            {
                return symbol;
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
        SyntaxNode boundary,
        StatementSyntax loop)
    {
        // Walk from the loop up to the boundary block (the method body, or the enclosing
        // long-running loop's body for nested loops); in every enclosing block, locals declared
        // in statements that precede the loop's enclosing statement are in scope at the loop.
        SyntaxNode current = loop;
        while (current != boundary.Parent && current.Parent is not null)
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

            if (current.Parent == boundary)
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
            CommonForEachStatementSyntax forEachStatement => forEachStatement.Statement,
            _ => null
        };
    }

    private static List<StatementSyntax> FindLongRunningLoops(
        BlockSyntax methodBody,
        SemanticModel semanticModel)
    {
        return methodBody
            .DescendantNodes(node => node is not (LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax))
            .OfType<StatementSyntax>()
            .Where(statement => IsLongRunningLoop(statement, semanticModel))
            .ToList();
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

            // await foreach (var item in reader.ReadAllAsync(token)) — the canonical channel
            // consumer loop. Only ChannelReader<T> sources qualify: ReadAllAsync on other types
            // (repositories, paginated APIs) is a bounded enumeration.
            CommonForEachStatementSyntax forEachStatement =>
                forEachStatement.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword) &&
                UnwrapInvocation(forEachStatement.Expression) is { } sourceInvocation &&
                IsChannelReaderMethod(sourceInvocation, semanticModel, "ReadAllAsync"),

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

            // while (await reader.WaitToReadAsync(token)) — channel consumer loop.
            case AwaitExpressionSyntax awaitExpression
                when UnwrapInvocation(awaitExpression.Expression) is { } invocation &&
                     IsChannelReaderMethod(invocation, semanticModel, "WaitToReadAsync"):
                return true;

            default:
                return false;
        }
    }

    private static bool IsChannelReaderMethod(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string methodName)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol ||
            methodSymbol.Name != methodName)
        {
            return false;
        }

        for (var type = methodSymbol.ContainingType; type is not null; type = type.BaseType)
        {
            if (type.Name == "ChannelReader" &&
                type.ContainingNamespace?.ToDisplayString() == "System.Threading.Channels")
            {
                return true;
            }
        }

        return false;
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
                    if (member.Name != symbol.Name ||
                        containingType.FindImplementationForInterfaceMember(member) is not IMethodSymbol implementation)
                    {
                        continue;
                    }

                    // The interface mapping may point at a base virtual (BackgroundService.StartAsync)
                    // that this method overrides; walk the override chain to match.
                    for (IMethodSymbol? current = symbol; current is not null; current = current.OverriddenMethod)
                    {
                        if (SymbolEqualityComparer.Default.Equals(implementation, current))
                        {
                            return true;
                        }
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

    private sealed class FieldEntry
    {
        public FieldEntry(List<FieldSite> sites, ITypeSymbol? serviceType, List<ISymbol?>? siteSourceScopes)
        {
            Sites = sites;
            ServiceType = serviceType;
            SiteSourceScopes = siteSourceScopes;
        }

        public List<FieldSite> Sites { get; }

        public ITypeSymbol? ServiceType { get; }

        /// <summary>Per-site source-scope attribution, parallel to <see cref="Sites"/>.</summary>
        public List<ISymbol?>? SiteSourceScopes { get; }

        public FieldEntry WithSiteSourceScopes(List<ISymbol?> siteSourceScopes) =>
            new(Sites, ServiceType, siteSourceScopes);
    }

    private sealed class FieldCandidates
    {
        public static readonly FieldCandidates Empty = new(
            new Dictionary<ISymbol, FieldEntry>(SymbolEqualityComparer.Default),
            new Dictionary<ISymbol, FieldEntry>(SymbolEqualityComparer.Default));

        public FieldCandidates(
            Dictionary<ISymbol, FieldEntry> scopeFields,
            Dictionary<ISymbol, FieldEntry> serviceFields)
        {
            ScopeFields = scopeFields;
            ServiceFields = serviceFields;
        }

        public Dictionary<ISymbol, FieldEntry> ScopeFields { get; }

        public Dictionary<ISymbol, FieldEntry> ServiceFields { get; }
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
