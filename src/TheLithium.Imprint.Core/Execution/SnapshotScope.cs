namespace TheLithium.Imprint;

/// <summary>
/// An execution boundary for one test. Complete asserts and commits; Dispose only abandons.
/// Prefer Snapshots.Run/RunAsync so completion cannot be accidentally omitted.
/// </summary>
public sealed class SnapshotScope : IDisposable
{
    private enum State
    {
        Open, Completing, Completed, Faulted, Aborted
    }
    private readonly object _gate = new();
    private readonly EffectiveSettings _settings;
    private readonly SnapshotStore _store;
    private readonly BaselineState _baseline;
    private readonly SnapshotScope? _previous;
    private readonly List<CapturedValue> _values = [];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _automatic = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _executionId = Guid.NewGuid().ToString("N");
    private SnapshotUpdate _update;
    private ResolvedSnapshotComparison _comparison;
    private ResolvedSnapshotRepresentation _representation;
    private SnapshotStringContent _stringContent;
    private State _state;
    private bool _started;
    private bool _disposed;
    private int _unnamed;
    private long _bytes;
    private Exception? _captureFailure;
    private bool _artifactsWritten;
    private string? _artifactError;

    internal SnapshotScope(EffectiveSettings settings, SnapshotScope? previous)
    {
        _settings = settings;
        _previous = previous;
        _update = settings.Update;
        _comparison = settings.Comparison;
        _representation = settings.Representation;
        _stringContent = settings.StringContent;
        _store = new(settings);
        _baseline = _store.Read();
    }

    /// <summary>Absolute directory containing this test's baseline files.</summary>
    public string BaselineDirectory => _settings.TestDirectory;
    /// <summary>Fingerprint read when the scope began.</summary>
    public string BaselineFingerprint => _baseline.Fingerprint;
    /// <summary>Failure-artifact directory created when execution aborts or comparison fails.</summary>
    public string? ArtifactDirectory
    {
        get; private set;
    }
    /// <summary>Effective whole-test policy after environment and read-only enforcement.</summary>
    public SnapshotUpdate EffectiveUpdate => _settings.ResolveUpdate(SnapshotUpdate.Inherit, _update);

    /// <summary>Effective comparison fields. Assign a patch before capture; unset fields retain their current values.</summary>
    public SnapshotComparison Comparison
    {
        get
        {
            lock (_gate) { return _comparison.ToOptions(); }
        }
        set
        {
            lock (_gate) { ArgumentNullException.ThrowIfNull(value); EnsureConfigurable(); _comparison = _comparison.Apply(value); }
        }
    }

    /// <summary>Effective representation preferences. Assign a patch before capture; unset categories retain their current values.</summary>
    public SnapshotRepresentationOptions Representation
    {
        get
        {
            lock (_gate) { return _representation.ToOptions(); }
        }
        set
        {
            lock (_gate) { ArgumentNullException.ThrowIfNull(value); EnsureConfigurable(); _representation = _representation.Apply(value); }
        }
    }

    /// <summary>Interpret root strings as values or serialized JSON. Set before capture.</summary>
    public SnapshotStringContent StringContent
    {
        get
        {
            lock (_gate) { return _stringContent; }
        }
        set
        {
            lock (_gate) { EnsureConfigurable(); Settings.ValidateStringContent(value); _stringContent = value; }
        }
    }

    private void EnsureConfigurable()
    {
        EnsureOpen();
        if (_started)
        {
            throw new SnapshotConfigurationException("Set test configuration before the first capture.");
        }
    }

    /// <summary>Session-local. Must be set before the first capture.</summary>
    public SnapshotUpdate Update
    {
        get
        {
            lock (_gate)
            {
                return _update;
            }
        }
        set
        {
            lock (_gate)
            {
                EnsureOpen();
                Settings.ValidateUpdate(value);
                if (_started)
                {
                    throw new SnapshotConfigurationException("Set the test update policy before the first capture.");
                }

                _update = value == SnapshotUpdate.Inherit ? _settings.Update : value;
            }
        }
    }

    internal void Capture<T>(T value, string? explicitName, SnapshotOptions? requested,
        string? expression, SnapshotWriter<T>? writer)
    {
        lock (_gate)
        {
            EnsureOpen();
            _started = true;
            try
            {
                _settings.Cancellation.ThrowIfCancellationRequested();
                var options = requested ?? new();
                Settings.ValidateUpdate(options.Update);
                var comparison = _comparison.Apply(options.Comparison);
                var representation = new CaptureRepresentation(options.Format, options.StringContent ?? _stringContent,
                    _representation.Apply(options.Representation));
                Settings.ValidateStringContent(representation.StringContent);
                if (_values.Count >= SnapshotLimits.MaximumEntries)
                {
                    throw new SnapshotCaptureException($"A test may capture at most {SnapshotLimits.MaximumEntries} values.");
                }

                var name = ResolveName(explicitName, expression);
                var encoded = SnapshotEncoding.Encode(value, writer, _settings, representation);
                _bytes += SnapshotEncoding.Utf8.GetByteCount(encoded.Text);
                if (_bytes > Math.Max(_settings.MaxBytesPerSnapshot, SnapshotLimits.TestBytes))
                {
                    throw new SnapshotCaptureException("The captured test set exceeds its total size budget.");
                }

                var extension = SnapshotFileNames.Extension(encoded.Format);
                _values.Add(new(name, name + extension, encoded.Text, encoded.Format,
                    options.Update, comparison, options.Comparer));
            }
            catch (Exception error)
            {
                _captureFailure ??= error;
                throw;
            }
        }
    }

