# Static type support

This is the static serialization contract. See [validation](VALIDATION.md) for the executed managed and Native AOT checks.

## Built-in runtime writers

Strings, chars, booleans, integral numeric types, native integers, float/double/decimal/Half, Int128/UInt128/BigInteger, Guid, DateTime/DateTimeOffset/DateOnly/TimeOnly/TimeSpan, Uri, JsonElement, and JsonDocument.

String roots normally use text; strings nested inside structured values use JSON strings. Date/time and numeric formatting is invariant. NaN and infinities have no normal JSON numeric representation and fail rather than acquiring an undocumented encoding. Undefined/disposed JSON values fail. JSON objects with duplicate property names fail.

## Generated writers

- Publicly readable instance properties and public instance fields of accessible user types. Static members, constants, indexers, write-only properties, fixed buffers, and explicit-interface implementations are not automatically included.
- Records, supported structs, and named DTOs. Types need not be partial or carry production-code attributes.
- Anonymous types and nested common arrays/collections. Anonymous property order is preserved while constructing an inference-only shape; output properties are sorted independently.
- Enums: declared names where exactly matched; numeric underlying value otherwise. Flags combinations are not reflectively expanded into a string.
- Nullable values, ordinary tuples, and KeyValuePair. Tuple output names are Item1, Item2, etc., rather than caller-local aliases.
- Arrays of rank 1, 2, or 3, using nested JSON arrays. Nonzero-based arrays require a custom writer.
- A single unambiguous IEnumerable<T> contract. Enumeration order is preserved; collection iteration must be finite. No automatic set sorting is performed.
- String-key dictionaries become JSON objects. Other dictionary key types become arrays of KeyValuePair-shaped objects, preserving enumeration order.
- User-defined interfaces/base types use their statically declared readable contract. Runtime-derived fields are not guessed.

Anonymous generic shape inference supports arrays; List, IEnumerable, IList, ICollection, IReadOnlyList, IReadOnlyCollection, HashSet, ISet, Dictionary, IDictionary, IReadOnlyDictionary, and KeyValuePair. Other containers containing anonymous types need a projection to a supported shape or an explicit writer.

## Unsupported or explicit

Opaque object/dynamic shapes, pointers/function pointers/ref-like values, inaccessible private nested named types, opaque unrecognized framework types, objects with no readable public members, multiple incompatible enumeration contracts, and arbitrary runtime subtype discovery.

The generator reports IMP001 for an unsupported discovered static root/dependency. Per-call explicit-writer overloads do not request automatic root generation. A concrete type seen only through a generic helper needs `[assembly: SnapshotInclude<T>]` or a writer. A closed type that is never generated/registered causes a clear runtime capture error, not reflection fallback.

A global `SnapshotWriters.Register<T>` overrides a writer. Register before test concurrency starts. For unsupported types, the explicit per-call writer avoids a generator diagnostic; the generator does not infer registration effects from arbitrary startup code.

## Safety and bounded work

Reference cycles fail with a member path. Capture is eager and completes before the extension call returns. A throwing getter fails capture and poisons the scope even when the caller catches that exception.

Default limits are depth 64, 100,000 generated-value nodes, 4 MiB per file, 1,024 captures/files per test, and an aggregate 128 MiB test budget (or the explicit per-file budget when larger). Configuration raises limits within documented schema bounds. Comparison has its own bounded-work checks and an unordered-array limit of 256 by default.

Raw JsonDocument/JsonElement and explicit raw JSON bypass the generated per-member node counter, but remain subject to parsed JSON depth, per-file byte limits, and comparison work limits. Custom writers can bypass generated traversal accounting; serialized bytes and parsed depth remain bounded, but their own execution is trusted and cannot be preempted by this library.

Text captures preserve their payload, including final-newline presence. JSON formatting adds a final LF and sorts object keys. Equality options operate on comparison copies or parsed representations and do not remove saved properties.
