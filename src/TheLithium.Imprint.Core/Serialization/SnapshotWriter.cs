using System.Text.Json;

namespace TheLithium.Imprint;

/// <summary>Writes exactly one JSON value through statically typed member access, without runtime reflection.</summary>
/// <typeparam name="T">The compile-time value type.</typeparam>
/// <param name="writer">JSON destination. Leave it open.</param>
/// <param name="value">Non-null captured value.</param>
/// <param name="context">Tracks paths, recursion, cycles, and serialization limits.</param>
public delegate void SnapshotWriter<in T>(Utf8JsonWriter writer, T value, SnapshotWriteContext context);
