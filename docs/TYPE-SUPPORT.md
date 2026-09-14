# What can be serialized

The complete contract. The [README](../README.md) has the short version.

**The statically declared type of the expression is the contract.** The generator reads that type at compile time and emits a writer for exactly it. There is no reflection, no `ToString()` fallback, and no runtime type discovery.

```csharp
Animal pet = new Dog { Name = "Rex", GoodBoy = true };
pet.AssertSnapshot();     // → { "Name": "Rex" }, Dog.GoodBoy is not captured
```

Unsupported visible call sites produce the compile-time warning **IMP001**. A missing writer still fails capture at runtime; generic helper roots can be declared with `SnapshotInclude<T>`.

## Built-in

Written through first-party .NET formatting and JSON APIs in the runtime:

`string`, `char`, `bool`, all integral types, `byte[]`, `nint`/`nuint`, `float`, `double`, `decimal`, `Half`, `Int128`, `UInt128`, `BigInteger`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Uri`, `JsonElement`, `JsonDocument`.

Details:

- A non-null `string` captured at the root with Auto format is stored as **text** (`.txt`); null is JSON `null`. A string nested inside a structured value is a JSON string.
- Date/time and numeric formatting is invariant.
- `NaN` and infinities **fail**. JSON has no representation for them, and inventing one would make the file undocumented.
- Exact numeric comparison uses System.Text.Json and treats `1` and `1.0` as equal. Explicit exponent fields are bounded to -1,000,000 through 1,000,000; larger fields fail comparison with an actionable error. Numeric tolerance has its own smaller arithmetic-work budget.
- Undefined or disposed JSON values fail. JSON objects with duplicate property names fail.
- `byte[]` defaults to a numeric array; the base64 view writes a JSON string. Neither view creates a binary snapshot file.

## Generated

| Supported                                                      | Notes                                                                                                                          |
| -------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| Public readable instance properties and public instance fields | Of accessible user types. Types need not be `partial` or carry any attribute.                                                  |
| Records, classes, structs, DTOs                                |                                                                                                                                |
| Anonymous types                                                | Property order is preserved while inferring the shape; output keys are sorted like everything else.                            |
| Enums                                                          | Declared name when it matches exactly, otherwise the numeric value; optionally always numeric. Flags combinations are not expanded. |
| Nullable values                                                |                                                                                                                                |
| Tuples, `KeyValuePair`                                         | Tuple members are `Item1`, `Item2`, and so on. Caller-local element names are not visible to the generator.                    |
| Arrays of rank 1, 2, or 3                                      | As nested JSON arrays. Nonzero-based arrays need a custom writer.                                                              |
| A single unambiguous `IEnumerable<T>`                          | Enumeration order is preserved. Iteration must be finite. Sets are **not** sorted for you.                                     |
| String-keyed dictionaries                                      | JSON objects by default; optionally arrays of Key/Value entries. |
| Other dictionaries                                             | Become arrays of `KeyValuePair`-shaped objects, in enumeration order.                                                          |
| Interfaces and base types                                      | Serialized by their declared readable contract. See the `Animal`/`Dog` example above.                                          |

**Not** included automatically: static members, constants, indexers, write-only properties, fixed buffers, and explicit interface implementations.

Anonymous types nested in containers are inferred for: arrays; `List`, `IEnumerable`, `IList`, `ICollection`, `IReadOnlyList`, `IReadOnlyCollection`, `HashSet`, `ISet`, `Dictionary`, `IDictionary`, `IReadOnlyDictionary`, and `KeyValuePair`. Any other container holding an anonymous type needs a projection into one of those, or an explicit writer.

## Representation choices

Representation preferences compose from project through test to assertion. Each specified category overrides the broader setting; unspecified categories inherit. The resulting preferences apply throughout the capture, including nested members. Options are immutable records, so shared defaults can be reused and copied with `with`:

```csharp
var defaults = new SnapshotRepresentationOptions
{
    Enums = SnapshotEnumRepresentation.Number,
    Dictionaries = SnapshotDictionaryRepresentation.Entries
};

