using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Represents a high-confidence missing dependency discovered during resolution.
/// </summary>
internal sealed class MissingDependency
{
    public MissingDependency(
        ITypeSymbol type,
        object? key,
        bool isKeyed,
        string? keyLiteral,
        ImmutableArray<string> provenancePath)
    {
        Type = type;
        Key = key;
        IsKeyed = isKeyed;
        KeyLiteral = keyLiteral;
        ProvenancePath = provenancePath;
    }

    /// <summary>
    /// Gets the missing dependency type.
    /// </summary>
    public ITypeSymbol Type { get; }

    /// <summary>
    /// Gets the key for keyed dependencies.
    /// </summary>
    public object? Key { get; }

    /// <summary>
    /// Gets whether the missing dependency is keyed.
    /// </summary>
    public bool IsKeyed { get; }

    /// <summary>
    /// Gets a C# literal for the key when it can be safely round-tripped into code.
    /// </summary>
    public string? KeyLiteral { get; }

    /// <summary>
    /// Gets the provenance path that led to the missing dependency.
    /// </summary>
    public ImmutableArray<string> ProvenancePath { get; }

    /// <summary>
    /// Gets the number of steps in the provenance path.
    /// </summary>
    public int PathLength => ProvenancePath.Length;

    public MissingDependency PrependProvenance(string provenanceStep)
    {
        var builder = ImmutableArray.CreateBuilder<string>(ProvenancePath.Length + 1);
        builder.Add(provenanceStep);
        builder.AddRange(ProvenancePath);

        return new MissingDependency(
            Type,
            Key,
            IsKeyed,
            KeyLiteral,
            builder.ToImmutable());
    }

    public static MissingDependency CreateDirect(DependencyRequest request)
    {
        return new MissingDependency(
            request.Type,
            request.Key,
            request.IsKeyed,
            request.KeyLiteral,
            ImmutableArray.Create(request.ProvenanceStep));
    }
}
