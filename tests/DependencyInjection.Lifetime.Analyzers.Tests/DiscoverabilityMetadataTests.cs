using System.Security.Cryptography;
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
    public void Readme_rule_index_links_resolve_to_rule_sections()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));

        var indexRows = Regex
            .Matches(readme, @"^\| \[(DI\d{3})\]\(#([a-z0-9-]+)\) \|", RegexOptions.Multiline)
            .ToList();

        Assert.NotEmpty(indexRows);

        var sectionAnchors = Regex
            .Matches(readme, @"^## (DI\d{3}): (.+)$", RegexOptions.Multiline)
            .ToDictionary(
                match => match.Groups[1].Value,
                match => GitHubAnchor($"{match.Groups[1].Value}: {match.Groups[2].Value.Trim()}"),
                StringComparer.Ordinal
            );

        foreach (var row in indexRows)
        {
            var id = row.Groups[1].Value;
            var anchor = row.Groups[2].Value;

            Assert.True(
                sectionAnchors.TryGetValue(id, out var expectedAnchor),
                $"README Rule Index links to '#{anchor}' for {id}, but README has no '## {id}: ...' "
                    + "section. The link is dead on GitHub and NuGet, and the docs site emits no page for it."
            );

            Assert.True(
                string.Equals(expectedAnchor, anchor, StringComparison.Ordinal),
                $"README Rule Index anchor for {id} is '#{anchor}' but its section renders as '#{expectedAnchor}'."
            );
        }
    }

    [Fact]
    public void Readme_documents_every_rule_that_the_canonical_rules_reference_documents()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));
        var rulesReference = File.ReadAllText(
            Path.Combine(RepositoryRoot, "docs", "RULES.md")
        );

        var readmeRules = RuleSectionIds(readme);
        var referenceRules = RuleSectionIds(rulesReference);

        Assert.NotEmpty(referenceRules);

        var missing = referenceRules.Except(readmeRules, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0,
            "README is missing rule sections documented in docs/RULES.md: "
                + string.Join(", ", missing)
                + ". Each missing section costs a docs-site landing page and breaks its Rule Index link."
        );
    }

    [Fact]
    public void Social_card_rule_count_matches_the_documented_rule_count()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));
        var cardPath = Path.Combine(RepositoryRoot, "site-assets", "social-card.svg");

        Assert.True(File.Exists(cardPath), "Missing social card source: site-assets/social-card.svg");

        var card = File.ReadAllText(cardPath);
        var documented = RuleSectionIds(readme).Count;

        var claimed = Regex.Match(card, @"(\d+) rules");

        Assert.True(
            claimed.Success,
            "site-assets/social-card.svg no longer states a rule count; the link-preview claim and the "
                + "README would drift apart silently."
        );

        Assert.True(
            int.Parse(claimed.Groups[1].Value) == documented,
            $"site-assets/social-card.svg advertises {claimed.Groups[1].Value} rules but README documents "
                + $"{documented}. Re-render site-assets/social-card.png after correcting the number."
        );

        var rendered = new FileInfo(Path.Combine(RepositoryRoot, "site-assets", "social-card.png"));
        Assert.True(rendered.Exists, "Missing rendered social card: site-assets/social-card.png");
        Assert.True(rendered.Length > 0, "Empty rendered social card: site-assets/social-card.png");

        // og:image serves the PNG, not the SVG. Without this the SVG could be corrected and
        // the stale count would stay visible in every link preview.
        var sidecarPath = Path.Combine(
            RepositoryRoot,
            "site-assets",
            "social-card.png.source-sha256"
        );

        Assert.True(
            File.Exists(sidecarPath),
            "Missing site-assets/social-card.png.source-sha256. Run scripts/render-social-card.sh."
        );

        var renderedFrom = File.ReadAllText(sidecarPath).Trim();
        var currentSource = Convert
            .ToHexString(SHA256.HashData(File.ReadAllBytes(cardPath)))
            .ToLowerInvariant();

        Assert.True(
            string.Equals(renderedFrom, currentSource, StringComparison.OrdinalIgnoreCase),
            "site-assets/social-card.png was rendered from an older site-assets/social-card.svg, so link "
                + "previews still serve the previous artwork. Run scripts/render-social-card.sh."
        );
    }

    [Fact]
    public void Readme_table_of_contents_lists_every_rule_section()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));

        var tocIds = Regex
            .Matches(readme, @"^- \[(DI\d{3}): ", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = RuleSectionIds(readme).Except(tocIds, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0,
            "README table of contents does not list documented rules: " + string.Join(", ", missing)
        );
    }

    [Fact]
    public void Readme_rule_sections_match_the_canonical_rules_reference()
    {
        var readme = RuleSections(File.ReadAllText(Path.Combine(RepositoryRoot, "README.md")));
        var reference = RuleSections(
            File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "RULES.md"))
        );

        Assert.NotEmpty(reference);

        var drifted = reference
            .Where(entry =>
                readme.TryGetValue(entry.Key, out var body)
                && !string.Equals(body, entry.Value, StringComparison.Ordinal)
            )
            .Select(entry => entry.Key)
            .ToList();

        Assert.True(
            drifted.Count == 0,
            "README.md and docs/RULES.md describe these rules differently: "
                + string.Join(", ", drifted)
                + ". The docs site is generated from README.md, so drift publishes whichever "
                + "copy is stale. Update both when a rule's behaviour changes."
        );
    }

    /// <summary>
    /// Rule sections keyed by diagnostic id. A section runs to the next '## ' heading; the
    /// trailing horizontal rule is not part of the content.
    /// </summary>
    private static Dictionary<string, string> RuleSections(string markdown)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        var headings = Regex.Matches(markdown, @"^## (DI\d{3}): .+$", RegexOptions.Multiline);

        for (var index = 0; index < headings.Count; index++)
        {
            var start = headings[index].Index;
            var end = index + 1 < headings.Count ? headings[index + 1].Index : markdown.Length;

            var nextHeading = Regex.Match(
                markdown[(start + headings[index].Length)..],
                @"^## ",
                RegexOptions.Multiline
            );

            if (nextHeading.Success)
            {
                end = start + headings[index].Length + nextHeading.Index;
            }

            var body = markdown[start..end];
            body = Regex.Replace(body, @"\n---\s*\n*$", string.Empty).Trim();
            sections[headings[index].Groups[1].Value] = body;
        }

        return sections;
    }

    private static SortedSet<string> RuleSectionIds(string markdown) =>
        new(
            Regex
                .Matches(markdown, @"^## (DI\d{3}): ", RegexOptions.Multiline)
                .Select(match => match.Groups[1].Value),
            StringComparer.Ordinal
        );

    /// <summary>
    /// Mirrors GitHub's heading-anchor slug for the rule headings used here: lowercase, every run
    /// of non-alphanumeric characters collapsed to a single hyphen, no leading or trailing hyphen.
    /// </summary>
    private static string GitHubAnchor(string heading) =>
        Regex.Replace(Regex.Replace(heading.ToLowerInvariant(), "[^a-z0-9]+", "-"), "^-+|-+$", "");

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