order.AssertSnapshot("order", new() { Representation = defaults });
receipt.AssertSnapshot("receipt", new()
{
    Representation = defaults with { Enums = SnapshotEnumRepresentation.NameOrNumber }
});
```

The receipt capture uses enum names with numeric fallback and keeps dictionaries as entry arrays. The shared defaults remain unchanged. To format a particular member differently, project the value before capture or supply an explicit writer.

Representation preferences do not alter parameterized case identity, interpret application serializer attributes, or discover runtime subtypes. Custom writers retain responsibility for their own output and can read the capture's concrete preferences through `context.Representation` (`ResolvedSnapshotRepresentation`). JSON output keeps Unicode and HTML characters readable while escaping quotes, backslashes and control characters. See the [API overview](API.md#configuration-apis) for configuration layers and the [configuration reference](../README.md#configuration) for project settings.

## Not supported

These produce IMP001 and need an explicit writer or a projection:

- `object` and `dynamic` shapes
- pointers, function pointers, and ref-like types
- inaccessible private nested named types
- opaque framework types the generator does not recognize
- objects with no readable public members
- multiple incompatible `IEnumerable<T>` implementations on one type
- anything that would require discovering a runtime subtype

## Escape hatches

**Per call, an explicit writer.** The usual answer. No diagnostic is raised, because you have taken responsibility for the type:

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

The delegate must write exactly one JSON value and leave the writer open. A `null` value writes JSON `null` without calling it.

**Rooting a type the generator never sees directly**, typically a concrete type only reached through a generic helper:

```csharp
[assembly: SnapshotInclude<MyType>]
```

**Replacing a writer globally:**

```csharp
SnapshotWriters.Register<MyType>(MyWriter);
```

Register before any test concurrency starts. The generator does not analyze startup code, so it cannot infer that you did this. A type registered this way and not otherwise supported will still raise IMP001 at its call sites.

## Safety and limits

Capture is eager. It completes before `AssertSnapshot` returns, so mutating the object afterwards cannot change what was captured.

- **Reference cycles fail**, with the member path that closed the loop. Shared references that are not cycles are fine.
- **A throwing getter fails the capture** and poisons the scope. Even if the caller catches that exception, the test cannot go on to approve anything.

Defaults, and where to change them:

| Limit                      | Default                                   | Config key                           |
| -------------------------- | ----------------------------------------- | ------------------------------------ |
| Nesting depth              | 64                                        | `limits.maxNestingDepth`             |
| Values visited per capture | 100,000                                   | `limits.maxValuesPerSnapshot`        |
| Bytes per snapshot file    | 4 MiB                                     | `limits.maxBytesPerSnapshot`         |
| Captures per test          | 1,024                                     | None                                 |
| Total bytes per test       | 128 MiB, or the per-file budget if larger | None                                 |
| Unordered array length     | 256                                       | `comparison.maxUnorderedArrayLength` |

Every JSON capture is bounded by emitted node count, parsed depth and UTF-8 bytes, including raw JSON, `JsonDocument`/`JsonElement` and custom-writer output. Each object, array and scalar counts as one emitted node; property names do not count separately. Generated writers also keep an independent traversal counter to stop runaway graphs early. The two counters use the same configured limit and are not added together. Custom writer/getter execution remains trusted; cancellation is checked before and after it and during Imprint's own traversal, but cannot interrupt arbitrary user code.

## What is written

- **Text** is preserved byte for byte, including whether it ends with a newline.
- **Generated JSON values** are canonicalized: indented two spaces, object keys sorted ordinally, invariant formatting, one trailing LF.
- **Supplied JSON strings** (`StringContent = Json`) are validated and preserved as supplied. Their comparison and contextual diffs are structural; whitespace and object property order do not change equality.
- **Comparison options never change any of this.** `ignoreStringCase` does not lowercase the file; `ignoreArrayOrder` does not sort it. Equality works on copies or on the parsed tree.

### How values appear

Snapshots are read by people, so the default rendering favours the form you would write by hand. The [README](../README.md#what-the-snapshots-look-like) shows a worked example; this is the rule for each family.

| Value | Written as | Note |
| --- | --- | --- |
| `enum` | `"Shipped"` | Declared name when one matches exactly; otherwise the underlying number. A combined `[Flags]` value with no declared name is a number. |
| `DateTime` | `"2026-03-09T14:05:00.0000000Z"` | Round-trip (`"O"`) format, invariant culture. A UTC value ends in `Z`. |
| `DateTimeOffset` | `"2026-03-09T14:05:00.0000000+00:00"` | Same format; the offset is always explicit, so a zero offset is `+00:00` rather than `Z`. |
| `DateOnly`, `TimeOnly` | `"2026-03-09"`, `"14:05:00.0000000"` | Invariant. |
| `TimeSpan` | `"01:33:00"` | Constant (`"c"`) format. |
| `Guid` | `"6f9619ff-8b86-d011-b42d-00cf4fc964ff"` | `"D"` format, lowercase. |
| `decimal` | `42.50` | Scale preserved; `42.50m` does not become `42.5`. |
| `double`, `float` | `0.30000000000000004` | Shortest round-trippable form, so drift is visible rather than rounded away. |
| `Int128`, `BigInteger`, `long` | exact digits | Never exponential notation. |
| `Uri` | `"https://example.com/a?b=1&c=2"` | Original string. |
| `string` at the root | a `.txt` file | Exact bytes, no JSON quoting or escaping. |
| `string` nested in a value | `"text"` | JSON string, escaped only as JSON requires. |
| Nullable with no value | `null` | |
| String-keyed dictionary | `{ "BE": 1, "NL": 2 }` | Object, keys sorted. Other key types become entry arrays. |
| Tuple | `{ "Item1": 3, "Item2": "boxes" }` | Element names are a caller-side alias and are not visible to the generator. |
| `byte[]` | `[1, 2, 3]` | Or a base64 string. See [representation choices](#representation-choices). |

`NaN` and infinity have no JSON number form, so they fail rather than acquiring an undocumented encoding.

### Which characters are escaped

JSON output escapes only what the format requires, so reviewers see text rather than code points:

| | |
| --- | --- |
| **Written literally** | Accented Latin, CJK, Cyrillic, Arabic, Greek, Hebrew, and symbols such as `☕`, meaning anything in the Basic Multilingual Plane. Also `<`, `>`, `&`, `'` and `+`, which many JSON writers escape by default to make output safe to embed in HTML. That protection is useless for a file on disk and makes review painful. |
| **Escaped** | `"` as `\"`, `\` as `\\`, and control characters, with tab as `\t`, newline as `\n`, and the rest as `\u00XX`. |
| **Escaped as surrogate pairs** | Characters above the Basic Multilingual Plane, which in practice means emoji. `"📦"` is stored as `"\uD83D\uDCE6"`. This is `Utf8JsonWriter` behaviour and Imprint does not override it. |

Escaping is never lossy in either direction. A Windows path keeps its backslashes (`"C:\\temp\\file.txt"` reads back as `C:\temp\file.txt`), and a real tab stays distinct from the two characters `\` and `t`. The round trip is covered by `JsonSnapshotsKeepReadableTextAndRoundTripEscapedCharacters` and by the independent package consumer, which assert both the readable form and lossless recovery of the same string.

Text snapshots are not JSON and are never escaped at all. A `.txt` file holds exactly the bytes you captured.
