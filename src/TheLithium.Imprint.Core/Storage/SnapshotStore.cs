using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

namespace TheLithium.Imprint.Storage;


/// <summary>Local-filesystem store with optimistic concurrency and a recoverable, per-test journal.</summary>
internal sealed class SnapshotStore
{
    private readonly EffectiveSettings _settings;
    private readonly string _journal;
    private readonly string _lockPath;
    private static readonly ConcurrentDictionary<string, string> Claims = new(StringComparer.Ordinal);

    internal SnapshotStore(EffectiveSettings settings)
    {
        _settings = settings;
        var physicalKey = PortableNames.Hash(settings.TestDirectory.Replace('\\', '/').ToUpperInvariant());
        _journal = Path.Combine(settings.StorageRoot, "transactions", physicalKey);
        var claim = Claims.GetOrAdd(physicalKey, settings.IdentityKey);
        if (claim != settings.IdentityKey)
        {
            throw new SnapshotConflictException($"Two test identities resolve to the same portable snapshot directory: {settings.TestDirectory}");
        }
        // A stable project lock path lets processes with different diagnostic paths coordinate.
        // Lock files are intentionally not deleted: unlinking them creates a lock-inode race.
        _lockPath = Path.Combine(settings.StorageRoot, "locks", $"{physicalKey}.lock");
    }

    internal BaselineState Read()
    {
        using var held = AcquireLock();
        Recover();
        return ReadUnlocked();
    }

    internal void VerifyUnchanged(BaselineState expected)
    {
        using var held = AcquireLock();
        Recover();
        if (ReadUnlocked().Fingerprint != expected.Fingerprint)
        {
            throw new SnapshotConflictException($"The baseline changed during verification. Rerun the test: {_settings.DisplayName}");
        }
    }

