using System.Runtime.CompilerServices;

namespace TheLithium.Imprint;

/// <summary>Snapshot assertions that fit into ordinary synchronous and asynchronous test bodies.</summary>
public static class SnapshotExtensions
{
    /// <summary>Captures a value now and asserts its snapshot after the test body succeeds.</summary>
    /// <typeparam name="T">The compile-time type used by the generated, reflection-free writer.</typeparam>
    /// <param name="value">The current value. Later mutation cannot change this capture.</param>
    /// <param name="name">Optional filename without an extension. Defaults to the variable or member name, then capture order.</param>
    /// <param name="options">Optional format, equality, or update overrides for this entry.</param>
    /// <param name="expression">Compiler-supplied expression for name inference. Normally omit this argument.</param>
    /// <remarks>
    /// Missing snapshots pass and are created on successful completion. Existing differences produce an aggregate diff.
    /// A later exception, including in an awaited finally block, discards staged changes.
    /// The package establishes the lifetime during compilation. Framework teardown outside the method is outside that lifetime.
    /// </remarks>
    public static void AssertSnapshot<T>(this T value, string? name = null, SnapshotOptions? options = null,
        [CallerArgumentExpression(nameof(value))] string? expression = null)
        => Snapshots.Current.Capture(value, name, options, expression, writer: null);

    /// <summary>Stages this snapshot for creation or replacement after the test body succeeds.</summary>
    /// <typeparam name="T">The compile-time captured type.</typeparam>
    /// <param name="value">The value to capture now.</param>
    /// <param name="name">Optional filename without an extension; uses the same inference as AssertSnapshot.</param>
    /// <param name="options">Optional format and comparison overrides. This method requests All for this entry.</param>
    /// <param name="expression">Compiler-supplied expression for name inference.</param>
    /// <remarks>
    /// Other entries keep their own policy. This method does not prune unused snapshots.
    /// A run-wide Verify or CI/read-only policy still prevents writes.
    /// Change back to AssertSnapshot after reviewing the update.
    /// </remarks>
    public static void UpdateSnapshot<T>(this T value, string? name = null, SnapshotOptions? options = null,
        [CallerArgumentExpression(nameof(value))] string? expression = null)
        => Snapshots.Current.Capture(value, name, (options ?? new()) with
        {
            Update = SnapshotUpdate.All
        }, expression, writer: null);

    /// <summary>Captures a value using an explicit statically typed JSON writer.</summary>
    /// <typeparam name="T">The concrete captured type.</typeparam>
    /// <param name="value">The value to serialize now. Null writes JSON null without calling the writer.</param>
    /// <param name="writer">Writes exactly one JSON value and leaves the provided destination open.</param>
    /// <param name="name">Optional stable filename without an extension.</param>
    /// <param name="options">Optional overrides for this entry. An explicit writer uses JSON by default.</param>
    /// <param name="expression">Compiler-supplied expression for name inference.</param>
    /// <remarks>Use for private types, third-party contracts, or a deliberate projection. No reflective serializer is required.</remarks>
    public static void AssertSnapshot<T>(this T value, SnapshotWriter<T> writer,
        string? name = null, SnapshotOptions? options = null,
        [CallerArgumentExpression(nameof(value))] string? expression = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Snapshots.Current.Capture(value, name, options, expression, writer);
    }

    /// <summary>Stages an update using an explicit statically typed JSON writer.</summary>
    /// <typeparam name="T">The concrete captured type.</typeparam>
    /// <param name="value">The value to capture now.</param>
    /// <param name="writer">Writes exactly one JSON value and leaves the destination open.</param>
    /// <param name="name">Optional stable filename without an extension.</param>
    /// <param name="options">Optional format and comparison overrides. This method requests All for this entry.</param>
    /// <param name="expression">Compiler-supplied expression for name inference.</param>
    /// <remarks>Approval occurs only on successful method completion, subject to run-wide read-only policy.</remarks>
    public static void UpdateSnapshot<T>(this T value, SnapshotWriter<T> writer,
        string? name = null, SnapshotOptions? options = null,
        [CallerArgumentExpression(nameof(value))] string? expression = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Snapshots.Current.Capture(value, name, (options ?? new()) with { Update = SnapshotUpdate.All }, expression, writer);
    }
}
