# TheLithium.Imprint

[![Build and test](https://github.com/TheLithium/TheLithium.Imprint/actions/workflows/build-test.yml/badge.svg)](https://github.com/TheLithium/TheLithium.Imprint/actions/workflows/build-test.yml)
[![xUnit](https://github.com/TheLithium/TheLithium.Imprint/actions/workflows/build-test.yml/badge.svg?job=xunit)](https://github.com/TheLithium/TheLithium.Imprint/actions/workflows/build-test.yml)
[![NUnit](https://github.com/TheLithium/TheLithium.Imprint/actions/workflows/build-test.yml/badge.svg?job=nunit)](https://github.com/TheLithium/TheLithium.Imprint/actions/workflows/build-test.yml)
[![MSTest](https://github.com/TheLithium/TheLithium.Imprint/actions/workflows/build-test.yml/badge.svg?job=mstest)](https://github.com/TheLithium/TheLithium.Imprint/actions/workflows/build-test.yml)
[![Native AOT](https://github.com/TheLithium/TheLithium.Imprint/actions/workflows/build-test.yml/badge.svg?job=aot)](https://github.com/TheLithium/TheLithium.Imprint/actions/workflows/build-test.yml)
[![NuGet](https://img.shields.io/nuget/vpre/TheLithium.Imprint?logo=nuget&label=NuGet)](https://www.nuget.org/packages/TheLithium.Imprint)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)

TheLithium.Imprint is a snapshot testing library for .NET 10. It keeps the normal test flow, works with xUnit, NUnit, MSTest, and other runners that report uncaught .NET exceptions, and supports Native AOT through compile-time source generation.

It is designed around one small idea: compute a value in the test you already have, then assert it where it is meaningful. There is no callback runner to learn and no separate command required for ordinary updates.

**What it gives you**

- **Normal test bodies.** Call `AssertSnapshot()` or `UpdateSnapshot()` beside your existing assertions.
- **Safe first runs.** A missing snapshot passes by default and is written only after the test succeeds.
- **Stable identities.** Suite, method, display name, parameterized case, variant, and capture name are kept separate.
- **Useful failures.** JSON member paths, bounded unified diffs, and expected/received artifacts are available on mismatch.
- **AOT-friendly output.** Serialization is generated at compile time from declared types; the runtime does not discover members through reflection.
- **One package.** The main package carries the Core runtime, analyzer, and MSBuild integration. A Core-only package is available for custom hosts.

## Quick start

~~~csharp
using TheLithium.Imprint;
using Xunit;

public sealed class OrderTests
{
    [Fact]
    public async Task CreatesOrder()
    {
        var order = await CreateOrderAsync();

        order.AssertSnapshot();
        order.Items.AssertSnapshot("items");
        "Ready for dispatch".AssertSnapshot("status");

        Assert.NotEmpty(order.Items);
    }
}
~~~

There is no callback wrapper in the normal API. The package adds the snapshot lifetime to the compiled test method, so captures can be placed beside ordinary assertions and application code.

## Install

Add the package to the test project:

~~~text
dotnet add package TheLithium.Imprint --version 0.1.0-preview.1
~~~

~~~xml
<PackageReference Include="TheLithium.Imprint" Version="0.1.0-preview.1" />
~~~

The package brings the .NET 10 runtime through its TheLithium.Imprint.Core dependency, plus the source generator and build integration that supply test identity and method lifetimes. The compiler-only components are not runtime dependencies. A project using only TheLithium.Imprint.Core can provide its own identity and lifetime through Snapshots.Begin.

The package targets `net10.0`. The repository build uses the SDK selected by [global.json](global.json); applications only need the .NET 10 SDK that matches their normal build policy.

## CI coverage

The [GitHub Actions build matrix](.github/workflows/build-test.yml) runs on Windows, Ubuntu, and macOS. It has two explicit lanes so the runtime mode is visible in every run:

- **Managed lane:** builds the solution, verifies formatting, runs the xUnit, NUnit, and MSTest projects, and runs the executable specifications with `--expect-managed`.
- **Packaged AOT lane:** packs both NuGet projects, restores the test projects from that local package feed, runs the packaged tests in managed mode, publishes `tests/TheLithium.Imprint.Specifications` as Native AOT for the platform, and runs the resulting executable with `--expect-aot`.

The framework adapters remain managed test hosts, which is how their runners are designed to work. The executable specification project is the AOT test project: it exercises the same Core runtime and generated package integration without requiring a reflection-heavy test adapter. The separate framework jobs make each badge above independently meaningful.

| Surface | Project | Runtime mode |
| --- | --- | --- |
| xUnit integration and generator tests | `tests/TheLithium.Imprint.Tests` and `tests/TheLithium.Imprint.Generator.Tests` | Managed |
| NUnit integration | `tests/TheLithium.Imprint.NUnit.Tests` | Managed |
| MSTest integration | `tests/TheLithium.Imprint.MSTest.Tests` | Managed |
| End-to-end Core and package specifications | `tests/TheLithium.Imprint.Specifications` | Managed and Native AOT |

## Assert and update

AssertSnapshot captures immediately and compares the complete set after the test method returns successfully. Under the default Missing policy, a missing file is created and the test passes; Verify still requires every baseline. An existing difference produces a structured failure with an inline diff. Every capture in a method is considered together, so a later exception prevents all staged writes.

The method body includes awaited work and finally blocks that are part of that method:

~~~csharp
[Fact]
public async Task ReadsState()
{
    var before = await ReadAsync();
    before.AssertSnapshot("before");

    try
    {
        await MutateAsync();
    }
    finally
    {
        await RestoreAsync();
    }
}
~~~

Use UpdateSnapshot when one capture should be replaced after the method succeeds:

~~~csharp
var response = await SendAsync();
response.UpdateSnapshot("response");
~~~

For a complete method or suite, use an attribute while reviewing the resulting files:

~~~csharp
[SnapshotSettings(Update = SnapshotUpdate.All)]
public sealed class ContractTests
{
    [Fact]
    public void CurrentContract()
    {
        GetContract().AssertSnapshot("contract");
    }
}
~~~

SnapshotSettings with Update = All can be placed on one method or its class. The project setting "update": "all" updates every test in that project. Change the setting back to "missing" after reviewing the files. Updates are staged and committed only after the method succeeds; a failed test never leaves a partial approval behind.

UpdateSnapshot does not remove files that the method no longer captures. Whole-test All does remove unused files after successful completion. Read-only and CI policies can still forbid every write.

When a temporary review run needs a narrower update, set `IMPRINT_UPDATE=all` and optionally set `IMPRINT_TEST` to a glob. With the default method names, a selector such as `Orders.Create*` updates one method and its parameterized invocations; a suite selector such as `Orders.*` updates the suite; omit `IMPRINT_TEST` to update the whole project. With `preferDisplayNames` or `SnapshotSettings(Name = "...")`, select the resulting suite/test name shown in failure output. A nonmatching selector verifies every test, so a typo cannot approve snapshots accidentally. These are environment settings consumed by the normal `dotnet test` command; there is no separate Imprint CLI.

`IMPRINT_UPDATE=verify` and `IMPRINT_READ_ONLY=true` force verification. Continuous integration also verifies by default; opt into a deliberate CI update only with `IMPRINT_ALLOW_CI_UPDATE=true` and an update policy. `IMPRINT_CONFIG` selects another configuration file and `IMPRINT_PROJECT_ROOT` remaps a relocated checkout.

## Names, files, and parameterized tests

The default test folder is the C# method name. The suite is the containing type. SnapshotSettings(Name = "...") supplies an explicit suite or test name. A project can opt into a framework display name or description with "preferDisplayNames": true; the method name remains the fallback when no constant display metadata exists.

Parameterized tests are isolated automatically. The build integration derives a deterministic case discriminator from the actual method arguments, so InlineData, TestCase, DataRow, and similar data rows receive separate folders without any wrapper code:

~~~csharp
[Theory]
[InlineData("first", 1)]
[InlineData("second", 2)]
public void Parses(string caseId, int count)
{
    var result = Parse(caseId, count);
    result.AssertSnapshot("result");
}
~~~

The files are scoped as follows:

~~~text
__snapshots__/
  <relative test source directory>/
    <suite>/
      <test> [<case>] [<variant>]/
        result.json
~~~

Case values are serialized canonically into a readable prefix plus a short hash suffix, keeping each invocation deterministic and collision-resistant. Keep data-row arguments deterministic and within the static type support contract; an opaque row value needs an explicit custom runner case key. The case folder is the invocation identity; the capture name is the file identity. If one invocation has several logical outputs, give them stable names such as result, headers, and diagnostics. A loop over runtime data still belongs to one invocation, so include a stable id in each capture name, for example `item.AssertSnapshot($"result-{item.Id}")`.

Use SnapshotTestOptions.Case only for a custom runner integration or when the runner cannot be compiled with the package build integration. A Variant is an explicit additional discriminator for intentionally different baselines, such as a documented target configuration.

Capture names are inferred from simple expressions when possible (result.AssertSnapshot() writes result.json). Explicit names are recommended for multiple captures of the same expression. Names are portable, bounded, and checked case-insensitively.

## Failure output

Failures aggregate every missing, changed, and unused entry. JSON comparisons report member paths and include a compact unified diff:

~~~diff
--- expected
+++ received
@@ -1,5 +1,5 @@
  {
    "Customer": {
-     "Name": "Alice"
+     "Name": "Bob"
    }
  }
~~~

Large values are abbreviated in the exception. Full expected, received, and run-manifest files are written beneath artifacts/imprint/failures (or the configured artifact directory), and SnapshotMismatchException.Report exposes the structured results.

## Formats and comparison

Structured values are written as canonical, indented JSON in .json files. Plain strings use text format and .txt by default. SnapshotFormat.Snap is available when a project needs the legacy .snap extension. A string can be parsed and compared as JSON explicitly:

~~~csharp
payload.AssertSnapshot("payload", new SnapshotOptions
{
    Format = SnapshotFormat.Json,
    Comparison = new SnapshotComparison
    {
        NumericTolerance = 0.001m,
        IgnoreArrayOrder = true
    }
});
~~~

Text comparison can ignore line-ending differences, case, or trailing whitespace. JSON comparison is structural, preserves duplicate array values, and supports bounded numeric tolerance and unordered arrays. ISnapshotComparer supplies a custom equality rule for one capture. Equality settings affect comparison only; the saved representation stays deterministic.

The static declared type is the serialization contract. Records, DTOs, anonymous projections, tuples, nullable values, enums, collections, dictionaries, and multidimensional arrays are supported. Runtime-derived members of an object or base-typed value are not discovered. Private or specialized contracts can use an explicit typed writer:

~~~csharp
value.AssertSnapshot(
    static (json, item, context) =>
    {
        json.WriteStartObject();
        json.WriteNumber("code", item.Code);
        json.WriteString("message", item.Message);
        json.WriteEndObject();
    },
    "custom");
~~~

SnapshotInclude<MyType> roots a generated writer for a type used only through a generic helper. No reflective serializer or ToString fallback is used.

## Configuration

Place an optional snapshots.config.json in the test-project root. Unknown and duplicate properties fail with an actionable configuration error.

~~~json
{
  "$schema": "../snapshots.schema.json",
  "version": 1,
  "update": "missing",
  "directoryName": "__snapshots__",
  "preferDisplayNames": false,
  "textExtension": "txt",
  "naming": "name-then-order",
  "comparison": {
    "ignoreLineEndings": true,
    "ignoreTrailingWhitespace": false
  }
}
~~~

update accepts missing, verify, or all. The default is missing, which creates a missing baseline and fails on changes. directoryName controls the root folder name; rootDirectory can choose a project-relative or absolute root. artifactDirectory is for failure artifacts and must remain outside the baseline root. See snapshots.schema.json and the example for all limits and comparison options.

The precedence for update policy is read-only/CI enforcement, an explicit run policy, capture options, test attributes, class attributes, project configuration, and finally the default. Paths are derived from source metadata and do not depend on the process working directory.

## Custom runner integration

Most users do not need this API. A runner or host that cannot use the package build integration can provide an explicit identity:

~~~csharp
using var scope = Snapshots.Begin(new SnapshotTestOptions
{
    Identity = new SnapshotTestIdentity(
        ProjectDirectory: projectRoot,
        SourceFile: sourceFile,
        Suite: "ContractTests",
        Test: "CurrentContract",
        Case: "default")
});

try
{
    ExecuteTestBody();
    scope.Complete();
}
catch (Exception error)
{
    scope.Abort(error);
    throw;
}
~~~

Snapshots.Run and Snapshots.RunAsync remain available for that explicit integration. They are not needed in ordinary framework test methods. A runner must call Complete only after all work that should influence approval has succeeded; teardown that happens after the method is outside the automatic lifetime.

## Build and verify

All generated build output belongs under ./artifacts:

~~~text
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet pack TheLithium.Imprint.slnx -c Release
dotnet run --project tests/TheLithium.Imprint.Specifications -c Release -- --expect-managed
dotnet restore TheLithium.Imprint.slnx -p:UsePackageReferences=true -p:RestorePackagesPath=artifacts/nuget/package-consumer --force-evaluate
dotnet test TheLithium.Imprint.slnx -c Release -p:UsePackageReferences=true -p:RestorePackagesPath=artifacts/nuget/package-consumer --no-restore
~~~

The package is written to artifacts/packages. The package-consumer restore selects that local feed and the test run exercises the bundled generator and build integration. Restore the default project-reference mode before switching back to ordinary development builds. Native AOT verification uses the executable specification project and the platform's normal Native AOT prerequisites:

~~~text
dotnet publish tests/TheLithium.Imprint.Specifications -c Release -r win-x64 -p:UsePackageReferences=true -p:RestorePackagesPath=artifacts/nuget/package-consumer -p:PublishAot=true -p:SelfContained=true -p:IlcTreatWarningsAsErrors=true -p:ILLinkTreatWarningsAsErrors=true -o artifacts/native/win-x64
./artifacts/native/win-x64/TheLithium.Imprint.Specifications.exe --expect-aot
~~~

The source repository contains one runtime library, one package project, one compiler generator, one build task, and test projects for the runtime, generator, xUnit, NUnit, MSTest, and Native AOT specifications. There is no command-line tool and no snapshot ownership sidecar file. Baselines are reviewed directly under the configured snapshots folder; transient journals, locks, and failure artifacts stay under artifacts/imprint.

See [framework integration](docs/FRAMEWORKS.md), [the design notes](docs/DESIGN.md), [the type contract](docs/TYPE-SUPPORT.md), [validation](docs/VALIDATION.md), and the [licensing note](LICENSING.md) for details.

## Release automation

The [release workflow](.github/workflows/release.yml) runs only for a `v*.*.*` tag or an intentional manual dispatch. It validates the managed matrix first, then packs the requested version, tests the packages as consumers, verifies a Linux Native AOT publish, uploads both `.nupkg` files to NuGet.org, and creates a GitHub Release with generated notes and package assets.

Configure the repository secret `NUGET_API_KEY` before using it. A manual release accepts a SemVer version and a prerelease flag; a tag determines both automatically. Normal pushes and pull requests never publish packages.
