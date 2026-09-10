using System.Text.Json;

namespace TheLithium.Imprint;

public enum SnapshotUpdate { Inherit = 0, Verify = 1, Missing = 2, All = 3 }
public enum SnapshotFormat { Auto = 0, Json = 1, Snap = 2, Text = 3 }
public enum SnapshotNaming { NameThenOrder = 0, Order = 1, ExplicitOnly = 2 }

/// <summary>Equality rules only. These settings never modify the saved representation.</summary>
public sealed record SnapshotComparison
{
    public decimal NumericTolerance { get; init; }
    public bool IgnoreArrayOrder { get; init; }
    public bool IgnoreStringCase { get; init; }
    public bool IgnoreLineEndings { get; init; } = true;
    public bool IgnoreTrailingWhitespace { get; init; }
    public int MaxUnorderedArrayLength { get; init; } = 256;
}

public sealed record SnapshotOptions
{
    public SnapshotUpdate Update { get; init; } = SnapshotUpdate.Inherit;
    public SnapshotFormat Format { get; init; } = SnapshotFormat.Auto;
    /// <summary>Replaces the inherited comparison object when specified.</summary>
    public SnapshotComparison? Comparison { get; init; }
    public ISnapshotComparer? Comparer { get; init; }
}

/// <summary>An explicit identity for custom runners, shared helpers and deployed executables.</summary>
public sealed record SnapshotTestIdentity(
    string ProjectDirectory,
    string SourceFile,
    string Suite,
    string Test,
    string? Case = null,
    string? Variant = null,
    string? LogicalId = null);

public sealed record SnapshotTestOptions
{
    public SnapshotUpdate Update { get; init; } = SnapshotUpdate.Inherit;
    public string? Name { get; init; }
    public string? Suite { get; init; }
    public string? Case { get; init; }
    public string? Variant { get; init; }
    public SnapshotTestIdentity? Identity { get; init; }
    /// <summary>Overrides the baseline root. Relative paths are project-relative.</summary>
    public string? RootDirectory { get; init; }
    public string? ArtifactDirectory { get; init; }
    public string? ConfigurationFile { get; init; }
    public SnapshotComparison? Comparison { get; init; }
    public SnapshotNaming? Naming { get; init; }
    public bool? AllowEmpty { get; init; }
    public int? MaxDepth { get; init; }
    public int? MaxNodes { get; init; }
    public int? MaxBytes { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>On a class, Name renames the suite. On a method, Name renames the test.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class SnapshotSettingsAttribute : Attribute
{
    public SnapshotUpdate Update { get; set; } = SnapshotUpdate.Inherit;
    public string? Name { get; set; }
}

/// <summary>Roots a static writer when the concrete type is hidden behind a generic helper.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SnapshotIncludeAttribute<T> : Attribute { }

public delegate void SnapshotWriter<in T>(Utf8JsonWriter writer, T value, SnapshotWriteContext context);

public interface ISnapshotComparer
{
    SnapshotComparisonResult Compare(string expected, string received,
        SnapshotFormat format, SnapshotComparison options);
}

public sealed record SnapshotComparisonResult(bool Equal, string? Difference = null)
{
    public static SnapshotComparisonResult Match { get; } = new(true);
}

public enum SnapshotStatus { Matched, Missing, Changed, Unused, Created, Updated, Removed }

public sealed record SnapshotEntryResult(string Name, string FileName,
    SnapshotStatus Status, string? Difference = null);

public sealed record SnapshotReport(string Test, bool Success,
    IReadOnlyList<SnapshotEntryResult> Entries, string? ArtifactDirectory = null);

public class SnapshotException : Exception
{
    public SnapshotException(string message) : base(message) { }
    public SnapshotException(string message, Exception inner) : base(message, inner) { }
}

public sealed class SnapshotMismatchException : SnapshotException
{
    public SnapshotReport Report { get; }
    public SnapshotMismatchException(SnapshotReport report)
        : base(ReportText.Format(report)) => Report = report;
}

public sealed class SnapshotConfigurationException : SnapshotException
{
    public SnapshotConfigurationException(string message) : base(message) { }
}

public sealed class SnapshotCaptureException : SnapshotException
{
    public SnapshotCaptureException(string message) : base(message) { }
    public SnapshotCaptureException(string message, Exception inner) : base(message, inner) { }
}

public sealed class SnapshotConflictException : SnapshotException
{
    public SnapshotConflictException(string message) : base(message) { }
}

internal static class ReportText
{
    public static string Format(SnapshotReport report)
    {
        var lines = new List<string> { $"Snapshot mismatch: {report.Test}", "" };
        foreach (var entry in report.Entries.Where(x => x.Status is SnapshotStatus.Missing
                     or SnapshotStatus.Changed or SnapshotStatus.Unused))
        {
            lines.Add($"{entry.FileName}: {SnapshotStatusText.Format(entry.Status)}");
            if (entry.Difference is not null) lines.Add("  " + entry.Difference.Replace("\n", "\n  "));
        }
        if (report.ArtifactDirectory is not null)
        {
            lines.Add("");
            lines.Add("Received files: " + report.ArtifactDirectory);
        }
        lines.Add("");
        lines.Add("Review the differences, then authorize updates with a method/class attribute,");
        lines.Add("snapshot options, or: imprint run --update all -- <your test command>");
        return string.Join(Environment.NewLine, lines);
    }
}
