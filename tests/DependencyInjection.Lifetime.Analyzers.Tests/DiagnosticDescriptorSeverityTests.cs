using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DependencyInjection.Lifetime.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests;

public class DiagnosticDescriptorSeverityTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));

    public static IEnumerable<object[]> DefaultSeverityExpectations =>
    [
        ["DI001", DiagnosticSeverity.Warning],
        ["DI002", DiagnosticSeverity.Warning],
        ["DI003", DiagnosticSeverity.Warning],
        ["DI004", DiagnosticSeverity.Warning],
        ["DI005", DiagnosticSeverity.Warning],
        ["DI006", DiagnosticSeverity.Warning],
        ["DI007", DiagnosticSeverity.Info],
        ["DI008", DiagnosticSeverity.Warning],
        ["DI009", DiagnosticSeverity.Warning],
        ["DI010", DiagnosticSeverity.Info],
        ["DI011", DiagnosticSeverity.Info],
        ["DI012", DiagnosticSeverity.Info],
        ["DI012b", DiagnosticSeverity.Info],
        ["DI013", DiagnosticSeverity.Error],
        ["DI014", DiagnosticSeverity.Warning],
        ["DI015", DiagnosticSeverity.Warning],
        ["DI016", DiagnosticSeverity.Warning],
        ["DI017", DiagnosticSeverity.Warning],
        ["DI018", DiagnosticSeverity.Warning]
    ];

    [Theory]
    [MemberData(nameof(DefaultSeverityExpectations))]
    public void DefaultSeverity_MatchesNoiseBudget(string diagnosticId, DiagnosticSeverity expectedSeverity)
    {
        var descriptor = GetDescriptorById()[diagnosticId];

        Assert.Equal(expectedSeverity, descriptor.DefaultSeverity);
    }

    [Fact]
    public void PublicDiagnosticInventory_HasSeverityExpectationCoverage()
    {
        var publicDiagnosticIds = GetPublicDiagnosticIds();
        var severityExpectationIds = DefaultSeverityExpectations
            .Select(expectation => (string)expectation[0])
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(publicDiagnosticIds, severityExpectationIds);
    }

    [Fact]
    public void PublicDiagnosticInventory_MatchesDiagnosticDescriptors()
    {
        var publicDiagnosticIds = GetPublicDiagnosticIds();
        var descriptorIds = GetDescriptorById().Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(publicDiagnosticIds, descriptorIds);
    }

    [Fact]
    public void PublicDiagnosticInventory_IsReleaseTracked()
    {
        var publicDiagnosticIds = GetPublicDiagnosticIds();
        var releaseTrackedIds = GetReleaseTrackedDiagnosticIds();

        Assert.Equal(publicDiagnosticIds, releaseTrackedIds);
    }

    private static string[] GetPublicDiagnosticIds()
    {
        return typeof(DiagnosticIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    private static Dictionary<string, DiagnosticDescriptor> GetDescriptorById()
    {
        return typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => (DiagnosticDescriptor)field.GetValue(null)!)
            .ToDictionary(descriptor => descriptor.Id, StringComparer.Ordinal);
    }

    private static string[] GetReleaseTrackedDiagnosticIds()
    {
        var shipped = GetReleaseTrackedDiagnosticIds("AnalyzerReleases.Shipped.md");
        var unshipped = GetReleaseTrackedDiagnosticIds("AnalyzerReleases.Unshipped.md");

        return shipped
            .Concat(unshipped)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> GetReleaseTrackedDiagnosticIds(string fileName)
    {
        var filePath = Path.Combine(
            RepoRoot,
            "src",
            "DependencyInjection.Lifetime.Analyzers",
            fileName);

        var diagnosticIdPattern = new Regex(@"^DI\d+[A-Za-z]*\b", RegexOptions.Compiled);

        return File.ReadLines(filePath)
            .Select(line => line.Trim())
            .Select(line => diagnosticIdPattern.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Value);
    }
}
