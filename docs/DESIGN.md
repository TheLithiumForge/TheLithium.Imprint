# Design and implementation map

## Test lifetime

The package build target parses the test project during compilation and replaces only the compile input for methods that directly or indirectly reach AssertSnapshot or UpdateSnapshot. The replacement calls a small runtime runner around a local function containing the original body. This keeps the test method's public signature and ordinary runner flow while allowing completion after a successful return. The automatic wrapper accepts Task and ValueTask return shapes; async custom awaitables are rejected rather than completed early.

Captures serialize immediately into memory. Complete compares the complete set, produces structured entry results, and commits the staged set only when every entry is authorized. A missing entry is authorized by the default Missing policy. A changed or unused entry requires All. Any capture error or method exception aborts the scope and leaves the baseline untouched. Awaited work and finally blocks inside the method are part of the boundary; runner teardown after the method is not.

The runtime uses AsyncLocal for nested helper calls. A helper that only captures uses the active scope. A helper that explicitly opens a boundary can use Snapshots.Begin with a supplied SnapshotTestIdentity, and explicit callers can still use Snapshots.Run or Snapshots.RunAsync.

## Compile-time serialization and identity

The incremental generator inspects semantic symbols and emits direct SnapshotWriter<T> delegates. It also emits source metadata for suite, method, display metadata, update attributes, source locations, and the project artifact root. Module initializers register the generated writers and metadata. Runtime code does not discover assemblies, methods, properties, attributes, or runtime-derived types.

The generated build task derives a case discriminator from actual method arguments. Canonical argument JSON provides the stable hash; a bounded readable prefix makes the folder useful during review. The case folder identifies one parameterized invocation, while an AssertSnapshot name identifies one file inside that invocation. CancellationToken is excluded from the case key because it represents execution control rather than test data.

Source metadata uses the original file and method line range only to select the generated descriptor for a call. It is not persisted as snapshot identity. An explicit identity is required for linked source files outside the project or for a custom runner that cannot use the build integration.

## Formats and comparison

Each capture is one file: .json for structured JSON, .txt for plain text by default, or .snap when selected by configuration or an entry option. Existing files with more than one extension for the same capture name are rejected as ambiguous. JSON is canonicalized for deterministic output and compared structurally. Numeric tolerance uses bounded decimal arithmetic, and unordered arrays preserve duplicate counts through matching. Text comparison can normalize line endings, case, and trailing whitespace according to options.

The static declared type is the serialization contract. Generated writers cover supported public contracts, and explicit typed writers cover private or specialized values. No reflective serializer or ToString fallback is used.

## Filesystem safety and transactions

The baseline path is:

    snapshotsFolder / relative source directory / suite / test [case] [variant] / capture.extension

Segments are normalized, bounded, reserved device names are escaped, and case-insensitive filename collisions are rejected. Paths are checked for traversal and reparse points. This protects ordinary repository use; it is not a guarantee against malicious code racing a filesystem path.

The store keeps locks, journals, and failure artifacts below the project artifacts directory, outside the reviewed baseline tree. A process takes a cross-process file lock, reads and fingerprints the current files, writes a before and after journal, flushes a prepared marker, applies the desired files, and records a committed marker. A later invocation recovers an interrupted prepared journal after verifying its backup fingerprint. Optimistic fingerprints prevent a second process from silently overwriting a baseline changed during the test.

Individual files are replaced atomically where the host filesystem supports it. The whole directory is not changed by one atomic filesystem operation. Recovery covers cooperative local processes and ordinary interruption; distributed filesystems, arbitrary power loss, and hostile concurrent edits are outside the guarantee.

## Configuration and packaging

Configuration is parsed with JsonDocument and strict property checks. Project configuration controls update policy, the baseline directory name, optional display-name preference, text extension, comparison defaults, limits, and the artifact directory. Environment overrides can enforce read-only or select a run policy for build systems, but no command-line tool is required.

The main NuGet package contains the .NET 10 runtime dependency, the compiler analyzer, and the MSBuild task payload under buildTransitive. The task is used at compile time and is not a runtime dependency. The runtime project is AOT-compatible and contains no reflection-based discovery. Build output and generated sources are redirected to ./artifacts by Directory.Build.props.

## Deliberate boundaries

The library does not provide runner-specific adapters, a command-line updater, image or binary snapshot formats, global orphan pruning, a redaction pipeline, runtime polymorphic member discovery, or a distributed writable baseline store. These boundaries keep the ordinary test API small and the runtime suitable for Native AOT.
