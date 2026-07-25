using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// The kind of socket-owning object a construction site creates.
/// </summary>
internal enum HttpClientCreationKind
{
    /// <summary>Not a socket-owning construction, or not provable.</summary>
    Unknown,

    /// <summary>Exactly System.Net.Http.HttpClient.</summary>
    Client,

    /// <summary>A concrete handler that owns a connection pool and is rotated by IHttpClientFactory.</summary>
    RotatableHandler,
}

/// <summary>
/// What happens to a newly constructed client, which decides whether the construction can be judged
/// from the site alone.
/// </summary>
internal enum HttpClientCreationEscape
{
    /// <summary>
    /// The instance is used and abandoned within the expression or statement that creates it, so it
    /// is created once per execution of that path.
    /// </summary>
    None,

    /// <summary>
    /// The instance is stored in a field or property of the declaring type. Whether that is one
    /// client per process or one per resolution depends on the owner's registered lifetime, so the
    /// decision has to wait until the registration graph is known.
    /// </summary>
    RetainedInMember,

    /// <summary>
    /// The instance is returned, passed as an argument, or otherwise handed to code this analyzer
    /// does not model, so ownership cannot be established and the site stays silent.
    /// </summary>
    Escapes,
}

/// <summary>
/// Where in a type a construction site sits. DI029 reports only sites a caller reaches more than
/// once, so the distinction between a per-invocation body and a one-time initializer is the whole
/// difference between a socket leak and a shared client.
/// </summary>
internal readonly struct HttpClientCreationSite
{
    public HttpClientCreationSite(
        INamedTypeSymbol containingType,
        bool inLoopBody,
        bool inConstructorOrInstanceInitializer,
        bool inStaticInitializerOrStaticConstructor
    )
    {
        ContainingType = containingType;
        InLoopBody = inLoopBody;
        InConstructorOrInstanceInitializer = inConstructorOrInstanceInitializer;
        InStaticInitializerOrStaticConstructor = inStaticInitializerOrStaticConstructor;
    }

    /// <summary>Gets the named type that declares the construction site.</summary>
    public INamedTypeSymbol ContainingType { get; }

    /// <summary>Gets whether the site sits inside a loop body, which makes it per-iteration.</summary>
    public bool InLoopBody { get; }

    /// <summary>Gets whether the site sits in a constructor or an instance member initializer.</summary>
    public bool InConstructorOrInstanceInitializer { get; }

    /// <summary>
    /// Gets whether the site sits in a static initializer or static constructor. Such a site runs
    /// once per process, so it is never a socket leak; the static-member tier owns that shape.
    /// </summary>
    public bool InStaticInitializerOrStaticConstructor { get; }
}

/// <summary>
/// Classification helpers for HttpClient construction sites (DI029).
/// <para>
/// A new sibling file rather than an extension of an existing helper, following the convention that
/// added analysis capability must not perturb a proven proof: nothing here is reachable from
/// DI001-DI028.
/// </para>
/// </summary>
internal static class HttpClientCreationClassification
{
    /// <summary>
    /// Proof leg A-L1. Matches the created type by exact symbol identity. Derived types are excluded:
    /// a subclass may install a handler whose rotation this analyzer cannot reason about, and a
    /// false report on a deliberately pooled subclass is worse than the missed case.
    /// </summary>
    public static bool TryClassifyCreation(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        System.Threading.CancellationToken cancellationToken,
        out HttpClientCreationKind kind
    )
    {
        kind = HttpClientCreationKind.Unknown;

        var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        if (createdType is null || createdType is IErrorTypeSymbol)
        {
            return false;
        }

        if (wellKnownTypes.IsHttpClient(createdType))
        {
            kind = HttpClientCreationKind.Client;
            return true;
        }

        if (wellKnownTypes.IsRotatableHandler(createdType))
        {
            kind = HttpClientCreationKind.RotatableHandler;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Proof leg A-L2's location half: locates the declaring type and records whether the site is
    /// per-invocation, per-iteration, or one-time. Returns false when the enclosing type cannot be
    /// resolved, which keeps an unresolvable site silent.
    /// </summary>
    public static bool TryClassifySite(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out HttpClientCreationSite site
    )
    {
        site = default;

        var typeDeclaration = creation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDeclaration is null)
        {
            return false;
        }

        if (
            semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken)
            is not INamedTypeSymbol containingType
        )
        {
            return false;
        }

        var inLoopBody = false;
        var inConstructorOrInstanceInitializer = false;
        var inStaticInitializerOrStaticConstructor = false;

        for (SyntaxNode? node = creation; node is not null; node = node.Parent)
        {
            if (node is TypeDeclarationSyntax)
            {
                break;
            }

            if (
                node
                is ForStatementSyntax
                    or ForEachStatementSyntax
                    or ForEachVariableStatementSyntax
                    or WhileStatementSyntax
                    or DoStatementSyntax
            )
            {
                inLoopBody = true;
            }

            if (node is ConstructorDeclarationSyntax constructor)
            {
                if (constructor.Modifiers.Any(SyntaxKind.StaticKeyword))
                {
                    inStaticInitializerOrStaticConstructor = true;
                }
                else
                {
                    inConstructorOrInstanceInitializer = true;
                }
            }

            // A field or property initializer has no enclosing method; whether it runs once per
            // process or once per activation is decided by the static modifier on the member.
            if (node is FieldDeclarationSyntax field)
            {
                if (field.Modifiers.Any(SyntaxKind.StaticKeyword))
                {
                    inStaticInitializerOrStaticConstructor = true;
                }
                else
                {
                    inConstructorOrInstanceInitializer = true;
                }
            }

            if (node is PropertyDeclarationSyntax property && property.Initializer is not null)
            {
                if (property.Modifiers.Any(SyntaxKind.StaticKeyword))
                {
                    inStaticInitializerOrStaticConstructor = true;
                }
                else
                {
                    inConstructorOrInstanceInitializer = true;
                }
            }
        }

        site = new HttpClientCreationSite(
            containingType,
            inLoopBody,
            inConstructorOrInstanceInitializer,
            inStaticInitializerOrStaticConstructor
        );
        return true;
    }

