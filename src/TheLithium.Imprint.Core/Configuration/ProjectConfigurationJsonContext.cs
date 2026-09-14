using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheLithium.Imprint.Configuration;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(ProjectConfigurationFile))]
internal sealed partial class ProjectConfigurationJsonContext : JsonSerializerContext
{
    internal static ProjectConfigurationJsonContext Instance
    {
        get;
    } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        Converters =
        {
            new JsonStringEnumConverter<SnapshotUpdate>(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false),
            new JsonStringEnumConverter<SnapshotNaming>(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false),
            new JsonStringEnumConverter<SnapshotStringContent>(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false),
            new JsonStringEnumConverter<SnapshotEnumRepresentation>(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false),
            new JsonStringEnumConverter<SnapshotByteArrayRepresentation>(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false),
            new JsonStringEnumConverter<SnapshotDictionaryRepresentation>(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false)
        }
    });
}
