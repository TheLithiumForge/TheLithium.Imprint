# Validation

The repository is developed and validated with the .NET 10 SDK. Build and test output is redirected to the root artifacts directory by Directory.Build.props.

## Checks

The current managed suite covers:

- runtime storage, comparison, formatting, limits, recovery, conflict detection, and failure artifacts;
- ordinary xUnit test bodies with multiple captures, async and ValueTask methods, exceptions, awaited finally blocks, and parameterized data;
- compile-time writer generation and diagnostics;
- real NUnit and MSTest test methods;
- the explicit-registration executable specification harness used for Native AOT.

Run the complete managed suite with:

~~~text
dotnet build TheLithium.Imprint.slnx -c Release
dotnet test TheLithium.Imprint.slnx -c Release
dotnet run --project tests/TheLithium.Imprint.Specifications -c Release -- --expect-managed
~~~

The package project can be packed and consumed by the test projects with:

~~~text
dotnet pack TheLithium.Imprint.slnx -c Release
dotnet restore TheLithium.Imprint.slnx -p:UsePackageReferences=true -p:RestorePackagesPath=artifacts/nuget/package-consumer --force-evaluate
dotnet test TheLithium.Imprint.slnx -c Release -p:UsePackageReferences=true -p:RestorePackagesPath=artifacts/nuget/package-consumer --no-restore
~~~

The package-consumer run must use a local package source containing the version produced by dotnet pack. It exercises the bundled analyzer and transitive build task instead of project references. Restore the default project-reference mode before switching back to ordinary development builds.

Native AOT validation uses the executable specification project:

~~~text
dotnet publish tests/TheLithium.Imprint.Specifications -c Release -r win-x64 -p:UsePackageReferences=true -p:RestorePackagesPath=artifacts/nuget/package-consumer -p:PublishAot=true -p:SelfContained=true -o artifacts/native/win-x64
./artifacts/native/win-x64/TheLithium.Imprint.Specifications.exe --expect-aot
~~~

The executable checks RuntimeFeature.IsDynamicCodeSupported and runs the explicitly registered specification list. External test frameworks and applications still need their own Native AOT support.

The `build-test.yml` workflow runs these managed checks on Windows, Ubuntu, and macOS, then repeats the package-consumer checks and publishes the executable specification project as Native AOT for each platform. The xUnit, NUnit, and MSTest adapters are intentionally tested as managed hosts; the executable specification project is the AOT lane.

## Review evidence

Generated binaries, NuGet archives, test logs, and Native AOT output live below artifacts and are ignored by source control. Baselines under __snapshots__ are the reviewable fixtures. No generated output or transient transaction data is required to understand a change.

The tests deliberately cover the important approval invariant: a missing snapshot passes and is staged, a changed snapshot fails under the default Missing policy, and no staged update is committed when the method or its awaited finally block throws.

## Known limits

The automatic lifetime depends on a standard Microsoft.NET.Sdk compilation. Methods with ref, in, out, iterator, async void, async custom awaitable, by-ref return, or struct declarations are rejected because the build task cannot safely wrap their completion. Framework teardown that runs after the test method is outside the automatic boundary and must be included in a custom adapter boundary when it should affect approval.

The filesystem store coordinates cooperative local processes with locks, journals, and optimistic fingerprints. It does not claim distributed-filesystem transactions or protection from hostile code changing paths concurrently. The library intentionally has no command-line updater, no global orphan-pruning pass, and no runtime reflection-based discovery.
