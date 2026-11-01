# Snapshot pruning

Status: accepted for implementation, 2026-11-01.

## Problem

A snapshot folder is created when a test first runs and is never removed. Every rename leaves the old folder behind:

| Change in the test code | What is left behind |
| --- | --- |
| A test method is renamed or deleted | `<suite>/<test>/` |
| A test class is renamed or deleted | `<suite>/` with every test under it |
| A test file moves to another directory | the old `<source dir>/` subtree, because the source directory is part of the path |
| An `[InlineData]` row changes | `<suite>/<test> [<old case>]/` |
| `naming.useFrameworkDisplayNames` is switched | every renamed test folder |

Inside one test folder, the `all` policy already deletes entries the test no longer captures ([SnapshotPlan.cs](../../../../src/TheLithium.Imprint.Core/Execution/SnapshotPlan.cs)). That stays as it is. Pruning handles folders that no current test owns.

## Rule

Pruning works in layers. A layer may prune everything below it, and only once Imprint is certain the whole layer ran in this process. When it is not certain, it does nothing.

| Layer | Complete when | Removes |
| --- | --- | --- |
| Test | Every row of the method ran. Needs a known row count. | The method's case folders that no row visited. |
| Suite, which includes a nested class | Every test of the class ran. | Folders in the suite folder that belong to no current test. |
| Source directory | Every test whose file is in that directory or below ran. | Suite folders and subtrees there that belong to no current test, e.g. a deleted class or a moved file. |
| Project | Every test in the project ran. | Everything stale under the project's snapshot root. |
| All tests | Each test project runs in its own process and prunes its own root. | |

So `dotnet test` prunes the whole project, running one class prunes that class, and running one theory prunes only its old rows. Running one row prunes nothing. A filtered run never touches a suite it didn't run completely.

Updates already follow the same layering, because each test applies its own update policy to its own folder. `IMPRINT_UPDATE=all` on a class updates every test in that class and nothing else.

## What Imprint knows

**The list of tests, from the build.** The generator registers one `SnapshotDescriptor` per instrumented method from a module initializer. A descriptor carries the project directory, source file, suite, test name and display name. That is enough to predict each test's suite folder and test folder with the same code `Settings` uses. The registry is complete before the first test runs, whatever filter the runner applies.

The generator adds four facts to each descriptor:

| Fact | Meaning |
| --- | --- |
| `CaseCount` | The number of `[InlineData]`, `[TestCase]` or `[DataRow]` rows, when those are the method's only data source. Unknown otherwise, e.g. `[MemberData]`, `[TestCaseSource]`, NUnit parameter attributes, generic tests. |
| `Optional` | The method may legitimately not run in a complete run: a helper called by another instrumented method, or a test skipped by its attributes (`Skip`, `[Ignore]`, `[Explicit]`). An optional method never blocks a layer. Its own folders are still kept unless it ran completely. |
| `Explicit` | The method opens its own scope with `Snapshots.Run`, `RunAsync` or `Begin`, so its names are decided at runtime. |
| `Conditional` | The compilation uses `#if` in a test file, or has inactive code that looks like a test. Another build configuration or target framework may compile different tests. |

**What ran, from the run.** Every scope records its test directory and its descriptor when it opens. A method called by another test, inside that test's scope, is recorded as entered too.

## Deciding

A folder is a candidate when it holds snapshot files and no subfolders, or is empty, and no scope visited it in this process.

1. **Attributed to a current test.** The folder is in that test's suite folder and its name is the test's folder name or starts with it followed by ` [`. It is removed only when that test layer is complete. Otherwise it may be a row that didn't run, so it stays.
2. **Attributed to no test.** Find the nearest enclosing directory that contains a current test's suite folder. The folder is removed only when that layer is complete. If the nearest one is the snapshot root itself and the root was configured with `files.snapshotRootPath`, the root may be shared with another project, so the folder stays.

A layer is complete when every required test in it was entered. A required test is one that isn't `Optional`. A parameterized test whose folder names can't be told apart reliably, which happens past 94 characters, is required to run all its rows.

Nothing is pruned in a project while:

- an `Explicit` method hasn't been entered yet, or has written under the project's snapshot root, since its runtime names may be anywhere
- a test's location can't be predicted
- the compilation is `Conditional`, which turns the project to report-only

A folder is removed only when every file in it is a `.json` or `.txt` snapshot or an `.imprint-tmp-*` leftover. A folder with any other file, or with a pending transaction journal, stays and is reported. Links are rejected by the existing path checks. Each removal holds the same per-folder lock as a snapshot commit. Empty parent folders are removed afterwards, never the root and never a folder on the path to a current test.

## When

At the moment a layer becomes complete, while the test host is running: when its last test enters. Tests still running in that layer are visited, so their folders are safe. Waiting for process exit would be simpler, but VSTest stops the test host shortly after the run, so an exit handler can be cut short.

## Policy

A new top-level key in `snapshots.config.json`:

| `prune` | Writable run | Run-wide `verify`, from CI or `IMPRINT_UPDATE=verify` |
| --- | --- | --- |
| `auto` _(default)_ | remove and report | report only |
| `report` | report only | report only |
| `off` | nothing | nothing |

`IMPRINT_PRUNE` overrides it for one run, the same way `IMPRINT_UPDATE` does. An invalid value fails the test, like an invalid `IMPRINT_UPDATE`.

## Report

Whenever a layer completes and finds stale folders, Imprint writes `<build artifacts>/imprint/prune/<run>.json` with each folder, its decision and the reason, and prints one line to stderr:

```text
Imprint removed 3 stale snapshot folders under D:\repo\tests\Api\__snapshots__. See artifacts\imprint\prune\8f2c...json
```

In report mode, or in a verify run, the line says `found` and nothing is removed. Git is the undo.

## AOT and trimming

Pruning runs inside the test binary and follows Core's rules: no reflection, no type discovery. The test list comes from generated `Register` calls. The report is written with `Utf8JsonWriter`. The AOT lane, with ILC and trimming warnings as errors, covers it.

## Limits

- A test that never runs keeps its layer from completing, unless the generator marks it optional. That includes tests skipped at runtime, e.g. `SkipUnless`, and methods that reach a capture but are only called through a delegate.
- Tests split across machines, e.g. CI sharding by filter, never complete a layer.
- Two test projects in one directory share one snapshot root that each sees as its own.
- Entries a running test no longer captures stay under the update policy: `missing` fails, `all` deletes.

## Open points

1. Should a verify run with stale folders fail CI?
2. Should the report also list unused entries that `missing` refused to delete?
