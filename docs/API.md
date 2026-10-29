# API

The Imprint API is two calls that capture a value, plus the options that change how Imprint writes, compares and approves it.

You call `AssertSnapshot` to capture a value and compare it with the baseline, or `UpdateSnapshot` to ask for that baseline to be replaced. Configuration gives defaults for a project or a test, and overrides for one capture. The main package gives you generated writers and test lifetimes. The `TheLithium.Imprint.Core` package exposes the runtime for custom runners. Examples use `using TheLithium.Imprint;`.

## Capture calls

### Calls

Each call captures the value right away and acts on it when the test succeeds:

| Call | What it does |
| --- | --- |
| `value.AssertSnapshot(name: null, options: null)` | Captures now. Compares with the baseline when the test body succeeds. |
| `value.UpdateSnapshot(name: null, options: null)` | Captures now. Asks to create or replace this entry when the test succeeds. |
| `value.AssertSnapshot(writer, name: null, options: null)` | Captures with a custom `SnapshotWriter<T>`. |
| `value.UpdateSnapshot(writer, name: null, options: null)` | Asks for an update, using a custom writer. |

These methods return `void`. `T` is inferred from the value's declared type. The compiler fills in the optional `expression` argument for name inference, so normal calls leave it out. The writer overloads take the writer first, then the name and options.

```csharp
[Fact]
public void CreatesOrder()
{
    var order = new { Id = "order-123", Total = 42.50m };

    order.AssertSnapshot("order");                  // writes order.json
    "Ready for dispatch".AssertSnapshot("message"); // writes message.txt
}
```

Capture is eager. Changing `order` afterwards doesn't change its captured value.

Comparison and approval happen after the method body succeeds, including its awaited work and `finally` blocks. A later failure drops all proposed updates. Framework teardown after the method returns doesn't count, as [Approval](../README.md#approval) shows. If you catch a capture failure, the test still can't approve snapshots.

Names identify files within one test invocation. Pass names without an extension, and Imprint picks `.json` or `.txt`. Without a name, Imprint uses the variable or member name, then `snapshot-1`, `snapshot-2` and so on. Names you pass must be unique within the test, ignoring case.

### Common tasks

Each task has one call or approach:

| Task | Call or approach |
| --- | --- |
| Capture an object, collection or primitive | `value.AssertSnapshot("value")`. The declared type picks the generated writer. |
| Capture normal text | `text.AssertSnapshot("text")`. A root string is stored as text by default, even if it looks like JSON. |
| Capture a string as a JSON string value | `text.AssertSnapshot("text", new() { Format = SnapshotFormat.Json })`. |
| Capture JSON your app already produced | `json.AssertSnapshot("response", new() { StringContent = SnapshotStringContent.Json })`. |
| Change formatting or comparison for one capture | Pass a `SnapshotOptions` object. |
| Leave out a volatile field, or format one member differently | Capture a smaller view of the value, or pass a custom writer. |
| Approve one entry | `value.UpdateSnapshot("value")`, subject to the run policy. |
| Approve the current test, including removal of unused entries | Set `Snapshots.Current.Update = SnapshotUpdate.All` before the first capture. |
| Tell parameterized test rows apart | The main package builds a case key from the argument values. You don't need another call. |
| Capture several outputs from one row or a loop | Give each capture a different, stable name. |

[Type support](TYPE-SUPPORT.md) lists the supported shapes, and [Test frameworks](FRAMEWORKS.md) covers lifetime details.

## Configuration

### Layers

Settings come from seven layers:

| Layer | Where you set it | Applies to |
| --- | --- | --- |
| Built-in | Defaults when settings are left out | Every test. |
| Project | `snapshots.config.json` | Tests that read their configuration from that project. |
| Class or method | `[SnapshotSettings]` | Suite and test naming and update policy only. |
| Test initialization | `SnapshotTestOptions` passed to a custom runner | One test scope, including identity, storage, limits and preferences. |
| Active test | Setters on `Snapshots.Current` | Defaults for all captures in the active test. Set them before its first capture. |
| Assertion | `SnapshotOptions` passed to a capture | That capture and its nested values. |
| Run environment | `IMPRINT_UPDATE`, `CI`, `IMPRINT_PROJECT_ROOT` | The update policy for the run, or a moved checkout. |

