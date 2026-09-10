# TheLithium.Imprint

Snapshot testing for **.NET 10**, with typed source generation, Native AOT support, readable diffs, and one value per file. Keep your existing test framework and assertions.

```csharp
using TheLithium.Imprint;
using Xunit;

public sealed class OrderTests
{
    [Fact(DisplayName = "An order is ready for dispatch")]
    public Task CreatesOrder() => Snapshots.RunAsync(async () =>
    {
        var order = await CreateOrderAsync();
        order.Snapshot();
        order.Items.Snapshot("items");
        "Ready for dispatch".Snapshot("output");
        Assert.NotEmpty(order.Items);
    });
}
```

`CreateOrderAsync` is your application code. The files sit next to `OrderTests.cs`:

```text
__snapshots__/
  OrderTests/
    An order is ready for dispatch/
      order.json
      items.json
      output.snap
```

JSON files contain the captured value itself. Strings use `.snap`; `.txt` and explicit JSON text are also supported. Capture is immediate: taking `state.Snapshot("before")`, changing `state`, then taking `state.Snapshot("after")` records both states.

## Install

Local NuGet packages are generated in `artifacts/packages`. Add that directory as a package source in the consuming solution, then reference:

```xml
<PackageReference Include="TheLithium.Imprint" Version="0.1.0-preview.1" />
```

The main package supplies `TheLithium.Imprint.Core`, the compiler-only source generator, and automatic build metadata. The core has no runtime NuGet dependencies. Use the core alone when providing explicit identities and writers. These packages have not been published to nuget.org.

## Test boundaries and names

Use `Snapshots.Run(Action)` for synchronous tests and `Snapshots.RunAsync(Func<Task>)` for asynchronous tests. Put assertions and any cleanup that must succeed before approval inside the callback. A callback failure or cancellation prevents its updates. Await concurrent work before returning. A capture error prevents approval even if the callback catches it.

The boundary works with any runner that treats an uncaught .NET exception as a failure. This repository exercises xUnit, NUnit, and MSTest. No runner-specific lifecycle adapter is required.

Suite and test names come from compile-time source metadata. Test-name precedence is explicit `SnapshotTestOptions.Name`, method `[SnapshotSettings(Name = "...")]`, recognized framework display name/description, then the method name. Constant metadata on derived test attributes is supported. The generator also recognizes older MSTest constructors whose parameter is named `displayName`; current MSTest uses `[TestMethod(DisplayName = "...")]`.

`SnapshotTestOptions.Suite` overrides the suite. A class `[SnapshotSettings(Name = "...")]` names its suite. Nested classes use dotted names. File paths are escaped, bounded, and checked for collisions across operating systems.

```csharp
result.Snapshot();                  // result.json
result.Items.Snapshot();            // Items.json
state.Snapshot();                   // state.json
state.Snapshot();                   // state-2.json
BuildResult().Snapshot();           // snapshot-1.json
result.Snapshot("installation");   // installation.json
```

Use unique explicit names for concurrent captures. Explicit duplicates, including case-only duplicates, fail.

Parameterized tests need a stable case key. The generator cannot infer values or dynamic display names produced by a runner at execution time:

```csharp
public void Parses(string input, string caseName) => Snapshots.Run(() =>
{
    Parse(input).Snapshot("result");
}, new() { Case = caseName });
```

The folder becomes `Parses [caseName]`. An optional `Variant = "linux"` adds another explicit discriminator. Tests do not silently create separate baselines for each OS.

## Approve snapshots

Verification is the default. The smallest update is an entry option:

```csharp
result.Snapshot("result", new() { Update = SnapshotUpdate.All });
```

To update the whole test, call `Snapshots.UpdateCurrentTest()` **before the first capture**, pass `new() { Update = SnapshotUpdate.All }` to the boundary, or put `[SnapshotSettings(Update = SnapshotUpdate.All)]` on the method/class. Return to verification when finished.

For a whole run, install the optional local tool:

```text
dotnet tool install --tool-path artifacts/tools TheLithium.Imprint.Tool --version 0.1.0-preview.1 --add-source artifacts/packages
```

The installed `imprint` command wraps your normal test command:

```text
imprint run --update missing -- dotnet test
imprint run --update all --test "*OrderTests*" -- dotnet test
imprint run --read-only -- dotnet test
```

With a local installation, use its path, for example `./artifacts/tools/imprint`. `--test` selects which tests may update; pass the runner's selection options after `dotnet test`. Nonmatching tests verify. Arguments are passed directly to the runner, and its exit code is preserved.

The same policies are available through environment variables: `IMPRINT_UPDATE=verify|missing|all`, `IMPRINT_TEST`, and `IMPRINT_READ_ONLY=true`. `CI=true` enforces verification unless `IMPRINT_ALLOW_CI_UPDATE=true` is explicitly set along with an update policy. Read-only always wins, including over attributes and entry options.

| Policy | Missing baseline | Changed baseline | Uncaptured baseline |
| --- | --- | --- | --- |
| `Verify` | Fail | Fail | Fail |
| `Missing` | Create | Fail | Fail |
| `All` | Create | Replace | Remove only under a whole-test `All` policy |

All differences in a test must be authorized before any are written. An entry's `All` cannot approve another entry. Empty tests fail unless `AllowEmpty = true`. Disposal alone never approves.

## Understand a failure

Errors aggregate mismatched files and include contextual expected/received diffs. JSON differences identify the member path, for example `$["Customer"]["Name"]: expected "Alice", received "Bob"`, followed by:

