using System.Runtime.CompilerServices;

namespace TheLithium.Imprint;

/// <summary>Runner-independent execution boundaries. Exceptions propagate to the caller's assertion framework.</summary>
public static class Snapshots
{
    private static readonly AsyncLocal<SnapshotScope?> Ambient = new();

    internal static SnapshotScope? Active => Ambient.Value;

    /// <summary>Gets the active scope for the current asynchronous execution context.</summary>
    /// <exception cref="SnapshotConfigurationException">No generated or explicit test lifetime is active.</exception>
    public static SnapshotScope Current => Ambient.Value
        ?? throw new SnapshotConfigurationException("No snapshot lifetime is active. Reference the TheLithium.Imprint package in the test project and rebuild. " +
            "The package instruments test methods during compilation; the Core package alone requires an explicit integration.");

    /// <summary>Begins a scope for an explicit runner integration.</summary>
    /// <param name="options">Identity, update, storage, and comparison settings for the test.</param>
    /// <param name="sourceFile">Caller source file used to resolve generated identity when available.</param>
    /// <param name="sourceLine">Caller source line used to resolve generated identity when available.</param>
    /// <returns>A scope that must be completed after successful test and relevant teardown work.</returns>
    public static SnapshotScope Begin(SnapshotTestOptions? options = null,
        [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        var settings = Settings.Resolve(options, sourceFile, sourceLine);
        var scope = new SnapshotScope(settings, Ambient.Value);
        Ambient.Value = scope;
        return scope;
    }

    /// <summary>Runs a synchronous callback and commits its snapshots only after it succeeds.</summary>
    /// <param name="body">The test body and any teardown that should influence approval.</param>
    /// <param name="options">Optional explicit runner settings.</param>
    /// <param name="sourceFile">Caller source file used to resolve generated identity when available.</param>
    /// <param name="sourceLine">Caller source line used to resolve generated identity when available.</param>
    /// <returns>The aggregate snapshot report.</returns>
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

    /// <summary>Rejected asynchronous overload retained to produce a clear compile-time migration error.</summary>
    [Obsolete("Use Snapshots.RunAsync for asynchronous callbacks. Async-void test bodies are unsafe.", error: true)]
    public static void Run(Func<Task> body, SnapshotTestOptions? options = null)
        => throw new SnapshotConfigurationException("Use Snapshots.RunAsync.");

    /// <summary>Runs an asynchronous callback and commits its snapshots only after it and its awaited work succeed.</summary>
    /// <param name="body">The asynchronous test body and any teardown that should influence approval.</param>
    /// <param name="options">Optional explicit runner settings.</param>
    /// <param name="sourceFile">Caller source file used to resolve generated identity when available.</param>
    /// <param name="sourceLine">Caller source line used to resolve generated identity when available.</param>
    /// <returns>The aggregate snapshot report.</returns>
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

    /// <summary>Sets the current test policy before its first capture.</summary>
    /// <param name="update">The policy to apply to the whole current test.</param>
    public static void UpdateCurrentTest(SnapshotUpdate update = SnapshotUpdate.All) => Current.Update = update;

    internal static void Restore(SnapshotScope scope, SnapshotScope? previous)
    {
        if (ReferenceEquals(Ambient.Value, scope))
        {
            Ambient.Value = previous;
        }
    }
}
