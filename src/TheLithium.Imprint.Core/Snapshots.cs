using System.Runtime.CompilerServices;

namespace TheLithium.Imprint;

/// <summary>Runner-independent execution boundaries. Exceptions propagate to the caller's assertion framework.</summary>
public static class Snapshots
{
    private static readonly AsyncLocal<SnapshotScope?> Ambient = new();

    public static SnapshotScope Current => Ambient.Value
        ?? throw new SnapshotConfigurationException("No snapshot scope is active. Put this test body inside Snapshots.Run/RunAsync, " +
            "or use Snapshots.Begin with explicit Complete in a runner integration.");

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
        if (ReferenceEquals(Ambient.Value, scope)) Ambient.Value = previous;
    }
}

public static class SnapshotExtensions
{
    /// <summary>Captures this value immediately; comparison and authorized updates happen at test completion.</summary>
    public static void Snapshot<T>(this T value, string? name = null, SnapshotOptions? options = null,
        [CallerArgumentExpression(nameof(value))] string? expression = null)
        => Snapshots.Current.Capture(value, name, options, expression, writer: null);

    /// <summary>Uses an explicit statically typed writer for this capture only.</summary>
    public static void Snapshot<T>(this T value, SnapshotWriter<T> writer,
        string? name = null, SnapshotOptions? options = null,
        [CallerArgumentExpression(nameof(value))] string? expression = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Snapshots.Current.Capture(value, name, options, expression, writer);
    }
}
