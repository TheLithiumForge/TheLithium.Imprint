/**
 * Resolves the release version from a pushed tag or a manual dispatch input.
 *
 * Inputs (environment):
 *   GITHUB_REF        refs/tags/v1.2.3 for a tag push
 *   GITHUB_REF_NAME   v1.2.3
 *   INPUT_VERSION     manual dispatch version, with or without a leading v
 *   INPUT_PRERELEASE  manual dispatch prerelease flag
 *
 * Outputs (GITHUB_OUTPUT): version, tag, prerelease
 */
import { appendFileSync } from "node:fs";

export type VersionInputs = {
  ref?: string;
  refName?: string;
  inputVersion?: string;
  inputPrerelease?: string;
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
  const { ref, refName, inputVersion, inputPrerelease } = inputs;

  // A tag push always wins: it is the thing being released.
  const fromTag = ref?.startsWith("refs/tags/v") ? refName : undefined;
  const raw = (fromTag ?? inputVersion ?? "").trim().replace(/^v/, "");

  if (!raw) {
    throw new Error("No version supplied. Push a v*.*.* tag or provide the version input.");
  }
  if (!SEMVER.test(raw)) {
    throw new Error(`Version '${raw}' is not valid SemVer.`);
  }

  // A prerelease label makes the release a prerelease regardless of the input flag;
  // a stable version honours the flag.
  const prerelease = raw.includes("-") || String(inputPrerelease ?? "false") === "true";

  return { version: raw, tag: `v${raw}`, prerelease: String(prerelease) };
}

function main(): void {
  const resolved = resolveVersion({
    ref: process.env.GITHUB_REF,
    refName: process.env.GITHUB_REF_NAME,
    inputVersion: process.env.INPUT_VERSION,
    inputPrerelease: process.env.INPUT_PRERELEASE,
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
