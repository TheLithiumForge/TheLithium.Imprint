/**
 * Tests for the release scripts. Run with: node --test .github/scripts/scripts.test.ts
 *
 * These run on any machine with Node, so a pipeline change can be checked before it is
 * pushed rather than by watching a release fail.
 */
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";

import { resolveVersion } from "./resolve-version.ts";
import { orderedPackages, packageId } from "./packages.ts";

test("a pushed tag determines the version", () => {
  const result = resolveVersion({ ref: "refs/tags/v1.2.3", refName: "v1.2.3" });
  assert.deepEqual(result, { version: "1.2.3", tag: "v1.2.3", prerelease: "false" });
});

test("a tag wins over a manual input", () => {
  const result = resolveVersion({ ref: "refs/tags/v2.0.0", refName: "v2.0.0", inputVersion: "9.9.9" });
  assert.equal(result.version, "2.0.0");
});

test("a manual input works with or without the leading v", () => {
  assert.equal(resolveVersion({ inputVersion: "v1.2.3" }).version, "1.2.3");
  assert.equal(resolveVersion({ inputVersion: "1.2.3" }).version, "1.2.3");
});

test("a prerelease label forces a prerelease regardless of the flag", () => {
  const result = resolveVersion({ inputVersion: "1.0.0-preview.1", inputPrerelease: "false" });
  assert.equal(result.prerelease, "true");
});

test("a stable version honours the prerelease flag", () => {
  assert.equal(resolveVersion({ inputVersion: "1.0.0", inputPrerelease: "true" }).prerelease, "true");
  assert.equal(resolveVersion({ inputVersion: "1.0.0", inputPrerelease: "false" }).prerelease, "false");
});

test("an invalid version is rejected", () => {
  for (const bad of ["1.2", "1.2.3.4", "one.two.three", "v", "1.2.3-", "01.2.3"]) {
    assert.throws(() => resolveVersion({ inputVersion: bad }), /not valid SemVer|No version/, `accepted '${bad}'`);
  }
});

test("a missing version is rejected", () => {
  assert.throws(() => resolveVersion({}), /No version supplied/);
});

test("package ids are read from file names", () => {
  assert.equal(packageId("TheLithium.Imprint.Core.1.0.0.nupkg"), "TheLithium.Imprint.Core");
  assert.equal(packageId("TheLithium.Imprint.1.0.0.nupkg"), "TheLithium.Imprint");
  assert.equal(packageId("TheLithium.Imprint.1.0.0-preview.2.nupkg"), "TheLithium.Imprint");
});

function withPackages(names: string[], body: (directory: string) => void): void {
  const directory = mkdtempSync(join(tmpdir(), "imprint-packages-"));
  try {
    for (const name of names) {
      writeFileSync(join(directory, name), "x");
    }
    body(directory);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

test("Core is pushed before the package that depends on it", () => {
  // Deliberately written in the order a plain glob would produce, which is the wrong one.
  withPackages(["TheLithium.Imprint.1.0.0.nupkg", "TheLithium.Imprint.Core.1.0.0.nupkg"], (directory) => {
    const ordered = orderedPackages(directory).map((p) => p.id);
    assert.deepEqual(ordered, ["TheLithium.Imprint.Core", "TheLithium.Imprint"]);
  });
});

test("a missing package fails the release", () => {
  withPackages(["TheLithium.Imprint.1.0.0.nupkg"], (directory) => {
    assert.throws(() => orderedPackages(directory), /Missing package 'TheLithium.Imprint.Core'/);
  });
});

test("an unexpected package fails the release", () => {
  withPackages(
    ["TheLithium.Imprint.1.0.0.nupkg", "TheLithium.Imprint.Core.1.0.0.nupkg", "Something.Else.1.0.0.nupkg"],
    (directory) => {
      assert.throws(() => orderedPackages(directory), /Unexpected package/);
    },
  );
});

test("two versions of one package fail the release", () => {
  withPackages(
    [
      "TheLithium.Imprint.Core.1.0.0.nupkg",
      "TheLithium.Imprint.1.0.0.nupkg",
      "TheLithium.Imprint.1.0.1.nupkg",
    ],
    (directory) => {
      assert.throws(() => orderedPackages(directory), /Found 2 copies/);
    },
  );
});

test("a missing package directory is reported clearly", () => {
  assert.throws(() => orderedPackages(join(tmpdir(), "imprint-does-not-exist")), /does not exist/);
});
