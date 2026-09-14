using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TheLithium.Imprint.Serialization;

internal static class SnapshotEncoding
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);

    internal static (string Text, SnapshotFormat Format) Encode<T>(T value, SnapshotWriter<T>? writer,
        EffectiveSettings settings, CaptureRepresentation representation)
    {
        var format = representation.Format;
        if (representation.StringContent == SnapshotStringContent.Json)
        {
            if (value is not string supplied || writer is not null || format is not (SnapshotFormat.Auto or SnapshotFormat.Json))
            {
                throw new SnapshotConfigurationException("JSON string content requires a non-null string, Auto/Json format and no explicit writer.");
            }
            EnsureSize(supplied, settings.MaxBytesPerSnapshot);
            try
            {
                using var document = StrictJson.Parse(supplied, settings.MaxNestingDepth, settings.Cancellation, settings.MaxValuesPerSnapshot);
                return (supplied, SnapshotFormat.Json);
            }
            catch (JsonException error)
            {
                throw new SnapshotCaptureException($"Invalid supplied JSON: {error.Message}", error);
            }
        }
        if (format == SnapshotFormat.Auto)
        {
            format = value is string && writer is null ? SnapshotFormat.Text : SnapshotFormat.Json;
        }

        if (format == SnapshotFormat.Text)
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
        {
            using var stream = new SizeLimitedStream(settings.MaxBytesPerSnapshot);
            using (var output = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }))
            {
                var context = new SnapshotWriteContext(settings.MaxNestingDepth, settings.MaxValuesPerSnapshot,
                    settings.Cancellation, representation.Options);
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
        try
        {
            return (CanonicalJson(json, settings.MaxNestingDepth, settings.MaxBytesPerSnapshot,
                settings.MaxValuesPerSnapshot, settings.Cancellation), SnapshotFormat.Json);
        }
        catch (JsonException error)
        {
            throw new SnapshotCaptureException($"Invalid JSON snapshot: {error.Message}", error);
        }
    }

    internal static string CanonicalJson(string json, int maxDepth, int maxBytes,
        int maxNodes = SnapshotLimits.DefaultNodes, CancellationToken cancellation = default)
    {
        EnsureSize(json, maxBytes);
        using var document = StrictJson.Parse(json, maxDepth, cancellation, maxNodes);
        return FormatDocument(document.RootElement, maxBytes, cancellation);
    }

    internal static string FormatDocument(JsonElement value, int maxBytes, CancellationToken cancellation)
    {
        using var stream = new SizeLimitedStream(maxBytes);
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = true,
            NewLine = "\n"
        }))
        {
            WriteCanonical(writer, value, cancellation);
            writer.Flush();
        }
        stream.WriteByte((byte)'\n');
        cancellation.ThrowIfCancellationRequested();
        return Utf8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value, cancellation);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var element in value.EnumerateArray())
                {
                    WriteCanonical(writer, element, cancellation);
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
