using System.Text.Json;
using System.Text.Json.Serialization;
using TheLithium.Imprint;

[assembly: SnapshotInclude<ExternalConsumer.IncludedPayload>]

namespace ExternalConsumer;

public enum Mode
{
    None, Ready
}
public sealed record Payload(int Id, Mode Mode, byte[] Bytes, Dictionary<string, Mode> Labels);
public sealed record IncludedPayload(int Id);
public sealed record RegisteredId(int Id);
public sealed record WirePayload([property: JsonPropertyName("wire_id")] int Id);

[JsonSerializable(typeof(WirePayload))]
internal sealed partial class WireJsonContext : JsonSerializerContext;

internal sealed class InspectingComparer(Action<ResolvedSnapshotComparison> inspect) : ISnapshotComparer
{
    public SnapshotComparisonResult Compare(string expected, string received, SnapshotFormat format, ResolvedSnapshotComparison options)
    {
        inspect(options);
        return new(true);
    }
}
