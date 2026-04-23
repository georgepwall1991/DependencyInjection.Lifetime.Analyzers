using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace DependencyInjection.Lifetime.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider for DI013: implementation type mismatch.
/// Offers broad but symbol-backed assists for invalid type/instance registrations.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DI013_ImplementationTypeMismatchCodeFixProvider))]
[Shared]
public sealed class DI013_ImplementationTypeMismatchCodeFixProvider : CodeFixProvider
{
    internal const string RemoveInvalidRegistrationEquivalenceKey = "DI013_RemoveInvalidRegistration";
    internal const string ReplaceImplementationEquivalenceKeyPrefix = "DI013_ReplaceImplementation_";
    internal const string ChangeServiceTypeEquivalenceKeyPrefix = "DI013_ChangeServiceType_";

    private const int MaxImplementationCandidates = 4;
    private const int MaxServiceTypeCandidates = 4;

    private static readonly SymbolDisplayFormat TypeDisplayFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included);

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.ImplementationTypeMismatch);

    /// <inheritdoc />
    public sealed override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc />
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        var syntaxInfo = await TryGetRegistrationSyntaxInfoAsync(
            context.Document,
            diagnostic,
            context.CancellationToken).ConfigureAwait(false);

        if (syntaxInfo is null)
        {
            return;
        }

        if (syntaxInfo.Value.RemovableStatement is not null)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Resources.DI013_FixTitle_RemoveInvalidRegistration,
                    createChangedDocument: cancellationToken => RemoveInvalidRegistrationAsync(
                        context.Document,
                        syntaxInfo.Value.RemovableStatement,
                        cancellationToken),
                    equivalenceKey: RemoveInvalidRegistrationEquivalenceKey),
                diagnostic);
        }

        if (syntaxInfo.Value.ImplementationTypeSyntax is not null)
        {
            var implementationCandidates = await FindCompatibleImplementationCandidatesAsync(
                context.Document,
                syntaxInfo.Value,
                context.CancellationToken).ConfigureAwait(false);

            foreach (var candidate in implementationCandidates)
            {
                var title = string.Format(
                    Resources.DI013_FixTitle_UseImplementation,
                    candidate.DisplayName);
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title,
                        cancellationToken => ReplaceTypeSyntaxAsync(
                            context.Document,
                            syntaxInfo.Value.ImplementationTypeSyntax,
                            candidate.FullyQualifiedName,
                            cancellationToken),
                        equivalenceKey: ReplaceImplementationEquivalenceKeyPrefix + candidate.MetadataName),
                    diagnostic);
            }
        }

        foreach (var candidate in GetCompatibleServiceTypeCandidates(syntaxInfo.Value))
        {
            var title = string.Format(
                Resources.DI013_FixTitle_UseServiceType,
                candidate.DisplayName);
            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    cancellationToken => ReplaceTypeSyntaxAsync(
                        context.Document,
                        syntaxInfo.Value.ServiceTypeSyntax,
                        candidate.FullyQualifiedName,
                        cancellationToken),
                    equivalenceKey: ChangeServiceTypeEquivalenceKeyPrefix + candidate.MetadataName),
                diagnostic);
        }
    }

    private readonly struct RegistrationSyntaxInfo
    {
        public RegistrationSyntaxInfo(
            InvocationExpressionSyntax registrationInvocation,
            ExpressionStatementSyntax? removableStatement,
            TypeSyntax serviceTypeSyntax,
            TypeSyntax? implementationTypeSyntax,
            INamedTypeSymbol serviceType,
            INamedTypeSymbol implementationType)
        {
            RegistrationInvocation = registrationInvocation;
            RemovableStatement = removableStatement;
            ServiceTypeSyntax = serviceTypeSyntax;
            ImplementationTypeSyntax = implementationTypeSyntax;
            ServiceType = serviceType;
            ImplementationType = implementationType;
        }

        public InvocationExpressionSyntax RegistrationInvocation { get; }

        public ExpressionStatementSyntax? RemovableStatement { get; }

        public TypeSyntax ServiceTypeSyntax { get; }

        public TypeSyntax? ImplementationTypeSyntax { get; }

        public INamedTypeSymbol ServiceType { get; }

        public INamedTypeSymbol ImplementationType { get; }
    }

    private readonly struct TypeCandidate
    {
        public TypeCandidate(INamedTypeSymbol symbol)
        {
            Symbol = symbol;
            FullyQualifiedName = symbol.ToDisplayString(TypeDisplayFormat);
            DisplayName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            MetadataName = GetFullMetadataName(symbol);
        }

        public INamedTypeSymbol Symbol { get; }

        public string FullyQualifiedName { get; }

        public string DisplayName { get; }

        public string MetadataName { get; }
    }

    private static async Task<RegistrationSyntaxInfo?> TryGetRegistrationSyntaxInfoAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        if (!diagnostic.Properties.TryGetValue(DI013_ImplementationTypeMismatchAnalyzer.ServiceTypeNamePropertyName, out var serviceTypeName) ||
            !diagnostic.Properties.TryGetValue(DI013_ImplementationTypeMismatchAnalyzer.ImplementationTypeNamePropertyName, out var implementationTypeName) ||
            string.IsNullOrWhiteSpace(serviceTypeName) ||
            string.IsNullOrWhiteSpace(implementationTypeName))
        {
            return null;
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return null;
        }

        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        var registrationInvocation = node as InvocationExpressionSyntax ?? node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (registrationInvocation is null)
        {
            return null;
        }

        if (TryGetGenericRegistrationSyntax(
                registrationInvocation,
                semanticModel,
                serviceTypeName!,
                implementationTypeName!,
                out var serviceTypeSyntax,
                out var implementationTypeSyntax,
                out var serviceType,
                out var implementationType))
        {
            return CreateSyntaxInfo(
                registrationInvocation,
                serviceTypeSyntax,
                implementationTypeSyntax,
                serviceType,
                implementationType);
        }

        if (TryGetTypeOfRegistrationSyntax(
                registrationInvocation,
                semanticModel,
                serviceTypeName!,
                implementationTypeName!,
                out serviceTypeSyntax,
                out implementationTypeSyntax,
                out serviceType,
                out implementationType))
        {
            return CreateSyntaxInfo(
                registrationInvocation,
                serviceTypeSyntax,
                implementationTypeSyntax,
                serviceType,
                implementationType);
        }

        if (TryGetInstanceRegistrationSyntax(
                registrationInvocation,
                semanticModel,
                serviceTypeName!,
                implementationTypeName!,
                out serviceTypeSyntax,
                out serviceType,
                out implementationType))
        {
            return CreateSyntaxInfo(
                registrationInvocation,
                serviceTypeSyntax,
                implementationTypeSyntax: null,
                serviceType,
                implementationType);
        }

        return null;
    }

    private static RegistrationSyntaxInfo CreateSyntaxInfo(
        InvocationExpressionSyntax registrationInvocation,
        TypeSyntax serviceTypeSyntax,
        TypeSyntax? implementationTypeSyntax,
        INamedTypeSymbol serviceType,
        INamedTypeSymbol implementationType)
    {
        var removableStatement = registrationInvocation.Parent is ExpressionStatementSyntax statement &&
            ReferenceEquals(statement.Expression, registrationInvocation)
                ? statement
                : null;

        return new RegistrationSyntaxInfo(
            registrationInvocation,
            removableStatement,
            serviceTypeSyntax,
            implementationTypeSyntax,
            serviceType,
            implementationType);
    }

    private static bool TryGetGenericRegistrationSyntax(
        InvocationExpressionSyntax registrationInvocation,
        SemanticModel semanticModel,
        string serviceTypeName,
        string implementationTypeName,
        out TypeSyntax serviceTypeSyntax,
        out TypeSyntax implementationTypeSyntax,
        out INamedTypeSymbol serviceType,
        out INamedTypeSymbol implementationType)
    {
        serviceTypeSyntax = null!;
        implementationTypeSyntax = null!;
        serviceType = null!;
        implementationType = null!;

        foreach (var invocation in GetCandidateInvocations(registrationInvocation))
        {
            if (GetMethodSymbol(invocation, semanticModel) is not { IsGenericMethod: true } method ||
                method.TypeArguments.Length < 2 ||
                invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } ||
                genericName.TypeArgumentList.Arguments.Count < 2)
            {
                continue;
            }

            if (method.TypeArguments[0] is not INamedTypeSymbol candidateServiceType ||
                method.TypeArguments[1] is not INamedTypeSymbol candidateImplementationType ||
                !MatchesDisplayName(candidateServiceType, serviceTypeName) ||
                !MatchesDisplayName(candidateImplementationType, implementationTypeName))
            {
                continue;
            }

            serviceTypeSyntax = genericName.TypeArgumentList.Arguments[0];
            implementationTypeSyntax = genericName.TypeArgumentList.Arguments[1];
            serviceType = candidateServiceType;
            implementationType = candidateImplementationType;
            return true;
        }

        return false;
    }

    private static IMethodSymbol? GetMethodSymbol(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        return symbolInfo.Symbol as IMethodSymbol ??
            symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    private static bool TryGetTypeOfRegistrationSyntax(
        InvocationExpressionSyntax registrationInvocation,
        SemanticModel semanticModel,
        string serviceTypeName,
        string implementationTypeName,
        out TypeSyntax serviceTypeSyntax,
        out TypeSyntax implementationTypeSyntax,
        out INamedTypeSymbol serviceType,
        out INamedTypeSymbol implementationType)
    {
        serviceTypeSyntax = null!;
        implementationTypeSyntax = null!;
        serviceType = null!;
        implementationType = null!;

        foreach (var typeOfExpression in registrationInvocation.DescendantNodesAndSelf().OfType<TypeOfExpressionSyntax>())
        {
            if (semanticModel.GetTypeInfo(typeOfExpression.Type).Type is not INamedTypeSymbol type)
            {
                continue;
            }

            if (serviceTypeSyntax is null && MatchesDisplayName(type, serviceTypeName))
            {
                serviceTypeSyntax = typeOfExpression.Type;
                serviceType = type;
                continue;
            }

            if (implementationTypeSyntax is null && MatchesDisplayName(type, implementationTypeName))
            {
                implementationTypeSyntax = typeOfExpression.Type;
                implementationType = type;
            }
        }

        return serviceTypeSyntax is not null && implementationTypeSyntax is not null;
    }

    private static bool TryGetInstanceRegistrationSyntax(
        InvocationExpressionSyntax registrationInvocation,
        SemanticModel semanticModel,
        string serviceTypeName,
        string implementationTypeName,
        out TypeSyntax serviceTypeSyntax,
        out INamedTypeSymbol serviceType,
        out INamedTypeSymbol implementationType)
    {
        serviceTypeSyntax = null!;
        serviceType = null!;
        implementationType = null!;

        foreach (var typeOfExpression in registrationInvocation.DescendantNodesAndSelf().OfType<TypeOfExpressionSyntax>())
        {
            if (semanticModel.GetTypeInfo(typeOfExpression.Type).Type is INamedTypeSymbol type &&
                MatchesDisplayName(type, serviceTypeName))
            {
                serviceTypeSyntax = typeOfExpression.Type;
                serviceType = type;
                break;
            }
        }

        if (serviceTypeSyntax is null)
        {
            return false;
        }

        foreach (var expression in registrationInvocation.ArgumentList.Arguments.Select(argument => argument.Expression))
        {
            if (expression is TypeOfExpressionSyntax)
            {
                continue;
            }

            if (semanticModel.GetTypeInfo(expression).Type is INamedTypeSymbol type &&
                MatchesDisplayName(type, implementationTypeName))
            {
                implementationType = type;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<InvocationExpressionSyntax> GetCandidateInvocations(
        InvocationExpressionSyntax registrationInvocation)
    {
        yield return registrationInvocation;

        foreach (var invocation in registrationInvocation.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            yield return invocation;
        }
    }

    private static async Task<Document> RemoveInvalidRegistrationAsync(
        Document document,
        ExpressionStatementSyntax statement,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        SyntaxNode? newRoot;
        if (statement.Parent is BlockSyntax block)
        {
            newRoot = root.ReplaceNode(block, block.WithStatements(block.Statements.Remove(statement)));
        }
        else
        {
            newRoot = root.RemoveNode(statement, SyntaxRemoveOptions.KeepNoTrivia);
        }

        return newRoot is null ? document : document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> ReplaceTypeSyntaxAsync(
        Document document,
        TypeSyntax oldTypeSyntax,
        string replacementTypeName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var replacementType = SyntaxFactory.ParseTypeName(replacementTypeName)
            .WithTriviaFrom(oldTypeSyntax)
            .WithAdditionalAnnotations(Formatter.Annotation);
        return document.WithSyntaxRoot(root.ReplaceNode(oldTypeSyntax, replacementType));
    }

    private static async Task<IReadOnlyList<TypeCandidate>> FindCompatibleImplementationCandidatesAsync(
        Document document,
        RegistrationSyntaxInfo syntaxInfo,
        CancellationToken cancellationToken)
    {
        var currentCompilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (currentCompilation is null)
        {
            return [];
        }

        var orderedProjects = document.Project.Solution.Projects
            .OrderBy(project => GetProjectPriority(project, document.Project))
            .ToArray();
        var candidates = new List<TypeCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in orderedProjects)
        {
            var projectCompilation = project.Id == document.Project.Id
                ? currentCompilation
                : await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (projectCompilation is null)
            {
                continue;
            }

            foreach (var sourceType in GetSourceNamedTypes(projectCompilation.Assembly.GlobalNamespace)
                         .OrderBy(type => GetDocumentPriority(type, document))
                         .ThenBy(type => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var metadataName = GetFullMetadataName(sourceType);
                if (string.IsNullOrEmpty(metadataName) ||
                    !seen.Add(metadataName) ||
                    SymbolEqualityComparer.Default.Equals(sourceType, syntaxInfo.ImplementationType) ||
                    !CanUseAsImplementationCandidate(sourceType))
                {
                    continue;
                }

                var currentCompilationType = currentCompilation.GetTypeByMetadataName(metadataName);
                if (currentCompilationType is null ||
                    !IsCompatible(currentCompilation, syntaxInfo.ServiceType, currentCompilationType))
                {
                    continue;
                }

                candidates.Add(new TypeCandidate(currentCompilationType));
                if (candidates.Count >= MaxImplementationCandidates)
                {
                    return candidates;
                }
            }
        }

        return candidates;
    }

    private static IEnumerable<TypeCandidate> GetCompatibleServiceTypeCandidates(
        RegistrationSyntaxInfo syntaxInfo)
    {
        if (syntaxInfo.ImplementationType.IsUnboundGenericType)
        {
            return [];
        }

        var candidates = new List<INamedTypeSymbol>();
        candidates.AddRange(syntaxInfo.ImplementationType.AllInterfaces);

        for (var baseType = syntaxInfo.ImplementationType.BaseType;
             baseType is not null && baseType.SpecialType != SpecialType.System_Object;
             baseType = baseType.BaseType)
        {
            candidates.Add(baseType);
        }

        return candidates
            .Where(type =>
                !SymbolEqualityComparer.Default.Equals(type, syntaxInfo.ServiceType) &&
                IsSourceDeclared(type))
            .GroupBy(GetFullMetadataName, StringComparer.Ordinal)
            .Select(group => new TypeCandidate(group.First()))
            .OrderBy(candidate => candidate.DisplayName, StringComparer.Ordinal)
            .Take(MaxServiceTypeCandidates)
            .ToArray();
    }

    private static bool IsCompatible(
        Compilation compilation,
        INamedTypeSymbol service,
        INamedTypeSymbol implementation)
    {
        if (service.IsUnboundGenericType)
        {
            return IsOpenGenericCompatible(service, implementation);
        }

        if (implementation.IsUnboundGenericType)
        {
            return false;
        }

        return compilation is CSharpCompilation csharpCompilation &&
            csharpCompilation.ClassifyConversion(implementation, service).IsImplicit;
    }

    private static bool IsOpenGenericCompatible(INamedTypeSymbol service, INamedTypeSymbol implementation)
    {
        if (!implementation.IsGenericType || !implementation.IsUnboundGenericType)
        {
            return false;
        }

        var implDef = implementation.OriginalDefinition;
        if (implDef.TypeParameters.Length != service.TypeParameters.Length)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(implDef, service.OriginalDefinition))
        {
            return true;
        }

        foreach (var iface in implDef.AllInterfaces)
        {
            if (MatchesOpenGenericProjection(iface, service, service.TypeParameters.Length))
            {
                return true;
            }
        }

        for (var baseType = implDef.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (MatchesOpenGenericProjection(baseType, service, service.TypeParameters.Length))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesOpenGenericProjection(
        INamedTypeSymbol candidate,
        INamedTypeSymbol service,
        int serviceArity)
    {
        INamedTypeSymbol candidateUnbound;
        if (candidate.IsUnboundGenericType)
        {
            candidateUnbound = candidate;
        }
        else if (candidate.IsGenericType)
        {
            candidateUnbound = candidate.ConstructUnboundGenericType();
        }
        else
        {
            return serviceArity == 0;
        }

        if (!SymbolEqualityComparer.Default.Equals(candidateUnbound, service))
        {
            return false;
        }

        if (candidate.IsUnboundGenericType)
        {
            return candidate.TypeParameters.Length == serviceArity;
        }

        var typeArgs = candidate.TypeArguments;
        if (typeArgs.Length != serviceArity)
        {
            return false;
        }

        for (var i = 0; i < typeArgs.Length; i++)
        {
            if (typeArgs[i] is not ITypeParameterSymbol typeParam || typeParam.Ordinal != i)
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<INamedTypeSymbol> GetSourceNamedTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            foreach (var nestedType in GetSourceNamedTypes(type))
            {
                yield return nestedType;
            }
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var type in GetSourceNamedTypes(childNamespace))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetSourceNamedTypes(INamedTypeSymbol type)
    {
        if (IsSourceDeclared(type))
        {
            yield return type;
        }

        foreach (var nestedType in type.GetTypeMembers())
        {
            foreach (var typeMember in GetSourceNamedTypes(nestedType))
            {
                yield return typeMember;
            }
        }
    }

    private static bool CanUseAsImplementationCandidate(INamedTypeSymbol type) =>
        IsSourceDeclared(type) &&
        type.TypeKind is TypeKind.Class or TypeKind.Struct &&
        !type.IsAbstract &&
        !type.IsStatic;

    private static bool IsSourceDeclared(INamedTypeSymbol type) =>
        type.Locations.Any(location => location.IsInSource);

    private static int GetProjectPriority(Project project, Project currentProject) =>
        project.Id == currentProject.Id ? 0 : 1;

    private static int GetDocumentPriority(INamedTypeSymbol type, Document document) =>
        type.Locations.Any(location =>
            location.SourceTree is not null &&
            !string.IsNullOrEmpty(document.FilePath) &&
            string.Equals(location.SourceTree.FilePath, document.FilePath, StringComparison.Ordinal))
                ? 0
                : 1;

    private static bool MatchesDisplayName(INamedTypeSymbol symbol, string displayName) =>
        string.Equals(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), displayName, StringComparison.Ordinal);

    private static string GetFullMetadataName(INamedTypeSymbol type)
    {
        var containingTypes = new Stack<string>();
        for (var currentType = type; currentType is not null; currentType = currentType.ContainingType)
        {
            containingTypes.Push(currentType.MetadataName);
        }

        var typeName = string.Join("+", containingTypes);
        if (type.ContainingNamespace is null || type.ContainingNamespace.IsGlobalNamespace)
        {
            return typeName;
        }

        var containingNamespace = type.ContainingNamespace.ToDisplayString();
        return string.IsNullOrEmpty(containingNamespace)
            ? typeName
            : containingNamespace + "." + typeName;
    }
}
