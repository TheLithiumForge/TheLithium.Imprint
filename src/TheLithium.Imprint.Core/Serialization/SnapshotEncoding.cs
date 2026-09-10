using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TheLithium.Imprint;

internal sealed record CapturedValue(string Name, string FileName, string Text,
    SnapshotFormat Format, SnapshotUpdate Update, SnapshotComparison Comparison, ISnapshotComparer? Comparer);

internal static class SnapshotEncoding
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);

    internal static (string Text, SnapshotFormat Format) Encode<T>(T value, SnapshotFormat format,
        SnapshotWriter<T>? writer, EffectiveSettings settings)
    {
        if (format == SnapshotFormat.Auto)
            format = value is string && writer is null ? settings.TextFormat : SnapshotFormat.Json;
        if (format is SnapshotFormat.Snap or SnapshotFormat.Text)
        {
            if (value is not string text)
                throw new SnapshotCaptureException("A text snapshot requires a non-null string. Format the value explicitly first.");
            EnsureSize(text, settings.MaxBytes);
            return (text, format);
        }
        if (format != SnapshotFormat.Json)
            throw new SnapshotConfigurationException("Unsupported snapshot format.");

        string json;
        if (value is string rawJson && writer is null)
        {
            EnsureSize(rawJson, settings.MaxBytes);
            json = rawJson;
        }
        else
        {
            using var stream = new LimitedStream(settings.MaxBytes);
            using (var output = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.Default
            }))
            {
                var context = new SnapshotWriteContext(settings.MaxDepth, settings.MaxNodes, settings.Cancellation);
                if (writer is null) SnapshotWriters.Write(output, value, context);
                else
                {
                    using var frame = context.Enter(value);
                    if (value is null) output.WriteNullValue();
                    else writer(output, value, context);
                }
                output.Flush();
            }
            json = Utf8.GetString(stream.ToArray());
        }
        return (CanonicalJson(json, settings.MaxDepth, settings.MaxBytes), SnapshotFormat.Json);
    }

    internal static string CanonicalJson(string json, int maxDepth, int maxBytes)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = maxDepth });
        using var stream = new LimitedStream(maxBytes);
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               { Indented = true }))
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
                        throw new SnapshotCaptureException("JSON contains a duplicate property: " + property.Name);
                    previous = property.Name;
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var element in value.EnumerateArray()) WriteCanonical(writer, element);
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
            throw new SnapshotCaptureException($"Snapshot exceeds its {maximum}-byte limit.");
    }

    private sealed class LimitedStream(int limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(count); base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length); base.Write(buffer);
        }
        public override void WriteByte(byte value)
        {
            Check(1); base.WriteByte(value);
        }
        private void Check(int additional)
        {
            if (Position + additional > limit)
                throw new SnapshotCaptureException($"Snapshot exceeds its {limit}-byte limit.");
        }
    }
}
