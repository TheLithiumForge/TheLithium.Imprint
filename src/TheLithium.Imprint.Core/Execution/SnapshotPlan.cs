namespace TheLithium.Imprint.Execution;

/// <summary>One decision list owns authorization, before/after reporting and desired file contents.</summary>
internal sealed class SnapshotPlan(BaselineState baseline, IReadOnlyList<SnapshotDecision> decisions)
{
    internal bool Authorized => decisions.All(decision => decision.Authorized);
    internal bool Changes => decisions.Any(decision => decision.Changes);
    internal SnapshotEntryResult[] Entries(bool approved) => decisions.Select(decision => decision.ReportEntry(approved)).ToArray();

    /// <summary>Comparer-produced files, named after the capture they explain, e.g. <c>settings.diff.png</c>.</summary>
    internal IEnumerable<ArtifactFile> Attachments()
    {
        foreach (var decision in decisions)
        {
            if (decision.Capture is { } capture)
            {
                foreach (var attachment in decision.Attachments)
                {
                    yield return new($"{Path.GetFileNameWithoutExtension(capture.FileName)}.{attachment.Suffix}", attachment.Content);
                }
            }
        }
    }

    internal Dictionary<string, SnapshotFile> DesiredFiles()
    {
        var files = new Dictionary<string, SnapshotFile>(baseline.Files, StringComparer.Ordinal);
        foreach (var decision in decisions.Where(decision => decision.Changes))
        {
            if (decision.ExistingFile is { } existing)
            {
                files.Remove(existing);
            }
            if (decision.Capture is { } capture)
            {
                files[capture.FileName] = capture.Content;
            }
        }
        return files;
    }

    internal static SnapshotPlan Evaluate(EffectiveSettings settings, SnapshotUpdate update,
        IReadOnlyList<CapturedValue> values, BaselineState baseline)
    {
        var decisions = new List<SnapshotDecision>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values.OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            settings.Cancellation.ThrowIfCancellationRequested();
            var policy = settings.ResolveUpdate(value.Update, update);
            var candidates = baseline.Files.Keys.Where(name => string.Equals(
                Path.GetFileNameWithoutExtension(name), value.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length > 1)
            {
                throw new SnapshotConflictException($"More than one baseline format exists for {value.Name}. Remove the ambiguity before updating.");
            }
            if (candidates.Length == 0)
            {
                decisions.Add(new(new(value.Name, value.FileName, SnapshotStatus.Missing, "No baseline exists."),
                    SnapshotStatus.Created, policy is SnapshotUpdate.All or SnapshotUpdate.Missing, Capture: value));
                continue;
            }
            var existing = candidates[0];
            used.Add(existing);
            var stored = baseline.Files[existing];
            var comparison = existing == value.FileName
                ? Compare(settings, stored, value)
                : new BinaryComparisonResult(false, "Snapshot name or file format changed.", []);
            settings.Cancellation.ThrowIfCancellationRequested();
            var entry = new SnapshotEntryResult(value.Name, value.FileName,
                comparison.Equal ? SnapshotStatus.Matched : SnapshotStatus.Changed, comparison.Difference);
            decisions.Add(new(entry, Writes(policy, existing, stored, value, comparison) ? SnapshotStatus.Updated : entry.Status,
                comparison.Equal || policy == SnapshotUpdate.All, Capture: value, ExistingFile: existing)
            {
                Attachments = comparison.Attachments
            });
        }
        var testPolicy = settings.ResolveUpdate(SnapshotUpdate.Inherit, update);
        foreach (var existing in baseline.Files.Keys.Where(name => !used.Contains(name)).Order(StringComparer.Ordinal))
        {
            settings.Cancellation.ThrowIfCancellationRequested();
            decisions.Add(new(new(Path.GetFileNameWithoutExtension(existing), existing,
                SnapshotStatus.Unused, "This test no longer captures this entry."),
                SnapshotStatus.Removed, testPolicy == SnapshotUpdate.All, ExistingFile: existing));
        }
        return new(baseline, decisions);
    }

    private static BinaryComparisonResult Compare(EffectiveSettings settings, SnapshotFile stored, CapturedValue value)
    {
        if (value.BinaryComparer is { } binary)
        {
            return stored.SameContent(value.Content)
                ? BinaryComparisonResult.Match
                : binary.Compare(stored.Content, value.Content.Content, settings.Cancellation);
        }

        var text = value.Comparer is null
            ? DefaultSnapshotComparer.Instance.Compare(stored.Text, value.Content.Text, value.Format, value.Comparison, settings.Cancellation)
            : value.Comparer.Compare(stored.Text, value.Content.Text, value.Format, value.Comparison);
        return new(text.Equal, text.Difference, []);
    }

    /// <summary>
    /// Under All, text is rewritten whenever it differs. A binary snapshot is rewritten only when it no longer matches,
    /// so a capture within tolerance, or a re-encoding of the same pixels, leaves the file and its history alone.
    /// </summary>
    private static bool Writes(SnapshotUpdate policy, string existing, SnapshotFile stored, CapturedValue value, BinaryComparisonResult comparison)
    {
        if (policy != SnapshotUpdate.All)
        {
            return false;
        }

        if (existing != value.FileName)
        {
            return true;
        }

        return value.BinaryComparer is null ? !stored.SameContent(value.Content) : !comparison.Equal;
    }
}
