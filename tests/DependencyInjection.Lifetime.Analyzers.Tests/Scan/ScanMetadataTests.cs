using Xunit;
using System.Reflection;
using DependencyInjection.Lifetime.Analyzers.Scan;
using Microsoft.CodeAnalysis;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Scan;

public sealed class ScanMetadataTests
{
    [Fact]
    public void Catalog_MatchesEveryShippedDescriptor()
    {
        var descriptors = typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => (DiagnosticDescriptor)field.GetValue(null)!)
            .GroupBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        Assert.Equal(
            descriptors.Keys.Order(StringComparer.Ordinal),
            RuleCatalog.ReportedRules.Order(StringComparer.Ordinal)
        );

        foreach (var rule in RuleCatalog.Rules)
        {
            var descriptor = descriptors[rule.Id];
            Assert.Equal(descriptor.DefaultSeverity.ToString(), rule.Severity);
            Assert.False(string.IsNullOrWhiteSpace(rule.Title));
            Assert.NotNull(rule.HelpUri);
        }
    }

    [Fact]
    public void EveryRuleHasADocumentationPage()
    {
        var docsRoot = Path.Combine(RepoRoot, "docs", "rules");
        foreach (var rule in RuleCatalog.Rules)
        {
            var path = Path.Combine(docsRoot, rule.Id + ".md");
            Assert.True(File.Exists(path), $"Missing rule page: {path}");
            var text = File.ReadAllText(path);
            Assert.Contains($"# {rule.Id}", text, StringComparison.Ordinal);
            Assert.Contains("## Why it matters", text, StringComparison.Ordinal);
            Assert.Contains("## Bad", text, StringComparison.Ordinal);
            Assert.Contains("## Better", text, StringComparison.Ordinal);
            Assert.Contains("## Detection", text, StringComparison.Ordinal);
            Assert.Contains("## Suppression", text, StringComparison.Ordinal);
            Assert.Contains("## References", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryRuleHasAProfileEntry()
    {
        var profileRoot = Path.Combine(RepoRoot, "profiles");
        foreach (
            var profile in new[]
            {
                "default.editorconfig",
                "brownfield-adoption.editorconfig",
                "strict-ci.editorconfig",
                "library-author.editorconfig",
            }
        )
        {
            var path = Path.Combine(profileRoot, profile);
            Assert.True(File.Exists(path), $"Missing profile: {path}");
            var text = File.ReadAllText(path);
            foreach (var rule in RuleCatalog.Rules)
            {
                Assert.Contains(
                    $"dotnet_diagnostic.{rule.Id}.severity",
                    text,
                    StringComparison.Ordinal
                );
            }
        }
    }

    [Fact]
    public void GlobalConfig_SetsEveryRuleToDefault()
    {
        var config = ScanTargets.GlobalConfig();
        foreach (var rule in RuleCatalog.ReportedRules)
            Assert.Contains($"dotnet_diagnostic.{rule}.severity = default", config);
        Assert.Contains("is_global = true", config);
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