    internal void Commit(BaselineState expected, Dictionary<string, string> desired)
    {
        _settings.Cancellation.ThrowIfCancellationRequested();
        if (_settings.ReadOnly)
        {
            throw new SnapshotConfigurationException("Baseline writes are disabled for this run.");
        }

        using var held = AcquireLock();
        Recover();
        var current = ReadUnlocked();
        if (current.Fingerprint != expected.Fingerprint)
        {
            throw new SnapshotConflictException($"The baseline changed during this test. Nothing was approved. Rerun the test: {_settings.DisplayName}");
        }

        EnsureSafeDirectory(_journal);
        Directory.CreateDirectory(Path.Combine(_journal, "before"));
        try
        {
            foreach (var file in current.Files)
            {
                DurableWrite(Path.Combine(_journal, "before", file.Key), file.Value);
            }

            foreach (var file in desired)
            {
                ValidateFileName(file.Key);
            }
            DurableWrite(Path.Combine(_journal, "before.fingerprint"), current.Fingerprint);
            _settings.Cancellation.ThrowIfCancellationRequested();
            // No baseline mutation is allowed before this marker has been flushed.
            DurableWrite(Path.Combine(_journal, "prepared"), SnapshotProtocol.JournalMarker);
            // Do not interrupt this small commit section with cancellation. Finish or roll back.
            Apply(desired);
            DurableWrite(Path.Combine(_journal, "committed"), SnapshotProtocol.JournalMarker);
        }
        catch (Exception error)
        {
            try
            {
                Recover();
            }
            catch (Exception rollback)
            {
                throw new SnapshotException($"Snapshot commit failed and automatic recovery could not complete. Preserve the journal at {_journal}.",
                    new AggregateException(error, rollback));
            }
            throw;
        }
        // A committed journal can be cleaned on the next run if cleanup is temporarily blocked.
        try
        {
            Directory.Delete(_journal, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private BaselineState ReadUnlocked()
    {
        EnsureSafeDirectory(_settings.TestDirectory, create: false);
        var files = ReadFiles(_settings.TestDirectory);
        return new(files, Fingerprint(files));
    }

    private static string Fingerprint(Dictionary<string, string> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            Append(hash, file.Key);
            Append(hash, file.Value);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private Dictionary<string, string> ReadFiles(string directory)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(directory))
        {
            return result;
        }

        EnsureSafeDirectory(directory, create: false);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var name = Path.GetFileName(path);
            if (!SnapshotFileNames.IsSnapshotFile(name))
            {
                continue;
            }

            if (!seen.Add(name))
            {
                throw new SnapshotConflictException($"Case-insensitive snapshot filename collision in {directory}.");
            }

            if (result.Count >= SnapshotLimits.MaximumEntries)
            {
                throw new SnapshotException($"A test directory cannot contain more than {SnapshotLimits.MaximumEntries} snapshot files.");
            }

            SnapshotPaths.CheckLink(path);
            var value = SnapshotFileReader.ReadText(path, _settings.MaxBytesPerSnapshot);
            total += SnapshotEncoding.Utf8.GetByteCount(value);
            if (total > Math.Max(_settings.MaxBytesPerSnapshot, SnapshotLimits.TestBytes))
            {
                throw new SnapshotException("The total baseline size for this test exceeds its safety budget.");
            }

            result.Add(name, value);
        }
        return result;
    }

    private void Recover()
    {
        EnsureSafeDirectory(_journal, create: false);
        if (!Directory.Exists(_journal))
        {
            return;
        }

        foreach (var marker in new[] { "prepared", "committed", "before.fingerprint" })
        {
            SnapshotPaths.CheckLink(Path.Combine(_journal, marker));
        }

        if (File.Exists(Path.Combine(_journal, "committed")))
        {
            if (SnapshotFileReader.ReadText(Path.Combine(_journal, "committed"), SnapshotLimits.JournalMarkerBytes) != SnapshotProtocol.JournalMarker)
            {
                throw new SnapshotConflictException($"Unrecognized snapshot journal version: {_journal}");
            }

            if (!_settings.ReadOnly)
            {
                Directory.Delete(_journal, recursive: true);
            }

            return;
        }
        if (File.Exists(Path.Combine(_journal, "prepared")))
        {
            // Recovery mutates baselines and is never implicit in an enforced read-only run.
            if (_settings.ReadOnly)
            {
                throw new SnapshotConflictException($"An interrupted snapshot update needs recovery. Run locally with writes enabled before verification: {_journal}");
            }

            if (SnapshotFileReader.ReadText(Path.Combine(_journal, "prepared"), SnapshotLimits.JournalMarkerBytes) != SnapshotProtocol.JournalMarker
                || !Directory.Exists(Path.Combine(_journal, "before"))
                || !File.Exists(Path.Combine(_journal, "before.fingerprint")))
            {
                throw new SnapshotConflictException($"Incomplete or unrecognized snapshot recovery journal: {_journal}");
            }

            var previous = ReadFiles(Path.Combine(_journal, "before"));
            if (Fingerprint(previous) != SnapshotFileReader.ReadText(Path.Combine(_journal, "before.fingerprint"), SnapshotLimits.JournalMarkerBytes))
            {
                throw new SnapshotConflictException($"Snapshot recovery backup failed its integrity check. Preserve {_journal}");
            }

            Apply(previous);
        }
        // An unprepared journal cannot have modified the baselines.
        if (!_settings.ReadOnly)
        {
            Directory.Delete(_journal, recursive: true);
        }
    }

    private void Apply(Dictionary<string, string> files)
    {
        EnsureSafeDirectory(_settings.TestDirectory);
        // Remove old names first so case-only renames are portable on case-insensitive systems.
        foreach (var path in Directory.EnumerateFiles(_settings.TestDirectory))
        {
            var name = Path.GetFileName(path);
            if (SnapshotFileNames.IsSnapshotFile(name) && !files.ContainsKey(name))
            {
                SnapshotPaths.CheckLink(path);
                File.Delete(path);
            }
            else if (name.StartsWith(".imprint-tmp-", StringComparison.Ordinal))
            {
                SnapshotPaths.CheckLink(path);
                File.Delete(path);
            }
        }
        foreach (var file in files.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            ValidateFileName(file.Key);
            AtomicWrite(Path.Combine(_settings.TestDirectory, file.Key), file.Value);
        }
    }

    private FileStream AcquireLock()
    {
        _settings.Cancellation.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(_lockPath) ?? throw new SnapshotConfigurationException("The lock path has no parent directory.");
        EnsureSafeDirectory(directory);
        SnapshotPaths.CheckLink(_lockPath);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            _settings.Cancellation.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.None);
            }
            catch (IOException) when (elapsed.Elapsed < _settings.LockTimeout)
            {
                Thread.Sleep(25);
            }
            catch (IOException error)
            {
                throw new SnapshotException($"Could not acquire the snapshot write lock: {_settings.DisplayName}", error);
            }
        }
    }

    private void EnsureSafeDirectory(string path, bool create = true)
    {
        path = Path.GetFullPath(path);
        var root = SnapshotPaths.Contains(_settings.StorageRoot, path) ? _settings.StorageRoot : _settings.BaselineRoot;
        if (!SnapshotPaths.Contains(root, path))
        {
            throw new SnapshotConfigurationException("Snapshot storage path escapes its configured root.");
        }

        SnapshotPaths.CheckPath(_settings.Identity.ProjectDirectory, path);
        if (create)
        {
            Directory.CreateDirectory(path);
        }
    }

    private static void AtomicWrite(string path, string text)
    {
        SnapshotPaths.CheckLink(path);
        var directory = Path.GetDirectoryName(path)
            ?? throw new SnapshotConfigurationException("The snapshot path has no parent directory.");
        var temporary = Path.Combine(directory, $".imprint-tmp-{Guid.NewGuid():N}");
        try
        {
            DurableWrite(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void DurableWrite(string path, string text)
    {
        SnapshotPaths.CheckLink(path);
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        file.Write(SnapshotEncoding.Utf8.GetBytes(text));
        file.Flush(flushToDisk: true);
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = SnapshotEncoding.Utf8.GetBytes(value);
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(length, bytes.LongLength);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void ValidateFileName(string file)
    {
        if (file != Path.GetFileName(file) || !SnapshotFileNames.IsSnapshotFile(file))
        {
            throw new SnapshotConfigurationException("Invalid snapshot filename in a storage operation.");
        }
    }
}
