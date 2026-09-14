namespace TheLithium.Imprint;

/// <summary>Requests a generated writer for a concrete type used through a generic helper.</summary>
/// <typeparam name="T">The concrete type to include in the generated writer registry.</typeparam>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SnapshotIncludeAttribute<T> : Attribute;
