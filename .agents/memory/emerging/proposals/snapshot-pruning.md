# Snapshot pruning

Status: proposal, 2026-11-01. Nothing here is implemented.

## Problem

A snapshot folder is only ever created, never removed. Every rename leaves the old folder behind:

| Change in the test code | What is left behind |
| --- | --- |
| A test method is renamed or deleted | `<suite>/<test>/` |
| A test class is renamed | `<suite>/` with every test under it |
| A test file moves to another directory | the whole `<source dir>/` subtree, because the directory of the source file is part of the path |
| An `[InlineData]` row changes | `<suite>/<test> [<old case>]/` |
| A variant is dropped | `<suite>/<test> [<case>] [<variant>]/` |
| `naming.useFrameworkDisplayNames` is switched | every test folder, since each is renamed |

Today the README tells the user to delete these by hand and watch `git status`. The only automatic removal is inside one test folder: when a running test no longer captures an entry, the `all` policy deletes that file ([SnapshotPlan.cs](../../../../src/TheLithium.Imprint.Core/Execution/SnapshotPlan.cs) marks it `Unused`). Nothing ever looks at a folder that no test visits.

## Goal

After a test run, the baseline root holds exactly the folders that the current test code can produce. The user does nothing. Removal is safe enough to be on by default, and every removal is recorded so it can be reviewed, or reverted with git.

Out of scope: files inside a folder that a test still visits. Those keep the current `Unused` behavior, where `missing` fails and `all` deletes. Pruning works on folders that no test owns anymore.

## What Imprint knows

Two facts make this possible without a CLI, a manifest file in the repo, or reflection.

**The complete list of tests exists at compile time.** The generator registers one `SnapshotDescriptor` per instrumented method from a module initializer ([SnapshotGenerator.cs](../../../../src/TheLithium.Imprint.Generator/SnapshotGenerator.cs) line 218, [Metadata.cs](../../../../src/TheLithium.Imprint.Core/Generation/Metadata.cs)). A descriptor carries the project directory, source file, suite, test name, display name and whether the method is parameterized. The registry is filled when the test assembly loads, before any test runs, and it is complete regardless of which tests the runner then selects. From a descriptor and the project configuration, the expected test folder is computable with the same code that [Settings.cs](../../../../src/TheLithium.Imprint.Core/Configuration/Settings.cs) uses at line 48.

**Every folder a run touches passes through one place.** Each scope resolves its `EffectiveSettings.TestDirectory`. Recording that path gives the set of folders visited in this process, including case folders, variant folders and explicit custom-runner identities, none of which the compile-time list can know.

Together: a folder under the baseline root is expected if the manifest predicts it or this run visited it. Anything else is stale.

## Design

### When it runs

Once per process, when the test process exits normally, through `AppDomain.CurrentDomain.ProcessExit`. This is the only hook that is the same for xUnit, NUnit, MSTest, Microsoft.Testing.Platform and the plain `Main` used under Native AOT. Nothing framework-specific is generated.

Why not at compile time: the MSBuild task could delete test folders that are not in the call graph, but it knows nothing about case folders, and deleting files during a build, including IDE builds, is the wrong moment.

Why not at the start of the run: the manifest alone would be enough for test folders, but case and variant folders need the visited set, which only exists at the end.

A process that is killed, or crashes, never prunes. That is the right outcome. `Snapshots.Prune()` is also exposed for runners that prefer to call it from their own end-of-session hook, and for tests of the feature itself.

### What is expected

For each project directory that has at least one registered descriptor:

1. Resolve the baseline root the way a generated identity would: `files.snapshotRootPath` if set, otherwise `<project>/<files.snapshotFolderName>`. Only this root is considered. A test that sets `SnapshotTestOptions.RootDirectory` to somewhere else is recorded as visited but its root is never pruned.
2. For each descriptor, compute `<root>/<dir of source relative to project>/<Segment(suite)>/` and the test folder name, applying `naming.useFrameworkDisplayNames` the same way `Settings` does. Factor that path computation out of `Settings` so both callers share it.
   - A method without parameters expects exactly one folder.
   - A parameterized method (`RequiresCase`) expects a family: every folder whose name is the sanitized test name followed by ` [`.
3. Add every directory visited in this process.

### What is removed

Walking the root, at the depth of test folders:

| Folder | Decision |
| --- | --- |
| In the expected set | keep |
| Belongs to a parameterized family, not visited this run | remove only when the run was complete, see below |
| In a suite directory the manifest knows, not expected | remove |
| In a source directory the manifest knows, suite unknown | remove the suite directory |
| Source directory unknown to the manifest | remove the subtree |
| Its name cannot be attributed to any test | keep and report |

Then remove suite and source directories that became empty, but never the root itself.

A folder is removed only when every file in it is a snapshot file (`.json`, `.txt`) or an `.imprint-tmp-*` leftover. One other file, such as a `README.md`, keeps the folder and puts it in the report. Reparse points are rejected with the existing `SnapshotPaths.CheckPath` checks.

### Complete runs

