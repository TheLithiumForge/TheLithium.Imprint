/**
 * Tests for the release scripts. Run with: node --test .github/scripts/scripts.test.ts
 *
 * These run on any machine with Node, so a pipeline change can be checked before it is
 * pushed rather than by watching a release fail.
 */
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

import { resolveVersion } from "./resolve-version.ts";
import { orderedPackages, packageId } from "./packages.ts";

const manualMain = { ref: "refs/heads/main", eventName: "workflow_dispatch" };

test("releases reject other branches, tags, pushes, and missing context", () => {
  for (const context of [
    { ref: "refs/heads/feature", eventName: "workflow_dispatch" },
    { ref: "refs/heads/master", eventName: "workflow_dispatch" },
    { ref: "refs/tags/v1.2.3", eventName: "workflow_dispatch" },
    { ref: "refs/tags/v1.2.3", eventName: "push" },
    { ref: "refs/heads/main", eventName: "push" },
    {},
  ]) {
    assert.throws(() => resolveVersion({ ...context, inputVersion: "1.2.3" }), /manually dispatched from main/);
  }
});

test("a manual input works with or without the leading v", () => {
  for (const inputVersion of ["v1.2.3", "1.2.3", "  v1.2.3  "]) {
    assert.deepEqual(resolveVersion({ ...manualMain, inputVersion }), {
      version: "1.2.3", tag: "v1.2.3", prerelease: "false",
    });
  }
});

test("a prerelease label determines prerelease status", () => {
  const result = resolveVersion({ ...manualMain, inputVersion: "1.0.0-preview.1" });
  assert.equal(result.prerelease, "true");
});

test("hyphens in build metadata do not make stable versions prereleases", () => {
  assert.equal(resolveVersion({ ...manualMain, inputVersion: "1.0.0+build-123" }).prerelease, "false");
  assert.equal(resolveVersion({ ...manualMain, inputVersion: "1.0.0-rc.1+build-123" }).prerelease, "true");
});

test("an invalid version is rejected", () => {
  for (const bad of ["1.2", "1.2.3.4", "one.two.three", "v", "1.2.3-", "01.2.3", "1.2.3-01", "1.2.3\nother=value"]) {
    assert.throws(() => resolveVersion({ ...manualMain, inputVersion: bad }), /not valid SemVer|No version/, `accepted '${bad}'`);
  }
});

test("a missing version is rejected", () => {
  assert.throws(() => resolveVersion(manualMain), /No version supplied/);
});

test("the workflow entry point writes outputs and rejects requests outside main", () => {
  const directory = mkdtempSync(join(tmpdir(), "imprint-version-"));
  const output = join(directory, "output");
  try {
    const env = {
      ...process.env,
      GITHUB_REF: manualMain.ref,
      GITHUB_EVENT_NAME: manualMain.eventName,
      INPUT_VERSION: "v1.2.3-rc.1",
      GITHUB_OUTPUT: output,
    };
    const script = fileURLToPath(new URL("./resolve-version.ts", import.meta.url));
    const accepted = spawnSync(process.execPath, [script], { env, encoding: "utf8" });
    assert.equal(accepted.status, 0, accepted.stderr);
    const expected = "version=1.2.3-rc.1\ntag=v1.2.3-rc.1\nprerelease=true\n";
    assert.equal(readFileSync(output, "utf8"), expected);
    const rejected = spawnSync(process.execPath, [script], {
      env: { ...env, GITHUB_REF: "refs/heads/feature" }, encoding: "utf8",
    });
    assert.equal(rejected.status, 1);
    assert.match(rejected.stderr, /manually dispatched from main/);
    assert.equal(readFileSync(output, "utf8"), expected);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
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
