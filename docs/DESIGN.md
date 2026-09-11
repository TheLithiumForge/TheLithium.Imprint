# How it works

Implementation notes. For using the library, see the [README](../README.md).

Three pieces do the work: an MSBuild task that gives every test method a snapshot lifetime, a source generator that turns declared types into serializers, and a transactional file store that only ever commits a whole test at once.

## 1. The test lifetime

The problem: `AssertSnapshot` has to compare a value, but the decision to _write_ it cannot be made until the whole test succeeds. Something has to know when the method ends.

The usual answer is a callback — `Verify(() => { ... })` — which costs every user a wrapper around every test. Instead, the build task rewrites the compile input.

During compilation it parses the test project, builds a call graph, and finds every method that reaches `AssertSnapshot` or `UpdateSnapshot`, directly or through helpers. For each one it replaces the _compiler input only_ (never a file on disk) with a version whose original body has become a local function, called through `TestExecution.Run`:

```csharp
// what you wrote
public async Task CreatesAnOrder() { ... }

// what the compiler sees
public async Task CreatesAnOrder()
    => await TestExecution.RunAsync(async () => { ...original body... }, options, file, line);
```

The method's signature, attributes, and visibility are untouched, so the runner sees exactly what it expected. `#line` directives keep stack traces and debugger stepping pointing at the original source.

The wrapper handles `void`, `T`, `Task`, `Task<T>`, `ValueTask`, and `ValueTask<T>`. Anything without a reliable completion point — `async void`, an async custom awaitable, an iterator, `ref`/`in`/`out`, a by-ref return, a test on a struct — is rejected at compile time with **IMP102** rather than being wrapped incorrectly.

The active scope lives in an `AsyncLocal`, so a helper called from the test body finds it without being passed anything. A helper that wants its own boundary calls `Snapshots.Begin` with an explicit identity.

**Boundary:** awaited work and `finally` blocks _inside_ the method are part of the lifetime. Runner teardown that happens _after_ the method is not.

## 2. Compile-time serialization

The incremental generator inspects the semantic model at each capture site, takes the statically declared type of the expression, and emits a direct `SnapshotWriter<T>` — a delegate that writes that exact type's members to a `Utf8JsonWriter`. Module initializers register them.

Nothing at runtime enumerates assemblies, types, properties, or attributes. That is the whole reason Native AOT works, and it is also why the declared type is the contract: a `Dog` held in an `Animal` variable serializes `Animal`'s members, because `Animal` is what the generator saw.

A type it cannot handle is reported as **IMP001** at compile time with the reason, so the failure arrives while you are writing the test rather than while you are running it. A closed generic reached only through a helper needs `[assembly: SnapshotInclude<T>]` to be rooted. A type that is somehow never registered produces a clear capture error — there is no reflection fallback and no `ToString()` fallback.

The generator also emits the identity metadata: suite, method, display metadata, update attributes, source line ranges, and the project artifact root. Source location is used _only_ to select the right descriptor for a call site; it is never persisted as snapshot identity, so moving a method within a file does not rename its folder.

### Parameterized case keys

The build task derives the case discriminator from the actual argument values, rendered from their canonical JSON so the folder is reviewable:

```text
Parses [count=1, label=first]
```

`SnapshotCases.Create` appends a 12-character hash of that canonical JSON **only when the label cannot stand alone as an identity**, because these folders are committed and every character is path budget. Three cases force the hash:

1. **Truncation.** The label is capped at 56 characters. Past that the visible text no longer distinguishes rows, so the hash becomes the identity.
2. **A value that reads as a literal.** The string `"1"` and the number `1` both render as `1`, as do `"true"` and `true`.
3. **A value carrying the label's own separators** (`,` `=` `~`), or a nested object or array, where the rendered text cannot be parsed back to one arrangement of arguments.

Anything else — plain numbers, booleans, null, and strings that cannot be mistaken for them — is already unique per row, and gets no suffix.