A case folder is only known to be stale if its test ran all its rows, and no test host says which rows a filter selected. A good proxy is a complete run: every descriptor registered for the project was visited at least once. `dotnet test` without a filter is complete. "Run all" in an IDE is complete. `--filter` on a class, or "run this row" in Test Explorer, is not, and in that case only test-level folders are pruned, which the manifest proves stale regardless of filtering.

The remaining gap is a filter that selects a subset of rows in every parameterized test of the assembly while still touching every test. That is implausible, and the result is still reversible with git.

### Policy

A new top-level key in `snapshots.config.json`, next to `update`:

| `prune` | Writable run | Run-wide `verify` (CI, or `IMPRINT_UPDATE=verify`) |
| --- | --- | --- |
| `local` _(default)_ | delete and report | report only |
| `report` | report only | report only |
| `off` | nothing | nothing |

`IMPRINT_PRUNE` overrides it for one run, the same way `IMPRINT_UPDATE` does.

When `files.snapshotRootPath` points at a shared root, two test projects would each see the other's folders as unknown and delete them. Imprint cannot tell a shared root from a relocated one, so when `snapshotRootPath` is set the default becomes `report`, and the report says so. Setting `prune` to `local` explicitly restores deletion.

A process in which no descriptor was registered at all, meaning a Core-only consumer with explicit identities, never prunes. Nothing is known, so nothing is stale.

### Report

Before anything is deleted, the plan is written to `<build artifacts>/imprint/prune/<run id>.json`: root, policy, whether the run was complete, each folder with its decision and reason. After deletion one line goes to stderr, because stdout is often swallowed at process exit:

```text
Imprint removed 3 stale snapshot folders under D:\repo\tests\Api\__snapshots__. Report: artifacts\imprint\prune\8f2c...json
```

In `report` mode the verb is `found`. The report is the undo guide. Git is the undo.

### Concurrency

Pruning takes a root-level lock in the existing `storage/locks` directory. Two processes of a multi-target test project run the same tests and visit the same folders, so whichever prunes second finds nothing. Variant folders that differ per target framework are the one case where the first finisher would remove the other's work, if a project sets `Variant` per target and runs targets in parallel. That is documented, and `report` mode covers it.

### AOT and trimming

Everything above runs inside the test binary, so it holds to the same rule as the rest of Core: no reflection, no type discovery, nothing the trimmer can remove. The manifest comes from generated `Register` calls that already exist. The visit log is a concurrent set of strings. The report is written with `Utf8JsonWriter`, as `run.json` is. `ProcessExit` is available under Native AOT and the AOT lane proves it fires. `IlcTreatWarningsAsErrors` and `ILLinkTreatWarningsAsErrors` stay on.

## Changes

| Area | Change |
| --- | --- |
| `Core/Configuration/Settings.cs` | Extract `SnapshotLayout.TestDirectory(root, identity)` so the pruner computes paths with the same code. |
| `Core/Generation/Metadata.cs` | Add `internal static IEnumerable<SnapshotDescriptor> All()`. |
| `Core/Execution/SnapshotScope.cs` | Record `TestDirectory` and the project root in a new `VisitLog` when a scope opens. |
| `Core/Storage/SnapshotPruner.cs` | New. Builds the expected set, walks the root, writes the report, deletes. Registers the `ProcessExit` handler on first scope. |
| `Core/Snapshots.cs` | `public static PruneReport Prune()` for explicit calls. |
| `Core/Configuration/Models/ProjectConfigurationFile.cs`, reader, `schemas/1.0.0/snapshots.schema.json`, `ConfigurationSchemaTests` | `prune` key. Additive, so the schema version stays 1.0.0. |
| `IMPRINT_PRUNE` | Read next to `IMPRINT_UPDATE`. |
| README `Update snapshots`, `Limits`, `Roadmap` | Replace "delete stale folders yourself" with the behavior. Remove the roadmap line. |
| `docs/API.md` | `prune` key, `IMPRINT_PRUNE`, `Snapshots.Prune()`. |

Rough size: 300 to 400 lines of Core, plus tests and docs.

## Tests

- Specifications executable: rename a test folder in a temp project, run, assert removal and the report. Same for a moved source directory, a changed case, and an unattributable folder that must be kept.
- A filtered run (visit one test of two) prunes test folders but not case folders.
- `verify` run reports and deletes nothing. `off` writes no report.
- A folder holding a non-snapshot file survives.
- Shared root default is `report`.
- Core-only process with explicit identities prunes nothing.
- The external consumer `Full` scenario list gets one group for pruning. The AOT lane proves `ProcessExit` fires under Native AOT.
- Framework suites (xUnit, NUnit, MSTest) run once with a planted stale folder.

## Open points

1. Should a run-wide `verify` with stale folders be able to fail CI? A process-exit hook cannot fail a test. The options are an MSBuild target after `VSTest` that reads the report, or leaving CI at report-only.
2. Is `report` the right default for a configured `snapshotRootPath`, or should deletion stay on and the shared-root case be a documented exception?
3. Should the report also list `Unused` entries that `missing` refused to delete, so the user has one place to look?
