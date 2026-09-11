# Building, testing, and releasing

Maintainer notes. For using the library, see the [README](../README.md).

## Layout

| Project                            | What it is                                                | Ships?                                       |
| ---------------------------------- | --------------------------------------------------------- | -------------------------------------------- |
| `src/TheLithium.Imprint.Core`      | The runtime. AOT-compatible, no reflection.               | Yes, as `TheLithium.Imprint.Core`            |
| `src/TheLithium.Imprint.Generator` | Incremental source generator: writers, metadata, IMP001.  | Yes, as an analyzer inside the main package  |
| `src/TheLithium.Imprint.Build`     | MSBuild task that wraps test method bodies. Emits IMP102. | Yes, as a build task inside the main package |
| `src/TheLithium.Imprint`           | The package users install. No code of its own.            | Yes, as `TheLithium.Imprint`                 |

The generator and build task run at compile time only. Neither is a runtime dependency of a consumer.

| Test project                               | Covers                                                                                                                         | Runtime                    |
| ------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------ | -------------------------- |
| `tests/TheLithium.Imprint.Specifications`  | The engine end to end: storage, comparison, limits, recovery, conflicts, artifacts. A plain `Main` with an explicit test list. | Managed **and** Native AOT |
| `tests/TheLithium.Imprint.Tests`           | Ordinary xUnit bodies — multiple captures, async, `ValueTask`, exceptions, awaited `finally`, parameterized data.              | Managed                    |
| `tests/TheLithium.Imprint.Generator.Tests` | Writer generation and diagnostics.                                                                                             | Managed                    |
| `tests/TheLithium.Imprint.NUnit.Tests`     | Real NUnit methods.                                                                                                            | Managed                    |
| `tests/TheLithium.Imprint.MSTest.Tests`    | Real MSTest methods.                                                                                                           | Managed                    |

The framework projects stay managed on purpose — their runners discover tests by reflection, which is how those runners are designed to work. The specification project is the AOT lane: it exercises the same runtime and the same generated package integration without needing a reflection-heavy adapter.

All build output goes under `./artifacts` (set by `Directory.Build.props`). Nothing generated is tracked. The reviewable fixtures are the baselines under each project's `__snapshots__`.

## Everyday loop

```bash
dotnet build TheLithium.Imprint.slnx -c Release
dotnet test  TheLithium.Imprint.slnx -c Release
dotnet run --project tests/TheLithium.Imprint.Specifications -c Release -- --expect-managed
```

`--expect-managed` and `--expect-aot` make the harness assert which runtime it is actually running under, so a lane cannot silently test the wrong thing.

## Testing as a package consumer

Project references hide packaging bugs — a missing analyzer, a build task in the wrong folder, a bad `buildTransitive` props file. This mode restores the test projects from the locally packed `.nupkg` instead:

```bash
dotnet pack TheLithium.Imprint.slnx -c Release
dotnet restore TheLithium.Imprint.slnx -p:UsePackageReferences=true -p:RestorePackagesPath=artifacts/nuget/package-consumer --force-evaluate
dotnet test    TheLithium.Imprint.slnx -c Release -p:UsePackageReferences=true -p:RestorePackagesPath=artifacts/nuget/package-consumer --no-restore
```

Packages land in `artifacts/packages`, which `NuGet.Config` maps as the `local` source for `TheLithium.Imprint*`.

Restore the default project-reference mode before going back to ordinary development:

```bash
dotnet restore TheLithium.Imprint.slnx --force-evaluate
```

> Repacking the same version number will not be picked up by a consumer that already cached it. Either bump the version or delete `~/.nuget/packages/thelithium.imprint*` first.

## Native AOT verification