    private string ResolveName(string? explicitName, string? expression)
    {
        if (explicitName is not null)
        {
            var name = PortableNames.Segment(explicitName);
            if (!_names.Add(name))
            {
                throw new SnapshotCaptureException($"Duplicate explicit snapshot name (case-insensitive): {name}");
            }

            return name;
        }
        if (_settings.Naming == SnapshotNaming.ExplicitOnly)
        {
            throw new SnapshotCaptureException("This project requires an explicit name for every capture.");
        }

        var inferred = _settings.Naming == SnapshotNaming.Order ? null : PortableNames.Infer(expression);
        if (inferred is null)
        {
            string sequential;
            do
            {
                sequential = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"snapshot-{++_unnamed}");
            }
            while (!_names.Add(sequential));
            return sequential;
        }
        var basis = PortableNames.Segment(inferred);
        _automatic.TryGetValue(basis, out var count);
        string candidate;
        do
        {
            count++;
            candidate = count == 1 ? basis : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{basis}-{count}");
        } while (!_names.Add(candidate));
        _automatic[basis] = count;
        return candidate;
    }

    /// <summary>Compares all captures and commits authorized staged changes.</summary>
    /// <returns>A report containing every captured and unused entry.</returns>
    public SnapshotReport Complete()
    {
        lock (_gate)
        {
            EnsureOpen();
            _state = State.Completing;
            try
            {
                _settings.Cancellation.ThrowIfCancellationRequested();
                if (_captureFailure is not null)
                {
                    throw new SnapshotCaptureException("A capture failed earlier in this test. The test cannot approve snapshots even if that exception was caught.", _captureFailure);
                }

                if (_values.Count == 0 && !_settings.AllowEmpty)
                {
                    throw new SnapshotCaptureException("This snapshot test captured no values. Set AllowEmpty explicitly to approve an empty set.");
                }

                var plan = SnapshotPlan.Evaluate(_settings, _update, _values, _baseline);
                if (!plan.Authorized)
                {
                    var entries = plan.Entries(approved: false);
                    TryWriteArtifacts(entries, incomplete: false, error: null);
                    throw new SnapshotMismatchException(new(_settings.DisplayName, false, entries)
                    {
                        ArtifactDirectory = ArtifactDirectory,
                        ArtifactError = _artifactError
                    });
                }
                if (plan.Changes)
                {
                    _store.Commit(_baseline, plan.DesiredFiles());
                }
                else
                {
                    _store.VerifyUnchanged(_baseline);
                }
                _state = State.Completed;
                return new(_settings.DisplayName, true, plan.Entries(approved: true));
            }
            catch
            {
                _state = State.Faulted;
                throw;
            }
        }
    }

    /// <summary>Abandons the scope and preserves the original test exception when supplied.</summary>
    /// <param name="error">The exception that caused the test to abort.</param>
    public void Abort(Exception? error = null)
    {
        lock (_gate)
        {
            if (_state is State.Completed or State.Aborted)
            {
                return;
            }

            _state = State.Aborted;
            TryWriteArtifacts([], incomplete: true, error?.Message);
            if (error is not null)
            {
                try
                {
                    if (ArtifactDirectory is not null)
                    {
                        error.Data["TheLithium.Imprint.Artifacts"] = ArtifactDirectory;
                    }
                    if (_artifactError is not null)
                    {
                        error.Data["TheLithium.Imprint.ArtifactError"] = _artifactError;
                    }
                }
                catch (Exception) { /* Artifact diagnostics must not replace the original exception. */ }
            }
        }
    }

    private void TryWriteArtifacts(IReadOnlyList<SnapshotEntryResult> entries, bool incomplete, string? error)
    {
        if (_artifactsWritten)
        {
            return;
        }

        _artifactsWritten = true;
        try
        {
            ArtifactDirectory = SnapshotArtifacts.Write(new()
            {
                Settings = _settings,
                ExecutionId = _executionId,
                Values = _values,
                Baseline = _baseline,
                Entries = entries,
                Incomplete = incomplete,
                Error = error
            });
        }
        catch (Exception failure) { _artifactError = failure.Message; }
    }

    private void EnsureOpen()
    {
        if (_disposed || _state != State.Open)
        {
            throw new SnapshotConfigurationException("This snapshot scope is no longer open. Await all capture work before the test completes.");
        }
    }

    /// <summary>Closes the scope; an open or failed scope is abandoned without approval.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_state != State.Completed)
            {
                Abort();
            }

            _disposed = true;
            Snapshots.Restore(this, _previous);
        }
    }
}
