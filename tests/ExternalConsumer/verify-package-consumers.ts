#!/usr/bin/env node
/**
 * Verifies the packed NuGet packages from outside this repository.
 *
 * Copies the consumer templates to a fresh directory, points them at a local feed holding
 * only the freshly packed packages, and builds and runs them with their own package cache.
 * Nothing here may use a project reference, repository Directory.Build import or internals
 * access: that is the point of the check.
 *
 * Usage:
 *   node tests/ExternalConsumer/verify-package-consumers.ts
 *     [--package-directory DIR] [--output-root DIR] [--managed-only]
 *
 * Runs on Windows, Linux and macOS with nothing but the .NET SDK and Node.
 */
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  realpathSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, extname, isAbsolute, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const TEMPLATE_EXTENSIONS = new Set([".cs", ".csproj", ".props", ".targets"]);
const FRAMEWORK_PROJECTS = ["Xunit", "NUnit", "MSTest"];
const EXPECTED_TESTS = 5;
const EXPECTED_SNAPSHOTS = 5;
const NEGATIVE_SCENARIOS = [
  { name: "UnsupportedObject", code: "IMP001" },
  { name: "AsyncVoid", code: "IMP102" },
  { name: "AsyncRun", code: "CS0619" },
];

type Options = {
  packageDirectory: string;
  outputRoot: string;
  managedOnly: boolean;
};

type Context = {
  output: string;
  logs: string;
  env: NodeJS.ProcessEnv;
  runtime: string;
};

type CommandResult = {
  status: number | null;
  output: string;
  logPath: string;
};

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repository = resolve(scriptDirectory, "..", "..");

function fail(message: string): never {
  throw new Error(message);
}

function parseArguments(argv: string[]): Options {
  const options = {
    packageDirectory: join(repository, "artifacts", "packages"),
    outputRoot: "",
    managedOnly: false,
  };
  for (let i = 0; i < argv.length; i++) {
    const argument = argv[i];
    if (argument === "--managed-only") {
      options.managedOnly = true;
    } else if (argument === "--package-directory" || argument === "--output-root") {
      const value = argv[++i];
      if (!value) fail(`Missing directory for ${argument}`);
      if (argument === "--package-directory") options.packageDirectory = value;
      else options.outputRoot = value;
    } else if (argument === "-h" || argument === "--help") {
      console.log(
        "Usage: node verify-package-consumers.ts [--package-directory DIR] [--output-root DIR] [--managed-only]",
      );
      process.exit(0);
    } else {
      fail(`Unknown argument: ${argument}`);
    }
  }
  return options;
}

function runtimeIdentifier(): string {
  const platform = { linux: "linux", darwin: "osx", win32: "win" }[process.platform];
  if (!platform) fail(`Unsupported platform: ${process.platform}`);
  const architecture = { x64: "x64", arm64: "arm64" }[process.arch];
  if (!architecture) fail(`Supported architectures are x64 and arm64, not ${process.arch}`);
  return `${platform}-${architecture}`;
}

/** Every file below `directory`, recursively. */
function walk(directory: string): string[] {
  const found: string[] = [];
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const full = join(directory, entry.name);
    if (entry.isDirectory()) found.push(...walk(full));
    else if (entry.isFile()) found.push(full);
  }
  return found;
}

/** Refuses to reuse or delete an existing directory, and refuses to run inside the repository. */
function prepareOutputRoot(requested: string): string {
  const output = requested
    ? resolve(requested)
    : mkdtempSync(join(realpathSync(tmpdir()), "Imprint.ExternalConsumer-"));

  // Resolve symlinks on both sides before comparing. A path on another drive relatives to an
  // absolute path, which is outside by definition.
  const real = realpathSync(repository);
  const resolved = existsSync(output) ? realpathSync(output) : output;
  const relation = relative(real, resolved);
  const inside = relation === "" || (!relation.startsWith("..") && !isAbsolute(relation));
  if (inside) {
    fail("Consumer must run outside the repository");
  }
  if (requested) {
    if (existsSync(output)) fail("Choose a new external output directory");
    mkdirSync(output, { recursive: true });
  }
  return output;
}

