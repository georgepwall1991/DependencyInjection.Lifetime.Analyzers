namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Identifies how a dependency requirement was discovered.
/// </summary>
internal enum DependencySourceKind
{
    /// <summary>
    /// The dependency comes from a constructor parameter.
    /// </summary>
    ConstructorParameter,

    /// <summary>
    /// The dependency comes from a GetRequiredService/GetRequiredKeyedService call.
    /// </summary>
    RequiredServiceCall,

    /// <summary>
    /// The dependency comes from ActivatorUtilities.CreateInstance(...).
    /// </summary>
    ActivatorUtilitiesConstruction
}
