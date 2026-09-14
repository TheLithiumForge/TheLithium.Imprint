/**
 * Fails the release unless exactly the expected packages were produced, and prints the
 * order they will be pushed in. Runs before anything is uploaded so a packing mistake
 * stops the release rather than publishing half of it.
 */
import { statSync } from "node:fs";
import { orderedPackages, PUBLISH_ORDER } from "./packages.ts";

function main(): void {
  const packages = orderedPackages();

  console.log(`Publish order (dependencies first): ${PUBLISH_ORDER.join(" -> ")}`);
  for (const [index, item] of packages.entries()) {
    const { size } = statSync(item.path);
    if (size === 0) {
      throw new Error(`Package '${item.fileName}' is empty.`);
    }
    console.log(`  ${index + 1}. ${item.fileName} (${(size / 1024).toFixed(0)} KiB)`);
  }
}

try {
  main();
} catch (error) {
  console.error(`::error::${(error as Error).message}`);
  process.exit(1);
}
