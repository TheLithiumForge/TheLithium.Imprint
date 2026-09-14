import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { appendWindowsPath } from "./environment.ts";

const installer = "C:\\Program Files (x86)\\Microsoft Visual Studio\\Installer";

test("Windows PATH spellings preserve SDK lookup and unrelated environment values", () => {
  for (const key of ["Path", "PATH", "path"]) {
    const env = { [key]: "C:\\Program Files\\dotnet;C:\\Windows\\System32", CI: "true" };
    appendWindowsPath(env, installer);
    assert.deepEqual(env, {
      PATH: `C:\\Program Files\\dotnet;C:\\Windows\\System32;${installer}`,
      CI: "true",
    });
  }
});

test("an existing installer entry is not added twice, regardless of case", () => {
  const current = `C:\\dotnet;${installer.toUpperCase()}`;
  const env = { Path: current } as NodeJS.ProcessEnv;
  appendWindowsPath(env, installer);
  appendWindowsPath(env, installer);
  assert.deepEqual(env, { PATH: current });
});

test("an installer path prefix is not mistaken for an existing entry", () => {
  const env = { PATH: `${installer}-old` };
  appendWindowsPath(env, installer);
  assert.equal(env.PATH, `${installer}-old;${installer}`);
});

test("missing or empty PATH does not inject undefined or an empty search directory", () => {
  for (const env of [{}, { Path: "" }, { PATH: undefined }] as NodeJS.ProcessEnv[]) {
    appendWindowsPath(env, installer);
    assert.deepEqual(env, { PATH: installer });
  }
});

test("duplicate PATH keys preserve the same value Node would pass to the child", () => {
  const env = { Path: "C:\\other", PATH: "C:\\dotnet", path: "C:\\third" };
  appendWindowsPath(env, installer);
  assert.deepEqual(env, { PATH: `C:\\dotnet;${installer}` });
});

test("dotnet remains executable outside the repository with a Windows Path environment", {
  skip: process.platform !== "win32",
}, () => {
  const env = { ...process.env };
  const keys = Object.keys(env).filter((key) => key.toUpperCase() === "PATH").sort();
  const current = env[keys[0]];
  assert.ok(current, "The Windows test requires the .NET SDK on PATH.");
  for (const key of keys) delete env[key];
  env.Path = current;
  appendWindowsPath(env, installer);

  const directory = mkdtempSync(join(tmpdir(), "Imprint PATH test "));
  try {
    const result = spawnSync("dotnet", ["--version"], {
      env, cwd: directory, encoding: "utf8", shell: false,
    });
    assert.ifError(result.error);
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout.trim(), /^\d+\.\d+\.\d+/);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
