#!/usr/bin/env node

import { promises as fs, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "..");

const paths = {
  readme: path.join(repoRoot, "README.md"),
  changelog: path.join(repoRoot, "CHANGELOG.md"),
  adoption: path.join(repoRoot, "docs", "ADOPTION.md"),
  rules: path.join(repoRoot, "docs", "RULES.md"),
  csproj: path.join(repoRoot, "src", "DependencyInjection.Lifetime.Analyzers", "DependencyInjection.Lifetime.Analyzers.csproj"),
};

const ruleSampleConfig = {
  DI001: {
    samplePath: "samples/SampleApp/Diagnostics/DI001/ScopeNotDisposedExamples.cs",
    highlights: [
      { label: "Sample app warning case", symbol: "Bad_ScopeNotDisposed" },
      { label: "Sample app safe pattern", symbol: "Good_UsingDeclaration" },
    ],
  },
  DI002: {
    samplePath: "samples/SampleApp/Diagnostics/DI002/ScopeEscapeExamples.cs",
    highlights: [
      { label: "Sample app warning case", symbol: "Bad_ServiceEscapesViaReturn" },
      { label: "Sample app safe pattern", symbol: "Good_UsedWithinScope" },
    ],
  },
  DI003: {
    samplePath: "samples/SampleApp/Diagnostics/DI003/CaptiveDependencyExamples.cs",
    highlights: [
      { label: "Sample app warning case", symbol: "BadSingletonWithScopedDependency" },
      { label: "Sample app safe pattern", symbol: "GoodSingletonWithScopeFactory" },
    ],
  },
  DI004: {
    samplePath: "samples/SampleApp/Diagnostics/DI004/UseAfterDisposeExamples.cs",
    highlights: [
      { label: "Sample app warning case", symbol: "Bad_UseAfterDispose" },
      { label: "Sample app safe pattern", symbol: "Good_UsedWithinScope" },
    ],
  },
  DI005: {
    samplePath: "samples/SampleApp/Diagnostics/DI005/AsyncScopeExamples.cs",
    highlights: [
      { label: "Sample app warning case", symbol: "Bad_CreateScopeInAsyncMethod" },
      { label: "Sample app safe pattern", symbol: "Good_CreateAsyncScope" },
    ],
  },
  DI006: {
    samplePath: "samples/SampleApp/Diagnostics/DI006/StaticProviderExamples.cs",
    highlights: [
      { label: "Sample app warning case", symbol: "BadStaticProviderCache" },
      { label: "Sample app safe pattern", symbol: "GoodInstanceProvider" },
    ],
  },
  DI007: {
    samplePath: "samples/SampleApp/Diagnostics/DI007/ServiceLocatorExamples.cs",
    highlights: [
      { label: "Sample app warning case", symbol: "BadServiceLocatorInConstructor" },
      { label: "Sample app safe pattern", symbol: "GoodConstructorInjection" },
    ],
  },
  DI008: {
    samplePath: "samples/SampleApp/Diagnostics/DI008/DisposableTransientExamples.cs",
    highlights: [
      { label: "Sample app warning case", symbol: "BadDisposableTransient" },
      { label: "Sample app safe pattern", symbol: "GoodScopedDisposable" },
    ],
  },
  DI009: {
    samplePath: "samples/SampleApp/Diagnostics/DI009/OpenGenericExamples.cs",
    highlights: [
      { label: "Sample app warning case", symbol: "BadRepository" },
      { label: "Sample app safe pattern", symbol: "GoodScopedRepository" },
    ],
  },
  DI010: {
    samplePath: "samples/SampleApp/Diagnostics/DI010/ConstructorOverInjectionExamples.cs",
    highlights: [
      { label: "Sample app warning case", symbol: "ConstructorOverInjectionExample" },
      { label: "Sample app safe pattern", symbol: "GoodConstructorExample" },
    ],
  },
  DI011: {
    samplePath: "samples/SampleApp/Diagnostics/DI011/ServiceProviderInjectionExamples.cs",
    highlights: [
      { label: "Sample app warning case", symbol: "ServiceProviderInjectionExample" },
      { label: "Sample app safe pattern", symbol: "ExplicitDependencyExample" },
    ],
  },
  DI012: {
    samplePath: "samples/SampleApp/Diagnostics/DI012/ConditionalRegistrationExamples.cs",
    highlights: [
      { label: "Sample app warning case", marker: "TryAdd will be ignored because ServiceA is already registered" },
      { label: "Sample app duplicate-registration case", marker: "Multiple Add calls (Duplicate Registration)" },
    ],
  },
  DI013: {
    samplePath: "samples/SampleApp/Diagnostics/DI013/ImplementationTypeMismatchExamples.cs",
    highlights: [
      { label: "Sample app safe pattern", marker: "Correct implementation type" },
      { label: "Sample app warning case", marker: "WrongType does not implement IRepository" },
    ],
  },
  DI014: {
    samplePath: "samples/SampleApp/Diagnostics/DI014/RootProviderDisposalExamples.cs",
    highlights: [
      { label: "Sample app warning case", symbol: "BadExample" },
      { label: "Sample app safe pattern", symbol: "GoodExample" },
    ],
  },
  DI015: {
    samplePath: "samples/SampleApp/Diagnostics/DI015/UnresolvableDependencyExamples.cs",
    highlights: [
      { label: "Sample app warning case", symbol: "BadUnresolvableService" },
      { label: "Sample app safe pattern", symbol: "GoodResolvableService" },
    ],
  },
  DI016: {
    samplePath: "samples/SampleApp/Diagnostics/DI016/BuildServiceProviderMisuseExamples.cs",
    highlights: [
      { label: "Sample app warning case", symbol: "RegisterBad" },
      { label: "Sample app allowed pattern", symbol: "CreateProvider" },
    ],
  },
};

const problemPages = [
  {
    slug: "objectdisposedexception-from-scoped-service",
    title: "Fix ObjectDisposedException From Scoped Services",
    description:
      "Compile-time guidance for scope leaks, services escaping scopes, and using resolved services after the scope ends in ASP.NET Core and .NET apps.",
    rules: ["DI001", "DI002", "DI004", "DI014"],
    summary:
      "These diagnostics catch the most common scope-lifetime mistakes that lead to disposed-service failures in background jobs, middleware, and startup code.",
  },
  {
    slug: "captive-dependency-in-aspnet-core",
    title: "Find Captive Dependencies In ASP.NET Core",
    description:
      "Use compile-time diagnostics to catch singleton-to-scoped and singleton-to-transient lifetime mismatches before stale state or thread-safety bugs ship.",
    rules: ["DI003", "DI009"],
    summary:
      "This page is for teams searching for captive dependency detection in ASP.NET Core, worker services, and generic-host apps.",
  },
  {
    slug: "avoid-buildserviceprovider-in-configureservices",
    title: "Avoid BuildServiceProvider In ConfigureServices",
    description:
      "Catch BuildServiceProvider misuse during service registration so composition code does not silently create a second service container.",
    rules: ["DI016", "DI014"],
    summary:
      "Use these rules when you want to stop BuildServiceProvider misuse during registration while still allowing intentional provider factory methods.",
  },
  {
    slug: "unable-to-resolve-service-for-type",
    title: "Catch Unable To Resolve Service For Type Failures Earlier",
    description:
      "Find missing registrations and implementation mismatches at compile time instead of learning about them from runtime activation exceptions.",
    rules: ["DI015", "DI013"],
    summary:
      "These diagnostics focus on missing registrations, factory paths, keyed lookups, and incompatible implementation types.",
  },
  {
    slug: "iserviceprovider-injection-and-service-locator",
    title: "Reduce IServiceProvider Injection And Service Locator Usage",
    description:
      "Surface hidden dependencies, service locator drift, and direct IServiceProvider injection in application code before it hardens into architecture debt.",
    rules: ["DI006", "DI007", "DI011"],
    summary:
      "Use these diagnostics when you want a stronger constructor-injection culture across app code and shared libraries.",
  },
  {
    slug: "disposable-transient-service",
    title: "Catch Disposable Transient Service Registrations",
    description:
      "Identify transient services implementing IDisposable or IAsyncDisposable that the container will not track or dispose automatically.",
    rules: ["DI008"],
    summary:
      "This rule is useful when teams are diagnosing resource leaks caused by transient disposables that look harmless in reviews.",
  },
  {
    slug: "constructor-over-injection",
    title: "Spot Constructor Over-Injection Early",
    description:
      "Find classes that are accumulating too many constructor dependencies so SRP drift shows up in IDE diagnostics and CI instead of code-review debates.",
    rules: ["DI010"],
    summary:
      "This rule is intentionally softer than runtime-failure diagnostics, but it is useful for preventing DI-heavy classes from becoming maintenance hotspots.",
  },
  {
    slug: "conditional-registration-and-tryadd-misuse",
    title: "Detect TryAdd And Duplicate Registration Misuse",
    description:
      "Catch ignored TryAdd calls and duplicate registrations that change single-service resolution behavior in Microsoft.Extensions.DependencyInjection.",
    rules: ["DI012"],
    summary:
      "This page targets teams debugging confusing registration order issues and silent overrides in service composition code.",
  },
  {
    slug: "createasyncscope-in-async-methods",
    title: "Use CreateAsyncScope In Async Methods",
    description:
      "Catch CreateScope usage in async flows so async-disposable services are cleaned up with the correct scope pattern.",
    rules: ["DI005"],
    summary:
      "Use this rule when background services, async handlers, or hosted-service workflows are mixing sync scopes into async disposal paths.",
  },
];

