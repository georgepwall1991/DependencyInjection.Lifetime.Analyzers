using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects service locator anti-pattern - using IServiceProvider to resolve services
/// instead of constructor injection.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI007_ServiceLocatorAntiPatternAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ServiceLocatorAntiPattern);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var wellKnownTypes = WellKnownTypes.Create(compilationContext.Compilation);
            if (wellKnownTypes is null)
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzeInvocation(syntaxContext, wellKnownTypes),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, WellKnownTypes wellKnownTypes)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Get the method symbol
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        // Check if this is a GetService/GetRequiredService call
        if (!IsServiceResolutionMethod(methodSymbol, wellKnownTypes))
        {
            return;
        }

        // Get the resolved service type
        var resolvedType = GetResolvedServiceType(invocation, methodSymbol, context.SemanticModel);
        if (resolvedType is null)
        {
            return;
        }

        // Check if we're in an allowed context (factory registration, middleware Invoke, etc.)
        if (IsAllowedContext(invocation, context.SemanticModel, wellKnownTypes))
        {
            return;
        }

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.ServiceLocatorAntiPattern,
            invocation.GetLocation(),
            resolvedType.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsServiceResolutionMethod(IMethodSymbol method, WellKnownTypes wellKnownTypes)
    {
        var originalMethod = method.ReducedFrom ?? method;
        var containingType = originalMethod.ContainingType;

        if (containingType is null)
        {
            return false;
        }

        var methodName = originalMethod.Name;
        var isResolutionName = methodName == "GetService" ||
                               methodName == "GetRequiredService" ||
                               methodName == "GetServices" ||
                               methodName == "GetKeyedService" ||
                               methodName == "GetRequiredKeyedService" ||
                               methodName == "GetKeyedServices";
        if (!isResolutionName)
        {
            return false;
        }

        // ServiceProviderServiceExtensions extension methods
        if (containingType.Name == "ServiceProviderServiceExtensions" &&
            containingType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
        {
            if (originalMethod.IsExtensionMethod &&
                originalMethod.Parameters.Length > 0)
            {
                var receiverType = originalMethod.Parameters[0].Type;
                return IsSystemIServiceProvider(receiverType) ||
                       wellKnownTypes.IsKeyedServiceProvider(receiverType);
            }

            return true;
        }

        // IServiceProvider members
        if (IsSystemIServiceProvider(containingType) &&
            methodName == "GetService" &&
            originalMethod.Parameters.Length == 1)
        {
            return true;
        }

        // IKeyedServiceProvider members
        return wellKnownTypes.IsKeyedServiceProvider(containingType) &&
               methodName is "GetKeyedService" or "GetRequiredKeyedService";
    }

    private static ITypeSymbol? GetResolvedServiceType(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel semanticModel)
    {
        // For generic methods like GetService<T>, get T
        if (method.IsGenericMethod && method.TypeArguments.Length > 0)
        {
            return method.TypeArguments[0];
        }

        var serviceTypeExpression = GetInvocationArgumentExpression(invocation, method, "serviceType");
        if (serviceTypeExpression is null)
        {
            return null;
        }

        if (serviceTypeExpression is TypeOfExpressionSyntax typeOfExpression)
        {
            return semanticModel.GetTypeInfo(typeOfExpression.Type).Type;
        }

        if (TryGetLocalTypeOfExpression(serviceTypeExpression, semanticModel, out var localTypeOfExpression))
        {
            return semanticModel.GetTypeInfo(localTypeOfExpression.Type).Type;
        }

        return null;
    }

    private static bool TryGetLocalTypeOfExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out TypeOfExpressionSyntax typeOfExpression)
    {
        typeOfExpression = null!;

        if (semanticModel.GetSymbolInfo(expression).Symbol is not ILocalSymbol local ||
            local.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }

        var localSyntax = local.DeclaringSyntaxReferences[0].GetSyntax();
        if (localSyntax is not VariableDeclaratorSyntax
            {
                Initializer.Value: TypeOfExpressionSyntax initializerTypeOfExpression
            })
        {
            return false;
        }

        if (IsReassignedBeforeUse(local, localSyntax, expression, semanticModel))
        {
            return false;
        }

        typeOfExpression = initializerTypeOfExpression;
        return true;
    }

    private static bool IsReassignedBeforeUse(
        ILocalSymbol local,
        SyntaxNode localSyntax,
        ExpressionSyntax useExpression,
        SemanticModel semanticModel)
    {
        if (localSyntax.SyntaxTree != useExpression.SyntaxTree)
        {
            return true;
        }

        var executableOwner = GetExecutableOwner(localSyntax);
        if (!ReferenceEquals(executableOwner, GetExecutableOwner(useExpression)))
        {
            return true;
        }

        var containingNode = localSyntax.FirstAncestorOrSelf<BlockSyntax>() as SyntaxNode ??
                             localSyntax.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() as SyntaxNode ??
                             localSyntax.FirstAncestorOrSelf<MethodDeclarationSyntax>() as SyntaxNode ??
                             localSyntax.SyntaxTree.GetRoot();

        foreach (var assignment in containingNode.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.SpanStart <= localSyntax.Span.End ||
                assignment.SpanStart >= useExpression.SpanStart ||
                !ReferenceEquals(GetExecutableOwner(assignment), executableOwner))
            {
                continue;
            }

            if (TargetsLocal(assignment.Left, local, semanticModel))
            {
                return true;
            }
        }

        foreach (var argument in containingNode.DescendantNodes().OfType<ArgumentSyntax>())
        {
            if (argument.SpanStart <= localSyntax.Span.End ||
                argument.SpanStart >= useExpression.SpanStart ||
                !ReferenceEquals(GetExecutableOwner(argument), executableOwner) ||
                argument.RefOrOutKeyword.Kind() is not (SyntaxKind.RefKeyword or SyntaxKind.OutKeyword))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(argument.Expression).Symbol, local))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TargetsLocal(
        ExpressionSyntax expression,
        ILocalSymbol local,
        SemanticModel semanticModel)
    {
        if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(expression).Symbol, local))
        {
            return true;
        }

        return expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => TargetsLocal(parenthesized.Expression, local, semanticModel),
            TupleExpressionSyntax tuple => tuple.Arguments.Any(argument =>
                TargetsLocal(argument.Expression, local, semanticModel)),
            _ => false
        };
    }

    private static SyntaxNode? GetExecutableOwner(SyntaxNode node)
    {
        return node.AncestorsAndSelf().FirstOrDefault(static ancestor =>
            ancestor is AnonymousMethodExpressionSyntax or
                ConstructorDeclarationSyntax or
                LocalFunctionStatementSyntax or
                MethodDeclarationSyntax or
                ParenthesizedLambdaExpressionSyntax or
                SimpleLambdaExpressionSyntax);
    }

    private static ExpressionSyntax? GetInvocationArgumentExpression(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        string parameterName)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon?.Name.Identifier.Text == parameterName)
            {
                return argument.Expression;
            }
        }

        var sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var isReducedExtension = methodSymbol.ReducedFrom is not null;

        for (var i = 0; i < sourceMethod.Parameters.Length; i++)
        {
            if (sourceMethod.Parameters[i].Name != parameterName)
            {
                continue;
            }

            var argumentIndex = isReducedExtension ? i - 1 : i;
            if (argumentIndex >= 0 && argumentIndex < invocation.ArgumentList.Arguments.Count)
            {
                return invocation.ArgumentList.Arguments[argumentIndex].Expression;
            }
        }

        return null;
    }

    private static bool IsAllowedContext(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        // Walk up the syntax tree to check the context
        var node = invocation.Parent;
        while (node is not null)
        {
            switch (node)
            {
                // Check if we're inside a lambda/anonymous function (factory registration)
                case LambdaExpressionSyntax:
                case AnonymousMethodExpressionSyntax:
                    if (IsFactoryRegistrationContext(node, semanticModel))
                    {
                        return true;
                    }

                    if (IsProviderFactoryDelegateBoundary(node, semanticModel, wellKnownTypes))
                    {
                        return true;
                    }

                    break;

                case GlobalStatementSyntax:
                    return true;

                // Check if we're inside a method
                case MethodDeclarationSyntax methodDecl:
                    return IsAllowedMethod(methodDecl, semanticModel);

                // Check if we're inside a constructor
                case ConstructorDeclarationSyntax:
                    // Service locator in constructors is generally not allowed
                    return false;
            }

            node = node.Parent;
        }

        return false;
    }

    private static bool IsProviderFactoryDelegateBoundary(
        SyntaxNode lambda,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        if (!TryGetInvocationArgument(lambda, out var invocation, out var argument, out var argumentIndex))
        {
            return false;
        }

        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol ||
            !TryGetArgumentParameter(methodSymbol, argument, argumentIndex, out var parameter))
        {
            return false;
        }

        if (!IsDelegateWithServiceProviderParameter(parameter.Type))
        {
            return false;
        }

        return IsFactoryBoundaryInvocation(invocation, methodSymbol, semanticModel, wellKnownTypes);
    }

    private static bool TryGetInvocationArgument(
        SyntaxNode lambda,
        out InvocationExpressionSyntax invocation,
        out ArgumentSyntax argument,
        out int argumentIndex)
    {
        invocation = null!;
        argument = null!;
        argumentIndex = -1;

        if (lambda.Parent is not ArgumentSyntax argumentSyntax ||
            argumentSyntax.Parent is not ArgumentListSyntax argumentList ||
            argumentList.Parent is not InvocationExpressionSyntax invocationSyntax)
        {
            return false;
        }

        invocation = invocationSyntax;
        argument = argumentSyntax;
        argumentIndex = argumentList.Arguments.IndexOf(argumentSyntax);
        return argumentIndex >= 0;
    }

    private static bool IsDelegateWithServiceProviderParameter(ITypeSymbol type)
    {
        return type is INamedTypeSymbol named &&
               named.DelegateInvokeMethod is not null &&
               named.DelegateInvokeMethod.Parameters.Any(parameter => IsSystemIServiceProvider(parameter.Type));
    }

    private static bool IsFactoryBoundaryInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        return IsServiceCollectionFactoryBoundary(invocation, method, semanticModel, wellKnownTypes) ||
               IsServiceDescriptorFactoryBoundary(method) ||
               IsOptionsFactoryBoundary(invocation, method, semanticModel);
    }

    private static bool IsServiceDescriptorFactoryBoundary(IMethodSymbol method)
    {
        var originalMethod = method.ReducedFrom ?? method;
        return originalMethod.ContainingType?.Name == "ServiceDescriptor" &&
               originalMethod.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" &&
               originalMethod.Name is "Transient" or "Scoped" or "Singleton" or
                   "KeyedTransient" or "KeyedScoped" or "KeyedSingleton" or "Describe" or "DescribeKeyed";
    }

    private static bool IsServiceCollectionFactoryBoundary(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel semanticModel,
        WellKnownTypes wellKnownTypes)
    {
        var originalMethod = method.ReducedFrom ?? method;
        if (!originalMethod.Name.StartsWith("Add") && !originalMethod.Name.StartsWith("TryAdd"))
        {
            return false;
        }

        if (originalMethod.IsExtensionMethod &&
            originalMethod.Parameters.Length > 0 &&
            wellKnownTypes.IsServiceCollection(originalMethod.Parameters[0].Type))
        {
            return true;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
            return IsOrImplementsServiceCollection(receiverType, wellKnownTypes);
        }

        return false;
    }

    private static bool IsOrImplementsServiceCollection(ITypeSymbol? type, WellKnownTypes wellKnownTypes)
    {
        if (type is null)
        {
            return false;
        }

        if (wellKnownTypes.IsServiceCollection(type))
        {
            return true;
        }

        return type.AllInterfaces.Any(wellKnownTypes.IsServiceCollection);
    }

    private static bool IsOptionsFactoryBoundary(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel semanticModel)
    {
        var originalMethod = method.ReducedFrom ?? method;
        if (originalMethod.Name is not ("Configure" or "Validate" or "PostConfigure"))
        {
            return false;
        }

        if (originalMethod.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.Options")
        {
            return true;
        }

        if (originalMethod.IsExtensionMethod &&
            originalMethod.Parameters.Length > 0 &&
            IsOptionsType(originalMethod.Parameters[0].Type))
        {
            return true;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
            return IsOptionsType(receiverType);
        }

        return false;
    }

    private static bool IsOptionsType(ITypeSymbol? type)
    {
        return type is not null &&
               (type.Name is "OptionsBuilder" or "IServiceCollection" ||
                type.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.Options");
    }

    private static bool IsFactoryRegistrationContext(SyntaxNode lambda, SemanticModel semanticModel)
    {
        // Check if the lambda is an argument to an Add* method on IServiceCollection
        var parent = lambda.Parent;
        while (parent is not null)
        {
            if (parent is InvocationExpressionSyntax parentInvocation)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(parentInvocation);
                if (symbolInfo.Symbol is IMethodSymbol parentMethod)
                {
                    var originalMethod = parentMethod.ReducedFrom ?? parentMethod;
                    var containingType = originalMethod.ContainingType;

                    // Check if it's a service registration method
                    if (containingType?.Name == "ServiceCollectionServiceExtensions" &&
                        containingType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" &&
                        (originalMethod.Name.StartsWith("Add") || originalMethod.Name == "TryAdd"))
                    {
                        return true;
                    }
                }
            }

            // Don't traverse too far up
            if (parent is MethodDeclarationSyntax || parent is ClassDeclarationSyntax)
            {
                break;
            }

            parent = parent.Parent;
        }

        return false;
    }

    private static bool IsAllowedMethod(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel)
    {
        var methodName = methodDecl.Identifier.Text;

        if (methodName == "Main" && IsApplicationEntryPoint(methodDecl, semanticModel))
        {
            return true;
        }

        // Allow real ASP.NET Core middleware Invoke/InvokeAsync methods.
        if (methodName == "Invoke" || methodName == "InvokeAsync")
        {
            return IsMiddlewareInvokeMethod(methodDecl, semanticModel);
        }

        // Allow factory-shaped Create*/Build* methods, but do not let side-effect methods
        // suppress service-locator diagnostics by name alone.
        if (methodName.StartsWith("Create") || methodName.StartsWith("Build"))
        {
            return IsFactoryLikeMethod(methodDecl, semanticModel);
        }

        // Allow in hosting entry points: BackgroundService.ExecuteAsync override and IHostedService.StartAsync/StopAsync
        if (methodName is "ExecuteAsync" or "StartAsync" or "StopAsync" or
            "StartingAsync" or "StartedAsync" or "StoppingAsync" or "StoppedAsync")
        {
            if (IsHostingMethod(methodDecl, semanticModel))
            {
                return true;
            }
        }

        // Allow in OptionsBuilder.Validate / IValidateOptions.Validate / IConfigureOptions.Configure / IPostConfigureOptions.PostConfigure
        if (methodName is "Validate" or "Configure" or "PostConfigure")
        {
            if (IsOptionsConfigurationMethod(methodDecl, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsApplicationEntryPoint(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
        var entryPoint = semanticModel.Compilation.GetEntryPoint(default);
        return symbol is not null &&
               SymbolEqualityComparer.Default.Equals(symbol, entryPoint);
    }

    private static bool IsFactoryLikeMethod(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
        if (symbol is null || symbol.ReturnsVoid)
        {
            return false;
        }

        if (symbol.ReturnType is INamedTypeSymbol namedType &&
            IsNonGenericTaskLikeType(namedType))
        {
            return false;
        }

        return true;
    }

    private static bool IsNonGenericTaskLikeType(INamedTypeSymbol type)
    {
        return !type.IsGenericType &&
               type.Name is "Task" or "ValueTask" &&
               type.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
    }

    private static bool IsMiddlewareInvokeMethod(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
        if (symbol?.ContainingType is not INamedTypeSymbol)
        {
            return false;
        }

        return symbol.DeclaredAccessibility == Accessibility.Public &&
               IsTaskType(symbol.ReturnType) &&
               symbol.Parameters.Length > 0 &&
               IsAspNetCoreHttpContext(symbol.Parameters[0].Type);
    }

    private static bool IsTaskType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { IsGenericType: false } &&
               type.Name == "Task" &&
               type.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
    }

    private static bool IsAspNetCoreHttpContext(ITypeSymbol type)
    {
        return type.Name == "HttpContext" &&
               type.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Http";
    }

    private static bool IsHostingMethod(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
        if (symbol?.ContainingType is not INamedTypeSymbol containingType)
        {
            return false;
        }

        if (symbol.Name == "ExecuteAsync" &&
            symbol.IsOverride &&
            symbol.Parameters.Length == 1 &&
            IsCancellationToken(symbol.Parameters[0].Type))
        {
            for (var t = containingType.BaseType; t is not null; t = t.BaseType)
            {
                if (t.Name == "BackgroundService" &&
                    t.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.Hosting")
                {
                    return true;
                }
            }
        }

        foreach (var iface in containingType.AllInterfaces)
        {
            if (iface.Name is not ("IHostedService" or "IHostedLifecycleService") ||
                iface.ContainingNamespace?.ToDisplayString() != "Microsoft.Extensions.Hosting")
            {
                continue;
            }

            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                if (member.Name != symbol.Name)
                {
                    continue;
                }

                var implementation = containingType.FindImplementationForInterfaceMember(member);
                if (SymbolEqualityComparer.Default.Equals(implementation, symbol))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsCancellationToken(ITypeSymbol type)
    {
        return type.Name == "CancellationToken" &&
               type.ContainingNamespace?.ToDisplayString() == "System.Threading";
    }

    private static bool IsOptionsConfigurationMethod(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
        if (symbol?.ContainingType is not INamedTypeSymbol containingType)
        {
            return false;
        }

        foreach (var iface in containingType.AllInterfaces)
        {
            if (iface.Name is not ("IConfigureOptions" or "IConfigureNamedOptions" or
                    "IPostConfigureOptions" or "IValidateOptions") ||
                iface.ContainingNamespace?.ToDisplayString() != "Microsoft.Extensions.Options")
            {
                continue;
            }

            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                if (member.Name != symbol.Name)
                {
                    continue;
                }

                var implementation = containingType.FindImplementationForInterfaceMember(member);
                if (SymbolEqualityComparer.Default.Equals(implementation, symbol))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetArgumentParameter(
        IMethodSymbol method,
        ArgumentSyntax argument,
        int argumentIndex,
        out IParameterSymbol parameter)
    {
        var originalMethod = method.ReducedFrom ?? method;

        if (argument.NameColon is { } nameColon)
        {
            var argumentName = nameColon.Name.Identifier.ValueText;
            foreach (var candidate in originalMethod.Parameters)
            {
                if (candidate.Name == argumentName)
                {
                    parameter = candidate;
                    return true;
                }
            }

            parameter = null!;
            return false;
        }

        var parameterIndex = argumentIndex + (method.ReducedFrom is not null ? 1 : 0);
        if (parameterIndex >= 0 && parameterIndex < originalMethod.Parameters.Length)
        {
            parameter = originalMethod.Parameters[parameterIndex];
            return true;
        }

        parameter = null!;
        return false;
    }

    private static bool IsSystemIServiceProvider(ITypeSymbol type)
    {
        return type.Name == "IServiceProvider" &&
               type.ContainingNamespace.ToDisplayString() == "System";
    }
}
