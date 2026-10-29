# Imprint

Imprint is a snapshot testing library for C# that keeps snapshots readable and easy to review.

A snapshot test saves a value to a file on its first run, then fails on a later run if the value changed. With Imprint, you add `.AssertSnapshot()` to a value in a test you already have. The snapshot writers are generated at compile time, so Imprint works in normal .NET tests and under Native AOT.

[![Build](https://img.shields.io/github/actions/workflow/status/TheLithiumForge/TheLithium.Imprint/build.yml?branch=main&event=push&label=build)](https://github.com/TheLithiumForge/TheLithium.Imprint/actions/workflows/build.yml?query=branch%3Amain)
[![Test](https://img.shields.io/github/actions/workflow/status/TheLithiumForge/TheLithium.Imprint/test.yml?branch=main&event=push&label=test)](https://github.com/TheLithiumForge/TheLithium.Imprint/actions/workflows/test.yml?query=branch%3Amain)
[![Test AOT](https://img.shields.io/github/actions/workflow/status/TheLithiumForge/TheLithium.Imprint/test-aot.yml?branch=main&event=push&label=test%20AOT)](https://github.com/TheLithiumForge/TheLithium.Imprint/actions/workflows/test-aot.yml?query=branch%3Amain)
[![NuGet releases](https://img.shields.io/badge/NuGet-releases-004880?logo=nuget&logoColor=white)](https://github.com/TheLithiumForge/TheLithium.Imprint/releases)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![.NET 11 preview](https://img.shields.io/badge/.NET-11%20preview-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/11.0)

## Requirements

You need:

- .NET 10 or later. The package targets `net10.0`, so .NET 11 and later projects use it as normal. There's no `netstandard2.0` or .NET Framework build.
- A test framework that fails a test on an uncaught exception. xUnit, NUnit and MSTest all do. [Test frameworks](docs/FRAMEWORKS.md) has the details for each one.

## Install

Run this in your test project:

```bash
dotnet add package TheLithium.Imprint --version 1.0.2
```

Installing the package is the whole setup. If you come from Verify, you don't need a base class, a fixture, `[UsesVerify]` or a CLI tool.

The package has three parts:

- The runtime - the only part that ships into your test binary.
- A source generator - builds a snapshot writer for each captured type. It runs at compile time.
- An MSBuild task - gives each test method its snapshot lifetime. It runs at compile time.

`TheLithium.Imprint.Core` is also published on its own. You only need it to write a custom test runner, as [Custom runners](#custom-runners) describes.

## Quick start

Add `AssertSnapshot` to a value in a test:

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

Run the test. It passes, and `__snapshots__/OrderTests/CreatesAnOrder/order.json` appears next to your test source:

```json
{
  "Id": "order-123",
  "Items": [
    "Keyboard",
    "Cable"
  ],
  "Total": 42.50
}
```

Commit that file. It's now the baseline, the stored value that every later run compares against. Review it in pull requests like any other code.

Then change `42.50m` to `43.50m` and run the test again. It fails with a diff. If the change is a bug, fix the code. If you made it on purpose, [update the snapshot](#update-snapshots).

## How to use

### Capture values

You can capture as many values as you like in one test. Give each one a name:

```csharp
[Fact]
public async Task CreatesAnOrder()
{
    var order = await CreateOrderAsync();

    order.AssertSnapshot("order");                 // writes order.json
    order.Items.AssertSnapshot("items");           // writes items.json
    "Ready for dispatch".AssertSnapshot("status"); // writes status.txt

    Assert.NotEmpty(order.Items);                  // your normal assertions still work
}
```

A string is stored as text in a `.txt` file. Every other value is stored as JSON in a `.json` file.

Without a name, Imprint uses the variable name, so `order.AssertSnapshot()` writes `order.json`. Once a test has more than one capture, name each one.

### Approval

Imprint writes snapshots only after the test method returns successfully.

Each `AssertSnapshot` call serializes the value right away and holds it in memory. When the method finishes, Imprint compares all the captures as a set. If anything throws, nothing is written and the baseline stays as it was. That includes your assertion, the code under test and an awaited `finally` block:

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
}                                      // "before" isn't written
```

So a failing test never leaves a half-approved snapshot behind.

Framework teardown runs after the method returns, so it's too late to stop an approval. E.g. xUnit `IAsyncLifetime.DisposeAsync`, NUnit `[TearDown]` and MSTest `[TestCleanup]` all behave like this:

```csharp
public sealed class Tests : IAsyncLifetime
{
    [Fact]
    public async Task Example()
    {
        var result = await CallServiceAsync();
        result.AssertSnapshot();
    }                                         // snapshot approved here

    public async Task DisposeAsync()
    {
        await AssertNoServerErrorsAsync();    // too late, the snapshot is already written
    }
}
```

To make the check count, move it into the method:

```csharp
[Fact]
public async Task Example()
{
    var result = await CallServiceAsync();
    result.AssertSnapshot();

    await AssertNoServerErrorsAsync();        // if this throws, nothing is written
}
```

The same goes for `finally`. A `finally` block inside the method counts. One in a framework hook doesn't. If you can't move the teardown into the method, open the scope yourself, as [Custom runners](docs/FRAMEWORKS.md#custom-runners) shows.

### When a test fails

A changed snapshot fails the test with a message like this:

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

Imprint reports all the entries of a test together, so you see every difference in one run instead of one per fix.

Large values are shortened in the message. The full expected and received files go to the failure artifact folder, so you can diff them with your own tools.

The exception is a `SnapshotMismatchException`. Its `Report` lists each entry with its status and difference, as [Reports and failures](docs/API.md#reports-and-failures) describes.

### Update snapshots

If you changed the code on purpose and the new output is correct, you can approve it at four scopes:

- One run - set `IMPRINT_UPDATE=all` for that run. You don't edit or revert any file.
- One capture - change `AssertSnapshot` to `UpdateSnapshot`, run the test, then change it back.
- One test or class - add `[SnapshotSettings(Update = SnapshotUpdate.All)]` while you work on it.
- The whole project - set `"update": "all"` in `snapshots.config.json`. Set it back to `"missing"` when you're done.

To update from the command line, set the variable for one run:

```bash
IMPRINT_UPDATE=all dotnet test
```

To update fewer tests, add your runner's own filter. A test that doesn't run can't be updated:

```bash
IMPRINT_UPDATE=all dotnet test --filter 'FullyQualifiedName~OrderTests'
```

To update one capture, call `UpdateSnapshot` in place of `AssertSnapshot`:

```csharp
response.UpdateSnapshot("response");
```

To update one test or class, add the attribute:

```csharp
[SnapshotSettings(Update = SnapshotUpdate.All)]
public sealed class ContractTests { /* ... */ }
```

#### Update policies

An entry is unused when the baseline has a snapshot that the test no longer captures. Each policy decides what happens to missing, changed and unused entries:

| Policy                | Missing          | Changed   | Unused |
| --------------------- | ---------------- | --------- | ------ |
| `verify`              | fail             | fail      | fail   |
| `missing` _(default)_ | create, and pass | fail      | fail   |
| `all`                 | create           | overwrite | delete |

`all` deletes unused files only when it applies to the whole test, through an attribute, the config file or `IMPRINT_UPDATE`. A single `UpdateSnapshot()` call never deletes anything.

A run-wide `verify` stops baseline changes. You get it from `IMPRINT_UPDATE=verify` or from CI. In that mode, an interrupted baseline transaction from an earlier run is reported instead of recovered. Locks and failure diagnostics can still write.

#### CI

When `CI=true` is set, the policy is `verify` whatever the config file says. Every major CI provider sets it. So a missing snapshot fails the build instead of being created.

To update from CI on purpose, set `IMPRINT_UPDATE` for that run. A request for one run wins over the CI default. A committed `"update": "all"` never does.

[Run environment](docs/API.md#run-environment) covers each environment variable and why it isn't a typed setting.

### Snapshot format

A snapshot is only worth having if you can read it in a pull request and tell whether the change is right. So Imprint writes each value the way you'd write it by hand. E.g. take this capture:

```csharp
new
{
    Status = Fulfilment.Shipped,
    Placed = new DateTime(2026, 3, 9, 14, 5, 0, DateTimeKind.Utc),
    Day = new DateOnly(2026, 3, 9),
    Elapsed = TimeSpan.FromMinutes(93),
    Id = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff"),
    Total = 42.50m,
    Ratio = 0.1 + 0.2,
    Big = long.MaxValue,
    Missing = (int?)null,
    Where = new Uri("https://example.com/orders?id=1&x=2"),
    Tags = new[] { "keyboard", "cable" },
    ByCountry = new Dictionary<string, int> { ["NL"] = 2, ["BE"] = 1 }
}.AssertSnapshot("types");
```

It writes this `types.json`:

```json
{
  "Big": 9223372036854775807,
  "ByCountry": {
    "BE": 1,
    "NL": 2
  },
  "Day": "2026-03-09",
  "Elapsed": "01:33:00",
  "Id": "6f9619ff-8b86-d011-b42d-00cf4fc964ff",
  "Missing": null,
  "Placed": "2026-03-09T14:05:00.0000000Z",
  "Ratio": 0.30000000000000004,
  "Status": "Shipped",
  "Tags": [
    "keyboard",
    "cable"
  ],
  "Total": 42.50,
  "Where": "https://example.com/orders?id=1&x=2"
}
```

Enums use their declared name and `decimal` keeps its scale. `double` is written exactly, so floating-point drift shows up in the diff instead of being rounded away. To accept a small difference, set [`NumericTolerance`](docs/API.md#comparison). Dates and numbers are formatted invariantly, so a machine in another culture writes the same bytes. [How values appear](docs/TYPE-SUPPORT.md#how-values-appear) gives the rule for each type.

Enums, byte arrays and dictionaries each have a second format you can choose. [Representation](docs/TYPE-SUPPORT.md#representation) covers how to set it.

#### Text

Text is written as itself:

```csharp
new
{
    Accented = "café — naïve",
    Japanese = "注文が完了しました",
    Markup = "<b>bold</b> & \"quoted\"",
    Apostrophe = "it's fine",
    Path = @"C:\temp\file.txt",
    Tabbed = "a\tb"
}.AssertSnapshot("characters");
```

```json
{
  "Accented": "café — naïve",
  "Apostrophe": "it's fine",
  "Japanese": "注文が完了しました",
  "Markup": "<b>bold</b> & \"quoted\"",
  "Path": "C:\\temp\\file.txt",
  "Tabbed": "a\tb"
}
```

So a suite written in French, German or Japanese gives snapshots you can read. Imprint escapes only what JSON requires. [Escaped characters](docs/TYPE-SUPPORT.md#escaped-characters) lists each case.

A captured string isn't JSON at all. It becomes a `.txt` file that holds your exact bytes, final newline and all:

```text
Order created
Ready for dispatch, café ☕
```

#### Stable output

The same input always gives the same bytes. Object keys are sorted, so declaration order doesn't leak into the file:

```csharp
new { zebra = 1, apple = 2, Mango = 3, banana = 4 }.AssertSnapshot("sorted");
```

```json
{
  "Mango": 3,
  "apple": 2,
  "banana": 4,
  "zebra": 1
}
```

Sorting is ordinal, so uppercase letters sort first. The order itself doesn't matter. It never changes between runs, machines or platforms, so a diff appears only when a value changed. [What is written](docs/TYPE-SUPPORT.md#what-is-written) covers indentation, line endings and how comparison treats the file.

### What you can capture

The declared type of the expression decides what's captured. The source generator reads that type at compile time and builds a writer for it. Nothing is discovered by reflection at runtime, and that's what makes Native AOT work.

These types work by default:

- Records, classes and structs with public members
- Anonymous types and tuples
- Enums and nullables
- Collections, dictionaries and arrays of rank 1 to 3
- Common primitives, such as `string`, numbers, `Guid`, `DateTime`, `TimeSpan`, `Uri` and `JsonElement`

Since the declared type decides, a runtime subtype's own members aren't captured:

```csharp
Animal pet = new Dog { Name = "Rex", GoodBoy = true };
pet.AssertSnapshot();   // captures Animal.Name, not Dog.GoodBoy
```

To capture the derived members, declare the variable as `Dog`.

For a private type, a third-party type or a view you build on purpose, pass a custom writer. A custom writer is a `SnapshotWriter<T>` delegate that writes the value with `Utf8JsonWriter`:

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

If the generator can't build a writer for a type it sees, it reports `IMP001` at compile time with the reason, instead of failing at runtime. If a type is reached only through a generic helper, add `[assembly: SnapshotInclude<MyType>]` so the generator builds its writer.

Full details in [Type support](docs/TYPE-SUPPORT.md).

### Volatile data

A value with a timestamp in it changes on every run. Imprint has no scrubbing or redaction pipeline. To handle such a value, pass an `ISnapshotComparer`, which decides equality for one capture:

```csharp
public sealed class IgnoreTimestamps : ISnapshotComparer
{
    private static string Scrub(string text) =>
        Regex.Replace(text, @"\d{4}-\d{2}-\d{2}T[\d:.]+Z", "<timestamp>");

    public SnapshotComparisonResult Compare(
        string expected, string received, SnapshotFormat format, ResolvedSnapshotComparison options)
        => Scrub(expected) == Scrub(received)
            ? SnapshotComparisonResult.Match
            : new SnapshotComparisonResult(false, "Differs after ignoring timestamps.");
}

payload.AssertSnapshot("payload", new SnapshotOptions { Comparer = new IgnoreTimestamps() });
```

The stored file keeps the real timestamp. Only the comparison ignores it.

Often the better fix is to leave the volatile field out. Capture a smaller view of the value, or use a custom writer.

### Where files go

Each snapshot path is built from the test source and the test names:

```text
__snapshots__/
  <directory of the test source file, relative to the project>/
    <suite>/
      <test> [<case>] [<variant>]/
        <capture>.json
```

Each part comes from:

- `suite` - the containing class, or `[SnapshotSettings(Name = "...")]` on the class.
- `test` - the method, or `Name` on the method. If `naming.useFrameworkDisplayNames` is on, it's the framework display name, as [Names](docs/FRAMEWORKS.md#names) describes.
- `case` - one row of a parameterized test. It's added for you.
- `variant` - a label you set on purpose, to keep different baselines per target or configuration. You set it through `SnapshotTestOptions.Variant` or `SnapshotTestIdentity.Variant`.
- `capture` - the name you passed to `AssertSnapshot`.

Paths come from compile-time source metadata, not the working directory, so tests find their snapshots wherever they run from. Names are made portable across Windows, Linux and macOS. Names that differ only in case are rejected instead of merged.

### Parameterized tests

Each row gets its own folder, without any wrapper code or naming per row:

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

The `[...]` part appears only for parameterized tests. A normal `[Fact]` gets a plain folder, such as `__snapshots__/ParserTests/Parses/result.json`.

The label is built from the argument values, so it's readable in a diff and stable across runs. xUnit `InlineData`, NUnit `TestCase` and MSTest `DataRow` all work the same way. `CancellationToken` parameters are left out.

Imprint adds a short hash only when the label alone can't tell two rows apart:

```text
Parses [count=1, label=first]              plain, the label names every argument
Parses [value=1~9f2c1a4b7e05]              string "1" and number 1 would both read as 1
Parses [value=a, b=c~3d7e02f1ac88]         the value contains the label's own separators
Parses [first=aaaaaaaa...aaa~5b1c9e4470af] the label was too long and had to be shortened
```

If you see a hash, it carries real information. Otherwise the path stays short, which matters because you commit these files, as [Path length](#path-length) explains.

The case key follows the same declared-type rule as snapshots. So a row value the generator can't see, such as an `object`, can't give a stable key. For those rows, set `SnapshotTestOptions.Case` from a custom runner.

The case names the invocation, and the capture name names the file. If one invocation gives several outputs, name each one:

```csharp
result.AssertSnapshot("result");
headers.AssertSnapshot("headers");
```

A loop inside one invocation is still one case. If you loop over runtime data, put a stable ID in the name:

```csharp
foreach (var item in items)
{
    item.AssertSnapshot($"item-{item.Id}");
}
```

### Options

Both calls take an optional name and options:

```csharp
value.AssertSnapshot();                 // compares against the baseline
value.AssertSnapshot("name");           // compares name.json
value.AssertSnapshot("name", options);  // with options for this capture
value.UpdateSnapshot("name");           // overwrites this one baseline
```

Both also take a custom writer, as [What you can capture](#what-you-can-capture) shows.

To change one capture, pass `SnapshotOptions`:

```csharp
payload.AssertSnapshot("payload", new SnapshotOptions
{
    Format     = SnapshotFormat.Json,   // JSON value. Strings stay strings unless StringContent is Json
    Comparison = new SnapshotComparison { NumericTolerance = 0.001m },
    Comparer   = new MyComparer(),      // full control over equality
    Update     = SnapshotUpdate.All     // policy for this capture only
});
```

`Comparison` holds the equality rules, such as `NumericTolerance` and `IgnoreArrayOrder`. They decide when two snapshots count as equal. They never change what's written to disk. The stored file is always the exact captured value.

To capture JSON your app already produced, pass the JSON string with `StringContent = SnapshotStringContent.Json`:

```csharp
jsonFromYourApplication.AssertSnapshot("response", new()
{
    StringContent = SnapshotStringContent.Json
});
```

Imprint checks that the string is one JSON document and keeps its text as it is. A scalar root counts too. JSON equality ignores formatting and property order. A normal string is never checked to guess what it holds.

To change settings for a test, use `[SnapshotSettings]` on the method or class, or set `Snapshots.Current` before the first capture. A method attribute wins over its class.

[API](docs/API.md) covers every option, the configuration layers and their precedence.

### Configuration

To change defaults for a project, add a `snapshots.config.json` to the test project root. Every key has a default, so write only the ones you change:

```json
{
  "$schema": "https://raw.githubusercontent.com/TheLithiumForge/TheLithium.Imprint/main/schemas/1.0.0/snapshots.schema.json",
  "version": 1,

  "update": "missing",

  "comparison": {
    "ignoreTrailingWhitespace": true
  }
}
```

The `$schema` line gives you completion and inline docs in VS Code, Visual Studio and Rider. [Editor support](docs/API.md#editor-support) covers other ways to load the schema.

[Project file](docs/API.md#project-file) lists every key with its default and range.

### Native AOT

The runtime does no reflection. Serializers are generated at compile time and registered by module initializers. The generator and the MSBuild task run during the build and don't ship into your app.

Your test runner is the limit under AOT. Runners that discover tests by reflection generally don't survive trimming. [Native AOT](docs/FRAMEWORKS.md#native-aot) shows how to run the tests from a plain `Main`, and what CI checks.

### Custom runners

You almost certainly don't need this. The MSBuild task gives the test lifetime to any method compiled by the standard .NET SDK.

If you write a runner the task can't see, or a host that compiles test bodies itself, open the scope yourself with `Snapshots.Begin`, `Snapshots.Run` or `Snapshots.RunAsync`. [Custom runners](docs/FRAMEWORKS.md#custom-runners) shows the pattern and its rules.

## Limits

### Method shapes

The MSBuild task moves your test body into a local function, so it can act after the body returns. Some method shapes have no reliable point where the body is done, such as `async void` and iterators. These fail the build with `IMP102` instead of being wrapped wrong. [Return types](docs/FRAMEWORKS.md#return-types) lists each one and its fix.

### Old case folders

Changing a data row leaves its old folder behind. The case folder is part of the path, so editing an `[InlineData]` value creates a new folder, and Imprint stops visiting the old one. Nothing deletes it. There's no global orphan pruning, by design.

Delete stale folders yourself. `git status` shows them as untracked or unchanged strays.

### Not included

Imprint doesn't include:

- A scrubbing pipeline. Use a comparer or a smaller view, as [Volatile data](#volatile-data) shows.
- Image or binary snapshot formats.
- A CLI updater. Use `IMPRINT_UPDATE` with `dotnet test`.
- Global orphan pruning.

### Storage

The store uses locks, journals and fingerprints to coordinate local processes that cooperate. It doesn't provide transactions on a distributed file system, or protection against hostile concurrent edits.

The baseline, failure-artifact and recovery folders must be separate. None of them can contain another.

Imprint rejects existing symbolic links and reparse points at or below the project folder. It checks configured external paths from their file system root. Standard macOS system aliases are resolved for these checks.

A failure to write diagnostics never replaces the original test failure. It appears in `SnapshotReport.ArtifactError` and in the mismatch message. When a test aborts, it appears in the original exception's `Data["TheLithium.Imprint.ArtifactError"]` instead.

### Path length

Imprint limits the length of every path segment. The full path is your repository layout plus your suite, test, case and capture names.

.NET writes long paths without trouble. Git on Windows doesn't, unless `core.longpaths` is `true`. A path over about 260 characters can be written by a passing test, then skipped by `git add` without a warning. CI then sees a missing baseline that you can't reproduce locally.

Keep names short, or set `core.longpaths=true`. Imprint helps in two ways:

- The test folder defaults to the C# method name instead of the framework display name. `naming.useFrameworkDisplayNames` is `false`.
- The case hash is left out whenever the readable label already tells rows apart.

## Development

[Maintainer docs](.agents/memory/crystallized/documents/development.md) cover building, testing, CI, releases and the internal architecture.

## Roadmap

Pruning helpers for orphaned snapshot folders are an idea for now.

## License

Imprint is licensed under [MIT](LICENSE).
