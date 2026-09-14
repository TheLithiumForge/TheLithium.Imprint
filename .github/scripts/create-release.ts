/**
 * Creates the GitHub release and attaches both packages.
 *
 * Runs after a successful publish so a release never advertises packages that are not
 * on nuget.org.
 */
import { spawnSync } from "node:child_process";
import { orderedPackages } from "./packages.ts";

function main(): void {
  const tag = process.env.RELEASE_TAG;
  const version = process.env.RELEASE_VERSION;
  const prerelease = process.env.RELEASE_PRERELEASE === "true";
  const target = process.env.GITHUB_SHA;

  if (!tag || !version) {
    throw new Error("RELEASE_TAG and RELEASE_VERSION are required.");
  }

  const assets = orderedPackages().map((item) => item.path);
  const args = [
    "release",
    "create",
    tag,
    ...assets,
    "--title",
    `TheLithium.Imprint ${version}`,
    "--generate-notes",
  ];
  if (target) {
    args.push("--target", target);
  }
  if (prerelease) {
    args.push("--prerelease");
  }

  const result = spawnSync("gh", args, { stdio: "inherit", shell: false });
  if (result.error) {
    throw new Error(`Could not run gh: ${result.error.message}`);
  }
  if (result.status !== 0) {
    throw new Error(`gh release create failed with exit code ${result.status}.`);
  }
}

try {
  main();
} catch (error) {
  console.error(`::error::${(error as Error).message}`);
  process.exit(1);
}
