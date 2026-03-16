#!/usr/bin/env node

import { spawnSync } from "node:child_process";
import { readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";

function getRequiredArg(args, name) {
  const index = args.indexOf(name);
  if (index < 0 || index === args.length - 1) {
    throw new Error(`Missing required argument: ${name}`);
  }

  return args[index + 1];
}

const args = process.argv.slice(2);
const project = getRequiredArg(args, "--project");
const configuration = getRequiredArg(args, "--configuration");
const output = getRequiredArg(args, "--output");
const releaseNotesFile = getRequiredArg(args, "--release-notes-file");
const releaseNotes = (await readFile(releaseNotesFile, "utf8")).trim();
const propsPath = path.join(tmpdir(), `package-release-notes-${process.pid}-${Date.now()}.props`);

function escapeXml(text) {
  return text
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&apos;");
}

await writeFile(
  propsPath,
  `<Project>\n  <PropertyGroup>\n    <PackageReleaseNotes>${escapeXml(releaseNotes)}</PackageReleaseNotes>\n  </PropertyGroup>\n</Project>\n`,
  "utf8",
);

try {
  const result = spawnSync(
    "dotnet",
    [
      "pack",
      project,
      "--configuration",
      configuration,
      "--output",
      output,
      `-p:CustomBeforeMicrosoftCommonProps=${propsPath}`,
    ],
    { stdio: "inherit" },
  );

  process.exit(result.status ?? 1);
} finally {
  await rm(propsPath, { force: true });
}
