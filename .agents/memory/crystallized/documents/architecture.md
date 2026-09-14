# Imprint architecture

Current implementation responsibilities and guarantees. The [API overview](../../../../docs/API.md) describes the public surface and configuration layers; [development](development.md) covers building, testing and releases.

## A1. Projects and dependency boundaries

| Owner | Responsibility | Consumer boundary |
| --- | --- | --- |
| `src/TheLithium.Imprint.Core` | Configuration, execution, serialization runtime, comparison, storage, reports | net10.0 runtime package; AOT-compatible |
| `src/TheLithium.Imprint.Generator` | Incremental Roslyn generator for writers and source identity; IMP001 | netstandard2.0 analyzer in the main package |
| `src/TheLithium.Imprint.Build` | Roslyn/MSBuild compile-input rewriting and IMP102 | netstandard2.0 build task in the main package |
| `src/TheLithium.Imprint` | Main package assembly layout, schema, README, analyzer and buildTransitive integration | Main package depends on Core |

The build task and generator execute at compilation, not in the test binary. Core uses System.Text.Json, standard streams/files, SHA256, concurrent collections and AsyncLocal. Production build dependencies are Microsoft packages. xUnit/NUnit are test dependencies, not production serializer dependencies. C# 14 and .NET 10 are selected in build properties; the SDK baseline is global.json.

## A2. Public and generated call surfaces

The [API overview](../../../../docs/API.md) lists ordinary test calls, configuration by scope, writer/comparer extensions, explicit runner lifetimes, reports and generated support APIs.

The obsolete-error `Run(Func<Task>)` overload is a misuse guard against async-void binding, not an executable compatibility path. Generated entry points (`TestExecution`, `SnapshotMetadata`, descriptor records, `SnapshotCases`, `Shapes`, try-registration overloads) are public for cross-assembly generated calls; they are not the ordinary user workflow.

## A3. Compile-time lifetime

`InstrumentSnapshotTests` parses compile inputs with Roslyn, constructs a compilation and asks `SnapshotCallGraph` for methods reaching captures. Calls are propagated outward to callers. Explicit boundaries suppress automatic wrapping. Compile item metadata `ImprintInstrument=false` opts a source out; design-time builds do not run the rewriting target.

The original body becomes a local function, invoked through `TestExecution`. Original source files are not changed. Replacements live under IntermediateOutputPath; `#line` and caller-information preservation keep diagnostics and caller names meaningful. Generic/parameter data is incorporated into generated case identity, excluding cancellation tokens.

Supported return shapes: void, T, Task, Task<T>, ValueTask, ValueTask<T>. Iterator bodies, ref/in/out parameters, by-ref returns, struct test containers, async void and unsupported async awaitables are rejected by IMP102. Wrappers reuse an active scope for helpers; explicit nested scopes restore their parent through AsyncLocal.

The boundary includes awaited body work and finally blocks inside the method. Framework cleanup after return is outside it. Unawaited work cannot extend completion. The generator reads class policy/name metadata. A direct method SnapshotSettings attribute can establish a lifetime even when no visible assertion seeds the call graph; a class attribute supplies metadata to discovered methods and is not a general seed for every method in the class.

## A4. Identity and portable naming

Generated metadata records project root/name, source path and line interval, suite, method, display metadata, update policy, parameterization and build artifact root. Caller source location selects a descriptor; line numbers do not become persisted identity. Ambiguous equal-sized descriptor matches fail.

Explicit SnapshotTestIdentity supplies project/source/suite/test plus case, variant and logical ID. IMPRINT_PROJECT_ROOT relocates checkout identity. The source path is project-relative for directory construction. Snapshot roots may be explicitly configured; artifact roots must be outside the baseline tree.

Path shape:

```text
<baseline root>/<source directory relative to project>/<suite>/<test [case] [variant]>/<capture>.<extension>
```

PortableNames normalizes Unicode, escapes forbidden/control characters and Windows device names, bounds segments, and adds hash suffixes when rewriting could merge names. Explicit capture names must be unique case-insensitively. Automatic names infer simple variable/member expressions, then use numbered fallbacks. Folder display names are opt-in; dynamic row display text is not identity.

SnapshotCases renders arguments as canonical JSON and a readable label. Truncation, ambiguous scalar labels, separators and complex values require a hash. Shape helpers permit anonymous values in supported generic containers without runtime reflection. Claims detect identity collisions within a process; locks/fingerprints coordinate separate processes.

## A5. Configuration and precedence

SnapshotTestOptions owns identity overrides (Name, Suite, Case, Variant, Identity), paths (RootDirectory, ArtifactDirectory, ConfigurationFile), Update, Comparison, Representation, StringContent, Naming, AllowEmpty, three capture limits and CancellationToken. SnapshotOptions owns Update, Format, StringContent, Representation, Comparison and Comparer for one capture.