Note that `PortableNames.Segment` is a second, independent safety net: it appends its own hash whenever it has to rewrite or truncate a folder name, so an illegal character or an over-length path cannot silently merge two folders even when the case key itself was left bare.

A row's _display text_ is deliberately not used — it is dynamic in several frameworks and would make identity unstable. `CancellationToken` parameters are excluded because they are execution control, not test data.

The case folder identifies one invocation; the capture name identifies one file inside it.

## 3. Capture, compare, commit

```text
AssertSnapshot(value)   →  serialize now, into memory
                           (so later mutation of the object cannot change what was captured)

method returns          →  compare the complete set against the baseline
                           produce one SnapshotEntryResult per entry
                           authorize: Missing needs the missing policy, Changed/Unused need All

all entries authorized  →  commit the whole set in one transaction
any entry not           →  throw SnapshotMismatchException, write artifacts, touch nothing
method threw            →  abort, touch nothing
```

Capture is eager and finishes before the extension method returns. A throwing property getter fails the capture and poisons the scope even if the caller swallows that exception, so a test cannot half-succeed its way to an approval.

Comparison never rewrites stored bytes. Equality options operate on parsed representations or on copies; the file on disk is always the exact captured value.

## 4. Formats

One capture, one file: `.json` for structured values, `.txt` (or `.snap`) for plain text. Two files with the same capture name but different extensions is an ambiguity error, not a silent pick.

JSON is canonicalized — indented, object keys sorted, invariant number and date formatting, final LF — so the bytes are deterministic across machines and runs. Text is preserved exactly, including whether it ends with a newline.

Comparison of JSON is structural, not textual. `IgnoreArrayOrder` uses maximum bipartite matching rather than a greedy scan: with `NumericTolerance` in play, "equal" is not transitive, and a greedy match would report differences that depend on element order. Matching is bounded by `maxUnorderedArrayLength` and by an overall comparison work budget.

## 5. The store

Baseline path:

```text
<snapshots root>/<source dir relative to project>/<suite>/<test> [case] [variant]/<capture>.<ext>
```

Every segment is normalized and length-bounded, reserved Windows device names are escaped, traversal and reparse points are checked, and case-insensitive filename collisions are rejected. This protects ordinary repository use; it is not a defense against malicious code racing the filesystem.

A commit is a small transaction:

1. take a cross-process file lock
2. read and fingerprint the current files
3. write a journal containing the before and after states
4. flush a `prepared` marker
5. apply the files
6. record a `committed` marker

A later run that finds an interrupted `prepared` journal recovers it, but only after verifying the backup's fingerprint — a corrupt journal refuses to roll back and is preserved for inspection rather than guessed at. Optimistic fingerprints stop a second process from silently overwriting a baseline that changed during the test.

Individual files are replaced atomically where the filesystem supports it; the directory as a whole is not one atomic operation. Recovery covers cooperative local processes and ordinary interruption. Distributed filesystems, power loss, and hostile concurrent edits are outside the guarantee.

Locks, journals, and failure artifacts all live under the project's `artifacts` directory — never inside the reviewed baseline tree. Config enforces this: a `files.failureArtifactPath` inside the snapshot root is a configuration error.

## What is deliberately absent

Each of these was considered and left out, because it would either require runtime reflection or grow the surface a user has to learn:

| Not provided                          | Instead                                                      |
| ------------------------------------- | ------------------------------------------------------------ |
| Runner-specific adapters              | The build task works for any runner compiled by the .NET SDK |
| A CLI updater                         | `IMPRINT_UPDATE=all dotnet test`                             |
| A scrubbing / redaction pipeline      | `ISnapshotComparer`, or project the value before capturing   |
| Image and binary snapshot formats     | —                                                            |
| Global orphan pruning                 | Test-wide `all` removes that test's unused entries           |
| Runtime polymorphic member discovery  | Declare the derived type, or use an explicit writer          |
| A distributed writable baseline store | —                                                            |
