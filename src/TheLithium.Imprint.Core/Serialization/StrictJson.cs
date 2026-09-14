using System.Text.Json;

namespace TheLithium.Imprint.Serialization;

/// <summary>Owns the JSON syntax contract shared by capture, comparison and configuration.</summary>
internal static class StrictJson
{
    internal static JsonDocument Parse(string json, int maxDepth, CancellationToken cancellation = default, int? maxNodes = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            MaxDepth = maxDepth,
            AllowDuplicateProperties = false,
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (maxNodes is { } remaining)
            {
                CountValues(document.RootElement, ref remaining, cancellation);
            }
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static void CountValues(JsonElement value, ref int remaining, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (--remaining < 0)
        {
            throw new SnapshotCaptureException("Snapshot emitted JSON node budget exceeded.");
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                CountValues(property.Value, ref remaining, cancellation);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray())
            {
                CountValues(child, ref remaining, cancellation);
            }
        }
    }
}
