using System.Globalization;

namespace TheLithium.Imprint;

/// <summary>Tracks depth, cycles, property paths and a total node budget during eager capture.</summary>
public sealed class SnapshotWriteContext
{
    private readonly HashSet<object> _active = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _path = [];
    private readonly int _maxDepth;
    private readonly int _maxNodes;
    private readonly CancellationToken _cancellation;
    private int _depth;
    private int _nodes;

    internal SnapshotWriteContext(int maxDepth, int maxNodes, CancellationToken cancellation, ResolvedSnapshotRepresentation? representation = null)
    {
        (_maxDepth, _maxNodes, _cancellation) = (maxDepth, maxNodes, cancellation);
        Representation = representation ?? ResolvedSnapshotRepresentation.Defaults;
    }

    /// <summary>Resolved representation preferences shared by every value in this capture.</summary>
    public ResolvedSnapshotRepresentation Representation
    {
        get;
    }

    /// <summary>JSON-style member path for the value currently being written.</summary>
    public string Path => $"${string.Concat(_path)}";

    /// <summary>Pushes a named member onto the current error path.</summary>
    /// <param name="member">The member name.</param>
    /// <returns>A disposable frame that restores the previous path.</returns>
    public IDisposable At(string member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var escapedMember = member.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _path.Add($"[\"{escapedMember}\"]");
        return new PathFrame(this);
    }

    /// <summary>Pushes an array index onto the current error path.</summary>
    /// <param name="index">The zero-based array index.</param>
    /// <returns>A disposable frame that restores the previous path.</returns>
    public IDisposable At(int index)
    {
        _path.Add(string.Create(CultureInfo.InvariantCulture, $"[{index}]"));
        return new PathFrame(this);
    }

    internal IDisposable Enter<T>(T value)
    {
        _cancellation.ThrowIfCancellationRequested();
        if (++_nodes > _maxNodes)
        {
            throw new SnapshotCaptureException($"Snapshot node budget exceeded at {Path}.");
        }

        if (_depth + 1 > _maxDepth)
        {
            throw new SnapshotCaptureException($"Snapshot depth budget exceeded at {Path}.");
        }

        // Static type classification is supported by both JIT and Native AOT.
        // Structs still consume budgets; reference-valued descendants enter separately.
        // Reference-typed contracts retain identity tracking, even for boxed structs.
        object? reference = typeof(T).IsValueType ? null : value;
        if (reference is not null && !_active.Add(reference))
        {
            throw new SnapshotCaptureException($"Reference cycle detected at {Path}.");
        }

        _depth++;
        return new ValueFrame(this, reference);
    }

    private sealed class ValueFrame(SnapshotWriteContext owner, object? value) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner._depth--;
            if (value is not null)
            {
                owner._active.Remove(value);
            }
        }
    }

    private sealed class PathFrame(SnapshotWriteContext owner) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner._path.RemoveAt(owner._path.Count - 1);
        }
    }
}
