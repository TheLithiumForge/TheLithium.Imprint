# Design and implementation map

## Boundary and state

Snapshots.Run/RunAsync create an ambient SnapshotScope. States are Open, Completing, Completed, Faulted, and Aborted. Capture serializes and materializes data immediately while holding the scope gate. Duplicate names, unsupported writers, limits, getter failures, and other capture errors poison approval even when caught by test code.

Complete compares the full set and builds structured entry results. Any unauthorized Missing/Changed/Unused result prevents all writes. Completed scopes cannot capture again. Dispose abandons and restores the previous ambient scope; it never verifies or approves. The original callback exception survives best-effort artifact failures.

Opening a scope reads and fingerprints its baseline. Completion verifies that fingerprint even when no changes are authorized/needed. Cooperating concurrent writers cannot silently overwrite each other's approvals.

## No runtime reflection

SnapshotGenerator inspects semantic symbols at compilation, emits SnapshotWriter<T> delegates with direct member access, and emits source-location-to-test descriptors. Module initializers directly register these writers and descriptors. The runtime uses generic static writer slots; it does not discover methods, properties, attributes, assemblies, or runtime code.

Ordinary source generators are additive. They do not rewrite user methods and this generator does not depend on a second generator consuming its output. The public wrapper is the portable lifecycle boundary rather than a fabricated universal test-runner hook.

Generated metadata uses call-site member source ranges to resolve the declaring method. Lines are lookup keys for this build, not persisted snapshot identity. A wrapper opened inside a shared helper needs an explicit caller identity. A helper that only captures should receive the active scope implicitly instead of opening another boundary.

## Formats and comparison

Each named capture has one .json, .snap, or .txt file. Ambiguous existing formats for the same name fail. All can authorize changing one entry's extension. No mixed container grammar is needed.

JSON output is canonical in key order and indentation. Arrays retain captured order. Comparison is structural and numeric equality does not first round through a double. Exact tolerance uses bounded BigInteger arithmetic. Unordered arrays use maximum bipartite matching to preserve multiplicities and avoid greedy errors with nontransitive tolerance.

There is no data filtering or transformation pipeline. Custom comparison is an equality contract on captured representations, not an object extraction framework. Different per-entry options never become defaults for subsequent captures.

## Storage ownership and transactions

Readable suite/test folders do not fully encode project, method signature, case, or variant. A sidecar owner identifies the logical owner and prevents overlapping tests from sharing data silently. Portable case-insensitive collision checks apply on every OS. Safe filename segments are bounded and hash-suffixed when normalization changes them.

The storage layer checks paths for traversal and reparse/symlink nodes within its root. This is not a sandbox against malicious code racing filesystem path changes.

Each transaction holds a local cross-process FileShare.None lock. It compares the current baseline fingerprint, stages before/after files, persists the previous owner and fingerprint, and flushes a prepared marker before mutating baselines. A committed marker records success. An interrupted prepared transaction restores its verified before set. Corrupt backups are preserved and rejected; read-only runs refuse recovery requiring writes.

Individual file replacements are atomic to the extent provided by the host filesystem. The whole directory is not claimed to change in one atomic filesystem operation. Recovery covers ordinary process interruption and cooperative local workers, not arbitrary power loss, network filesystem semantics, host crashes with lost directory metadata, or hostile concurrent edits. In-memory snapshots and per-test size limits bound normal resource use.

Artifact output contains received/expected data and an execution manifest outside the baseline root. There is no CLI accept-from-received action: stale or incomplete received files cannot be blindly approved by the supplied tool. Global orphan pruning is also not implemented; filtered tests do not establish which suites have been deleted.

## Configuration

The compiler publishes project source metadata via package-provided props. Users do not edit project properties for update modes, baseline locations, or comparison choices. Runtime project settings are read manually from strict JSON using JsonDocument, not an object serializer with reflection defaults.

Root locations resolve from explicit options/config, otherwise source-adjacent __snapshots__. Artifact locations resolve from the project root. IMPRINT_PROJECT_ROOT remaps a checkout for a deployed executable on the same path-syntax platform; explicit identity covers other host arrangements. No current-working-directory fallback creates accidental baseline trees.

CI/read-only policy overrides everything. A run override can force verification over committed All attributes, or authorize a selected set while forcing nonmatches to verify.

## Files and packages

- src/TheLithium.Imprint.Core: runtime contracts, scope, static writer registry, encoding, comparison, settings, filesystem store.
- src/TheLithium.Imprint: NuGet packaging, bundled generator, and automatic consumer build metadata.
- src/TheLithium.Imprint.Generator: incremental generator and unsupported-type diagnostics, bundled in the main package.
- src/TheLithium.Imprint.Tool: optional process wrapper and environment forwarding.
- tests/TheLithium.Imprint.Specifications: explicitly registered executable specifications, including generator-dependent captures and Native AOT execution guard.
- tests/TheLithium.Imprint.Tests: xUnit execution of the specification suite, checked-in snapshot examples, and process-level tool tests.
- tests/TheLithium.Imprint.Generator.Tests: generated-source compilation, unsupported-type diagnostics, and naming metadata tests.
- tests/TheLithium.Imprint.NUnit.Tests and tests/TheLithium.Imprint.MSTest.Tests: real framework integration and checked-in baselines.

Normal development uses ProjectReference boundaries and standard dotnet build/test/pack commands. The main NuGet project packages its compiler-only generator with a dependency on the Core package. Tests can switch to the packed library with UsePackageReferences=true, verifying its bundled analyzer and transitive build metadata. No bootstrap scripts are required.

## Scope exclusions

No automatic runner adapters, live IDE acceptance UI, image/binary/directory snapshot feature, global orphan-pruning engine, snapshot redaction pipeline, runtime polymorphic member discovery, distributed writable baseline store, or claim of external framework AOT support is made. Users can snapshot a directory listing by preparing an ordinary DTO/array themselves.