const comparisonRows = [
  {
    concern: "Captive dependencies and lifetime mismatch",
    analyzer: "Flags singleton-to-scoped/transient capture in IDE and CI.",
    runtime: "Usually discovered only under load, stale-state bugs, or production behavior.",
    review: "Easy to miss when registrations and constructors are far apart.",
  },
  {
    concern: "Scope leaks and use-after-dispose",
    analyzer: "Finds undisposed scopes, escapes, and use-after-dispose paths before runtime.",
    runtime: "Often surfaces as ObjectDisposedException or memory leaks after deployment.",
    review: "Reviewers need to reason about scope boundaries manually.",
  },
  {
    concern: "Missing registrations and bad implementation types",
    analyzer: "Reports unresolved dependencies and incompatible registrations at compile time.",
    runtime: "Startup or activation throws once the path is exercised.",
    review: "Hard to validate transitive constructor graphs by inspection.",
  },
  {
    concern: "BuildServiceProvider misuse during registration",
    analyzer: "Stops accidental second-container creation in composition code.",
    runtime: "Can remain hidden until subtle lifetime bugs or duplicate singletons appear.",
    review: "Often slips in as a convenient workaround.",
  },
  {
    concern: "Service locator drift and IServiceProvider injection",
    analyzer: "Keeps direct provider usage visible with Info-level guidance or stronger policies.",
    runtime: "No runtime guardrail unless behavior fails elsewhere.",
    review: "Frequently normalized over time unless explicitly enforced.",
  },
];

async function main() {
  const [command = "help", ...rest] = process.argv.slice(2);
  const args = parseArgs(rest);

  if (command === "sync-readme") {
    await syncReadme({ check: args.check === "true" || args.check === true });
    return;
  }

  if (command === "site") {
    const outputDir = path.resolve(args["output-dir"] ?? path.join(repoRoot, "artifacts", "site"));
    await generateSite(outputDir);
    return;
  }

  if (command === "release-notes") {
    const version = args.version;
    if (!version) {
      throw new Error("Missing required --version <x.y.z> argument for release-notes.");
    }

    const outputDir = path.resolve(args["output-dir"] ?? path.join(repoRoot, "artifacts", "release-notes"));
    await generateReleaseNotes({ version, outputDir });
    return;
  }

  if (command === "check-freshness") {
    await checkFreshness();
    return;
  }

  printUsage();
}

function printUsage() {
  console.error(
    [
      "Usage:",
      "  node tools/generate-growth-assets.mjs sync-readme [--check]",
      "  node tools/generate-growth-assets.mjs site [--output-dir <path>]",
      "  node tools/generate-growth-assets.mjs release-notes --version <x.y.z> [--output-dir <path>]",
      "  node tools/generate-growth-assets.mjs check-freshness",
    ].join("\n"),
  );
  process.exitCode = 1;
}

function parseArgs(parts) {
  const args = {};

  for (let index = 0; index < parts.length; index += 1) {
    const part = parts[index];
    if (!part.startsWith("--")) {
      continue;
    }

    const trimmed = part.slice(2);
    const [rawKey, rawValue] = trimmed.split("=", 2);

    if (rawValue !== undefined) {
      args[rawKey] = rawValue;
      continue;
    }

    const next = parts[index + 1];
    if (!next || next.startsWith("--")) {
      args[rawKey] = true;
      continue;
    }

    args[rawKey] = next;
    index += 1;
  }

  return args;
}

async function syncReadme({ check }) {
  const metadata = await loadMetadata();
  const readme = await readText(paths.readme);
  const startMarker = "<!-- generated-install-snippets:start -->";
  const endMarker = "<!-- generated-install-snippets:end -->";
  const replacement = `${startMarker}\n${buildReadmeInstallSnippets(metadata)}\n${endMarker}`;
  const nextReadme = replaceBetweenMarkers(readme, startMarker, endMarker, replacement);

  if (nextReadme === readme) {
    console.log("README install snippets are already in sync.");
    return;
  }

  if (check) {
    console.error("README install snippets are out of sync. Run the sync-readme command and commit the updated README.");
    process.exitCode = 1;
    return;
  }

  await fs.writeFile(paths.readme, nextReadme, "utf8");
  console.log("Updated README install snippets.");
}

/**
 * Validates that the sample/docs wiring is fresh and consistent:
 *
 * 1. Every configured rule-page snippet (symbol or marker) can still be extracted
 *    from its mapped sample file. Missing snippets fail loudly instead of being
 *    silently dropped. (VAL-SAMPLES-004)
 *
 * 2. The set of rule IDs in ruleSampleConfig matches the set of rule-sample
 *    directories under samples/SampleApp/Diagnostics/, except for explicitly
 *    approved aliases or omissions. (VAL-SAMPLES-005)
 *
 * 3. Every configured sample file actually exists on disk. (VAL-SAMPLES-003)
 */
async function checkFreshness() {
  const failures = [];

  // --- VAL-SAMPLES-005: parity between ruleSampleConfig keys and sample dirs ---

  const sampleDiagnosticsDir = path.join(repoRoot, "samples", "SampleApp", "Diagnostics");
  let sampleDirs = [];

  try {
    const entries = await fs.readdir(sampleDiagnosticsDir);
    sampleDirs = entries.filter((e) => /^DI\d+$/.test(e)).sort();
  } catch (error) {
    failures.push(`Cannot read sample diagnostics directory '${sampleDiagnosticsDir}': ${error.message}`);
  }

  const configuredIds = Object.keys(ruleSampleConfig).sort();
  const configuredSet = new Set(configuredIds);
  const sampleDirSet = new Set(sampleDirs);

  // Rule IDs in sample dirs but not in ruleSampleConfig
  for (const dir of sampleDirs) {
    if (!configuredSet.has(dir)) {
      failures.push(
        `[VAL-SAMPLES-005] Sample directory 'samples/SampleApp/Diagnostics/${dir}/' exists but '${dir}' is not` +
          ` in ruleSampleConfig. Add it to the mapping or add it to an approved omissions list.`,
      );
    }
  }

  // Rule IDs in ruleSampleConfig but not in sample dirs
  for (const id of configuredIds) {
    if (!sampleDirSet.has(id)) {
      failures.push(
        `[VAL-SAMPLES-005] Rule '${id}' is in ruleSampleConfig but no corresponding directory` +
          ` 'samples/SampleApp/Diagnostics/${id}/' was found. Create the directory or remove the stale mapping.`,
      );
    }
  }

  // --- VAL-SAMPLES-003 / VAL-SAMPLES-004: snippet extraction freshness ---

  for (const [ruleId, config] of Object.entries(ruleSampleConfig)) {
    const samplePath = path.join(repoRoot, config.samplePath);

    // Check that the sample file itself exists
    let contents;

    try {
      contents = normalizeNewlines(readFileSyncSafe(samplePath));
    } catch (error) {
      failures.push(
        `[VAL-SAMPLES-003] Rule ${ruleId}: sample file '${config.samplePath}' cannot be read: ${error.message}`,
      );
      continue;
    }

    // Check every highlight
    for (const highlight of config.highlights) {
      if (highlight.symbol) {
        const code = extractSymbolSnippet(contents, highlight.symbol);

        if (!code) {
          failures.push(
            `[VAL-SAMPLES-004] Rule ${ruleId}: symbol '${highlight.symbol}' (label: "${highlight.label}")` +
              ` could not be extracted from '${config.samplePath}'.` +
              ` Rename the symbol in the sample file or update the mapping in ruleSampleConfig.`,
          );
        }
      } else if (highlight.marker) {
        const code = extractMarkerSnippet(contents, highlight.marker);

        if (!code) {
          failures.push(
            `[VAL-SAMPLES-004] Rule ${ruleId}: marker '${highlight.marker}' (label: "${highlight.label}")` +
              ` could not be extracted from '${config.samplePath}'.` +
              ` Update the marker comment in the sample file or update the mapping in ruleSampleConfig.`,
          );
        }
      } else {
        failures.push(
          `[VAL-SAMPLES-004] Rule ${ruleId}: highlight entry (label: "${highlight.label}") has neither` +
            ` a 'symbol' nor a 'marker' field. Each highlight must specify exactly one.`,
        );
      }
    }
  }

  if (failures.length === 0) {
    console.log(
      `Sample/docs freshness check passed. ${configuredIds.length} rule(s) verified across` +
        ` ${configuredIds.reduce((sum, id) => sum + (ruleSampleConfig[id]?.highlights?.length ?? 0), 0)} highlight(s).`,
    );
    return;
  }

  console.error(`Sample/docs freshness check FAILED (${failures.length} issue(s)):`);

  for (const failure of failures) {
    console.error(`  - ${failure}`);
  }

  process.exitCode = 1;
}

