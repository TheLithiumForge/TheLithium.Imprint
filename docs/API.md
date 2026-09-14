# API overview

Imprint has two ordinary test operations: capture and compare a value with `AssertSnapshot`, or request an update with `UpdateSnapshot`. Configuration supplies defaults for a project or test and overrides for one capture.

This document describes the current implementation. Examples use `using TheLithium.Imprint;`. The main package supplies generated writers and test lifetimes; the Core package exposes the runtime for explicit integrations.

- [Test APIs](#test-apis)
- [Configuration APIs](#configuration-apis)
- [Shared options and reuse](#shared-options-and-reuse)
- [Writers and comparers](#writers-and-comparers)
- [Explicit runner APIs](#explicit-runner-apis)
- [Reports and failures](#reports-and-failures)
- [Generated and build support](#generated-and-build-support)

## Test APIs

### Capture and update

| Call | Purpose |
| --- | --- |
| `value.AssertSnapshot(name: null, options: null)` | Capture now; compare against the baseline when the test body succeeds. |
| `value.UpdateSnapshot(name: null, options: null)` | Capture now; request creation or replacement of this entry when the test succeeds. |
| `value.AssertSnapshot(writer, name: null, options: null)` | Capture with an explicit `SnapshotWriter<T>`. |
| `value.UpdateSnapshot(writer, name: null, options: null)` | Request an update using an explicit writer. |

These methods return `void`. `T` is inferred from the value's declared type. The compiler supplies the optional `expression` argument for name inference; ordinary calls omit it. The writer overloads take the writer first, followed by the name and options.

```csharp
[Fact]
public void CreatesOrder()
{
    var order = new { Id = "order-123", Total = 42.50m };

    order.AssertSnapshot("order");
    "Ready for dispatch".AssertSnapshot("message");
}
```

Capture is eager: changing `order` afterwards does not change its captured value. Comparison and approval happen after the method body, including its awaited work and `finally` blocks, succeeds. A later failure abandons all proposed updates. Framework teardown after the method returns is outside that boundary. Catching a capture failure does not make the test eligible to approve snapshots.

Names identify files within one test invocation. Supply names without extensions; Imprint chooses `.json` or `.txt`. Without an explicit name, the default is a variable/member name, then `snapshot-1`, `snapshot-2`, and so on. Explicit names must be unique within the test, ignoring case.

### Common situations

| Situation | API or approach |
| --- | --- |
| Capture an object, collection or primitive | `value.AssertSnapshot("value")`. The declared type determines the generated writer. |
| Capture ordinary text | `text.AssertSnapshot("text")`. A root string uses text by default, even if it looks like JSON. |
| Capture a string as a JSON string value | `text.AssertSnapshot("text", new() { Format = SnapshotFormat.Json })`. |
| Capture JSON already produced by the application | `json.AssertSnapshot("response", new() { StringContent = SnapshotStringContent.Json })`. |
| Change formatting or comparison for one capture | Pass a `SnapshotOptions` object. |
| Omit a volatile field or format one member specially | Project the value before capture, or pass an explicit writer. |
| Approve one entry | `value.UpdateSnapshot("value")`, subject to the run policy. |
| Approve the current test, including removal of unused entries | Set `Snapshots.Current.Update = SnapshotUpdate.All` before capture. |
| Distinguish parameterized test rows | The main package derives a case key from the actual arguments. No additional assertion API is needed. |
| Capture several outputs from one row or a loop | Give each capture a distinct, stable name. |

Changing a parameterized row leaves its old test folder behind; Imprint does not perform project-wide orphan cleanup. See [type support](TYPE-SUPPORT.md) for supported shapes and [framework integration](FRAMEWORKS.md) for lifetime details.

## Configuration APIs

### Layers at a glance

| Layer | Surface | Applies to |
| --- | --- | --- |
| Built-in | Defaults when settings are omitted | Every test. |
| Project | `snapshots.config.json` | Tests resolving configuration from that project. |
| Class or method | `[SnapshotSettings]` | Suite/test naming and update policy only. |
| Test initialization | `SnapshotTestOptions` passed to an explicit runner | One test scope, including identity, storage, limits and preferences. |
| Active test | Setters on `Snapshots.Current` | Defaults for all captures in the active test; set before its first capture. |
| Assertion | `SnapshotOptions` passed to a capture | That capture and its nested values. |
| Run environment | `IMPRINT_UPDATE`, `CI`, `IMPRINT_PROJECT_ROOT` | Update-policy enforcement or checkout relocation for the run. |

The layers do not all expose the same settings. In particular, attributes only have `Name` and `Update`; `Format` and custom `Comparer` belong to individual assertions. A shared C# options variable has no scope by itself: it takes effect where it is passed or assigned.

For representation and comparison, specified fields override earlier values in this order:

```text
built-in defaults → project → test initialization → active-test changes → assertion
```

`null` fields in C# option records mean inherit. Explicit `false`, zero, and an enum's zero-valued member are real overrides. `StringContent` follows the same scope order as one setting. An assertion override leaves the test defaults and subsequent assertions unchanged. Update policy has its own precedence, described below.

### Built-in defaults

| Concern | Default |
| --- | --- |
| Update | `Missing`: create missing entries after success; fail on changed or unused entries. |
| Output format | `Auto`: text for a non-null root string without an explicit writer; JSON otherwise. |
| String interpretation | `Value`: do not guess whether a string contains JSON. |
| Representation | Enum names with numeric fallback; byte arrays as numbers; string-key dictionaries as objects. |
| Comparison | Exact numbers, ordered arrays, case-sensitive strings, ignored text line endings, significant trailing whitespace. |
| Unordered matching limit | 256 elements per array when `IgnoreArrayOrder` is enabled. |
| Snapshot root | `<project>/__snapshots__`; source-relative directories, suite and test names are appended. |
| Failure artifacts | `<build artifacts>/imprint/failures`, falling back to `<project>/artifacts/imprint/failures`. |
| Naming | `NameThenOrder`; framework display names are off. |
| Empty test | Fails if the active scope captures nothing. |
| Capture limits | Depth 64, 100,000 values, 4 MiB of UTF-8 output per snapshot. |
| Lock timeout | 10 seconds. |

There are also fixed limits of 1,024 captures per test and a combined byte budget of the larger of 128 MiB or the configured per-snapshot byte limit.

### Project: `snapshots.config.json`

Imprint reads one file at the resolved project root. It does not walk directories and merge multiple configuration files. An absent default file uses built-in defaults. An explicit `SnapshotTestOptions.ConfigurationFile` selects a replacement file and must exist.

```json
{
  "version": 1,
  "update": "missing",
  "representation": {
    "enums": "number"
  },
  "comparison": {
    "ignoreLineEndings": true
  }
}
```

| Key | Values / purpose |
| --- | --- |
| `$schema` | Optional editor schema reference. See the [versioned schema](../schemas/1.0.0/snapshots.schema.json). |
| `version` | `1`; defaults to 1 when omitted. |
| `update` | `verify`, `missing`, `all`. |
| `allowEmptyTests` | Boolean; permits a test scope with no captures. |
| `stringContent` | `value` or `json`, for root strings. |
| `files.snapshotFolderName` | Portable single folder name; defaults to `__snapshots__`. |
| `files.snapshotRootPath` | Optional project-relative or absolute replacement snapshot root. |
| `files.failureArtifactPath` | Optional nonempty project-relative or absolute failure-artifact root. |
| `naming.unnamedCaptures` | `name-then-order`, `order`, or `explicit-only`. |
| `naming.useFrameworkDisplayNames` | Boolean; opt into generated framework display metadata for the test folder. |
| `representation.enums` | `name-or-number` or `number`. |
| `representation.byteArrays` | `numbers` or `base64`. |
| `representation.dictionaries` | `automatic` or `entries`. |
| `comparison.numericTolerance` | Nonnegative decimal; default `0`. |
| `comparison.ignoreArrayOrder` | Boolean; default `false`. |
| `comparison.ignoreStringCase` | Boolean; default `false`. |
| `comparison.ignoreLineEndings` | Boolean; default `true`. |
| `comparison.ignoreTrailingWhitespace` | Boolean; default `false`. |
| `comparison.maxUnorderedArrayLength` | Integer from 1 to 1,024; default 256. |
| `limits.maxNestingDepth` | Integer from 1 to 256. |
| `limits.maxValuesPerSnapshot` | Integer from 1 to 10,000,000. |
| `limits.maxBytesPerSnapshot` | Integer from 1 to 268,435,456 (256 MiB). |
| `limits.lockTimeoutSeconds` | Integer from 1 to 300. |

Omitted values inherit defaults. JSON settings use the documented types rather than the nullable C# patch convention: explicit nulls are rejected except for `files.snapshotRootPath`. Unknown keys, duplicate keys, unsupported enum values and invalid limits fail configuration, even if a narrower setting would replace them. Snapshot, artifact and recovery directories must be separate and may not contain one another.

### Class and method: `[SnapshotSettings]`

```csharp
[SnapshotSettings(Name = "Orders", Update = SnapshotUpdate.Missing)]
public sealed class OrderTests
{
    [Fact]
    [SnapshotSettings(Name = "NewOrder", Update = SnapshotUpdate.Verify)]
    public void CreatesOrder()
    {
        new { Id = 123 }.AssertSnapshot("order");
    }
}
```

| Property | On a class | On a method |
| --- | --- | --- |
| `Name` | Suite folder name. | Test folder name. |
| `Update` | Default update policy for its tests. | Overrides the class policy when not `Inherit`. |

These attributes do not configure representation, comparison or capture limits. A direct method attribute can also establish the generated lifetime when a capture is reached indirectly, such as through a delegate. A class attribute supplies settings to discovered tests; it does not wrap every method in the class.

### Active test: `Snapshots.Current`

| Writable property | Behavior |
| --- | --- |
| `Update` | Set the whole-test update policy. `Inherit` restores the policy selected when the scope began. |
| `Representation` | Merge specified enum, byte-array and dictionary preferences into the current defaults. |
| `Comparison` | Merge specified comparison fields into the current defaults. |
| `StringContent` | Set root-string interpretation for the test. |

Set these before the first capture. Later assignments fail. Representation and comparison getters return the effective values, including inherited defaults. Repeated partial assignments preserve fields that are not specified. Settings belong to the current asynchronous test scope; concurrent tests have their own scopes.

`Snapshots.UpdateCurrentTest(update = SnapshotUpdate.All)` is a convenience for assigning `Snapshots.Current.Update`.

For preferences shared by a test class, define a `static readonly` options record and assign it at the beginning of each test, directly or through a helper. Merely declaring the field does not register suite defaults.

### Assertion: `SnapshotOptions`

| Property | Default / scope |
| --- | --- |
| `Update` | `Inherit`; an explicit policy affects this entry only. `UpdateSnapshot` always requests `All` for the entry. |
| `Format` | `Auto`; selects JSON or text for this capture. For strings, `Json` writes a quoted JSON string value; use `StringContent = Json` to capture a parsed JSON document. |
| `StringContent` | `null` inherits the test's root-string interpretation. |
| `Representation` | `null` inherits test preferences; a supplied record replaces only specified categories. |
| `Comparison` | `null` inherits test comparison; a supplied record replaces only specified fields. |
| `Comparer` | `null` uses `DefaultSnapshotComparer`; otherwise use the supplied `ISnapshotComparer`. |

`Format = Json` treats an ordinary string as a JSON string value. `StringContent = Json` instead accepts an already serialized JSON document, validates it and preserves the supplied text. It accepts JSON objects, arrays and scalar roots, requires a non-null string, and is incompatible with `Format = Text` or an explicit writer. Text format requires a non-null string; explicitly selecting it writes that string directly and bypasses any supplied JSON writer. Null values normally capture as JSON `null`.

Representation preferences affect generated and built-in serialization. They do not reinterpret text, rewrite an existing JSON document, or force custom writers to change their output.

### Explicit test initialization: `SnapshotTestOptions`

Pass this record to `Snapshots.Run`, `RunAsync` or `Begin`. It configures a test scope when integrating an explicit runner; ordinary tests generally use project settings, attributes and `Snapshots.Current`.

| Properties | Purpose |
| --- | --- |
| `Update` | Initial test policy; `Inherit` uses generated method/class policy, then project settings. |
| `Representation`, `Comparison`, `StringContent` | Initial test preferences over project settings. |
| `Name`, `Suite` | Explicit test/suite names over the resolved identity. With generated identity, an enabled framework display name is selected during settings resolution; an explicit `Identity` bypasses that display-name selection. |
| `Case`, `Variant` | Case and target/configuration discriminators over the resolved identity. |
| `Identity` | A `SnapshotTestIdentity` for a custom runner or relocated executable; bypasses generated source identity. |
| `RootDirectory` | Replacement snapshot root; source directory, suite and test are still appended. |
| `ArtifactDirectory` | Replacement failure-artifact root. |
| `ConfigurationFile` | Select a required project configuration file; relative paths are project-relative. |
| `Naming` | `NameThenOrder`, `Order`, or `ExplicitOnly` for capture filenames. |
| `AllowEmpty` | Allow no captures. Combined with whole-test `All`, this can remove the test's existing entries. |
| `MaxNestingDepth`, `MaxValuesPerSnapshot`, `MaxBytesPerSnapshot` | Override the three project capture limits, using the same ranges. |
| `CancellationToken` | Cancel Imprint-owned capture, comparison and lock-wait work. Custom callbacks are not interrupted or passed the token. An active storage transaction completes or rolls back. |

Nullable properties inherit when omitted. There is no per-test `Format`, custom `Comparer`, or lock-timeout property: format and custom equality belong to captures; lock timeout belongs to project configuration.

### Run environment and update precedence

| Variable | Meaning |
| --- | --- |
| `IMPRINT_UPDATE` | `verify`, `missing`, or `all` for the run; takes precedence over every test and assertion policy. |
| `CI` | When true/1 and no `IMPRINT_UPDATE` is supplied, enforces run-wide `Verify`. False/0 or unset/blank does not. |
| `IMPRINT_PROJECT_ROOT` | Existing absolute project directory for checkout relocation. Overrides the root carried by generated or explicit identity. |

These inputs are read when a scope begins. There is no separate Imprint CLI or automatic `.runsettings` integration.

For each entry, the first applicable update policy wins:

1. Explicit `IMPRINT_UPDATE`.
2. `Verify` when CI is enabled.
3. Assertion `Update`, including the `All` requested by `UpdateSnapshot`.
4. The active test policy: an explicit scope assignment, otherwise `SnapshotTestOptions.Update`, otherwise generated method/class policy, otherwise the project policy.
5. Built-in `Missing`.

| Effective policy | Missing entry | Different entry | Unused entry |
| --- | --- | --- | --- |
| `Verify` | Fail | Fail | Fail |
| `Missing` | Create after success | Fail | Fail |
| `All` | Create after success | Replace after success | Remove only when `All` applies to the whole test |

Unused entries are evaluated against the whole-test policy. A single `UpdateSnapshot` does not approve pruning. A narrower assertion can override a project or test `Verify`, but cannot override a run-wide `Verify`. Run-wide verification also refuses interrupted baseline recovery; locks and failure diagnostics can still write. All proposed baseline changes require successful completion and an authorized result for the entire test.

## Shared options and reuse

### Representation: `SnapshotRepresentationOptions`

| Property / enum type | Choices | Built-in default |
| --- | --- | --- |
| `Enums` / `SnapshotEnumRepresentation` | `NameOrNumber`: exact declared name, otherwise underlying number. `Number`: always the underlying number. | `NameOrNumber` |
| `ByteArrays` / `SnapshotByteArrayRepresentation` | `Numbers`: JSON numeric array. `Base64`: JSON base64 string. | `Numbers` |
| `Dictionaries` / `SnapshotDictionaryRepresentation` | `Automatic`: string keys as object properties, other keys as entry arrays. `Entries`: arrays of `{ "Key": ..., "Value": ... }`. | `Automatic` |

Each preference applies throughout a capture, including nested values. Enum flags combinations are not expanded into names unless the combined value has an exact declared name. Both byte-array choices are JSON representations, not binary snapshot files. Representation settings do not change parameterized case identity.

### Comparison: `SnapshotComparison`

| Property | Meaning | Built-in default |
| --- | --- | --- |
| `NumericTolerance` | Maximum absolute decimal difference accepted between JSON numbers; must be nonnegative. | `0` |
| `IgnoreArrayOrder` | Match JSON array elements without order; duplicates still count. | `false` |
| `IgnoreStringCase` | Ordinal case-insensitive string comparison; JSON property names remain case-sensitive. | `false` |
| `IgnoreLineEndings` | Treat CRLF and LF as equal in text snapshots. | `true` |
| `IgnoreTrailingWhitespace` | Ignore trailing spaces and tabs on each text line. | `false` |
| `MaxUnorderedArrayLength` | Bound unordered matching to arrays of this length; range 1–1,024. | `256` |

Comparison settings decide equality; they do not transform the stored output. JSON comparison is structural and ignores formatting and object-property order. See [type support](TYPE-SUPPORT.md) for numeric boundaries.

### Reuse defaults, then override one capture

`SnapshotRepresentationOptions`, `SnapshotComparison`, `SnapshotOptions` and `SnapshotTestOptions` are records with initialization-only settings. Reuse them directly or derive a copy with C# `with`; no builder or freeze operation is needed.

```csharp
private static readonly SnapshotRepresentationOptions Defaults = new()
{
    Enums = SnapshotEnumRepresentation.Number,
    Dictionaries = SnapshotDictionaryRepresentation.Entries
};

[Fact]
public void CapturesOrderAndReceipt()
{
    Snapshots.Current.Representation = Defaults;

    var order = new { Id = "order-123", Status = OrderStatus.Shipped };
    var receipt = new { order.Id, order.Status };

    order.AssertSnapshot("order");
    receipt.AssertSnapshot("receipt", new()
    {
        Representation = Defaults with
        {
            Enums = SnapshotEnumRepresentation.NameOrNumber
        }
    });
    order.AssertSnapshot("order-again");
}

public enum OrderStatus { Pending, Shipped }
```

The order captures use status `1`; the receipt uses `"Shipped"`. All retain entry-array dictionary preferences. `Defaults` and the test defaults stay unchanged. If defaults have already been installed on the test, the assertion may instead supply only `new() { Enums = SnapshotEnumRepresentation.NameOrNumber }` and inherit everything else.

For defaults used only by selected captures, pass the shared record through `SnapshotOptions.Representation` directly instead of assigning it to the active test.

## Writers and comparers

| API | Role |
| --- | --- |
| `SnapshotWriter<T>` | Delegate receiving `Utf8JsonWriter`, the non-null value, and `SnapshotWriteContext`. Write exactly one JSON value and leave the writer open. |
| Writer overloads of `AssertSnapshot` / `UpdateSnapshot` | Customize serialization for one capture. Null bypasses the delegate and writes JSON null. |
| `SnapshotWriters.Register<T>(writer)` | Replace the writer for a declared type globally. Register during startup before concurrent tests. |
| `SnapshotWriters.Write(writer, value, context)` | Delegate nested writing to an existing generated, built-in or registered writer while retaining context. |
| `[assembly: SnapshotInclude<MyType>]` | Request generation for a concrete type reached only through a generic helper. |
| `SnapshotWriteContext.Representation` | Read `ResolvedSnapshotRepresentation`: concrete, read-only enum, byte-array and dictionary preferences. |
| `SnapshotWriteContext.Path` | Current JSON-style path, initially `$`. |
| `SnapshotWriteContext.At(member)` / `At(index)` | Push a member/index path; disposing the returned frame restores the previous path. |
| `ISnapshotComparer.Compare(expected, received, format, options)` | Custom equality for already captured text. Receives `ResolvedSnapshotComparison`, whose fields are non-nullable, and a resolved format. |
| `SnapshotComparisonResult` | `Equal` and optional `Difference`; `Match` is a reusable successful result. |
| `DefaultSnapshotComparer.Instance` | The built-in comparer, also usable when composing custom equality. Pass the received `ResolvedSnapshotComparison`, or construct one with concrete rules; omitted fields use built-in defaults. |

Custom writers own their output; they can consult `context.Representation` when useful. Custom comparers must be deterministic and safe for concurrent use. A custom comparer cannot change the saved representation. Explicit writer registration still selects by declared type; it is an extension for serialization behavior rather than an additional configuration layer.

## Explicit runner APIs

| API | Result / lifecycle |
| --- | --- |
| `Snapshots.Run(Action body, options: null)` | Runs a synchronous body and completes the scope; returns `SnapshotReport`. |
| `Snapshots.RunAsync(Func<Task> body, options: null)` | Awaits the body and completes; returns `Task<SnapshotReport>`. |
| `Snapshots.Begin(options: null)` | Returns a `SnapshotScope` for manual lifetime management. |
| `Snapshots.Current` | Returns the active scope in this asynchronous context; throws if none exists. |
| `scope.Complete()` | Compares all captures and commits authorized changes; returns `SnapshotReport` or throws. |
| `scope.Abort(error: null)` | Abandons approval and attempts to preserve failure diagnostics. |
| `scope.Dispose()` | Abandons an incomplete scope and restores its parent; never approves. |

`Run`, `RunAsync` and `Begin` also accept compiler-supplied `sourceFile` and `sourceLine` arguments. Ordinary callers omit them. `Run(Func<Task>)` is an obsolete-error overload that deliberately rejects asynchronous callbacks; use `RunAsync`.

`SnapshotTestIdentity` requires `ProjectDirectory`, `SourceFile`, `Suite` and `Test`. It optionally accepts `Case`, `Variant` and `LogicalId`. Core-only integrations need an explicit identity and built-in or explicit writers. The main package usually supplies both identity and writers during compilation.

Manual scopes must call `Complete` after all body and teardown work that should influence approval. On failure, call `Abort(error)` and rethrow the original exception. Prefer `Run`/`RunAsync` when a callback can express that boundary. See the [runner example](../README.md#custom-runners).

Read-only scope properties are `BaselineDirectory`, `BaselineFingerprint`, `ArtifactDirectory`, and `EffectiveUpdate`. The last includes run-policy enforcement; `Update` is the test's requested policy. Separate explicit nested scopes resolve their own settings and restore the parent when disposed; sharing an ambient scope in a helper does not open a new test.

## Reports and failures

| Type | Information |
| --- | --- |
| `SnapshotReport` | `Test`, `Success`, `Entries`, optional `ArtifactDirectory` and `ArtifactError`. Returned by successful explicit completion and attached to a mismatch exception. |
| `SnapshotEntryResult` | `Name`, `FileName`, `Status`, optional `Difference`. |
| `SnapshotStatus` | `Matched`, `Missing`, `Changed`, `Unused`, `Created`, `Updated`, `Removed`. |
| `SnapshotException` | Base exception for Imprint failures. |
| `SnapshotMismatchException` | The completed comparison set failed; inspect `Report`. |
| `SnapshotConfigurationException` | Invalid options, missing lifetime/identity, or invalid lifecycle use. |
| `SnapshotCaptureException` | A value could not be captured, exceeded a budget, or an earlier capture failure prevents completion. |
| `SnapshotConflictException` | Conflicting baseline ownership, concurrent changes or ambiguous stored state. |

Standard argument and cancellation exceptions can also propagate. Diagnostic write failures preserve the primary error. An aborted test may attach diagnostic information to the original exception's `Data` under `TheLithium.Imprint.Artifacts` or `TheLithium.Imprint.ArtifactError`.

## Generated and build support

These members are public so generated code in a consuming assembly can call them. They belong to the implementation support surface, not ordinary test setup.

| Surface | Purpose |
| --- | --- |
| `Generation.TestExecution.Run` / `Run<T>` | Generated wrappers for synchronous test return shapes. |
| `Generation.TestExecution.RunAsync` / `RunAsync<T>` | Generated wrappers for `Task` / `Task<T>`. |
| `Generation.TestExecution.RunValueTask` / `RunValueTask<T>` | Generated wrappers for `ValueTask` / `ValueTask<T>`. |
| `Generation.SnapshotMetadata.Register` and `Generation.Models.SnapshotDescriptor` | Register generated source identity, names, policies and build paths. |
| `Generation.SnapshotCases.Create<T>` | Build stable parameterized case labels from argument values; also usable by explicit integrations. |
| `Generation.Shapes` | Type-inference helpers for anonymous arrays, collections, dictionaries and pairs. Their sample values are not inspected. |
| `SnapshotWriters.TryRegister<T>(writer)` and `TryRegister<T>(shape, writer)` | Register generated writers only if a writer is not already installed. |

The main package imports its build integration automatically. Advanced build controls include `ImprintInstrument="false"` metadata on a `Compile` item to exclude that source from rewriting, and `ImprintBuildAssembly` to locate the build-task assembly. `MSBuildProjectDirectory`, `MSBuildProjectName` and `ArtifactsPath` supply generated metadata. These build settings are separate from runtime configuration.

The package supports ordinary `void`, value-returning, `Task` and `ValueTask` test methods, including generic task/value-task results. Unsupported serialization is diagnosed with `IMP001`; unsupported lifetime shapes, such as async-void or iterator methods, use `IMP102`. See [framework integration](FRAMEWORKS.md) for the boundary and supported alternatives.

Implementation sources: [capture methods](../src/TheLithium.Imprint.Core/SnapshotExtensions.cs), [scope behavior](../src/TheLithium.Imprint.Core/Execution/SnapshotScope.cs), [settings resolution](../src/TheLithium.Imprint.Core/Configuration/Settings.cs), [project reader](../src/TheLithium.Imprint.Core/Configuration/ProjectConfigurationReader.cs), and [project schema](../schemas/1.0.0/snapshots.schema.json).
