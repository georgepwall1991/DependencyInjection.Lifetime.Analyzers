import assert from "node:assert/strict";
import test from "node:test";

import { verifyReleaseNotesXml } from "./verify-package-release-notes.mjs";

test("accepts release notes that exactly match the generated source", () => {
  const expected = "Fix <root> & preserve \"quoted\" and 'apostrophe' text.";
  const nuspec = `
    <package>
      <metadata>
        <releaseNotes>Fix &lt;root&gt; &amp; preserve &quot;quoted&quot; and &apos;apostrophe&apos; text.</releaseNotes>
      </metadata>
    </package>
  `;

  assert.doesNotThrow(() => verifyReleaseNotesXml(nuspec, expected));
});

test("accepts NuGet serialization with literal apostrophes", () => {
  const expected = "The nuspec's <releaseNotes> text is complete.";
  const nuspec = `
    <package>
      <metadata>
        <releaseNotes>The nuspec's &lt;releaseNotes&gt; text is complete.</releaseNotes>
      </metadata>
    </package>
  `;

  assert.doesNotThrow(() => verifyReleaseNotesXml(nuspec, expected));
});

test("rejects a package with no release notes element", () => {
  const nuspec = "<package><metadata /></package>";

  assert.throws(
    () => verifyReleaseNotesXml(nuspec, "Expected notes."),
    /releaseNotes element is missing/,
  );
});

test("rejects release notes that differ from the generated source", () => {
  const nuspec = `
    <package>
      <metadata>
        <releaseNotes>Truncated notes</releaseNotes>
      </metadata>
    </package>
  `;

  assert.throws(
    () => verifyReleaseNotesXml(nuspec, "Truncated notes with the required ending."),
    /do not match/,
  );
});