async function generateSite(outputDir) {
  const metadata = await loadMetadata();
  const site = buildSiteModel(metadata);

  await fs.rm(outputDir, { recursive: true, force: true });
  await fs.mkdir(outputDir, { recursive: true });

  await writeOutput(outputDir, "styles.css", buildStyles());
  await writeOutput(outputDir, "index.html", renderIndexPage(site));
  await writeOutput(outputDir, "rules/index.html", renderRulesIndexPage(site));
  await writeOutput(outputDir, "problems/index.html", renderProblemsIndexPage(site));
  await writeOutput(outputDir, "compare/index.html", renderComparePage(site));
  await writeOutput(outputDir, "adoption/index.html", renderAdoptionPage(site));
  await writeOutput(outputDir, "releases/latest/index.html", renderLatestReleasePage(site));
  await writeOutput(outputDir, "404.html", renderNotFoundPage(site));

  for (const rule of site.rules) {
    await writeOutput(outputDir, `rules/${rule.slug}/index.html`, renderRulePage(site, rule));
  }

  for (const page of site.problemPages) {
    await writeOutput(outputDir, `problems/${page.slug}/index.html`, renderProblemPage(site, page));
  }

  await writeOutput(outputDir, "sitemap.xml", renderSitemap(site));
  await writeOutput(outputDir, "robots.txt", renderRobots(site));
  await writeOutput(outputDir, "search-index.json", JSON.stringify(buildSearchIndex(site), null, 2));

  console.log(`Generated docs site in ${outputDir}`);
}

async function generateReleaseNotes({ version, outputDir }) {
  const metadata = await loadMetadata();
  const release = metadata.changelog.releases.find((entry) => entry.version === version);

  if (!release) {
    throw new Error(`Unable to find version ${version} in CHANGELOG.md`);
  }

  await fs.rm(outputDir, { recursive: true, force: true });
  await fs.mkdir(outputDir, { recursive: true });

  const body = buildGitHubReleaseBody(metadata, release);
  const packageReleaseNotes = buildPackageReleaseNotes(release);
  await writeOutput(outputDir, "github-release.md", body);
  await writeOutput(outputDir, "package-release-notes.txt", `${packageReleaseNotes}\n`);
  await writeOutput(
    outputDir,
    "release-summary.json",
    JSON.stringify(
      {
        version: release.version,
        date: release.date,
        packageReleaseNotes,
      },
      null,
      2,
    ),
  );

  console.log(`Generated release notes for ${version} in ${outputDir}`);
}

async function loadMetadata() {
  const [readme, changelog, adoption, rulesDoc, csproj] = await Promise.all([
    readText(paths.readme),
    readText(paths.changelog),
    readText(paths.adoption),
    readText(paths.rules),
    readText(paths.csproj),
  ]);

  const packageId = extractXmlValue(csproj, "PackageId");
  const version = extractXmlValue(csproj, "Version");
  const repositoryUrl = extractXmlValue(csproj, "RepositoryUrl");
  const baseUrl = buildBaseUrl(repositoryUrl);
  const basePath = new URL(`${baseUrl}/`).pathname.replace(/\/$/, "");
  const ruleIndex = parseRuleIndex(readme);
  const rules = parseRuleSections(readme, ruleIndex);
  const parsedChangelog = parseChangelog(changelog);

  return {
    packageId,
    version,
    repositoryUrl,
    baseUrl,
    basePath,
    readme,
    adoption,
    rulesDoc,
    rules,
    changelog: parsedChangelog,
  };
}

function buildSiteModel(metadata) {
  const latestRelease = metadata.changelog.releases.find((release) => release.version !== "Unreleased");
  const ruleMap = new Map(metadata.rules.map((rule) => [rule.id, rule]));

  const rules = metadata.rules.map((rule) => {
    const config = ruleSampleConfig[rule.id];
    return {
      ...rule,
      samplePath: config?.samplePath,
      sampleHighlights: config ? extractSampleHighlights(config) : [],
      githubSampleUrl: config ? toGitHubUrl(metadata.repositoryUrl, config.samplePath) : null,
      pagePath: `/rules/${rule.slug}/`,
    };
  });

  const enrichedRuleMap = new Map(rules.map((rule) => [rule.id, rule]));

  const problemPageModels = problemPages.map((page) => ({
    ...page,
    pagePath: `/problems/${page.slug}/`,
    rules: page.rules.map((id) => enrichedRuleMap.get(id)).filter(Boolean),
  }));

  return {
    ...metadata,
    latestRelease,
    rules,
    problemPages: problemPageModels,
    compareRows: comparisonRows,
    navigation: [
      { label: "Home", path: "/" },
      { label: "Rules", path: "/rules/" },
      { label: "Problems", path: "/problems/" },
      { label: "Compare", path: "/compare/" },
      { label: "Adoption", path: "/adoption/" },
      { label: "Latest Release", path: "/releases/latest/" },
      { label: "NuGet", href: `https://www.nuget.org/packages/${metadata.packageId}` },
    ],
    ruleMap,
  };
}

function parseRuleIndex(readme) {
  const match = readme.match(/## Rule Index\s*\n\n\| ID \| Title \| Default Severity \| Code Fix \|\n\|[-| ]+\|\n([\s\S]*?)\n\n---/);
  if (!match) {
    throw new Error("Unable to parse rule index table from README.md");
  }

  const map = new Map();
  const rows = match[1]
    .trim()
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.startsWith("|"));

  for (const row of rows) {
    const columns = row
      .split("|")
      .slice(1, -1)
      .map((column) => column.trim());

    const idMatch = columns[0].match(/\[(DI\d{3})\]/);
    if (!idMatch) {
      continue;
    }

    map.set(idMatch[1], {
      title: columns[1],
      severity: columns[2],
      codeFix: columns[3],
    });
  }

  return map;
}

