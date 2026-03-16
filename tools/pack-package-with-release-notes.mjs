#!/usr/bin/env node

import { spawnSync } from "node:child_process";
import { readFile } from "node:fs/promises";

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
const releaseNotes = (await readFile(releaseNotesFile, "utf8")).trim().replace(/"/g, '\\"');

const result = spawnSync(
  "dotnet",
  [
    "pack",
    project,
    "--configuration",
    configuration,
    "--output",
    output,
    `-p:PackageReleaseNotes="${releaseNotes}"`,
  ],
  { stdio: "inherit" },
);

process.exit(result.status ?? 1);