    /// <summary>
    /// Proof leg A-L3. Reports what happens to the created instance.
    /// <para>
    /// A <c>using</c> declaration counts as non-escaping and therefore reportable: disposing the
    /// client is precisely what strands its socket in TIME_WAIT, so it is the defect rather than the
    /// remedy.
    /// </para>
    /// <para>
    /// Assignment into a field or property is reported separately from a genuine escape, because a
    /// member-held client is one per process for a singleton owner but one per resolution for a
    /// transient one — the same syntax, opposite verdicts. Anything else unmodelled is treated as an
    /// escape and stays silent.
    /// </para>
    /// </summary>
    public static HttpClientCreationEscape ClassifyEscape(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        SyntaxNode current = creation;
        var parent = current.Parent;

        // Unwrap the shapes that do not change what happens to the value.
        while (
            parent
                is ParenthesizedExpressionSyntax
                    or CastExpressionSyntax
                    or AwaitExpressionSyntax
                    or PostfixUnaryExpressionSyntax
        )
        {
            current = parent;
            parent = current.Parent;
        }

        switch (parent)
        {
            // new HttpClient(); -- the value is dropped on the floor.
            case ExpressionStatementSyntax:
                return HttpClientCreationEscape.None;

            // new HttpClient().GetAsync(...) -- consumed and abandoned in place.
            case MemberAccessExpressionSyntax memberAccess when memberAccess.Expression == current:
            case ConditionalAccessExpressionSyntax conditionalAccess
                when conditionalAccess.Expression == current:
                return HttpClientCreationEscape.None;

            // using (new HttpClient()) -- the expression form; the declaration form is an
            // EqualsValueClause and is handled below.
            case UsingStatementSyntax usingStatement when usingStatement.Expression == current:
                return HttpClientCreationEscape.None;

            case EqualsValueClauseSyntax equalsValue:
                switch (equalsValue.Parent)
                {
                    // var c = new HttpClient(); and using var c = new HttpClient();
                    case VariableDeclaratorSyntax declarator
                        when declarator.Parent?.Parent
                            is LocalDeclarationStatementSyntax
                                or UsingStatementSyntax:
                        // The declaration alone does not settle it: a local that is later returned or
                        // handed to another call escapes just as plainly as `return new HttpClient()`,
                        // which is silent. Judging from the declaration only would report the two
                        // spellings inconsistently.
                        return LocalEscapesAfterDeclaration(declarator, semanticModel, cancellationToken)
                            ? HttpClientCreationEscape.Escapes
                            : HttpClientCreationEscape.None;

                    // A field or property initializer holds the instance on the instance.
                    case VariableDeclaratorSyntax:
                    case PropertyDeclarationSyntax:
                        return HttpClientCreationEscape.RetainedInMember;

                    default:
                        return HttpClientCreationEscape.Escapes;
                }

            case AssignmentExpressionSyntax assignment when assignment.Right == current:
                return AssignmentTargetIsInstanceMember(
                    assignment.Left,
                    semanticModel,
                    cancellationToken
                )
                    ? HttpClientCreationEscape.RetainedInMember
                    : HttpClientCreationEscape.Escapes;

            default:
                // Return, argument position, lambda body, out/ref, collection element, throw, and
                // anything unmodelled. Argument position in particular is what keeps
                // services.AddSingleton(new HttpClient()) out of this tier -- the registration tier
                // reports it instead, so one construction never yields two diagnostics.
                return HttpClientCreationEscape.Escapes;
        }
    }

