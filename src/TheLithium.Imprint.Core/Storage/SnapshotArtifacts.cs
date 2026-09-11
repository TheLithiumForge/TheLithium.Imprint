using System.Text.Json;

namespace TheLithium.Imprint.Storage;

internal static class SnapshotArtifacts
{
    private static readonly string RunId = Guid.NewGuid().ToString("N");

    internal static string Write(EffectiveSettings settings, string executionId,
        IReadOnlyList<CapturedValue> values, BaselineState baseline,
        IReadOnlyList<SnapshotEntryResult> entries, bool incomplete, string? error)
    {
        var directory = Path.Combine(settings.ArtifactRoot, RunId,
            PortableNames.Segment(settings.Identity.Suite),
            PortableNames.Segment(settings.Identity.Test), executionId);
        Directory.CreateDirectory(directory);
        foreach (var value in values)
        {
            var extension = Path.GetExtension(value.FileName);
            var name = Path.GetFileNameWithoutExtension(value.FileName);
            File.WriteAllText(Path.Combine(directory, name + ".received" + extension), value.Text, SnapshotEncoding.Utf8);
        }
        foreach (var entry in entries.Where(x => x.Status is SnapshotStatus.Changed or SnapshotStatus.Unused))
        {
            var existing = baseline.Files.Keys.FirstOrDefault(file => string.Equals(
                Path.GetFileNameWithoutExtension(file), entry.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && baseline.Files.TryGetValue(existing, out var expected))
            {
                var extension = Path.GetExtension(existing);
                var name = Path.GetFileNameWithoutExtension(existing);
                File.WriteAllText(Path.Combine(directory, name + ".expected" + extension), expected, SnapshotEncoding.Utf8);
            }
        }
        using var stream = File.Create(Path.Combine(directory, "run.json"));
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("version", 1);
        writer.WriteString("status", incomplete ? "incomplete" : "mismatch");
        writer.WriteString("test", settings.DisplayName);
        writer.WriteString("baselineDirectory", settings.TestDirectory);
        writer.WriteString("baselineFingerprint", baseline.Fingerprint);
        if (error is not null)
        {
            writer.WriteString("error", error);
        }

        writer.WriteStartArray("entries");
        foreach (var entry in entries)
        {
            writer.WriteStartObject();
            writer.WriteString("name", entry.Name);
            writer.WriteString("file", entry.FileName);
            writer.WriteString("status", SnapshotStatusText.Format(entry.Status));
            if (entry.Difference is not null)
            {
                writer.WriteString("difference", entry.Difference);
            }

            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        return directory;
    }
}
