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
/// DI028: a shorter-lived registered service registers a callback on a longer-lived registration
/// source and discards the registration that would detach it. The third member of the DI025/DI027
/// family — where DI025 proves a missing <c>-=</c> and DI027 proves a discarded <c>Subscribe</c>
/// token, DI028 covers the remaining callback mechanisms: <c>IOptionsMonitor.OnChange</c>,
/// <c>CancellationToken.Register</c>, <c>ChangeToken.OnChange</c>,
/// <c>IChangeToken.RegisterChangeCallback</c>, and <c>CreateLinkedTokenSource</c>.
/// <para>
/// Disjoint from its siblings by syntax: DI025 registers only assignment actions and requires a
/// delegate-typed target, DI027 matches <c>Subscribe</c> on an <c>IObservable&lt;T&gt;</c>. DI028
/// matches invocations against a closed mechanism allowlist that excludes <c>Subscribe</c> outright.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI028_CallbackRegistrationLeakAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.CallbackRegistrationLeak,
            DiagnosticDescriptors.CallbackRegistrationLeakLinkedTokenSource
        );

    /// <summary>
    /// The callback mechanisms DI028 recognizes. The order is the dedup priority: when one leak
    /// matches more than one mechanism (linking a token and then registering on it) the highest
    /// value wins, so the reported diagnostic names the outermost cause.
    /// </summary>
    private enum RegistrationMechanism
    {
        OptionsMonitorOnChange = 0,
        ChangeTokenOnChange = 1,
        RegisterChangeCallback = 2,
        CancellationTokenRegister = 3,
        LinkedTokenSource = 4,
    }

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

            // Nothing to match without at least one mechanism's anchor type present.
            if (
                wellKnownTypes.CancellationToken is null
                && wellKnownTypes.IOptionsMonitorOfT is null
                && wellKnownTypes.IChangeToken is null
            )
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

            var lifetimeClassifier = new KnownServiceLifetimeClassifier(wellKnownTypes);
            var records = new ConcurrentBag<RegistrationRecord>();
            var semanticModelsByTree = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    registrationCollector.AnalyzeInvocation(
                        (InvocationExpressionSyntax)syntaxContext.Node,
                        syntaxContext.SemanticModel
                    );
                    semanticModelsByTree.TryAdd(
                        syntaxContext.SemanticModel.SyntaxTree,
                        syntaxContext.SemanticModel
                    );
                },
                SyntaxKind.InvocationExpression
            );

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => CollectRegistration(syntaxContext, wellKnownTypes, records),
                SyntaxKind.InvocationExpression
            );

            compilationContext.RegisterCompilationEndAction(endContext =>
                ReportLeaks(
                    endContext,
                    registrationCollector,
                    wellKnownTypes,
                    lifetimeClassifier,
                    records,
                    semanticModelsByTree
                )
            );
        });
    }

    private sealed class RegistrationRecord
    {
        public RegistrationRecord(
            RegistrationMechanism mechanism,
            INamedTypeSymbol containingType,
            EventReceiverKind receiverKind,
            ISymbol? receiverRoot,
            ImmutableArray<ISymbol> receiverSegments,
            INamedTypeSymbol? publisherType,
            string mechanismDisplay,
            string sourceDisplay,
            Location location,
            TextSpanKey dedupKey
        )
        {
            Mechanism = mechanism;
            ContainingType = containingType;
            ReceiverKind = receiverKind;
            ReceiverRoot = receiverRoot;
            ReceiverSegments = receiverSegments;
            PublisherType = publisherType;
            MechanismDisplay = mechanismDisplay;
            SourceDisplay = sourceDisplay;
            Location = location;
            DedupKey = dedupKey;
        }

        public RegistrationMechanism Mechanism { get; }
        public INamedTypeSymbol ContainingType { get; }
        public EventReceiverKind ReceiverKind { get; }
        public ISymbol? ReceiverRoot { get; }
        public ImmutableArray<ISymbol> ReceiverSegments { get; }
        public INamedTypeSymbol? PublisherType { get; }
        public string MechanismDisplay { get; }
        public string SourceDisplay { get; }
        public Location Location { get; }
        public TextSpanKey DedupKey { get; }
    }

    /// <summary>
    /// Identifies the statement a registration sits in, so two mechanisms matching one leak collapse
    /// to a single diagnostic.
    /// </summary>
    private readonly struct TextSpanKey : System.IEquatable<TextSpanKey>
    {
        public TextSpanKey(string filePath, int start, int end, INamedTypeSymbol containingType)
        {
            FilePath = filePath;
            Start = start;
            End = end;
            ContainingType = containingType;
        }

        public string FilePath { get; }
        public int Start { get; }
        public int End { get; }
        public INamedTypeSymbol ContainingType { get; }

        public bool Equals(TextSpanKey other) =>
            Start == other.Start
            && End == other.End
            && string.Equals(FilePath, other.FilePath, System.StringComparison.Ordinal)
            && SymbolEqualityComparer.Default.Equals(ContainingType, other.ContainingType);

        public override bool Equals(object? obj) => obj is TextSpanKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = Start * 397;
            hash = (hash * 397) ^ End;
            hash = (hash * 397) ^ (FilePath?.GetHashCode() ?? 0);
            return hash;
        }
    }

    // ----------------------------------------------------------------
    // Leg M -- mechanism identification
    // ----------------------------------------------------------------

    private static void CollectRegistration(
        SyntaxNodeAnalysisContext context,
        WellKnownTypes wellKnownTypes,
        ConcurrentBag<RegistrationRecord> records
    )
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var cancellationToken = context.CancellationToken;

        var invokedName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        };

        // The DI027 boundary, asserted rather than assumed: DI028 must never widen into "any method
        // returning IDisposable on a long-lived receiver", which would swallow Subscribe whole.
        if (invokedName is null || invokedName == "Subscribe")
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

        var containingType = GetEnclosingNamedType(invocation, semanticModel, cancellationToken);
        if (containingType is null)
        {
            return;
        }

        if (
            !TryIdentifyMechanism(
                invocation,
                method,
                wellKnownTypes,
                semanticModel,
                cancellationToken,
                out var mechanism,
                out var anchors,
                out var mechanismDisplay
            )
        )
        {
            return;
        }

        // Leg C: the callback must provably root the subscriber, or the registration's lifetime is
        // irrelevant to the subscriber's.
        if (
            mechanism != RegistrationMechanism.LinkedTokenSource
            && !CallbackCapturesContainingInstance(
                invocation,
                method,
                containingType,
                semanticModel,
                cancellationToken
            )
        )
        {
            return;
        }

        // Leg D: only a provably discarded registration reports.
        if (!RegistrationIsDiscarded(invocation, mechanism, semanticModel, cancellationToken))
        {
            return;
        }

        var statement = invocation.FirstAncestorOrSelf<StatementSyntax>();
        var dedupSpan = statement?.Span ?? invocation.Span;

        // One record per candidate source. Only linking produces more than one, and there each token
        // is independently capable of rooting the creator; statement-level dedup then collapses them
        // to a single diagnostic whichever one proves long-lived.
        foreach (var anchor in anchors)
        {
            var (receiverKind, receiverRoot, receiverSegments, publisherType) =
                EventReceiverClassification.ClassifyReceiverExpression(
                    anchor,
                    containingType,
                    semanticModel,
                    cancellationToken
                );

            if (receiverKind == EventReceiverKind.Unknown)
            {
                continue;
            }

            records.Add(
                new RegistrationRecord(
                    mechanism,
                    containingType,
                    receiverKind,
                    receiverRoot,
                    receiverSegments,
                    publisherType,
                    mechanismDisplay,
                    anchor.ToString(),
                    invocation.GetLocation(),
                    new TextSpanKey(
                        invocation.SyntaxTree.FilePath,
                        dedupSpan.Start,
                        dedupSpan.End,
                        containingType
                    )
                )
            );
        }
    }

    private static bool TryIdentifyMechanism(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        WellKnownTypes wellKnownTypes,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out RegistrationMechanism mechanism,
        out ImmutableArray<ExpressionSyntax> anchors,
        out string mechanismDisplay
    )
    {
        mechanism = default;
        anchors = ImmutableArray<ExpressionSyntax>.Empty;
        mechanismDisplay = string.Empty;

        var original = method.ReducedFrom ?? method;
        var name = original.Name;

        switch (name)
        {
            case "OnChange":
                // ChangeToken.OnChange is static on the Primitives helper; the anchor is the receiver
                // of the single invocation inside the producer lambda.
                if (wellKnownTypes.IsChangeTokenStaticHelper(original.ContainingType))
                {
                    if (!TryGetChangeTokenProducerAnchor(invocation, out var producerAnchor))
                    {
                        return false;
                    }

                    anchors = ImmutableArray.Create(producerAnchor);
                    mechanism = RegistrationMechanism.ChangeTokenOnChange;
                    mechanismDisplay = "ChangeToken.OnChange";
                    return true;
                }

                // IOptionsMonitor<T>.OnChange, instance or extension form.
                {
                    var monitorExpression = GetReceiverOrFirstArgument(invocation, method);
                    if (monitorExpression is null)
                    {
                        return false;
                    }

                    var monitorType = semanticModel
                        .GetTypeInfo(monitorExpression, cancellationToken)
                        .Type;
                    if (!wellKnownTypes.IsOptionsMonitor(monitorType))
                    {
                        return false;
                    }

                    // The receiver type alone does not make this a registration. A project may define
                    // its own OnChange extension on IOptionsMonitor that returns void and invokes the
                    // callback immediately; there is no registration to discard there.
                    if (!ReturnsDisposable(method.ReturnType))
                    {
                        return false;
                    }

                    anchors = ImmutableArray.Create(monitorExpression);
                    mechanism = RegistrationMechanism.OptionsMonitorOnChange;
                    mechanismDisplay = "IOptionsMonitor<T>.OnChange";
                    return true;
                }

            case "RegisterChangeCallback":
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax tokenAccess)
                {
                    return false;
                }

                var tokenType = semanticModel
                    .GetTypeInfo(tokenAccess.Expression, cancellationToken)
                    .Type;
                if (!wellKnownTypes.ImplementsChangeToken(tokenType))
                {
                    return false;
                }

                // A change token is single-shot and replaced on every reload, so only a token
                // taken directly from a producer call has provable provenance and longevity. A
                // token reached through a field or local stays silent.
                if (
                    tokenAccess.Expression is not InvocationExpressionSyntax producer
                    || producer.Expression is not MemberAccessExpressionSyntax producerAccess
                )
                {
                    return false;
                }

                anchors = ImmutableArray.Create(producerAccess.Expression);
                mechanism = RegistrationMechanism.RegisterChangeCallback;
                mechanismDisplay = "IChangeToken.RegisterChangeCallback";
                return true;
            }

            case "Register":
            case "UnsafeRegister":
            {
                if (
                    !wellKnownTypes.IsCancellationToken(original.ContainingType)
                    || !wellKnownTypes.IsCancellationTokenRegistration(method.ReturnType)
                )
                {
                    return false;
                }

                if (invocation.Expression is not MemberAccessExpressionSyntax tokenAccess)
                {
                    return false;
                }

                anchors = ImmutableArray.Create(tokenAccess.Expression);
                mechanism = RegistrationMechanism.CancellationTokenRegister;
                mechanismDisplay = $"CancellationToken.{name}";
                return true;
            }

            case "CreateLinkedTokenSource":
            {
                if (!wellKnownTypes.IsCancellationTokenSource(original.ContainingType))
                {
                    return false;
                }

                // Linking registers a callback on every token it links; one provably long-lived
                // token is enough to root the creator.
                // Every token argument is a candidate anchor. Collecting all of them rather than
                // the first keeps the verdict independent of argument order -- whichever token proves
                // longest-lived decides -- and statement-level dedup collapses the rest into one
                // diagnostic.
                var tokenAnchors = ImmutableArray.CreateBuilder<ExpressionSyntax>();
                foreach (var argument in invocation.ArgumentList.Arguments)
                {
                    var argumentType = semanticModel
                        .GetTypeInfo(argument.Expression, cancellationToken)
                        .Type;
                    if (wellKnownTypes.IsCancellationToken(argumentType))
                    {
                        tokenAnchors.Add(argument.Expression);
                    }
                }

                if (tokenAnchors.Count == 0)
                {
                    return false;
                }

                anchors = tokenAnchors.ToImmutable();
                mechanism = RegistrationMechanism.LinkedTokenSource;
                mechanismDisplay = "CancellationTokenSource.CreateLinkedTokenSource";
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// A registration mechanism must hand back something disposable; that token is the thing whose
    /// discard this rule proves.
    /// </summary>
    private static bool ReturnsDisposable(ITypeSymbol returnType)
    {
        if (returnType.SpecialType == SpecialType.System_IDisposable)
        {
            return true;
        }

        return returnType.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_IDisposable);
    }

    /// <summary>
    /// Extracts the change-token source from <c>ChangeToken.OnChange(() =&gt; x.GetReloadToken(), ...)</c>.
    /// Only a lambda whose body is a single invocation on a member access is accepted; a block body,
    /// a cached delegate, or a method group leaves the source unprovable.
    /// </summary>
    private static bool TryGetChangeTokenProducerAnchor(
        InvocationExpressionSyntax invocation,
        out ExpressionSyntax anchor
    )
    {
        anchor = null!;

        var producerArgument = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (producerArgument?.Expression is not AnonymousFunctionExpressionSyntax lambda)
        {
            return false;
        }

        if (
            lambda.Body is not InvocationExpressionSyntax producer
            || producer.Expression is not MemberAccessExpressionSyntax producerAccess
        )
        {
            return false;
        }

        anchor = producerAccess.Expression;
        return true;
    }

    /// <summary>
    /// Returns the instance receiver, or for a static extension call the argument bound to the
    /// extension's <c>this</c> parameter. Bound through the reduced-form comparison rather than by
    /// position so named and reordered arguments resolve correctly.
    /// </summary>
    private static ExpressionSyntax? GetReceiverOrFirstArgument(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method
    )
    {
        if (
            invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && method.ReducedFrom is not null
        )
        {
            return memberAccess.Expression;
        }

        if (
            invocation.Expression is MemberAccessExpressionSyntax instanceAccess
            && !method.IsStatic
        )
        {
            return instanceAccess.Expression;
        }

        // Explicit static extension syntax: OptionsMonitorExtensions.OnChange(monitor, handler).
        // Bound through the declared parameters rather than by source order, so reordered named
        // arguments such as OnChange(listener: Apply, monitor: m) still identify the receiver.
        return ArgumentForParameterOrdinal(invocation, method, 0);
    }

    /// <summary>
    /// Returns the argument bound to a parameter ordinal, honoring named arguments. Source position is
    /// not a reliable guide: a named-argument call may list the extension receiver last.
    /// </summary>
    private static ExpressionSyntax? ArgumentForParameterOrdinal(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        int ordinal
    )
    {
        var original = method.ReducedFrom ?? method;
        if (ordinal >= original.Parameters.Length)
        {
            return null;
        }

        var parameterName = original.Parameters[ordinal].Name;
        var arguments = invocation.ArgumentList.Arguments;

        foreach (var argument in arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText == parameterName)
            {
                return argument.Expression;
            }
        }

        // No name matched, so positional binding applies -- but only while no earlier argument was
        // named, which would shift the mapping.
        if (ordinal < arguments.Count && arguments.Take(ordinal + 1).All(a => a.NameColon is null))
        {
            return arguments[ordinal].Expression;
        }

        return null;
    }

    // ----------------------------------------------------------------
    // Leg C -- closure capture
    // ----------------------------------------------------------------

    /// <summary>
    /// Proves the registration roots the subscriber. Any delegate-typed argument that captures the
    /// containing instance counts, and so does a <c>state</c> argument that is the containing
    /// instance: <c>token.Register(static s =&gt; ((Worker)s!).Run(), this)</c> has a static callback
    /// yet still pins the subscriber for as long as the token lives.
    /// </summary>
    private static bool CallbackCapturesContainingInstance(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            // Classified unconditionally rather than only for delegate-typed arguments: a method
            // group has no Type of its own, only a ConvertedType, so gating on the argument's type
            // would skip `Register(OnStopping)` -- the most common shape this rule exists to catch.
            var (kind, _, _) = EventHandlerClassification.ClassifyHandler(
                argument.Expression,
                containingType,
                semanticModel,
                cancellationToken
            );
            if (kind != EventHandlerKind.Unknown)
            {
                return true;
            }

            if (
                ExpressionIsContainingInstance(
                    argument.Expression,
                    containingType,
                    semanticModel,
                    cancellationToken
                )
            )
            {
                return true;
            }
        }

        _ = method;
        return false;
    }

    private static bool ExpressionIsContainingInstance(
        ExpressionSyntax expression,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        var current = expression;
        while (true)
        {
            switch (current)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    current = cast.Expression;
                    continue;
                default:
                    break;
            }

            break;
        }

        if (current is not ThisExpressionSyntax)
        {
            return false;
        }

        var type = semanticModel.GetTypeInfo(current, cancellationToken).Type;
        return SymbolEqualityComparer.Default.Equals(type, containingType);
    }

    // ----------------------------------------------------------------
    // Leg D -- discard proof (mirrors DI027's token-discard shapes)
    // ----------------------------------------------------------------

    private static bool RegistrationIsDiscarded(
        InvocationExpressionSyntax invocation,
        RegistrationMechanism mechanism,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        ExpressionSyntax node = invocation;
        var parent = invocation.Parent;
        var linkedSourceTokenExtracted = false;
        var linkedSourceRemainsDirectReceiver = true;

        // A chained call such as CreateLinkedTokenSource(...).Token.Register(H) consumes the outer
        // value; whether the whole statement is discarded is decided by the outermost expression.
        while (
            parent is ExpressionSyntax parentExpression
            && (
                parent
                    is MemberAccessExpressionSyntax
                        or InvocationExpressionSyntax
                        or ConditionalAccessExpressionSyntax
                || IsTransparentOuterExpression(
                    node,
                    parentExpression,
                    semanticModel,
                    cancellationToken
                )
                || IsOwnershipPreservingOuterExpression(
                    node,
                    parentExpression,
                    semanticModel,
                    cancellationToken
                )
            )
        )
        {
            var isTransparentOuterExpression = IsTransparentOuterExpression(
                node,
                parentExpression,
                semanticModel,
                cancellationToken
            );
            var directlyExtractsToken =
                parent is MemberAccessExpressionSyntax tokenAccess
                    && tokenAccess.Expression == node
                    && tokenAccess.Name.Identifier.ValueText == "Token"
                || parent is ConditionalAccessExpressionSyntax conditionalAccess
                    && conditionalAccess.Expression == node
                    && ConditionalAccessStartsWithTokenProjection(conditionalAccess);
            linkedSourceTokenExtracted |=
                mechanism == RegistrationMechanism.LinkedTokenSource
                && linkedSourceRemainsDirectReceiver
                && directlyExtractsToken;
            linkedSourceRemainsDirectReceiver &= isTransparentOuterExpression;

            // `token.Register(OnStop).Dispose();` disposes the registration in the same expression.
            // Without this the promotion below would classify the whole statement as discarded and
            // the rule would warn on one of the remediations it recommends.
            if (
                parent is MemberAccessExpressionSyntax disposalAccess
                && disposalAccess.Expression == node
                && disposalAccess.Name.Identifier.ValueText
                    is "Dispose"
                        or "DisposeAsync"
                        or "Unregister"
                && disposalAccess.Parent is InvocationExpressionSyntax disposalInvocation
                && disposalInvocation.Expression == disposalAccess
            )
            {
                if (
                    IsActualParameterlessInstanceCall(
                        disposalInvocation,
                        semanticModel,
                        cancellationToken
                    )
                )
                {
                    return linkedSourceTokenExtracted;
                }

                if (mechanism == RegistrationMechanism.LinkedTokenSource && node == invocation)
                {
                    // An extension method that merely borrows the source is not disposal proof.
                    return true;
                }
            }

            node = parentExpression;
            parent = parent.Parent;
        }

        // Extracting Token directly loses the only reference through which the linked source can be
        // disposed, so its registration on the longer-lived parent remains attached regardless of
        // how the token value itself is subsequently consumed or stored.
        if (linkedSourceTokenExtracted)
        {
            return true;
        }

        if (parent is ExpressionStatementSyntax)
        {
            return true;
        }

        if (
            parent is AssignmentExpressionSyntax assignment
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && assignment.Right == node
        )
        {
            var target = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
            if (target is IDiscardSymbol)
            {
                return true;
            }

            if (
                mechanism == RegistrationMechanism.LinkedTokenSource
                && target is ILocalSymbol
                && LinkedTokenSourceLocalNeedsDisposalDiagnostic(
                    invocation,
                    node,
                    parent,
                    semanticModel,
                    cancellationToken
                )
            )
            {
                return true;
            }

            return TargetIsOtherwiseUnusedPrivateField(
                assignment,
                target,
                semanticModel,
                cancellationToken
            );
        }

        return RegistrationIsUnreferencedLocal(
                node,
                parent,
                semanticModel,
                cancellationToken
            )
            || mechanism == RegistrationMechanism.LinkedTokenSource
                && LinkedTokenSourceLocalNeedsDisposalDiagnostic(
                    invocation,
                    node,
                    parent,
                    semanticModel,
                    cancellationToken
                );
    }

    private static bool IsTransparentOuterExpression(
        ExpressionSyntax inner,
        ExpressionSyntax outer,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        if (outer is ParenthesizedExpressionSyntax parenthesized)
        {
            return parenthesized.Expression == inner;
        }

        if (
            outer is PostfixUnaryExpressionSyntax suppression
            && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression)
        )
        {
            return suppression.Operand == inner;
        }

        return outer is CastExpressionSyntax cast
            && cast.Expression == inner
            && SymbolEqualityComparer.Default.Equals(
                semanticModel.GetTypeInfo(inner, cancellationToken).Type,
                semanticModel.GetTypeInfo(cast.Type, cancellationToken).Type
            );
    }

    private static bool IsOwnershipPreservingOuterExpression(
        ExpressionSyntax inner,
        ExpressionSyntax outer,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        if (
            outer is ConditionalExpressionSyntax conditional
            && (conditional.WhenTrue == inner || conditional.WhenFalse == inner)
        )
        {
            return !semanticModel.GetConversion(inner, cancellationToken).IsUserDefined;
        }

        if (
            outer is not BinaryExpressionSyntax coalesce
            || !coalesce.IsKind(SyntaxKind.CoalesceExpression)
            || (coalesce.Left != inner && coalesce.Right != inner)
            || semanticModel.GetConversion(inner, cancellationToken).IsUserDefined
        )
        {
            return false;
        }

        return coalesce.Right == inner
            || semanticModel.GetOperation(coalesce, cancellationToken)
                is ICoalesceOperation { ValueConversion.IsUserDefined: false };
    }

    private static bool IsActualParameterlessInstanceCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        return semanticModel.GetOperation(invocation, cancellationToken)
                is IInvocationOperation operation
            && !operation.TargetMethod.IsExtensionMethod
            && operation.TargetMethod.ReducedFrom is null
            && operation.TargetMethod.Parameters.Length == 0;
    }

    private static bool TargetIsOtherwiseUnusedPrivateField(
        AssignmentExpressionSyntax assignment,
        ISymbol? target,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        if (
            target is not IFieldSymbol field
            || field.DeclaredAccessibility != Accessibility.Private
        )
        {
            return false;
        }

        var containingType = GetEnclosingNamedType(assignment, semanticModel, cancellationToken);
        if (!SymbolEqualityComparer.Default.Equals(field.ContainingType, containingType))
        {
            return false;
        }

        // Because the field is private, every reference to it lives inside this type's declarations,
        // so scanning them is a complete proof that nothing else ever touches the registration.
        foreach (var typeReference in field.ContainingType.DeclaringSyntaxReferences)
        {
            if (
                typeReference.GetSyntax(cancellationToken)
                is not TypeDeclarationSyntax typeDeclaration
            )
            {
                return false;
            }

            var typeModel = FactoryAnalysis.GetSemanticModelForNode(typeDeclaration, semanticModel);
            if (typeModel is null)
            {
                return false;
            }

            foreach (
                var identifier in typeDeclaration.DescendantNodes().OfType<IdentifierNameSyntax>()
            )
            {
                if (
                    identifier.Identifier.ValueText != field.Name
                    || !SymbolEqualityComparer.Default.Equals(
                        typeModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                        field
                    )
                )
                {
                    continue;
                }

                var isAssignmentTarget =
                    identifier.SyntaxTree == assignment.SyntaxTree
                    && identifier.SpanStart >= assignment.Left.SpanStart
                    && identifier.Span.End <= assignment.Left.Span.End;
                if (!isAssignmentTarget)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool RegistrationIsUnreferencedLocal(
        ExpressionSyntax node,
        SyntaxNode? parent,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        if (
            parent
                is not EqualsValueClauseSyntax
                {
                    Parent: VariableDeclaratorSyntax declarator
                } equals
            || equals.Value != node
            || declarator.Parent
                is not VariableDeclarationSyntax
                {
                    Parent: LocalDeclarationStatementSyntax localStatement
                }
        )
        {
            return false;
        }

        // `using var r = ...` disposes deterministically at scope end.
        if (!localStatement.UsingKeyword.IsKind(SyntaxKind.None))
        {
            return false;
        }

        if (
            semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol local
        )
        {
            return false;
        }

        var body = EventReceiverClassification.ExecutableBodyOf(declarator);
        if (body is null)
        {
            return false;
        }

        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (
                identifier.Identifier.ValueText != local.Name
                || identifier.Parent is VariableDeclaratorSyntax
            )
            {
                continue;
            }

            if (
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    local
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool LinkedTokenSourceLocalNeedsDisposalDiagnostic(
        InvocationExpressionSyntax creation,
        ExpressionSyntax node,
        SyntaxNode? parent,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        ILocalSymbol? local = null;
        SyntaxNode? referenceRoot = null;

        if (
            parent
                is EqualsValueClauseSyntax
                {
                    Parent: VariableDeclaratorSyntax declarator
                } equals
            && equals.Value == node
            && declarator.Parent
                is VariableDeclarationSyntax
                {
                    Parent: LocalDeclarationStatementSyntax localStatement
                }
            && localStatement.UsingKeyword.IsKind(SyntaxKind.None)
        )
        {
            local = semanticModel.GetDeclaredSymbol(declarator, cancellationToken) as ILocalSymbol;
            referenceRoot = declarator;
        }
        else if (
            parent is AssignmentExpressionSyntax assignment
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && assignment.Right == node
            && semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol
                is ILocalSymbol assignmentLocal
        )
        {
            local = assignmentLocal;
            referenceRoot = assignment;
        }

        if (local is null || referenceRoot is null)
        {
            return false;
        }

        if (
            semanticModel.GetOperation(creation, cancellationToken) is IInvocationOperation operation
            && DI014_RootProviderNotDisposedAnalyzer.IsProperlyDisposed(operation, semanticModel)
        )
        {
            return false;
        }

        var body = EventReceiverClassification.ExecutableBodyOf(referenceRoot);
        if (body is null)
        {
            return false;
        }

        var foundTokenRead =
            referenceRoot is AssignmentExpressionSyntax assignmentExpression
            && IsTokenReadOfExpression(
                assignmentExpression,
                semanticModel,
                cancellationToken
            );
        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (
                identifier.Identifier.ValueText != local.Name
                || identifier.Parent is VariableDeclaratorSyntax
                || !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    local
                )
            )
            {
                continue;
            }

            if (EventReceiverClassification.ExecutableBodyOf(identifier) != body)
            {
                // A captured local can be consumed or disposed when a nested function is invoked.
                // DI014 deliberately refuses cross-boundary cleanup as a straight-line proof, so
                // keep DI028 conservative rather than interpreting nested syntax as outer flow.
                return false;
            }

            if (identifier.SpanStart <= node.Span.End)
            {
                // Assignment targets may have belonged to an older instance before this creation.
                // Only references after the current value is stored describe this linked source.
                continue;
            }

            var referenceExpression = GetTransparentReferenceExpression(
                identifier,
                semanticModel,
                cancellationToken
            );
            var isTokenRead = IsTokenReadOfExpression(
                referenceExpression,
                semanticModel,
                cancellationToken
            );
            var isDirectDisposalAttempt =
                referenceExpression.Parent
                    is MemberAccessExpressionSyntax
                    {
                        Expression: var disposalReceiver,
                        Name.Identifier.ValueText: "Dispose" or "DisposeAsync"
                    }
                && disposalReceiver == referenceExpression;
            var isConditionalDisposalAttempt =
                referenceExpression.Parent
                    is ConditionalAccessExpressionSyntax
                    {
                        Expression: var conditionalDisposalReceiver,
                        WhenNotNull: InvocationExpressionSyntax
                        {
                            Expression: MemberBindingExpressionSyntax
                            {
                                Name.Identifier.ValueText: "Dispose" or "DisposeAsync"
                            }
                        }
                    }
                && conditionalDisposalReceiver == referenceExpression;
            var isDisposalAttempt = isDirectDisposalAttempt || isConditionalDisposalAttempt;
            var isReassignment =
                identifier.Parent is AssignmentExpressionSyntax assignment
                && assignment.Left == identifier;

            if (!isTokenRead && !isDisposalAttempt && !isReassignment)
            {
                return false;
            }

            foundTokenRead |= isTokenRead;
        }

        return foundTokenRead;
    }

    private static ExpressionSyntax GetTransparentReferenceExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        var current = expression;
        while (current.Parent is ExpressionSyntax parent)
        {
            if (
                parent is ParenthesizedExpressionSyntax parenthesized
                && parenthesized.Expression == current
            )
            {
                current = parenthesized;
                continue;
            }

            if (
                parent is PostfixUnaryExpressionSyntax suppression
                && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression)
                && suppression.Operand == current
            )
            {
                current = suppression;
                continue;
            }

            if (
                parent is CastExpressionSyntax cast
                && cast.Expression == current
                && SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetTypeInfo(current, cancellationToken).Type,
                    semanticModel.GetTypeInfo(cast.Type, cancellationToken).Type
                )
            )
            {
                current = cast;
                continue;
            }

            break;
        }

        return current;
    }

    private static bool IsTokenReadOfExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        var referenceExpression = GetTransparentReferenceExpression(
            expression,
            semanticModel,
            cancellationToken
        );
        return referenceExpression.Parent
                is MemberAccessExpressionSyntax
                {
                    Expression: var tokenReceiver,
                    Name.Identifier.ValueText: "Token"
                }
                && tokenReceiver == referenceExpression
            || referenceExpression.Parent
                is ConditionalAccessExpressionSyntax conditionalAccess
                && conditionalAccess.Expression == referenceExpression
                && ConditionalAccessStartsWithTokenProjection(conditionalAccess);
    }

    private static bool ConditionalAccessStartsWithTokenProjection(
        ConditionalAccessExpressionSyntax conditionalAccess
    )
    {
        ExpressionSyntax current = conditionalAccess.WhenNotNull;
        while (true)
        {
            switch (current)
            {
                case InvocationExpressionSyntax invocation:
                    current = invocation.Expression;
                    continue;
                case MemberAccessExpressionSyntax memberAccess:
                    current = memberAccess.Expression;
                    continue;
                case MemberBindingExpressionSyntax memberBinding:
                    return memberBinding.Name.Identifier.ValueText == "Token";
                default:
                    return false;
            }
        }
    }

    // ----------------------------------------------------------------
    // Reporting -- legs P and S
    // ----------------------------------------------------------------

    private static void ReportLeaks(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        ConcurrentBag<RegistrationRecord> records,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree
    )
    {
        if (records.IsEmpty)
        {
            return;
        }

        var registrations = registrationCollector.AllRegistrations.ToList();
        var subscriberRanks = BuildSubscriberRankMap(registrations);
        var reported = new HashSet<TextSpanKey>();

        // Records arrive from a ConcurrentBag in nondeterministic order, so they are sorted before
        // dedup: without this the surviving record for a doubly-matched leak -- and therefore the
        // descriptor and location reported -- would vary between runs.
        var ordered = records
            .OrderBy(
                record => record.Location.SourceTree?.FilePath ?? string.Empty,
                System.StringComparer.Ordinal
            )
            .ThenBy(record => record.Location.SourceSpan.Start)
            .ThenByDescending(record => (int)record.Mechanism)
            .ToList();

        foreach (var record in ordered)
        {
            if (
                !IsReportableRegistration(
                    record,
                    registrations,
                    subscriberRanks,
                    wellKnownTypes,
                    lifetimeClassifier,
                    context.Compilation,
                    semanticModelsByTree,
                    context.CancellationToken,
                    out var subscriberRank
                )
            )
            {
                continue;
            }

            if (!reported.Add(record.DedupKey))
            {
                continue;
            }

            var lifetimeText =
                subscriberRank == RankOf(ServiceLifetime.Transient) ? "transient" : "scoped";

            if (record.Mechanism == RegistrationMechanism.LinkedTokenSource)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DiagnosticDescriptors.CallbackRegistrationLeakLinkedTokenSource,
                        record.Location,
                        record.ContainingType.Name,
                        lifetimeText,
                        record.SourceDisplay
                    )
                );
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.CallbackRegistrationLeak,
                    record.Location,
                    record.ContainingType.Name,
                    lifetimeText,
                    record.MechanismDisplay,
                    record.SourceDisplay
                )
            );
        }
    }

    private static bool IsReportableRegistration(
        RegistrationRecord record,
        List<ServiceRegistration> registrations,
        Dictionary<INamedTypeSymbol, int> subscriberRanks,
        WellKnownTypes wellKnownTypes,
        KnownServiceLifetimeClassifier lifetimeClassifier,
        Compilation compilation,
        ConcurrentDictionary<SyntaxTree, SemanticModel> semanticModelsByTree,
        System.Threading.CancellationToken cancellationToken,
        out int subscriberRank
    )
    {
        // Leg S: an unregistered subscriber, or one that lives as long as the source, has nothing to
        // leak. This is the guard that keeps the idiomatic singleton and hosted-service registrations
        // on ApplicationStopping silent.
        if (
            !subscriberRanks.TryGetValue(record.ContainingType, out subscriberRank)
            || subscriberRank >= RankOf(ServiceLifetime.Singleton)
        )
        {
            return false;
        }

        // Leg P: the source must outlive the subscriber.
        var publisherRank = ResolvePublisherRank(record, registrations, lifetimeClassifier);
        if (
            publisherRank is null
            || publisherRank < RankOf(ServiceLifetime.Scoped)
            || publisherRank <= subscriberRank
        )
        {
            return false;
        }

        if (
            record.ReceiverKind == EventReceiverKind.InjectedMember
            && !EventReceiverClassification.MemberIsProvablyInjected(
                record.ReceiverRoot,
                compilation,
                semanticModelsByTree,
                cancellationToken
            )
        )
        {
            return false;
        }

        if (
            !record.ReceiverSegments.IsEmpty
            && !EventReceiverClassification.ChainSegmentsAreStableProjectionsWithFrameworkSuffix(
                record.ReceiverSegments,
                record.PublisherType,
                registrations,
                compilation,
                semanticModelsByTree,
                cancellationToken,
                segment => IsKnownStableFrameworkSegment(segment, wellKnownTypes)
            )
        )
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// The framework projections DI028 accepts as a contiguous chain suffix. Each is get-only and
    /// returns the same instance for its owner's lifetime by documented contract, but lives only in
    /// metadata — so the general stable-projection proof, which requires declaring syntax, can never
    /// establish it. Nothing outside this list is accepted.
    /// </summary>
    private static bool IsKnownStableFrameworkSegment(
        ISymbol segment,
        WellKnownTypes wellKnownTypes
    )
    {
        if (segment is not IPropertySymbol property)
        {
            return false;
        }

        if (
            property.Name == "Token"
            && wellKnownTypes.IsCancellationTokenSource(property.ContainingType)
        )
        {
            return true;
        }

        return property.Name
                is "ApplicationStarted"
                    or "ApplicationStopping"
                    or "ApplicationStopped"
            && wellKnownTypes.IsHostApplicationLifetimeIncludingSourceDeclared(
                property.ContainingType
            );
    }

    private static int RankOf(ServiceLifetime lifetime) =>
        lifetime switch
        {
            ServiceLifetime.Transient => 0,
            ServiceLifetime.Scoped => 1,
            _ => 2,
        };

    // Mirrors DI025/DI027's subscriber-rank map (base-chain aware, most-conservative wins).
    // Duplicated rather than shared so those rules' proven collections stay untouched.
    private static Dictionary<INamedTypeSymbol, int> BuildSubscriberRankMap(
        List<ServiceRegistration> registrations
    )
    {
        var ranks = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);

        foreach (var registration in registrations)
        {
            var implementationType = registration.ImplementationType;
            if (implementationType is null)
            {
                continue;
            }

            var rank = RankOf(registration.Lifetime);
            for (
                var current = implementationType;
                current is not null && current.SpecialType != SpecialType.System_Object;
                current = current.BaseType
            )
            {
                if (!ranks.TryGetValue(current, out var existing) || rank > existing)
                {
                    ranks[current] = rank;
                }
            }
        }

        return ranks;
    }

    private static int? ResolvePublisherRank(
        RegistrationRecord record,
        List<ServiceRegistration> registrations,
        KnownServiceLifetimeClassifier lifetimeClassifier
    )
    {
        if (record.PublisherType is null)
        {
            return null;
        }

        var rank = MinimumRegisteredRank(registrations, record.PublisherType);

        if (
            rank is null
            && record.PublisherType.IsGenericType
            && !record.PublisherType.IsUnboundGenericType
        )
        {
            rank = MinimumRegisteredRank(
                registrations,
                record.PublisherType.ConstructUnboundGenericType()
            );
        }

        if (rank is not null)
        {
            return rank;
        }

        return lifetimeClassifier.TryGetLifetime(
            record.PublisherType,
            isKeyed: false,
            out var knownLifetime
        )
            ? RankOf(knownLifetime)
            : null;
    }

    private static int? MinimumRegisteredRank(
        List<ServiceRegistration> registrations,
        INamedTypeSymbol serviceType
    )
    {
        int? rank = null;
        foreach (var registration in registrations)
        {
            if (
                registration.IsKeyed
                || !SymbolEqualityComparer.Default.Equals(registration.ServiceType, serviceType)
            )
            {
                continue;
            }

            var registrationRank = RankOf(registration.Lifetime);
            if (rank is null || registrationRank < rank)
            {
                rank = registrationRank;
            }
        }

        return rank;
    }

    private static INamedTypeSymbol? GetEnclosingNamedType(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        var typeDeclaration = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        return typeDeclaration is null
            ? null
            : semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);
    }
}
