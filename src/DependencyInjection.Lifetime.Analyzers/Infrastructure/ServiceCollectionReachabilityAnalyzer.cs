using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Computes which service registrations are reachable through actual wrapper-call paths and
/// whether an earlier opaque wrapper could have contributed registrations before them.
/// </summary>
internal sealed class ServiceCollectionReachabilityAnalyzer
{
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _serviceCollectionType;
    private readonly INamedTypeSymbol? _serviceCollectionServiceExtensionsType;
    private readonly INamedTypeSymbol? _serviceCollectionDescriptorExtensionsType;
    private readonly ImmutableArray<InvocationObservation> _observations;
    private readonly HashSet<LocationKey> _registrationLocations;

    private readonly Dictionary<IMethodSymbol, ImmutableArray<RootMethodStep>> _rootStepsByMethod;
    private readonly Dictionary<IMethodSymbol, ImmutableArray<WrapperStep>> _wrapperStepsByMethod;
    private readonly HashSet<LocationKey> _reachableRegistrations = new HashSet<LocationKey>();
    private readonly HashSet<LocationKey> _opaquePredecessorRegistrations = new HashSet<LocationKey>();

    private ServiceCollectionReachabilityAnalyzer(
        Compilation compilation,
        ImmutableArray<InvocationObservation> observations,
        IEnumerable<ServiceRegistration> registrations)
    {
        _compilation = compilation;
        _serviceCollectionType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection");
        _serviceCollectionServiceExtensionsType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions");
        _serviceCollectionDescriptorExtensionsType = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions") ??
            compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.ServiceCollectionDescriptorExtensions");
        _observations = observations;
        _registrationLocations = new HashSet<LocationKey>();

        foreach (var registration in registrations)
        {
            var key = LocationKey.Create(registration.Location);
            if (key.HasValue)
            {
                _registrationLocations.Add(key.Value);
            }
        }

        _rootStepsByMethod = new Dictionary<IMethodSymbol, ImmutableArray<RootMethodStep>>(SymbolEqualityComparer.Default);
        _wrapperStepsByMethod = new Dictionary<IMethodSymbol, ImmutableArray<WrapperStep>>(SymbolEqualityComparer.Default);
    }

    public static ServiceCollectionReachabilityAnalyzer Create(
        Compilation compilation,
        ImmutableArray<InvocationObservation> observations,
        IEnumerable<ServiceRegistration> registrations)
    {
        var analyzer = new ServiceCollectionReachabilityAnalyzer(compilation, observations, registrations);
        analyzer.Build();
        return analyzer;
    }

    public bool IsReachable(Location location)
    {
        var key = LocationKey.Create(location);
        return key.HasValue && _reachableRegistrations.Contains(key.Value);
    }

    public bool HasOpaquePredecessor(Location location)
    {
        var key = LocationKey.Create(location);
        return key.HasValue && _opaquePredecessorRegistrations.Contains(key.Value);
    }

    private void Build()
    {
        if (_serviceCollectionType is null)
        {
            return;
        }

        var wrapperMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var groupedObservations = new Dictionary<IMethodSymbol, List<ObservedInvocation>>(SymbolEqualityComparer.Default);

        foreach (var observation in _observations)
        {
            if (!TryCreateObservedInvocation(observation, out var observed))
            {
                continue;
            }

            if (observed.IsContainedInWrapper)
            {
                wrapperMethods.Add(observed.ContainingMethod);
            }

            if (!groupedObservations.TryGetValue(observed.ContainingMethod, out var list))
            {
                list = new List<ObservedInvocation>();
                groupedObservations.Add(observed.ContainingMethod, list);
            }

            list.Add(observed);
        }

        foreach (var entry in groupedObservations)
        {
            var method = entry.Key;
            var invocations = entry.Value;
            invocations.Sort(static (left, right) => left.SpanStart.CompareTo(right.SpanStart));

            if (wrapperMethods.Contains(method))
            {
                _wrapperStepsByMethod[method] = invocations
                    .Select(static invocation => invocation.ToWrapperStep())
                    .Where(static step => step is not null)
                    .Cast<WrapperStep>()
                    .ToImmutableArray();
            }
            else
            {
                _rootStepsByMethod[method] = invocations
                    .Select(static invocation => invocation.ToRootStep())
                    .Where(static step => step is not null)
                    .Cast<RootMethodStep>()
                    .ToImmutableArray();
            }
        }

        foreach (var entry in _rootStepsByMethod)
        {
            TraverseRootMethod(entry.Value);
        }
    }

