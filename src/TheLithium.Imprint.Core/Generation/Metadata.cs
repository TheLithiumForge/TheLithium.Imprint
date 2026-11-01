using System.Collections.Concurrent;
using System.ComponentModel;

namespace TheLithium.Imprint.Generation;

/// <summary>Populated by generated direct calls, never by assembly or attribute inspection.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SnapshotMetadata
{
    private static readonly ConcurrentDictionary<string, List<RegisteredTest>> Files = new(StringComparer.Ordinal);
    private static int _version;

    public static void Register(string file, int firstLine, int lastLine, SnapshotDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(descriptor);
        var list = Files.GetOrAdd(Normalize(file), static _ => new());
        lock (list)
        {
            if (list.Any(x => x.Start == firstLine && x.End == lastLine
                && x.Descriptor == descriptor))
            {
                return;
            }

            list.Add(new(firstLine, lastLine, descriptor));
        }
        Interlocked.Increment(ref _version);
    }

    /// <summary>Changes whenever a descriptor is added, so a cached view of the registry can tell it is stale.</summary>
    internal static int Version => Volatile.Read(ref _version);

    internal static SnapshotDescriptor[] All()
    {
        var all = new List<SnapshotDescriptor>();
        foreach (var list in Files.Values)
        {
            lock (list)
            {
                all.AddRange(list.Select(x => x.Descriptor));
            }
        }

        return all.Distinct().ToArray();
    }

    internal static SnapshotDescriptor? Find(string file, int line)
    {
        if (!Files.TryGetValue(Normalize(file), out var list))
        {
            return null;
        }

        lock (list)
        {
            var matches = list.Where(x => x.Start <= line && line <= x.End)
                .OrderBy(x => x.End - x.Start).ToArray();
            if (matches.Length == 0)
            {
                return null;
            }

            if (matches.Length > 1 && matches[0].End - matches[0].Start == matches[1].End - matches[1].Start
                && matches[0].Descriptor != matches[1].Descriptor)
            {
                throw new SnapshotConfigurationException("Ambiguous generated test identity. Supply SnapshotTestOptions.Identity.");
            }

            return matches[0].Descriptor;
        }
    }

    private static string Normalize(string file) => OperatingSystem.IsWindows()
        ? file.Replace('\\', '/').ToUpperInvariant() : file.Replace('\\', '/');
}