function copyTemplates(output: string): void {
  for (const source of walk(scriptDirectory)) {
    if (!TEMPLATE_EXTENSIONS.has(extname(source))) continue;
    const destination = join(output, relative(scriptDirectory, source));
    mkdirSync(dirname(destination), { recursive: true });
    copyFileSync(source, destination);
  }
  copyFileSync(join(repository, "global.json"), join(output, "global.json"));
}

/** Copies every packed package into an isolated feed and records its hash for the run log. */
function createFeed(output: string, packageDirectory: string): string[] {
  const feed = join(output, "feed");
  mkdirSync(feed);

  const packages = readdirSync(packageDirectory).filter((name) => name.toLowerCase().endsWith(".nupkg"));
  if (packages.length === 0) fail(`No packages in '${packageDirectory}'. Run dotnet pack first.`);

  const hashes: { name: string; sha256: string }[] = [];
  for (const name of packages) {
    const target = join(feed, name);
    copyFileSync(join(packageDirectory, name), target);
    hashes.push({ name, sha256: createHash("sha256").update(readFileSync(target)).digest("hex").toUpperCase() });
  }
  writeFileSync(join(output, "package-hashes.json"), `${JSON.stringify(hashes, null, 2)}\n`, "utf8");

  writeFileSync(
    join(output, "NuGet.Config"),
    `<configuration>
  <packageSources>
    <clear />
    <add key="imprint-local" value="feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="imprint-local"><package pattern="TheLithium.Imprint*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
  <config><add key="globalPackagesFolder" value="packages" /></config>
</configuration>
`,
    "utf8",
  );
  return packages;
}

/**
 * Runs a command, writing all output to a log. On failure the tail is printed, so a CI log
 * shows the reason without holding the whole build output.
 */
function runLogged(
  context: Context,
  logName: string,
  command: string,
  args: string[],
  { allowFailure = false }: { allowFailure?: boolean } = {},
): CommandResult {
  const logPath = join(context.logs, logName);
  const result = spawnSync(command, args, {
    cwd: context.output,
    env: context.env,
    encoding: "utf8",
    shell: false,
  });
  if (result.error) fail(`Could not run ${command}: ${result.error.message}`);

  const output = `${result.stdout ?? ""}${result.stderr ?? ""}`;
  writeFileSync(logPath, output, "utf8");

  if (result.status !== 0 && !allowFailure) {
    console.error(output.split(/\r?\n/).slice(-45).join("\n"));
    fail(`Command failed; see ${logPath}`);
  }
  return { status: result.status, output, logPath };
}

function printTail(text: string, lines = 2): void {
  const trimmed = text.split(/\r?\n/).filter((line) => line.trim() !== "");
  console.log(trimmed.slice(-lines).join("\n"));
}

/** A TRX must report exactly the expected passing tests, so empty discovery cannot pass. */
function assertTestResults(resultsDirectory: string, snapshotDirectory: string): void {
  const results = readdirSync(resultsDirectory).filter((name) => name.toLowerCase().endsWith(".trx"));
  if (results.length !== 1) fail(`Expected one test result in ${resultsDirectory}, found ${results.length}`);

  const trx = readFileSync(join(resultsDirectory, results[0]), "utf8");
  const counters = trx.match(/<Counters\b[^>]*>/);
  if (!counters) fail(`No result summary in ${results[0]}`);

  const attribute = (name: string) => counters[0].match(new RegExp(`${name}="(\\d+)"`))?.[1];
  if (attribute("total") !== String(EXPECTED_TESTS) || attribute("passed") !== String(EXPECTED_TESTS)) {
    fail(`Expected ${EXPECTED_TESTS} passing tests in ${results[0]}, got total=${attribute("total")} passed=${attribute("passed")}`);
  }

  const snapshots = existsSync(snapshotDirectory)
    ? walk(snapshotDirectory).filter((file) => extname(file) === ".json").length
    : 0;
  if (snapshots !== EXPECTED_SNAPSHOTS) {
    fail(`Expected ${EXPECTED_SNAPSHOTS} generated snapshots in ${snapshotDirectory}, found ${snapshots}`);
  }
}

