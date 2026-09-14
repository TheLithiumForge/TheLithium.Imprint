# Development and verification

Current build, package and release procedures. Project responsibilities and invariants belong to [architecture](architecture.md); the [API overview](../../../../docs/API.md) describes supported calls and configuration.

Run commands from the repository root. Generated output and temporary verification evidence belong under `artifacts/`; committed test baselines remain in each project's `__snapshots__`. Do not approve changed fixtures without inspecting the intended behavioral difference.

## Managed development

```powershell
dotnet build TheLithium.Imprint.slnx -c Release
dotnet test TheLithium.Imprint.slnx -c Release --no-build --no-restore
dotnet run --project tests/TheLithium.Imprint.Specifications -c Release --no-build -- --expect-managed
dotnet format TheLithium.Imprint.slnx --verify-no-changes --no-restore
```

The specifications exercise the engine using an explicit executable test list. Framework projects exercise ordinary xUnit, NUnit and MSTest methods; generator tests check emitted code and diagnostics. The specifications run in both managed and Native AOT modes. The `--expect-managed`/`--expect-aot` switches verify the execution mode instead of merely trusting the publish command.

Run focused tests while implementing; the complete commands are the local release gate. Record pre-existing failures separately from regressions. Follow the repository [style](../../../directives/style.md) and [design](../../../directives/design.md) guidance.

## Package-consumer verification

Project references cannot prove that the published analyzer, build task and buildTransitive assets are packaged correctly. Pack, restore from the local feed, and run the same tests against packages:

```powershell
dotnet pack TheLithium.Imprint.slnx -c Release
$imprintConsumerCache = Join-Path (Get-Location) ('artifacts/nuget/consumer-' + [Guid]::NewGuid().ToString('N'))
dotnet restore TheLithium.Imprint.slnx -p:UsePackageReferences=true "-p:RestorePackagesPath=$imprintConsumerCache" --force-evaluate
dotnet test TheLithium.Imprint.slnx -c Release -p:UsePackageReferences=true "-p:RestorePackagesPath=$imprintConsumerCache" --no-restore
dotnet run --project tests/TheLithium.Imprint.Specifications -c Release -p:UsePackageReferences=true "-p:RestorePackagesPath=$imprintConsumerCache" -- --expect-managed
```

Packages land in `artifacts/packages`; `NuGet.Config` maps the local source for `TheLithium.Imprint*`. Use a fresh cache when repacking the same version so stale packages cannot pass the test. No deletion of the user's global NuGet cache is necessary.

Restore project-reference mode afterwards:

```powershell
dotnet restore TheLithium.Imprint.slnx --force-evaluate
```

## Native AOT verification

The [independent consumer runners](../../../../tests/ExternalConsumer/README.md) additionally copy standalone projects outside this repository and test fresh packages with isolated build settings and caches. After packing, run node tests/ExternalConsumer/verify-package-consumers.ts on any platform. It covers managed and Native AOT executables, Core-only integration, three managed test frameworks and expected compiler failures. The linked document owns the commands and API/configuration matrix. The CI package/AOT matrix also runs these checks in runner.temp and uploads their logs and package hashes.

After packing, use the same fresh package cache:

```powershell
dotnet publish tests/TheLithium.Imprint.Specifications -c Release -r win-x64 -p:UsePackageReferences=true "-p:RestorePackagesPath=$imprintConsumerCache" -p:PublishAot=true -p:SelfContained=true -p:IlcTreatWarningsAsErrors=true -p:ILLinkTreatWarningsAsErrors=true -o artifacts/native/win-x64
./artifacts/native/win-x64/TheLithium.Imprint.Specifications.exe --expect-aot
```

On Windows, native linking requires the Visual Studio native build prerequisites; `vswhere.exe` is normally under `C:\Program Files (x86)\Microsoft Visual Studio\Installer`. Use an appropriate developer environment when tool discovery fails. Do not suppress trimming/AOT warnings to obtain a passing result. Restore the normal project-reference mode after this lane as well.

## CI and releases

[build.yml](../../../../.github/workflows/build.yml), [test.yml](../../../../.github/workflows/test.yml) and [test-aot.yml](../../../../.github/workflows/test-aot.yml) independently run on pushes and pull requests, each across Windows, Ubuntu and macOS. Build checks compilation and formatting. Test runs the release-script tests and all framework/generator/specification tests. Test AOT restores locally packed packages, tests managed consumers and publishes/runs the specification executable as Native AOT. The README badges track each workflow's push runs on `main`. The former standalone Ubuntu framework jobs are covered by the full managed solution tests on every OS.

[build-test.yml](../../../../.github/workflows/build-test.yml) coordinates those three reusable workflows for releases and deliberate manual validation; it does not duplicate their normal push/PR runs. All three must pass before publication. Inspect the individual workflows for exact platform prerequisites and commands. The framework compatibility badges are separate from CI results: .NET 10 is the validated target, and .NET 11 preview has no dedicated validation lane yet. The NuGet badge links to releases without claiming that a package version has been published.

[release.yml](../../../../.github/workflows/release.yml) is the publication authority. Start it manually from `main` with a SemVer `version` input (an optional leading `v` is accepted). Prerelease versions such as `1.2.3-rc.1` automatically produce GitHub prereleases. Requests from other branches or tags cannot publish; pushes, including version changes and tags, do not start releases. The workflow validates the request before invoking the reusable cross-platform validation workflow, then verifies the requested packages, including Linux Native AOT, and publishes NuGet/GitHub assets from the selected main commit. NuGet trusted publishing uses the configured `NUGET_USER` and OIDC to obtain a short-lived key only after verification. Implementation approval does not authorize release dispatch, tags or package publication.

Release scripts run directly as TypeScript with Node 24. Their tests run in every managed OS lane and locally with `node --test .github/scripts/scripts.test.ts`.

## Configuration contract maintenance

Keep the active [versioned schema](../../../../schemas/1.0.0/snapshots.schema.json), configuration reader, `ConfigurationSchemaTests`, examples and the README/API configuration sections synchronized. Published breaking configuration changes need deliberate schema versioning. Document implemented behavior and keep local file links valid. Release validation must cover each supported platform; a local run does not substitute for the full CI matrix.
