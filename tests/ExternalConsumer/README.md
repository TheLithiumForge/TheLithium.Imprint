# Independent package consumers

These projects are templates for verification outside Imprint's repository. Run the script from the repository root after packing:

```bash
dotnet pack TheLithium.Imprint.slnx -c Release
node tests/ExternalConsumer/verify-package-consumers.ts
```

One script covers Windows, Linux and macOS. It needs the .NET SDK selected by global.json and Node 24, which runs TypeScript directly, so there is nothing to compile or install. Native AOT additionally requires the platform's native compiler and linker; on Windows the runner adds the Visual Studio Installer discovery directory if present, preserving the inherited SDK search path regardless of whether Windows names it `Path` or `PATH`. The Visual Studio C++ build tools must already be installed.

Run the environment regression checks with `node --test tests/ExternalConsumer/environment.test.ts`. On Windows this includes launching `dotnet` from a temporary directory with a mixed-case `Path` environment. The managed CI matrix runs these alongside the release-script tests.

| Option | Flag |
| --- | --- |
| Skip native publication | `--managed-only` |
| Read packages from another directory | `--package-directory DIR` |
| Use a new directory outside the repository | `--output-root DIR` |

Without an output path, the runner creates a unique temporary directory and prints its location. It retains logs, TRX reports, snapshots, native binaries and package hashes for inspection. CI runs the same script on Windows, Linux and macOS.

The runner copies the templates before compiling. Each copy has its own NuGet configuration, package cache and build settings. It consumes only the freshly packed main/Core packages, with no project references, shared implementation tests, internals access or repository Directory.Build imports. Do not add these templates to the main solution. The negative project intentionally cannot build, and an in-repository build would defeat this check.

## Execution matrix

| Consumer | Managed | Native AOT | Contract |
| --- | --- | --- | --- |
| Full | Yes | Yes | 17 independently checked scenario groups; normal generated lifetimes and explicit runners |
| CoreOnly | Yes | Yes | Explicit sync/async integration, primitive/custom writers, rejection of missing writers/identity |
| Xunit, NUnit, MSTest | Five tests each, twice | No | Ordinary package-instrumented test methods, parameterized cases, awaited methods, scope configuration, attributes and indirect capture; create, then verify in another test process |
| Negative | Compile-time only | Same compiler contract | IMP001 unsupported object, IMP102 async void, CS0619 asynchronous callback passed to Run |

Executables assert `RuntimeFeature.IsDynamicCodeSupported` for the requested execution mode. Native publication treats compiler, trimming and AOT warnings as errors. Framework runs require five passing TRX results and five baseline files each, preventing empty test discovery from passing. Expected compiler failures are consumed explicitly and do not become a false CI failure exit code.

## API and configuration coverage

The source of the behavioral assertions is [Full/Scenarios.cs](Full/Scenarios.cs). Scenarios compare actual files, reports, return values and failures rather than only checking that a call does not throw.

| Surface | Scenario groups |
| --- | --- |
| AssertSnapshot / UpdateSnapshot, default and explicit writers, inferred/explicit names | DefaultAndAllAssertionOverloads; CustomWriterAndRooting |
| Auto / Json / Text, null, multiline text, application-serialized JSON, all StringContent layers | FormatsAndApplicationJson; UnsafeConfigurationAndCapture |
| Representation categories, nested composition, project/test/assertion precedence, scope isolation | RepresentationScopes; AsyncScopesAndIsolation |
| Every comparison field, field inheritance/reset, custom comparer, mathematical JSON numbers, unordered multiplicities, text normalization, contextual diff | ComparisonLayers; ComparisonBehavior |
| Default Missing, project/test/current/assertion update policy, UpdateCurrentTest, all-or-nothing authorization, unused removal | UpdateLayersAndWholeSetAuthorization; ArtifactFailureAndUnusedEntries |
| IMPRINT_UPDATE, CI, IMPRINT_PROJECT_ROOT, invalid values and precedence | EnvironmentPolicies; OrdinaryGeneratedLifetimes |
| ConfigurationFile, snapshot folder/root, artifact path, test Name/Suite/Case/Variant/Identity, root overrides, naming policies | NamesPathsAndIdentity; NamingModes; OrdinaryGeneratedLifetimes |
| Project/test limits, cancellation, empty-test policy, malformed/duplicate/unknown/null/invalid configuration | LimitsAndInvalidConfiguration; UnsafeConfigurationAndCapture |
| Begin / Run / RunAsync / Current, Complete / Abort / Dispose, nested and concurrent scopes, capture poisoning | ScopesReportsAndArtifacts; AsyncScopesAndIsolation; UnsafeConfigurationAndCapture |
| Reports/statuses/fingerprint, received/expected files, run.json, secondary artifact error preserving mismatch/baselines | ScopesReportsAndArtifacts; ArtifactFailureAndUnusedEntries |
| Register / both TryRegister overloads / Write, SnapshotWriteContext.Path / At / Representation, SnapshotInclude, SnapshotCases | CustomWriterAndRooting; RepresentationScopes |
| Class/method SnapshotSettings, inferred source identity, parameter cases, display names, indirect capture, finally failure | OrdinaryGeneratedLifetimes; three framework consumers |
| Generated void, value, Task, Task<T>, ValueTask, ValueTask<T> lifetimes; anonymous/container/tuple/multidimensional shapes | OrdinaryGeneratedLifetimes; AnonymousShapes |

Generated metadata and execution entry points hidden from IntelliSense are exercised through real generated calls. The explicit Full dispatcher files opt out of instrumentation to avoid opening a second lifetime around their test harness; OrdinaryTests.cs and the three framework projects use the package defaults.

This is a representative contract matrix, not a claim to test every possible combination or arbitrary user callback. Existing repository tests retain deeper adversarial storage, concurrency, generation and security cases. Windows and Linux execution do not prove macOS behavior; the CI matrix runs this runner on all three platforms.