```bash
dotnet publish tests/TheLithium.Imprint.Specifications -c Release -r win-x64 \
  -p:UsePackageReferences=true -p:RestorePackagesPath=artifacts/nuget/package-consumer \
  -p:PublishAot=true -p:SelfContained=true \
  -p:IlcTreatWarningsAsErrors=true -p:ILLinkTreatWarningsAsErrors=true \
  -o artifacts/native/win-x64

./artifacts/native/win-x64/TheLithium.Imprint.Specifications.exe --expect-aot
```

Warnings are errors on purpose: an IL2xxx or IL3xxx warning from our own assemblies means a reflection path crept in.

The harness checks `RuntimeFeature.IsDynamicCodeSupported` before running, so `--expect-aot` fails loudly if the binary turns out to be managed.

On Windows, the native link step shells out to `vswhere.exe`. If it is not on `PATH` the publish fails at link time with `MSB3073`; add `C:\Program Files (x86)\Microsoft Visual Studio\Installer`, or run from a Developer prompt.

## CI

[`build-test.yml`](../.github/workflows/build-test.yml) runs on Windows, Ubuntu, and macOS, in two explicit lanes so the runtime mode is visible in every run:

- **Managed** — builds the solution, verifies formatting, runs the xUnit, NUnit, MSTest, and generator projects, and runs the specifications with `--expect-managed`.
- **Packaged AOT** — packs both NuGet projects, restores the test projects from that local feed, runs them in managed mode, then publishes the specification project as Native AOT and runs it with `--expect-aot`.

## Releases

[`release.yml`](../.github/workflows/release.yml) runs only for a `v*.*.*` tag or a deliberate manual dispatch. Normal pushes and pull requests never publish.

It validates the managed matrix, packs the requested version, tests the packages as a consumer, verifies a Linux Native AOT publish, uploads both `.nupkg` files to NuGet.org, and creates a GitHub Release with generated notes and the package assets.

The repository secret `NUGET_API_KEY` must be set. A tag determines the version and prerelease flag automatically; a manual run takes both as inputs.

## Invariants the tests exist to protect

If you change the engine, these are the properties that must survive:

1. **A missing snapshot passes and is staged** under the default `missing` policy.
2. **A changed snapshot fails** under `missing`.
3. **Nothing is committed when the method throws** — including from an awaited `finally` block.
4. **A run-wide `verify`** — `IMPRINT_UPDATE=verify` or `CI` — prevents every write regardless of attributes or config, including recovery of an interrupted commit.
5. **A committed `"update": "all"` does not write under `CI`**, but an `IMPRINT_UPDATE` set for that run does.
6. **Comparison never rewrites stored bytes** — equality options work on copies.
7. **An interrupted commit is recoverable** from its journal, and a corrupt journal refuses to roll back rather than guessing.
8. **`snapshots.schema.json` and the configuration reader describe the same keys.** `ConfigurationSchemaTests` compares them in both directions and fails if either side gains, loses, or regroups a key. The schema is what editors use for completion, so drift would mean the editor confidently offering keys that do not work.

## Configuration surface

Two rules keep this from sprawling:

- **Settings are typed.** `[SnapshotSettings]`, `SnapshotOptions`, `SnapshotTestOptions`, and `snapshots.config.json` are the whole surface. Adding an environment variable for a new setting is not on the table.
- **The environment describes the environment, never policy.** Only `IMPRINT_PROJECT_ROOT` and the CI-provider `CI` flag qualify. `IMPRINT_UPDATE` is the single deliberate exception: approving a change must not require editing and reverting a committed file, and with no runner adapter there is no command-line or `.runsettings` channel that reaches the runtime.

When you add a configuration key, you must update `ProjectConfigurationReader`, `snapshots.schema.json`, and the README table together. The drift test fails if you miss the schema; nothing but review catches a missing README row.

## Known limits

- The automatic lifetime depends on a standard `Microsoft.NET.Sdk` compilation.
- Method shapes that cannot be wrapped safely are rejected with IMP102 (see the README's Limitations).
- The store coordinates cooperative local processes. It does not claim distributed-filesystem transactions or protection against hostile concurrent path edits.