ProjectConfigurationReader reads optional snapshots.config.json through the bounded UTF-8 reader and StrictJson, then deserializes with ProjectConfigurationJsonContext. Groups: files, naming, comparison, representation and limits; top-level version, update, stringContent, allowEmptyTests and $schema. Unknown and duplicate properties, integer enum values and invalid product constraints fail. Private mutable input classes preserve source-generated default initialization, then map immediately into immutable domain settings. Absent failureArtifactPath preserves build-provided defaults; explicit null is rejected. Legacy names are not recognized. The shipped versioned JSON Schema assists editors; schema tests compare its keys with generated JSON metadata. No runtime schema-validation dependency is needed.

For update policy, explicit IMPRINT_UPDATE wins. Without it, CI=true/1 imposes run-wide Verify. Otherwise an entry override wins over current/test policy, which resolves explicit test options, method/class metadata, project config, then Missing. Only whole-test All prunes unused entries. Ordinary Verify can be overridden at a narrower scope; run-wide Verify cannot and refuses interrupted recovery. Locks and diagnostics can still write during verification.

The supported environment inputs are IMPRINT_UPDATE, IMPRINT_PROJECT_ROOT and CI. Comparison and representation patches resolve field by field from defaults through project, test and assertion. Null means inherit; explicit false/zero overrides. ResolvedSnapshotComparison and ResolvedSnapshotRepresentation own concrete values; custom comparers and writers receive these non-nullable types. Scope setters accept patches only before the first capture.

Default comparison: exact numbers, ordered arrays, ordinal case-sensitive strings/property names, ignored text line endings, significant trailing spaces/tabs, unordered-array limit 256. Negative tolerance and invalid bounds fail. Format is Auto/Json/Text. Auto chooses .txt for a non-null root string without an explicit writer and .json otherwise, without inspecting content. Explicit Json captures an ordinary string as a JSON string value. StringContent.Json explicitly accepts a non-null supplied JSON document with Auto/Json and no explicit writer; Text or non-string input is contradictory and fails. Null normally produces JSON null; Text requires a non-null string. Naming defaults to NameThenOrder; framework display names are off; empty tests fail.

SnapshotRepresentationOptions holds nullable enum, byte-array and dictionary preferences. Preferences resolve once per capture, by category, from defaults through project, test and assertion. Both scope Representation and Comparison getters return effective fields; setters apply partial overrides before capture. Every descendant writer reads the same SnapshotWriteContext.Representation. Reusable options are immutable records copied with C# `with` expressions. Case-key serialization uses independent fixed defaults, so display choices cannot rename parameterized tests.

## A6. Serialization and representations

SnapshotGenerator.cs discovers identities and orchestrates generation; SnapshotGenerator.Writers.cs emits direct typed writers and inferred shapes. Module initializers register these writers. Writers are selected by the statically declared type through generic slots. Explicit registrations win over generated try-registration. No member/assembly discovery or ToString fallback occurs at runtime.

Built-ins cover strings/chars/booleans, integral/floating/decimal numeric types (including native integers, Half, Int128, UInt128 and BigInteger), GUIDs, temporal values, URI, byte arrays and JSON DOM types. Scalars use BCL/Utf8JsonWriter APIs. Enums default to exact declared names with numeric fallback, or can always use the underlying number. Byte arrays default to numeric JSON arrays and optionally use base64 strings. Public fields and readable public properties are captured independently of application JSON serialization attributes.

Generated shapes cover accessible classes/records/structs, declared interfaces/base contracts, nullable types, tuples, KeyValuePair, rank 1-3 arrays and unambiguous IEnumerable<T>. String-key dictionaries default to objects and can use Key/Value entry arrays; other dictionaries stay entry arrays. Sets retain enumeration order. Anonymous container inference has an explicit supported set, including nested dictionary entries without naming anonymous generic arguments. Object/dynamic runtime shapes, inaccessible named types, pointers/ref-like values, opaque framework types and ambiguous enumerables require projection or an explicit writer. Unsupported generation reports IMP001; an unregistered runtime writer fails clearly.

Capture is eager and serializes into an append-only bounded stream composed around MemoryStream. Context tracks reference cycles, paths, depth, cancellation and visited values. Its internal generic Enter<T> avoids boxing statically declared value types solely for reference tracking. All values still consume node/depth budgets and observe cancellation. Reference-valued descendants enter separately; reference-typed contracts retain boxed-object identity tracking. Shared references are valid when not active cycles. Disposable frames remain idempotent reference objects. Null bypasses user writers and emits JSON null. StrictJson owns System.Text.Json parsing with duplicate properties, comments and trailing commas disabled. Generated values are key-sorted, indented and LF-terminated. Text remains literal UTF-8. Explicit supplied JSON strings are strictly validated and retained as supplied; application-specific serializer attributes are handled by the application's serializer before capture. Custom writers own their representations but their output still passes strict JSON validation.

Capture limits: default depth 64 (max 256), values 100,000 (max 10,000,000), output 4 MiB (max 256 MiB), 1,024 captures, combined test bytes max(128 MiB, per-file limit). Configuration input cap is 1 MiB and depth 16; lock timeout defaults to 10 seconds (range 1-300). Every JSON result, including supplied strings, DOM and custom writers, counts emitted containers and scalar nodes; property names do not count separately. Generated traversal has an independent early counter with the same limit, never added to emitted nodes. User delegates/getters remain trusted executable code. Cancellation surrounds external execution/parsing and participates in owned traversal, canonicalization, comparison, reads and lock waits; it cannot preempt arbitrary user code.

