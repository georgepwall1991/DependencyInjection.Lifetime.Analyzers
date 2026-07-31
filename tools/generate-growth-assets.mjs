#!/usr/bin/env node

import { existsSync, promises as fs, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "..");

const NOT_FOUND_PAGE_PATH = "/404.html";
const SOCIAL_CARD_PATH = "/social-card.png";
const SOCIAL_CARD_ALT =
  "DependencyInjection.Lifetime.Analyzers — catch captive dependencies and scope leaks at compile time";

/**
 * Files copied verbatim from the repository root into the generated site.
 * assets/ ships inside the NuGet package (README visuals); site-assets/ is docs-site only.
 */
const siteStaticAssets = [
  { source: "site-assets/social-card.png", target: "social-card.png" },
  { source: "assets/flow-ide-diagnostics.svg", target: "assets/flow-ide-diagnostics.svg" },
  { source: "assets/flow-before-after-fix.svg", target: "assets/flow-before-after-fix.svg" },
  { source: "assets/flow-ci-analyzer-loop.svg", target: "assets/flow-ci-analyzer-loop.svg" },
  { source: "icon.svg", target: "icon.svg" },
  { source: "icon.png", target: "icon.png" },
];

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
  DI017: {
    samplePath: "samples/SampleApp/Diagnostics/DI017/CircularDependencyExamples.cs",
    highlights: [
      { label: "Sample app circular dependency", symbol: "BadOrderService" },
      { label: "Sample app safe pattern", symbol: "GoodOrderService" },
    ],
  },
  DI018: {
    samplePath: "samples/SampleApp/Diagnostics/DI018/NonInstantiableImplementationExamples.cs",
    highlights: [
      { label: "Sample app non-instantiable type", symbol: "BadAbstractService" },
      { label: "Sample app safe pattern", symbol: "GoodConcreteService" },
    ],
  },
  DI019: {
    samplePath: "samples/SampleApp/Diagnostics/DI019/RootScopedResolutionExamples.cs",
    highlights: [
      { label: "Sample app root-provider warning", symbol: "BuildRootScopedResolutionExample" },
      { label: "Sample app scoped-provider pattern", symbol: "GoodScopedResolutionExample" },
    ],
  },
  DI020: {
    samplePath: "samples/SampleApp/Diagnostics/DI020/MiddlewareScopedServiceExamples.cs",
    highlights: [
      { label: "Sample app middleware capture warning", symbol: "Bad_MiddlewareConstructorCapture" },
      { label: "Sample app safe invoke pattern", symbol: "Good_MiddlewareInvokeResolution" },
    ],
  },
  DI021: {
    samplePath: "samples/SampleApp/Diagnostics/DI021/ConcurrentHandlerSharedStateExamples.cs",
    highlights: [
      { label: "Sample app concurrent capture warning", symbol: "Bad_TimerCallbackSharedConnection" },
      { label: "Sample app per-invocation pattern", symbol: "Good_ConnectionPerInvocation" },
    ],
  },
  DI022: {
    samplePath: "samples/SampleApp/Diagnostics/DI022/ConfigGatedHandlerCaptureExamples.cs",
    highlights: [
      { label: "Sample app config-gated capture info", symbol: "Bad_ProcessorCaptureWithUnprovenConcurrency" },
      { label: "Sample app per-message pattern", symbol: "Good_ConnectionPerMessage" },
    ],
  },
  DI023: {
    samplePath: "samples/SampleApp/Diagnostics/DI023/FireAndForgetScopeCaptureExamples.cs",
    highlights: [
      {
        label: "Sample app fire-and-forget scope-capture warning",
        symbol: "Bad_FireAndForgetScopeCapture",
      },
      {
        label: "Sample app scope-inside-background-work pattern",
        symbol: "Good_ScopeInsideBackgroundWork",
      },
    ],
  },
  DI024: {
    samplePath: "samples/SampleApp/Diagnostics/DI024/HostedServiceScopePerIterationExamples.cs",
    highlights: [
      { label: "Sample app hoisted-scope warning", symbol: "Bad_HoistedScopePollingService" },
      { label: "Sample app scope-per-iteration pattern", symbol: "Good_ScopePerIterationPollingService" },
    ],
  },
  DI025: {
    samplePath: "samples/SampleApp/Diagnostics/DI025/EventSubscriptionLeakExamples.cs",
    highlights: [
      { label: "Sample app event-subscription leak warning", symbol: "Bad_SubscribeWithoutUnsubscribe" },
      { label: "Sample app unsubscribe-in-dispose pattern", symbol: "Good_UnsubscribeInDispose" },
    ],
  },
  DI026: {
    samplePath: "samples/SampleApp/Diagnostics/DI026/EventSubscriptionLeakScopedPublisherExamples.cs",
    highlights: [
      { label: "Sample app scoped-publisher subscription info", symbol: "Bad_TransientSubscribesToScopedPublisher" },
      { label: "Sample app unsubscribe-in-dispose pattern", symbol: "Good_UnsubscribeInDispose" },
    ],
  },
  DI027: {
    samplePath: "samples/SampleApp/Diagnostics/DI027/RxSubscriptionLeakExamples.cs",
    highlights: [
      { label: "Sample app Rx subscription-leak warning", symbol: "Bad_SubscribeWithoutDispose" },
      { label: "Sample app store-and-dispose pattern", symbol: "Good_StoreAndDispose" },
    ],
  },
  DI028: {
    samplePath: "samples/SampleApp/Diagnostics/DI028/CallbackRegistrationLeakExamples.cs",
    highlights: [
      {
        label: "Sample app discarded callback-registration warning",
        symbol: "Bad_RegisterWithoutDispose",
      },
      { label: "Sample app store-and-dispose pattern", symbol: "Good_StoreAndDispose" },
    ],
  },
  DI029: {
    samplePath: "samples/SampleApp/Diagnostics/DI029/HttpClientLifetimeExamples.cs",
    highlights: [
      { label: "Sample app socket-exhaustion warning", symbol: "Bad_HttpClientPerRequest" },
      { label: "Sample app injected-factory pattern", symbol: "Good_InjectedFactory" },
    ],
  },
  DI035: {
    samplePath: "samples/SampleApp/Diagnostics/DI035/ConcurrentFanOutExamples.cs",
    highlights: [
      { label: "Sample app shared-context fan-out warning", symbol: "Bad_SharedContextFanOut" },
      { label: "Sample app scope-per-task pattern", symbol: "Good_ScopePerTaskFanOut" },
    ],
  },
  DI034: {
    samplePath: "samples/SampleApp/Diagnostics/DI034/HttpContextOffRequestExamples.cs",
    highlights: [
      { label: "Sample app off-request HttpContext warning", symbol: "Bad_AuditFromBackgroundWork" },
      {
        label: "Sample app copy-values-first pattern",
        symbol: "Good_CopyValuesBeforeBackgroundWork",
      },
    ],
  },
  DI032: {
    samplePath: "samples/SampleApp/Diagnostics/DI032/AsyncOnlyDisposableExamples.cs",
    highlights: [
      { label: "Sample app async-only disposable warning", symbol: "Bad_AsyncOnlyUploadQueue" },
      { label: "Sample app dual-disposable pattern", symbol: "Good_DualDisposableUploadQueue" },
    ],
  },
  DI036: {
    samplePath: "samples/SampleApp/Diagnostics/DI036/RegistrationAfterBuildExamples.cs",
    highlights: [
      { label: "Sample app registration-after-build note", symbol: "ConfigureBad" },
      { label: "Sample app build-last registration pattern", symbol: "ConfigureGood" },
    ],
  },
  DI037: {
    samplePath: "samples/SampleApp/Diagnostics/DI037/UnawaitedTaskEscapesScopeExamples.cs",
    highlights: [
      { label: "Sample app un-awaited task note", symbol: "ArchiveBad" },
      { label: "Sample app await-inside-scope pattern", symbol: "ArchiveGood" },
    ],
  },
  DI033: {
    samplePath: "samples/SampleApp/Diagnostics/DI033/ExternallyOwnedInstanceExamples.cs",
    highlights: [
      { label: "Sample app externally-owned instance note", symbol: "Configure" },
      { label: "Sample app container-owned registration pattern", symbol: "ConfigureGood" },
    ],
  },
  DI031: {
    samplePath: "samples/SampleApp/Diagnostics/DI031/SharedImplementationExamples.cs",
    highlights: [
      { label: "Sample app shared-implementation note", symbol: "ConfigureBad" },
      { label: "Sample app forwarded-registration pattern", symbol: "ConfigureGood" },
    ],
  },
  DI030: {
    samplePath: "samples/SampleApp/Diagnostics/DI030/UnboundedSingletonCacheExamples.cs",
    highlights: [
      { label: "Sample app unbounded-cache note", symbol: "Bad_UnboundedTenantCache" },
      {
        label: "Sample app bounded-by-eviction pattern",
        symbol: "Good_BoundedByEvictionOnRelease",
      },
    ],
  },
};