    /// <summary>
    /// Reports whether a local holding a freshly created client is later returned, passed to another
    /// call, or assigned onward — any of which hands ownership somewhere this analyzer cannot follow.
    /// A local that is only used through its own members (the ordinary request-scoped shape) does not
    /// escape.
    /// </summary>
    private static bool LocalEscapesAfterDeclaration(
        VariableDeclaratorSyntax declarator,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        if (
            semanticModel.GetDeclaredSymbol(declarator, cancellationToken)
            is not ILocalSymbol local
        )
        {
            return true;
        }

        var body = declarator.FirstAncestorOrSelf<SyntaxNode>(ExecutableSyntaxHelper.IsExecutableBoundary);
        if (body is null)
        {
            return true;
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
                !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    local
                )
            )
            {
                continue;
            }

            switch (identifier.Parent)
            {
                // c.GetStringAsync(...) -- used in place, not handed on.
                case MemberAccessExpressionSyntax memberAccess
                    when memberAccess.Expression == identifier:
                case ConditionalAccessExpressionSyntax conditional
                    when conditional.Expression == identifier:
                    continue;

                // return c; / Store(c); / _field = c; / new Wrapper(c) ...
                default:
                    return true;
            }
        }

        return false;
    }

    private static bool AssignmentTargetIsInstanceMember(
        ExpressionSyntax target,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        var symbol = semanticModel.GetSymbolInfo(target, cancellationToken).Symbol;
        return symbol is IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false };
    }

    /// <summary>
    /// Proof leg A-L4. Reports whether the creation is nested inside another socket-owning creation,
    /// as in <c>new HttpClient(new SocketsHttpHandler())</c>. Only the outermost site is reported so
    /// one leak yields one diagnostic.
    /// </summary>
    public static bool IsNestedInsideMatchingCreation(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        System.Threading.CancellationToken cancellationToken
    )
    {
        for (var node = creation.Parent; node is not null; node = node.Parent)
        {
            if (ExecutableSyntaxHelper.IsExecutableBoundary(node) || node is TypeDeclarationSyntax)
            {
                return false;
            }

            if (
                node is BaseObjectCreationExpressionSyntax outer
                && TryClassifyCreation(
                    outer,
                    semanticModel,
                    wellKnownTypes,
                    cancellationToken,
                    out _
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Proof leg A-L5. Reports whether the construction hands off, or declines, ownership of the
    /// socket pool: an externally supplied handler owns the pool, and <c>disposeHandler: false</c>
    /// says the handler outlives this client, making the client a cheap wrapper rather than a leak.
    /// <para>
    /// Arguments are bound through the parameter symbol rather than by position, so named and
    /// reordered arguments are handled correctly. A non-constant <c>disposeHandler</c> is treated as
    /// unknown and therefore silencing.
    /// </para>
    /// </summary>
    public static bool HasExternallyOwnedOrRetainedHandler(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes,
        System.Threading.CancellationToken cancellationToken
    )
    {
        if (creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        if (
            semanticModel.GetSymbolInfo(creation, cancellationToken).Symbol
            is not IMethodSymbol constructor
        )
        {
            // The constructor could not be bound, so the handler argument cannot be identified.
            return true;
        }

        foreach (var argument in creation.ArgumentList.Arguments)
        {
            var parameter = ResolveParameter(argument, constructor, creation.ArgumentList);
            if (parameter is null)
            {
                return true;
            }

            if (wellKnownTypes.DerivesFromHttpMessageHandler(parameter.Type))
            {
                var handlerExpression = Unwrap(argument.Expression);
                if (
                    handlerExpression is not BaseObjectCreationExpressionSyntax nested
                    || !TryClassifyCreation(
                        nested,
                        semanticModel,
                        wellKnownTypes,
                        cancellationToken,
                        out _
                    )
                )
                {
                    // The handler came from somewhere else, so it owns the pool.
                    return true;
                }

                continue;
            }

            if (parameter.Type.SpecialType == SpecialType.System_Boolean)
            {
                var constant = semanticModel.GetConstantValue(
                    argument.Expression,
                    cancellationToken
                );
                if (!constant.HasValue || constant.Value is not bool disposeHandler)
                {
                    return true;
                }

                if (!disposeHandler)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IParameterSymbol? ResolveParameter(
        ArgumentSyntax argument,
        IMethodSymbol constructor,
        BaseArgumentListSyntax argumentList
    )
    {
        if (argument.NameColon?.Name.Identifier.ValueText is { } name)
        {
            foreach (var parameter in constructor.Parameters)
            {
                if (parameter.Name == name)
                {
                    return parameter;
                }
            }

            return null;
        }

        var ordinal = argumentList.Arguments.IndexOf(argument);
        if (ordinal < 0 || ordinal >= constructor.Parameters.Length)
        {
            return null;
        }

        return constructor.Parameters[ordinal];
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
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
                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    current = postfix.Operand;
                    continue;
                default:
                    return current;
            }
        }
    }
}
