# Test frameworks

For the common cases, see the [README](../README.md). This page collects the per-framework details and the edge cases.

There is no adapter to install and no base class to inherit. The MSBuild task supplies the snapshot lifetime at compile time, so a test method is just a test method.

## What works everywhere

|                       | xUnit                         | NUnit                         | MSTest                           |
| --------------------- | ----------------------------- | ----------------------------- | -------------------------------- |
| Sync test             | `[Fact]`                      | `[Test]`                      | `[TestMethod]`                   |
| Async test            | `[Fact]` + `Task`             | `[Test]` + `Task`             | `[TestMethod]` + `Task`          |
| Parameterized         | `[Theory]` + `[InlineData]`   | `[TestCase]`                  | `[DataRow]`                      |
| Explicit display name | `[Fact(DisplayName = "...")]` | `[Test(Description = "...")]` | `[TestMethod]` + `[DisplayName]` |

Any other runner that fails a test on an uncaught exception works too — that is the only integration requirement.

```csharp
public void Example()
{
    var result = new { Count = 3, Names = new[] { "a", "b", "c" } };

    result.AssertSnapshot("result");
    "three results".AssertSnapshot("stdout");

    Assert.Equal(3, result.Count);   // ordinary assertions sit right alongside
}
```

## Return types

`void`, `T`, `Task`, `Task<T>`, `ValueTask`, and `ValueTask<T>` are all supported. The original signature stays visible to the runner.

These are rejected at compile time with **IMP102**, because there is no reliable point at which the method has finished:

| Rejected                             | Why                                                            |
| ------------------------------------ | -------------------------------------------------------------- |
| `async void`                         | Nothing to await; the runner returns before the body finishes  |
| `async` returning a custom awaitable | Cannot be completed generically without risking early approval |
| Iterators (`yield return`)           | The body runs lazily, outside the call                         |
| `ref` / `in` / `out` parameters      | The body cannot be moved into a local function                 |
| By-ref returns                       | Same                                                           |
| Tests declared on a `struct`         | Same                                                           |

`async Task` is the fix for almost all of these.

## The lifetime boundary

Everything inside the method — including awaited work and `finally` blocks — is inside the boundary and can prevent approval.

Framework teardown that runs _after_ the method is outside it:

```csharp
public sealed class Tests : IAsyncLifetime
{
    [Fact]
    public async Task Example()
    {
        value.AssertSnapshot();
    }                                  // ← snapshots are decided here

    public async Task DisposeAsync()
    {
        await VerifyNoErrorsAsync();   // ← too late to stop the approval
    }
}
```

If teardown must influence approval, move it into the method, or open the scope yourself (see below).

## Names

The suite folder is the containing type; the test folder is the method name.

```csharp
[SnapshotSettings(Name = "Contract")]     // on a class → suite folder
public sealed class ContractTests
{
    [SnapshotSettings(Name = "V1")]       // on a method → test folder
    [Fact]
    public void CurrentContract() { }
}
// → __snapshots__/Contract/V1/
```

Set `naming.useFrameworkDisplayNames` to `true` in the config to use the framework's display or description metadata for the test folder instead. The generator reads it from known test attributes, including inherited ones, and only when it is a compile-time constant; the method name remains the fallback.

A parameterized row's _own_ display text is deliberately ignored. It describes one invocation, not the method, and in several frameworks it is computed at runtime — using it would make folder identity unstable. Rows are separated by their argument values instead.

## Parameterized tests

Rows are separated automatically; see the README for the folder shape. Two things to know:

- The case key uses the same static type contract as snapshot serialization. An opaque row value (an `object`, a type the generator cannot see) cannot produce a stable key — supply `SnapshotTestOptions.Case` from a custom integration instead.
- A loop _inside_ one invocation is still one case. Put a stable id in the capture name: `item.AssertSnapshot($"item-{item.Id}")`.

## Opening the scope yourself

For a runner the build task cannot compile, or a host that generates test bodies itself:

```csharp
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
    ExecuteTestBodyAndAnyTeardownThatShouldCount();
    scope.Complete();
}
catch (Exception error)
{
    scope.Abort(error);
    throw;
}
```

Rules for an adapter:

- Call `Complete` only after _everything_ that should affect approval has succeeded.
- `Dispose` without `Complete` abandons the scope and approves nothing. The `using` above is the safety net, not the approval.
- Treat an uncaught `SnapshotMismatchException`, `SnapshotCaptureException`, `SnapshotConfigurationException`, or `SnapshotConflictException` as a test failure.
- Reporting and artifact attachment are the runner's job. `SnapshotMismatchException.Report` has the structured results.

`Snapshots.Run(...)` and `Snapshots.RunAsync(...)` wrap this pattern when you do not need the scope object itself.

`[SnapshotSettings]` on a class or method also establishes a lifetime, which is useful when a test reaches its captures indirectly — through a delegate, say — so the call graph cannot see the path.

## Native AOT

Imprint's runtime is AOT-clean: no reflective discovery, and the generator and build task never ship into your binary.

Your _runner_ is the constraint. Runners that discover tests by reflection generally do not survive trimming — and at the time of writing, xunit.v3's in-process runner fails under Native AOT with `The path is empty (Parameter 'path')` from `Assembly.Location`, with or without Imprint present.

So for an AOT test project, drive the tests from a plain `Main` with an explicit list:

```csharp
internal static class Program
{
    public static int Main()
    {
        var failed = 0;
        foreach (var (name, body) in new (string, Action)[]
        {
            ("CapturesARecord", CapturesARecord),
            ("CapturesText", CapturesText),
        })
        {
            try { body(); Console.WriteLine("PASS " + name); }
            catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
        }
        return failed == 0 ? 0 : 1;
    }

    private static void CapturesARecord()
        => new Order("aot-1", 99.95m, ["Disk"]).AssertSnapshot();
}
```

The build task instruments any method that reaches a capture, not just attributed ones, so these plain methods get the same lifetime, identity, and folder layout as a `[Fact]`.

[`tests/TheLithium.Imprint.Specifications`](../tests/TheLithium.Imprint.Specifications) is exactly this, and is what the AOT CI lane publishes and runs.