function parseRuleSections(readme, ruleIndex) {
  const headingPattern = /^## (DI\d{3}): (.+)$/gm;
  const matches = [...readme.matchAll(headingPattern)];
  const results = [];

  for (let index = 0; index < matches.length; index += 1) {
    const current = matches[index];
    const next = matches[index + 1];
    const id = current[1];
    const title = current[2].trim();
    const start = current.index + current[0].length;
    const end = next ? next.index : readme.indexOf("\n## Samples");
    const body = readme.slice(start, end).trim();
    const tableEntry = ruleIndex.get(id);

    results.push({
      id,
      slug: slugify(`${id} ${title}`),
      title,
      severity: tableEntry?.severity ?? "Unknown",
      codeFixAvailability: tableEntry?.codeFix ?? "Unknown",
      whatItCatches: extractMarkdownField(body, "**What it catches:**"),
      whyItMatters: extractMarkdownField(body, "**Why it matters:**"),
      explainLikeImTen: extractMarkdownField(body, "> **Explain Like I'm Ten:**"),
      problemSnippet: extractCodeFenceAfterLabel(body, "**Problem:**"),
      betterPatternSnippet: extractCodeFenceAfterLabel(body, "**Better pattern:**"),
      codeFixSummary: extractMarkdownField(body, "**Code Fix:**"),
    });
  }

  return results;
}

function parseChangelog(markdown) {
  const normalized = normalizeNewlines(markdown);
  const headingPattern = /^## \[(.+?)\](?: - (\d{4}-\d{2}-\d{2}))?$/gm;
  const matches = [...normalized.matchAll(headingPattern)];
  const releases = [];

  for (let index = 0; index < matches.length; index += 1) {
    const current = matches[index];
    const next = matches[index + 1];
    const version = current[1];
    const start = current.index + current[0].length;
    const end = next ? next.index : normalized.length;
    const body = normalized.slice(start, end).trim();
    releases.push({
      version,
      date: current[2] ?? "",
      body,
      sections: parseChangelogSections(body),
    });
  }

  return { releases };
}

