using TheLithium.Imprint;

namespace TheLithium.Imprint.Specifications;

internal sealed class Fixture : IDisposable
{
    internal string Root { get; } = Path.Combine(AppContext.BaseDirectory, "TestResults", "imprint-specs-" + Guid.NewGuid().ToString("N"));
    internal Fixture() => Directory.CreateDirectory(Root);
    internal string Baselines(string test = "Example") => Path.Combine(Root, "__snapshots__", "Suite", test);
    internal string FilePath(string name, string test = "Example") => Path.Combine(Baselines(test), name);
    internal SnapshotTestOptions Options(SnapshotUpdate update = SnapshotUpdate.Verify, string test = "Example")
        => new()
        {
            Identity = new SnapshotTestIdentity(
                ProjectDirectory: Root,
                SourceFile: Path.Combine(Root, "Fixture.cs"),
                Suite: "Suite",
                Test: test,
                LogicalId: "TheLithium.Imprint.Specifications/" + test),
            Update = update
        };
    internal SnapshotReport Run(Action body, SnapshotUpdate update = SnapshotUpdate.Verify, string test = "Example")
        => Snapshots.Run(body, Options(update, test));
    internal void Configure(string json) => File.WriteAllText(Path.Combine(Root, "snapshots.config.json"), json);
    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed class EnvironmentValue : IDisposable
{
    private readonly string _name;
    private readonly string? _old;
    internal EnvironmentValue(string name, string? value)
    {
        _name = name;
        _old = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }
    public void Dispose() => Environment.SetEnvironmentVariable(_name, _old);
}

internal static class Check
{
    internal static void True(bool condition, string message = "Assertion failed.")
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception("Values differ.");
        }
    }
    internal static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T error) { return error; }
        throw new Exception("The expected exception was not thrown.");
    }
    internal static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T error) { return error; }
        throw new Exception("The expected exception was not thrown.");
    }
    internal static Func<Task> Sync(Action action) => () => { action(); return Task.CompletedTask; };
}