```diff
--- expected
+++ received
@@ -1,5 +1,5 @@
  {
    "Customer": {
-     "Name": "Alice"
+     "Name": "Bob"
    }
  }
```

Long lines and large changes are abbreviated. Full `.received.*`, `.expected.*`, and `run.json` artifacts are written beneath `TestResults/TheLithium.Imprint`; the failure includes that execution's directory. `SnapshotMismatchException.Report` exposes structured statuses.

## Comparison and serialization

JSON compares structurally, with deterministic property ordering and exact numeric equality. Arrays are ordered by default. Unordered comparison preserves duplicate counts and handles overlapping numeric tolerances.

```csharp
result.Snapshot("result", new()
{
    Comparison = new() { NumericTolerance = 0.001m, IgnoreArrayOrder = true }
});

jsonText.Snapshot("payload", new() { Format = SnapshotFormat.Json });
output.Snapshot("output", new() { Format = SnapshotFormat.Text }); // .txt
```

Text comparison ignores line-ending differences by default. Optional case and trailing-whitespace rules affect equality, not saved content. Comparison options replace inherited comparison options as a whole. `ISnapshotComparer` supports custom equality.

The **static declared type is the serialization contract**. DTOs, records, anonymous projections, tuples, collections, dictionaries, nullable values, enums, and common scalar types are supported. Runtime-derived members of an `object` or base-typed value are not discovered. No reflective serialization or `ToString` fallback exists. See [the type contract](docs/TYPE-SUPPORT.md).

Explicit writers handle private or specialized types:

```csharp
value.Snapshot(static (json, item, context) =>
{
    json.WriteStartObject();
    json.WriteNumber("Code", item.Code);
    json.WriteString("Message", item.Message);
    json.WriteEndObject();
}, "custom");
```

Use `SnapshotWriters.Write` for nested values and `context.At("Member")` for error paths. `[assembly: SnapshotInclude<MyResult>]` roots a writer for a type used only through a generic helper. Configure global `SnapshotWriters.Register<T>` overrides before tests run concurrently. Project values before capture when you need to remove volatile fields.

## Configuration and custom runners

An optional `snapshots.config.json` belongs in the test-project root:

```json
{
  "version": 1,
  "update": "verify",
  "directoryName": "__snapshots__",
  "textExtension": "snap",
  "comparison": { "ignoreLineEndings": true }
}
```

See [the schema](snapshots.schema.json) and [full example](examples/snapshots.config.json). Unknown or duplicate settings fail clearly. Policy precedence is enforced read-only, run override, entry options, test options, method settings, class settings, project configuration, then default verification.

`Snapshots.Begin(options)` exposes a scope for adapters: call `Complete()` after success, `Abort(error)` on failure, and `Dispose()` for cleanup. The runner remains responsible for later teardown. Use an explicit `SnapshotTestIdentity` when a shared helper opens the boundary or the binary runs without its original source checkout. `IMPRINT_PROJECT_ROOT` remaps a relocated checkout; `IMPRINT_CONFIG` selects another config. Paths never depend on the working directory. See [framework integration](docs/FRAMEWORKS.md).

Commit baseline files **and** `__snapshots__/.imprint/owners/*.owner`. Ownership records prevent distinct tests from silently sharing readable folder names. Updates use local file locks, optimistic conflict checks, and a recovery journal. Use separate checkouts for remote workers; a shared network filesystem is not a distributed transaction store. See [storage and design](docs/DESIGN.md).

## Build and verify

The solution pins the .NET 10 SDK and needs no custom build scripts:

```text
dotnet build -c Release
dotnet test -c Release
dotnet pack -c Release
dotnet test -c Release -p:UsePackageReferences=true
dotnet run --project tests/TheLithium.Imprint.Specifications -c Release
```

The package-consumer check uses the packed runtime and bundled generator. When rebuilding the same package version, use a fresh `RestorePackagesPath` to avoid cached package content.

Native AOT verification on Windows x64:

```text
dotnet publish tests/TheLithium.Imprint.Specifications -c Release -r win-x64 -p:PublishAot=true -o artifacts/native/win-x64
./artifacts/native/win-x64/TheLithium.Imprint.Specifications.exe --expect-aot
```

Use the matching RID and executable name on Linux/macOS. Install the platform's [.NET Native AOT prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot). The AOT harness uses explicit test registration and requires dynamic code to be disabled. An external runner and the application being tested must independently support AOT for their entire executable to publish natively.

| Project | Purpose |
| --- | --- |
| `TheLithium.Imprint.Core` | Runtime, storage, serialization, comparison, scopes |
| `TheLithium.Imprint` | Main NuGet package with core dependency and bundled generator |
| `TheLithium.Imprint.Generator` | Compile-time writers, identity, and diagnostics |
| `TheLithium.Imprint.Tool` | Optional `imprint` .NET tool |
| `TheLithium.Imprint.Tests` | Runtime specifications, xUnit usage, and tool process tests |
| `TheLithium.Imprint.Generator.Tests` | Compile generated code and test diagnostics/metadata |
| `TheLithium.Imprint.NUnit.Tests` / `TheLithium.Imprint.MSTest.Tests` | Real framework integration tests |
| `TheLithium.Imprint.Specifications` | Managed and Native AOT executable specifications |

See [validation](docs/VALIDATION.md) for measured results and remaining platform limits. Distribution licensing remains an owner decision; see [licensing](LICENSING.md).
