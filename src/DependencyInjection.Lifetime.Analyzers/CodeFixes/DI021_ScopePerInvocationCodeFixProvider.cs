using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace DependencyInjection.Lifetime.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider for DI021/DI022: rewrites the handler to resolve the shared service from a
/// new IServiceScope created per invocation, plumbing IServiceScopeFactory through the
/// constructor when needed and removing the now-dead captured field.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI021_ScopePerInvocationCodeFixProvider))]
[Shared]
public sealed class DI021_ScopePerInvocationCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "DI021_ScopePerInvocation";
    private const string ScopeFactoryTypeName = "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(
            DiagnosticIds.ConcurrentHandlerSharedState,
            DiagnosticIds.ConcurrentHandlerConfigGatedSharedState);

    /// <inheritdoc />
    public sealed override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc />
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        if (!diagnostic.Properties.TryGetValue("CaptureKind", out var captureKind) ||
            captureKind is null or "ScopeResolution" ||
            !diagnostic.Properties.TryGetValue("ServiceTypeName", out var serviceTypeName) ||
            serviceTypeName is null ||
            !diagnostic.Properties.TryGetValue("SymbolName", out var symbolName) ||
            symbolName is null ||
            !diagnostic.Properties.TryGetValue("HandlerIsAsync", out var handlerIsAsyncText))
        {
            return;
        }

        // A non-async handler returning Task/ValueTask cannot host a synchronous using-scope:
        // the scope would dispose before the returned task completes. No safe rewrite in v1.
        if (handlerIsAsyncText != "true" &&
            diagnostic.Properties.TryGetValue("HandlerReturnsAwaitable", out var returnsAwaitable) &&
            returnsAwaitable == "true")
        {
            return;
        }

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return;
        }

        var plan = CreatePlan(
            root, semanticModel, diagnostic,
            symbolName, serviceTypeName, captureKind,
            handlerIsAsyncText == "true",
            context.CancellationToken);
        if (plan is null)
        {
            return;
        }

        var shortTypeName = serviceTypeName.Split('.').Last();
        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Resolve '{shortTypeName}' from a new scope per invocation",
                createChangedDocument: c => ApplyFixAsync(context.Document, plan, c),
                equivalenceKey: EquivalenceKey),
            diagnostic);
    }

    private sealed class FixPlan
    {
        public FixPlan(
            SyntaxNode handlerNode,
            List<TextSpanOffset> useSpans,
            string symbolName,
            string serviceTypeName,
            string captureKind,
            bool isAsync,
            string? existingFactoryName,
            bool expressionBodyNeedsReturn)
        {
            HandlerNode = handlerNode;
            UseSpans = useSpans;
            SymbolName = symbolName;
            ServiceTypeName = serviceTypeName;
            CaptureKind = captureKind;
            IsAsync = isAsync;
            ExistingFactoryName = existingFactoryName;
            ExpressionBodyNeedsReturn = expressionBodyNeedsReturn;
        }

        public SyntaxNode HandlerNode { get; }

        public List<TextSpanOffset> UseSpans { get; }

        public string SymbolName { get; }

        public string ServiceTypeName { get; }

        public string CaptureKind { get; }

        public bool IsAsync { get; }

        public string? ExistingFactoryName { get; }

        public bool ExpressionBodyNeedsReturn { get; }
    }

    private readonly struct TextSpanOffset
    {
        public TextSpanOffset(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public int Start { get; }

        public int Length { get; }
    }

    private static FixPlan? CreatePlan(
        SyntaxNode root,
        SemanticModel semanticModel,
        Diagnostic diagnostic,
        string symbolName,
        string serviceTypeName,
        string captureKind,
        bool isAsync,
        CancellationToken cancellationToken)
    {
        var firstUse = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

        // Locate the handler boundary: the nearest lambda/anonymous method, local function, or
        // method declaration.
        SyntaxNode? handler = null;
        for (var node = firstUse.Parent; node is not null; node = node.Parent)
        {
            if (node is AnonymousFunctionExpressionSyntax or MethodDeclarationSyntax or LocalFunctionStatementSyntax)
            {
                handler = node;
                break;
            }
        }

        if (handler is null)
        {
            return null;
        }

        // Methods and local functions need a block body; expression-bodied forms are refused
        // (lambdas are converted to blocks instead).
        if (handler is MethodDeclarationSyntax { Body: null } or LocalFunctionStatementSyntax { Body: null })
        {
            return null;
        }

        var typeDeclaration = handler.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeDeclaration is null)
        {
            return null;
        }

        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);
        if (typeSymbol is null)
        {
            return null;
        }

        var existingFactory = typeSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f => !f.IsStatic && f.Type.ToDisplayString() == ScopeFactoryTypeName);

        if (existingFactory is null)
        {
            // Plumbing an instance field requires an instance context and a declared constructor.
            if (IsStaticContext(handler))
            {
                return null;
            }

            // Exactly one declared block-bodied constructor: plumbing only the first of several
            // would leave instances created through the others with a null _scopeFactory at
            // runtime, and an expression-bodied constructor cannot receive the assignment.
            var declaredConstructors = typeDeclaration.Members
                .OfType<ConstructorDeclarationSyntax>()
                .Where(c => !c.Modifiers.Any(SyntaxKind.StaticKeyword))
                .ToList();
            if (declaredConstructors.Count != 1 || declaredConstructors[0].Body is null)
            {
                return null;
            }

            if (typeSymbol.GetMembers().Any(m => m.Name is "_scopeFactory" or "scopeFactory"))
            {
                return null;
            }
        }
        else if (IsStaticContext(handler))
        {
            return null;
        }

        var useSpans = new List<TextSpanOffset>
        {
            new(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)
        };
        for (var i = 2; i < diagnostic.AdditionalLocations.Count; i++)
        {
            var span = diagnostic.AdditionalLocations[i].SourceSpan;
            useSpans.Add(new TextSpanOffset(span.Start, span.Length));
        }

        // Every use must be an identifier of the captured symbol inside the handler; anything
        // else (member bindings, refactored shapes) means the analysis no longer matches.
        foreach (var span in useSpans)
        {
            var node = root.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(span.Start, span.Length), getInnermostNodeForTie: true);
            if (node is not IdentifierNameSyntax identifier ||
                identifier.Identifier.ValueText != symbolName ||
                !handler.FullSpan.Contains(node.Span))
            {
                return null;
            }
        }

        var expressionBodyNeedsReturn = false;
        if (handler is AnonymousFunctionExpressionSyntax { Block: null } expressionLambda &&
            semanticModel.GetSymbolInfo(expressionLambda, cancellationToken).Symbol is IMethodSymbol lambdaSymbol)
        {
            expressionBodyNeedsReturn = !lambdaSymbol.ReturnsVoid && !lambdaSymbol.IsAsync;
        }

        return new FixPlan(
            handler, useSpans, symbolName, serviceTypeName, captureKind, isAsync,
            existingFactory?.Name, expressionBodyNeedsReturn);
    }

    private static bool IsStaticContext(SyntaxNode handler)
    {
        if (handler is MethodDeclarationSyntax method)
        {
            return method.Modifiers.Any(SyntaxKind.StaticKeyword);
        }

        var enclosingMethod = handler.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        return enclosingMethod?.Modifiers.Any(SyntaxKind.StaticKeyword) ?? false;
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        FixPlan plan,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            return document;
        }

        // Innermost-for-tie matters: a lambda passed as an invocation argument shares its full
        // span with the wrapping ArgumentSyntax, and the lambda is the inner node of that tie.
        var handler = compilationUnit.FindNode(plan.HandlerNode.Span, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .FirstOrDefault(n => n.Span == plan.HandlerNode.Span &&
                                 n is AnonymousFunctionExpressionSyntax or MethodDeclarationSyntax or LocalFunctionStatementSyntax);
        if (handler is null)
        {
            return document;
        }

        // Annotate the handler's own containing type declaration so later plumbing/cleanup edits
        // target exactly this declaration — same-named types in other namespaces or other partial
        // declarations must not receive the edits. Annotations survive descendant replacements.
        var containingType = handler.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (containingType is null)
        {
            return document;
        }

        var typeMarker = new SyntaxAnnotation();
        compilationUnit = compilationUnit.ReplaceNode(
            containingType, containingType.WithAdditionalAnnotations(typeMarker));
        handler = compilationUnit.FindNode(plan.HandlerNode.Span, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .FirstOrDefault(n => n.Span == plan.HandlerNode.Span &&
                                 n is AnonymousFunctionExpressionSyntax or MethodDeclarationSyntax or LocalFunctionStatementSyntax);
        if (handler is null)
        {
            return document;
        }

        var factoryName = plan.ExistingFactoryName ?? "_scopeFactory";
        var needsPlumbing = plan.ExistingFactoryName is null;

        // Names for the inserted locals, avoiding collisions within the handler.
        var handlerIdentifiers = new HashSet<string>(
            handler.DescendantTokens()
                .Where(t => t.IsKind(SyntaxKind.IdentifierToken))
                .Select(t => t.ValueText));
        var scopeName = PickName("scope", handlerIdentifiers);
        var localName = PickName(DeriveLocalName(plan.SymbolName), handlerIdentifiers);

        var scopeStatement = SyntaxFactory.ParseStatement(plan.IsAsync
            ? $"await using var {scopeName} = {factoryName}.CreateAsyncScope();\n"
            : $"using var {scopeName} = {factoryName}.CreateScope();\n");
        var resolveStatement = SyntaxFactory.ParseStatement(
            $"var {localName} = {scopeName}.ServiceProvider.GetRequiredService<{plan.ServiceTypeName}>();\n");

        var rewrittenHandler = RewriteHandler(
            handler, plan, scopeStatement, resolveStatement, localName);
        if (rewrittenHandler is null)
        {
            return document;
        }

        compilationUnit = compilationUnit.ReplaceNode(handler, rewrittenHandler);

        if (needsPlumbing)
        {
            compilationUnit = AddScopeFactoryPlumbing(compilationUnit, typeMarker, factoryName);
        }

        if (plan.CaptureKind == "Field")
        {
            compilationUnit = TryRemoveDeadCapturedField(compilationUnit, typeMarker, plan.SymbolName);
        }

        compilationUnit = EnsureDependencyInjectionUsing(compilationUnit);
        return document.WithSyntaxRoot(compilationUnit);
    }

    private static string DeriveLocalName(string symbolName)
    {
        var trimmed = symbolName.TrimStart('_');
        if (trimmed.Length == 0)
        {
            return "service";
        }

        return char.ToLowerInvariant(trimmed[0]) + trimmed.Substring(1);
    }

    private static string PickName(string candidate, HashSet<string> taken)
    {
        if (taken.Add(candidate))
        {
            return candidate;
        }

        for (var i = 1; ; i++)
        {
            var numbered = candidate + i;
            if (taken.Add(numbered))
            {
                return numbered;
            }
        }
    }

    private static SyntaxNode? RewriteHandler(
        SyntaxNode handler,
        FixPlan plan,
        StatementSyntax scopeStatement,
        StatementSyntax resolveStatement,
        string localName)
    {
        // Replace the captured-symbol uses with the new local.
        var uses = handler.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(id => plan.UseSpans.Any(span => span.Start == id.SpanStart && span.Length == id.Span.Length))
            .ToList();
        if (uses.Count == 0)
        {
            return null;
        }

        var withReplacedUses = handler.ReplaceNodes(
            uses,
            (_, _) => SyntaxFactory.IdentifierName(localName));

        var insertedStatements = new[]
        {
            scopeStatement.WithAdditionalAnnotations(Formatter.Annotation),
            resolveStatement.WithAdditionalAnnotations(Formatter.Annotation)
        };

        switch (withReplacedUses)
        {
            case MethodDeclarationSyntax { Body: { } body } method:
                return method
                    .WithBody(body.WithStatements(body.Statements.InsertRange(0, insertedStatements)))
                    .WithAdditionalAnnotations(Formatter.Annotation);

            case LocalFunctionStatementSyntax { Body: { } localFunctionBody } localFunction:
                return localFunction
                    .WithBody(localFunctionBody.WithStatements(
                        localFunctionBody.Statements.InsertRange(0, insertedStatements)))
                    .WithAdditionalAnnotations(Formatter.Annotation);

            case AnonymousFunctionExpressionSyntax { Block: { } block } anonymous:
                return anonymous
                    .ReplaceNode(block, block.WithStatements(block.Statements.InsertRange(0, insertedStatements)))
                    .WithAdditionalAnnotations(Formatter.Annotation);

            case AnonymousFunctionExpressionSyntax anonymous when
                anonymous.Body is ExpressionSyntax expression:
            {
                // Convert the expression-bodied lambda to a block so the scope statements fit.
                StatementSyntax bodyStatement = plan.ExpressionBodyNeedsReturn
                    ? SyntaxFactory.ReturnStatement(expression.WithoutTrivia())
                    : SyntaxFactory.ExpressionStatement(expression.WithoutTrivia());
                var newBlock = SyntaxFactory.Block(
                    insertedStatements[0],
                    insertedStatements[1],
                    bodyStatement.WithAdditionalAnnotations(Formatter.Annotation));
                return WithLambdaBlockBody(anonymous, newBlock)
                    ?.WithAdditionalAnnotations(Formatter.Annotation);
            }

            default:
                return null;
        }
    }

    private static SyntaxNode? WithLambdaBlockBody(AnonymousFunctionExpressionSyntax anonymous, BlockSyntax block)
    {
        return anonymous switch
        {
            SimpleLambdaExpressionSyntax simple => simple.WithBody(block),
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.WithBody(block),
            _ => null
        };
    }

    private static CompilationUnitSyntax AddScopeFactoryPlumbing(
        CompilationUnitSyntax compilationUnit,
        SyntaxAnnotation typeMarker,
        string fieldName)
    {
        var typeDeclaration = FindTypeDeclaration(compilationUnit, typeMarker);
        var constructor = typeDeclaration?.Members
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault(c => !c.Modifiers.Any(SyntaxKind.StaticKeyword));
        if (typeDeclaration is null || constructor?.Body is null)
        {
            return compilationUnit;
        }

        var parameterName = fieldName.TrimStart('_');
        var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameterName))
            .WithType(SyntaxFactory.ParseTypeName("IServiceScopeFactory"));

        // A required parameter must precede optional and params parameters, or the
        // constructor no longer compiles.
        var parameters = constructor.ParameterList.Parameters;
        var insertIndex = parameters.Count;
        for (var i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Default is not null ||
                parameters[i].Modifiers.Any(SyntaxKind.ParamsKeyword))
            {
                insertIndex = i;
                break;
            }
        }

        var newConstructor = constructor
            .WithParameterList(constructor.ParameterList.WithParameters(
                parameters.Insert(insertIndex, parameter)))
            .WithBody(constructor.Body.AddStatements(
                SyntaxFactory.ParseStatement($"{fieldName} = {parameterName};\n")
                    .WithAdditionalAnnotations(Formatter.Annotation)))
            .WithAdditionalAnnotations(Formatter.Annotation);

        var field = SyntaxFactory.ParseMemberDeclaration(
                $"private readonly IServiceScopeFactory {fieldName};\n")!
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newType = typeDeclaration.ReplaceNode(constructor, newConstructor);
        newType = newType.WithMembers(newType.Members.Insert(0, field));
        return compilationUnit.ReplaceNode(typeDeclaration, newType);
    }

    /// <summary>
    /// Removes the captured field, its constructor assignment, and the constructor parameter when
    /// the handler rewrite was the only remaining consumer. Aborts when any other reference exists.
    /// </summary>
    private static CompilationUnitSyntax TryRemoveDeadCapturedField(
        CompilationUnitSyntax compilationUnit,
        SyntaxAnnotation typeMarker,
        string fieldName)
    {
        var typeDeclaration = FindTypeDeclaration(compilationUnit, typeMarker);
        if (typeDeclaration is null)
        {
            return compilationUnit;
        }

        var fieldDeclaration = typeDeclaration.Members
            .OfType<FieldDeclarationSyntax>()
            .FirstOrDefault(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == fieldName));
        if (fieldDeclaration is null || fieldDeclaration.Declaration.Variables.Count != 1)
        {
            return compilationUnit;
        }

        var removableAssignments = new List<StatementSyntax>();
        var parameterNames = new HashSet<string>();
        foreach (var reference in typeDeclaration.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (reference.Identifier.ValueText != fieldName ||
                reference.Ancestors().Contains(fieldDeclaration))
            {
                continue;
            }

            // The only tolerated remaining references are `_field = parameter;` constructor
            // assignments whose right side is a bare constructor-parameter identifier.
            if (reference.Parent is AssignmentExpressionSyntax assignment &&
                assignment.Left == reference &&
                assignment.Right is IdentifierNameSyntax parameterIdentifier &&
                assignment.Parent is ExpressionStatementSyntax statement &&
                reference.Ancestors().OfType<ConstructorDeclarationSyntax>().Any())
            {
                removableAssignments.Add(statement);
                parameterNames.Add(parameterIdentifier.Identifier.ValueText);
                continue;
            }

            return compilationUnit;
        }

        var newType = typeDeclaration.RemoveNodes(
            removableAssignments.Concat<SyntaxNode>(new[] { fieldDeclaration }),
            SyntaxRemoveOptions.KeepNoTrivia);
        if (newType is null)
        {
            return compilationUnit;
        }

        // Remove constructor parameters that fed only the removed assignments. A constructor
        // parameter is only in scope inside its constructor, so the reference scan is
        // constructor-scoped to avoid same-named locals elsewhere keeping it alive.
        foreach (var parameterName in parameterNames)
        {
            var stillReferenced = newType.DescendantNodes()
                .OfType<ConstructorDeclarationSyntax>()
                .SelectMany(c => c.DescendantNodes())
                .OfType<IdentifierNameSyntax>()
                .Any(id => id.Identifier.ValueText == parameterName);
            if (stillReferenced)
            {
                continue;
            }

            var parameter = newType.DescendantNodes()
                .OfType<ConstructorDeclarationSyntax>()
                .SelectMany(c => c.ParameterList.Parameters)
                .FirstOrDefault(p => p.Identifier.ValueText == parameterName);
            if (parameter is not null)
            {
                newType = newType.RemoveNode(parameter, SyntaxRemoveOptions.KeepNoTrivia) ?? newType;
            }
        }

        return compilationUnit.ReplaceNode(typeDeclaration, newType);
    }

    private static TypeDeclarationSyntax? FindTypeDeclaration(
        CompilationUnitSyntax compilationUnit,
        SyntaxAnnotation typeMarker)
    {
        return compilationUnit.GetAnnotatedNodes(typeMarker)
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();
    }

    private static CompilationUnitSyntax EnsureDependencyInjectionUsing(CompilationUnitSyntax compilationUnit)
    {
        const string Namespace = "Microsoft.Extensions.DependencyInjection";
        if (compilationUnit.Usings.Any(u => u.Name?.ToString() == Namespace))
        {
            return compilationUnit;
        }

        return compilationUnit.AddUsings(
            SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(Namespace))
                .WithAdditionalAnnotations(Formatter.Annotation));
    }
}
