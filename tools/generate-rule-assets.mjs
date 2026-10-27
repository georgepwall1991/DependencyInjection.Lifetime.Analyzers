#!/usr/bin/env node

import { promises as fs, existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "..");
const readmePath = path.join(repoRoot, "README.md");
const descriptorsPath = path.join(
  repoRoot,
  "src/DependencyInjection.Lifetime.Analyzers/DiagnosticDescriptors.cs",
);
const idsPath = path.join(repoRoot, "src/DependencyInjection.Lifetime.Analyzers/DiagnosticIds.cs");
const rulesSourceRoot = path.join(
  repoRoot,
  "src/DependencyInjection.Lifetime.Analyzers/Rules",
);
const codeFixRoot = path.join(repoRoot, "src/DependencyInjection.Lifetime.Analyzers/CodeFixes");
const docsRoot = path.join(repoRoot, "docs/rules");
const profileRoot = path.join(repoRoot, "profiles");

const check = process.argv.includes("--check");

const readme = readFileSync(readmePath, "utf8");
const descriptors = parseDescriptors(
  readFileSync(descriptorsPath, "utf8"),
  readFileSync(idsPath, "utf8"),
);
const codeFixes = new Set(
  (await fs.readdir(codeFixRoot))
    .map((file) => file.match(/^(DI\d{3}[a-z]?)_/)?.[1])
    .filter(Boolean),
);
const ruleSources = new Map(
  (await fs.readdir(rulesSourceRoot))
    .map((file) => [file.match(/^(DI\d{3}[a-z]?)_/)?.[1], file])
    .filter(([id]) => id),
);

const sections = parseReadmeSections(readme);
const specialRules = {
  DI012b: {
    title: "Duplicate service registration",
    anchor: "di012-conditional-registration-misuse",
    why:
      "When the same service type is registered more than once, the container resolves the last registration. Earlier registrations are silently unreachable, so a startup that looks correct can run the wrong implementation, lose decorators, or create disposable instances that nobody resolves.",
    bad: `\`\`\`csharp
services.AddSingleton<IClock, SystemClock>();
services.AddSingleton<IClock, FakeClock>(); // SystemClock is unreachable
\`\`\``,
    better: `\`\`\`csharp
services.AddSingleton<IClock, SystemClock>();
// Register one implementation, or use TryAdd when a library default is allowed.
\`\`\``,
    detection:
      "The duplicate-registration tier of DI012. It reports only when both registrations target the same service type and key through symbol-resolved Microsoft.Extensions.DependencyInjection registration methods in the same composition flow. A conditional branch, a different key, or a call through a custom helper that merely reuses DI names is not evidence. This includes the same concrete service registered with different lifetimes: the later lifetime wins, so the earlier registration is unreachable and the intent is ambiguous.",
    codeFix: "No code fix ships for this sub-rule. Remove the redundant registration or change it to TryAdd.",
  },
};
const rules = descriptors.map((descriptor) => {
  const section = sections.get(descriptor.id);
  const special = specialRules[descriptor.id];
  return {
    ...descriptor,
    title: special?.title ?? section?.title ?? descriptor.title,
    section,
    special,
    codeFix: codeFixes.has(descriptor.id),
    sourceFile: ruleSources.get(descriptor.id),
  };
});

const generated = new Map();
for (const rule of rules) {
  generated.set(path.join(docsRoot, `${rule.id}.md`), renderRulePage(rule));
}
generated.set(path.join(docsRoot, "README.md"), renderIndex(rules));
for (const [name, severities] of Object.entries(profileDefinitions(rules))) {
  generated.set(path.join(profileRoot, name), renderProfile(name, rules, severities));
}

let stale = 0;
for (const [file, content] of generated) {
  const current = existsSync(file) ? await fs.readFile(file, "utf8") : null;
  if (current === content) continue;

  if (check) {
    console.error(`stale: ${path.relative(repoRoot, file)}`);
    stale++;
  } else {
    await fs.mkdir(path.dirname(file), { recursive: true });
    await fs.writeFile(file, content, "utf8");
    console.log(`wrote ${path.relative(repoRoot, file)}`);
  }
}

