using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Helpers for working with executable syntax boundaries such as methods, constructors,
/// accessors, local functions, and anonymous functions.
/// </summary>
internal static class ExecutableSyntaxHelper
{
    public static readonly SyntaxKind[] ExecutableRootKinds =
    [
        SyntaxKind.MethodDeclaration,
        SyntaxKind.ConstructorDeclaration,
        SyntaxKind.GetAccessorDeclaration,
        SyntaxKind.SetAccessorDeclaration,
        SyntaxKind.InitAccessorDeclaration,
        SyntaxKind.AddAccessorDeclaration,
        SyntaxKind.RemoveAccessorDeclaration,
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.SimpleLambdaExpression,
        SyntaxKind.ParenthesizedLambdaExpression,
        SyntaxKind.AnonymousMethodExpression
    ];

    public static bool IsExecutableBoundary(SyntaxNode? node)
    {
        return node is MethodDeclarationSyntax or
               ConstructorDeclarationSyntax or
               AccessorDeclarationSyntax or
               LocalFunctionStatementSyntax or
               AnonymousFunctionExpressionSyntax;
    }

    public static bool TryGetExecutableBody(
        SyntaxNode executableRoot,
        out SyntaxNode executableBody)
    {
        SyntaxNode? body = executableRoot switch
        {
            MethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
            ConstructorDeclarationSyntax constructor => (SyntaxNode?)constructor.Body ?? constructor.ExpressionBody?.Expression,
            AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax localFunction => (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody?.Expression,
            SimpleLambdaExpressionSyntax simpleLambda => simpleLambda.Body,
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda => parenthesizedLambda.Body,
            AnonymousMethodExpressionSyntax anonymousMethod => anonymousMethod.Body,
            _ => null
        };

        executableBody = body!;
        return body is not null;
    }

    public static IEnumerable<SyntaxNode> EnumerateSameBoundaryNodes(SyntaxNode container)
    {
        yield return container;

        foreach (var child in container.ChildNodes())
        {
            if (IsExecutableBoundary(child))
            {
                continue;
            }

            foreach (var descendant in EnumerateSameBoundaryNodes(child))
            {
                yield return descendant;
            }
        }
    }
}