function verifyExecutables(context: Context, managedOnly: boolean): void {
  for (const project of ["Full", "CoreOnly"]) {
    const projectFile = join(project, `${project}.csproj`);
    runLogged(context, `${project}-restore.log`, "dotnet", ["restore", projectFile, "--force-evaluate"]);

    const managed = runLogged(context, `${project}-managed.log`, "dotnet", [
      "run", "--project", projectFile, "-c", "Release", "--no-restore",
      "--", "--managed", join(context.output, "runs", `${project}-managed`),
    ]);
    printTail(managed.output);

    if (managedOnly) continue;

    const publish = join(context.output, "publish", project);
    runLogged(context, `${project}-publish.log`, "dotnet", [
      "publish", projectFile, "-c", "Release", "-r", context.runtime,
      "-p:PublishAot=true", "-p:SelfContained=true",
      "-p:IlcTreatWarningsAsErrors=true", "-p:ILLinkTreatWarningsAsErrors=true",
      "-o", publish,
    ]);

    const executable = join(publish, process.platform === "win32" ? `${project}.exe` : project);
    const aot = runLogged(context, `${project}-aot.log`, executable, [
      "--aot", join(context.output, "runs", `${project}-aot`),
    ]);
    printTail(aot.output);
  }
}

function verifyFrameworks(context: Context): void {
  for (const project of FRAMEWORK_PROJECTS) {
    const projectFile = join(project, `${project}.csproj`);
    runLogged(context, `${project}-restore.log`, "dotnet", ["restore", projectFile, "--force-evaluate"]);

    // Create baselines on the first pass, then verify them in a separate process.
    for (const policy of ["missing", "verify"]) {
      const results = join(context.logs, `${project}-${policy}`);
      const run = runLogged(
        { ...context, env: { ...context.env, IMPRINT_UPDATE: policy } },
        `${project}-${policy}.log`,
        "dotnet",
        ["test", projectFile, "-c", "Release", "--no-restore", "--logger", "trx", "--results-directory", results],
      );
      assertTestResults(results, join(context.output, project, "__snapshots__"));
      printTail(run.output);
    }
  }
}

function verifyCompilerRejections(context: Context): void {
  runLogged(context, "Negative-restore.log", "dotnet", [
    "restore", join("Negative", "Negative.csproj"), "--force-evaluate", "-p:Scenario=UnsupportedObject",
  ]);

  for (const { name, code } of NEGATIVE_SCENARIOS) {
    const build = runLogged(
      context,
      `Negative-${name}.log`,
      "dotnet",
      ["build", join("Negative", "Negative.csproj"), "-c", "Release", "--no-restore", `-p:Scenario=${name}`],
      { allowFailure: true },
    );

    if (build.status === 0) fail(`Expected rejection ${code} for ${name}; see ${build.logPath}`);
    if (!build.output.includes(`error ${code}`)) {
      console.error(build.output.split(/\r?\n/).slice(-45).join("\n"));
      fail(`Expected rejection ${code} for ${name}`);
    }
    console.log(`PASS compiler rejection ${name}: ${code}`);
  }
}

function main(): void {
  const options = parseArguments(process.argv.slice(2));
  const output = prepareOutputRoot(options.outputRoot);

  copyTemplates(output);
  createFeed(output, resolve(options.packageDirectory));

  console.log(`External consumer directory: ${output}`);
  const logs = join(output, "logs");
  mkdirSync(logs);

  // A consumer must not inherit this repository's snapshot policy or CI detection.
  const env = { ...process.env };
  delete env.IMPRINT_UPDATE;
  delete env.IMPRINT_PROJECT_ROOT;
  delete env.CI;

  // Native AOT linking on Windows needs the Visual Studio toolchain on PATH.
  if (process.platform === "win32") {
    const installer = "C:\\Program Files (x86)\\Microsoft Visual Studio\\Installer";
    if (existsSync(installer) && !env.PATH?.includes(installer)) {
      env.PATH = `${env.PATH};${installer}`;
    }
  }

  const context = { output, logs, env, runtime: runtimeIdentifier() };
  verifyExecutables(context, options.managedOnly);
  verifyFrameworks(context);
  verifyCompilerRejections(context);
}

try {
  main();
} catch (error) {
  console.error((error as Error).message);
  process.exit(1);
}
