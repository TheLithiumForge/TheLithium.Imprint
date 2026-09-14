using System.Text.Json;

namespace TheLithium.Imprint.Storage;

internal static class SnapshotArtifacts
{
    private static readonly string RunId = Guid.NewGuid().ToString("N");

    internal static string Write(ArtifactRequest request)
    {
        var settings = request.Settings;
        var directory = Path.Combine(settings.ArtifactRoot, RunId,
            PortableNames.Segment(settings.Identity.Suite),
            PortableNames.Segment(settings.Identity.Test), request.ExecutionId);
        SnapshotPaths.CheckPath(settings.Identity.ProjectDirectory, directory);
        Directory.CreateDirectory(directory);
        foreach (var value in request.Values)
        {
            var extension = Path.GetExtension(value.FileName);
            var name = Path.GetFileNameWithoutExtension(value.FileName);
            WriteText(Path.Combine(directory, $"{name}.received{extension}"), value.Text);
        }
        foreach (var entry in request.Entries.Where(x => x.Status is SnapshotStatus.Changed or SnapshotStatus.Unused))
        {
            var existing = request.Baseline.Files.Keys.FirstOrDefault(file => string.Equals(
                Path.GetFileNameWithoutExtension(file), entry.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && request.Baseline.Files.TryGetValue(existing, out var expected))
            {
                var extension = Path.GetExtension(existing);
                var name = Path.GetFileNameWithoutExtension(existing);
                WriteText(Path.Combine(directory, $"{name}.expected{extension}"), expected);
            }
        }
        var reportPath = Path.Combine(directory, "run.json");
        SnapshotPaths.CheckLink(reportPath);
        using var stream = File.Create(reportPath);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("version", 1);
        writer.WriteString("status", request.Incomplete ? "incomplete" : "mismatch");
        writer.WriteString("test", settings.DisplayName);
        writer.WriteString("baselineDirectory", settings.TestDirectory);
        writer.WriteString("baselineFingerprint", request.Baseline.Fingerprint);
        if (request.Error is not null)
        {
            writer.WriteString("error", request.Error);
        }

        writer.WriteStartArray("entries");
        foreach (var entry in request.Entries)
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

    private static void WriteText(string path, string text)
    {
        SnapshotPaths.CheckLink(path);
        File.WriteAllText(path, text, SnapshotEncoding.Utf8);
    }
}
