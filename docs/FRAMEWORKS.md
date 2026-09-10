# Framework integration

The runtime is runner-independent. The portable callback boundary is tested with real xUnit, NUnit, and MSTest runners. The executable specification project separately verifies the runtime under Native AOT. See [validation](VALIDATION.md).

## Synchronous tests

```csharp
// xUnit: [Fact]
// NUnit: [Test]
// MSTest: [TestMethod]
public void Example()
{
    Snapshots.Run(() =>
    {
        var result = new { Count = 3, Names = new[] { "a", "b", "c" } };
        result.Snapshot();
        "three results".Snapshot("stdout");
        // Put the framework's ordinary assertions here.
    });
}
```

## Asynchronous tests

```csharp
public Task ExampleAsync() => Snapshots.RunAsync(async () =>
{
    var result = await LoadAsync();
    result.Snapshot();
    // Ordinary assertions and all awaited work belong inside this callback.
});
```

Task<SnapshotReport> is a Task and can be returned as Task. Use RunAsync for Task-returning work; the accidental Func<Task> overload of Run is a compile-time obsolete error to discourage async-void conversion.

SnapshotSettings is independent of the runner's own test attributes. Recognized constant Xunit DisplayName and NUnit/MSTest Description metadata is read from Roslyn symbols at compilation, including derived test attributes and MSTest constructor display names. Runner-generated dynamic display names, theory-row discovery metadata, fixtures, and teardown outcomes are not inferred automatically. Supply Name/Case/Identity where needed. Per-row display names never rename the whole parameterized method.

## Explicit adapter boundary

A custom runner/integration can use:

```csharp
using var scope = Snapshots.Begin(new()
{
    Identity = new SnapshotTestIdentity(
        projectRoot, sourceFile, "InstallTests", "Install", Case: "default")
});
try
{
    await ExecuteTestBodyAndRelevantTeardownAsync();
    scope.Complete();
}
catch (Exception error)
{
    scope.Abort(error);
    throw;
}
```

The scope must be established in the execution context in which test code is invoked. AsyncLocal is not a replacement for correct execution-context propagation by an adapter. Standalone Snapshot calls outside a scope fail clearly.

Complete can throw SnapshotMismatchException, SnapshotCaptureException, SnapshotConfigurationException, SnapshotConflictException, or storage/comparison errors. A conventional runner treats uncaught errors as test failure; specialized failure categories and artifact attachments need that runner's own integration.

The core cannot guarantee that a future runner teardown succeeds after the callback completes. Either put the relevant cleanup inside the callback or make the adapter control the real final execution boundary. Do not approve from an asynchronously observed result message and claim it was part of the test's assertion phase.

## Native AOT

The snapshot runtime uses no reflective discovery. Its compiler generator is a managed build-time component and is not shipped as an application runtime dependency. A Native AOT host must still be able to compile its test runner, assertion libraries, application code, and other dependencies.

The supplied executable specification harness deliberately discovers tests through an explicit array of delegates, so its own discovery does not require reflection. It is the built-in end-to-end native verification target. It is not evidence that every external test framework supports Native AOT.
