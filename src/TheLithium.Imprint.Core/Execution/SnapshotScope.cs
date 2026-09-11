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
    private readonly List<CapturedValue> _values = new();
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _automatic = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _executionId = Guid.NewGuid().ToString("N");
    private SnapshotUpdate _update;
    private State _state;
    private bool _started;
    private bool _disposed;
    private int _unnamed;
    private long _bytes;
    private Exception? _captureFailure;
    private bool _artifactsWritten;

    internal SnapshotScope(EffectiveSettings settings, SnapshotScope? previous)
    {
        _settings = settings;
        _previous = previous;
        _update = settings.Update;
        _store = new(settings);
        _baseline = _store.Read();
    }

    public string BaselineDirectory => _settings.TestDirectory;
    public string BaselineFingerprint => _baseline.Fingerprint;
    public string? ArtifactDirectory
    {
        get; private set;
    }
    public SnapshotUpdate EffectiveUpdate => _settings.ResolveUpdate(SnapshotUpdate.Inherit, _update);

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
                var comparison = options.Comparison ?? _settings.Comparison;
                Settings.ValidateComparison(comparison);
                if (_values.Count >= SnapshotLimits.MaximumEntries)
                {
                    throw new SnapshotCaptureException("A test may capture at most 1024 values.");
                }

                var name = ResolveName(explicitName, expression);
                var encoded = SnapshotEncoding.Encode(value, options.Format, writer, _settings);
                _bytes += SnapshotEncoding.Utf8.GetByteCount(encoded.Text);
                if (_bytes > Math.Max(_settings.MaxBytes, SnapshotLimits.TestBytes))
                {
                    throw new SnapshotCaptureException("The captured test set exceeds its total size budget.");
                }

                var extension = encoded.Format switch
                {
                    SnapshotFormat.Json => ".json",
                    SnapshotFormat.Text => ".txt",
                    _ => ".snap"
                };
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
                throw new SnapshotCaptureException("Duplicate explicit snapshot name (case-insensitive): " + name);
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
                sequential = "snapshot-" + (++_unnamed).ToString(System.Globalization.CultureInfo.InvariantCulture);
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
            candidate = count == 1 ? basis : basis + "-" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        } while (!_names.Add(candidate));
        _automatic[basis] = count;
        return candidate;
    }

    /// <summary>Compares all entries and only commits if all differences are authorized.</summary>
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

                var desired = new Dictionary<string, string>(_baseline.Files, StringComparer.Ordinal);
                var used = new HashSet<string>(StringComparer.Ordinal);
                var entries = new List<SnapshotEntryResult>();
                var approvedEntries = new List<SnapshotEntryResult>();
                var authorized = true;
                var changes = false;
                foreach (var value in _values.OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    var policy = _settings.ResolveUpdate(value.Update, _update);
                    var candidates = _baseline.Files.Keys.Where(name => string.Equals(
                        Path.GetFileNameWithoutExtension(name), value.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (candidates.Length > 1)
                    {
                        throw new SnapshotConflictException("More than one baseline format exists for " + value.Name + ". Remove the ambiguity before updating.");
                    }

                    if (candidates.Length == 0)
                    {
                        var entry = new SnapshotEntryResult(value.Name, value.FileName, SnapshotStatus.Missing, "No baseline exists.");
                        entries.Add(entry);
                        approvedEntries.Add(entry with
                        {
                            Status = SnapshotStatus.Created,
                            Difference = null
                        });
                        if (policy is SnapshotUpdate.All or SnapshotUpdate.Missing)
                        {
                            desired[value.FileName] = value.Text;
                            changes = true;
                        }
                        else
                        {
                            authorized = false;
                        }

                        continue;
                    }
                    var existing = candidates[0];
                    used.Add(existing);
                    SnapshotComparisonResult comparison;
                    if (existing != value.FileName)
                    {
                        comparison = new(false, "Snapshot name or file format changed.");
                    }
                    else
                    {
                        comparison = (value.Comparer ?? DefaultSnapshotComparer.Instance)
                        .Compare(_baseline.Files[existing], value.Text, value.Format, value.Comparison);
                    }

                    var entryResult = new SnapshotEntryResult(value.Name, value.FileName,
                        comparison.Equal ? SnapshotStatus.Matched : SnapshotStatus.Changed, comparison.Difference);
                    entries.Add(entryResult);
                    if (!comparison.Equal && policy != SnapshotUpdate.All)
                    {
                        authorized = false;
                    }

                    if (policy == SnapshotUpdate.All && (existing != value.FileName || _baseline.Files[existing] != value.Text))
                    {
                        desired.Remove(existing);
                        desired[value.FileName] = value.Text;
                        changes = true;
                        approvedEntries.Add(entryResult with
                        {
                            Status = SnapshotStatus.Updated,
                            Difference = null
                        });
                    }
                    else
                    {
                        approvedEntries.Add(entryResult);
                    }
                }
                var testPolicy = _settings.ResolveUpdate(SnapshotUpdate.Inherit, _update);
                foreach (var existing in _baseline.Files.Keys.Where(x => !used.Contains(x)).Order(StringComparer.Ordinal))
                {
                    // New entries have no existing filename in the baseline and do not reach this loop.
                    var entry = new SnapshotEntryResult(Path.GetFileNameWithoutExtension(existing), existing,
                        SnapshotStatus.Unused, "This test no longer captures this entry.");
                    entries.Add(entry);
                    approvedEntries.Add(entry with
                    {
                        Status = SnapshotStatus.Removed,
                        Difference = null
                    });
                    if (testPolicy == SnapshotUpdate.All)
                    {
                        desired.Remove(existing);
                        changes = true;
                    }
                    else
                    {
                        authorized = false;
                    }
                }
                if (!authorized)
                {
                    var report = new SnapshotReport(_settings.DisplayName, false, entries.ToArray());
                    TryWriteArtifacts(entries, incomplete: false, error: null);
                    report = report with
                    {
                        ArtifactDirectory = ArtifactDirectory
                    };
                    throw new SnapshotMismatchException(report);
                }
                if (changes)
                {
                    _store.Commit(_baseline, desired);
                }
                else
                {
                    _store.VerifyUnchanged(_baseline);
                }

                _state = State.Completed;
                return new(_settings.DisplayName, true, approvedEntries.ToArray());
            }
            catch
            {
                _state = State.Faulted;
                throw;
            }
        }
    }

    /// <summary>Abandons approval and preserves the original test exception.</summary>
    public void Abort(Exception? error = null)
    {
        lock (_gate)
        {
            if (_state is State.Completed or State.Aborted)
            {
                return;
            }

            _state = State.Aborted;
            TryWriteArtifacts(Array.Empty<SnapshotEntryResult>(), incomplete: true, error?.Message);
            if (error is not null && ArtifactDirectory is not null)
            {
                try
                {
                    error.Data["TheLithium.Imprint.Artifacts"] = ArtifactDirectory;
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
            ArtifactDirectory = SnapshotArtifacts.Write(_settings, _executionId,
                _values, _baseline, entries, incomplete, error);
        }
        catch (Exception) { /* Best-effort diagnostics must never mask the original test failure. */ }
    }

    private void EnsureOpen()
    {
        if (_disposed || _state != State.Open)
        {
            throw new SnapshotConfigurationException("This snapshot scope is no longer open. Await all capture work before the test completes.");
        }
    }

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