function parseChangelogSections(body) {
  const lines = body.split("\n");
  const sections = [];
  let current = null;

  for (const line of lines) {
    const headingMatch = line.match(/^### (.+)$/);
    if (headingMatch) {
      current = { title: headingMatch[1], items: [] };
      sections.push(current);
      continue;
    }

    if (line.startsWith("- ")) {
      if (!current) {
        current = { title: "Changes", items: [] };
        sections.push(current);
      }

      current.items.push(line.slice(2).trim());
      continue;
    }

    if (/^\s+- /.test(line) && current && current.items.length > 0) {
      current.items[current.items.length - 1] = `${current.items[current.items.length - 1]} ${line.trim()}`;
    }
  }

  return sections;
}

function extractSampleHighlights(config) {
  const samplePath = path.join(repoRoot, config.samplePath);
  const contents = normalizeNewlines(readFileSyncSafe(samplePath));

  return config.highlights
    .map((highlight) => {
      const code = highlight.symbol
        ? extractSymbolSnippet(contents, highlight.symbol)
        : extractMarkerSnippet(contents, highlight.marker);

      if (!code) {
        return null;
      }

      return {
        label: highlight.label,
        code,
      };
    })
    .filter(Boolean);
}

function extractSymbolSnippet(contents, symbol) {
  const lines = contents.split("\n");
  const typeDeclarationPattern = new RegExp(`\\b(class|record|interface|enum|struct)\\s+${escapeRegex(symbol)}\\b`);
  const memberDeclarationPattern = new RegExp(`\\b${escapeRegex(symbol)}\\s*\\(`);
  let start = lines.findIndex((line) => typeDeclarationPattern.test(line));

  if (start === -1) {
    start = lines.findIndex((line) => memberDeclarationPattern.test(line) && /\b(public|private|internal|protected)\b/.test(line));
  }

  if (start === -1) {
    start = lines.findIndex((line) => memberDeclarationPattern.test(line));
  }

  if (start === -1) {
    return null;
  }

  while (start > 0 && /^(?:\s*\/\/\/|\s*\/\/|\s*)$/.test(lines[start - 1])) {
    start -= 1;
  }

  let end = start;
  let balance = 0;
  let seenBrace = false;

  for (let index = start; index < lines.length; index += 1) {
    const line = lines[index];
    balance += countChars(line, "{");
    balance -= countChars(line, "}");
    if (line.includes("{")) {
      seenBrace = true;
    }

    end = index;

    if (seenBrace && balance === 0 && index > start) {
      break;
    }

    if (!seenBrace && line.trim().endsWith(";")) {
      break;
    }
  }

  return trimEmptyEdges(lines.slice(start, end + 1)).join("\n");
}

function extractMarkerSnippet(contents, marker) {
  const lines = contents.split("\n");
  const start = lines.findIndex((line) => line.includes(marker));

  if (start === -1) {
    return null;
  }

  let end = Math.min(lines.length - 1, start + 5);
  while (end + 1 < lines.length && lines[end + 1].trim() !== "") {
    end += 1;
  }

  return trimEmptyEdges(lines.slice(start, end + 1)).join("\n");
}

function buildReadmeInstallSnippets(metadata) {
  return [
    "Install from NuGet:",
    "",
    "```bash",
    `dotnet add package ${metadata.packageId} --version ${metadata.version}`,
    "```",
    "",
    "Or add a package reference directly:",
    "",
    "```xml",
    `<PackageReference Include="${metadata.packageId}" Version="${metadata.version}">`,
    "  <PrivateAssets>all</PrivateAssets>",
    "</PackageReference>",
    "```",
    "",
    "For Central Package Management (`Directory.Packages.props`):",
    "",
    "```xml",
    `<PackageVersion Include="${metadata.packageId}" Version="${metadata.version}" />`,
    "```",
    "",
    "Then reference it from the project file:",
    "",
    "```xml",
    `<PackageReference Include="${metadata.packageId}" PrivateAssets="all" />`,
    "```",
  ].join("\n");
}

function buildGitHubReleaseBody(metadata, release) {
  const siteUrl = metadata.baseUrl;
  const bullets = release.sections.flatMap((section) => section.items);
  const highlights = bullets.slice(0, 5).map((item) => `- ${item}`);
  const install = [
    "```bash",
    `dotnet add package ${metadata.packageId} --version ${release.version}`,
    "```",
  ].join("\n");
  const changeLines = release.sections
    .map((section) => {
      const items = section.items.map((item) => `- ${item}`).join("\n");
      return `### ${section.title}\n\n${items}`;
    })
    .join("\n\n");

  return normalizeNewlines(
    [
      `# ${metadata.packageId} ${release.version}`,
      "",
      "Compile-time DI diagnostics for `Microsoft.Extensions.DependencyInjection` projects that want earlier feedback on lifetime bugs, scope leaks, service locator drift, and unresolvable registrations.",
      "",
      "## Why install or upgrade",
      "",
      ...highlights,
      "",
      "## Install",
      "",
      install,
      "",
      "## What changed",
      "",
      changeLines,
      "",
      "## Learn more",
      "",
      `- Searchable docs site: ${siteUrl}/`,
      `- Rule index: ${siteUrl}/rules/`,
      `- Problem guides: ${siteUrl}/problems/`,
      `- Adoption guide: ${siteUrl}/adoption/`,
      `- NuGet package: https://www.nuget.org/packages/${metadata.packageId}`,
    ].join("\n"),
  );
}

function buildPackageReleaseNotes(release) {
  const bullets = release.sections.flatMap((section) => section.items).slice(0, 4);
  return bullets.join(" | ");
}

function renderIndexPage(site) {
  const rulesIndexHref = siteHref(site, "/rules/");
  const problemsIndexHref = siteHref(site, "/problems/");
  const adoptionHref = siteHref(site, "/adoption/");
  const compareHref = siteHref(site, "/compare/");
  const latestReleaseHref = siteHref(site, "/releases/latest/");
  const latestReleaseItems = site.latestRelease.sections
    .flatMap((section) => section.items)
    .slice(0, 4)
    .map((item) => `<li>${renderInline(item)}</li>`)
    .join("");

  const featuredRules = ["DI003", "DI015", "DI016", "DI007", "DI001", "DI014"]
    .map((id) => site.rules.find((rule) => rule.id === id))
    .filter(Boolean)
    .map(
      (rule) => `
        <article class="card">
          <p class="eyebrow">${rule.id}</p>
          <h3><a href="${siteHref(site, rule.pagePath)}">${escapeHtml(rule.title)}</a></h3>
          <p>${escapeHtml(rule.whatItCatches)}</p>
          <p class="meta">Severity: ${escapeHtml(rule.severity)} · Code fix: ${escapeHtml(rule.codeFixAvailability)}</p>
        </article>`,
    )
    .join("");

  const problemCards = site.problemPages
    .map(
      (page) => `
        <article class="card">
          <p class="eyebrow">Problem guide</p>
          <h3><a href="${siteHref(site, page.pagePath)}">${escapeHtml(page.title)}</a></h3>
          <p>${escapeHtml(page.summary)}</p>
        </article>`,
    )
    .join("");

  const installSnippet = escapeHtml(
    `dotnet add package ${site.packageId} --version ${site.version}`,
  );

  const content = `
    <section class="hero">
      <div>
        <p class="eyebrow">Compile-time DI guardrails</p>
        <h1>${escapeHtml(site.packageId)}</h1>
        <p class="lede">Catch DI scope leaks, captive dependencies, BuildServiceProvider misuse, and unresolvable services before they become runtime bugs, flaky tests, or production-only failures.</p>
        <div class="hero-actions">
          <a class="button" href="https://www.nuget.org/packages/${site.packageId}">Install from NuGet</a>
          <a class="button button-secondary" href="${rulesIndexHref}">Browse 16 rules</a>
          <a class="button button-secondary" href="${problemsIndexHref}">Solve a specific DI problem</a>
        </div>
      </div>
      <aside class="callout">
        <p class="eyebrow">Current package version</p>
        <h2>${escapeHtml(site.version)}</h2>
        <pre><code>${installSnippet}</code></pre>
        <p class="meta">Works in Rider, Visual Studio, and dotnet build / CI.</p>
      </aside>
    </section>

    <section class="grid two-up">
      <article class="card card-contrast">
        <p class="eyebrow">Why teams install it</p>
        <ul class="stack-list">
          <li>Find captive dependencies before stale state and thread-safety bugs ship.</li>
          <li>Catch scope leaks before they become ObjectDisposedException incidents.</li>
          <li>Detect missing registrations and implementation mismatches before runtime activation fails.</li>
          <li>Push DI rules into CI instead of relying on reviewer memory.</li>
        </ul>
      </article>
      <article class="card">
        <p class="eyebrow">Latest release</p>
        <h2><a href="${latestReleaseHref}">${escapeHtml(site.latestRelease.version)}</a></h2>
        <p class="meta">${escapeHtml(site.latestRelease.date)}</p>
        <ul class="stack-list">${latestReleaseItems}</ul>
      </article>
    </section>

    <section>
      <div class="section-heading">
        <div>
          <p class="eyebrow">Featured diagnostics</p>
          <h2>High-intent landing pages for common DI bugs</h2>
        </div>
        <a class="text-link" href="${rulesIndexHref}">View all rules</a>
      </div>
      <div class="grid three-up">${featuredRules}</div>
    </section>

    <section>
      <div class="section-heading">
        <div>
          <p class="eyebrow">Search-targeted pages</p>
          <h2>Common DI failure searches mapped to the right rules</h2>
        </div>
        <a class="text-link" href="${problemsIndexHref}">See all problem guides</a>
      </div>
      <div class="grid three-up">${problemCards}</div>
    </section>

    <section class="grid two-up">
      <article class="card">
        <p class="eyebrow">Adoption</p>
        <h2><a href="${adoptionHref}">Roll it out without noise</a></h2>
        <p>Start with the default severities, promote high-confidence rules to errors, and use the sample-driven rule pages to explain the policy to the team.</p>
      </article>
      <article class="card">
        <p class="eyebrow">Compare</p>
        <h2><a href="${compareHref}">Analyzer vs runtime validation vs review</a></h2>
        <p>Use the comparison matrix when someone asks why this belongs in the build instead of code review or startup smoke tests.</p>
      </article>
    </section>
  `;

  return renderPage(site, {
    pagePath: "/",
    title: `${site.packageId} | Dependency Injection Lifetime Analyzer`,
    description:
      "Searchable rule and problem guides for compile-time DI diagnostics covering captive dependencies, scope leaks, BuildServiceProvider misuse, service locator usage, and unresolvable services.",
    content,
    structuredData: buildHomeStructuredData(site),
  });
}

function renderRulesIndexPage(site) {
  const cards = site.rules
    .map(
      (rule) => `
        <article class="card">
          <p class="eyebrow">${rule.id}</p>
          <h2><a href="${siteHref(site, rule.pagePath)}">${escapeHtml(rule.title)}</a></h2>
          <p>${escapeHtml(rule.whatItCatches)}</p>
          <p class="meta">Severity: ${escapeHtml(rule.severity)} · Code fix: ${escapeHtml(rule.codeFixAvailability)}</p>
        </article>`,
    )
    .join("");

  const content = `
    <section class="page-intro">
      <p class="eyebrow">Rules</p>
      <h1>Rule index</h1>
      <p class="lede">Browse all analyzer rules with rule summaries, README examples, and extracted sample-app snippets.</p>
    </section>
    <section class="grid three-up">${cards}</section>
  `;

  return renderPage(site, {
    pagePath: "/rules/",
    title: `Rule Index | ${site.packageId}`,
    description: "All DependencyInjection.Lifetime.Analyzers rule pages with severity, code-fix availability, and sample-backed guidance.",
    content,
  });
}

function renderRulePage(site, rule) {
  const sampleBlocks = rule.sampleHighlights
    .map(
      (highlight) => `
        <article class="card">
          <p class="eyebrow">${escapeHtml(highlight.label)}</p>
          <pre><code>${escapeHtml(highlight.code)}</code></pre>
        </article>`,
    )
    .join("");

  const relatedProblems = site.problemPages
    .filter((page) => page.rules.some((pageRule) => pageRule.id === rule.id))
    .map((page) => `<li><a href="${siteHref(site, page.pagePath)}">${escapeHtml(page.title)}</a></li>`)
    .join("");

  const content = `
    <section class="page-intro">
      <p class="eyebrow">${rule.id}</p>
      <h1>${escapeHtml(rule.title)}</h1>
      <p class="lede">${escapeHtml(rule.whatItCatches)}</p>
      <p class="meta">Default severity: ${escapeHtml(rule.severity)} · Code fix: ${escapeHtml(rule.codeFixAvailability)}</p>
    </section>

    <section class="grid two-up">
      <article class="card">
        <p class="eyebrow">Why it matters</p>
        <p>${escapeHtml(rule.whyItMatters)}</p>
        <blockquote>${escapeHtml(rule.explainLikeImTen)}</blockquote>
      </article>
      <article class="card card-contrast">
        <p class="eyebrow">Install</p>
        <pre><code>${escapeHtml(`dotnet add package ${site.packageId} --version ${site.version}`)}</code></pre>
        <p class="meta"><a href="https://www.nuget.org/packages/${site.packageId}">View the NuGet package</a></p>
      </article>
    </section>

    <section class="grid two-up">
      <article class="card">
        <p class="eyebrow">README problem example</p>
        <pre><code>${escapeHtml(rule.problemSnippet.code)}</code></pre>
      </article>
      <article class="card">
        <p class="eyebrow">README better pattern</p>
        <pre><code>${escapeHtml(rule.betterPatternSnippet.code)}</code></pre>
        <p class="meta">${escapeHtml(rule.codeFixSummary)}</p>
      </article>
    </section>

    <section>
      <div class="section-heading">
        <div>
          <p class="eyebrow">Repo sample extraction</p>
          <h2>Examples pulled from the sample app</h2>
        </div>
        ${rule.githubSampleUrl ? `<a class="text-link" href="${rule.githubSampleUrl}">Open full sample file</a>` : ""}
      </div>
      <div class="grid two-up">${sampleBlocks}</div>
    </section>

    <section class="grid two-up">
      <article class="card">
        <p class="eyebrow">Related guides</p>
        <ul class="stack-list">${relatedProblems || "<li>No problem-guide pages point here yet.</li>"}</ul>
      </article>
      <article class="card">
        <p class="eyebrow">More documentation</p>
        <ul class="stack-list">
          <li><a href="${siteHref(site, "/rules/")}">Back to rule index</a></li>
          <li><a href="${siteHref(site, "/adoption/")}">Adoption guide</a></li>
          <li><a href="${siteHref(site, "/compare/")}">Analyzer comparison matrix</a></li>
          <li><a href="${siteHref(site, "/releases/latest/")}">Latest release notes</a></li>
        </ul>
      </article>
    </section>
  `;

  return renderPage(site, {
    pagePath: rule.pagePath,
    title: `${rule.id}: ${rule.title} | ${site.packageId}`,
    description: rule.whatItCatches,
    content,
  });
}

function renderProblemsIndexPage(site) {
  const cards = site.problemPages
    .map(
      (page) => `
        <article class="card">
          <p class="eyebrow">Problem guide</p>
          <h2><a href="${siteHref(site, page.pagePath)}">${escapeHtml(page.title)}</a></h2>
          <p>${escapeHtml(page.description)}</p>
        </article>`,
    )
    .join("");

  return renderPage(site, {
    pagePath: "/problems/",
    title: `Problem Guides | ${site.packageId}`,
    description: "Search-targeted pages for common dependency injection failures such as captive dependencies, scope leaks, BuildServiceProvider misuse, and missing registrations.",
    content: `
      <section class="page-intro">
        <p class="eyebrow">Problem guides</p>
        <h1>Start from the failure you are trying to prevent</h1>
        <p class="lede">These pages are written around the search queries maintainers and teams actually use when DI issues show up in logs, reviews, or startup failures.</p>
      </section>
      <section class="grid three-up">${cards}</section>
    `,
  });
}

function renderProblemPage(site, page) {
  const ruleCards = page.rules
    .map(
      (rule) => `
        <article class="card">
          <p class="eyebrow">${rule.id}</p>
          <h2><a href="${siteHref(site, rule.pagePath)}">${escapeHtml(rule.title)}</a></h2>
          <p>${escapeHtml(rule.whatItCatches)}</p>
          <p class="meta">Severity: ${escapeHtml(rule.severity)} · Code fix: ${escapeHtml(rule.codeFixAvailability)}</p>
        </article>`,
    )
    .join("");

  const content = `
    <section class="page-intro">
      <p class="eyebrow">Problem guide</p>
      <h1>${escapeHtml(page.title)}</h1>
      <p class="lede">${escapeHtml(page.summary)}</p>
    </section>

    <section class="grid two-up">
      <article class="card">
        <p class="eyebrow">When this page is relevant</p>
        <p>${escapeHtml(page.description)}</p>
      </article>
      <article class="card card-contrast">
        <p class="eyebrow">Recommended install command</p>
        <pre><code>${escapeHtml(`dotnet add package ${site.packageId} --version ${site.version}`)}</code></pre>
      </article>
    </section>

    <section>
      <div class="section-heading">
        <div>
          <p class="eyebrow">Relevant diagnostics</p>
          <h2>The rules that cover this failure mode</h2>
        </div>
      </div>
      <div class="grid two-up">${ruleCards}</div>
    </section>
  `;

  return renderPage(site, {
    pagePath: page.pagePath,
    title: `${page.title} | ${site.packageId}`,
    description: page.description,
    content,
  });
}

function renderComparePage(site) {
  const rows = site.compareRows
    .map(
      (row) => `
        <tr>
          <th scope="row">${escapeHtml(row.concern)}</th>
          <td>${escapeHtml(row.analyzer)}</td>
          <td>${escapeHtml(row.runtime)}</td>
          <td>${escapeHtml(row.review)}</td>
        </tr>`,
    )
    .join("");

  const content = `
    <section class="page-intro">
      <p class="eyebrow">Comparison</p>
      <h1>Why put DI rules in the build?</h1>
      <p class="lede">Use this matrix when deciding what belongs in compile-time diagnostics versus runtime validation or manual review.</p>
    </section>
    <section class="card table-card">
      <table>
        <thead>
          <tr>
            <th scope="col">Concern</th>
            <th scope="col">${escapeHtml(site.packageId)}</th>
            <th scope="col">Runtime container validation</th>
            <th scope="col">Code review only</th>
          </tr>
        </thead>
        <tbody>${rows}</tbody>
      </table>
    </section>
  `;

  return renderPage(site, {
    pagePath: "/compare/",
    title: `Analyzer Comparison Matrix | ${site.packageId}`,
    description: "Compare compile-time DI analyzer coverage against runtime validation and code review for lifetime bugs, scope leaks, and registration failures.",
    content,
  });
}

function renderAdoptionPage(site) {
  const adoptionMarkdown = site.adoption
    .replace(/\]\(\.\.\/README\.md\)/g, `](${site.repositoryUrl})`)
    .replace(/\]\(\.\/RULES\.md\)/g, `](${siteHref(site, "/rules/")})`);
  const content = `
    <section class="page-intro">
      <p class="eyebrow">Adoption guide</p>
      <h1>Roll out the analyzer with a sensible severity ladder</h1>
      <p class="lede">Use this guide when you want more NuGet installs to turn into successful team adoption instead of one-time evaluation churn.</p>
    </section>
    <section class="card prose">
      ${renderMarkdown(adoptionMarkdown)}
    </section>
  `;

  return renderPage(site, {
    pagePath: "/adoption/",
    title: `Adoption Guide | ${site.packageId}`,
    description: "Install, evaluate, and roll out DependencyInjection.Lifetime.Analyzers with a staged severity policy and sample-backed documentation.",
    content,
  });
}

