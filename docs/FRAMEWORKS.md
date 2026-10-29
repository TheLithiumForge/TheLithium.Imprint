# Test frameworks

Imprint works with any test framework that fails a test on an uncaught exception, because the MSBuild task adds the snapshot lifetime to your test methods at compile time.

You don't install an adapter or inherit a base class. A test method stays a normal test method.

## Supported frameworks

xUnit, NUnit and MSTest all work the same way:

|                       | xUnit                         | NUnit                         | MSTest                           |
| --------------------- | ----------------------------- | ----------------------------- | -------------------------------- |
| Sync test             | `[Fact]`                      | `[Test]`                      | `[TestMethod]`                   |
| Async test            | `[Fact]` + `Task`             | `[Test]` + `Task`             | `[TestMethod]` + `Task`          |
| Parameterized         | `[Theory]` + `[InlineData]`   | `[TestCase]`                  | `[DataRow]`                      |
| Display name          | `[Fact(DisplayName = "...")]` | `[Test(Description = "...")]` | `[TestMethod]` + `[DisplayName]` |

Any other runner that fails a test on an uncaught exception works too. That's the only requirement. Snapshots sit right next to your normal assertions:

```csharp
public void Example()
{
    var result = new { Count = 3, Names = new[] { "a", "b", "c" } };

    result.AssertSnapshot("result");          // writes result.json
    "three results".AssertSnapshot("stdout"); // writes stdout.txt

    Assert.Equal(3, result.Count);
}
```

## Return types

These return types work, and the runner still sees the original signature:

- `void` and `T`
- `Task` and `Task<T>`
- `ValueTask` and `ValueTask<T>`

The MSBuild task moves your test body into a local function, so it can act after the body returns. Some shapes have no reliable point where the method has finished. These fail the build with `IMP102` instead of being wrapped wrong:

| Rejected                             | Why                                                               |
| ------------------------------------ | ----------------------------------------------------------------- |
| `async void`                         | There's nothing to await, so the runner returns before the body finishes |
| `async` returning a custom awaitable | It can't be completed in a generic way without risking an early approval |
| Iterators (`yield return`)           | The body runs lazily, after the call has already returned          |
| `ref`, `in` or `out` parameters      | The body can't move into a local function                          |
| By-ref returns (`ref int Rejected()`) | Same as above                                                     |
| Tests declared on a `struct`         | Same as above                                                      |

The fix is small in every case. `async Task` fixes almost all of them.

For `async void`, return `Task`:

```csharp
// Rejected
public async void Rejected()
{
    (await LoadAsync()).AssertSnapshot();
}

// Accepted
public async Task Accepted()
{
    (await LoadAsync()).AssertSnapshot();
}
```

For an iterator, materialize the items, then capture them:

```csharp
// Rejected
public IEnumerable<int> Rejected()
{
    yield return 1;
    value.AssertSnapshot();
}

// Accepted
public void Accepted()
{
    var items = Produce().ToArray();
    items.AssertSnapshot();
}
```

For a `ref`, `in` or `out` parameter, return the value instead:

```csharp
// Rejected
public void Rejected(out int count)
{
    count = 1;
    count.AssertSnapshot();
}

// Accepted
public void Accepted()
{
    var count = Compute();
    count.AssertSnapshot();
}
```

For a custom awaitable, use `Task` or `ValueTask`. For a test on a `struct`, use a `class`.

## Lifetime

Everything inside the method can stop an approval, including awaited work and `finally` blocks. Framework teardown that runs after the method can't, as [Approval](../README.md#approval) shows.

If teardown must affect approval, move it into the method, or open the scope yourself, as [Custom runners](#custom-runners) describes.

## Names

The suite folder is the containing type, and the test folder is the method name. `[SnapshotSettings(Name = "...")]` replaces either one:

```csharp
[SnapshotSettings(Name = "Contract")]     // on a class, names the suite folder
public sealed class ContractTests
{
    [SnapshotSettings(Name = "V1")]       // on a method, names the test folder
    [Fact]
    public void CurrentContract() { }
}
// writes to __snapshots__/Contract/V1/
```

To name the test folder after the framework's display or description metadata, set `naming.useFrameworkDisplayNames` to `true` in the config. The generator reads it from known test attributes, including inherited ones. It uses it only when the value is a compile-time constant. Otherwise the method name is still the fallback.

A parameterized row's own display text is ignored on purpose. It describes one invocation, not the method. In several frameworks it's computed at runtime, so using it would make the folder name unstable. Rows are told apart by their argument values instead, as [Parameterized tests](../README.md#parameterized-tests) describes.

## Custom runners

If your runner is one the MSBuild task can't compile, or a host that generates test bodies itself, open the scope yourself:

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

An adapter follows these rules:

- Call `Complete` only after everything that should affect approval has succeeded.
- `Dispose` without `Complete` gives up the scope and approves nothing. The `using` above is the safety net, not the approval.
- Treat an uncaught `SnapshotMismatchException`, `SnapshotCaptureException`, `SnapshotConfigurationException` or `SnapshotConflictException` as a test failure.
- The runner handles reporting and attaching artifacts. `SnapshotMismatchException.Report` has the structured results.

If you don't need the scope object itself, `Snapshots.Run(...)` and `Snapshots.RunAsync(...)` wrap this pattern.

`SnapshotTestOptions` carries the settings a runner might need to override per test, such as `Identity`, `Case`, `Variant` and the capture limits. [`SnapshotTestOptions`](API.md#snapshottestoptions) lists all of them. In a normal test method, use `[SnapshotSettings]` and the config file instead.

A `[SnapshotSettings]` attribute on a method also sets up a lifetime. That helps when a test reaches its captures indirectly, e.g. through a delegate, so the call graph can't see the path.

## Native AOT

Imprint's runtime is AOT-clean. It does no reflective discovery. Serializers are generated at compile time and registered by module initializers, and the generator and the MSBuild task never ship into your binary.

Your runner is the limit. Runners that discover tests by reflection generally don't survive trimming. At the time of writing, the xunit.v3 in-process runner fails under Native AOT with `The path is empty (Parameter 'path')` from `Assembly.Location`, with or without Imprint.

So for an AOT test project, run the tests from a plain `Main` with a list of them:

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

The MSBuild task instruments any method that reaches a capture, not only methods with a test attribute. So these plain methods get the same lifetime, identity and folder layout as a `[Fact]`.

Back to the [README](../README.md).
