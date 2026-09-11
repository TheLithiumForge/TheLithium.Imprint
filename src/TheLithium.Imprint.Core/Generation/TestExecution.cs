using System.ComponentModel;

namespace TheLithium.Imprint.Generation;

/// <summary>Build-generated entry points that complete snapshots after the original method body returns successfully.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class TestExecution
{
    public static void Run(Action body, SnapshotTestOptions options, string sourceFile, int sourceLine)
    {
        Run(() => { body(); return true; }, options, sourceFile, sourceLine);
    }

    public static T Run<T>(Func<T> body, SnapshotTestOptions options, string sourceFile, int sourceLine)
    {
        using var scope = Open(options, sourceFile, sourceLine);
        try
        {
            var result = body();
            scope?.Complete();
            return result;
        }
        catch (Exception error)
        {
            scope?.Abort(error);
            throw;
        }
    }

    public static Task RunAsync(Func<Task> body, SnapshotTestOptions options, string sourceFile, int sourceLine)
    {
        return RunAsync(async () => { await body().ConfigureAwait(false); return true; }, options, sourceFile, sourceLine);
    }

    public static async Task<T> RunAsync<T>(Func<Task<T>> body, SnapshotTestOptions options, string sourceFile, int sourceLine)
    {
        using var scope = Open(options, sourceFile, sourceLine);
        try
        {
            var result = await body().ConfigureAwait(false);
            scope?.Complete();
            return result;
        }
        catch (Exception error)
        {
            scope?.Abort(error);
            throw;
        }
    }

    public static async ValueTask RunValueTask(Func<ValueTask> body, SnapshotTestOptions options, string sourceFile, int sourceLine)
    {
        await RunValueTask(async () => { await body().ConfigureAwait(false); return true; }, options, sourceFile, sourceLine).ConfigureAwait(false);
    }

    public static async ValueTask<T> RunValueTask<T>(Func<ValueTask<T>> body, SnapshotTestOptions options, string sourceFile, int sourceLine)
    {
        using var scope = Open(options, sourceFile, sourceLine);
        try
        {
            var result = await body().ConfigureAwait(false);
            scope?.Complete();
            return result;
        }
        catch (Exception error)
        {
            scope?.Abort(error);
            throw;
        }
    }

    private static SnapshotScope? Open(SnapshotTestOptions options, string sourceFile, int sourceLine)
        => Snapshots.Active is null ? Snapshots.Begin(options, sourceFile, sourceLine) : null;
}
