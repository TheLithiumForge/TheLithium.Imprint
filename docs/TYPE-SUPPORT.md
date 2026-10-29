# Type support

Imprint can capture a value when the source generator can build a writer for its declared type, or when the type is one of the built-in types.

The statically declared type of the expression decides what's captured. The generator reads that type at compile time and builds a writer for exactly that type. There's no reflection, no `ToString()` fallback and no runtime type discovery. E.g. a `Dog` held in an `Animal` variable is captured as an `Animal`:

```csharp
Animal pet = new Dog { Name = "Rex", GoodBoy = true };
pet.AssertSnapshot();     // writes { "Name": "Rex" }. Dog.GoodBoy isn't captured
```

A call site the generator sees but can't support gets the compile-time warning `IMP001`. A missing writer still fails the capture at runtime. For a type reached only through a generic helper, declare it with `SnapshotInclude<T>`, as [Custom writers](#custom-writers) shows.

## Built-in

The runtime writes these types through first-party .NET formatting and JSON APIs:

`string`, `char`, `bool`, all integral types, `byte[]`, `nint`, `nuint`, `float`, `double`, `decimal`, `Half`, `Int128`, `UInt128`, `BigInteger`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Uri`, `JsonElement`, `JsonDocument`.

They follow these rules:

- A non-null `string` captured at the root with `Auto` format is stored as text, in a `.txt` file. A null string is JSON `null`. A string nested inside a structured value is a JSON string.
- Date, time and number formatting is invariant.
- `NaN` and infinities fail. JSON has no form for them, and making one up would put an undocumented format in the file.
- Exact numeric comparison uses System.Text.Json and treats `1` and `1.0` as equal. Exponent fields are limited to -1,000,000 through 1,000,000. A larger field fails comparison with an error that says what to do. Numeric tolerance has its own, smaller budget for arithmetic work.
- Undefined or disposed JSON values fail. JSON objects with duplicate property names fail.
- `byte[]` defaults to an array of numbers. The base64 form writes a JSON string. Neither form creates a binary snapshot file.

## Generated

The generator builds writers for these shapes:

| Supported                                                      | Notes                                                                                                                          |
| -------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| Public readable instance properties and public instance fields | Of accessible user types. Types don't need to be `partial` or have an attribute.                                               |
| Records, classes, structs, DTOs                                |                                                                                                                                |
| Anonymous types                                                | Property order is kept while the shape is inferred. Output keys are sorted like everything else.                               |
| Enums                                                          | The declared name when it matches exactly, otherwise the number. You can choose to always write the number. Flag combinations aren't expanded. |
| Nullable values                                                |                                                                                                                                |
| Tuples, `KeyValuePair`                                         | Tuple members are `Item1`, `Item2` and so on. The generator can't see element names the caller gives locally.                  |
| Arrays of rank 1, 2 or 3                                       | Written as nested JSON arrays. Arrays that don't start at zero need a custom writer.                                           |
| A single unambiguous `IEnumerable<T>`                          | Enumeration order is kept. Iteration must end. Sets aren't sorted for you.                                                     |
| String-keyed dictionaries                                      | JSON objects by default. You can choose arrays of Key/Value entries.                                                           |
| Other dictionaries                                             | Arrays of `KeyValuePair`-shaped objects, in enumeration order.                                                                 |
| Interfaces and base types                                      | Written by their declared readable members. See the `Animal` and `Dog` example above.                                          |

The generator leaves out static members, constants, indexers, write-only properties, fixed buffers and explicit interface implementations.

Anonymous types nested in containers are inferred for arrays, `List`, `IEnumerable`, `IList`, `ICollection`, `IReadOnlyList`, `IReadOnlyCollection`, `HashSet`, `ISet`, `Dictionary`, `IDictionary`, `IReadOnlyDictionary` and `KeyValuePair`. If another container holds an anonymous type, map it to one of those, or use a custom writer.

## Unsupported types

These shapes produce `IMP001`, and need a custom writer or a smaller view of the value:

- `object` and `dynamic` shapes
- Pointers, function pointers and ref-like types
- Private nested named types you can't access
- Framework types the generator doesn't recognize
- Objects with no readable public members
- Several incompatible `IEnumerable<T>` implementations on one type
- Anything that needs a runtime subtype to be discovered

## Custom writers

You can take over serialization in three ways.

### Per call

A custom writer on one call is the usual answer. It raises no diagnostic, because you've taken responsibility for the type:

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

### Generic helpers

If the generator never sees a type directly, add it at the assembly level. Typically that's a concrete type reached only through a generic helper:

```csharp
[assembly: SnapshotInclude<MyType>]
```

### Global writer

To replace a writer for every capture of a type, register it:

```csharp
SnapshotWriters.Register<MyType>(MyWriter);
```

Register it before tests start running in parallel. The generator doesn't read startup code, so it can't tell that you did this. A type registered this way, and not otherwise supported, still raises `IMP001` at its call sites.

## Representation

Enums, byte arrays and dictionaries each have two formats. You can set the preference for the project, the test or one assertion. Each category you set overrides the broader setting, and the categories you don't set inherit. The result applies to the whole capture, including nested members.

The options are immutable records, so you can reuse shared defaults and copy them with `with`:

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

The receipt capture writes enum names with a numeric fallback, and keeps dictionaries as entry arrays. The shared defaults stay as they were. To format one member differently, capture a smaller view of the value, or use a custom writer.

Representation preferences don't change the case identity of parameterized tests. They don't read your app's serializer attributes or discover runtime subtypes.

A custom writer is still responsible for its own output. It can read the capture's concrete preferences through `context.Representation`, a `ResolvedSnapshotRepresentation`.

[Representation](API.md#representation) lists each choice, and [Configuration](API.md#configuration) covers the layers.

## Limits

Capture is eager. It completes before `AssertSnapshot` returns, so changing the object afterwards can't change what was captured.

Two graph shapes fail:

- Reference cycles - fail with the member path that closed the loop. Shared references that aren't cycles are fine.
- A throwing getter - fails the capture and poisons the scope. Even if the caller catches that exception, the test can't approve anything afterwards.

Each limit has a default, and most have a config key:

| Limit                      | Default                                   | Config key                           |
| -------------------------- | ----------------------------------------- | ------------------------------------ |
| Nesting depth              | 64                                        | `limits.maxNestingDepth`             |
| Values visited per capture | 100,000                                   | `limits.maxValuesPerSnapshot`        |
| Bytes per snapshot file    | 4 MiB                                     | `limits.maxBytesPerSnapshot`         |
| Captures per test          | 1,024                                     | None                                 |
| Total bytes per test       | 128 MiB, or the per-file budget if larger | None                                 |
| Unordered array length     | 256                                       | `comparison.maxUnorderedArrayLength` |

Every JSON capture is limited by its emitted node count, parsed depth and UTF-8 bytes. That includes raw JSON, `JsonDocument`, `JsonElement` and custom-writer output. Each object, array and scalar counts as one emitted node. Property names don't count separately.

Generated writers also keep their own traversal counter, to stop runaway graphs early. The two counters use the same configured limit, and they aren't added together.

Custom writers and getters are still trusted code. Imprint checks for cancellation before and after it runs them, and during its own traversal. It can't interrupt arbitrary user code.

## What is written

Each kind of snapshot is written in one fixed way:

- Text - kept byte for byte, including whether it ends with a newline.
- Generated JSON values - indented by two spaces, with object keys sorted ordinally, invariant formatting and one trailing LF.
- JSON strings you pass with `StringContent = Json` - checked, then kept as you gave them. Their comparison and diffs are structural, so whitespace and the order of object properties don't change equality.

Comparison options never change any of this. `ignoreStringCase` doesn't lowercase the file, and `ignoreArrayOrder` doesn't sort it. Equality works on copies or on the parsed tree.

### How values appear

People read snapshots, so each value is written by default in the form you'd write by hand. The [README](../README.md#snapshot-format) shows an example. Each type follows its own rule:

| Value | Written as | Note |
| --- | --- | --- |
| `enum` | `"Shipped"` | The declared name when one matches exactly. Otherwise the underlying number. A combined `[Flags]` value with no declared name is a number. |
| `DateTime` | `"2026-03-09T14:05:00.0000000Z"` | Round-trip (`"O"`) ISO 8601 format, invariant culture. A UTC value ends in `Z`. |
| `DateTimeOffset` | `"2026-03-09T14:05:00.0000000+00:00"` | Same format. The offset is always written, so a zero offset is `+00:00` instead of `Z`. |
| `DateOnly`, `TimeOnly` | `"2026-03-09"`, `"14:05:00.0000000"` | Invariant. |
| `TimeSpan` | `"01:33:00"` | Constant (`"c"`) format. |
| `Guid` | `"6f9619ff-8b86-d011-b42d-00cf4fc964ff"` | `"D"` format, lowercase. |
| `decimal` | `42.50` | Scale kept. `42.50m` doesn't become `42.5`. |
| `double`, `float` | `0.30000000000000004` | Shortest form that round-trips, so drift shows in the diff instead of being rounded away. |
| `Int128`, `BigInteger`, `long` | Exact digits | Never exponential notation, e.g. never `9.2E+18`. |
| `Uri` | `"https://example.com/a?b=1&c=2"` | The original string, `&` included. |
| `string` at the root | A `.txt` file | Exact bytes, with no JSON quoting or escaping. |
| `string` nested in a value | `"text"` | A JSON string, escaped only as JSON requires. |
| Nullable with no value | `null` | |
| String-keyed dictionary | `{ "BE": 1, "NL": 2 }` | An object with sorted keys, not an array of key/value pairs. Other key types become entry arrays. |
| Tuple | `{ "Item1": 3, "Item2": "boxes" }` | Element names are a caller-side alias, so the generator can't see them. |
| `byte[]` | `[1, 2, 3]` | Or a base64 string. See [Representation](#representation). |

`NaN` and infinity have no JSON number form, so they fail instead of getting an undocumented encoding.

### Escaped characters

JSON output escapes only what the format requires, so reviewers see text instead of code points:

| Characters | How they're written |
| --- | --- |
| Accented Latin, CJK, Cyrillic, Arabic, Greek, Hebrew, and symbols such as `☕`. That's anything in the Basic Multilingual Plane. Also `<`, `>`, `&`, `'` and `+`. | As themselves. Many JSON writers escape `<`, `>`, `&`, `'` and `+` by default, so the output is safe to embed in HTML. That protection is useless for a file on disk, and it makes review painful. |
| `"`, `\` and control characters | Escaped. `"` as `\"`, `\` as `\\`, tab as `\t`, newline as `\n`, and the rest as `\u00XX`. |
| Characters above the Basic Multilingual Plane, which in practice means emoji | Escaped as surrogate pairs. `"📦"` is stored as `"\uD83D\uDCE6"`. That's `Utf8JsonWriter` behavior, and Imprint doesn't override it. |

Escaping never loses information in either direction. A Windows path keeps its backslashes, so `"C:\\temp\\file.txt"` reads back as `C:\temp\file.txt`. A real tab stays different from the two characters `\` and `t`.

Text snapshots aren't JSON, and they're never escaped. A `.txt` file holds exactly the bytes you captured.

Back to the [README](../README.md).
