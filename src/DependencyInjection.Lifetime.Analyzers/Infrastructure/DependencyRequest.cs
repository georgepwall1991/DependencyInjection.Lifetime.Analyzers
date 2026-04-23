using Microsoft.CodeAnalysis;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Represents a specific dependency that must be resolved to activate a service.
/// </summary>
internal sealed class DependencyRequest
{
    public DependencyRequest(
        ITypeSymbol type,
        object? key,
        bool isKeyed,
        string? keyLiteral,
        DependencySourceKind sourceKind,
        Location sourceLocation,
        string provenanceStep)
    {
        Type = type;
        Key = key;
        IsKeyed = isKeyed;
        KeyLiteral = keyLiteral;
        SourceKind = sourceKind;
        SourceLocation = sourceLocation;
        ProvenanceStep = provenanceStep;
    }

    /// <summary>
    /// Gets the requested dependency type.
    /// </summary>
    public ITypeSymbol Type { get; }

    /// <summary>
    /// Gets the required key for keyed dependencies.
    /// </summary>
    public object? Key { get; }

    /// <summary>
    /// Gets whether the dependency is keyed.
    /// </summary>
    public bool IsKeyed { get; }

    /// <summary>
    /// Gets a C# literal for the key when it can be safely round-tripped into code.
    /// </summary>
    public string? KeyLiteral { get; }

    /// <summary>
    /// Gets how the dependency was discovered.
    /// </summary>
    public DependencySourceKind SourceKind { get; }

    /// <summary>
    /// Gets the source location that should receive diagnostics for this request.
    /// </summary>
    public Location SourceLocation { get; }

    /// <summary>
    /// Gets the provenance label to prepend when a transitive missing dependency is found.
    /// </summary>
    public string ProvenanceStep { get; }
}
