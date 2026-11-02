using TheLithium.Imprint.Configuration;

namespace TheLithium.Imprint.Visual.Tools;

/// <summary>
/// A lock that holds across test processes, as a lock file opened with <see cref="FileShare.None"/>, the way the
/// snapshot store locks a test folder. It lives under the user's local data folder, because test hosts often point
/// TEMP at a folder of their own.
/// </summary>
internal static class MachineLock
{
    /// <summary>Takes the lock if nobody holds it, without waiting.</summary>
    internal static FileStream? TryAcquire(string name)
    {
        try
        {
            return Open(PathOf(name));
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal static async Task<FileStream> AcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var path = PathOf(name);
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return Open(path);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException error)
            {
                throw new SnapshotCaptureException($"Another test held {name} for more than {timeout.TotalSeconds:0} seconds.", error);
            }
        }
    }

    private static FileStream Open(string path) => new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);

    private static string PathOf(string name)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        var directory = Path.Combine(root.Length != 0 ? root : Path.GetTempPath(), "TheLithium.Imprint", "visual", "locks");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, PortableNames.Segment(name) + ".lock");
    }
}