function renderLatestReleasePage(site) {
  const sections = site.latestRelease.sections
    .map(
      (section) => `
        <section class="card">
          <p class="eyebrow">${escapeHtml(section.title)}</p>
          <ul class="stack-list">${section.items.map((item) => `<li>${renderInline(item)}</li>`).join("")}</ul>
        </section>`,
    )
    .join("");

  const content = `
    <section class="page-intro">
      <p class="eyebrow">Latest release</p>
      <h1>${escapeHtml(site.latestRelease.version)}</h1>
      <p class="lede">${escapeHtml(site.latestRelease.date)}</p>
    </section>
    <section class="grid two-up">
      <article class="card card-contrast">
        <p class="eyebrow">Upgrade</p>
        <pre><code>${escapeHtml(`dotnet add package ${site.packageId} --version ${site.latestRelease.version}`)}</code></pre>
        <p class="meta"><a href="https://www.nuget.org/packages/${site.packageId}">Open on NuGet</a></p>
      </article>
      <article class="card">
        <p class="eyebrow">Release workflow output</p>
        <p>This page is generated from <code>CHANGELOG.md</code> so GitHub Releases, the docs site, and package surfaces all stay aligned on the same release narrative.</p>
      </article>
    </section>
    <section class="grid two-up">${sections}</section>
  `;

  return renderPage(site, {
    pagePath: "/releases/latest/",
    title: `Latest Release | ${site.packageId}`,
    description: `Latest curated release summary for ${site.packageId}, generated from CHANGELOG.md.`,
    content,
  });
}

function renderNotFoundPage(site) {
  return renderPage(site, {
    pagePath: "/404.html",
    title: `Page Not Found | ${site.packageId}`,
    description: "Page not found.",
    content: `
      <section class="page-intro">
        <p class="eyebrow">404</p>
        <h1>Page not found</h1>
        <p class="lede">Use one of the main entry points below.</p>
      </section>
      <section class="grid two-up">
        <article class="card"><h2><a href="${siteHref(site, "/")}">Home</a></h2><p>Overview, install links, and featured diagnostics.</p></article>
        <article class="card"><h2><a href="${siteHref(site, "/rules/")}">Rule index</a></h2><p>All rule landing pages.</p></article>
        <article class="card"><h2><a href="${siteHref(site, "/problems/")}">Problem guides</a></h2><p>Start from a real failure mode.</p></article>
        <article class="card"><h2><a href="${siteHref(site, "/adoption/")}">Adoption guide</a></h2><p>Rollout and severity guidance.</p></article>
      </section>
    `,
  });
}

function renderSitemap(site) {
  const pathsToPublish = [
    "/",
    "/rules/",
    "/problems/",
    "/compare/",
    "/adoption/",
    "/releases/latest/",
    ...site.rules.map((rule) => rule.pagePath),
    ...site.problemPages.map((page) => page.pagePath),
  ];

  const urls = pathsToPublish
    .map((pagePath) => `<url><loc>${escapeHtml(`${site.baseUrl}${pagePath === "/" ? "" : pagePath}`)}</loc></url>`)
    .join("");

  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">${urls}</urlset>\n`;
}

