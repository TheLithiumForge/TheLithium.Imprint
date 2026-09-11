using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TheLithium.Imprint.Serialization;

internal static class SnapshotEncoding
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);

    internal static (string Text, SnapshotFormat Format) Encode<T>(T value, SnapshotFormat format,
        SnapshotWriter<T>? writer, EffectiveSettings settings)
    {
        if (format == SnapshotFormat.Auto)
        {
            format = value is string && writer is null ? settings.TextFormat : SnapshotFormat.Json;
        }

        if (format is SnapshotFormat.Snap or SnapshotFormat.Text)
        {
            if (value is not string text)
            {
                throw new SnapshotCaptureException("A text snapshot requires a non-null string. Format the value explicitly first.");
            }

            EnsureSize(text, settings.MaxBytesPerSnapshot);
            return (text, format);
        }
        if (format != SnapshotFormat.Json)
        {
            throw new SnapshotConfigurationException("Unsupported snapshot format.");
        }

        string json;
        if (value is string rawJson && writer is null)
        {
            EnsureSize(rawJson, settings.MaxBytesPerSnapshot);
            json = rawJson;
        }
        else
        {
            using var stream = new SizeLimitedStream(settings.MaxBytesPerSnapshot);
            using (var output = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.Default
            }))
            {
                var context = new SnapshotWriteContext(settings.MaxNestingDepth, settings.MaxValuesPerSnapshot, settings.Cancellation);
                if (writer is null)
                {
                    SnapshotWriters.Write(output, value, context);
                }
                else
                {
                    using var frame = context.Enter(value);
                    if (value is null)
                    {
                        output.WriteNullValue();
                    }
                    else
                    {
                        writer(output, value, context);
                    }
                }
                output.Flush();
            }
            json = Utf8.GetString(stream.ToArray());
        }
        return (CanonicalJson(json, settings.MaxNestingDepth, settings.MaxBytesPerSnapshot), SnapshotFormat.Json);
    }

    internal static string CanonicalJson(string json, int maxDepth, int maxBytes)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = maxDepth });
        using var stream = new SizeLimitedStream(maxBytes);
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true
        }))
        {
            WriteCanonical(writer, document.RootElement);
            writer.Flush();
        }
        var result = Utf8.GetString(stream.ToArray()).Replace("\r\n", "\n") + "\n";
        EnsureSize(result, maxBytes);
        return result;
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                string? previous = null;
                foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    if (property.Name == previous)
                    {
                        throw new SnapshotCaptureException("JSON contains a duplicate property: " + property.Name);
                    }

                    previous = property.Name;
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var element in value.EnumerateArray())
                {
                    WriteCanonical(writer, element);
                }

                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    internal static void EnsureSize(string text, int maximum)
    {
        if (Utf8.GetByteCount(text) > maximum)
        {
            throw new SnapshotCaptureException($"Snapshot exceeds its {maximum}-byte limit.");
        }
    }

}
