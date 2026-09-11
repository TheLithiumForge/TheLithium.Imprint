using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace TheLithium.Imprint.Generation;

/// <summary>Inference-only shapes for anonymous types. Values are never inspected.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class Shapes
{
    [return: MaybeNull] public static T[] Array<T>(T sample) => default;
    [return: MaybeNull] public static T[,] Array2<T>(T sample) => default;
    [return: MaybeNull] public static T[,,] Array3<T>(T sample) => default;
    [return: MaybeNull] public static List<T> List<T>(T sample) => default;
    [return: MaybeNull] public static IEnumerable<T> Enumerable<T>(T sample) => default;
    [return: MaybeNull] public static IList<T> IList<T>(T sample) => default;
    [return: MaybeNull] public static ICollection<T> ICollection<T>(T sample) => default;
    [return: MaybeNull] public static IReadOnlyList<T> ReadOnlyList<T>(T sample) => default;
    [return: MaybeNull] public static IReadOnlyCollection<T> ReadOnlyCollection<T>(T sample) => default;
    [return: MaybeNull] public static HashSet<T> HashSet<T>(T sample) => default;
    [return: MaybeNull] public static ISet<T> ISet<T>(T sample) => default;
    [return: MaybeNull] public static Dictionary<TKey, TValue> Dictionary<TKey, TValue>(TKey key, TValue value) where TKey : notnull => default;
    [return: MaybeNull] public static IDictionary<TKey, TValue> IDictionary<TKey, TValue>(TKey key, TValue value) where TKey : notnull => default;
    [return: MaybeNull] public static IReadOnlyDictionary<TKey, TValue> ReadOnlyDictionary<TKey, TValue>(TKey key, TValue value) where TKey : notnull => default;
    public static KeyValuePair<TKey, TValue> Pair<TKey, TValue>(TKey key, TValue value) => default;
}
