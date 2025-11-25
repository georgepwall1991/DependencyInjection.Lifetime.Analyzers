using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects conditional registration issues:
/// - TryAdd* after Add* for the same service type (TryAdd will be ignored)
/// - Multiple Add* calls for the same service type (later registration overrides earlier)
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI012_ConditionalRegistrationMisuseAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.TryAddIgnored,
            DiagnosticDescriptors.DuplicateRegistration);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var registrationCollector = RegistrationCollector.Create(compilationContext.Compilation);
            if (registrationCollector is null)
            {
                return;
            }

            // First pass: collect all registrations
            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => registrationCollector.AnalyzeInvocation(
                    (InvocationExpressionSyntax)syntaxContext.Node,
                    syntaxContext.SemanticModel),
                SyntaxKind.InvocationExpression);

            // Second pass: analyze registration order at compilation end
            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeRegistrationOrder(endContext, registrationCollector));
        });
    }

    private static void AnalyzeRegistrationOrder(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector)
    {
        // Group registrations by service type
        var registrationsByServiceType = registrationCollector.OrderedRegistrations
            .GroupBy(r => r.ServiceType, SymbolEqualityComparer.Default)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in registrationsByServiceType)
        {
            var orderedRegistrations = group.OrderBy(r => r.Order).ToList();
            AnalyzeServiceTypeRegistrations(context, orderedRegistrations);
        }
    }

    private static void AnalyzeServiceTypeRegistrations(
        CompilationAnalysisContext context,
        List<OrderedRegistration> registrations)
    {
        if (registrations.Count < 2)
        {
            return;
        }

        var serviceTypeName = registrations[0].ServiceType.Name;

        // Track the first Add* registration
        OrderedRegistration? firstAddRegistration = null;

        for (var i = 0; i < registrations.Count; i++)
        {
            var current = registrations[i];

            if (!current.IsTryAdd)
            {
                // This is an Add* registration
                if (firstAddRegistration is null)
                {
                    firstAddRegistration = current;
                }
                else
                {
                    // Duplicate Add* registration - later one overrides earlier
                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.DuplicateRegistration,
                        current.Location,
                        serviceTypeName,
                        FormatLocation(firstAddRegistration.Location));

                    context.ReportDiagnostic(diagnostic);
                }
            }
            else
            {
                // This is a TryAdd* registration
                if (firstAddRegistration is not null)
                {
                    // TryAdd after Add - TryAdd will be ignored
                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.TryAddIgnored,
                        current.Location,
                        serviceTypeName,
                        FormatLocation(firstAddRegistration.Location));

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private static string FormatLocation(Location location)
    {
        var lineSpan = location.GetLineSpan();
        if (lineSpan.IsValid)
        {
            var lineNumber = lineSpan.StartLinePosition.Line + 1;
            return $"line {lineNumber}";
        }

        return "unknown location";
    }
}
