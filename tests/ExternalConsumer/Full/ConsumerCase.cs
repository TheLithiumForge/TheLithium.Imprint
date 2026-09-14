using TheLithium.Imprint;

namespace ExternalConsumer;

internal sealed class ConsumerCase
{
    internal static string RunRoot { get; set; } = string.Empty;
    internal string Root { get; } = Path.Combine(RunRoot, Guid.NewGuid().ToString("N"));
    internal string DirectoryPath { get; set; } = string.Empty;
    internal ConsumerCase() => Directory.CreateDirectory(Root);
    internal SnapshotTestOptions Options => new()
    {
        Identity = new(Root, "Case.cs", "External", "Contract")
    };
    internal void Configure(string json, string name = "snapshots.config.json") => File.WriteAllText(Path.Combine(Root, name), json);
    internal string Read(string name) => File.ReadAllText(Path.Combine(DirectoryPath, name));
    internal SnapshotReport Run(Action body, SnapshotTestOptions? options = null)
        => Snapshots.Run(() =>
        {
            DirectoryPath = Snapshots.Current.BaselineDirectory;
            body();
        }, options ?? Options);
}

internal sealed class EnvironmentSetting : IDisposable
{
    private readonly string _name;
    private readonly string? _previous;
    internal EnvironmentSetting(string name, string? value)
    {
        _name = name;
        _previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }
    public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
}

internal static class Expect
{
    internal static void True(bool value, string message = "Condition failed")
    {
        if (!value) throw new InvalidOperationException(message);
    }
    internal static void Equal<T>(T expected, T actual)
        => True(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; received {actual}");
    internal static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }
}
