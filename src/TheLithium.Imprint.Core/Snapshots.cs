using System.Runtime.CompilerServices;

namespace TheLithium.Imprint;

/// <summary>Runner-independent execution boundaries. Exceptions propagate to the caller's assertion framework.</summary>
public static class Snapshots
{
    private static readonly AsyncLocal<SnapshotScope?> Ambient = new();

    internal static SnapshotScope? Active => Ambient.Value;

    public static SnapshotScope Current => Ambient.Value
        ?? throw new SnapshotConfigurationException("No snapshot lifetime is active. Reference the TheLithium.Imprint package in the test project and rebuild. " +
            "The package instruments test methods during compilation; the Core package alone requires an explicit integration.");

    public static SnapshotScope Begin(SnapshotTestOptions? options = null,
        [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
    {
        var settings = Settings.Resolve(options, sourceFile, sourceLine);
        var scope = new SnapshotScope(settings, Ambient.Value);
        Ambient.Value = scope;
        return scope;
    }

    public static SnapshotReport Run(Action body, SnapshotTestOptions? options = null,
        [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
    {
        ArgumentNullException.ThrowIfNull(body);
        using var scope = Begin(options, sourceFile, sourceLine);
        try
        {
            body();
            return scope.Complete();
        }
        catch (Exception error)
        {
            scope.Abort(error);
            throw;
        }
    }

    [Obsolete("Use Snapshots.RunAsync for asynchronous callbacks. Async-void test bodies are unsafe.", error: true)]
    public static void Run(Func<Task> body, SnapshotTestOptions? options = null)
        => throw new SnapshotConfigurationException("Use Snapshots.RunAsync.");

    public static async Task<SnapshotReport> RunAsync(Func<Task> body, SnapshotTestOptions? options = null,
        [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
    {
        ArgumentNullException.ThrowIfNull(body);
        using var scope = Begin(options, sourceFile, sourceLine);
        try
        {
            await body().ConfigureAwait(false);
            return scope.Complete();
        }
        catch (Exception error)
        {
            scope.Abort(error);
            throw;
        }
    }

    public static void UpdateCurrentTest(SnapshotUpdate update = SnapshotUpdate.All) => Current.Update = update;

    internal static void Restore(SnapshotScope scope, SnapshotScope? previous)
    {
        if (ReferenceEquals(Ambient.Value, scope))
        {
            Ambient.Value = previous;
        }
    }
}
