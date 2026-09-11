---
open-forge:
  description: Deferred code-quality findings from the 2026-09-11 review of TheLithium.Imprint.Core
  tags: [Observation, Deferred, CSharp, Style, Maintainability, TheLithium.Imprint]
---

# Deferred findings — TheLithium.Imprint.Core

From a full read of the runtime on 2026-09-11, measured against `.agents/directives/style.md`
and `.agents/directives/design.md`. Everything safety- or UX-facing from that review was fixed at
the time. What is left is correctness-latent or stylistic, and is parked here deliberately.

None of these are user-visible today. Item 1 is a latent bug and should go first.

## 1. Duplicated constants, one of which can silently disagree

`SnapshotLimits` owns the budgets, but several places restate them.

| Where | Literal | Owner it duplicates |
| --- | --- | --- |
| `Comparison/DefaultSnapshotComparer.cs:55,56` | `new JsonDocumentOptions { MaxDepth = 256 }` (twice) | `SnapshotLimits.MaximumDepth` |
| `Execution/SnapshotScope.cs` | `"A test may capture at most 1024 values."` | `SnapshotLimits.MaximumEntries` |
| `Storage/SnapshotStore.cs:154` | `"A test directory cannot contain more than 1024 snapshot files."` | `SnapshotLimits.MaximumEntries` |
| `Configuration/Settings.cs` | `"Limits: MaxNestingDepth 1..256, MaxValuesPerSnapshot 1..10000000, MaxBytesPerSnapshot 1..268435456."` | all three |
| `Configuration/Settings.cs` | `"MaxUnorderedArrayLength must be 1..1024."` | the comparison bound |

Raising `MaximumDepth` leaves the comparer parsing at 256 — the capture succeeds and the
comparison then rejects the same document. Two answers to one structural question, agreeing only
by coincidence. The message literals are milder: change a constant and the errors start lying.

Fix: reference the constants, and build the range text from them.

## 2. Constants with no symbolic home

Values with constant-like meaning written inline, against the "one symbolic definition at the
nearest owning scope" rule:

- `DefaultSnapshotComparer.Budget._remaining = 2_000_000` — the comparison work budget.
- `PortableNames.Segment` — `96` segment cap, `[..16]` hash prefix.
- `ProjectConfigurationReader` — `1024 * 1024` config size cap, `MaxDepth = 16` config nesting.
- `SnapshotStore` — `128` as the marker-file read size, three times.

`SnapshotCases` does this correctly (`MaximumCaseBytes`, `MaximumLabelLength`, `HashLength`) and is
the model to copy.

## 3. String concatenation where the style directive asks for interpolation

78 lines across the runtime concatenate a literal with a value. The directive is explicit:
*"When one coherent string contains fixed text and values, prefer an interpolated template over
concatenation."*

| File | Lines |
| --- | --- |
| `Comparison/DefaultSnapshotComparer.cs` | 16 |
| `Storage/SnapshotStore.cs` | 16 |
| `Generator/SnapshotGenerator.cs` | 14 |
| `Configuration/ProjectConfigurationReader.cs` | 12 |
| others | 20 |

The codebase already does both, sometimes on adjacent lines in the same method
(`DefaultSnapshotComparer.cs:44` interpolates, `:116` concatenates). Mechanical, low risk, best done
as one style-only pass that changes no behaviour.

## 4. `SnapshotScope.Complete()` is ~150 lines in a single lock

It guards state, compares every capture, detects unused entries, decides authorization, commits,
and maintains two parallel result lists (`entries` and `approvedEntries`) that must stay in step.
It is the most correctness-critical method in the library and the hardest to read.

Natural seams: per-entry comparison → a result; unused-entry detection → results; authorization
decision → a verdict; commit + report. Extracting them would also make the `entries` /
`approvedEntries` duality explicit instead of interleaved.

Behaviour is well covered by the specification suite, so this refactor is verifiable.

## 5. `SnapshotArtifacts.Write` takes seven parameters

```csharp
Write(EffectiveSettings settings, string executionId, IReadOnlyList<CapturedValue> values,
      BaselineState baseline, IReadOnlyList<SnapshotEntryResult> entries, bool incomplete, string? error)
```

A behavioural method, so the design directive's 4–5 limit applies. `SnapshotTestIdentity` (7) and
`DefaultSnapshotComparer.Augment` (6) are both covered by the documented exceptions — data contract
and recursive traversal — but this one is textbook repeated member forwarding. The caller already
holds the scope's cohesive state; pass one context.

## 6. `SizeLimitedStream` only guards three write paths

It derives from `MemoryStream` and overrides `Write(byte[],int,int)`, `Write(ReadOnlySpan<byte>)`,
and `WriteByte`. `WriteAsync`, `CopyTo`, and `SetLength` would bypass the byte cap without error.

Nothing reaches those today — `Utf8JsonWriter` uses the covered paths — so this is a trap for a
future edit rather than a live defect. Sealing the gap (override the rest to throw, or wrap instead
of inherit) makes the limit structural rather than incidental.

## 7. Failed artifact writing leaves no trace

`SnapshotScope.TryWriteArtifacts` swallows every exception, correctly: best-effort diagnostics must
never mask the real test failure. But on failure `ArtifactDirectory` stays `null` and the report
simply omits the "Received files" line, so the user gets no artifacts *and no reason*.

A single line in the report — "failure artifacts could not be written" — would keep the
non-masking property while ending the silence.

## 8. Value types are boxed once per node during capture

`SnapshotWriteContext.Enter(object? value)` takes `object?`, so every `int`, `Guid`, `DateTime`, and
struct captured allocates a box purely to participate in reference-cycle tracking. A 100,000-node
snapshot allocates 100,000 boxes that can never be cycles.

Test-time only and never hot, so this is a note rather than a defect. If touched, the fix is to skip
cycle tracking for value types — they cannot form reference cycles — which removes both the
allocation and a pointless `HashSet` probe.

## 9. Smaller notes

- `SnapshotStore.Claims` is a process-lifetime `ConcurrentDictionary` that never shrinks; one entry
  per distinct test directory. Fine for a test host, worth knowing for a long-lived one.
- `SnapshotStore.EnsureSafeDirectory` compares the storage-root prefix with `OrdinalIgnoreCase`
  while the traversal check uses `Ordinal`. On a case-sensitive filesystem two directories differing
  only in case could be treated as one. Contrived, low severity, but the two comparisons should
  agree deliberately rather than by accident.
- `EffectiveSettings` is near-pure state living at `Configuration/` root while `ProjectConfiguration`
  sits correctly in `Configuration/Models/`. It does carry `ResolveUpdate` and `DisplayName`, so
  whether it is a model is genuinely arguable — but the inconsistency is worth a decision.
- `PortableNames.Segment` calls `safe.Split('.')[0]` to test the reserved-device list, allocating an
  array to read one span.

## Not defects — recorded so they are not "found" again

- **`CheckLink` is TOCTOU.** Checked then used. Already disclaimed in `docs/DESIGN.md`; closing it
  would require handle-based APIs throughout and is out of proportion to a test-fixture tree.
- **`SnapshotWriters` is mutable global state.** Inherent to a registry. The atomics are correct
  (`Interlocked.Exchange` / `CompareExchange`, `Volatile.Read`) and the ordering constraint is
  documented on `Register`.
- **`ReadText` re-checks length after reading.** The `FileInfo.Length` stat is advisory; the
  post-read check is the real guard, and it is present.