    private bool TryCreateObservedInvocation(
        InvocationObservation observation,
        out ObservedInvocation observed)
    {
        observed = default;

        if (!TryGetInvocationTarget(observation.Invocation, observation.SemanticModel, out var targetMethod))
        {
            return false;
        }

        var containingMethod = observation.SemanticModel.GetEnclosingSymbol(observation.Invocation.SpanStart) as IMethodSymbol;
        if (containingMethod is null)
        {
            return false;
        }

        containingMethod = NormalizeMethod(containingMethod);
        if (containingMethod is null)
        {
            return false;
        }

        var locationKey = LocationKey.Create(observation.Invocation.GetLocation());
        if (!locationKey.HasValue)
        {
            return false;
        }

        var invocationLocation = locationKey.Value;

        var normalizedTarget = NormalizeMethod(targetMethod);
        var isWrapperMethod = IsSourceServiceCollectionWrapperMethod(containingMethod);
        var flowKey = isWrapperMethod
            ? null
            : GetRootFlowKey(observation.Invocation, observation.SemanticModel);

        if (!isWrapperMethod && flowKey is null)
        {
            return false;
        }

        if (_registrationLocations.Contains(invocationLocation))
        {
            observed = ObservedInvocation.DirectRegistration(
                containingMethod,
                invocationLocation,
                observation.Invocation.SpanStart,
                flowKey,
                isWrapperMethod);
            return true;
        }

        if (normalizedTarget is not null && IsSourceServiceCollectionWrapperMethod(normalizedTarget))
        {
            observed = ObservedInvocation.WrapperCall(
                containingMethod,
                normalizedTarget,
                observation.Invocation.SpanStart,
                flowKey,
                isWrapperMethod);
            return true;
        }

        if (!IsPossibleServiceCollectionFlowInvocation(
                observation.Invocation,
                observation.SemanticModel))
        {
            return false;
        }

        observed = ObservedInvocation.OpaqueWrapper(
            containingMethod,
            observation.Invocation.SpanStart,
            flowKey,
            isWrapperMethod);
        return true;
    }

    private void TraverseRootMethod(ImmutableArray<RootMethodStep> steps)
    {
        var opaqueByFlow = new Dictionary<string, bool>();

        foreach (var step in steps)
        {
            switch (step.Kind)
            {
                case StepKind.DirectRegistration:
                    _reachableRegistrations.Add(step.Location);
                    if (opaqueByFlow.TryGetValue(step.FlowKey!, out var hasOpaquePredecessor) &&
                        hasOpaquePredecessor)
                    {
                        _opaquePredecessorRegistrations.Add(step.Location);
                    }

                    break;

                case StepKind.WrapperCall:
                    var flowKey = step.FlowKey!;
                    var incomingOpaque = opaqueByFlow.TryGetValue(flowKey, out var currentOpaque) && currentOpaque;
                    var outgoingOpaque = TraverseWrapper(
                        step.TargetWrapper!,
                        incomingOpaque,
                        new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
                    opaqueByFlow[flowKey] = outgoingOpaque;
                    break;

                case StepKind.OpaqueWrapper:
                    opaqueByFlow[step.FlowKey!] = true;
                    break;
            }
        }
    }

    private bool TraverseWrapper(
        IMethodSymbol wrapperMethod,
        bool opaqueSoFar,
        HashSet<IMethodSymbol> wrapperStack)
    {
        if (!wrapperStack.Add(wrapperMethod))
        {
            return true;
        }

        if (!_wrapperStepsByMethod.TryGetValue(wrapperMethod, out var steps))
        {
            wrapperStack.Remove(wrapperMethod);
            return opaqueSoFar;
        }

        var currentOpaque = opaqueSoFar;

        foreach (var step in steps)
        {
            switch (step.Kind)
            {
                case StepKind.DirectRegistration:
                    _reachableRegistrations.Add(step.Location);
                    if (currentOpaque)
                    {
                        _opaquePredecessorRegistrations.Add(step.Location);
                    }

                    break;

                case StepKind.WrapperCall:
                    currentOpaque = TraverseWrapper(step.TargetWrapper!, currentOpaque, wrapperStack);
                    break;

                case StepKind.OpaqueWrapper:
                    currentOpaque = true;
                    break;
            }
        }

        wrapperStack.Remove(wrapperMethod);
        return currentOpaque;
    }

    internal static bool TryGetInvocationTarget(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out IMethodSymbol method)
    {
        method = null!;

        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        method = symbolInfo.Symbol as IMethodSymbol ??
                 symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault()!;

        return method is not null;
    }

    private string? GetRootFlowKey(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        return GetServiceCollectionReceiverKey(invocation, semanticModel);
    }

    private bool IsCustomServiceCollectionExtension(IMethodSymbol method)
    {
        var originalMethod = method.ReducedFrom ?? method;
        if (!originalMethod.IsExtensionMethod ||
            originalMethod.Parameters.Length == 0 ||
            !IsServiceCollectionType(originalMethod.Parameters[0].Type))
        {
            return false;
        }

        return !IsKnownServiceCollectionExtensionsType(originalMethod.ContainingType);
    }

    private bool IsPossibleServiceCollectionFlowInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (!TryGetServiceCollectionReceiverExpression(invocation, semanticModel, out var receiver))
        {
            return false;
        }

        var type = semanticModel.GetTypeInfo(receiver).Type ?? semanticModel.GetTypeInfo(receiver).ConvertedType;
        return type is not null && IsServiceCollectionFlowType(type);
    }

