/**
 * Resolves a manually requested release version, restricted to main.
 *
 * Inputs (environment):
 *   GITHUB_REF        must be refs/heads/main
 *   GITHUB_EVENT_NAME must be workflow_dispatch
 *   INPUT_VERSION     manual dispatch version, with or without a leading v
 *
 * Outputs (GITHUB_OUTPUT): version, tag, prerelease
 */
import { appendFileSync } from "node:fs";

export type VersionInputs = {
  ref?: string;
  eventName?: string;
  inputVersion?: string;
};

export type ResolvedVersion = {
  version: string;
  tag: string;
  prerelease: string;
};

// Major.Minor.Patch with an optional prerelease label and optional build metadata.
const SEMVER =
  /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/;

export function resolveVersion(inputs: VersionInputs): ResolvedVersion {
  const { ref, eventName, inputVersion } = inputs;
  if (ref !== "refs/heads/main" || eventName !== "workflow_dispatch") {
    throw new Error("Releases must be manually dispatched from main.");
  }
  const raw = (inputVersion ?? "").trim().replace(/^v/, "");

  if (!raw) {
    throw new Error("No version supplied. Provide the version input.");
  }
  const match = SEMVER.exec(raw);
  if (!match) {
    throw new Error(`Version '${raw}' is not valid SemVer.`);
  }

  const prerelease = Boolean(match[4]);

  return { version: raw, tag: `v${raw}`, prerelease: String(prerelease) };
}

function main(): void {
  const resolved = resolveVersion({
    ref: process.env.GITHUB_REF,
    eventName: process.env.GITHUB_EVENT_NAME,
    inputVersion: process.env.INPUT_VERSION,
  });

  const lines = Object.entries(resolved).map(([key, value]) => `${key}=${value}`);
  console.log(lines.join("\n"));

  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(process.env.GITHUB_OUTPUT, `${lines.join("\n")}\n`);
  }
}

// Only act when executed directly, so the test file can import resolveVersion.
if (process.argv[1]?.endsWith("resolve-version.ts")) {
  try {
    main();
  } catch (error) {
    console.error((error as Error).message);
    process.exit(1);
  }
}
