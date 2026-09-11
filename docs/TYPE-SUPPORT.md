# What can be serialized

The complete contract. The [README](../README.md) has the short version.

**The statically declared type of the expression is the contract.** The generator reads that type at compile time and emits a writer for exactly it. There is no reflection, no `ToString()` fallback, and no runtime type discovery.

```csharp
Animal pet = new Dog { Name = "Rex", GoodBoy = true };
pet.AssertSnapshot();     // → { "Name": "Rex" }   — Dog.GoodBoy is not captured
```

If a type cannot be handled, you find out at compile time as **IMP001**, not at runtime.

## Built-in

Written by hand-rolled writers in the runtime:

`string`, `char`, `bool`, all integral types, `nint`/`nuint`, `float`, `double`, `decimal`, `Half`, `Int128`, `UInt128`, `BigInteger`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Uri`, `JsonElement`, `JsonDocument`.

Details:

- A `string` captured at the root is stored as **text** (`.txt`). A string nested inside a structured value is a JSON string.
- Date/time and numeric formatting is invariant.
- `NaN` and infinities **fail**. JSON has no representation for them, and inventing one would make the file undocumented.
- Undefined or disposed JSON values fail. JSON objects with duplicate property names fail.

## Generated

| Supported                                                      | Notes                                                                                                                          |
| -------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| Public readable instance properties and public instance fields | Of accessible user types. Types need not be `partial` or carry any attribute.                                                  |
| Records, classes, structs, DTOs                                | —                                                                                                                              |
| Anonymous types                                                | Property order is preserved while inferring the shape; output keys are sorted like everything else.                            |
| Enums                                                          | The declared name when it matches exactly, otherwise the numeric value. Flags combinations are **not** expanded into a string. |
| Nullable values                                                | —                                                                                                                              |
| Tuples, `KeyValuePair`                                         | Tuple members are `Item1`, `Item2`, … — caller-local element names are not visible to the generator.                           |
| Arrays of rank 1, 2, or 3                                      | As nested JSON arrays. Nonzero-based arrays need a custom writer.                                                              |
| A single unambiguous `IEnumerable<T>`                          | Enumeration order is preserved. Iteration must be finite. Sets are **not** sorted for you.                                     |
| String-keyed dictionaries                                      | Become JSON objects.                                                                                                           |
| Other dictionaries                                             | Become arrays of `KeyValuePair`-shaped objects, in enumeration order.                                                          |
| Interfaces and base types                                      | Serialized by their declared readable contract. See the `Animal`/`Dog` example above.                                          |

**Not** included automatically: static members, constants, indexers, write-only properties, fixed buffers, and explicit interface implementations.

Anonymous types nested in containers are inferred for: arrays; `List`, `IEnumerable`, `IList`, `ICollection`, `IReadOnlyList`, `IReadOnlyCollection`, `HashSet`, `ISet`, `Dictionary`, `IDictionary`, `IReadOnlyDictionary`, and `KeyValuePair`. Any other container holding an anonymous type needs a projection into one of those, or an explicit writer.

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

**Per call — an explicit writer.** The usual answer. No diagnostic is raised, because you have taken responsibility for the type:

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

**Rooting a type the generator never sees directly** — typically a concrete type only reached through a generic helper:

```csharp
[assembly: SnapshotInclude<MyType>]
```

**Replacing a writer globally:**

```csharp
SnapshotWriters.Register<MyType>(MyWriter);
```

Register before any test concurrency starts. The generator does not analyze startup code, so it cannot infer that you did this — a type registered this way and not otherwise supported will still raise IMP001 at its call sites.

## Safety and limits

Capture is eager: it completes before `AssertSnapshot` returns, so mutating the object afterwards cannot change what was captured.

- **Reference cycles fail**, with the member path that closed the loop. Shared references that are not cycles are fine.
- **A throwing getter fails the capture** and poisons the scope — even if the caller catches that exception, the test cannot go on to approve anything.

Defaults, and where to change them:

| Limit                      | Default                                   | Config key                           |
| -------------------------- | ----------------------------------------- | ------------------------------------ |
| Nesting depth              | 64                                        | `limits.maxNestingDepth`             |
| Values visited per capture | 100,000                                   | `limits.maxValuesPerSnapshot`        |
| Bytes per snapshot file    | 4 MiB                                     | `limits.maxBytesPerSnapshot`         |
| Captures per test          | 1,024                                     | —                                    |
| Total bytes per test       | 128 MiB, or the per-file budget if larger | —                                    |
| Unordered array length     | 256                                       | `comparison.maxUnorderedArrayLength` |

Raw `JsonDocument`/`JsonElement` values and explicit raw JSON bypass the per-member node counter, but are still bounded by parsed JSON depth, the per-file byte limit, and the comparison work budget. A custom writer bypasses generated traversal accounting entirely: its output bytes and parsed depth are still bounded, but its own execution is trusted and cannot be interrupted.

## What is written

- **Text** is preserved byte for byte, including whether it ends with a newline.
- **JSON** is canonicalized: indented, object keys sorted, invariant formatting, one trailing LF.
- **Comparison options never change any of this.** `ignoreStringCase` does not lowercase the file; `ignoreArrayOrder` does not sort it. Equality works on copies or on the parsed tree.