    private bool IsServiceCollectionType(ITypeSymbol type)
    {
        if (_serviceCollectionType is not null &&
            SymbolEqualityComparer.Default.Equals(type, _serviceCollectionType))
        {
            return true;
        }

        return type.Name == "IServiceCollection" &&
               type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }

    private bool IsServiceCollectionFlowType(ITypeSymbol type)
    {
        if (IsServiceCollectionType(type))
        {
            return true;
        }

        if (_serviceCollectionType is null)
        {
            return false;
        }

        return type.AllInterfaces.Any(@interface =>
            SymbolEqualityComparer.Default.Equals(@interface, _serviceCollectionType) ||
            IsServiceCollectionType(@interface));
    }

    private bool IsSourceServiceCollectionWrapperMethod(IMethodSymbol method)
    {
        return method.DeclaringSyntaxReferences.Length > 0 &&
               IsCustomServiceCollectionExtension(method);
    }

    private bool IsKnownServiceCollectionExtensionsType(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(type, _serviceCollectionServiceExtensionsType) ||
            SymbolEqualityComparer.Default.Equals(type, _serviceCollectionDescriptorExtensionsType))
        {
            return true;
        }

        var fullName = type.ToDisplayString();
        return IsKnownServiceCollectionExtensionsTypeByName(fullName);
    }

    private static IMethodSymbol? NormalizeMethod(IMethodSymbol? method)
    {
        if (method is null)
        {
            return null;
        }

        return NormalizeContainingMethod(method);
    }

    internal static IMethodSymbol? NormalizeContainingMethod(IMethodSymbol? method)
    {
        if (method is null)
        {
            return null;
        }

        var original = method.ReducedFrom ?? method;
        return original.ConstructedFrom;
    }

    internal static bool IsSourceDefinedCustomServiceCollectionWrapper(IMethodSymbol method)
    {
        var originalMethod = method.ReducedFrom ?? method;
        return originalMethod.DeclaringSyntaxReferences.Length > 0 &&
               IsCustomServiceCollectionExtensionByName(originalMethod);
    }

    internal static bool IsCustomServiceCollectionExtensionByName(IMethodSymbol method)
    {
        var originalMethod = method.ReducedFrom ?? method;
        return originalMethod.IsExtensionMethod &&
               originalMethod.Parameters.Length > 0 &&
               IsServiceCollectionTypeByName(originalMethod.Parameters[0].Type) &&
               !IsKnownServiceCollectionExtensionsTypeByName(originalMethod.ContainingType?.ToDisplayString());
    }

    internal static bool IsServiceCollectionTypeByName(ITypeSymbol type)
    {
        return type.Name == "IServiceCollection" &&
               type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }

    internal static bool IsKnownServiceCollectionExtensionsTypeByName(string? fullName)
    {
        return fullName == "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions" ||
               fullName == "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions" ||
               fullName == "Microsoft.Extensions.DependencyInjection.ServiceCollectionDescriptorExtensions";
    }

    internal static bool IsPotentialServiceCollectionWrapperInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        return TryGetServiceCollectionReceiverExpression(
                   invocation,
                   semanticModel,
                   out var receiver) &&
               IsServiceCollectionReceiver(receiver, semanticModel);
    }

    internal static string? GetServiceCollectionReceiverKey(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (!TryGetServiceCollectionReceiverExpression(invocation, semanticModel, out var receiver) ||
            !IsServiceCollectionReceiver(receiver, semanticModel))
        {
            return null;
        }

        var receiverSymbol = semanticModel.GetSymbolInfo(receiver).Symbol;
        return receiverSymbol is not null ? CreateFlowKey(receiverSymbol) : receiver.ToString();
    }

    internal static bool TryGetServiceCollectionReceiverExpression(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out ExpressionSyntax receiver)
    {
        receiver = null!;

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            receiver = memberAccess.Expression;
            return true;
        }

        if (invocation.Expression is MemberBindingExpressionSyntax &&
            invocation.Parent is ConditionalAccessExpressionSyntax conditionalAccess)
        {
            receiver = conditionalAccess.Expression;
            return true;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        var targetMethod = symbolInfo.Symbol as IMethodSymbol ??
                           symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (targetMethod is null)
        {
            return false;
        }

        var originalMethod = targetMethod.ReducedFrom ?? targetMethod;
        if (originalMethod.IsExtensionMethod && invocation.ArgumentList.Arguments.Count > 0)
        {
            receiver = invocation.ArgumentList.Arguments[0].Expression;
            return true;
        }

        return false;
    }

    internal static bool IsServiceCollectionReceiver(ExpressionSyntax receiver, SemanticModel semanticModel)
    {
        var type = semanticModel.GetTypeInfo(receiver).Type ?? semanticModel.GetTypeInfo(receiver).ConvertedType;
        if (type is null)
        {
            return false;
        }

        if (type.Name == "IServiceCollection" &&
            type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
        {
            return true;
        }

        return type.AllInterfaces.Any(@interface =>
            @interface.Name == "IServiceCollection" &&
            @interface.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection");
    }

    private static string CreateFlowKey(ISymbol symbol)
    {
        var primaryLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        return primaryLocation != null
            ? symbol.Kind + ":" + symbol.Name + ":" + primaryLocation.SourceTree?.FilePath + ":" + primaryLocation.SourceSpan.Start
            : symbol.Kind + ":" + symbol.Name + ":" + symbol.ContainingType?.ToDisplayString();
    }

    internal readonly struct InvocationObservation
    {
        public InvocationObservation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            Invocation = invocation;
            SemanticModel = semanticModel;
        }

        public InvocationExpressionSyntax Invocation { get; }

        public SemanticModel SemanticModel { get; }
    }

    internal readonly struct LocationKey : System.IEquatable<LocationKey>
    {
        public LocationKey(SyntaxTree tree, TextSpan span)
        {
            Tree = tree;
            Span = span;
        }

        public SyntaxTree Tree { get; }

        public TextSpan Span { get; }

        public static LocationKey? Create(Location location)
        {
            return location.SourceTree is null
                ? null
                : new LocationKey(location.SourceTree, location.SourceSpan);
        }

        public bool Equals(LocationKey other)
        {
            return ReferenceEquals(Tree, other.Tree) && Span.Equals(other.Span);
        }

        public override bool Equals(object? obj)
        {
            return obj is LocationKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Tree != null ? Tree.GetHashCode() : 0) * 397) ^ Span.GetHashCode();
            }
        }
    }

    private enum StepKind
    {
        DirectRegistration,
        WrapperCall,
        OpaqueWrapper
    }

    private readonly struct RootMethodStep
    {
        private RootMethodStep(
            StepKind kind,
            LocationKey location,
            string? flowKey,
            IMethodSymbol? targetWrapper)
        {
            Kind = kind;
            Location = location;
            FlowKey = flowKey;
            TargetWrapper = targetWrapper;
        }

        public StepKind Kind { get; }

        public LocationKey Location { get; }

        public string? FlowKey { get; }

        public IMethodSymbol? TargetWrapper { get; }

        public static RootMethodStep DirectRegistration(LocationKey location, string flowKey) =>
            new(StepKind.DirectRegistration, location, flowKey, targetWrapper: null);

        public static RootMethodStep WrapperCall(string flowKey, IMethodSymbol targetWrapper) =>
            new(StepKind.WrapperCall, default, flowKey, targetWrapper);

        public static RootMethodStep OpaqueWrapper(string flowKey) =>
            new(StepKind.OpaqueWrapper, default, flowKey, targetWrapper: null);
    }

    private readonly struct WrapperStep
    {
        private WrapperStep(
            StepKind kind,
            LocationKey location,
            IMethodSymbol? targetWrapper)
        {
            Kind = kind;
            Location = location;
            TargetWrapper = targetWrapper;
        }

        public StepKind Kind { get; }

        public LocationKey Location { get; }

        public IMethodSymbol? TargetWrapper { get; }

        public static WrapperStep DirectRegistration(LocationKey location) =>
            new(StepKind.DirectRegistration, location, targetWrapper: null);

        public static WrapperStep WrapperCall(IMethodSymbol targetWrapper) =>
            new(StepKind.WrapperCall, default, targetWrapper);

        public static WrapperStep OpaqueWrapper() =>
            new(StepKind.OpaqueWrapper, default, targetWrapper: null);
    }

    private readonly struct ObservedInvocation
    {
        private ObservedInvocation(
            IMethodSymbol containingMethod,
            LocationKey? registrationLocation,
            IMethodSymbol? targetWrapper,
            int spanStart,
            string? flowKey,
            StepKind kind,
            bool isContainedInWrapper)
        {
            ContainingMethod = containingMethod;
            RegistrationLocation = registrationLocation;
            TargetWrapper = targetWrapper;
            SpanStart = spanStart;
            FlowKey = flowKey;
            Kind = kind;
            IsContainedInWrapper = isContainedInWrapper;
        }

        public IMethodSymbol ContainingMethod { get; }

        public LocationKey? RegistrationLocation { get; }

        public IMethodSymbol? TargetWrapper { get; }

        public int SpanStart { get; }

        public string? FlowKey { get; }

        public StepKind Kind { get; }

        public bool IsContainedInWrapper { get; }

        public static ObservedInvocation DirectRegistration(
            IMethodSymbol containingMethod,
            LocationKey registrationLocation,
            int spanStart,
            string? flowKey,
            bool isContainedInWrapper) =>
            new(containingMethod, registrationLocation, targetWrapper: null, spanStart, flowKey, StepKind.DirectRegistration, isContainedInWrapper);

        public static ObservedInvocation WrapperCall(
            IMethodSymbol containingMethod,
            IMethodSymbol targetWrapper,
            int spanStart,
            string? flowKey,
            bool isContainedInWrapper) =>
            new(containingMethod, registrationLocation: null, targetWrapper, spanStart, flowKey, StepKind.WrapperCall, isContainedInWrapper);

        public static ObservedInvocation OpaqueWrapper(
            IMethodSymbol containingMethod,
            int spanStart,
            string? flowKey,
            bool isContainedInWrapper) =>
            new(containingMethod, registrationLocation: null, targetWrapper: null, spanStart, flowKey, StepKind.OpaqueWrapper, isContainedInWrapper);

        public RootMethodStep? ToRootStep()
        {
            return Kind switch
            {
                StepKind.DirectRegistration when RegistrationLocation is { } location && FlowKey is not null =>
                    RootMethodStep.DirectRegistration(location, FlowKey),
                StepKind.WrapperCall when FlowKey is not null && TargetWrapper is not null =>
                    RootMethodStep.WrapperCall(FlowKey, TargetWrapper),
                StepKind.OpaqueWrapper when FlowKey is not null =>
                    RootMethodStep.OpaqueWrapper(FlowKey),
                _ => null
            };
        }

        public WrapperStep? ToWrapperStep()
        {
            return Kind switch
            {
                StepKind.DirectRegistration when RegistrationLocation is { } location =>
                    WrapperStep.DirectRegistration(location),
                StepKind.WrapperCall when TargetWrapper is not null =>
                    WrapperStep.WrapperCall(TargetWrapper),
                StepKind.OpaqueWrapper =>
                    WrapperStep.OpaqueWrapper(),
                _ => null
            };
        }
    }
}
