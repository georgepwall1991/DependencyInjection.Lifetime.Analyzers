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
/// DI029: two opposite lifetime errors on the same connection pool. A registered service constructs
/// <see cref="System.Net.Http.HttpClient"/> or one of its pooling handlers on a per-invocation path,
/// exhausting sockets under load; or an <c>HttpClient</c> is handed to the container as a singleton
/// or held in a static member, pinning a handler that then never rotates and never re-resolves DNS.
/// <para>
/// The boundary with DI008 is the registration shape: DI008 owns transient disposable registrations,
/// while DI029 owns singleton registrations and exact type-backed scoped <c>HttpClient</c>
/// self-bindings. Factory-backed and derived scoped registrations remain outside this rule.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI029_HttpClientLifetimeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.HttpClientSocketExhaustion,
            DiagnosticDescriptors.HttpClientStaleDnsRegistration,
            DiagnosticDescriptors.HttpClientScopedRegistration,
            DiagnosticDescriptors.HttpClientStaleDnsStaticMember
        );

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var wellKnownTypes = WellKnownTypes.Create(compilationContext.Compilation);
            if (wellKnownTypes?.HttpClient is null)
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

            // A rotating alternative must exist for the per-invocation and static-member tiers to be
            // actionable; without Microsoft.Extensions.Http there is nothing to recommend. The
            // registration tier is deliberately not gated: an explicit AddSingleton<HttpClient> is a
            // process-lifetime handler whatever else the project references.
            var hasFactory = wellKnownTypes.IHttpClientFactory is not null;

            var creations = new ConcurrentBag<CreationRecord>();
            var invocationObservations =
                new ConcurrentQueue<ServiceCollectionReachabilityAnalyzer.InvocationObservation>();

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
                    registrationCollector.AnalyzeInvocation(
                        invocation,
                        syntaxContext.SemanticModel
                    );

                    if (
                        ServiceCollectionReachabilityAnalyzer.IsPotentialServiceCollectionWrapperInvocation(
                            invocation,
                            syntaxContext.SemanticModel
                        )
                    )
                    {
                        invocationObservations.Enqueue(
                            new ServiceCollectionReachabilityAnalyzer.InvocationObservation(
                                invocation,
                                syntaxContext.SemanticModel
                            )
                        );
                    }
                },
                SyntaxKind.InvocationExpression
            );

            if (hasFactory)
            {
                compilationContext.RegisterSyntaxNodeAction(
                    syntaxContext => CollectCreation(syntaxContext, wellKnownTypes, creations),
                    SyntaxKind.ObjectCreationExpression,
                    SyntaxKind.ImplicitObjectCreationExpression
                );

                compilationContext.RegisterSymbolAction(
                    symbolContext => AnalyzeStaticMember(symbolContext, wellKnownTypes),
                    SymbolKind.Field,
                    SymbolKind.Property
                );
            }

            compilationContext.RegisterCompilationEndAction(endContext =>
            {
                ReportSocketExhaustion(endContext, registrationCollector, creations);
                ReportContainerRegistrations(
                    endContext,
                    registrationCollector,
                    wellKnownTypes,
                    invocationObservations
                );
            });
        });
    }

    // ----------------------------------------------------------------
    // Tier A -- socket exhaustion
    // ----------------------------------------------------------------

    private sealed class CreationRecord
    {
        public CreationRecord(
            INamedTypeSymbol containingType,
            string createdTypeName,
            bool inLoopBody,
            bool inConstructorOrInstanceInitializer,
            HttpClientCreationEscape escape,
            Location location
        )
        {
            ContainingType = containingType;
            CreatedTypeName = createdTypeName;
            InLoopBody = inLoopBody;
            InConstructorOrInstanceInitializer = inConstructorOrInstanceInitializer;
            Escape = escape;
            Location = location;
        }

        public INamedTypeSymbol ContainingType { get; }
        public string CreatedTypeName { get; }
        public bool InLoopBody { get; }
        public bool InConstructorOrInstanceInitializer { get; }
        public HttpClientCreationEscape Escape { get; }
        public Location Location { get; }
    }

    private static void CollectCreation(
        SyntaxNodeAnalysisContext context,
        WellKnownTypes wellKnownTypes,
        ConcurrentBag<CreationRecord> records
    )
    {
        var creation = (BaseObjectCreationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var cancellationToken = context.CancellationToken;

        // A-L1: exact created type.
        if (
            !HttpClientCreationClassification.TryClassifyCreation(
                creation,
                semanticModel,
                wellKnownTypes,
                cancellationToken,
                out var creationKind
            )
        )
        {
            return;
        }

        // Only a client consumes a socket. Constructing a handler opens no connection until something
        // sends through it, and proving that it reaches a request path needs interprocedural analysis
        // this rule does not do -- so a bare handler construction stays silent. The leak shape that
        // matters, new HttpClient(new SocketsHttpHandler()), is still reported through the client.
        if (creationKind != HttpClientCreationKind.Client)
        {
            return;
        }

        // A-L4: one leak, one diagnostic.
        if (
            HttpClientCreationClassification.IsNestedInsideMatchingCreation(
                creation,
                semanticModel,
                wellKnownTypes,
                cancellationToken
            )
        )
        {
            return;
        }

        // A-L5: an externally supplied handler owns the pool.
        if (
            HttpClientCreationClassification.HasExternallyOwnedOrRetainedHandler(
                creation,
                semanticModel,
                wellKnownTypes,
                cancellationToken
            )
        )
        {
            return;
        }

        // A-L3: an escaped instance has an owner this analyzer cannot see.
        var escape = HttpClientCreationClassification.ClassifyEscape(
            creation,
            semanticModel,
            cancellationToken
        );
        if (escape == HttpClientCreationEscape.Escapes)
        {
            return;
        }

        // A-L2 (location half).
        if (
            !HttpClientCreationClassification.TryClassifySite(
                creation,
                semanticModel,
                cancellationToken,
                out var site
            )
        )
        {
            return;
        }

        // A static initializer runs once per process, so it is never a socket leak. The
        // static-member tier reports that shape on the declaration instead.
        if (site.InStaticInitializerOrStaticConstructor)
        {
            return;
        }

        var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        records.Add(
            new CreationRecord(
                site.ContainingType,
                createdType?.Name ?? "HttpClient",
                site.InLoopBody,
                site.InConstructorOrInstanceInitializer,
                escape,
                creation.GetLocation()
            )
        );
    }

    private static void ReportSocketExhaustion(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        ConcurrentBag<CreationRecord> records
    )
    {
        if (records.IsEmpty)
        {
            return;
        }

        var lifetimesByImplementation = BuildRegisteredImplementationLifetimeMap(
            registrationCollector.AllRegistrations
        );
        if (lifetimesByImplementation.Count == 0)
        {
            // No registrations at all: tests, libraries, and console entry points land here and stay
            // silent, because nothing proves the construction sits on a repeated path.
            return;
        }

        foreach (
            var record in records
                .OrderBy(r => r.Location.SourceSpan.Start)
                .ThenBy(r => r.Location.SourceTree?.FilePath, System.StringComparer.Ordinal)
        )
        {
            if (!lifetimesByImplementation.TryGetValue(record.ContainingType, out var lifetime))
            {
                continue;
            }

            if (!IsReportableSite(record, lifetime))
            {
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.HttpClientSocketExhaustion,
                    record.Location,
                    record.ContainingType.Name,
                    lifetime.ToString(),
                    record.CreatedTypeName
                )
            );
        }
    }

    /// <summary>
    /// Decides whether a construction site actually repeats, which needs both where the site sits and
    /// how long its owner lives.
    /// <para>
    /// A client stored in a member is one pool shared for the owner's lifetime — correct for a
    /// singleton or scoped owner, and one fresh socket per resolution for a transient one. That is
    /// the same syntax with opposite verdicts, which is why the escape kind cannot be judged at the
    /// construction site alone.
    /// </para>
    /// </summary>
    private static bool IsReportableSite(CreationRecord record, ServiceLifetime lifetime)
    {
        if (record.Escape == HttpClientCreationEscape.RetainedInMember)
        {
            // A loop that repeatedly overwrites a member burns a socket per iteration whatever the
            // owner's lifetime; otherwise only a transient owner reconstructs the member.
            return record.InLoopBody || lifetime == ServiceLifetime.Transient;
        }

        // A loop body is per-iteration regardless of lifetime. Outside a loop, a constructor or
        // instance initializer only repeats for a transient service; for a scoped or singleton
        // service it runs once per scope or once per process, which is an accepted false negative
        // rather than a socket leak.
        return record.InLoopBody
            || !record.InConstructorOrInstanceInitializer
            || lifetime == ServiceLifetime.Transient;
    }

    /// <summary>
    /// Maps every registered implementation type, and each of its base types, to the lifetime it is
    /// activated under. Where a type is registered more than once the <em>shortest-lived</em>
    /// registration wins, because that is the one that constructs most often.
    /// <para>
    /// Deliberately not shared with DI027's subscriber rank map: that one takes the most conservative
    /// (longest-lived) reading for a leak proof, this one takes the opposite for a churn proof.
    /// Merging them would give one of the two rules the wrong answer.
    /// </para>
    /// </summary>
    private static Dictionary<
        INamedTypeSymbol,
        ServiceLifetime
    > BuildRegisteredImplementationLifetimeMap(IEnumerable<ServiceRegistration> registrations)
    {
        var map = new Dictionary<INamedTypeSymbol, ServiceLifetime>(SymbolEqualityComparer.Default);

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
                if (map.TryGetValue(current, out var existing))
                {
                    // Higher enum value is shorter-lived: Singleton = 0, Scoped = 1, Transient = 2.
                    if (registration.Lifetime > existing)
                    {
                        map[current] = registration.Lifetime;
                    }
                }
                else
                {
                    map[current] = registration.Lifetime;
                }
            }
        }

        return map;
    }

    // ----------------------------------------------------------------
    // Tier B leg 1 -- container registrations
    // ----------------------------------------------------------------

    private static void ReportContainerRegistrations(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector,
        WellKnownTypes wellKnownTypes,
        ConcurrentQueue<ServiceCollectionReachabilityAnalyzer.InvocationObservation> observations
    )
    {
        // The deduplicated winner view, not AllRegistrations: a TryAddSingleton shadowed by an
        // earlier AddSingleton must yield one diagnostic, and the duplicate-registration story
        // belongs to DI012/DI012b.
        var candidates = registrationCollector
            .Registrations.Where(registration =>
                wellKnownTypes.IsHttpClient(registration.ServiceType)
                && (
                    registration.Lifetime == ServiceLifetime.Singleton
                    || IsDirectScopedHttpClientRegistration(registration, wellKnownTypes)
                )
            )
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var reachability = ServiceCollectionReachabilityAnalyzer.Create(
            context.Compilation,
            observations.ToImmutableArray(),
            registrationCollector.AllRegistrations
        );

        foreach (var registration in candidates.OrderBy(r => r.Location.SourceSpan.Start))
        {
            // A handler configured with PooledConnectionLifetime replaces its connections on that
            // interval and re-resolves DNS with them, which is the documented way to run a long-lived
            // client without the factory. Reporting it would be wrong.
            if (ConfiguresConnectionRotation(registration.FactoryExpression))
            {
                continue;
            }

            if (
                !reachability.IsReachable(registration.Location)
                || reachability.HasOpaquePredecessor(registration.Location)
            )
            {
                continue;
            }

            var keySuffix =
                registration.IsKeyed && registration.KeyLiteral is { } key ? $" with key {key}"
                : registration.IsKeyed ? " with a service key"
                : string.Empty;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    registration.Lifetime == ServiceLifetime.Scoped
                        ? DiagnosticDescriptors.HttpClientScopedRegistration
                        : DiagnosticDescriptors.HttpClientStaleDnsRegistration,
                    registration.Location,
                    keySuffix
                )
            );
        }
    }

    private static bool IsDirectScopedHttpClientRegistration(
        ServiceRegistration registration,
        WellKnownTypes wellKnownTypes
    ) =>
        wellKnownTypes.IHttpClientFactory is not null
        && registration.Lifetime == ServiceLifetime.Scoped
        && registration.ImplementationType is { } implementationType
        && wellKnownTypes.IsHttpClient(implementationType)
        && registration.FactoryExpression is null
        && !registration.HasImplementationInstance;


    // ----------------------------------------------------------------
    // Tier B leg 2 -- static member
    // ----------------------------------------------------------------

    /// <summary>
    /// Reports whether a property stores its value rather than computing it. An auto-property (no
    /// accessor bodies) or one with an initializer retains the instance; an expression-bodied or
    /// explicitly-implemented getter produces a value on each access and retains nothing.
    /// </summary>
    /// <summary>
    /// Reports whether an expression provably configures connection rotation, which is what makes a
    /// long-lived client acceptable: <c>SocketsHttpHandler.PooledConnectionLifetime</c> retires pooled
    /// connections on an interval, so DNS is re-resolved without rotating the client itself. An idle
    /// timeout does not qualify -- a connection under continuous load never goes idle.
    /// </summary>
    private static bool ConfiguresConnectionRotation(SyntaxNode? expression)
    {
        if (expression is null)
        {
            return false;
        }

        foreach (var node in expression.DescendantNodesAndSelf())
        {
            var name = node switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                _ => null,
            };

            // Only PooledConnectionLifetime establishes a maximum connection age. An idle timeout
            // never elapses on a continuously active connection, so it proves nothing about DNS.
            if (name is not "PooledConnectionLifetime")
            {
                continue;
            }

            // ...and only a finite one. Timeout.InfiniteTimeSpan is precisely the non-rotating
            // default, so assigning it explicitly proves the opposite of rotation.
            var assigned = node.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
            if (assigned is not null && AssignsInfiniteTimeSpan(assigned.Right))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Recognizes the infinite timeout, which disables rotation rather than configuring it.
    /// </summary>
    private static bool AssignsInfiniteTimeSpan(ExpressionSyntax value)
    {
        foreach (var node in value.DescendantNodesAndSelf())
        {
            var name = node switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => null,
            };

            if (name is "InfiniteTimeSpan" or "MaxValue" or "Infinite")
            {
                return true;
            }
        }

        return false;
    }

    private static bool PropertyRetainsValue(IPropertySymbol property)
    {
        if (property.DeclaringSyntaxReferences.IsEmpty)
        {
            return false;
        }

        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not PropertyDeclarationSyntax declaration)
            {
                return false;
            }

            if (declaration.ExpressionBody is not null)
            {
                return false;
            }

            if (declaration.Initializer is not null)
            {
                continue;
            }

            var accessors = declaration.AccessorList?.Accessors;
            if (accessors is null)
            {
                return false;
            }

            foreach (var accessor in accessors)
            {
                if (accessor.Body is not null || accessor.ExpressionBody is not null)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void AnalyzeStaticMember(
        SymbolAnalysisContext context,
        WellKnownTypes wellKnownTypes
    )
    {
        var symbol = context.Symbol;
        if (!symbol.IsStatic || symbol.IsImplicitlyDeclared)
        {
            return;
        }

        var (memberType, memberKind) = symbol switch
        {
            IFieldSymbol field => ((ITypeSymbol?)field.Type, "field"),
            // Only a property that actually stores a client can pin a handler. A computed getter such
            // as `static HttpClient Client => factory.CreateClient();` hands back a fresh client per
            // access and rotates exactly as intended, so reporting it would be wrong.
            IPropertySymbol property when PropertyRetainsValue(property) => (property.Type, "property"),
            _ => (null, string.Empty),
        };

        // Exact HttpClient only. A Lazy<HttpClient>, Func<HttpClient>, or dictionary-of-clients
        // wrapper is an accepted false negative rather than a guess about the wrapper's semantics.
        if (!wellKnownTypes.IsHttpClient(memberType))
        {
            return;
        }

        var location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource);
        if (location is null)
        {
            return;
        }

        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            if (ConfiguresConnectionRotation(reference.GetSyntax(context.CancellationToken)))
            {
                return;
            }
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                DiagnosticDescriptors.HttpClientStaleDnsStaticMember,
                location,
                symbol.Name,
                memberKind
            )
        );
    }
}
