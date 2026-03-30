using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Helpers for extracting constant values from syntax expressions.
/// </summary>
internal static class SyntaxValueHelpers
{
    /// <summary>
    /// Tries to extract a constant value from an expression using the semantic model.
    /// Returns true if the expression has a known constant value (even if that value is null).
    /// Returns false if the semantic model cannot determine a constant value.
    /// </summary>
    public static bool TryExtractConstantValue(ExpressionSyntax expr, SemanticModel semanticModel, out object? value)
    {
        var constantValue = semanticModel.GetConstantValue(expr);
        if (constantValue.HasValue)
        {
            value = constantValue.Value;
            return true;
        }

        value = null;
        return false;
    }
}
