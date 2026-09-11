using System.ComponentModel;
using System.Text.Json;

namespace TheLithium.Imprint.Generation;

/// <summary>Build-generated, reflection-free case identity from the actual parameter values.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SnapshotCases
{
    private const int MaximumCaseBytes = 64 * 1024;
    private const int MaximumLabelLength = 56;

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
        using var document = JsonDocument.Parse(canonical);
        var label = string.Join(", ", document.RootElement.EnumerateObject().Select(property =>
        {
            var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText();
            return $"{property.Name}={value}";
        }));
        if (label.Length > MaximumLabelLength)
        {
            var length = char.IsHighSurrogate(label[MaximumLabelLength - 1]) ? MaximumLabelLength - 1 : MaximumLabelLength;
            label = label[..length];
        }
        return $"{label}~{PortableNames.Hash(canonical)[..12]}";
    }
}
