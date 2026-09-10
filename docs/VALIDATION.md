# Validation

Executed on September 11, 2026, on Windows x64 with .NET SDK 10.0.101. All runtime projects target .NET 10. The source generator targets netstandard2.0 because it runs inside the compiler.

## Results

| Check | Result |
| --- | --- |
| Release build | Passed, zero warnings and errors |
| xUnit runtime, examples, and tool tests | 88 passed |
| Roslyn generator regression tests | 22 passed |
| NUnit integration | 4 passed |
| MSTest integration | 5 passed |
| Total managed tests | **119 passed, zero failed or skipped** |
| Same tests consuming the NuGet package from a fresh cache | **119 passed** |
| Native AOT publish of the package consumer, win-x64 | Passed with AOT/linker warnings treated as errors |
| Native executable specifications | **76 passed**, `RuntimeFeature.IsDynamicCodeSupported == false` |
| Local .NET tool package installation | Passed |
| Installed tool running real framework examples with read-only enforcement | **14 passed** |
| NuGet archive inspection | Correct .NET 10 runtime, core dependency, compiler-only analyzer, and transitive build props |
| Product source scan | No Python, PowerShell, Bash, batch, or command-script files |

The managed runtime tests include all 76 executable specifications. They are not additional distinct tests beyond that suite. The package-consumer run switches library references to the actual package with `UsePackageReferences=true`; only the compiler tests deliberately retain a development reference to the generator being tested, and tool tests launch the built tool. The installed tool was also tested separately from its packed artifact.

## Evidence in this workspace

- [Managed test results](../artifacts/final-test-results/): VSTest TRX files for all four test projects.
- [Fresh-cache package-consumer results](../artifacts/final-package-test-results/): TRX files for all four projects.
- [Native publish log](../artifacts/logs/native-publish.log).
- [Native execution log](../artifacts/logs/native-tests.log).
- [Installed-tool integration results](../artifacts/tool-test-results/).
- [Generated NuGet packages](../artifacts/packages/).

Build outputs and logs are intentionally excluded from source control and can be recreated with the [README commands](../README.md#build-and-verify).

## Coverage

The runtime suite tests immediate capture; named and inferred files; anonymous and named contracts; enums, nullable values, tuples, collections and multidimensional arrays; JSON/text equality; exact numeric comparison; unordered array matching; missing/changed/unused baselines; update precedence; abort/cancellation; CI/read-only enforcement; transactional approval; journal recovery and corrupt-backup refusal; ownership collisions; compare-and-swap conflicts; concurrent async captures; and competing writers in separate OS processes.

The additional tests verify bounded contextual diffs, JSON member paths, original getter errors, format-change artifacts, Unicode and reserved filenames, and byte-for-byte preservation during verification. Tool process tests exercise literal arguments including spaces, quotes and shell metacharacters, exit-code propagation, inherited selection clearing, invalid arguments, and missing executables.

Compiler tests actually compile generated C# for supported shapes and exercise unsupported-type diagnostics, explicit writer escape hatches, rooted generic helpers, framework metadata, custom derived attributes, enum aliases, method case requirements, and writer changes following member edits. Legacy MSTest display-name constructor support is tested with a compiler fixture; the real framework integration uses MSTest 4.4.0's current DisplayName property and Description attribute.

## Platform limits

Windows x64 is the platform executed in this workspace. A [GitHub Actions matrix](../.github/workflows/ci.yml) is provided for Windows x64, Linux x64, and macOS arm64; its remote runs have not been executed here. The runner labels match the [official runner image inventory](https://github.com/actions/runner-images#available-images).

The complete xUnit/NUnit/MSTest runners were executed as managed tests. The independent explicit-registration harness establishes Native AOT support for the snapshot library and its package. This does not assert that every third-party runner or application dependency supports AOT.

Storage concurrency tests use cooperative local filesystems. Recovery tests include constructed interrupted journals and a real competing-process update; they do not establish arbitrary power-loss or distributed-filesystem guarantees.

## Delivered package integrity

Version: `0.1.0-preview.1`. These are local artifacts, not publicly published releases. SHA-256:

```text
4D708BB2BEBC1C80191D86FF89C239A184A7670E0B7FA3A65BB446DA4964BAE5  TheLithium.Imprint.0.1.0-preview.1.nupkg
709FE926BBFB3AEC62F568A98D15D6E4F893E270B8CDD5F15F3636EFCEC43519  TheLithium.Imprint.Core.0.1.0-preview.1.nupkg
7A83DA8F3E9C7E1574150ADABF456279F23192FBEAA1A02BCAA59DCA4AB70764  TheLithium.Imprint.Tool.0.1.0-preview.1.nupkg
```
