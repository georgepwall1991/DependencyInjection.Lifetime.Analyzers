using System.Collections.Immutable;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Captures the result of resolving a dependency request.
/// </summary>
internal sealed class ResolutionResult
{
    private ResolutionResult(
        bool isResolvable,
        ResolutionConfidence confidence,
        ImmutableArray<MissingDependency> missingDependencies)
    {
        IsResolvable = isResolvable;
        Confidence = confidence;
        MissingDependencies = missingDependencies;
    }

    /// <summary>
    /// Gets whether the dependency should be treated as resolvable.
    /// </summary>
    public bool IsResolvable { get; }

    /// <summary>
    /// Gets the confidence level of this result.
    /// </summary>
    public ResolutionConfidence Confidence { get; }

    /// <summary>
    /// Gets the missing dependencies when the result is not resolvable.
    /// </summary>
    public ImmutableArray<MissingDependency> MissingDependencies { get; }

    public static ResolutionResult Resolvable(ResolutionConfidence confidence) =>
        new(isResolvable: true, confidence, ImmutableArray<MissingDependency>.Empty);

    public static ResolutionResult Missing(ImmutableArray<MissingDependency> missingDependencies) =>
        new(isResolvable: false, ResolutionConfidence.High, missingDependencies);
}
