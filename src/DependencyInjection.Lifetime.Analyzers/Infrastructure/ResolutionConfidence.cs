namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Describes how confidently the analyzer can reason about a dependency path.
/// </summary>
internal enum ResolutionConfidence
{
    /// <summary>
    /// The analyzer has a complete, high-confidence understanding of the path.
    /// </summary>
    High,

    /// <summary>
    /// The analyzer encountered dynamic or opaque behavior and should stay silent.
    /// </summary>
    Unknown
}
