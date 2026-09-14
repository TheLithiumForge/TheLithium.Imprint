using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace TheLithium.Imprint.Generation;

/// <summary>Build-generated, reflection-free case identity from the actual parameter values.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SnapshotCases
{
    private const int MaximumCaseBytes = 64 * 1024;
    private const int MaximumLabelLength = 56;
    private const int HashLength = 12;

    public static string Create<T>(T arguments)
    {
        using var stream = new SizeLimitedStream(MaximumCaseBytes);
        using (var writer = new Utf8JsonWriter(stream))
        {
            var context = new SnapshotWriteContext(SnapshotLimits.DefaultDepth, SnapshotLimits.DefaultNodes, CancellationToken.None);
            SnapshotWriters.Write(writer, arguments, context);
        }
        var json = SnapshotEncoding.Utf8.GetString(stream.ToArray());
        var canonical = SnapshotEncoding.CanonicalJson(json, SnapshotLimits.DefaultDepth, MaximumCaseBytes);
        using var document = StrictJson.Parse(canonical, SnapshotLimits.DefaultDepth);
        var values = document.RootElement.EnumerateObject().ToArray();
        var label = string.Join(", ", values.Select(property => $"{property.Name}={Render(property.Value)}"));
        if (label.Length > MaximumLabelLength)
        {
            var length = char.IsHighSurrogate(label[MaximumLabelLength - 1]) ? MaximumLabelLength - 1 : MaximumLabelLength;
            // A truncated label no longer identifies the row, so the hash becomes the identity.
            return $"{label[..length]}~{PortableNames.Hash(canonical)[..HashLength]}";
        }

        // A readable label that already distinguishes every row needs no suffix, and folder paths
        // that hold committed files are worth keeping short.
        return values.All(property => Distinguishes(property.Value))
            ? label
            : $"{label}~{PortableNames.Hash(canonical)[..HashLength]}";
    }

    private static string Render(JsonElement value)
        => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();

    /// <summary>
    /// True when this value's rendered text can only have come from this value, so two different
    /// rows cannot produce the same label. Separators would break the label's own structure, and a
    /// string that reads as a JSON literal would be indistinguishable from that literal.
    /// </summary>
    private static bool Distinguishes(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = value.GetString();
        if (string.IsNullOrEmpty(text) || text.AsSpan().IndexOfAny(',', '=', '~') >= 0)
        {
            return false;
        }

        return !ReadsAsLiteral(text);
    }

    private static bool ReadsAsLiteral(string text)
        => text is "true" or "false" or "null"
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
}
