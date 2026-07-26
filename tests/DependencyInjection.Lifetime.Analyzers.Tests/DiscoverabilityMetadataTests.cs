using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests;

/// <summary>
/// Guards NuGet/GitHub discoverability assets: package description/tags, README funnel,
/// and product-flow visuals that ship with PackageReadmeFile.
/// </summary>
public sealed class DiscoverabilityMetadataTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")
    );

    [Fact]
    public void Analyzer_package_description_and_tags_include_high_intent_di_lifetime_terms()
    {
        var csproj = XDocument.Load(
            Path.Combine(
                RepositoryRoot,
                "src",
                "DependencyInjection.Lifetime.Analyzers",
                "DependencyInjection.Lifetime.Analyzers.csproj"
            )
        );

        var description = Assert.Single(csproj.Descendants("Description")).Value;
        var tags = Assert.Single(csproj.Descendants("PackageTags")).Value;
        var title = Assert.Single(csproj.Descendants("Title")).Value;
        var readmeFile = Assert.Single(csproj.Descendants("PackageReadmeFile")).Value;
        var version = Assert.Single(csproj.Descendants("Version")).Value;

        Assert.Equal("README.md", readmeFile);
        Assert.Contains("captive dependency", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scope leak", title, StringComparison.OrdinalIgnoreCase);

        foreach (
            var term in new[]
            {
                "dependency injection",
                "lifetime",
                "Microsoft.Extensions.DependencyInjection",
                "captive dependencies",
                "scope leaks",
                "BuildServiceProvider",
                "Roslyn",
                "ASP.NET Core",
            }
        )
        {
            Assert.True(
                description.Contains(term, StringComparison.OrdinalIgnoreCase),
                $"Analyzer Description must contain '{term}' for NuGet search discoverability."
            );
        }

        foreach (
            var tag in new[]
            {
                "lifetime",
                "service-lifetime",
                "singleton",
                "scoped",
                "transient",
                "captive-dependency",
                "scope-leak",
                "CreateScope",
                "BuildServiceProvider",
                "IServiceCollection",
                "roslyn-analyzer",
                "code-fix",
            }
        )
        {
            Assert.True(
                tags.Contains(tag, StringComparison.Ordinal),
                $"Analyzer PackageTags must include '{tag}'."
            );
        }

        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    [Fact]
    public void Readme_conversion_funnel_and_product_visuals_exist_with_resolvable_paths()
    {
        var readmePath = Path.Combine(RepositoryRoot, "README.md");
        var readme = File.ReadAllText(readmePath);
        var version = Assert
            .Single(
                XDocument
                    .Load(
                        Path.Combine(
                            RepositoryRoot,
                            "src",
                            "DependencyInjection.Lifetime.Analyzers",
                            "DependencyInjection.Lifetime.Analyzers.csproj"
                        )
                    )
                    .Descendants("Version")
            )
            .Value;

        foreach (
            var section in new[]
            {
                "## The problem",
                "## What it catches",
                "## Install",
                "## See it work",
                "## 30-second path",
                "## Feature snapshot",
                "## Compatibility",
                "## Rule Index",
            }
        )
        {
            Assert.Contains(section, readme, StringComparison.Ordinal);
        }

        Assert.Contains("PrivateAssets=\"all\"", readme, StringComparison.Ordinal);
        Assert.Contains($"Version=\"{version}\"", readme, StringComparison.Ordinal);
        Assert.Contains("DI001", readme, StringComparison.Ordinal);
        Assert.Contains("DI003", readme, StringComparison.Ordinal);
        Assert.Contains("DI015", readme, StringComparison.Ordinal);
        Assert.Contains("stays quiet", readme, StringComparison.OrdinalIgnoreCase);

        // NuGet.org requires absolute HTTPS image URLs in PackageReadmeFile content.
        const string rawBase =
            "https://raw.githubusercontent.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/main/";

        var visualAssets = new[]
        {
            "assets/flow-ide-diagnostics.svg",
            "assets/flow-before-after-fix.svg",
            "assets/flow-ci-analyzer-loop.svg",
        };

        foreach (var asset in visualAssets)
        {
            Assert.Contains(rawBase + asset, readme, StringComparison.Ordinal);
            var fullPath = Path.Combine(RepositoryRoot, asset);
            Assert.True(File.Exists(fullPath), $"Missing README visual: {asset}");
            Assert.True(new FileInfo(fullPath).Length > 0, $"Empty README visual: {asset}");
        }

        Assert.Contains(rawBase + "icon.png", readme, StringComparison.Ordinal);

        var imageRefs = Regex
            .Matches(readme, @"!\[[^\]]*\]\(([^)]+)\)")
            .Select(m => m.Groups[1].Value)
            .Concat(
                Regex
                    .Matches(readme, @"<img[^>]+src=""([^""]+)""")
                    .Select(m => m.Groups[1].Value)
            )
            .Distinct(StringComparer.Ordinal);

        foreach (var imageRef in imageRefs)
        {
            Assert.True(
                imageRef.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                $"README image must use absolute HTTPS for NuGet rendering: {imageRef}"
            );
        }
    }

    [Fact]
    public void Analyzer_packs_all_assets_for_nuget_readme_rendering()
    {
        var analyzer = XDocument.Load(
            Path.Combine(
                RepositoryRoot,
                "src",
                "DependencyInjection.Lifetime.Analyzers",
                "DependencyInjection.Lifetime.Analyzers.csproj"
            )
        );

        Assert.Contains(
            analyzer.Descendants("None"),
            n =>
                (n.Attribute("Include")?.Value ?? string.Empty).Contains(
                    "assets",
                    StringComparison.Ordinal
                )
                && string.Equals(
                    n.Attribute("Pack")?.Value,
                    "true",
                    StringComparison.OrdinalIgnoreCase
                )
                && (n.Attribute("PackagePath")?.Value ?? string.Empty).Contains(
                    "assets",
                    StringComparison.Ordinal
                )
        );
    }
}