if (check) {
  if (stale > 0) {
    console.error(
      `\n${stale} generated rule asset(s) are stale. Run: node tools/generate-rule-assets.mjs`,
    );
    process.exit(1);
  }
  console.log(`Rule assets are current (${rules.length} rules).`);
} else {
  console.log(`Generated rule assets for ${rules.length} rules.`);
}

function parseDescriptors(text, idsText) {
  const constants = new Map();
  for (const match of idsText.matchAll(/public const string (\w+) = "(DI\d{3}[a-z]?)"/g)) {
    constants.set(match[1], match[2]);
  }

  const byId = new Map();
  const pattern =
    /public static readonly DiagnosticDescriptor (\w+) = new\(\s*id: (?:DiagnosticIds\.(\w+)|"(DI\d{3}[a-z]?)"),\s*title: "([^"]+)",[\s\S]*?defaultSeverity: DiagnosticSeverity\.(\w+)/g;
  for (const match of text.matchAll(pattern)) {
    const id = match[2] ? constants.get(match[2]) : match[3];
    if (!id || byId.has(id)) continue;
    byId.set(id, {
      id,
      title: match[4],
      severity: match[5],
    });
  }

  return [...byId.values()].sort((a, b) => a.id.localeCompare(b.id, "en"));
}

function parseReadmeSections(text) {
  const parts = [...text.matchAll(/^## (DI\d{3}[a-z]?): (.+)$/gm)];
  const sections = new Map();
  for (let index = 0; index < parts.length; index++) {
    const start = parts[index].index + parts[index][0].length;
    const end = index + 1 < parts.length ? parts[index + 1].index : text.length;
    const body = text.slice(start, end).replace(/\n---\s*$/, "").trim();
    sections.set(parts[index][1], {
      title: parts[index][2].trim(),
      anchor: slugify(`${parts[index][1]} ${parts[index][2]}`),
      body,
      segments: parseSegments(body),
    });
  }
  return sections;
}

function parseSegments(body) {
  const matches = [...body.matchAll(/\*\*([^*\n]+):\*\*/g)];
  const segments = [];
  for (let index = 0; index < matches.length; index++) {
    const start = matches[index].index + matches[index][0].length;
    const end = index + 1 < matches.length ? matches[index + 1].index : body.length;
    segments.push({
      label: matches[index][1].trim(),
      text: body.slice(start, end).trim(),
    });
  }
  return segments;
}

function segment(section, label) {
  return section?.segments.find((entry) => entry.label === label)?.text ?? "";
}

function extraSegments(section, known) {
  return (section?.segments ?? [])
    .filter((entry) => !known.includes(entry.label))
    .map((entry) => `### ${entry.label}\n\n${entry.text}`)
    .join("\n\n");
}

function renderRulePage(rule) {
  const section = rule.section;
  const special = rule.special;
  const known = [
    "What it catches",
    "Why it matters",
    "Explain Like I'm Ten",
    "Problem",
    "Better pattern",
    "Code Fix",
    "Guardrails",
    "Known exceptions in this rule",
    "Explicit registration precedence",
    "DbContext-backed processors",
    "Options",
  ];
  const detection = special?.detection ?? [
    segment(section, "What it catches"),
    segment(section, "Guardrails"),
    segment(section, "Known exceptions in this rule"),
    segment(section, "Explicit registration precedence"),
  ]
    .filter(Boolean)
    .join("\n\n");
  const why = special?.why ? [special.why] : [
    cleanSegment(segment(section, "Why it matters")),
    segment(section, "Explain Like I'm Ten")
      ? `> **Explain like I'm ten:** ${segment(section, "Explain Like I'm Ten")}`
      : "",
  ]
    .filter(Boolean)
    .join("\n\n");
  const bad =
    special?.bad ?? (cleanSegment(segment(section, "Problem")) || missingExample(rule, "bad"));
  const betterParts = [];
  if (segment(section, "Better pattern"))
    betterParts.push(segment(section, "Better pattern"));
  if (segment(section, "DbContext-backed processors"))
    betterParts.push(
      `### DbContext-backed processors\n\n${segment(section, "DbContext-backed processors")}`,
    );
  if (segment(section, "Options"))
    betterParts.push(`### Options\n\n${segment(section, "Options")}`);
  const better = special?.better ?? (betterParts.join("\n\n") || missingExample(rule, "better"));
  const codeFix =
    special?.codeFix ??
    (segment(section, "Code Fix") || "No code fix ships for this rule.");
  const extras = extraSegments(section, known);
  const source = rule.sourceFile
    ? `../src/DependencyInjection.Lifetime.Analyzers/Rules/${rule.sourceFile}`
    : "../src/DependencyInjection.Lifetime.Analyzers/Rules";

  return `# ${rule.id}: ${rule.title}

> **Severity:** ${titleCase(rule.severity)} · **Category:** DependencyInjection · **Enabled by default:** Yes · **Code fix:** ${rule.codeFix ? "Yes" : "No"}
>
> [All rules](README.md) · [Full guidance](../../README.md#${special?.anchor ?? section?.anchor ?? slugify(`${rule.id} ${rule.title}`)}) · [NuGet package](https://www.nuget.org/packages/DependencyInjection.Lifetime.Analyzers)

## Why it matters

${why || "This rule prevents a dependency injection failure that only appears at runtime."}

## Bad

${bad}

## Better

${better}

${extras ? extras + "\n\n" : ""}## Detection

${detection || "The analyzer reports only when the Microsoft.Extensions.DependencyInjection symbols and the lifetime flow are provable from the compilation. Custom APIs that reuse DI names are not evidence."}

## Code fix

${codeFix}

## Suppression

Suppress one site with a pragma:

\`\`\`csharp
#pragma warning disable ${rule.id}
// ...
#pragma warning restore ${rule.id}
\`\`\`

Or lower the rule for a project or folder in \`.editorconfig\`:

\`\`\`ini
[*.cs]
dotnet_diagnostic.${rule.id}.severity = none
\`\`\`

## References

- [Service lifetimes in Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection#service-lifetimes)
- [Dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection-guidelines)
- [Rule source](${source})
- [All DependencyInjection.Lifetime.Analyzers rules](../../docs/RULES.md)
`;
}

function cleanSegment(text) {
  const lines = text.split("\n");
  while (lines.length > 0 && (/^\s*>\s*$/.test(lines[0]) || /^\s*---\s*$/.test(lines[0])))
    lines.shift();
  while (
    lines.length > 0 &&
    (/^\s*>\s*$/.test(lines[lines.length - 1]) ||
      /^\s*---\s*$/.test(lines[lines.length - 1]))
  )
    lines.pop();
  return lines.join("\n").trim();
}

function missingExample(rule, kind) {
  if (rule.id === "DI022") {
    return kind === "bad"
      ? `A service registered as scoped is captured once by a handler that the sink may invoke
concurrently, for example a message processor whose \`MaxConcurrentCalls\` comes from
configuration. The capture is the lifetime violation even when the configured value happens to
be 1 today.

See the [sample app's DI022 examples](https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/tree/main/samples/SampleApp/Diagnostics/DI022)
for the shape.`
      : `Resolve the service per invocation, or register it as transient when the handler owns one
instance per message. If the service must be shared, prove that the sink is sequential and keep the
registration's lifetime equal to the sink's.`;
  }

  return kind === "bad"
    ? "The bad shape is described in the detection notes above. See the linked sample app for a complete example."
    : "The safe shape is described in the detection notes above. See the linked sample app for a complete example.";
}

function renderIndex(rules) {
  const rows = rules
    .map(
      (rule) =>
        `| [${rule.id}](${rule.id}.md) | ${escapeCell(rule.title)} | ${titleCase(rule.severity)} | ${rule.codeFix ? "Yes" : "No"} |`,
    )
    .join("\n");

  return `# Rule reference

One page per shipped rule. Every page explains the production failure, the bad shape, the safe
shape, how the analyzer proves the finding, how to suppress it, and where to read more.

The rule pages are generated from the repository README and the analyzer descriptors by
\`node tools/generate-rule-assets.mjs\`. Change the README or the descriptor, then regenerate;
\`--check\` fails CI when a page is stale.

| ID | Title | Default severity | Code fix |
| --- | --- | --- | --- |
${rows}

## Profiles

The same generator writes the starter \`.editorconfig\` profiles under \`profiles/\`:

| Profile | Use it when |
| --- | --- |
| \`default.editorconfig\` | You want each rule at its shipped default severity. |
| \`brownfield-adoption.editorconfig\` | You are adding the package to a large existing codebase and want composition failures first. |
| \`strict-ci.editorconfig\` | Every high-confidence rule should fail CI; informational rules stay warnings. |
| \`library-author.editorconfig\` | You ship a library and want activation and composition failures to be errors. |

## Priority failure modes

The shipped IDs are the stable contract. The short names below are the six highest-value failure
modes used by the scanner corpus and the showcase; they map onto one or more shipped rules.

| Priority | Production failure | Shipped rule |
| --- | --- | --- |
| DIA001 | A singleton captures a scoped or transient service | \`DI003\`; \`DI009\` for open generics |
| DIA002 | \`BuildServiceProvider()\` outside the composition root | \`DI016\` |
| DIA003 | A scoped service is resolved from the root provider | \`DI019\` |
| DIA004 | The same service is registered with conflicting lifetimes | \`DI012b\` (duplicate registration; the lifetime consequence is \`DI003\`/\`DI031\`) |
| DIA005 | An open generic registration has an unsafe lifetime for its dependencies | \`DI009\` |
| DIA006 | \`IServiceProvider\` is stored in a singleton or static field | \`DI006\` for static members; \`DI011\` for injected providers |
`;
}

function profileDefinitions(rules) {
  const infoRules = new Set(
    rules.filter((rule) => rule.severity === "Info").map((rule) => rule.id),
  );
  const compositionRules = new Set(["DI013", "DI015", "DI017", "DI018", "DI036"]);
  const leakRules = new Set(["DI001", "DI014"]);

  const defaultSeverities = new Map(
    rules.map((rule) => [rule.id, rule.severity.toLowerCase()]),
  );

  const brownfield = new Map(
    rules.map((rule) => [
      rule.id,
      compositionRules.has(rule.id) || leakRules.has(rule.id) ? "warning" : "suggestion",
    ]),
  );

  const strict = new Map(
    rules.map((rule) => [rule.id, infoRules.has(rule.id) ? "warning" : "error"]),
  );

  const library = new Map(
    rules.map((rule) => [
      rule.id,
      compositionRules.has(rule.id) || rule.id === "DI016"
        ? "error"
        : infoRules.has(rule.id)
          ? "suggestion"
          : "warning",
    ]),
  );

  return {
    "default.editorconfig": defaultSeverities,
    "brownfield-adoption.editorconfig": brownfield,
    "strict-ci.editorconfig": strict,
    "library-author.editorconfig": library,
  };
}

function renderProfile(name, rules, severities) {
  const descriptions = {
    "default.editorconfig":
      "Shipped default severities. Copy this into your repository and promote the rules your team has triaged.",
    "brownfield-adoption.editorconfig":
      "Start here when adding the analyzer to an existing codebase. Composition and activation failures are warnings; style and informational rules are suggestions until the baseline is clean.",
    "strict-ci.editorconfig":
      "Fail CI on every high-confidence rule. Informational rules stay warnings so a deliberate service-locator or cache decision does not block a build.",
    "library-author.editorconfig":
      "For libraries that register services. Activation and composition failures are errors; informational rules stay suggestions.",
  };
  const lines = [
    `# ${name}`,
    `# ${descriptions[name]}`,
    "# Generated by node tools/generate-rule-assets.mjs. Do not hand-edit.",
    "",
    "[*.cs]",
  ];
  for (const rule of rules) {
    lines.push(`dotnet_diagnostic.${rule.id}.severity = ${severities.get(rule.id)}`);
  }
  return lines.join("\n") + "\n";
}

function quote(text) {
  if (!text) return "";
  return text
    .split("\n")
    .map((line) => `> ${line}`)
    .join("\n");
}

function titleCase(value) {
  return value.charAt(0).toUpperCase() + value.slice(1).toLowerCase();
}

function escapeCell(value) {
  return value.replace(/\|/g, "\\|");
}

function slugify(value) {
  return value
    .toLowerCase()
    .replace(/[`]/g, "")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}
