using System;
using System.Globalization;
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

    public static bool TryExtractServiceKeyValue(
        ExpressionSyntax expr,
        SemanticModel semanticModel,
        out object? value,
        out string? literal)
    {
        if (IsKeyedServiceAnyKeyExpression(expr, semanticModel))
        {
            value = KeyedServiceAnyKey.Instance;
            literal = null;
            return true;
        }

        if (TryExtractConstantValue(expr, semanticModel, out value))
        {
            literal = TryFormatCSharpLiteral(value, out var formatted)
                ? formatted
                : null;
            return true;
        }

        literal = null;
        return false;
    }

    public static bool IsKeyedServiceAnyKey(object? value) =>
        ReferenceEquals(value, KeyedServiceAnyKey.Instance);

    public static bool TryFormatCSharpLiteral(object? value, out string literal)
    {
        switch (value)
        {
            case null:
                literal = "null";
                return true;
            case string text:
                literal = "@\"" + text.Replace("\"", "\"\"") + "\"";
                return true;
            case char character:
                literal = "'" + EscapeChar(character) + "'";
                return true;
            case bool boolean:
                literal = boolean ? "true" : "false";
                return true;
            case byte byteValue:
                literal = "(byte)" + byteValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case sbyte sbyteValue:
                literal = "(sbyte)" + sbyteValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case short shortValue:
                literal = "(short)" + shortValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case ushort ushortValue:
                literal = "(ushort)" + ushortValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case int intValue:
                literal = intValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case uint uintValue:
                literal = uintValue.ToString(CultureInfo.InvariantCulture) + "u";
                return true;
            case long longValue:
                literal = longValue.ToString(CultureInfo.InvariantCulture) + "L";
                return true;
            case ulong ulongValue:
                literal = ulongValue.ToString(CultureInfo.InvariantCulture) + "UL";
                return true;
            case float single:
                literal = single.ToString("R", CultureInfo.InvariantCulture) + "f";
                return true;
            case double dbl:
                literal = dbl.ToString("R", CultureInfo.InvariantCulture) + "d";
                return true;
            case decimal dec:
                literal = dec.ToString(CultureInfo.InvariantCulture) + "m";
                return true;
            default:
                literal = string.Empty;
                return false;
        }
    }

    private static string EscapeChar(char value) =>
        value switch
        {
            '\\' => "\\\\",
            '\'' => "\\'",
            '\0' => "\\0",
            '\a' => "\\a",
            '\b' => "\\b",
            '\f' => "\\f",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            '\v' => "\\v",
            _ => value.ToString()
        };

    private static bool IsKeyedServiceAnyKeyExpression(ExpressionSyntax expr, SemanticModel semanticModel)
    {
        while (expr is ParenthesizedExpressionSyntax parenthesizedExpression)
        {
            expr = parenthesizedExpression.Expression;
        }

        if (semanticModel.GetSymbolInfo(expr).Symbol is not IPropertySymbol propertySymbol)
        {
            return false;
        }

        return propertySymbol.Name == "AnyKey" &&
               propertySymbol.ContainingType?.Name == "KeyedService" &&
               propertySymbol.ContainingNamespace.ToDisplayString() ==
               "Microsoft.Extensions.DependencyInjection";
    }
}