function renderRobots(site) {
  return `User-agent: *\nAllow: /\nSitemap: ${site.baseUrl}/sitemap.xml\n`;
}

function buildSearchIndex(site) {
  return [
    {
      title: site.packageId,
      path: "/",
      description:
        "Compile-time DI analyzer for Microsoft.Extensions.DependencyInjection covering scope leaks, captive dependencies, BuildServiceProvider misuse, service locator usage, and unresolvable services.",
    },
    ...site.rules.map((rule) => ({
      title: `${rule.id}: ${rule.title}`,
      path: rule.pagePath,
      description: rule.whatItCatches,
    })),
    ...site.problemPages.map((page) => ({
      title: page.title,
      path: page.pagePath,
      description: page.description,
    })),
  ];
}

function buildHomeStructuredData(site) {
  return {
    "@context": "https://schema.org",
    "@type": "SoftwareSourceCode",
    name: site.packageId,
    codeRepository: site.repositoryUrl,
    programmingLanguage: "C#",
    runtimePlatform: ".NET",
    version: site.version,
    description:
      "Roslyn analyzers for Microsoft.Extensions.DependencyInjection that catch scope leaks, captive dependencies, BuildServiceProvider misuse, service locator usage, and unresolvable services at compile time.",
  };
}

function renderPage(site, { pagePath, title, description, content, structuredData }) {
  const canonical = pagePath === "/" ? `${site.baseUrl}/` : `${site.baseUrl}${pagePath}`;
  const navigation = site.navigation
    .map((item) => {
      const href = item.href ?? siteHref(site, item.path);
      const isActive = item.path === pagePath;
      return `<a class="${isActive ? "active" : ""}" href="${href}">${escapeHtml(item.label)}</a>`;
    })
    .join("");

  const structuredDataMarkup = structuredData
    ? `<script type="application/ld+json">${JSON.stringify(structuredData)}</script>`
    : "";

  return normalizeNewlines(`<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>${escapeHtml(title)}</title>
    <meta name="description" content="${escapeHtml(description)}">
    <meta property="og:title" content="${escapeHtml(title)}">
    <meta property="og:description" content="${escapeHtml(description)}">
    <meta property="og:type" content="website">
    <meta property="og:url" content="${escapeHtml(canonical)}">
    <link rel="canonical" href="${escapeHtml(canonical)}">
    <link rel="stylesheet" href="${pagePath === "/" ? "./styles.css" : `${relativeDepth(pagePath)}styles.css`}">
    ${structuredDataMarkup}
  </head>
  <body>
    <div class="site-shell">
      <header class="site-header">
        <a class="brand" href="${pagePath === "/" ? "./" : `${relativeDepth(pagePath)}`}">${escapeHtml(site.packageId)}</a>
        <nav>${navigation}</nav>
      </header>
      <main>
        ${content}
      </main>
      <footer class="site-footer">
        <p>${escapeHtml(site.packageId)} · <a href="https://www.nuget.org/packages/${site.packageId}">NuGet</a> · <a href="${site.repositoryUrl}">GitHub</a> · <a href="${siteHref(site, "/releases/latest/")}">Latest release</a></p>
      </footer>
    </div>
  </body>
</html>`);
}

function buildStyles() {
  return normalizeNewlines(`
    :root {
      color-scheme: light;
      --bg: #f7f4ec;
      --panel: rgba(255, 255, 255, 0.9);
      --panel-strong: #17211c;
      --text: #17211c;
      --muted: #526056;
      --line: rgba(23, 33, 28, 0.12);
      --accent: #b14d24;
      --accent-strong: #8f3210;
      --accent-soft: #f3dccf;
      --shadow: 0 20px 50px rgba(23, 33, 28, 0.08);
      --radius: 24px;
      --max: 1160px;
    }

    * {
      box-sizing: border-box;
    }

    html {
      min-height: 100%;
      background:
        radial-gradient(circle at top left, rgba(177, 77, 36, 0.12), transparent 26rem),
        radial-gradient(circle at top right, rgba(23, 33, 28, 0.1), transparent 24rem),
        linear-gradient(180deg, #f7f4ec 0%, #f0ece3 100%);
      font-family: "Iowan Old Style", "Palatino Linotype", "Book Antiqua", Georgia, serif;
      color: var(--text);
    }

    body {
      margin: 0;
      min-height: 100vh;
    }

    a {
      color: inherit;
      text-decoration-color: rgba(177, 77, 36, 0.55);
      text-underline-offset: 0.18em;
    }

    pre,
    code {
      font-family: "SFMono-Regular", "Consolas", "Liberation Mono", Menlo, monospace;
    }

    .site-shell {
      width: min(calc(100% - 2rem), var(--max));
      margin: 0 auto;
      padding: 1.25rem 0 3rem;
    }

    .site-header,
    .site-footer {
      display: flex;
      gap: 1rem;
      align-items: center;
      justify-content: space-between;
      padding: 0.75rem 0 1.25rem;
    }

    .site-header nav,
    .site-footer {
      flex-wrap: wrap;
    }

    .site-header nav a {
      margin-left: 1rem;
      color: var(--muted);
      font-size: 0.97rem;
    }

    .site-header nav a.active,
    .site-header nav a:hover {
      color: var(--accent-strong);
    }

    .brand {
      font-size: 1.05rem;
      font-weight: 700;
      letter-spacing: 0.02em;
      text-decoration: none;
    }

    main {
      display: grid;
      gap: 1.5rem;
    }

    .hero,
    .grid,
    .page-intro,
    .card,
    .table-card {
      animation: fade-up 0.35s ease both;
    }

    .hero {
      display: grid;
      grid-template-columns: 1.7fr 1fr;
      gap: 1.25rem;
      padding: 2rem;
      border: 1px solid var(--line);
      border-radius: calc(var(--radius) + 8px);
      background: linear-gradient(140deg, rgba(255, 255, 255, 0.92), rgba(255, 249, 244, 0.82));
      box-shadow: var(--shadow);
    }

    .page-intro {
      padding: 1.5rem 0 0.25rem;
    }

    .card,
    .table-card,
    .callout {
      padding: 1.35rem;
      border: 1px solid var(--line);
      border-radius: var(--radius);
      background: var(--panel);
      box-shadow: var(--shadow);
      backdrop-filter: blur(10px);
    }

    .card-contrast {
      background: linear-gradient(160deg, rgba(23, 33, 28, 0.96), rgba(36, 48, 41, 0.92));
      color: #f7f4ec;
    }

    .card-contrast a,
    .card-contrast .meta {
      color: rgba(247, 244, 236, 0.86);
    }

    .hero h1,
    .page-intro h1,
    .section-heading h2,
    .card h2,
    .card h3 {
      margin: 0;
      line-height: 1.05;
      letter-spacing: -0.03em;
    }

    .hero h1,
    .page-intro h1 {
      font-size: clamp(2.6rem, 6vw, 4.5rem);
    }

    .card h2,
    .card h3,
    .callout h2 {
      font-size: clamp(1.4rem, 2.6vw, 2rem);
    }

    .lede {
      color: var(--muted);
      font-size: clamp(1.06rem, 2vw, 1.25rem);
      line-height: 1.65;
      max-width: 62ch;
      margin: 0.8rem 0 0;
    }

    .eyebrow {
      margin: 0 0 0.45rem;
      text-transform: uppercase;
      letter-spacing: 0.18em;
      font-size: 0.76rem;
      color: var(--accent-strong);
      font-weight: 700;
    }

    .meta {
      color: var(--muted);
      font-size: 0.94rem;
    }

    .grid {
      display: grid;
      gap: 1rem;
    }

    .two-up {
      grid-template-columns: repeat(2, minmax(0, 1fr));
    }

    .three-up {
      grid-template-columns: repeat(3, minmax(0, 1fr));
    }

    .stack-list {
      padding-left: 1.15rem;
      margin: 0.25rem 0 0;
      line-height: 1.7;
    }

    .hero-actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
      margin-top: 1.4rem;
    }

    .button,
    .text-link {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      min-height: 2.8rem;
      padding: 0.75rem 1.1rem;
      border-radius: 999px;
      text-decoration: none;
      font-weight: 700;
    }

    .button {
      background: var(--accent);
      color: #fff8f2;
      box-shadow: 0 12px 28px rgba(177, 77, 36, 0.25);
    }

    .button:hover {
      background: var(--accent-strong);
    }

    .button-secondary {
      background: transparent;
      color: var(--text);
      border: 1px solid var(--line);
      box-shadow: none;
    }

    .text-link {
      padding: 0;
      min-height: auto;
      color: var(--accent-strong);
    }

    .section-heading {
      display: flex;
      align-items: end;
      justify-content: space-between;
      gap: 1rem;
      margin-bottom: 1rem;
    }

    pre {
      margin: 0.5rem 0 0;
      padding: 1rem;
      overflow-x: auto;
      border-radius: 16px;
      background: #111713;
      color: #eef6ed;
      box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.06);
    }

    code {
      background: rgba(23, 33, 28, 0.07);
      border-radius: 0.4rem;
      padding: 0.08rem 0.35rem;
    }

    pre code {
      padding: 0;
      background: transparent;
    }

    blockquote {
      margin: 1rem 0 0;
      padding-left: 1rem;
      border-left: 4px solid var(--accent);
      color: var(--muted);
    }

    table {
      width: 100%;
      border-collapse: collapse;
    }

    th,
    td {
      padding: 0.85rem;
      border-bottom: 1px solid var(--line);
      text-align: left;
      vertical-align: top;
    }

    .prose h1,
    .prose h2,
    .prose h3 {
      margin-top: 0;
    }

    .prose p,
    .prose li {
      line-height: 1.7;
    }

    .site-footer {
      color: var(--muted);
      font-size: 0.92rem;
      justify-content: center;
      padding-top: 1rem;
    }

    @keyframes fade-up {
      from {
        opacity: 0;
        transform: translateY(10px);
      }

      to {
        opacity: 1;
        transform: translateY(0);
      }
    }

    @media (max-width: 960px) {
      .hero,
      .two-up,
      .three-up {
        grid-template-columns: 1fr;
      }

      .site-header,
      .section-heading {
        align-items: start;
        flex-direction: column;
      }

      .site-header nav a {
        margin: 0 1rem 0 0;
      }
    }
  `);
}

