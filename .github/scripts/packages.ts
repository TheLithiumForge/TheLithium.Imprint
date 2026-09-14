/**
 * Shared knowledge about the packages this repository ships.
 *
 * TheLithium.Imprint declares a hard dependency on TheLithium.Imprint.Core and carries no
 * runtime assembly of its own, so Core must reach nuget.org first. Indexing is asynchronous:
 * if the dependent package is indexed while its dependency is not, installs fail during that
 * window. Ordering here closes it.
 */
import { readdirSync } from "node:fs";
import { join } from "node:path";

export type PackageFile = {
  id: string;
  fileName: string;
  path: string;
};

/** Dependencies first. Each entry is matched against the package file name. */
export const PUBLISH_ORDER: readonly string[] = ["TheLithium.Imprint.Core", "TheLithium.Imprint"];

export const PACKAGE_DIRECTORY = "artifacts/packages";

/** Package id from a .nupkg file name: TheLithium.Imprint.Core.1.0.0.nupkg -> TheLithium.Imprint.Core. */
export function packageId(fileName: string): string {
  const withoutExtension = fileName.replace(/\.nupkg$/i, "");
  // The id ends where the version begins: the first dot-segment starting with a digit.
  const match = withoutExtension.match(/^(.*?)\.(\d.*)$/);
  return match ? match[1] : withoutExtension;
}

export function findPackages(directory: string = PACKAGE_DIRECTORY): PackageFile[] {
  let entries: string[];
  try {
    entries = readdirSync(directory);
  } catch {
    throw new Error(`Package directory '${directory}' does not exist. Run dotnet pack first.`);
  }
  return entries
    .filter((name) => name.toLowerCase().endsWith(".nupkg"))
    .map((name) => ({ id: packageId(name), fileName: name, path: join(directory, name) }));
}

/**
 * Verifies the expected set was produced and returns it in dependency order.
 * Throws when a package is missing, duplicated, or unexpected.
 */
export function orderedPackages(directory: string = PACKAGE_DIRECTORY): PackageFile[] {
  const found = findPackages(directory);

  const unexpected = found.filter((item) => !PUBLISH_ORDER.includes(item.id));
  if (unexpected.length > 0) {
    throw new Error(`Unexpected package(s): ${unexpected.map((item) => item.fileName).join(", ")}`);
  }

  const ordered: PackageFile[] = [];
  for (const id of PUBLISH_ORDER) {
    const matches = found.filter((item) => item.id === id);
    if (matches.length === 0) {
      throw new Error(`Missing package '${id}'. Expected ${PUBLISH_ORDER.join(" and ")}.`);
    }
    if (matches.length > 1) {
      throw new Error(`Found ${matches.length} copies of '${id}': ${matches.map((item) => item.fileName).join(", ")}`);
    }
    ordered.push(matches[0]);
  }

  return ordered;
}
