using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DependencyInjection.Lifetime.Analyzers;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using Microsoft.CodeAnalysis.CodeFixes;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests;

public class CodeFixInventoryParityTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));

    private static readonly Regex PrimaryDiagnosticIdPattern = new(@"^DI\d+$", RegexOptions.Compiled);

    [Fact]
    public void RuleIndex_CoversPrimaryDiagnosticInventory()
    {
        var primaryDiagnosticIds = GetPrimaryDiagnosticIds();
        var ruleIndexDiagnosticIds = GetRuleIndexEntries().Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(primaryDiagnosticIds, ruleIndexDiagnosticIds);
    }

    [Fact]
    public void RuleIndex_FixableDiagnostics_MatchCodeFixProviderInventory()
    {
        var fixableDiagnosticIdsFromReadme = GetRuleIndexEntries()
            .Where(entry => entry.Value)
            .Select(entry => entry.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var fixableDiagnosticIdsFromProviders = GetCodeFixProviderDiagnosticIds();

        Assert.Equal(fixableDiagnosticIdsFromReadme, fixableDiagnosticIdsFromProviders);
    }

    [Fact]
    public void CodeFixProviders_AdvertiseOnlyPublicDiagnosticIds()
    {
        var publicDiagnosticIds = GetPublicDiagnosticIds();
        var fixableDiagnosticIdsFromProviders = GetCodeFixProviderDiagnosticIds();

        Assert.All(fixableDiagnosticIdsFromProviders, diagnosticId =>
            Assert.Contains(diagnosticId, publicDiagnosticIds));
    }

    private static string[] GetPrimaryDiagnosticIds()
    {
        return GetPublicDiagnosticIds()
            .Where(id => PrimaryDiagnosticIdPattern.IsMatch(id))
            .ToArray();
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

    private static Dictionary<string, bool> GetRuleIndexEntries()
    {
        var readmePath = Path.Combine(RepoRoot, "README.md");
        var lines = File.ReadAllLines(readmePath);
        var headerIndex = Array.FindIndex(lines, line =>
            line.Trim().Equals("| ID | Title | Default Severity | Code Fix |", StringComparison.Ordinal));

        Assert.True(headerIndex >= 0, "Could not locate the Rule Index table in README.md.");

        var entries = new Dictionary<string, bool>(StringComparer.Ordinal);
        var diagnosticIdPattern = new Regex(@"DI\d+[A-Za-z]*", RegexOptions.Compiled);

        foreach (var line in lines.Skip(headerIndex + 2))
        {
            if (!line.StartsWith("|", StringComparison.Ordinal))
            {
                break;
            }

            var columns = line.Split('|', StringSplitOptions.TrimEntries);
            if (columns.Length < 5)
            {
                continue;
            }

            var diagnosticIdMatch = diagnosticIdPattern.Match(columns[1]);
            if (!diagnosticIdMatch.Success)
            {
                continue;
            }

            var codeFixValue = columns[4];
            entries.Add(
                diagnosticIdMatch.Value,
                codeFixValue.Equals("Yes", StringComparison.OrdinalIgnoreCase));
        }

        return entries;
    }

    private static string[] GetCodeFixProviderDiagnosticIds()
    {
        return typeof(DI001_ScopeMustBeDisposedCodeFixProvider).Assembly
            .GetTypes()
            .Where(type =>
                typeof(CodeFixProvider).IsAssignableFrom(type) &&
                type is { IsAbstract: false, IsClass: true } &&
                type.Namespace == typeof(DI001_ScopeMustBeDisposedCodeFixProvider).Namespace)
            .Select(type => (CodeFixProvider)Activator.CreateInstance(type)!)
            .SelectMany(provider => provider.FixableDiagnosticIds)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }
}
