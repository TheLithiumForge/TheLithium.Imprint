# TheLithium.Imprint

[![Build and test](https://github.com/TheLithium/TheLithium.Imprint/actions/workflows/build-test.yml/badge.svg)](https://github.com/TheLithium/TheLithium.Imprint/actions/workflows/build-test.yml)
[![NuGet](https://img.shields.io/nuget/vpre/TheLithium.Imprint?logo=nuget&label=NuGet)](https://www.nuget.org/packages/TheLithium.Imprint)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)

Native AOT, Snapshot testing library for .NET 10 and later. Add `.AssertSnapshot()` to a value in a test you already have.

```csharp
[Fact]
public async Task CreatesAnOrder()
{
    var order = await CreateOrderAsync();

    order.AssertSnapshot();          // writes __snapshots__/OrderTests/CreatesAnOrder/order.json

    Assert.NotEmpty(order.Items);    // your normal assertions still work
}
```

The first run writes the file and passes. Every later run compares against it and fails if it changed.

Works with xUnit, NUnit, MSTest, and anything else that fails a test on an uncaught exception. Serializes without reflection, so it runs under Native AOT.

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [The one rule](#the-one-rule)
- [Updating snapshots](#updating-snapshots)
- [API](#api)
- [Configuration](#configuration)
- [Editor support](#editor-support)
- [What can be captured](#what-can-be-captured)
- [When a test fails](#when-a-test-fails)
- [Volatile data (timestamps, GUIDs)](#volatile-data-timestamps-guids)
- [Where files go](#where-files-go)
- [Parameterized tests](#parameterized-tests)
- [Native AOT](#native-aot)
- [Custom runners](#custom-runners)
- [More](#more)

## Install

```text
dotnet add package TheLithium.Imprint --version 0.1.0-preview.2
```

That is the whole setup. No base class, no fixture, no `[UsesVerify]`, no CLI tool.

The package contains the runtime, a source generator, and an MSBuild task. Only the runtime ships into your test binary; the other two run at compile time.

> `TheLithium.Imprint.Core` is also published on its own. You only need it if you are writing a custom test runner — see [Custom runners](#custom-runners).

## Quick start

```csharp
using TheLithium.Imprint;
using Xunit;

public sealed class OrderTests
{
    [Fact]
    public void CreatesAnOrder()
    {
        var order = new Order("order-123", 42.50m, ["Keyboard", "Cable"]);

        order.AssertSnapshot();
    }
}
```

Run the test. It passes, and this file appears next to your test source:

```text
__snapshots__/OrderTests/CreatesAnOrder/order.json
```

```json
{
    "Id": "order-123",
    "Items": ["Keyboard", "Cable"],
    "Total": 42.5
}
```

Commit that file. It is now the baseline — review it in pull requests like any other code.

Change `42.50m` to `43.50m` and the test fails with a diff. Either the change is a bug, or it is intended and you [update the snapshot](#updating-snapshots).

Capture as many values as you like. Give each one a name:

```csharp
[Fact]
public void CreatesAnOrder()
{
    var order = CreateOrder();

    order.AssertSnapshot("order");                 // order.json
    order.Items.AssertSnapshot("items");           // items.json
    "Ready for dispatch".AssertSnapshot("status"); // status.txt  (strings are stored as text)
}
```

Without a name, the variable name is used (`order.AssertSnapshot()` → `order.json`). Name them explicitly once you have more than one.

## The one rule

**Snapshots are written only after the test method returns successfully.**

Captures are serialized the moment you call `AssertSnapshot`, held in memory, and compared as a set when the method finishes. If anything throws — your assertion, the code under test, an awaited `finally` block — nothing is written and the baseline is untouched.

```csharp
[Fact]
public async Task ReadsState()
{
    var before = await ReadAsync();
    before.AssertSnapshot("before");   // captured here, written at the end

    try
    {
        await MutateAsync();           // if this throws,
    }
    finally
    {
        await RestoreAsync();          // or this does,
    }
}                                      // "before" is never written
```

Two consequences worth knowing:

- A failing test never leaves a half-approved snapshot behind.
- Teardown that runs _outside_ the method — an xUnit `IAsyncLifetime.DisposeAsync`, an NUnit `[TearDown]`, an MSTest `[TestCleanup]` — happens after the boundary and cannot stop an approval. [Worked example](#teardown-outside-the-method-cannot-affect-approval).

## Updating snapshots

You have changed the code on purpose and the new output is correct. Pick whichever fits:

**One test run, from the command line** — nothing to edit, nothing to undo:

```bash
IMPRINT_UPDATE=all dotnet test
```

Narrow it with your runner's own filter. A test that does not run cannot be updated:

```bash
IMPRINT_UPDATE=all dotnet test --filter 'FullyQualifiedName~OrderTests'
```

**One capture, in code** — change `AssertSnapshot` to `UpdateSnapshot`, run, change it back:

```csharp
response.UpdateSnapshot("response");
```

**One test or class, in code** — while you are iterating on it:

```csharp
[SnapshotSettings(Update = SnapshotUpdate.All)]
public sealed class ContractTests { /* ... */ }
```

**Everything in the project** — set `"update": "all"` in `snapshots.config.json`. Set it back to `"missing"` when you are done.

### The update policies

| Policy                | Missing snapshot | Changed snapshot | Snapshot no longer captured |
| --------------------- | ---------------- | ---------------- | --------------------------- |
| `verify`              | fail             | fail             | fail                        |
| `missing` _(default)_ | **create, pass** | fail             | fail                        |
| `all`                 | create           | overwrite        | delete                      |

`all` deletes unused files only when it applies to a whole test (an attribute, the config file, or `IMPRINT_UPDATE`). A single `UpdateSnapshot()` call never deletes anything.

A run-wide `verify` — `IMPRINT_UPDATE=verify`, or CI — means nothing on disk changes at all. An interrupted commit from an earlier run is reported rather than rolled back, so a verification run never modifies your working tree.

### CI verifies by default

When `CI=true` is set — every major CI provider sets it — the policy becomes `verify`, whatever the config file says. A missing snapshot fails the build instead of being quietly created.

To update from CI on purpose, set `IMPRINT_UPDATE` for that run. An explicit request for one run beats the default; a committed `"update": "all"` never does.

### The two environment variables

Almost everything is configured through the typed API — `[SnapshotSettings]`, `SnapshotOptions`, and `snapshots.config.json`. Two things cannot be, so they are read from the environment:

| Variable               | Why it is not a typed setting                                                                                                                                                                                                                                                                        |
| ---------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IMPRINT_UPDATE`       | `verify`, `missing`, or `all` for one run. Approving a change must not require editing and then reverting a committed file — forgetting to revert `"update": "all"` silently disables your whole suite. Imprint has no runner adapter, so no command-line flag or `.runsettings` value can reach it. |
| `IMPRINT_PROJECT_ROOT` | The project path is baked in at compile time. This repoints it when the tests run somewhere else than they were built — building in one container and testing in another, for example. It describes the environment, not a policy.                                                                   |

`CI` is also read, but it is set by your CI provider, not by you.

Both are read by the normal `dotnet test`. There is no separate Imprint command.

## API

Four methods cover almost everything:

```csharp
value.AssertSnapshot();                          // compare against the baseline
value.AssertSnapshot("name");                    // ...into name.json
value.AssertSnapshot("name", options);           // ...with per-capture options
value.UpdateSnapshot("name");                    // overwrite this one baseline
```

`AssertSnapshot` and `UpdateSnapshot` both take an optional explicit writer — see [What can be captured](#what-can-be-captured).

### Per-capture options — `SnapshotOptions`

```csharp
payload.AssertSnapshot("payload", new SnapshotOptions
{
    Format     = SnapshotFormat.Json,   // parse this string as JSON instead of storing it as text
    Comparison = new SnapshotComparison { NumericTolerance = 0.001m },
    Comparer   = new MyComparer(),      // full control over equality
    Update     = SnapshotUpdate.All     // policy for this capture only
});
```

| Property     | Meaning                                                                                                                                                     |
| ------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Format`     | `Auto` (default): text for strings, JSON for everything else. `Json` parses a string as JSON. `Text` / `Snap` force text with a `.txt` / `.snap` extension. |
| `Comparison` | Equality rules for this capture. Replaces the project rules wholesale.                                                                                      |
| `Comparer`   | Your own `ISnapshotComparer`. See [Volatile data](#volatile-data-timestamps-guids).                                                                         |
| `Update`     | Update policy for this capture only.                                                                                                                        |

### Equality rules

These decide when two snapshots count as equal. **They never change what is written to disk** — the stored file is always the exact captured value.

| Property                   | Default | Meaning                                                                                      |
| -------------------------- | ------- | -------------------------------------------------------------------------------------------- |
| `IgnoreLineEndings`        | `true`  | Treat CRLF and LF as equal. Keep this on for cross-platform repos.                           |
| `NumericTolerance`         | `0`     | Largest accepted absolute difference between two JSON numbers. For floats.                   |
| `IgnoreArrayOrder`         | `false` | Match array elements in any order. Duplicates still have to appear the same number of times. |
| `IgnoreTrailingWhitespace` | `false` | Ignore spaces and tabs at the end of each line.                                              |
| `IgnoreStringCase`         | `false` | Compare string _values_ case-insensitively. Property names stay case-sensitive.              |
| `MaxUnorderedArrayLength`  | `256`   | Longest array `IgnoreArrayOrder` will try to match, to bound the cost.                       |

`IgnoreArrayOrder` uses real multiset matching, not a greedy pass, so it stays correct when combined with `NumericTolerance`.

### Per-test settings — `[SnapshotSettings]`

On a method or a class. A method wins over its class.

```csharp
[SnapshotSettings(Update = SnapshotUpdate.All, Name = "Contract")]
```

| Property | Meaning                                                                                          |
| -------- | ------------------------------------------------------------------------------------------------ |
| `Update` | Update policy for this test or every test in the class.                                          |
| `Name`   | Folder name — the test folder on a method, the suite folder on a class. Defaults to the C# name. |

### Reading the result

A mismatch throws `SnapshotMismatchException`, which carries a structured `Report`:

```csharp
catch (SnapshotMismatchException e)
{
    foreach (var entry in e.Report.Entries)
    {
        // entry.Name, entry.FileName, entry.Status, entry.Difference
    }
}
```

`SnapshotStatus` is one of `Matched`, `Missing`, `Changed`, `Unused` (failures), or `Created`, `Updated`, `Removed` (approvals).

All exceptions derive from `SnapshotException`: `SnapshotMismatchException` (a snapshot differs), `SnapshotCaptureException` (serialization failed), `SnapshotConfigurationException` (bad config or no active test), `SnapshotConflictException` (another process changed the baseline mid-test).

## Configuration

Optional. Drop a `snapshots.config.json` in the test-project root. Every key has a default, so only write the ones you are changing.

```json
{
    "$schema": "https://raw.githubusercontent.com/TheLithium/TheLithium.Imprint/main/snapshots.schema.json",
    "version": 1,

    "update": "missing",

    "comparison": {
        "ignoreTrailingWhitespace": true
    }
}
```

That `$schema` line gives you completion and inline documentation in VS Code, Visual Studio, and Rider — see [Editor support](#editor-support).

The full shape, with defaults:

```json
{
    "version": 1,

    "update": "missing",
    "allowEmptyTests": false,

    "files": {
        "snapshotFolderName": "__snapshots__",
        "snapshotRootPath": null,
        "failureArtifactPath": "artifacts/imprint/failures",
        "textFileExtension": "txt"
    },

    "naming": {
        "unnamedCaptures": "name-then-order",
        "useFrameworkDisplayNames": false
    },

    "comparison": {
        "numericTolerance": 0,
        "ignoreArrayOrder": false,
        "ignoreStringCase": false,
        "ignoreLineEndings": true,
        "ignoreTrailingWhitespace": false,
        "maxUnorderedArrayLength": 256
    },

    "limits": {
        "maxNestingDepth": 64,
        "maxValuesPerSnapshot": 100000,
        "maxBytesPerSnapshot": 4194304,
        "lockTimeoutSeconds": 10
    }
}
```

**Top level** — the two policies you are most likely to change.

| Key               | Meaning                                                                       |
| ----------------- | ----------------------------------------------------------------------------- |
| `update`          | `verify`, `missing`, or `all`. See [the table above](#the-update-policies).   |
| `allowEmptyTests` | Let a test that captures nothing pass instead of failing as a likely mistake. |

**`files`** — where things are written.

| Key                   | Meaning                                                                                                    |
| --------------------- | ---------------------------------------------------------------------------------------------------------- |
| `snapshotFolderName`  | Folder created next to your test sources to hold the snapshots.                                            |
| `snapshotRootPath`    | Put snapshots somewhere else entirely. Project-relative or absolute. Replaces the source-adjacent default. |
| `failureArtifactPath` | Where expected/received files go when a test fails. Must be outside the snapshot root.                     |
| `textFileExtension`   | `txt` or `snap`, for text snapshots.                                                                       |

**`naming`** — how folders and files get their names.

| Key                        | Meaning                                                                                                                                                                            |
| -------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `unnamedCaptures`          | Filename when a capture has no explicit name. `name-then-order` (variable name, else `snapshot-1`), `order` (always `snapshot-1`), or `explicit-only` (require a name every time). |
| `useFrameworkDisplayNames` | Name the test folder after `[Fact(DisplayName = "...")]` instead of the method name, when one exists.                                                                              |

**`comparison`** — the same six rules as [`SnapshotComparison`](#equality-rules).

**`limits`** — safety valves. Raise one only when a legitimate snapshot hits it.

| Key                    | Meaning                                                                   |
| ---------------------- | ------------------------------------------------------------------------- |
| `maxNestingDepth`      | Deepest object or array nesting that will be serialized.                  |
| `maxValuesPerSnapshot` | Most values one capture may visit. Catches runaway graphs.                |
| `maxBytesPerSnapshot`  | Largest single snapshot file, in UTF-8 bytes.                             |
| `lockTimeoutSeconds`   | How long to wait for another process to release the snapshot folder lock. |

Unknown keys, duplicate keys, and wrong types are errors, not warnings — a typo will not be silently ignored, and a key that moved or was renamed tells you its new spelling:

```text
Unknown configuration property: maxNodes. It is now "limits.maxValuesPerSnapshot".
```

### Editor support

The keys above are described by [`snapshots.schema.json`](snapshots.schema.json), which ships inside the NuGet package and is published in this repository. A test keeps it in step with the reader, so it can never document a key that does not work or miss one that does.

To get completion and hover documentation, point your config at it:

| How                               | Setup                                                                                                                                   | Works offline                         |
| --------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------- |
| **`$schema` URL** _(recommended)_ | Add the `$schema` line shown above.                                                                                                     | No — fetched and cached by the editor |
| **`$schema` relative path**       | Copy `snapshots.schema.json` out of the package into your repo, then `"$schema": "./snapshots.schema.json"`.                            | Yes                                   |
| **VS Code workspace setting**     | Map the filename in `.vscode/settings.json` under `json.schemas`. Commit it, and everyone on the repo gets it without a `$schema` line. | With a local copy                     |

A fourth option needs no setup at all: registering the schema with [SchemaStore](https://www.schemastore.org), whose catalog ships inside VS Code and Rider. Once `snapshots.config.json` is in that catalog, the filename alone is enough — no `$schema` line, nothing to configure. That is a pull request to the SchemaStore repository rather than a code change here, and the schema already carries the `$id` it needs.

## What can be captured

**The declared type of the expression is the contract.** The source generator reads that type at compile time and emits a direct serializer for it. Nothing is discovered by reflection at runtime, which is what makes Native AOT work.

Out of the box: records, classes and structs with public members, anonymous types, tuples, enums, nullables, collections, dictionaries, arrays (rank 1–3), and the usual primitives — `string`, numbers, `Guid`, `DateTime`, `TimeSpan`, `Uri`, `JsonElement`, and friends.

Because the _declared_ type is the contract, this matters:

```csharp
Animal pet = new Dog { Name = "Rex", GoodBoy = true };
pet.AssertSnapshot();   // captures Animal.Name — NOT Dog.GoodBoy
```

Runtime subtypes are not discovered. If you want the derived members, declare the variable as `Dog`.

For a private type, a third-party type, or a deliberate projection, pass a writer:

```csharp
value.AssertSnapshot(
    static (json, item, context) =>
    {
        json.WriteStartObject();
        json.WriteString("id", item.Id);
        json.WriteNumber("itemCount", item.Items.Length);
        json.WriteEndObject();
    },
    "projection");
```

If the generator cannot build a writer for a type it sees, it reports **IMP001** at compile time with the reason — not a runtime surprise. For a type reached only through a generic helper, root it with `[assembly: SnapshotInclude<MyType>]`.

Full details in [docs/TYPE-SUPPORT.md](docs/TYPE-SUPPORT.md).

## When a test fails

```text
TheLithium.Imprint.SnapshotMismatchException : Snapshot mismatch: OrderTests.CreatesAnOrder

order.json: Changed
  $["Items"][1]: expected "Cable", received "Cord".
--- expected
+++ received
@@ -2,8 +2,8 @@
    "Id": "order-123",
    "Items": [
      "Keyboard",
-     "Cable"
+     "Cord"
    ],
-   "Total": 42.50
+   "Total": 43.50
  }

Received files: .../artifacts/imprint/failures/<run>/OrderTests/CreatesAnOrder/<id>

Review the differences, then authorize updates with a method/class attribute,
UpdateSnapshot(), or the update setting in snapshots.config.json.
```

All entries of a test are reported together, so you see every difference in one run rather than one per fix.

Large values are abbreviated in the message. The full expected and received files are written to the artifact directory so you can diff them with your own tools.

## Volatile data (timestamps, GUIDs)

A payload with a timestamp in it changes every run. Imprint has no scrubbing or redaction pipeline; the way to handle this is an `ISnapshotComparer`, which decides equality for one capture:

```csharp
public sealed class IgnoreTimestamps : ISnapshotComparer
{
    private static string Scrub(string text) =>
        Regex.Replace(text, @"\d{4}-\d{2}-\d{2}T[\d:.]+Z", "<timestamp>");

    public SnapshotComparisonResult Compare(
        string expected, string received, SnapshotFormat format, SnapshotComparison options)
        => Scrub(expected) == Scrub(received)
            ? SnapshotComparisonResult.Match
            : new SnapshotComparisonResult(false, "Differs after ignoring timestamps.");
}

// then:
payload.AssertSnapshot("payload", new SnapshotOptions { Comparer = new IgnoreTimestamps() });
```

The stored file keeps the real timestamp; only the comparison ignores it.

The alternative — often the better one — is to not capture the volatile field at all, by projecting the value first or using an explicit writer.

## Where files go

```text
__snapshots__/
  <directory of the test source file, relative to the project>/
    <suite>/
      <test> [<case>] [<variant>]/
        <capture>.json
```

- **suite** — the containing class, or `[SnapshotSettings(Name = "...")]` on the class.
- **test** — the method, or `Name` on the method, or the framework display name if `naming.useFrameworkDisplayNames` is on.
- **case** — one row of a parameterized test. Added automatically.
- **variant** — an extra discriminator you set explicitly, for intentionally different baselines per target or configuration.
- **capture** — the name you passed to `AssertSnapshot`.

Paths come from compile-time source metadata, not the working directory, so tests find their snapshots wherever they run from. Names are normalized to be portable across Windows, Linux, and macOS; case-insensitive collisions are rejected rather than silently merged.

## Parameterized tests

Each row gets its own folder, automatically. No wrapper code and no per-row naming:

```csharp
[Theory]
[InlineData("first", 1)]
[InlineData("second", 2)]
public void Parses(string label, int count)
{
    Parse(label, count).AssertSnapshot("result");
}
```

```text
__snapshots__/ParserTests/Parses [count=1, label=first]/result.json
__snapshots__/ParserTests/Parses [count=2, label=second]/result.json
```

The `[...]` part appears **only for parameterized tests**. An ordinary `[Fact]` gets a plain folder — `__snapshots__/ParserTests/Parses/result.json`.

The label is built from the actual argument values, so it is readable in a diff and stable across runs. xUnit `InlineData`, NUnit `TestCase`, and MSTest `DataRow` all work the same way. `CancellationToken` parameters are excluded.

A short hash is appended **only when the label alone could not tell two rows apart**:

```text
Parses [count=1, label=first]              plain — the label names every argument
Parses [value=1~9f2c1a4b7e05]              string "1" and number 1 would both read as 1
Parses [value=a, b=c~3d7e02f1ac88]         the value contains the label's own separators
Parses [first=aaaaaaaa…aaa~5b1c9e4470af]   the label was too long and had to be truncated
```

If you see a hash, it is carrying real information. Otherwise the path stays short — which matters, because these files get committed. See [Path length](#path-length-is-yours-to-watch).

The case identifies the _invocation_; the capture name identifies the _file_. If one invocation produces several logical outputs, name them:

```csharp
result.AssertSnapshot("result");
headers.AssertSnapshot("headers");
```

And if you loop over runtime data inside one invocation, put a stable id in the name:

```csharp
foreach (var item in items)
{
    item.AssertSnapshot($"item-{item.Id}");
}
```

## Native AOT

The runtime does no reflection: serializers are generated at compile time and registered by module initializers. The generator and MSBuild task run during the build and are not shipped into your app.

Verified by publishing a test executable with `PublishAot=true`, `IlcTreatWarningsAsErrors=true`, and `ILLinkTreatWarningsAsErrors=true`, on Windows, Linux, and macOS.

One caveat that is not about Imprint: your _test runner_ also has to support Native AOT. Runners that discover tests by reflection generally do not. For an AOT test project, drive the tests from a plain `Main` with an explicit list, which is what [`tests/TheLithium.Imprint.Specifications`](tests/TheLithium.Imprint.Specifications) does.

## Custom runners

You almost certainly do not need this. The build integration supplies the test lifetime automatically for any method compiled by the standard .NET SDK.

If you are writing a runner it cannot see — or a host that compiles test bodies itself — open the scope yourself:

```csharp
using var scope = Snapshots.Begin(new SnapshotTestOptions
{
    Identity = new SnapshotTestIdentity(
        ProjectDirectory: projectRoot,
        SourceFile: sourceFile,
        Suite: "ContractTests",
        Test: "CurrentContract",
        Case: "default")
});

try
{
    ExecuteTestBody();
    scope.Complete();     // only after everything that should affect approval has succeeded
}
catch (Exception error)
{
    scope.Abort(error);
    throw;
}
```

`Snapshots.Run(...)` and `Snapshots.RunAsync(...)` wrap that pattern. `Dispose` without `Complete` abandons the scope and approves nothing.

`SnapshotTestOptions` carries the settings a runner may need to override per test: `Name`, `Suite`, `Case`, `Variant`, `Identity`, `RootDirectory`, `ArtifactDirectory`, `ConfigurationFile`, `Comparison`, `Naming`, `AllowEmpty`, `MaxNestingDepth`, `MaxValuesPerSnapshot`, `MaxBytesPerSnapshot`, and `CancellationToken`. In an ordinary test method you use `[SnapshotSettings]` and the config file instead.

See [docs/FRAMEWORKS.md](docs/FRAMEWORKS.md).

## Limitations

Worth knowing before you adopt it.

### .NET 10 or later

`net10.0` is the floor, not a ceiling — the package targets `net10.0`, so .NET 11 and later projects consume it normally. There is no `netstandard2.0` or .NET Framework build.

### Some method shapes cannot be given a lifetime

The build task turns your test body into a local function so it can act after the body returns. A handful of shapes have no reliable "after", so they are rejected at compile time with **IMP102** rather than wrapped incorrectly. In every case the fix is small:

```csharp
// ✗ async void — the runner returns before the body finishes
public async void Rejected()
{
    (await LoadAsync()).AssertSnapshot();
}

// ✓ return Task
public async Task Accepted()
{
    (await LoadAsync()).AssertSnapshot();
}
```

```csharp
// ✗ iterator — the body runs lazily, after the call has already returned
public IEnumerable<int> Rejected()
{
    yield return 1;
    value.AssertSnapshot();
}

// ✓ materialize, then capture
public void Accepted()
{
    var items = Produce().ToArray();
    items.AssertSnapshot();
}
```

```csharp
// ✗ ref / in / out parameters — the body cannot move into a local function
public void Rejected(out int count)
{
    count = 1;
    count.AssertSnapshot();
}

// ✓ return the value instead
public void Accepted()
{
    var count = Compute();
    count.AssertSnapshot();
}
```

Also rejected: methods returning a custom awaitable from `async` (use `Task`/`ValueTask`), by-ref returns (`ref int Rejected()`), and tests declared on a `struct` (use a `class`).

`void`, `T`, `Task`, `Task<T>`, `ValueTask`, and `ValueTask<T>` are all accepted.

### Teardown outside the method cannot affect approval

Approval happens when the test method returns. Anything the framework runs _after_ that is too late to stop it:

```csharp
public sealed class Tests : IAsyncLifetime          // xUnit; NUnit [TearDown], MSTest [TestCleanup] behave the same
{
    [Fact]
    public async Task Example()
    {
        var result = await CallServiceAsync();
        result.AssertSnapshot();
    }                                                // ← snapshot approved here

    public async Task DisposeAsync()
    {
        await AssertNoServerErrorsAsync();           // ← too late; the snapshot is already written
    }
}
```

Move the check inside the method, and it counts:

```csharp
[Fact]
public async Task Example()
{
    var result = await CallServiceAsync();
    result.AssertSnapshot();

    await AssertNoServerErrorsAsync();               // throws → nothing is written
}
```

The same applies to a `finally` block: one _inside_ the method is within the boundary, one in a framework hook is not.

### Changing a data row leaves its old folder behind

The case folder is part of the path, so editing an `[InlineData]` value creates a new folder and the old one simply stops being visited. Nothing deletes it — there is no global orphan pruning, by design. Delete stale folders yourself; `git status` will show them as untracked or unchanged strays.

### Not provided

No scrubbing pipeline, no image or binary snapshot formats, no CLI updater, no global orphan pruning. See [Volatile data](#volatile-data-timestamps-guids) for the intended approach to changing values.

Pruning helpers are an idea for now.

### Storage guarantees

The store coordinates cooperative local processes with locks, journals, and fingerprints. It does not claim distributed-filesystem transactions or protection against hostile concurrent edits.

### Path length

Imprint bounds every path _segment_, but the full path is your repository layout plus your suite, test, case, and capture names. .NET writes long paths happily; **git on Windows does not** unless `core.longpaths` is `true`. A path over ~260 characters can be written by a passing test and then silently skipped by `git add`, so CI sees a missing baseline that you cannot reproduce locally.

Keep names reasonable, or set `core.longpaths=true`. Imprint already helps by defaulting the test folder to the C# method name rather than the framework display name (`naming.useFrameworkDisplayNames` is `false`), and by omitting the case hash whenever the readable label is already unambiguous.

## More

- [docs/FRAMEWORKS.md](docs/FRAMEWORKS.md) — per-framework notes and the explicit adapter boundary
- [docs/TYPE-SUPPORT.md](docs/TYPE-SUPPORT.md) — the complete serialization contract
- [docs/DESIGN.md](docs/DESIGN.md) — how the lifetime, generator, and transactional store work
- [docs/CONTRIBUTING.md](docs/CONTRIBUTING.md) — building, testing, CI, and releases
- [LICENSING.md](LICENSING.md)
