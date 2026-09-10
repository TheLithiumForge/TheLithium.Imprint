using System.Collections.Concurrent;
using System.ComponentModel;

namespace TheLithium.Imprint.Generation;

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SnapshotDescriptor(string ProjectDirectory, string ProjectName,
    string SourceFile, string Suite, string Test, string LogicalId,
    SnapshotUpdate Update, bool RequiresCase);

/// <summary>Populated by generated direct calls, never by assembly or attribute inspection.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SnapshotMetadata
{
    private sealed record Entry(int Start, int End, SnapshotDescriptor Descriptor);
    private static readonly ConcurrentDictionary<string, List<Entry>> Files = new(StringComparer.Ordinal);

    public static void Register(string file, int firstLine, int lastLine, SnapshotDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var list = Files.GetOrAdd(Normalize(file), static _ => new());
        lock (list)
        {
            if (list.Any(x => x.Start == firstLine && x.End == lastLine
                && x.Descriptor == descriptor)) return;
            list.Add(new(firstLine, lastLine, descriptor));
        }
    }

    internal static SnapshotDescriptor? Find(string file, int line)
    {
        if (!Files.TryGetValue(Normalize(file), out var list)) return null;
        lock (list)
        {
            var matches = list.Where(x => x.Start <= line && line <= x.End)
                .OrderBy(x => x.End - x.Start).ToArray();
            if (matches.Length == 0) return null;
            if (matches.Length > 1 && matches[0].End - matches[0].Start == matches[1].End - matches[1].Start
                && matches[0].Descriptor != matches[1].Descriptor)
                throw new SnapshotConfigurationException("Ambiguous generated test identity. Supply SnapshotTestOptions.Identity.");
            return matches[0].Descriptor;
        }
    }

    private static string Normalize(string file) => OperatingSystem.IsWindows()
        ? file.Replace('\\', '/').ToUpperInvariant() : file.Replace('\\', '/');
}

/// <summary>Inference-only shapes for anonymous types. Values are never inspected.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class Shapes
{
    public static T[] Array<T>(T sample) => default!;
    public static T[,] Array2<T>(T sample) => default!;
    public static T[,,] Array3<T>(T sample) => default!;
    public static List<T> List<T>(T sample) => default!;
    public static IEnumerable<T> Enumerable<T>(T sample) => default!;
    public static IList<T> IList<T>(T sample) => default!;
    public static ICollection<T> ICollection<T>(T sample) => default!;
    public static IReadOnlyList<T> ReadOnlyList<T>(T sample) => default!;
    public static IReadOnlyCollection<T> ReadOnlyCollection<T>(T sample) => default!;
    public static HashSet<T> HashSet<T>(T sample) => default!;
    public static ISet<T> ISet<T>(T sample) => default!;
    public static Dictionary<TKey, TValue> Dictionary<TKey, TValue>(TKey key, TValue value) where TKey : notnull => default!;
    public static IDictionary<TKey, TValue> IDictionary<TKey, TValue>(TKey key, TValue value) where TKey : notnull => default!;
    public static IReadOnlyDictionary<TKey, TValue> ReadOnlyDictionary<TKey, TValue>(TKey key, TValue value) where TKey : notnull => default!;
    public static KeyValuePair<TKey, TValue> Pair<TKey, TValue>(TKey key, TValue value) => default;
}