## A7. Execution and approval

Scope state progresses Open -> Completing -> Completed or Faulted; abandonment becomes Aborted. A lock protects scope capture/completion state. Baselines are read and fingerprinted when the scope starts. A capture error permanently poisons approval, even if caught by the test.

Complete asks SnapshotPlan to evaluate the capture set. One SnapshotDecision list owns missing/changed/matched/unused entries, authorization, approved status and desired files. The scope owns state, transaction execution and reporting. No unauthorized partial update commits. Missing approves absent entries only; All can replace values even when relaxed equality considers them equal. Whole-test All handles removals. Empty-test approval requires AllowEmpty. If no writes are needed, fingerprint verification still detects concurrent changes.

Failure passes a cohesive ArtifactRequest to received/expected artifact and run.json rendering. Mismatch exposes SnapshotReport; aborted execution attaches an artifact path to the original exception when possible. Artifact write failures appear in SnapshotReport.ArtifactError/the mismatch message or the original exception's Data under TheLithium.Imprint.ArtifactError. Diagnostic failure never replaces the primary exception. Dispose never approves; it aborts an incomplete scope and restores its predecessor.

## A8. Comparison and diagnostic output

DefaultSnapshotComparer normalizes text according to policy or consumes StrictJson documents before recursive comparison. JSON objects compare independent of property order; arrays compare positionally by default; names remain case-sensitive even when string values ignore case. Exact numeric leaves use JsonElement.DeepEquals, treating 1 and 1.0 as equal without floating-point conversion. Explicit exponent fields are bounded to ±1,000,000 before exact comparison. Nonzero decimal tolerance retains bounded digit/exponent and BigInteger arithmetic (4,096 digits and 20,000 scale expansion). Unordered arrays use maximum bipartite matching because approximate equality is not transitive; repeated elements retain their multiplicity. A 2,000,000-step counter limits recursion/matching. No redundant whole-document equality pass is added.

SnapshotDiff implements bounded contextual line diffs with a limited quadratic matrix and a fallback for large changes. Excerpts/line counts/output lengths are bounded. JSON failures include a property/index path and format already-parsed documents for contextual display, so minified input yields readable changed-property lines. Display formatting has a 1 MiB cap and a surrogate-safe 16,000-character fallback with a truncation notice. Comparison never transforms the stored baseline as a side effect.

## A9. Storage and trust boundary

SnapshotStore owns per-test locks, stable fingerprints and a rollback journal under project build artifacts. Read recovers first; enforced verification refuses prepared recovery. Commit verifies the original fingerprint, writes before copies and their fingerprint, validates desired filenames, flushes the prepared marker, applies desired files and writes committed. Failure attempts recovery. Cancellation is checked before mutation; an entered transaction finishes or rolls back. No redundant after copy is written; injected partial-apply failure and interrupted recovery prove the before copy is sufficient.

Files are individually replaced through a temporary sibling and rename. A directory update is not one atomic filesystem operation. Cooperative local processes and ordinary interruption are covered; hostile concurrent filesystem changes, distributed transactions and power-loss durability are not promised.

SnapshotPaths shares containment and existing-link checks across settings, store and artifacts. Baseline, artifact and recovery roots must be pairwise disjoint, including ancestor overlap. The build/explicit project anchor's ancestors are trusted; links at/below it are rejected. External paths are checked from the filesystem root. Verified macOS /tmp, /var and /etc aliases are canonicalized consistently for containment and traversal. These checks are cooperative rather than race-free against hostile concurrent mutation. SnapshotFileReader bounds reads during allocation, validates strict UTF-8 and checks cancellation. Configuration accepts optional UTF-8 BOM, not implicit UTF-16 detection. Journal marker reads are bounded. Claims and writer registries have process lifetime.

## A10. Verification and documentation ownership

The plain executable specifications run on managed .NET and Native AOT. Framework suites exercise xUnit, NUnit, MSTest; generator tests inspect generated code and diagnostics. Package-consumer tests catch missing analyzer/task/schema payloads. Workflows run managed and packaged AOT lanes on Windows, Linux and macOS. The release workflow reuses validation before publishing two packages.

Workflow actions are pinned to reviewed official commit references; NuGet auditing explicitly includes transitive dependencies for all target frameworks. CI artifacts retain test results, package fingerprints and platform-specific evidence. Native AOT and ordinary managed testing share the supported runtime contract. The production runtime has no third-party serializer dependency.

Consumer documentation belongs in README, API, TYPE-SUPPORT and FRAMEWORKS. Internal mechanisms live here; [development.md](development.md) owns build/package/release procedures. Keep these documents aligned with implemented behavior and store generated validation output under `artifacts/`.

Future implementation cycles should freeze this architecture as an input and record discoveries in their task slices before reconciling at closure. The original 1.0.0 input archive and renewed fingerprint manifest distinguish this implemented state from its starting point.