function renderMarkdown(markdown) {
  const lines = normalizeNewlines(markdown).split("\n");
  const html = [];
  let paragraph = [];
  let listType = null;
  let listItems = [];
  let inCode = false;
  let codeLang = "";
  let codeLines = [];

  function flushParagraph() {
    if (paragraph.length === 0) {
      return;
    }

    html.push(`<p>${renderInline(paragraph.join(" "))}</p>`);
    paragraph = [];
  }

  function flushList() {
    if (!listType || listItems.length === 0) {
      return;
    }

    html.push(`<${listType}>${listItems.map((item) => `<li>${renderInline(item)}</li>`).join("")}</${listType}>`);
    listType = null;
    listItems = [];
  }

  for (const line of lines) {
    if (inCode) {
      if (line.startsWith("```")) {
        html.push(`<pre><code class="language-${escapeHtml(codeLang)}">${escapeHtml(codeLines.join("\n"))}</code></pre>`);
        inCode = false;
        codeLang = "";
        codeLines = [];
        continue;
      }

      codeLines.push(line);
      continue;
    }

    if (line.startsWith("```")) {
      flushParagraph();
      flushList();
      inCode = true;
      codeLang = line.slice(3).trim();
      continue;
    }

    if (/^#{1,6}\s/.test(line)) {
      flushParagraph();
      flushList();
      const level = line.match(/^#+/)[0].length;
      html.push(`<h${level}>${renderInline(line.slice(level + 1))}</h${level}>`);
      continue;
    }

    if (line.startsWith("> ")) {
      flushParagraph();
      flushList();
      html.push(`<blockquote>${renderInline(line.slice(2))}</blockquote>`);
      continue;
    }

    if (/^\d+\.\s/.test(line)) {
      flushParagraph();
      const item = line.replace(/^\d+\.\s/, "");
      if (listType !== "ol") {
        flushList();
        listType = "ol";
      }

      listItems.push(item);
      continue;
    }

    if (line.startsWith("- ")) {
      flushParagraph();
      if (listType !== "ul") {
        flushList();
        listType = "ul";
      }

      listItems.push(line.slice(2));
      continue;
    }

    if (line.trim() === "") {
      flushParagraph();
      flushList();
      continue;
    }

    paragraph.push(line.trim());
  }

  flushParagraph();
  flushList();
  return html.join("\n");
}

function renderInline(text) {
  let html = escapeHtml(text);
  html = html.replace(/`([^`]+)`/g, (_, match) => `<code>${match}</code>`);
  html = html.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_, label, href) => `<a href="${href}">${label}</a>`);
  html = html.replace(/\*\*([^*]+)\*\*/g, (_, match) => `<strong>${match}</strong>`);
  return html;
}

function buildBaseUrl(repositoryUrl) {
  const match = repositoryUrl.match(/github\.com\/([^/]+)\/([^/]+?)(?:\.git)?$/);
  if (!match) {
    throw new Error(`Unable to derive GitHub Pages base URL from repository URL: ${repositoryUrl}`);
  }

  return `https://${match[1]}.github.io/${match[2]}`;
}

function toGitHubUrl(repositoryUrl, relativePath) {
  return `${repositoryUrl}/blob/master/${relativePath.replace(/\\/g, "/")}`;
}

function replaceBetweenMarkers(text, startMarker, endMarker, replacement) {
  const startIndex = text.indexOf(startMarker);
  const endIndex = text.indexOf(endMarker);

  if (startIndex === -1 || endIndex === -1 || endIndex < startIndex) {
    throw new Error("README markers for generated install snippets were not found.");
  }

  return `${text.slice(0, startIndex)}${replacement}${text.slice(endIndex + endMarker.length)}`;
}

function extractXmlValue(contents, elementName) {
  const match = contents.match(new RegExp(`<${elementName}>([\\s\\S]*?)</${elementName}>`));
  if (!match) {
    throw new Error(`Unable to find <${elementName}> in project file.`);
  }

  return match[1].trim();
}

function extractMarkdownField(body, label) {
  const match = body.match(new RegExp(`${escapeRegex(label)}\\s*([^\\n]+)`));
  if (!match) {
    return "";
  }

  return match[1].trim();
}

function extractCodeFenceAfterLabel(body, label) {
  const match = body.match(new RegExp(`${escapeRegex(label)}\\s*\\n\\n\`\`\`([\\w-]*)\\n([\\s\\S]*?)\\n\`\`\``));
  if (!match) {
    return { language: "", code: "" };
  }

  return {
    language: match[1],
    code: match[2].trim(),
  };
}

function relativeDepth(pagePath) {
  if (pagePath === "/" || pagePath === "/404.html") {
    return "./";
  }

  const trimmed = pagePath.replace(/^\/|\/$/g, "");
  const segments = trimmed.split("/");
  return "../".repeat(segments.length);
}

function siteHref(site, pagePath) {
  if (!pagePath || pagePath === "/") {
    return `${site.basePath}/`;
  }

  return `${site.basePath}${pagePath}`;
}

async function writeOutput(rootDir, relativePath, contents) {
  const targetPath = path.join(rootDir, relativePath);
  await fs.mkdir(path.dirname(targetPath), { recursive: true });
  await fs.writeFile(targetPath, contents, "utf8");
}

async function readText(filePath) {
  return normalizeNewlines(await fs.readFile(filePath, "utf8"));
}

function readFileSyncSafe(filePath) {
  return normalizeNewlines(readFileSync(filePath, "utf8"));
}

function normalizeNewlines(text) {
  return text.replace(/\r\n/g, "\n");
}

function escapeHtml(value) {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function escapeXml(value) {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&apos;");
}

function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function slugify(value) {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

function trimEmptyEdges(lines) {
  let start = 0;
  let end = lines.length - 1;

  while (start <= end && lines[start].trim() === "") {
    start += 1;
  }

  while (end >= start && lines[end].trim() === "") {
    end -= 1;
  }

  return lines.slice(start, end + 1);
}

function countChars(value, char) {
  return [...value].filter((entry) => entry === char).length;
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
});