The layers don't all have the same settings. Attributes have only `Name` and `Update`. `Format` and a custom `Comparer` belong to single assertions.

A shared C# options variable has no scope by itself. It takes effect where you pass or assign it.

For representation and comparison, each field you set overrides the earlier layers, in this order:

1. Built-in defaults.
2. Project.
3. Test initialization.
4. Changes to the active test.
5. Assertion.

A `null` field in a C# options record means inherit. Setting `false`, zero or an enum's zero-valued member is a real override. `StringContent` follows the same order, as one setting. An assertion override leaves the test defaults and later assertions as they were.

Update policy has its own order, as [Update precedence](#update-precedence) describes.

### Built-in defaults

Each setting has a default:

| Setting | Default |
| --- | --- |
| Update | `Missing`. Creates missing entries after success. Fails on changed or unused entries. |
| Output format | `Auto`. Text for a non-null root string without a custom writer. JSON otherwise. |
| String content | `Value`. Imprint doesn't guess whether a string holds JSON. |
| Representation | Enum names with a numeric fallback. Byte arrays as numbers. String-keyed dictionaries as objects. |
| Comparison | Exact numbers, ordered arrays, case-sensitive strings, line endings ignored in text, trailing whitespace counted. |
| Unordered matching limit | 256 elements per array when `IgnoreArrayOrder` is on. |
| Snapshot root | `<project>/__snapshots__`. The source-relative folders and the suite and test names are added below it. |
| Failure artifacts | `<build artifacts>/imprint/failures`, falling back to `<project>/artifacts/imprint/failures`. |
| Naming | `NameThenOrder`. Framework display names are off. |
| Empty test | Fails if the active scope captures nothing. |
| Capture limits | Depth 64, 100,000 values and 4 MiB of UTF-8 output per snapshot. |
| Lock timeout | 10 seconds. |

Two limits are fixed. A test can have at most 1,024 captures. Its combined byte budget is the larger of 128 MiB and the configured per-snapshot byte limit.

### Project file

Imprint reads one `snapshots.config.json` at the resolved project root. It doesn't walk folders or merge several files. If the default file doesn't exist, Imprint uses the built-in defaults. `SnapshotTestOptions.ConfigurationFile` selects a replacement file, which must exist.

Every key, with its default:

```json
{
  "version": 1,

  "update": "missing",
  "allowEmptyTests": false,
  "stringContent": "value",

  "files": {
    "snapshotFolderName": "__snapshots__",
    "snapshotRootPath": null,
    "failureArtifactPath": "artifacts/imprint/failures"
  },

  "naming": {
    "unnamedCaptures": "name-then-order",
    "useFrameworkDisplayNames": false
  },

  "representation": {
    "enums": "name-or-number",
    "byteArrays": "numbers",
    "dictionaries": "automatic"
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

The top-level keys are:

| Key | Values | Meaning |
| --- | --- | --- |
| `$schema` | URL or path | Optional schema reference for your editor. See [Editor support](#editor-support). |
| `version` | `1` | Defaults to `1` when left out. |
| `update` | `verify`, `missing`, `all` | Update policy. [Update policies](../README.md#update-policies) describes each one. |
| `allowEmptyTests` | Boolean | Lets a test that captures nothing pass, instead of failing as a likely mistake. |
| `stringContent` | `value`, `json` | How root strings are read. Override it per test or assertion when a project captures both kinds. |

The `files` keys control where things are written:

| Key | Values | Meaning |
| --- | --- | --- |
| `snapshotFolderName` | A portable single folder name | Folder created next to your test sources to hold the snapshots. Defaults to `__snapshots__`. |
| `snapshotRootPath` | Project-relative or absolute path | Puts snapshots somewhere else. Replaces the default folder next to the sources. |
| `failureArtifactPath` | Nonempty project-relative or absolute path | Where expected and received files go when a test fails. Must be outside the snapshot root. |

The `naming` keys control how folders and files get their names:

| Key | Values | Meaning |
| --- | --- | --- |
| `unnamedCaptures` | `name-then-order`, `order`, `explicit-only` | File name for a capture without a name. `name-then-order` uses the variable name, else `snapshot-1`. `order` always uses `snapshot-1`. `explicit-only` requires a name every time. |
| `useFrameworkDisplayNames` | Boolean | Names the test folder after the framework display name, such as `[Fact(DisplayName = "...")]`, instead of the method name, when one exists. |

The `representation` keys set the defaults for each category, as [Representation](#representation) describes:

| Key | Values |
| --- | --- |
| `enums` | `name-or-number`, `number` |
| `byteArrays` | `numbers`, `base64` |
| `dictionaries` | `automatic`, `entries` |

The `comparison` keys hold the same six rules as [Comparison](#comparison):

| Key | Values |
| --- | --- |
| `numericTolerance` | Nonnegative decimal |
| `ignoreArrayOrder` | Boolean |
| `ignoreStringCase` | Boolean |
| `ignoreLineEndings` | Boolean |
| `ignoreTrailingWhitespace` | Boolean |
| `maxUnorderedArrayLength` | Integer from 1 to 1,024 |

The `limits` keys are the safety valves. Raise one only when a valid snapshot hits it:

| Key | Values | Meaning |
| --- | --- | --- |
| `maxNestingDepth` | Integer from 1 to 256 | Deepest object or array nesting that's serialized. |
| `maxValuesPerSnapshot` | Integer from 1 to 10,000,000 | Most values one capture can visit. Catches runaway graphs. |
| `maxBytesPerSnapshot` | Integer from 1 to 268,435,456 (256 MiB) | Largest single snapshot file, in UTF-8 bytes. |
| `lockTimeoutSeconds` | Integer from 1 to 300 | How long to wait for another process to release the snapshot folder lock. |

The file follows these rules:

- Keys you leave out inherit the defaults.
- JSON settings use the types above, not the nullable C# patch convention. `null` is rejected, except for `files.snapshotRootPath`.
- Unknown keys, duplicate keys, wrong types, unsupported enum values and invalid limits fail configuration. Invalid project settings are rejected even when a narrower setting overrides them.
- The snapshot, artifact and recovery folders must be separate, as [Storage](../README.md#storage) describes.

### Editor support

The keys above are described by a JSON schema, [`snapshots.schema.json`](../schemas/1.0.0/snapshots.schema.json). It also ships inside the NuGet package.

To get completion and hover docs, point your config at the schema in one of three ways:

| How | Setup | Works offline |
| --- | --- | --- |
| `$schema` URL _(recommended)_ | Add `"$schema": "https://raw.githubusercontent.com/TheLithiumForge/TheLithium.Imprint/main/schemas/1.0.0/snapshots.schema.json"`. | No, the editor fetches and caches it |
| `$schema` relative path | Copy `schemas/1.0.0/snapshots.schema.json` out of the package into your repo, then add `"$schema": "./schemas/1.0.0/snapshots.schema.json"`. | Yes |
| VS Code workspace setting | Map the file name in `.vscode/settings.json` under `json.schemas`. Commit it, and everyone on the repo gets it without a `$schema` line. | With a local copy |

### `[SnapshotSettings]`

You can put `[SnapshotSettings]` on a class or a method:

```csharp
[SnapshotSettings(Name = "Orders", Update = SnapshotUpdate.Missing)]
public sealed class OrderTests
{
    [Fact]
    [SnapshotSettings(Name = "NewOrder", Update = SnapshotUpdate.Verify)]
    public void CreatesOrder()
    {
        new { Id = 123 }.AssertSnapshot("order"); // writes .../Orders/NewOrder/order.json
    }
}
```

The attribute has two properties:

| Property | On a class | On a method |
| --- | --- | --- |
| `Name` | Suite folder name. Defaults to the class name. | Test folder name. Defaults to the method name. |
| `Update` | Default update policy for its tests. | Overrides the class policy when it isn't `Inherit`. |

These attributes don't set representation, comparison or capture limits.

An attribute on the method also sets up the generated lifetime when a capture is reached indirectly, e.g. through a delegate. An attribute on a class gives settings to discovered tests. It doesn't wrap every method in the class.

### `Snapshots.Current`

`Snapshots.Current` is the active test's scope. You can set four properties on it:

| Property | What it does |
| --- | --- |
| `Update` | Sets the whole-test update policy. `Inherit` restores the policy chosen when the scope began. |
| `Representation` | Merges the enum, byte-array and dictionary preferences you set into the current defaults. |
| `Comparison` | Merges the comparison fields you set into the current defaults. |
| `StringContent` | Sets how root strings are read for the test. |

Set these before the first capture. Later assignments fail.

The `Representation` and `Comparison` getters return the values in effect, including inherited defaults. Repeated partial assignments keep the fields you don't set. Settings belong to the current asynchronous test scope, so concurrent tests have their own scopes.

`Snapshots.UpdateCurrentTest(update = SnapshotUpdate.All)` is a shortcut for assigning `Snapshots.Current.Update`.

To share preferences across a test class, define a `static readonly` options record. Assign it at the start of each test, directly or through a helper. Declaring the field alone doesn't register suite defaults.

### `SnapshotOptions`

`SnapshotOptions` changes one capture:

| Property | Default and scope |
| --- | --- |
| `Update` | `Inherit`. A policy you set affects this entry only. `UpdateSnapshot` always asks for `All` for the entry. |
| `Format` | `Auto`. Selects JSON or text for this capture. For strings, `Json` writes a quoted JSON string value. To capture a parsed JSON document, use `StringContent = Json`. |
| `StringContent` | `null` inherits the test's setting for root strings. |
| `Representation` | `null` inherits the test preferences. A record you pass replaces only the categories it sets. |
| `Comparison` | `null` inherits the test comparison. A record you pass replaces only the fields it sets. |
| `Comparer` | `null` uses `DefaultSnapshotComparer`. Otherwise Imprint uses the `ISnapshotComparer` you pass. |

`Format = Json` treats a normal string as a JSON string value.

`StringContent = Json` takes a JSON document that's already serialized. Imprint checks it and keeps the text as you gave it. It accepts JSON objects, arrays and scalar roots. It needs a non-null string, and it can't be combined with `Format = Text` or a custom writer.

Text format needs a non-null string. If you select it, Imprint writes that string directly and skips any JSON writer you passed. A null value is normally captured as JSON `null`.

Representation preferences affect generated and built-in serialization. They don't reread text, rewrite a JSON document you gave or change a custom writer's output.

### `SnapshotTestOptions`

`SnapshotTestOptions` configures a test scope from a custom runner. Pass it to `Snapshots.Run`, `RunAsync` or `Begin`. Normal tests use the project file, attributes and `Snapshots.Current` instead.

| Properties | What they set |
| --- | --- |
| `Update` | The starting test policy. `Inherit` uses the generated method or class policy, then the project file. |
| `Representation`, `Comparison`, `StringContent` | The starting test preferences, over the project file. |
| `Name`, `Suite` | Test and suite names, over the resolved identity. With a generated identity, an enabled framework display name is chosen while settings resolve. An `Identity` you pass skips that display-name step. |
| `Case`, `Variant` | Case, and a label for each target or configuration, over the resolved identity. |
| `Identity` | A `SnapshotTestIdentity` for a custom runner or a moved executable. Skips the generated source identity. |
| `RootDirectory` | Replacement snapshot root. The source folder, suite and test are still added below it. |
| `ArtifactDirectory` | Replacement failure-artifact root. |
| `ConfigurationFile` | Selects a project config file, which must exist. Relative paths are project-relative. |
| `Naming` | `NameThenOrder`, `Order` or `ExplicitOnly`, for capture file names. |
| `AllowEmpty` | Allows a test with no captures. Combined with a whole-test `All`, this can remove the test's existing entries. |
| `MaxNestingDepth`, `MaxValuesPerSnapshot`, `MaxBytesPerSnapshot` | Override the three project capture limits, with the same ranges. |
| `CancellationToken` | Cancels the capture, comparison and lock-wait work Imprint does itself. Custom callbacks aren't interrupted or given the token. A storage transaction in progress completes or rolls back. |

Nullable properties inherit when you leave them out. There's no per-test `Format`, custom `Comparer` or lock timeout. Format and custom equality belong to captures. Lock timeout belongs to the project file.

### Run environment

Almost everything is configured through the typed API: `[SnapshotSettings]`, `SnapshotOptions` and `snapshots.config.json`. Two things can't be, so Imprint reads them from the environment:

| Variable | Meaning |
| --- | --- |
| `IMPRINT_UPDATE` | `verify`, `missing` or `all` for the run. Wins over every test and assertion policy. |
| `IMPRINT_PROJECT_ROOT` | An existing absolute project folder, for a moved checkout. Overrides the root carried by a generated or passed identity. |

Each one has its reason:

- `IMPRINT_UPDATE` - approving a change shouldn't need you to edit a committed file, then revert it. If you forget to revert `"update": "all"`, your whole suite stops checking anything without a warning. Imprint has no runner adapter, so no command-line flag or `.runsettings` value can reach it.
- `IMPRINT_PROJECT_ROOT` - the project path is baked in at compile time. This variable points it somewhere else when the tests run in a different place than they were built. E.g. you build in one container and test in another. It describes the environment, not a policy.

Imprint also reads `CI`, which your CI provider sets for you. When it's `true` or `1` and `IMPRINT_UPDATE` isn't set, the run uses `Verify`. `false`, `0`, unset or blank doesn't.

Imprint reads these variables when a scope begins. The normal `dotnet test` reads them. There's no separate Imprint command or automatic `.runsettings` integration.

### Update precedence

For each entry, the first update policy that applies wins:

1. `IMPRINT_UPDATE`, if set.
2. `Verify`, when CI is on.
3. The assertion's `Update`, including the `All` that `UpdateSnapshot` asks for.
4. The active test policy. That's a scope assignment, then `SnapshotTestOptions.Update`, then the generated method or class policy, then the project policy.
5. The built-in `Missing`.

Each policy handles the three kinds of entry like this:

| Policy | Missing entry | Different entry | Unused entry |
| --- | --- | --- | --- |
| `Verify` | Fail | Fail | Fail |
| `Missing` | Create after success | Fail | Fail |
| `All` | Create after success | Replace after success | Remove, only when `All` applies to the whole test |

Unused entries follow the whole-test policy, so a single `UpdateSnapshot` doesn't approve pruning. An assertion can override a project or test `Verify`, but not a run-wide `Verify`.

A run-wide `Verify` also refuses to recover an interrupted baseline. Locks and failure diagnostics can still write.

Every proposed baseline change needs the whole test to complete successfully, with an allowed result.

## Shared options

### Representation

`SnapshotRepresentationOptions` sets how three categories of value are written:

| Property and enum type | Choices | Default |
| --- | --- | --- |
| `Enums` / `SnapshotEnumRepresentation` | `NameOrNumber` - the exact declared name, otherwise the underlying number. `Number` - always the underlying number. | `NameOrNumber` |
| `ByteArrays` / `SnapshotByteArrayRepresentation` | `Numbers` - a JSON array of numbers. `Base64` - a JSON base64 string. | `Numbers` |
| `Dictionaries` / `SnapshotDictionaryRepresentation` | `Automatic` - string keys as object properties, other keys as entry arrays. `Entries` - arrays of `{ "Key": ..., "Value": ... }`. | `Automatic` |

Each preference applies to the whole capture, including nested values. All matching values in the capture use the same preference.

Enum flag combinations aren't expanded into names, unless the combined value has an exact declared name. Both byte-array choices are JSON, not binary snapshot files. Representation settings don't change the case identity of parameterized tests.

### Comparison

`SnapshotComparison` holds the equality rules. They decide when two snapshots count as equal:

| Property | Meaning | Default |
| --- | --- | --- |
| `NumericTolerance` | Largest absolute decimal difference accepted between two JSON numbers. Must be nonnegative. Meant for floats. | `0` |
| `IgnoreArrayOrder` | Matches JSON array elements in any order. Duplicates still have to appear the same number of times. | `false` |
| `IgnoreStringCase` | Compares string values with ordinal case-insensitive comparison. JSON property names stay case-sensitive. | `false` |
| `IgnoreLineEndings` | Treats CRLF and LF as equal in text snapshots. Keep this on for cross-platform repos. | `true` |
| `IgnoreTrailingWhitespace` | Ignores spaces and tabs at the end of each text line. | `false` |
| `MaxUnorderedArrayLength` | Longest array `IgnoreArrayOrder` tries to match, to limit the cost. Range 1 to 1,024. | `256` |

`IgnoreArrayOrder` uses real multiset matching, not a greedy pass. So it stays correct when combined with `NumericTolerance`.

These rules never change what's written to disk. The stored file is always the exact captured value. JSON comparison is structural, so it ignores formatting and the order of object properties. [Type support](TYPE-SUPPORT.md#built-in) covers the numeric limits.

### Reuse options

`SnapshotRepresentationOptions`, `SnapshotComparison`, `SnapshotOptions` and `SnapshotTestOptions` are records with init-only settings. Reuse them as they are, or copy one with C# `with`. You don't need a builder or a freeze step.

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

    order.AssertSnapshot("order");              // "Status": 1
    receipt.AssertSnapshot("receipt", new()
    {
        Representation = Defaults with
        {
            Enums = SnapshotEnumRepresentation.NameOrNumber
        }
    });                                         // "Status": "Shipped"
    order.AssertSnapshot("order-again");        // "Status": 1
}

public enum OrderStatus { Pending, Shipped }
```

All three captures keep the entry-array dictionary preference. `Defaults` and the test defaults stay as they were.

If the test already has the defaults installed, the assertion can pass only `new() { Enums = SnapshotEnumRepresentation.NameOrNumber }` and inherit the rest.

To use shared defaults for only some captures, pass the record through `SnapshotOptions.Representation` instead of assigning it to the active test.

## Writers and comparers

A custom writer changes how one value is serialized, and a custom comparer changes how two snapshots are compared:

| API | What it does |
| --- | --- |
| `SnapshotWriter<T>` | Delegate that gets a `Utf8JsonWriter`, the non-null value and a `SnapshotWriteContext`. Write exactly one JSON value and leave the writer open. |
| Writer overloads of `AssertSnapshot` and `UpdateSnapshot` | Change serialization for one capture. A null value skips the delegate and writes JSON `null`. |
| `SnapshotWriters.Register<T>(writer)` | Replaces the writer for a declared type everywhere. Register it at startup, before tests run in parallel. |
| `SnapshotWriters.Write(writer, value, context)` | Passes nested writing to an existing generated, built-in or registered writer, and keeps the context. |
| `[assembly: SnapshotInclude<MyType>]` | Asks the generator to build a writer for a concrete type reached only through a generic helper. |
| `SnapshotWriteContext.Representation` | Gives the `ResolvedSnapshotRepresentation`, the concrete read-only enum, byte-array and dictionary preferences. |
| `SnapshotWriteContext.Path` | The current JSON-style path. Starts at `$`. |
| `SnapshotWriteContext.At(member)` and `At(index)` | Push a member or index onto the path. Disposing the returned frame restores the previous path. |
| `ISnapshotComparer.Compare(expected, received, format, options)` | Custom equality for text that's already captured. Gets a `ResolvedSnapshotComparison`, whose fields are non-nullable, and a resolved format. |
| `SnapshotComparisonResult` | `Equal` and an optional `Difference`. `Match` is a reusable successful result. |
| `DefaultSnapshotComparer.Instance` | The built-in comparer. You can also use it inside your own comparer. Pass it the `ResolvedSnapshotComparison` you got, or build one with concrete rules. Fields you leave out use the built-in defaults. |

A custom writer handles its own output. It can read `context.Representation` when that helps. Registering a writer still selects it by declared type. It extends serialization, and isn't another configuration layer.

A custom comparer must be deterministic and safe to call from several threads at once. It can't change the saved format.

## Custom runner API

A custom runner opens a scope for each test, as [Custom runners](FRAMEWORKS.md#custom-runners) shows. It uses these calls:

| API | What it returns or does |
| --- | --- |
| `Snapshots.Run(Action body, options: null)` | Runs a synchronous body and completes the scope. Returns a `SnapshotReport`. |
| `Snapshots.RunAsync(Func<Task> body, options: null)` | Awaits the body and completes the scope. Returns `Task<SnapshotReport>`. |
| `Snapshots.Begin(options: null)` | Returns a `SnapshotScope` you manage yourself. |
| `Snapshots.Current` | Returns the active scope in this asynchronous context. Throws if there's none. |
| `scope.Complete()` | Compares all captures and commits the allowed changes. Returns a `SnapshotReport`, or throws. |
| `scope.Abort(error: null)` | Gives up approval and tries to keep the failure diagnostics. |
| `scope.Dispose()` | Gives up an incomplete scope and restores its parent. Never approves. |

`Run`, `RunAsync` and `Begin` also take `sourceFile` and `sourceLine` arguments that the compiler fills in. Normal callers leave them out. `Run(Func<Task>)` is an overload marked obsolete as an error, so an async callback fails to compile. Use `RunAsync` for it.

`SnapshotTestIdentity` needs `ProjectDirectory`, `SourceFile`, `Suite` and `Test`. It can also take `Case`, `Variant` and `LogicalId`. A runner that uses only the Core package needs to pass an identity, and to use built-in or custom writers. The main package usually gives you both the identity and the writers at compile time.

With a manual scope, call `Complete` after all the body and teardown work that should affect approval. On failure, call `Abort(error)` and rethrow the original exception. If a callback can hold that whole span, use `Run` or `RunAsync` instead.

The scope also has four read-only properties: `BaselineDirectory`, `BaselineFingerprint`, `ArtifactDirectory` and `EffectiveUpdate`. `EffectiveUpdate` includes the run policy. `Update` is the policy the test asked for.

Separate nested scopes resolve their own settings, and restore the parent when disposed. A helper that shares the active scope doesn't open a new test.

## Reports and failures

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

The report is made of these types:

| Type | What it holds |
| --- | --- |
| `SnapshotReport` | `Test`, `Success`, `Entries`, and an optional `ArtifactDirectory` and `ArtifactError`. A successful `Complete` returns it, and a mismatch exception carries it. |
| `SnapshotEntryResult` | `Name`, `FileName`, `Status` and an optional `Difference`. |
| `SnapshotStatus` | `Matched`, or a failure: `Missing`, `Changed`, `Unused`. Or an approval: `Created`, `Updated`, `Removed`. |

All Imprint exceptions derive from `SnapshotException`:

| Exception | Thrown when |
| --- | --- |
| `SnapshotMismatchException` | The comparison set failed. Read its `Report`. |
| `SnapshotConfigurationException` | Options are invalid, the lifetime or identity is missing, or the scope is used wrong. E.g. no test is active. |
| `SnapshotCaptureException` | A value can't be serialized or goes over a budget, or an earlier capture failure blocks completion. |
| `SnapshotConflictException` | Two test identities resolve to the same snapshot folder, another process changed the baseline during the test, or the stored state is ambiguous. |

Standard argument and cancellation exceptions can also come through. If a diagnostic write fails, the original error is kept. An aborted test can attach diagnostics to the original exception's `Data`, under `TheLithium.Imprint.Artifacts` or `TheLithium.Imprint.ArtifactError`.

## Generated code support

These members are public so that generated code in your test assembly can call them. They're part of the implementation, and you don't use them to set up normal tests:

| API | What it does |
| --- | --- |
| `Generation.TestExecution.Run` and `Run<T>` | Generated wrappers for synchronous return shapes. |
| `Generation.TestExecution.RunAsync` and `RunAsync<T>` | Generated wrappers for `Task` and `Task<T>`. |
| `Generation.TestExecution.RunValueTask` and `RunValueTask<T>` | Generated wrappers for `ValueTask` and `ValueTask<T>`. |
| `Generation.SnapshotMetadata.Register` and `Generation.Models.SnapshotDescriptor` | Register the generated source identity, names, policies and build paths. |
| `Generation.SnapshotCases.Create<T>` | Builds stable case labels for parameterized tests from argument values. Custom runners can use it too. |
| `Generation.Shapes` | Type-inference helpers for anonymous arrays, collections, dictionaries and pairs. Their sample values aren't read. |
| `SnapshotWriters.TryRegister<T>(writer)` and `TryRegister<T>(shape, writer)` | Register generated writers, only if no writer is installed yet. |

The main package imports its build integration for you. You can control it with these MSBuild settings:

- `ImprintInstrument="false"` - metadata on a `Compile` item. Leaves that source out of rewriting.
- `ImprintBuildAssembly` - the location of the build-task assembly.
- `MSBuildProjectDirectory`, `MSBuildProjectName` and `ArtifactsPath` - give the generated metadata.

These build settings are separate from the runtime configuration.

The package supports `void`, value-returning, `Task` and `ValueTask` test methods, including generic task and value-task results. Unsupported serialization fails with `IMP001`. Unsupported lifetime shapes, such as async void or iterator methods, fail with `IMP102`. [Return types](FRAMEWORKS.md#return-types) lists each shape and its fix.

Back to the [README](../README.md).