const publicDiagnosticInventoryPath = path.join(
  repoRoot,
  "src",
  "DependencyInjection.Lifetime.Analyzers",
  "DiagnosticIds.cs",
);

const approvedPublicDiagnosticParity = {
  aliases: new Map([
    // DI012b is the duplicate-registration family member that intentionally
    // rolls up to the outward-facing DI012 sample/docs surface.
    ["DI012b", "DI012"],
  ]),
  omissions: new Set(),
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
  const publicDiagnosticInventory = await loadPublicDiagnosticInventory();
  const normalizedPublicDiagnosticIds = normalizeParityIds(publicDiagnosticInventory);

  // --- VAL-SAMPLES-005: parity between ruleSampleConfig keys and sample dirs ---

  const sampleDiagnosticsDir = path.join(repoRoot, "samples", "SampleApp", "Diagnostics");
  let sampleDirs = [];

  try {
    const entries = await fs.readdir(sampleDiagnosticsDir);
    sampleDirs = entries.filter((e) => /^DI\d+[a-z]?$/.test(e)).sort();
  } catch (error) {
    failures.push(`Cannot read sample diagnostics directory '${sampleDiagnosticsDir}': ${error.message}`);
  }

  const configuredIds = Object.keys(ruleSampleConfig).sort();
  const parityInventory = {
    surfaceLabel: "public diagnostic inventory",
    ids: normalizedPublicDiagnosticIds,
    sourceLabel: path.relative(repoRoot, publicDiagnosticInventoryPath),
  };

  failures.push(
    ...compareParitySurfaces(
      parityInventory,
      {
        surfaceLabel: "SampleApp diagnostic directories",
        ids: normalizeParityIds(sampleDirs),
        sourceLabel: path.relative(repoRoot, sampleDiagnosticsDir),
      },
    ),
  );

  failures.push(
    ...compareParitySurfaces(
      parityInventory,
      {
        surfaceLabel: "sample/docs mappings",
        ids: normalizeParityIds(configuredIds),
        sourceLabel: "ruleSampleConfig",
      },
    ),
  );

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
    const rawInventoryCount = publicDiagnosticInventory.length;
    const normalizedInventoryCount = normalizedPublicDiagnosticIds.length;
    const approvedParityAdjustmentCount = rawInventoryCount - normalizedInventoryCount;
    console.log(
      `Sample/docs freshness check passed. Canonical public diagnostic inventory` +
        ` (${rawInventoryCount} id(s), ${normalizedInventoryCount} outward-facing after` +
        ` ${approvedParityAdjustmentCount} approved alias/omission adjustment(s)) matches` +
        ` ${sampleDirs.length} SampleApp diagnostic directory(s) and ${configuredIds.length}` +
        ` sample/docs mapping(s) across` +
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

async function loadPublicDiagnosticInventory() {
  let contents;

  try {
    contents = normalizeNewlines(readFileSyncSafe(publicDiagnosticInventoryPath));
  } catch (error) {
    throw new Error(`Unable to read public diagnostic inventory '${publicDiagnosticInventoryPath}': ${error.message}`);
  }

  const ids = [];
  const seen = new Set();
  const pattern = /public\s+const\s+string\s+\w+\s*=\s*"(?<id>DI\d+[a-z]?)"/gi;

  for (const match of contents.matchAll(pattern)) {
    const id = match.groups?.id;
    if (!id || seen.has(id)) {
      continue;
    }

    seen.add(id);
    ids.push(id);
  }

  if (ids.length === 0) {
    throw new Error(
      `No public diagnostic IDs could be parsed from '${publicDiagnosticInventoryPath}'.` +
        ` Expected public const string declarations in DiagnosticIds.cs.`,
    );
  }

  return ids;
}

function normalizeParityIds(ids) {
  const normalized = [];
  const seen = new Set();

  for (const id of ids) {
    const normalizedId = normalizeParityId(id);
    if (!normalizedId || seen.has(normalizedId)) {
      continue;
    }

    seen.add(normalizedId);
    normalized.push(normalizedId);
  }

  return normalized.sort();
}

function normalizeParityId(id) {
  if (approvedPublicDiagnosticParity.omissions.has(id)) {
    return null;
  }

  return approvedPublicDiagnosticParity.aliases.get(id) ?? id;
}

function compareParitySurfaces(referenceSurface, observedSurface) {
  const failures = [];
  const referenceIds = new Set(referenceSurface.ids);
  const observedIds = new Set(observedSurface.ids);

  for (const id of referenceSurface.ids) {
    if (!observedIds.has(id)) {
      failures.push(
        `[VAL-SAMPLES-005] ${referenceSurface.surfaceLabel} contains '${id}' but ${observedSurface.surfaceLabel}` +
          ` does not. Update ${observedSurface.sourceLabel} or add an approved alias/omission if the difference is intentional.`,
      );
    }
  }

  for (const id of observedSurface.ids) {
    if (!referenceIds.has(id)) {
      failures.push(
        `[VAL-SAMPLES-005] ${observedSurface.surfaceLabel} contains '${id}' but ${referenceSurface.surfaceLabel}` +
          ` does not. Update ${referenceSurface.sourceLabel} or add an approved alias/omission if the difference is intentional.`,
      );
    }
  }

  return failures;
}

async function generateSite(outputDir) {
  const metadata = await loadMetadata();
  const site = buildSiteModel(metadata);

  validateSiteModel(site);

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
  await writeOutput(outputDir, "llms.txt", renderLlmsTxt(site));
  await writeOutput(outputDir, "site.js", buildSiteScript());
  await writeOutput(outputDir, "search-index.json", JSON.stringify(buildSearchIndex(site), null, 2));

  for (const asset of siteStaticAssets) {
    const source = path.join(repoRoot, asset.source);
    const target = path.join(outputDir, asset.target);
    await fs.mkdir(path.dirname(target), { recursive: true });
    await fs.copyFile(source, target);
  }

  await verifyGeneratedSite(outputDir, site);

  console.log(
    `Generated docs site in ${outputDir}` +
      ` (${site.rules.length} rule pages, ${site.problemPages.length} problem guides,` +
      ` ${siteStaticAssets.length} static assets)`,
  );
}

/**
 * Search is only real if the index is reachable from the pages. Generating the JSON while no
 * page loads the client is the exact state this site shipped in before, and nothing caught it,
 * so the wiring is asserted against the written output rather than assumed.
 */
async function verifyGeneratedSite(outputDir, site) {
  const failures = [];

  const searchIndexPath = path.join(outputDir, "search-index.json");
  let indexEntries = [];

  try {
    indexEntries = JSON.parse(await readText(searchIndexPath));
  } catch (error) {
    failures.push(`search-index.json is missing or not valid JSON: ${error.message}`);
  }

  if (indexEntries.length < site.rules.length) {
    failures.push(
      `search-index.json holds ${indexEntries.length} entries for ${site.rules.length} rules.`,
    );
  }

  for (const entry of indexEntries) {
    const target =
      entry.path === "/"
        ? path.join(outputDir, "index.html")
        : path.join(outputDir, entry.path, "index.html");

    if (!existsSync(target)) {
      failures.push(`search-index.json points at '${entry.path}', which was not generated.`);
    }
  }

  const pages = [
    "index.html",
    "404.html",
    "rules/index.html",
    ...site.rules.map((rule) => `rules/${rule.slug}/index.html`),
  ];

  for (const page of pages) {
    const html = await readText(path.join(outputDir, page));

    if (!html.includes("site.js")) {
      failures.push(`${page} does not load site.js, so its search box would be inert.`);
    }

    if (!html.includes("data-search-root=")) {
      failures.push(`${page} has no search input.`);
    }
  }

  if (!existsSync(path.join(outputDir, "site.js"))) {
    failures.push("site.js was not written.");
  }

  if (failures.length > 0) {
    throw new Error(
      `Generated site verification failed (${failures.length} issue(s)):\n  - ${failures.join("\n  - ")}`,
    );
  }
}

/**
 * A rule whose README section cannot be parsed still produces a page — an empty one, which is
 * worse than no page at all because it is the URL search engines and release notes point at.
 * Fail the build instead.
 */
function validateSiteModel(site) {
  const failures = [];

  if (site.rules.length === 0) {
    failures.push("No rule sections were parsed from README.md.");
  }

  for (const rule of site.rules) {
    for (const field of ["whatItCatches", "whyItMatters"]) {
      if (!String(rule[field] ?? "").trim()) {
        failures.push(
          `Rule ${rule.id} has an empty '${field}'. Its page would render blank —` +
            ` check the '## ${rule.id}: ...' section in README.md.`,
        );
      }
    }

    // A worked example present in the source but missing from the model means extraction
    // failed silently, which is how every rule from DI023 on lost its fix example.
    for (const [label, snippet] of [
      ["**Problem:**", rule.problemSnippet],
      ["**Better pattern:**", rule.betterPatternSnippet],
    ]) {
      if (sectionHasFenceAfter(rule.body, label) && !snippet.code.trim()) {
        failures.push(
          `Rule ${rule.id} has a code fence after '${label}' that did not survive extraction,` +
            ` so its page would omit the example.`,
        );
      }
    }
  }

  for (const page of site.problemPages) {
    if (page.rules.length === 0) {
      failures.push(`Problem guide '${page.slug}' resolves to no rules.`);
    }
  }

  if (failures.length > 0) {
    throw new Error(`Docs site validation failed (${failures.length} issue(s)):\n  - ${failures.join("\n  - ")}`);
  }
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
    // The final rule section ends at the next non-rule heading. Anchoring on a named
    // heading instead silently emptied the last rule's page whenever a new rule was
    // appended below that heading.
    const end = next ? next.index : nextNonRuleHeadingIndex(readme, start);
    const body = readme.slice(start, end).trim();
    const tableEntry = ruleIndex.get(id);

    results.push({
      id,
      slug: slugify(`${id} ${title}`),
      title,
      severity: tableEntry?.severity ?? "Unknown",
      codeFixAvailability: tableEntry?.codeFix ?? "Unknown",
      whatItCatches: sentenceCase(extractMarkdownField(body, "**What it catches:**")),
      whyItMatters: sentenceCase(extractMarkdownField(body, "**Why it matters:**")),
      explainLikeImTen: extractMarkdownField(body, "> **Explain Like I'm Ten:**"),
      problemSnippet: extractCodeFenceAfterLabel(body, "**Problem:**"),
      betterPatternSnippet: extractCodeFenceAfterLabel(body, "**Better pattern:**"),
      guardrails: sentenceCase(extractMarkdownField(body, "**Guardrails:**")),
      codeFixSummary: extractMarkdownField(body, "**Code Fix:**"),
      body,
    });
  }

  return results;
}

/**
 * These fields continue the sentence started by their README label ("What it catches: a call
 * that ..."), so lifting them out leaves a lowercase opener in page ledes, meta descriptions,
 * and search snippets. Only a plain lowercase first word is raised — an identifier such as
 * `iterator` inside backticks, or a word with inner capitals, is left alone.
 */
function sentenceCase(text) {
  const value = String(text ?? "");

  return /^[a-z]+\b/.test(value) ? value.charAt(0).toUpperCase() + value.slice(1) : value;
}

function nextNonRuleHeadingIndex(readme, fromIndex) {
  const pattern = /^## (?!DI\d{3}:)/gm;
  pattern.lastIndex = fromIndex;
  const match = pattern.exec(readme);
  return match ? match.index : readme.length;
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
          <a class="button button-secondary" href="${rulesIndexHref}">Browse ${site.rules.length} rules</a>
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
        <article class="card" data-rule-id="${rule.id}">
          <p class="eyebrow">${rule.id}</p>
          <h2><a href="${siteHref(site, rule.pagePath)}">${escapeHtml(rule.title)}</a></h2>
          <p>${renderInline(rule.whatItCatches)}</p>
          <p class="meta"><span class="severity-badge severity-${slugify(rule.severity)}">${escapeHtml(rule.severity)}</span> Code fix: ${escapeHtml(rule.codeFixAvailability)}</p>
        </article>`,
    )
    .join("");

  const withCodeFixes = site.rules.filter((rule) =>
    rule.codeFixAvailability.toLowerCase().startsWith("yes"),
  ).length;

  const content = `
    <section class="page-intro">
      <p class="eyebrow">Rules</p>
      <h1>Rule index</h1>
      <p class="lede">All ${site.rules.length} analyzer rules with rule summaries, README examples, and extracted sample-app snippets. ${withCodeFixes} ship a code fix.</p>
    </section>
    <section class="grid three-up">${cards}</section>
  `;

  return renderPage(site, {
    pagePath: "/rules/",
    title: `All ${site.rules.length} DI Lifetime Rules | ${site.packageId}`,
    description: `All ${site.rules.length} DependencyInjection.Lifetime.Analyzers rules — captive dependencies, scope leaks, BuildServiceProvider misuse and more — with default severity, code-fix availability, and sample-backed guidance.`,
    content,
    structuredData: {
      "@context": "https://schema.org",
      "@type": "CollectionPage",
      name: `Rule index — ${site.packageId}`,
      url: `${site.baseUrl}/rules/`,
      hasPart: site.rules.map((rule) => ({
        "@type": "TechArticle",
        headline: `${rule.id}: ${rule.title}`,
        url: `${site.baseUrl}${rule.pagePath}`,
      })),
    },
    breadcrumbs: [{ label: "Rules", path: "/rules/" }],
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

  const position = site.rules.findIndex((entry) => entry.id === rule.id);
  const previous = position > 0 ? site.rules[position - 1] : null;
  const next = position >= 0 && position < site.rules.length - 1 ? site.rules[position + 1] : null;

  const siblingCards = [previous, next, site.rules[(position + 2) % site.rules.length]]
    .filter((entry, index, all) => entry && entry.id !== rule.id && all.indexOf(entry) === index)
    .map(
      (entry) => `
        <article class="card">
          <p class="eyebrow">${entry.id}</p>
          <h3><a href="${siteHref(site, entry.pagePath)}">${escapeHtml(entry.title)}</a></h3>
          <p class="meta">Severity: ${escapeHtml(entry.severity)} · Code fix: ${escapeHtml(entry.codeFixAvailability)}</p>
        </article>`,
    )
    .join("");

  const content = `
    <section class="page-intro">
      <p class="eyebrow">${rule.id}</p>
      <h1>${escapeHtml(rule.title)}</h1>
      <p class="lede">${renderInline(rule.whatItCatches)}</p>
      <p class="meta"><span class="severity-badge severity-${slugify(rule.severity)}">${escapeHtml(rule.severity)}</span> Default severity · Code fix: ${escapeHtml(rule.codeFixAvailability)}</p>
    </section>

    <section class="grid two-up">
      <article class="card">
        <p class="eyebrow">Why it matters</p>
        <p>${renderInline(rule.whyItMatters)}</p>
        <blockquote>${renderInline(rule.explainLikeImTen)}</blockquote>
      </article>
      <article class="card card-contrast">
        <p class="eyebrow">Install</p>
        <pre><code>${escapeHtml(`dotnet add package ${site.packageId} --version ${site.version}`)}</code></pre>
        <p class="meta"><a href="https://www.nuget.org/packages/${site.packageId}">View the NuGet package</a></p>
      </article>
    </section>

    <section class="grid two-up">
      ${
        rule.problemSnippet.code.trim()
          ? `<article class="card">
        <p class="eyebrow">README problem example</p>
        <pre><code>${escapeHtml(rule.problemSnippet.code)}</code></pre>
      </article>`
          : ""
      }
      ${
        rule.betterPatternSnippet.code.trim()
          ? `<article class="card">
        <p class="eyebrow">README better pattern</p>
        <pre><code>${escapeHtml(rule.betterPatternSnippet.code)}</code></pre>
        <p class="meta">${renderInline(rule.codeFixSummary)}</p>
      </article>`
          : `<article class="card">
        <p class="eyebrow">Code fix</p>
        <p class="meta">${renderInline(rule.codeFixSummary)}</p>
      </article>`
      }
    </section>

    ${
      rule.guardrails
        ? `<section>
      <div class="section-heading">
        <div>
          <p class="eyebrow">Guardrails</p>
          <h2>When ${rule.id} stays silent</h2>
        </div>
      </div>
      <article class="card prose"><p>${renderInline(rule.guardrails)}</p></article>
    </section>`
        : ""
    }

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

    <nav class="rule-pager" aria-label="Rule navigation">
      ${
        previous
          ? `<a class="rule-pager-link" rel="prev" href="${siteHref(site, previous.pagePath)}"><span>Previous rule</span><strong>${previous.id}: ${escapeHtml(previous.title)}</strong></a>`
          : "<span></span>"
      }
      ${
        next
          ? `<a class="rule-pager-link is-next" rel="next" href="${siteHref(site, next.pagePath)}"><span>Next rule</span><strong>${next.id}: ${escapeHtml(next.title)}</strong></a>`
          : "<span></span>"
      }
    </nav>

    <section>
      <div class="section-heading">
        <div>
          <p class="eyebrow">Nearby diagnostics</p>
          <h2>Other rules in this family</h2>
        </div>
        <a class="text-link" href="${siteHref(site, "/rules/")}">All ${site.rules.length} rules</a>
      </div>
      <div class="grid three-up">${siblingCards}</div>
    </section>
  `;

  return renderPage(site, {
    pagePath: rule.pagePath,
    title: `${rule.id}: ${rule.title} | ${site.packageId}`,
    description: rule.whatItCatches,
    content,
    structuredData: buildRuleStructuredData(site, rule),
    breadcrumbs: [
      { label: "Rules", path: "/rules/" },
      { label: rule.id, path: rule.pagePath },
    ],
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
    breadcrumbs: [{ label: "Problems", path: "/problems/" }],
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
    structuredData: buildProblemStructuredData(site, page),
    breadcrumbs: [
      { label: "Problems", path: "/problems/" },
      { label: page.title, path: page.pagePath },
    ],
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
    breadcrumbs: [{ label: "Compare", path: "/compare/" }],
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
    breadcrumbs: [{ label: "Adoption", path: "/adoption/" }],
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
    title: `Release ${site.latestRelease.version} | ${site.packageId}`,
    description: `Latest curated release summary for ${site.packageId} ${site.latestRelease.version}, generated from CHANGELOG.md.`,
    content,
    breadcrumbs: [{ label: `Release ${site.latestRelease.version}`, path: "/releases/latest/" }],
  });
}

function renderNotFoundPage(site) {
  return renderPage(site, {
    pagePath: NOT_FOUND_PAGE_PATH,
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
  const entries = [
    { path: "/", priority: "1.0" },
    { path: "/rules/", priority: "0.9" },
    { path: "/problems/", priority: "0.9" },
    { path: "/compare/", priority: "0.6" },
    { path: "/adoption/", priority: "0.7" },
    { path: "/releases/latest/", priority: "0.6" },
    ...site.rules.map((rule) => ({ path: rule.pagePath, priority: "0.8" })),
    ...site.problemPages.map((page) => ({ path: page.pagePath, priority: "0.8" })),
  ];

  // No <lastmod>: the site republishes on every push to main, so the only date available
  // here is the last package release, which is not when these pages changed. A wrong
  // lastmod is worse than none — crawlers discount the signal once it disagrees with what
  // they fetch.
  const urls = entries
    .map((entry) => {
      // The home canonical carries a trailing slash; the sitemap must agree or the two
      // URLs compete as separate documents.
      const loc = entry.path === "/" ? `${site.baseUrl}/` : `${site.baseUrl}${entry.path}`;
      return `<url><loc>${escapeXml(loc)}</loc><priority>${entry.priority}</priority></url>`;
    })
    .join("");

  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">${urls}</urlset>\n`;
}

function renderRobots(site) {
  return `User-agent: *\nAllow: /\n\nSitemap: ${site.baseUrl}/sitemap.xml\n`;
}

function buildSearchIndex(site) {
  return [
    {
      title: site.packageId,
      path: "/",
      kind: "Overview",
      description:
        "Compile-time DI analyzer for Microsoft.Extensions.DependencyInjection covering scope leaks, captive dependencies, BuildServiceProvider misuse, service locator usage, and unresolvable services.",
    },
    ...site.rules.map((rule) => ({
      title: `${rule.id}: ${rule.title}`,
      path: rule.pagePath,
      kind: rule.id,
      description: toMetaDescription(rule.whatItCatches),
    })),
    ...site.problemPages.map((page) => ({
      title: page.title,
      path: page.pagePath,
      kind: "Problem guide",
      description: toMetaDescription(page.description),
    })),
    ...["/rules/", "/problems/", "/compare/", "/adoption/", "/releases/latest/"].map((pagePath) => ({
      title: site.navigation.find((item) => item.path === pagePath)?.label ?? pagePath,
      path: pagePath,
      kind: "Guide",
      description: "",
    })),
  ];
}

function renderLlmsTxt(site) {
  const rules = site.rules
    .map(
      (rule) =>
        `- [${rule.id}: ${rule.title}](${site.baseUrl}${rule.pagePath}): ${toMetaDescription(rule.whatItCatches)}`,
    )
    .join("\n");

  const problems = site.problemPages
    .map((page) => `- [${page.title}](${site.baseUrl}${page.pagePath}): ${toMetaDescription(page.description)}`)
    .join("\n");

  return `# ${site.packageId}

> Roslyn analyzer package for Microsoft.Extensions.DependencyInjection. It reports dependency
> injection lifetime bugs at compile time — captive dependencies, scope leaks, undisposed scopes,
> BuildServiceProvider misuse, service locator drift, unresolvable and circular registrations —
> in Visual Studio, Rider, and dotnet build. It adds no runtime dependency and stays quiet when
> a bug cannot be proven statically.

Version ${site.version}. Install: \`dotnet add package ${site.packageId} --version ${site.version}\`
(reference it with \`PrivateAssets="all"\`).

## Rules

${rules}

## Problem guides

${problems}

## Reference

- [Rule index](${site.baseUrl}/rules/): every diagnostic with default severity and code-fix availability.
- [Adoption guide](${site.baseUrl}/adoption/): staged rollout and severity configuration.
- [Comparison](${site.baseUrl}/compare/): how static analysis differs from runtime scope validation and code review.
- [Latest release](${site.baseUrl}/releases/latest/): current release notes.
- [Source](${site.repositoryUrl}): analyzer implementation, tests, and sample app.
`;
}

function buildSiteScript() {
  return normalizeNewlines(`(function () {
  "use strict";

  function escapeText(value) {
    return String(value)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;");
  }

  function escapeAttribute(value) {
    return escapeText(value).replace(/"/g, "&quot;");
  }

  function initSearch() {
    var form = document.querySelector("[data-search-root]");
    if (!form) return;

    var input = form.querySelector("input[type=search]");
    var results = form.querySelector(".site-search-results");
    var root = form.getAttribute("data-search-root") || "./";
    var entries = null;
    var pending = null;
    var active = -1;

    form.addEventListener("submit", function (event) {
      event.preventDefault();
      var first = results.querySelector("a");
      if (first) window.location.href = first.href;
    });

    // One request, however fast the typing: without this every keystroke before the first
    // response starts another fetch, and an earlier one finishing last would render results
    // for a query the user has already moved past.
    function load() {
      if (entries) return Promise.resolve(entries);
      if (pending) return pending;

      pending = fetch(root + "search-index.json")
        .then(function (response) {
          return response.ok ? response.json() : [];
        })
        .catch(function () {
          return [];
        })
        .then(function (data) {
          entries = Array.isArray(data) ? data : [];
          pending = null;
          return entries;
        });

      return pending;
    }

    function score(entry, terms) {
      var haystack = (entry.title + " " + (entry.kind || "") + " " + (entry.description || "")).toLowerCase();
      var title = entry.title.toLowerCase();
      var total = 0;

      for (var i = 0; i < terms.length; i += 1) {
        if (haystack.indexOf(terms[i]) === -1) return -1;
        total += title.indexOf(terms[i]) === 0 ? 3 : title.indexOf(terms[i]) !== -1 ? 2 : 1;
      }

      return total;
    }

    function close() {
      results.hidden = true;
      results.innerHTML = "";
      input.setAttribute("aria-expanded", "false");
      active = -1;
    }

    function highlight(next) {
      var links = results.querySelectorAll("a");
      if (links.length === 0) return;
      active = (next + links.length) % links.length;
      for (var i = 0; i < links.length; i += 1) {
        links[i].classList.toggle("is-active", i === active);
      }
      links[active].focus();
    }

    function render(query) {
      var terms = query.toLowerCase().split(/\\s+/).filter(Boolean);
      if (terms.length === 0) return close();

      var matches = entries
        .map(function (entry) {
          return { entry: entry, score: score(entry, terms) };
        })
        .filter(function (candidate) {
          return candidate.score >= 0;
        })
        .sort(function (a, b) {
          return b.score - a.score;
        })
        .slice(0, 8);

      if (matches.length === 0) {
        results.innerHTML = '<p class="site-search-empty">No matching rule or guide.</p>';
        results.hidden = false;
        input.setAttribute("aria-expanded", "true");
        return;
      }

      results.innerHTML = matches
        .map(function (match) {
          var entry = match.entry;
          var href = root.replace(/\\/$/, "") + entry.path;
          return (
            '<a role="option" href="' + escapeAttribute(href) + '">' +
            '<span class="site-search-kind">' + escapeText(entry.kind || "Page") + "</span>" +
            "<span>" + escapeText(entry.title) + "</span>" +
            "</a>"
          );
        })
        .join("");

      results.hidden = false;
      input.setAttribute("aria-expanded", "true");
      active = -1;
    }

    input.addEventListener("input", function () {
      var query = input.value.trim();
      updateCardFilter(query);

      if (!query) return close();

      load().then(function () {
        // The box may have moved on while the index was loading.
        if (input.value.trim() !== query) return;
        render(query);
      });
    });

    input.addEventListener("focus", load);

    // On the rule index the query also filters the grid in place. It has to track the input,
    // not just the arrival query, or editing the box leaves the grid filtered by the old one.
    var cards = document.querySelectorAll("[data-rule-id]");
    var notice = null;

    function updateCardFilter(query) {
      if (cards.length === 0) return;

      var terms = query.toLowerCase().split(/\\s+/).filter(Boolean);
      var shown = 0;

      Array.prototype.forEach.call(cards, function (card) {
        var matches =
          terms.length === 0 ||
          terms.every(function (term) {
            return card.textContent.toLowerCase().indexOf(term) !== -1;
          });

        card.hidden = !matches;
        if (matches) shown += 1;
      });

      if (terms.length === 0) {
        if (notice && notice.parentNode) notice.parentNode.removeChild(notice);
        notice = null;
        return;
      }

      if (!notice) {
        notice = document.createElement("p");
        notice.className = "filter-notice";
        var intro = document.querySelector(".page-intro");
        if (intro) {
          intro.appendChild(notice);
        } else {
          notice = null;
          return;
        }
      }

      notice.textContent = shown + " of " + cards.length + ' rules match "' + query + '".';

      var reset = document.createElement("a");
      reset.href = window.location.pathname;
      reset.textContent = "Show all rules";
      notice.appendChild(document.createTextNode(" "));
      notice.appendChild(reset);
    }

    // The home page advertises a SearchAction against /rules/?q=... — honour it on arrival.
    function applyQueryParameter() {
      var query = "";

      try {
        query = (new URLSearchParams(window.location.search).get("q") || "").trim();
      } catch (error) {
        return;
      }

      if (!query) return;

      input.value = query;
      updateCardFilter(query);

      if (cards.length === 0) {
        load().then(function () {
          render(query);
        });
      }
    }

    applyQueryParameter();

    form.addEventListener("keydown", function (event) {
      if (event.key === "Escape") {
        close();
        input.focus();
      } else if (event.key === "ArrowDown") {
        event.preventDefault();
        highlight(active + 1);
      } else if (event.key === "ArrowUp") {
        event.preventDefault();
        highlight(active - 1);
      }
    });

    document.addEventListener("click", function (event) {
      if (!form.contains(event.target)) close();
    });

    document.addEventListener("keydown", function (event) {
      if (event.key === "/" && document.activeElement !== input && !/^(INPUT|TEXTAREA)$/.test(document.activeElement.tagName)) {
        event.preventDefault();
        input.focus();
      }
    });
  }

  function initCopyButtons() {
    if (!navigator.clipboard) return;

    var blocks = document.querySelectorAll("pre > code");

    Array.prototype.forEach.call(blocks, function (code) {
      var pre = code.parentNode;
      if (!code.textContent.trim()) return;

      var button = document.createElement("button");
      button.type = "button";
      button.className = "copy-button";
      button.textContent = "Copy";
      button.setAttribute("aria-label", "Copy code to clipboard");

      button.addEventListener("click", function () {
        navigator.clipboard.writeText(code.textContent).then(function () {
          button.textContent = "Copied";
          button.classList.add("is-copied");
          window.setTimeout(function () {
            button.textContent = "Copy";
            button.classList.remove("is-copied");
          }, 1600);
        });
      });

      pre.classList.add("has-copy");
      pre.appendChild(button);
    });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", function () {
      initSearch();
      initCopyButtons();
    });
  } else {
    initSearch();
    initCopyButtons();
  }
})();
`);
}

function buildHomeStructuredData(site) {
  const description =
    "Roslyn analyzers for Microsoft.Extensions.DependencyInjection that catch scope leaks, captive dependencies, BuildServiceProvider misuse, service locator usage, and unresolvable services at compile time.";

  return {
    "@context": "https://schema.org",
    "@graph": [
      {
        "@type": "WebSite",
        "@id": `${site.baseUrl}/#website`,
        url: `${site.baseUrl}/`,
        name: site.packageId,
        description,
        inLanguage: "en",
        potentialAction: {
          "@type": "SearchAction",
          target: {
            "@type": "EntryPoint",
            urlTemplate: `${site.baseUrl}/rules/?q={search_term_string}`,
          },
          "query-input": "required name=search_term_string",
        },
      },
      {
        "@type": "SoftwareSourceCode",
        "@id": `${site.baseUrl}/#software`,
        name: site.packageId,
        codeRepository: site.repositoryUrl,
        programmingLanguage: "C#",
        runtimePlatform: ".NET",
        version: site.version,
        license: "https://opensource.org/licenses/MIT",
        description,
      },
      {
        "@type": "SoftwareApplication",
        "@id": `${site.baseUrl}/#application`,
        name: site.packageId,
        applicationCategory: "DeveloperApplication",
        operatingSystem: "Windows, macOS, Linux",
        softwareVersion: site.version,
        downloadUrl: `https://www.nuget.org/packages/${site.packageId}`,
        description,
        offers: {
          "@type": "Offer",
          price: "0",
          priceCurrency: "USD",
        },
      },
    ],
  };
}

function buildRuleStructuredData(site, rule) {
  return {
    "@context": "https://schema.org",
    "@type": "TechArticle",
    headline: `${rule.id}: ${rule.title}`,
    description: toMetaDescription(rule.whatItCatches),
    url: `${site.baseUrl}${rule.pagePath}`,
    inLanguage: "en",
    articleSection: "Diagnostic rule",
    keywords: [
      rule.id,
      rule.title,
      "dependency injection",
      "service lifetime",
      "Roslyn analyzer",
      "Microsoft.Extensions.DependencyInjection",
    ].join(", "),
    about: {
      "@type": "SoftwareSourceCode",
      name: site.packageId,
      programmingLanguage: "C#",
      codeRepository: site.repositoryUrl,
    },
    isPartOf: {
      "@type": "WebSite",
      "@id": `${site.baseUrl}/#website`,
    },
  };
}

function buildProblemStructuredData(site, page) {
  return {
    "@context": "https://schema.org",
    "@type": "FAQPage",
    mainEntity: [
      {
        "@type": "Question",
        name: page.title,
        acceptedAnswer: {
          "@type": "Answer",
          text: toMetaDescription(`${page.summary} ${page.description}`),
        },
      },
      ...page.rules.map((rule) => ({
        "@type": "Question",
        name: `Which diagnostic reports this — what does ${rule.id} catch?`,
        acceptedAnswer: {
          "@type": "Answer",
          text: toMetaDescription(rule.whatItCatches),
        },
      })),
    ],
  };
}

function renderPage(site, { pagePath, title, description, content, structuredData, breadcrumbs }) {
  const canonical = pagePath === "/" ? `${site.baseUrl}/` : `${site.baseUrl}${pagePath}`;
  // 404.html is served for any missing URL while the browser keeps that URL in the address
  // bar, so a relative root would resolve its assets under the missing path. It alone must
  // address the site root absolutely.
  const root =
    pagePath === NOT_FOUND_PAGE_PATH
      ? `${site.basePath}/`
      : pagePath === "/"
        ? "./"
        : relativeDepth(pagePath);
  const metaDescription = toMetaDescription(description);
  const navigation = site.navigation
    .map((item) => {
      const href = item.href ?? siteHref(site, item.path);
      const isActive = item.path === pagePath;
      return `<a class="${isActive ? "active" : ""}" href="${href}">${escapeHtml(item.label)}</a>`;
    })
    .join("");

  const graphs = [structuredData, buildBreadcrumbStructuredData(site, breadcrumbs)].filter(Boolean);
  const structuredDataMarkup = graphs
    .map((graph) => `<script type="application/ld+json">${JSON.stringify(graph)}</script>`)
    .join("\n    ");

  const breadcrumbTrail =
    breadcrumbs && breadcrumbs.length > 0
      ? `<nav class="breadcrumbs" aria-label="Breadcrumb"><ol>${[
          { label: "Home", path: "/" },
          ...breadcrumbs,
        ]
          .map((crumb, index, all) =>
            index === all.length - 1
              ? `<li aria-current="page">${escapeHtml(crumb.label)}</li>`
              : `<li><a href="${siteHref(site, crumb.path)}">${escapeHtml(crumb.label)}</a></li>`,
          )
          .join("")}</ol></nav>`
      : "";

  return normalizeNewlines(`<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="color-scheme" content="light dark">
    <meta name="theme-color" content="#f7f4ec" media="(prefers-color-scheme: light)">
    <meta name="theme-color" content="#12161f" media="(prefers-color-scheme: dark)">
    <title>${escapeHtml(title)}</title>
    <meta name="description" content="${escapeHtml(metaDescription)}">
    <meta name="robots" content="${
      pagePath === NOT_FOUND_PAGE_PATH
        ? "noindex, follow"
        : "index, follow, max-image-preview:large, max-snippet:-1"
    }">
    <link rel="canonical" href="${escapeHtml(canonical)}">
    <meta property="og:site_name" content="${escapeHtml(site.packageId)}">
    <meta property="og:title" content="${escapeHtml(title)}">
    <meta property="og:description" content="${escapeHtml(metaDescription)}">
    <meta property="og:type" content="website">
    <meta property="og:url" content="${escapeHtml(canonical)}">
    <meta property="og:locale" content="en_GB">
    <meta property="og:image" content="${escapeHtml(`${site.baseUrl}${SOCIAL_CARD_PATH}`)}">
    <meta property="og:image:width" content="1200">
    <meta property="og:image:height" content="630">
    <meta property="og:image:alt" content="${escapeHtml(SOCIAL_CARD_ALT)}">
    <meta name="twitter:card" content="summary_large_image">
    <meta name="twitter:title" content="${escapeHtml(title)}">
    <meta name="twitter:description" content="${escapeHtml(metaDescription)}">
    <meta name="twitter:image" content="${escapeHtml(`${site.baseUrl}${SOCIAL_CARD_PATH}`)}">
    <meta name="twitter:image:alt" content="${escapeHtml(SOCIAL_CARD_ALT)}">
    <link rel="icon" href="${root}icon.svg" type="image/svg+xml">
    <link rel="icon" href="${root}icon.png" sizes="128x128" type="image/png">
    <link rel="apple-touch-icon" href="${root}icon.png">
    <link rel="stylesheet" href="${root}styles.css">
    ${structuredDataMarkup}
  </head>
  <body>
    <div class="site-shell">
      <a class="skip-link" href="#main">Skip to content</a>
      <header class="site-header">
        <a class="brand" href="${root}">${escapeHtml(site.packageId)}</a>
        <nav>${navigation}</nav>
        <form class="site-search" role="search" data-search-root="${root}" autocomplete="off">
          <label class="visually-hidden" for="site-search-input">Search the rules and problem guides</label>
          <input id="site-search-input" type="search" placeholder="Search rules (try DI003 or captive)" aria-controls="site-search-results" aria-expanded="false">
          <div id="site-search-results" class="site-search-results" role="listbox" hidden></div>
        </form>
      </header>
      ${breadcrumbTrail}
      <main id="main">
        ${content}
      </main>
      <footer class="site-footer">
        <p>${escapeHtml(site.packageId)} · <a href="https://www.nuget.org/packages/${site.packageId}">NuGet</a> · <a href="${site.repositoryUrl}">GitHub</a> · <a href="${siteHref(site, "/rules/")}">All rules</a> · <a href="${siteHref(site, "/releases/latest/")}">Latest release</a></p>
      </footer>
    </div>
    <script src="${root}site.js" defer></script>
  </body>
</html>`);
}

function buildBreadcrumbStructuredData(site, breadcrumbs) {
  if (!breadcrumbs || breadcrumbs.length === 0) {
    return null;
  }

  const trail = [{ label: "Home", path: "/" }, ...breadcrumbs];

  return {
    "@context": "https://schema.org",
    "@type": "BreadcrumbList",
    itemListElement: trail.map((crumb, index) => ({
      "@type": "ListItem",
      position: index + 1,
      name: crumb.label,
      item: crumb.path === "/" ? `${site.baseUrl}/` : `${site.baseUrl}${crumb.path}`,
    })),
  };
}

/**
 * Meta descriptions are plain text: markdown emphasis and code ticks leak as literal
 * punctuation into search snippets and link previews otherwise.
 */
function toMetaDescription(text) {
  const plain = String(text ?? "")
    .replace(/`([^`]+)`/g, "$1")
    .replace(/\*\*([^*]+)\*\*/g, "$1")
    .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
    .replace(/\s+/g, " ")
    .trim();

  if (plain.length <= 300) {
    return plain;
  }

  const clipped = plain.slice(0, 297);
  const lastSpace = clipped.lastIndexOf(" ");
  return `${(lastSpace > 200 ? clipped.slice(0, lastSpace) : clipped).replace(/[,;:]$/, "")}…`;
}

function buildStyles() {
  return normalizeNewlines(`
    :root {
      color-scheme: light dark;
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
      --page-background:
        radial-gradient(circle at top left, rgba(177, 77, 36, 0.12), transparent 26rem),
        radial-gradient(circle at top right, rgba(23, 33, 28, 0.1), transparent 24rem),
        linear-gradient(180deg, #f7f4ec 0%, #f0ece3 100%);
    }

    @media (prefers-color-scheme: dark) {
      :root {
        --bg: #12161f;
        --panel: rgba(31, 39, 54, 0.82);
        --panel-strong: #0d1119;
        --text: #eef2f9;
        --muted: #9dabc4;
        --line: rgba(224, 232, 245, 0.16);
        --accent: #f0955f;
        --accent-strong: #ffb98a;
        --accent-soft: rgba(240, 149, 95, 0.16);
        --shadow: 0 20px 50px rgba(0, 0, 0, 0.45);
        --page-background:
          radial-gradient(circle at top left, rgba(124, 92, 255, 0.16), transparent 26rem),
          radial-gradient(circle at top right, rgba(49, 198, 168, 0.12), transparent 24rem),
          linear-gradient(180deg, #12161f 0%, #0d1119 100%);
      }
    }

    * {
      box-sizing: border-box;
    }

    html {
      min-height: 100%;
      background: var(--page-background);
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

    .visually-hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      margin: -1px;
      padding: 0;
      overflow: hidden;
      clip: rect(0 0 0 0);
      white-space: nowrap;
      border: 0;
    }

    .skip-link {
      position: absolute;
      left: -9999px;
      top: 0;
      background: var(--panel-strong);
      color: #fff;
      padding: 0.6rem 1rem;
      border-radius: 0 0 12px 0;
      z-index: 20;
    }

    .skip-link:focus {
      left: 0;
    }

    .site-search {
      position: relative;
      flex: 0 1 22rem;
      min-width: 12rem;
    }

    .site-search input {
      width: 100%;
      padding: 0.6rem 0.9rem;
      border-radius: 999px;
      border: 1px solid var(--line);
      background: var(--panel);
      color: var(--text);
      font: inherit;
      font-size: 0.95rem;
    }

    .site-search input:focus-visible {
      outline: 2px solid var(--accent);
      outline-offset: 1px;
    }

    .site-search-results {
      position: absolute;
      z-index: 30;
      top: calc(100% + 0.45rem);
      left: 0;
      right: 0;
      background: var(--bg);
      border: 1px solid var(--line);
      border-radius: 16px;
      box-shadow: var(--shadow);
      overflow: hidden;
      max-height: 60vh;
      overflow-y: auto;
    }

    .site-search-results a {
      display: flex;
      gap: 0.6rem;
      align-items: baseline;
      padding: 0.6rem 0.9rem;
      text-decoration: none;
      border-bottom: 1px solid var(--line);
      font-size: 0.95rem;
    }

    .site-search-results a:last-child {
      border-bottom: 0;
    }

    .site-search-results a:hover,
    .site-search-results a:focus,
    .site-search-results a.is-active {
      background: var(--accent-soft);
    }

    .site-search-kind {
      flex: 0 0 auto;
      font-size: 0.72rem;
      letter-spacing: 0.08em;
      text-transform: uppercase;
      color: var(--muted);
    }

    .site-search-empty {
      margin: 0;
      padding: 0.75rem 0.9rem;
      color: var(--muted);
      font-size: 0.92rem;
    }

    .filter-notice {
      margin: 0.75rem 0 0;
      color: var(--muted);
      font-size: 0.95rem;
    }

    .breadcrumbs ol {
      display: flex;
      flex-wrap: wrap;
      gap: 0.45rem;
      list-style: none;
      margin: 0 0 1rem;
      padding: 0;
      font-size: 0.85rem;
      color: var(--muted);
    }

    .breadcrumbs li + li::before {
      content: "/";
      margin-right: 0.45rem;
      opacity: 0.6;
    }

    .severity-badge {
      display: inline-block;
      padding: 0.1rem 0.6rem;
      border-radius: 999px;
      border: 1px solid var(--line);
      font-size: 0.75rem;
      letter-spacing: 0.04em;
      text-transform: uppercase;
      margin-right: 0.4rem;
    }

    .severity-error {
      background: rgba(203, 62, 43, 0.14);
      border-color: rgba(203, 62, 43, 0.45);
    }

    .severity-warning {
      background: rgba(240, 161, 60, 0.16);
      border-color: rgba(240, 161, 60, 0.45);
    }

    .severity-info {
      background: rgba(49, 158, 198, 0.14);
      border-color: rgba(49, 158, 198, 0.4);
    }

    pre.has-copy {
      position: relative;
    }

    .copy-button {
      position: absolute;
      top: 0.5rem;
      right: 0.5rem;
      padding: 0.25rem 0.6rem;
      border-radius: 999px;
      border: 1px solid var(--line);
      background: var(--bg);
      color: var(--muted);
      font: inherit;
      font-size: 0.75rem;
      cursor: pointer;
      opacity: 0;
      transition: opacity 120ms ease-in;
    }

    pre.has-copy:hover .copy-button,
    .copy-button:focus-visible {
      opacity: 1;
    }

    .copy-button.is-copied {
      color: var(--accent-strong);
      border-color: var(--accent);
      opacity: 1;
    }

    .rule-pager {
      display: flex;
      justify-content: space-between;
      gap: 1rem;
      margin: 2rem 0 1rem;
    }

    .rule-pager-link {
      display: flex;
      flex-direction: column;
      gap: 0.2rem;
      max-width: 22rem;
      padding: 0.85rem 1.1rem;
      border: 1px solid var(--line);
      border-radius: 16px;
      background: var(--panel);
      text-decoration: none;
    }

    .rule-pager-link.is-next {
      margin-left: auto;
      text-align: right;
    }

    .rule-pager-link span {
      font-size: 0.75rem;
      letter-spacing: 0.08em;
      text-transform: uppercase;
      color: var(--muted);
    }

    @media (max-width: 720px) {
      .rule-pager {
        flex-direction: column;
      }

      .rule-pager-link.is-next {
        margin-left: 0;
        text-align: left;
      }

      .site-search {
        flex: 1 1 100%;
      }
    }

    /* The remaining light-mode literals: everything else already reads from the palette. */
    @media (prefers-color-scheme: dark) {
      a {
        text-decoration-color: rgba(240, 149, 95, 0.55);
      }

      .hero {
        background: linear-gradient(140deg, rgba(41, 51, 71, 0.88), rgba(27, 34, 49, 0.82));
      }

      code {
        background: rgba(224, 232, 245, 0.1);
      }

      .card-contrast {
        background: linear-gradient(160deg, rgba(9, 12, 18, 0.96), rgba(20, 26, 38, 0.92));
      }

      .severity-error {
        background: rgba(255, 122, 102, 0.18);
        border-color: rgba(255, 122, 102, 0.5);
      }

      .severity-warning {
        background: rgba(255, 190, 110, 0.18);
        border-color: rgba(255, 190, 110, 0.5);
      }

      .severity-info {
        background: rgba(118, 200, 235, 0.18);
        border-color: rgba(118, 200, 235, 0.45);
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
  return `${repositoryUrl}/blob/main/${relativePath.replace(/\\/g, "/")}`;
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

/** True when the source really does show an example under this label. */
function sectionHasFenceAfter(body, label) {
  return new RegExp(`${escapeRegex(label)}[^\\n]*\\n\\s*\\n\`\`\``).test(body ?? "");
}

function extractCodeFenceAfterLabel(body, label) {
  // Most sections introduce the example with a sentence on the label's own line
  // ("**Better pattern:** store the token and dispose it ..."). Requiring the label to
  // stand alone silently dropped the example for every rule written that way.
  const match = body.match(
    new RegExp(`${escapeRegex(label)}[^\\n]*\\n\\s*\\n\`\`\`([\\w-]*)\\n([\\s\\S]*?)\\n\`\`\``),
  );
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
