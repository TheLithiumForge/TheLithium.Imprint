/**
 * Pushes the packages to nuget.org in dependency order.
 *
 * The API key comes from the NuGet/login action's OIDC exchange and lives for one hour,
 * so this runs immediately after that step. Pushing is sequential and stops on the first
 * failure: publishing the dependent package after its dependency failed would leave
 * nuget.org in a state no consumer can restore.
 */
import { spawnSync } from "node:child_process";
import { orderedPackages, type PackageFile } from "./packages.ts";

const SOURCE = "https://api.nuget.org/v3/index.json";

function push(item: PackageFile, apiKey: string): void {
  console.log(`Pushing ${item.fileName}`);
  const result = spawnSync(
    "dotnet",
    ["nuget", "push", item.path, "--api-key", apiKey, "--source", SOURCE, "--skip-duplicate"],
    { stdio: "inherit", shell: false },
  );

  if (result.error) {
    throw new Error(`Could not run dotnet nuget push: ${result.error.message}`);
  }
  if (result.status !== 0) {
    throw new Error(`Pushing ${item.fileName} failed with exit code ${result.status}.`);
  }
}

function main(): void {
  const apiKey = process.env.NUGET_API_KEY;
  if (!apiKey) {
    throw new Error(
      "No NuGet API key available. The NuGet/login step should have produced one via " +
        "trusted publishing; check that the job grants id-token: write and that a policy " +
        "exists on nuget.org for this repository and workflow file.",
    );
  }

  const packages = orderedPackages();
  for (const item of packages) {
    push(item, apiKey);
  }

  console.log(`Published ${packages.length} package(s).`);
}

try {
  main();
} catch (error) {
  console.error(`::error::${(error as Error).message}`);
  process.exit(1);
}
