namespace TheLithium.Imprint.Execution;

/// <summary>One decision list owns authorization, before/after reporting and desired file contents.</summary>
internal sealed class SnapshotPlan(BaselineState baseline, IReadOnlyList<SnapshotDecision> decisions)
{
    internal bool Authorized => decisions.All(decision => decision.Authorized);
    internal bool Changes => decisions.Any(decision => decision.Changes);
    internal SnapshotEntryResult[] Entries(bool approved) => decisions.Select(decision => decision.ReportEntry(approved)).ToArray();

    internal Dictionary<string, string> DesiredFiles()
    {
        var files = new Dictionary<string, string>(baseline.Files, StringComparer.Ordinal);
        foreach (var decision in decisions.Where(decision => decision.Changes))
        {
            if (decision.ExistingFile is { } existing)
            {
                files.Remove(existing);
            }
            if (decision.Capture is { } capture)
            {
                files[capture.FileName] = capture.Text;
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
            SnapshotComparisonResult comparison;
            if (existing != value.FileName)
            {
                comparison = new(false, "Snapshot name or file format changed.");
            }
            else
            {
                comparison = value.Comparer is null
                    ? DefaultSnapshotComparer.Instance.Compare(baseline.Files[existing], value.Text, value.Format, value.Comparison, settings.Cancellation)
                    : value.Comparer.Compare(baseline.Files[existing], value.Text, value.Format, value.Comparison);
                settings.Cancellation.ThrowIfCancellationRequested();
            }
            var entry = new SnapshotEntryResult(value.Name, value.FileName,
                comparison.Equal ? SnapshotStatus.Matched : SnapshotStatus.Changed, comparison.Difference);
            var writes = policy == SnapshotUpdate.All && (existing != value.FileName || baseline.Files[existing] != value.Text);
            decisions.Add(new(entry, writes ? SnapshotStatus.Updated : entry.Status,
                comparison.Equal || policy == SnapshotUpdate.All, Capture: value, ExistingFile: existing));
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
}
