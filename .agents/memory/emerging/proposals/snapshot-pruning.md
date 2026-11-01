# Snapshot pruning

Snapshot pruning is the part of Imprint that removes snapshot folders no current test uses, once a run has covered every test that could use them.

A snapshot folder is created when a test first runs. Before pruning, nothing removed it. E.g. renaming `Saves` to `SavesOrder` left `Orders/Saves/` behind next to the new `Orders/SavesOrder/`.

Status: built on 2026-11-01.

## Problem

Every rename leaves the old folder behind:

| Change in the test code | What is left behind |
| --- | --- |
| A test method is renamed or deleted | `<suite>/<test>/` |
| A test class is renamed or deleted | `<suite>/` with every test under it |
| A test file moves to another directory | The old `<source dir>/` subtree, because the source directory is part of the path |
| An `[InlineData]` row changes | `<suite>/<test> [<old case>]/` |
| `naming.useFrameworkDisplayNames` is switched | Every renamed test folder |

Inside one test folder, the `all` policy already deletes entries the test no longer captures ([SnapshotPlan.cs](../../../../src/TheLithium.Imprint.Core/Execution/SnapshotPlan.cs)). That stays as it is. Pruning handles whole folders that no current test uses.

## Layers

Pruning works in layers. A layer can prune everything below it, and only once Imprint is certain the whole layer ran in this process. When it isn't certain, it does nothing.

| Layer | Complete when | Removes |
| --- | --- | --- |
| Test | Every row of the method ran. Needs a known row count. | The method's case folders that no row visited. |
| Suite, which includes a nested class | Every test of the class ran. | Folders in the suite folder that no current test uses. |
| Source directory | Every test whose file is in that directory or below ran. | Suite folders and subtrees there that no current test uses, e.g. a deleted class or a moved file. |
| Project | Every test in the project ran. | Everything stale under the project's snapshot root. |
| All tests | Each test project runs in its own process and prunes its own root. | |

So `dotnet test` prunes the whole project, running one class prunes that class, and running one theory prunes only its old rows. Running one row prunes nothing. A filtered run never touches a suite it didn't run completely.

Updates already follow the same layers, because each test applies its own update policy to its own folder. `IMPRINT_UPDATE=all` on a class updates every test in that class and nothing else.

## Test list

The generator registers one `SnapshotDescriptor` per instrumented method from a module initializer. A descriptor carries the project directory, source file, suite, test name and display name. That is enough to predict each test's suite folder and test folder with the same code `Settings` uses. The registry is complete before the first test runs, whatever filter the runner applies.

The generator adds four facts to each descriptor:

| Fact | Meaning |
| --- | --- |
| `CaseCount` | The number of `[InlineData]`, `[TestCase]` or `[DataRow]` rows, when those are the method's only data source. Unknown otherwise, e.g. `[MemberData]`, `[TestCaseSource]`, NUnit parameter attributes and generic tests. |
| `Optional` | A complete run can leave the method out on purpose. It's a helper called by another instrumented method, or a test skipped by its attributes (`Skip`, `[Ignore]`, `[Explicit]`). An optional method never blocks a layer. Its own folders are still kept unless it ran completely. |
| `Explicit` | The method opens its own scope with `Snapshots.Run`, `RunAsync` or `Begin`, so its names are decided at runtime. |
| `Conditional` | The compilation uses `#if` in a test file, or has inactive code that looks like a test. Another build configuration or target framework might compile different tests. |

## What ran

Every scope records its test directory and its descriptor when it opens. A method called by another test, inside that test's scope, is recorded as entered too.

## Deciding

A folder is a candidate when it holds snapshot files and no subfolders, or is empty, and no scope visited it in this process. A candidate goes one of two ways:

1. Attributed to a current test - the folder is in that test's suite folder, and its name is the test's folder name or starts with it followed by ` [`. It's removed only when that test layer is complete. Until then it might be a row that didn't run, so it stays.
2. Attributed to no test - Imprint finds the nearest enclosing directory that contains a current test's suite folder. The folder is removed only when that layer is complete. If the nearest one is the snapshot root and the root was configured with `files.snapshotRootPath`, another project might share the root, so the folder stays.

A layer is complete when every required test in it was entered. A required test is one that isn't `Optional`. Past 94 characters, the folder names of a parameterized test can't be told apart reliably, so that test must run all its rows.

Nothing is pruned in a project in these cases:

- An `Explicit` method hasn't been entered yet, or has written under the project's snapshot root, since its runtime names could be anywhere.
- A test's location can't be predicted.
- The compilation is `Conditional`, which turns the project to report-only.

A folder is removed only when every file in it is a `.json` or `.txt` snapshot or an `.imprint-tmp-*` leftover. A folder with any other file, or with a pending transaction journal, stays and is reported. Links are rejected by the existing path checks. Each removal holds the same per-folder lock as a snapshot commit. Empty parent folders are removed afterwards, never the root and never a folder on the path to a current test.

## Timing

A layer is evaluated when its last test enters, while the test host is still running. Tests still running in that layer have been visited, so their folders are safe. Waiting for process exit would be simpler, but VSTest stops the test host shortly after the run, so an exit handler can be cut short.

## Policy

The top-level `prune` key in `snapshots.config.json` sets what happens:

| `prune` | Writable run | Run-wide `verify`, from CI or `IMPRINT_UPDATE=verify` |
| --- | --- | --- |
| `auto` _(default)_ | Remove and report | Report only |
| `report` | Report only | Report only |
| `off` | Nothing | Nothing |

`IMPRINT_PRUNE` overrides it for one run, the same way `IMPRINT_UPDATE` does. An invalid value fails the test, like an invalid `IMPRINT_UPDATE`.

## Report

When a layer completes and finds stale folders, Imprint writes `<build artifacts>/imprint/prune/<run id>-<project>.json` and prints one line to stderr. The report lists each folder, what happened to it and the reason:

```text
Imprint removed 3 stale snapshot folders under D:\repo\tests\Api\__snapshots__. See D:\repo\artifacts\imprint\prune\8f2c41d07a9e4b6f9d3c2e1a5b7f6c08-Api.Tests.json
```

In report mode, or in a verify run, the line says `found` and nothing is removed. If the removed files were committed, `git restore` brings them back.

## AOT and trimming

Pruning runs inside the test binary and follows Core's rules, so it uses no reflection and no type discovery. The test list comes from generated `Register` calls. The report is written with `Utf8JsonWriter`. The AOT lane, with ILC and trimming warnings as errors, covers it.

## Limits

- A test that never runs keeps its layer from completing, unless the generator marks it optional. That includes tests skipped at runtime, e.g. `SkipUnless`, and methods that reach a capture but are only called through a delegate.
- Tests split across machines, e.g. CI sharding by filter, never complete a layer.
- Two test projects in one directory share one snapshot root, and each sees it as its own.
- Entries a running test no longer captures stay under the update policy. `missing` fails and `all` deletes.

## Not decided yet

- Should a verify run with stale folders fail CI?
- Should the report also list unused entries that `missing` refused to delete?
