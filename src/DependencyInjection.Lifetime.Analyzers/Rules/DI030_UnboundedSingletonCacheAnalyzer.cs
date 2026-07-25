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
/// DI030: a store that lives for the life of the process and grows without bound. Either a collection
/// owned by a singleton-registered service or held in a static member, written with request-derived
/// keys and never evicted; or an <c>IMemoryCache</c> entry with neither an expiration nor a size in a
/// cache with no configured size limit.
/// <para>
/// Reported at Info: a key space that is unbounded in the type system may be bounded in production,
/// so the claim is architectural rather than a proven defect.
/// </para>
/// <para>
/// Deliberately narrower than the shapes it neighbours. A scope-resolved value stored into a
/// collection is DI002, a scoped service cached by a singleton is DI003, and a static member whose
/// value is a provider is DI006 — each is excluded here rather than reported twice. The message never
/// mentions thread safety, keeping this claim separable from the parked concurrent-mutation rule.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI030_UnboundedSingletonCacheAnalyzer : DiagnosticAnalyzer
{
    private const string AllowedCacheTypesOption = "dotnet_code_quality.DI030.allowed_cache_types";
    private const string DetectMemoryCacheBoundsOption =
        "dotnet_code_quality.DI030.detect_memory_cache_bounds";

    /// <summary>Collection methods that grow the store.</summary>
    private static readonly ImmutableHashSet<string> GrowthMethodNames = ImmutableHashSet.Create(
        "Add",
        "TryAdd",
        "GetOrAdd",
        "AddOrUpdate",
        "Enqueue",
        "Push",
        "Insert",
        "AddRange",
        "UnionWith"
    );

    /// <summary>
    /// Reads that observe the store without changing its size and without revealing a cap. Any
    /// reference the analyzer cannot place in this set or in <see cref="GrowthMethodNames"/> makes the
    /// candidate silent, which is what turns "never evicted" into a sound claim rather than a guess.
    /// </summary>
    private static readonly ImmutableHashSet<string> PureReadNames = ImmutableHashSet.Create(
        "TryGetValue",
        "ContainsKey",
        "Contains",
        "TryPeek",
        "Peek",
        "Keys",
        "Values",
        "GetValueOrDefault"
    );

    /// <summary>Value types that make a dictionary a coordination registry rather than a cache.</summary>
    private static readonly ImmutableHashSet<string> LockLikeValueTypeNames =
        ImmutableHashSet.Create(
            "SemaphoreSlim",
            "Object",
            "Lazy",
            "Task",
            "Mutex",
            "ManualResetEventSlim",
            "AsyncLock"
        );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.UnboundedSingletonCache,
            DiagnosticDescriptors.UnboundedMemoryCacheEntry
        );

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

            var hasCollections =
                wellKnownTypes.ConcurrentDictionaryOfTKeyTValue is not null
                || wellKnownTypes.DictionaryOfTKeyTValue is not null
                || wellKnownTypes.ListOfT is not null;
            if (!hasCollections && wellKnownTypes.IMemoryCache is null)
            {
                return;
            }

            var registrationCollector = RegistrationCollector.Create(
                compilationContext.Compilation
            );
            if (registrationCollector is null)
            {
                return;
            }

            var optionsProvider = compilationContext.Options.AnalyzerConfigOptionsProvider;
            var candidateFields = new ConcurrentBag<CandidateField>();
            var cacheWrites = new ConcurrentBag<CacheWrite>();
            var cacheHasGlobalSizeLimit = new StrongBox(false);
            var semanticModelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
                    registrationCollector.AnalyzeInvocation(
                        invocation,
                        syntaxContext.SemanticModel
                    );
                    semanticModelsByTree.TryAdd(
                        syntaxContext.SemanticModel.SyntaxTree,
                        syntaxContext.SemanticModel
                    );

                    CollectCacheWrite(
                        syntaxContext,
                        invocation,
                        wellKnownTypes,
                        optionsProvider,
                        cacheWrites,
                        cacheHasGlobalSizeLimit
                    );
                },
                SyntaxKind.InvocationExpression
            );

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    semanticModelsByTree.TryAdd(
                        syntaxContext.SemanticModel.SyntaxTree,
                        syntaxContext.SemanticModel
                    );
                    CollectCandidateFields(
                        syntaxContext,
                        wellKnownTypes,
                        optionsProvider,
                        candidateFields
                    );
                },
                SyntaxKind.FieldDeclaration
            );

            // A global SizeLimit can be configured through a MemoryCacheOptions initializer rather
            // than through AddMemoryCache's lambda, so both shapes are observed.
            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                    DetectGlobalSizeLimit(syntaxContext, wellKnownTypes, cacheHasGlobalSizeLimit),
                SyntaxKind.SimpleAssignmentExpression
            );

            compilationContext.RegisterCompilationEndAction(endContext =>
            {
                ReportUnboundedCollections(
                    endContext,
                    registrationCollector,
                    wellKnownTypes,
                    candidateFields,
                    semanticModelsByTree
                );

                if (!cacheHasGlobalSizeLimit.Value)
                {
                    ReportUnboundedCacheEntries(endContext, cacheWrites);
                }
            });
        });
    }

    private sealed class StrongBox
    {
        private volatile bool _value;

        public StrongBox(bool value) => _value = value;

        public bool Value
        {
            get => _value;
            set => _value = value;
        }
    }

    private sealed class CandidateField
    {
        public CandidateField(
            IFieldSymbol field,
            INamedTypeSymbol containingType,
            string elementTypeName
        )
        {
            Field = field;
            ContainingType = containingType;
            ElementTypeName = elementTypeName;
        }

        public IFieldSymbol Field { get; }
        public INamedTypeSymbol ContainingType { get; }
        public string ElementTypeName { get; }
    }

    private sealed class CacheWrite
    {
        public CacheWrite(string keyDescription, Location location)
        {
            KeyDescription = keyDescription;
            Location = location;
        }

        public string KeyDescription { get; }
        public Location Location { get; }
    }

    // ----------------------------------------------------------------
    // Tier A leg A0 -- candidate fields
    // ----------------------------------------------------------------

    private static void CollectCandidateFields(
        SyntaxNodeAnalysisContext context,
        WellKnownTypes wellKnownTypes,
        AnalyzerConfigOptionsProvider optionsProvider,
        ConcurrentBag<CandidateField> candidates
    )
    {
        var declaration = (FieldDeclarationSyntax)context.Node;

        foreach (var declarator in declaration.Declaration.Variables)
        {
            if (
                context.SemanticModel.GetDeclaredSymbol(declarator, context.CancellationToken)
                is not IFieldSymbol field
            )
            {
                continue;
            }

            // Private is load-bearing, not stylistic: it is what makes a scan of this type's
            // declarations a complete proof that nothing else evicts from the store.
            if (
                field.DeclaredAccessibility != Accessibility.Private
                || field.IsConst
                || field.IsImplicitlyDeclared
            )
            {
                continue;
            }

            if (
                !wellKnownTypes.IsUnboundedGrowthCollection(field.Type, out var elementType)
                || elementType is null
            )
            {
                continue;
            }

            // E1: DI006 owns static members whose value is a provider. Excluded with DI006's own
            // predicate so the two rules provably cannot both fire.
            if (wellKnownTypes.IsServiceProviderOrFactoryOrKeyed(elementType))
            {
                continue;
            }

            // E4: a coordination registry keyed by identity is not a cache, and such registries
            // almost always remove on release in code this analyzer cannot see.
            if (IsLockLikeValueType(elementType))
            {
                continue;
            }

            if (IsAllowedCacheType(elementType, optionsProvider, declaration.SyntaxTree))
            {
                continue;
            }

            candidates.Add(new CandidateField(field, field.ContainingType, elementType.Name));
        }
    }

    private static bool IsLockLikeValueType(ITypeSymbol elementType)
    {
        if (LockLikeValueTypeNames.Contains(elementType.Name))
        {
            return true;
        }

        var name = elementType.Name;
        return name.IndexOf("Lock", StringComparison.Ordinal) >= 0
            || name.IndexOf("Mutex", StringComparison.Ordinal) >= 0
            || name.IndexOf("Semaphore", StringComparison.Ordinal) >= 0;
    }

    // ----------------------------------------------------------------
    // Tier A legs A1-A4 -- ownership, writes, and the completeness proof
    // ----------------------------------------------------------------

    private static void ReportUnboundedCollections(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        ConcurrentBag<CandidateField> candidates,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree
    )
    {
        if (candidates.IsEmpty)
        {
            return;
        }

        var registrations = registrationCollector.AllRegistrations.ToList();
        var singletonOwners = BuildSingletonOwnerSet(registrations);

        var ordered = candidates
            .OrderBy(
                c => c.Field.Locations.FirstOrDefault()?.SourceTree?.FilePath ?? string.Empty,
                StringComparer.Ordinal
            )
            .ThenBy(c => c.Field.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0);

        foreach (var candidate in ordered)
        {
            // A1: static members live for the process; otherwise the owner must be a proven singleton.
            var ownerDescription =
                candidate.Field.IsStatic ? "the life of the process"
                : singletonOwners.Contains(candidate.ContainingType)
                    ? $"as long as the singleton '{candidate.ContainingType.Name}'"
                : null;
            if (ownerDescription is null)
            {
                continue;
            }

            if (
                !TryFindUnboundedWrite(
                    candidate,
                    wellKnownTypes,
                    registrations,
                    context.Compilation,
                    semanticModelsByTree,
                    context.CancellationToken,
                    out var writeLocation,
                    out var keyDescription
                )
            )
            {
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.UnboundedSingletonCache,
                    writeLocation,
                    candidate.Field.Name,
                    keyDescription,
                    candidate.ContainingType.Name,
                    ownerDescription
                )
            );
        }
    }

    /// <summary>
    /// Types activated only under a singleton registration. A type also registered scoped or transient
    /// is excluded: taking the most conservative reading means a store it owns is not process-lived.
    /// </summary>
    private static HashSet<INamedTypeSymbol> BuildSingletonOwnerSet(
        List<ServiceRegistration> registrations
    )
    {
        var singletons = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var shorterLived = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var registration in registrations)
        {
            var implementation = registration.ImplementationType;
            if (implementation is null)
            {
                continue;
            }

            for (
                var current = implementation;
                current is not null && current.SpecialType != SpecialType.System_Object;
                current = current.BaseType
            )
            {
                // A keyed registration neither proves nor disproves an unkeyed singleton resolution,
                // so it is ignored entirely. Treating it as shorter-lived would let an additional keyed
                // singleton erase a proven unkeyed one, whose cache still lives for the process.
                if (registration.IsKeyed)
                {
                    continue;
                }

                if (registration.Lifetime == ServiceLifetime.Singleton)
                {
                    singletons.Add(current);
                }
                else
                {
                    shorterLived.Add(current);
                }
            }
        }

        singletons.ExceptWith(shorterLived);
        return singletons;
    }

    /// <summary>
    /// Runs legs A2 to A4 over the candidate's declaring type. Because the field is private, every
    /// reference to it lives inside these declarations, so this scan is a complete proof: it returns
    /// a write only when no reference anywhere could remove from, cap, drain, or hand off the store.
    /// </summary>
    private static bool TryFindUnboundedWrite(
        CandidateField candidate,
        WellKnownTypes wellKnownTypes,
        List<ServiceRegistration> registrations,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        System.Threading.CancellationToken cancellationToken,
        out Location writeLocation,
        out string keyDescription
    )
    {
        writeLocation = Location.None;
        keyDescription = string.Empty;

        var field = candidate.Field;
        ExpressionSyntax? firstWriteKey = null;
        SyntaxNode? firstWriteNode = null;

        foreach (var typeReference in field.ContainingType.DeclaringSyntaxReferences)
        {
            if (
                typeReference.GetSyntax(cancellationToken)
                is not TypeDeclarationSyntax typeDeclaration
            )
            {
                return false;
            }

            // A partial declaration may live in a tree this analyzer never visited, so the model has to
            // be fetched rather than looked up. Routed through the shared memoizing helper, which owns
            // the RS1030 suppression for exactly this compilation-end case.
            var model = EventReceiverClassification.GetSemanticModel(
                typeDeclaration.SyntaxTree,
                compilation,
                semanticModelsByTree
            );

            foreach (
                var identifier in typeDeclaration.DescendantNodes().OfType<IdentifierNameSyntax>()
            )
            {
                if (identifier.Identifier.ValueText != field.Name)
                {
                    continue;
                }

                if (
                    !SymbolEqualityComparer.Default.Equals(
                        model.GetSymbolInfo(identifier, cancellationToken).Symbol,
                        field
                    )
                )
                {
                    continue;
                }

                var access = NormalizeFieldAccess(identifier);

                // The declarator's own name node is not a use.
                if (access.Parent is VariableDeclaratorSyntax)
                {
                    continue;
                }

                // A reference inside a lambda or local function may be a background eviction path.
                if (IsInsideNestedFunction(access, typeDeclaration))
                {
                    return false;
                }

                var classification = ClassifyFieldReference(
                    access,
                    model,
                    cancellationToken,
                    out var keyExpression,
                    out var reportNode
                );

                switch (classification)
                {
                    case FieldReferenceKind.PureRead:
                        continue;

                    case FieldReferenceKind.Growth:
                        if (
                            firstWriteNode is null
                            && IsPerInvocationWrite(access)
                            && !IsOneTimeInitialization(
                                access,
                                candidate.ContainingType,
                                model,
                                cancellationToken
                            )
                            && keyExpression is not null
                            && KeyIsUnbounded(
                                keyExpression,
                                model,
                                wellKnownTypes,
                                registrations,
                                cancellationToken,
                                out var describedKey
                            )
                        )
                        {
                            firstWriteKey = keyExpression;
                            firstWriteNode = reportNode;
                            keyDescription = describedKey;
                        }

                        continue;

                    default:
                        // Eviction, a size check, an escape, a drain loop, LINQ, reassignment, or any
                        // shape not modelled: the store may well be bounded, so stay silent.
                        return false;
                }
            }
        }

        if (firstWriteNode is null || firstWriteKey is null)
        {
            return false;
        }

        writeLocation = firstWriteNode.GetLocation();
        return true;
    }

    private enum FieldReferenceKind
    {
        Disqualifying,
        PureRead,
        Growth,
    }

    /// <summary>
    /// Resolves <c>this._cache</c> or <c>Holder._cache</c> to the whole member access, so the caller
    /// inspects what is done to the field rather than the qualification in front of it.
    /// </summary>
    private static ExpressionSyntax NormalizeFieldAccess(IdentifierNameSyntax identifier)
    {
        if (
            identifier.Parent is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name == identifier
        )
        {
            return memberAccess;
        }

        return identifier;
    }

    private static FieldReferenceKind ClassifyFieldReference(
        ExpressionSyntax access,
        SemanticModel model,
        System.Threading.CancellationToken cancellationToken,
        out ExpressionSyntax? keyExpression,
        out SyntaxNode? reportNode
    )
    {
        keyExpression = null;
        reportNode = null;

        switch (access.Parent)
        {
            case MemberAccessExpressionSyntax memberAccess when memberAccess.Expression == access:
            {
                var memberName = memberAccess.Name.Identifier.ValueText;

                // A Count or Length read is how a hand-rolled cap is written, so it proves the
                // store may be bounded. Silencing metric reads with it is an accepted trade.
                if (memberName is "Count" or "Length")
                {
                    return FieldReferenceKind.Disqualifying;
                }

                if (PureReadNames.Contains(memberName))
                {
                    return FieldReferenceKind.PureRead;
                }

                if (
                    GrowthMethodNames.Contains(memberName)
                    && memberAccess.Parent is InvocationExpressionSyntax invocation
                )
                {
                    keyExpression = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                    if (keyExpression is null)
                    {
                        return FieldReferenceKind.Disqualifying;
                    }

                    // The whole call rather than just the member access: the diagnostic should span
                    // the key that makes the growth unbounded.
                    reportNode = invocation;
                    return FieldReferenceKind.Growth;
                }

                return FieldReferenceKind.Disqualifying;
            }

            case ElementAccessExpressionSyntax elementAccess
                when elementAccess.Expression == access:
            {
                var isWrite =
                    elementAccess.Parent is AssignmentExpressionSyntax assignment
                    && assignment.Left == elementAccess;
                if (!isWrite)
                {
                    return FieldReferenceKind.PureRead;
                }

                keyExpression = elementAccess.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                if (keyExpression is null)
                {
                    return FieldReferenceKind.Disqualifying;
                }

                reportNode = elementAccess.Parent;
                return FieldReferenceKind.Growth;
            }

            default:
                _ = model;
                _ = cancellationToken;
                return FieldReferenceKind.Disqualifying;
        }
    }

    private static bool IsInsideNestedFunction(
        SyntaxNode node,
        TypeDeclarationSyntax typeDeclaration
    )
    {
        for (
            var current = node.Parent;
            current is not null && current != typeDeclaration;
            current = current.Parent
        )
        {
            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Leg A2's site half: a write reached only through a constructor or an initializer runs once per
    /// activation, which for a singleton owner is once per process.
    /// </summary>
    private static bool IsPerInvocationWrite(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case ConstructorDeclarationSyntax:
                case FieldDeclarationSyntax:
                case PropertyDeclarationSyntax:
                    return false;
                case MethodDeclarationSyntax:
                case AccessorDeclarationSyntax:
                    return true;
                case TypeDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Leg A4: writes that populate the store once from a bounded source, or behind a one-shot guard,
    /// are not unbounded growth however the key is shaped.
    /// </summary>
    private static bool IsOneTimeInitialization(
        SyntaxNode node,
        INamedTypeSymbol containingType,
        SemanticModel model,
        System.Threading.CancellationToken cancellationToken
    )
    {
        var body = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (body is null)
        {
            return false;
        }

        foreach (var descendant in body.DescendantNodes())
        {
            switch (descendant)
            {
                // A bounded enumeration source: the store cannot outgrow it.
                case MemberAccessExpressionSyntax memberAccess
                    when memberAccess.Name.Identifier.ValueText
                        is "GetValues"
                            or "GetTypes"
                            or "GetExportedTypes"
                            or "GetAssemblies":
                    return true;

                // A one-shot flag guard: `if (_initialized) return;`
                case IfStatementSyntax ifStatement
                    when ifStatement.Span.End <= node.SpanStart
                        && GuardReturnsEarly(ifStatement)
                        && GuardTestsInstanceFlag(
                            ifStatement.Condition,
                            containingType,
                            model,
                            cancellationToken
                        ):
                    return true;
            }
        }

        return false;
    }

    private static bool GuardReturnsEarly(IfStatementSyntax ifStatement)
    {
        var statement = ifStatement.Statement is BlockSyntax block
            ? block.Statements.FirstOrDefault()
            : ifStatement.Statement;
        return statement is ReturnStatementSyntax;
    }

    /// <summary>
    /// A one-shot guard tests state carried by the instance — a flag field or property — so the write
    /// below it runs once. An early return over anything else is an ordinary argument or state check
    /// (<c>if (key is null) return;</c>) and says nothing about how often the write happens, so
    /// treating it as one-time initialization would silence real leaks. The condition must therefore
    /// reference a field or property of the declaring type and must not depend on a parameter.
    /// </summary>
    private static bool GuardTestsInstanceFlag(
        ExpressionSyntax condition,
        INamedTypeSymbol containingType,
        SemanticModel model,
        System.Threading.CancellationToken cancellationToken
    )
    {
        var flags = new List<ISymbol>();

        foreach (
            var identifier in condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
        )
        {
            var symbol = model.GetSymbolInfo(identifier, cancellationToken).Symbol;

            if (symbol is IParameterSymbol)
            {
                return false;
            }

            if (
                symbol is IFieldSymbol or IPropertySymbol
                && SymbolEqualityComparer.Default.Equals(symbol.ContainingType, containingType)
            )
            {
                flags.Add(symbol);
            }
        }

        if (flags.Count == 0)
        {
            return false;
        }

        // Referencing instance state is not enough. `if (_disabled) return;` still lets the write run
        // on every call while the flag is false, so suppression requires a provable one-time
        // transition: the guarded flag must itself be assigned somewhere in the same method.
        var body = condition.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (body is null)
        {
            return false;
        }

        foreach (var assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            var target = model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
            if (
                target is null
                || !flags.Any(flag => SymbolEqualityComparer.Default.Equals(flag, target))
            )
            {
                continue;
            }

            // The flag has to latch. `_initialized = false` after the write leaves the guard open, so
            // the write still runs on every call.
            if (assignment.Right.IsKind(SyntaxKind.FalseLiteralExpression))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Leg A2's key half. All three sub-legs must hold: the key must not be a compile-time constant,
    /// its type must not come from a bounded space, and it must be derived from caller input.
    /// </summary>
    private static bool KeyIsUnbounded(
        ExpressionSyntax keyExpression,
        SemanticModel model,
        WellKnownTypes wellKnownTypes,
        List<ServiceRegistration> registrations,
        System.Threading.CancellationToken cancellationToken,
        out string keyDescription
    )
    {
        keyDescription = string.Empty;

        // Sub-leg 1: a constant key addresses one slot. The cheapest and most valuable guard.
        if (SyntaxValueHelpers.TryExtractConstantValue(keyExpression, model, out _))
        {
            return false;
        }

        var keyType = model.GetTypeInfo(keyExpression, cancellationToken).Type;
        if (keyType is null)
        {
            return false;
        }

        // Sub-leg 2: a bounded key space cannot grow the store without bound.
        var unwrapped =
            keyType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && keyType is INamedTypeSymbol { TypeArguments.Length: 1 } nullable
                ? nullable.TypeArguments[0]
                : keyType;

        if (
            unwrapped.TypeKind == TypeKind.Enum
            || unwrapped.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char
            || unwrapped.Name == "Type"
        )
        {
            return false;
        }

        // E3: a value whose type is registered shorter-lived than singleton is DI002/DI003 territory.
        if (ValueTypeIsShorterLivedService(keyExpression, model, registrations, cancellationToken))
        {
            return false;
        }

        _ = wellKnownTypes;

        // Sub-leg 3: the key must trace back to caller input rather than to fixed state.
        return TryDescribeUnboundedKeySource(
            keyExpression,
            model,
            cancellationToken,
            out keyDescription
        );
    }

    private static bool ValueTypeIsShorterLivedService(
        ExpressionSyntax expression,
        SemanticModel model,
        List<ServiceRegistration> registrations,
        System.Threading.CancellationToken cancellationToken
    )
    {
        // E2: a scope-resolved value is DI002's finding, whatever the key looks like.
        var assignment = expression.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
        var invocation = expression.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        var searchRoot = (SyntaxNode?)assignment ?? invocation;
        if (searchRoot is null)
        {
            return false;
        }

        foreach (
            var candidate in searchRoot.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
        )
        {
            var name = candidate.Name.Identifier.ValueText;
            if (
                name
                is "GetService"
                    or "GetRequiredService"
                    or "GetKeyedService"
                    or "GetRequiredKeyedService"
            )
            {
                return true;
            }
        }

        _ = registrations;
        _ = model;
        _ = cancellationToken;
        return false;
    }

    private static bool TryDescribeUnboundedKeySource(
        ExpressionSyntax keyExpression,
        SemanticModel model,
        System.Threading.CancellationToken cancellationToken,
        out string keyDescription
    )
    {
        keyDescription = string.Empty;

        foreach (var node in keyExpression.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case IdentifierNameSyntax identifier:
                {
                    var symbol = model.GetSymbolInfo(identifier, cancellationToken).Symbol;
                    if (symbol is IParameterSymbol { IsThis: false } parameter)
                    {
                        keyDescription = $"the '{parameter.Name}' parameter";
                        return true;
                    }

                    continue;
                }

                // Guid.NewGuid() and DateTime.UtcNow have no parameter root but are provably distinct
                // on every call, so they grow the store exactly like caller input does.
                case MemberAccessExpressionSyntax memberAccess:
                {
                    var name = memberAccess.Name.Identifier.ValueText;
                    if (name is "NewGuid" or "UtcNow" or "Now")
                    {
                        keyDescription = $"'{memberAccess}'";
                        return true;
                    }

                    continue;
                }
            }
        }

        return false;
    }

    // ----------------------------------------------------------------
    // Tier B -- IMemoryCache
    // ----------------------------------------------------------------

    private static void DetectGlobalSizeLimit(
        SyntaxNodeAnalysisContext context,
        WellKnownTypes wellKnownTypes,
        StrongBox cacheHasGlobalSizeLimit
    )
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        // Only a limit configured on the container-provided cache is applied, because that is the cache
        // Tier B's writes actually go through. A standalone `new MemoryCache(options)` bounds only
        // itself, and letting it suppress every write in the compilation would hide unrelated unbounded
        // caches. Per-receiver identity would be more precise still; until then this errs toward
        // reporting rather than toward silence.
        if (
            assignment.FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { } enclosingCall
            || !IsCacheConfigurationCall(enclosingCall, context)
        )
        {
            return;
        }

        switch (assignment.Left)
        {
            // options.SizeLimit = 1024;
            case MemberAccessExpressionSyntax memberAccess
                when memberAccess.Name.Identifier.ValueText == "SizeLimit":
            {
                var ownerType = context
                    .SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken)
                    .Type;
                if (
                    wellKnownTypes.IsMemoryCacheOptions(ownerType)
                    && AssignsNonNullLimit(assignment.Right, context)
                )
                {
                    cacheHasGlobalSizeLimit.Value = true;
                }

                return;
            }

        }
    }

    /// <summary>
    /// <c>SizeLimit</c> is nullable, and assigning <c>null</c> explicitly leaves the cache unbounded.
    /// Treating any assignment as a bound would let a single <c>SizeLimit = null</c> suppress every
    /// memory-cache diagnostic in the compilation.
    /// </summary>
    /// <summary>
    /// Recognizes the registration whose options configure the cache the container hands to consumers:
    /// AddMemoryCache's own callback, or the standard options pattern over MemoryCacheOptions.
    /// <para>
    /// Bound to the framework's own extension containers rather than matched by name, so a project's
    /// own no-op <c>Configure(Action&lt;MemoryCacheOptions&gt;)</c> cannot suppress the whole tier.
    /// </para>
    /// </summary>
    private static bool IsCacheConfigurationCall(
        InvocationExpressionSyntax invocation,
        SyntaxNodeAnalysisContext context
    )
    {
        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
        {
            return false;
        }

        var original = method.ReducedFrom ?? method;
        if (
            original.Name
            is not ("AddMemoryCache" or "AddDistributedMemoryCache" or "Configure" or "PostConfigure")
        )
        {
            return false;
        }

        var containerNamespace = original.ContainingType?.ContainingNamespace?.ToDisplayString();
        return containerNamespace is "Microsoft.Extensions.DependencyInjection";
    }

    private static bool AssignsNonNullLimit(
        ExpressionSyntax value,
        SyntaxNodeAnalysisContext context
    )
    {
        if (value.IsKind(SyntaxKind.NullLiteralExpression))
        {
            return false;
        }

        var constant = context.SemanticModel.GetConstantValue(value, context.CancellationToken);
        if (constant.HasValue)
        {
            return constant.Value is not null;
        }

        // A non-constant nullable expression -- a configuration-backed long?, say -- may well be null at
        // runtime. Only a value whose type cannot be null establishes the bound.
        var valueType = context.SemanticModel.GetTypeInfo(value, context.CancellationToken).Type;
        return valueType is not null
            && valueType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
            && valueType.NullableAnnotation != NullableAnnotation.Annotated;
    }

    private static void CollectCacheWrite(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        WellKnownTypes wellKnownTypes,
        AnalyzerConfigOptionsProvider optionsProvider,
        ConcurrentBag<CacheWrite> writes,
        StrongBox cacheHasGlobalSizeLimit
    )
    {
        if (wellKnownTypes.IMemoryCache is null)
        {
            return;
        }

        if (!DetectMemoryCacheBoundsEnabled(optionsProvider, invocation.SyntaxTree))
        {
            return;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (methodName is not ("Set" or "GetOrCreate" or "GetOrCreateAsync" or "CreateEntry"))
        {
            return;
        }

        var semanticModel = context.SemanticModel;
        var cancellationToken = context.CancellationToken;

        var receiverType = semanticModel
            .GetTypeInfo(memberAccess.Expression, cancellationToken)
            .Type;
        if (!wellKnownTypes.ImplementsMemoryCache(receiverType))
        {
            return;
        }

        if (
            semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol
            is not IMethodSymbol method
        )
        {
            return;
        }

        var arguments = invocation.ArgumentList.Arguments;

        // Bound through the declared parameter: a reordered named-argument call such as
        // Set(value: q, key: userId) does not put the key first.
        var keyExpression = arguments
            .FirstOrDefault(argument =>
                ParameterFor(argument, method, arguments)?.Name == "key"
            )
            ?.Expression;
        if (keyExpression is null)
        {
            return;
        }

        if (SyntaxValueHelpers.TryExtractConstantValue(keyExpression, semanticModel, out _))
        {
            return;
        }

        var keyType = semanticModel.GetTypeInfo(keyExpression, cancellationToken).Type;
        if (
            keyType is null
            || keyType.TypeKind == TypeKind.Enum
            || keyType.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char
            || keyType.Name == "Type"
        )
        {
            return;
        }

        if (
            !TryDescribeUnboundedKeySource(
                keyExpression,
                semanticModel,
                cancellationToken,
                out var keyDescription
            )
        )
        {
            return;
        }

        if (
            EntryIsBounded(
                invocation,
                method,
                arguments,
                wellKnownTypes,
                semanticModel,
                cancellationToken
            )
        )
        {
            return;
        }

        _ = cacheHasGlobalSizeLimit;
        writes.Add(new CacheWrite(keyDescription, invocation.GetLocation()));
    }

    /// <summary>
    /// A cache entry counts as bounded when an expiration or size is provable at the call site. An
    /// options object built anywhere other than inline is unprovable and therefore silencing — the
    /// single biggest false-positive source in this tier.
    /// </summary>
    /// <summary>
    /// A cache entry counts as bounded when an expiration or size is provable at the call site. An
    /// options object built anywhere other than inline is unprovable and therefore silencing — the
    /// single biggest false-positive source in this tier.
    /// <para>
    /// Arguments are matched by declared parameter name rather than by type. The framework's
    /// <c>Set&lt;TItem&gt;(cache, key, TItem value, ...)</c> substitutes <c>TItem</c> with the cached
    /// value's own type, so a cached <c>TimeSpan</c> would otherwise be mistaken for an expiration and
    /// a real finding dropped.
    /// </para>
    /// </summary>
    private static bool EntryIsBounded(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        WellKnownTypes wellKnownTypes,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        // CreateEntry only commits when the returned entry is disposed. A discarded result never stores
        // anything, and a retained one may still be bounded by its caller, so there is nothing this rule
        // can honestly report about it.
        if (method.Name == "CreateEntry")
        {
            return true;
        }

        foreach (var argument in arguments)
        {
            var parameter = ParameterFor(argument, method, arguments);
            if (parameter is null)
            {
                // An unresolvable argument leaves the entry's bounds unknown.
                return true;
            }

            // The cache, the key, and the cached value carry no bound.
            if (parameter.Name is "cache" or "key" or "value")
            {
                continue;
            }

            if (wellKnownTypes.IsMemoryCacheEntryOptions(parameter.Type))
            {
                // Only an inline construction can be inspected.
                if (
                    argument.Expression
                    is not (
                        ObjectCreationExpressionSyntax
                        or ImplicitObjectCreationExpressionSyntax
                        or InvocationExpressionSyntax
                    )
                )
                {
                    return true;
                }

                return ExpressionSetsBound(argument.Expression);
            }

            // A GetOrCreate factory can bound the entry through its ICacheEntry.
            if (argument.Expression is AnonymousFunctionExpressionSyntax lambda)
            {
                if (ExpressionSetsBound(lambda))
                {
                    return true;
                }

                continue;
            }

            // Any remaining parameter on these overloads is an expiration or an expiration token, all
            // of which bound the entry.
            _ = semanticModel;
            _ = cancellationToken;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reports whether an expression provably configures a bound. A bare mention of a bounding member
    /// is not enough: <c>SlidingExpiration = null</c> assigns no bound, and a factory that merely reads
    /// or logs <c>entry.Size</c> configures nothing. Only an assignment with a non-null value, or a
    /// call to one of the bounding setters, counts.
    /// </summary>
    /// <summary>
    /// Resolves the parameter an argument binds to, honoring named arguments.
    /// </summary>
    private static IParameterSymbol? ParameterFor(
        ArgumentSyntax argument,
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments
    )
    {
        if (argument.NameColon?.Name.Identifier.ValueText is { } name)
        {
            return method.Parameters.FirstOrDefault(p => p.Name == name);
        }

        var ordinal = arguments.IndexOf(argument);
        return ordinal >= 0 && ordinal < method.Parameters.Length ? method.Parameters[ordinal] : null;
    }

    private static bool ExpressionSetsBound(SyntaxNode expression)
    {
        // The receiver that a bound must be configured on: the options object under construction, or
        // the ICacheEntry a factory lambda receives. Anything else with a same-named property -- a
        // cached domain object with its own Size, say -- configures nothing.
        var entryParameterName = expression switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax parenthesized when
                parenthesized.ParameterList.Parameters.Count == 1 =>
                parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
            _ => null,
        };

        foreach (var node in expression.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax assignment:
                {
                    if (assignment.Right.IsKind(SyntaxKind.NullLiteralExpression))
                    {
                        continue;
                    }

                    var (target, receiver) = assignment.Left switch
                    {
                        // Object-initializer form: the receiver is the object being constructed.
                        IdentifierNameSyntax identifier when
                            assignment.Parent is InitializerExpressionSyntax =>
                            (identifier.Identifier.ValueText, (string?)null),
                        MemberAccessExpressionSyntax memberAccess => (
                            memberAccess.Name.Identifier.ValueText,
                            (memberAccess.Expression as IdentifierNameSyntax)?.Identifier.ValueText
                        ),
                        _ => (null, null),
                    };

                    if (
                        target
                        is not (
                            "AbsoluteExpiration"
                            or "AbsoluteExpirationRelativeToNow"
                            or "SlidingExpiration"
                            or "Size"
                        )
                    )
                    {
                        continue;
                    }

                    // Inside a factory the receiver must be the entry parameter; in an initializer
                    // there is no receiver to check.
                    if (entryParameterName is null || receiver == entryParameterName)
                    {
                        return true;
                    }

                    continue;
                }

                case InvocationExpressionSyntax invocation
                    when invocation.Expression is MemberAccessExpressionSyntax call
                        && call.Name.Identifier.ValueText
                            is "SetAbsoluteExpiration"
                                or "SetSlidingExpiration"
                                or "SetSize"
                                or "AddExpirationToken":
                    return true;
            }
        }

        return false;
    }

    private static void ReportUnboundedCacheEntries(
        CompilationAnalysisContext context,
        ConcurrentBag<CacheWrite> writes
    )
    {
        foreach (
            var write in writes
                .OrderBy(
                    w => w.Location.SourceTree?.FilePath ?? string.Empty,
                    StringComparer.Ordinal
                )
                .ThenBy(w => w.Location.SourceSpan.Start)
        )
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.UnboundedMemoryCacheEntry,
                    write.Location,
                    write.KeyDescription
                )
            );
        }
    }

    // ----------------------------------------------------------------
    // Options
    // ----------------------------------------------------------------

    private static bool IsAllowedCacheType(
        ITypeSymbol elementType,
        AnalyzerConfigOptionsProvider optionsProvider,
        SyntaxTree syntaxTree
    )
    {
        if (
            !TryGetOption(
                optionsProvider.GetOptions(syntaxTree),
                AllowedCacheTypesOption,
                out var value
            ) && !TryGetOption(optionsProvider.GlobalOptions, AllowedCacheTypesOption, out value)
        )
        {
            return false;
        }

        var simpleName = elementType.Name;
        var displayName = elementType.ToDisplayString();
        var fullName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (fullName.StartsWith("global::", StringComparison.Ordinal))
        {
            fullName = fullName.Substring("global::".Length);
        }

        foreach (var candidate in value.Split(','))
        {
            var allowed = candidate.Trim();
            if (allowed.Length == 0)
            {
                continue;
            }

            if (
                string.Equals(allowed, simpleName, StringComparison.Ordinal)
                || string.Equals(allowed, displayName, StringComparison.Ordinal)
                || string.Equals(allowed, fullName, StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool DetectMemoryCacheBoundsEnabled(
        AnalyzerConfigOptionsProvider optionsProvider,
        SyntaxTree syntaxTree
    )
    {
        if (
            TryGetOption(
                optionsProvider.GetOptions(syntaxTree),
                DetectMemoryCacheBoundsOption,
                out var treeValue
            ) && bool.TryParse(treeValue, out var treeEnabled)
        )
        {
            return treeEnabled;
        }

        if (
            TryGetOption(
                optionsProvider.GlobalOptions,
                DetectMemoryCacheBoundsOption,
                out var globalValue
            ) && bool.TryParse(globalValue, out var globalEnabled)
        )
        {
            return globalEnabled;
        }

        return true;
    }

    private static bool TryGetOption(AnalyzerConfigOptions options, string key, out string value)
    {
        if (options.TryGetValue(key, out var optionValue))
        {
            value = optionValue;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
