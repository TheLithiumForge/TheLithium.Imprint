/** Append a Windows search directory without introducing competing PATH/Path entries. */
export function appendWindowsPath(env: NodeJS.ProcessEnv, directory: string): void {
  // Match Node's child-process selection if the caller already supplied duplicate spellings.
  const keys = Object.keys(env).filter((key) => key.toUpperCase() === "PATH").sort();
  const current = env[keys[0] ?? "PATH"] ?? "";
  for (const key of keys) delete env[key];

  const present = current.split(";").some((entry) => entry.toLowerCase() === directory.toLowerCase());
  env.PATH = present ? current : current ? `${current};${directory}` : directory;
}
