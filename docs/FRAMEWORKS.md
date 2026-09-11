# Framework integration

The normal integration is a regular test method with AssertSnapshot or UpdateSnapshot calls. The package build target supplies the lifetime at compile time, so no xUnit, NUnit, or MSTest adapter is required.

## Synchronous tests

~~~csharp
// xUnit: [Fact], NUnit: [Test], MSTest: [TestMethod]
public void Example()
{
    var result = new { Count = 3, Names = new[] { "a", "b", "c" } };

    result.AssertSnapshot("result");
    "three results".AssertSnapshot("stdout");

    // Keep the framework's ordinary assertions beside the snapshots.
}
~~~

## Asynchronous tests

~~~csharp
public async Task ExampleAsync()
{
    var result = await LoadAsync();
    result.AssertSnapshot();
}
~~~

Task, Task<T>, ValueTask, and ValueTask<T> methods are supported. The original method signature remains visible to the runner. An async void method or async custom awaitable, iterator, ref/in/out method, by-ref return, or test declared on a struct is rejected with a compile diagnostic because it cannot provide a reliable completion boundary.

## Parameterized tests

xUnit InlineData, NUnit TestCase, MSTest DataRow, and compatible parameterized attributes work without wrapper code. The compiled method receives the actual values, and the build integration creates a deterministic case discriminator from those values. Each row therefore gets its own test folder. A row's dynamic display text is not used as the case identity. Use an explicit AssertSnapshot name for each logical output and use SnapshotTestOptions.Case only in a custom integration.

The case discriminator covers data supplied by the test framework and uses the same static type contract as snapshot serialization. An opaque row value needs an explicit custom runner case key. Runtime loops inside one invocation still share one case, so include a stable id in the capture name when the loop produces several logical files, for example `item.AssertSnapshot($"result-{item.Id}")`.

## Names and display metadata

The default suite and test folders use the containing type and C# method names. SnapshotSettings(Name = "...") overrides either folder. The generator records constant display or description metadata from known and compatible test attributes, including inherited framework attributes. The project setting preferDisplayNames opts into that metadata for the test folder. Row-specific display names are intentionally ignored for the whole method because they describe one invocation rather than the method identity.

## Explicit adapter boundary

A custom runner that cannot use the package build target can establish the scope directly:

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
    ExecuteTestBodyAndRelevantTeardown();
    scope.Complete();
}
catch (Exception error)
{
    scope.Abort(error);
    throw;
}
~~~

The adapter must call Complete only after work that should influence approval succeeds. Dispose abandons an open scope and never approves it. A conventional runner treats an uncaught SnapshotMismatchException, SnapshotCaptureException, SnapshotConfigurationException, or SnapshotConflictException as a test failure. Runner-specific reporting and artifact attachments remain the runner's responsibility.

## Native AOT

The runtime has no reflective discovery. The generator and MSBuild task run during compilation and are not application runtime dependencies. A Native AOT application still needs an AOT-compatible test runner, assertion library, and application under test. The repository's executable specification harness uses an explicit delegate list so its own discovery does not depend on reflection.
