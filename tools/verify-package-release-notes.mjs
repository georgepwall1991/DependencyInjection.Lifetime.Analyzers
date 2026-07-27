#!/usr/bin/env node

import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { pathToFileURL } from "node:url";

function decodeXmlText(text) {
  return text
    .replace(/&#x([0-9a-f]+);/gi, (_, value) =>
      String.fromCodePoint(Number.parseInt(value, 16)),
    )
    .replace(/&#([0-9]+);/g, (_, value) =>
      String.fromCodePoint(Number.parseInt(value, 10)),
    )
    .replaceAll("&lt;", "<")
    .replaceAll("&gt;", ">")
    .replaceAll("&quot;", '"')
    .replaceAll("&apos;", "'")
    .replaceAll("&amp;", "&");
}

export function verifyReleaseNotesXml(nuspecXml, expectedNotes) {
  const releaseNotesMatch = nuspecXml.match(/<releaseNotes>([\s\S]*?)<\/releaseNotes>/);
  if (!releaseNotesMatch) {
    throw new Error("Packed nuspec releaseNotes element is missing.");
  }

  const actualNotes = decodeXmlText(releaseNotesMatch[1]);
  if (actualNotes !== expectedNotes.trim()) {
    throw new Error("Packed nuspec releaseNotes do not match the generated release notes.");
  }
}

function main() {
  const [packagePath, expectedNotesPath] = process.argv.slice(2);
  if (!packagePath || !expectedNotesPath) {
    throw new Error(
      "Usage: verify-package-release-notes.mjs <package.nupkg> <package-release-notes.txt>",
    );
  }

  const nuspecXml = execFileSync("unzip", ["-p", packagePath, "*.nuspec"], {
    encoding: "utf8",
  });
  const expectedNotes = readFileSync(expectedNotesPath, "utf8");
  verifyReleaseNotesXml(nuspecXml, expectedNotes);
  process.stdout.write("Verified packed release notes against the generated source.\n");
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    main();
  } catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  }
}
