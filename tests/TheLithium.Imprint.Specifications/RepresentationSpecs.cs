using System.Text.Json;
using TheLithium.Imprint;
using TheLithium.Imprint.Generation;

namespace TheLithium.Imprint.Specifications;

public static partial class Specs
{
    private static (string Name, Func<Task> Run)[] Representations =>
    [
        (nameof(GeneratedRepresentationsCompose), Check.Sync(GeneratedRepresentationsCompose)),
        (nameof(RepresentationDoesNotRenameCaseIdentity), Check.Sync(RepresentationDoesNotRenameCaseIdentity)),
        (nameof(ByteArrayViewsHandleEmptyAndPartialGroups), Check.Sync(ByteArrayViewsHandleEmptyAndPartialGroups)),
        (nameof(AnonymousDictionaryEntriesRemainSupported), Check.Sync(AnonymousDictionaryEntriesRemainSupported)),
        (nameof(EnumViewsPreserveFullUnderlyingRange), Check.Sync(EnumViewsPreserveFullUnderlyingRange))
    ];

    private static void GeneratedRepresentationsCompose()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"representation":{"enums":"number","byteArrays":"base64","dictionaries":"entries"}}""");
        var dictionary = new Dictionary<string, Mode> { ["mode"] = Mode.Fast };
        var value = new { Mode = Mode.Fast, Day = DayOfWeek.Friday, Bytes = new byte[] { 0, 255 }, Dictionary = dictionary };
        fixture.Run(() =>
        {
            value.AssertSnapshot("nested");
            Mode.Fast.AssertSnapshot("enum");
            dictionary.AssertSnapshot("dictionary");
            value.AssertSnapshot("override", new()
            {
                Representation = new()
                {
                    Enums = SnapshotEnumRepresentation.NameOrNumber,
                    ByteArrays = SnapshotByteArrayRepresentation.Numbers
                }
            });
            value.AssertSnapshot("unchanged");
        }, SnapshotUpdate.All);
        using var nested = JsonDocument.Parse(File.ReadAllText(fixture.FilePath("nested.json")));
        Check.Equal(1, nested.RootElement.GetProperty("Mode").GetInt32());
        Check.Equal(5, nested.RootElement.GetProperty("Day").GetInt32());
        Check.Equal("AP8=", nested.RootElement.GetProperty("Bytes").GetString());
        Check.Equal("mode", nested.RootElement.GetProperty("Dictionary")[0].GetProperty("Key").GetString());
        Check.Equal(1, nested.RootElement.GetProperty("Dictionary")[0].GetProperty("Value").GetInt32());
        Check.Equal("1\n", File.ReadAllText(fixture.FilePath("enum.json")));
        using var changed = JsonDocument.Parse(File.ReadAllText(fixture.FilePath("override.json")));
        Check.Equal("Fast", changed.RootElement.GetProperty("Mode").GetString());
        Check.Equal("Friday", changed.RootElement.GetProperty("Day").GetString());
        Check.Equal(255, changed.RootElement.GetProperty("Bytes")[1].GetInt32());
        Check.Equal("Fast", changed.RootElement.GetProperty("Dictionary")[0].GetProperty("Value").GetString());
        Check.Equal(File.ReadAllText(fixture.FilePath("nested.json")), File.ReadAllText(fixture.FilePath("unchanged.json")));
    }

    private static void RepresentationDoesNotRenameCaseIdentity()
    {
        var arguments = new { Mode = Mode.Fast, Bytes = new byte[] { 1, 2, 3 } };
        var before = SnapshotCases.Create(arguments);
        using var fixture = new Fixture();
        using var scope = Snapshots.Begin(fixture.Options(SnapshotUpdate.All));
        scope.Representation = new() { Enums = SnapshotEnumRepresentation.Number, ByteArrays = SnapshotByteArrayRepresentation.Base64 };
        arguments.AssertSnapshot("value");
        Check.Equal(before, SnapshotCases.Create(arguments));
        scope.Complete();
    }

    private static void ByteArrayViewsHandleEmptyAndPartialGroups()
    {
        foreach (var bytes in new byte[][] { [], [1], [1, 2], [1, 2, 3], [1, 2, 3, 4] })
        {
            using var fixture = new Fixture();
            fixture.Run(() => bytes.AssertSnapshot("bytes", new()
            {
                Representation = new()
                {
                    ByteArrays = SnapshotByteArrayRepresentation.Base64
                }
            }), SnapshotUpdate.All);
            using var json = JsonDocument.Parse(File.ReadAllText(fixture.FilePath("bytes.json")));
            Check.True(bytes.SequenceEqual(Convert.FromBase64String(json.RootElement.GetString() ?? throw new Exception("Expected base64 string."))));
        }
    }

    private static void AnonymousDictionaryEntriesRemainSupported()
    {
        using var fixture = new Fixture();
        var items = new[] { new { Name = "a", Mode = Mode.Fast } }.ToDictionary(item => item.Name);
        fixture.Run(() => items.AssertSnapshot("items", new()
        {
            Representation = new()
            {
                Dictionaries = SnapshotDictionaryRepresentation.Entries,
                Enums = SnapshotEnumRepresentation.Number
            }
        }), SnapshotUpdate.All);
        using var document = JsonDocument.Parse(File.ReadAllText(fixture.FilePath("items.json")));
        Check.Equal(1, document.RootElement[0].GetProperty("Value").GetProperty("Mode").GetInt32());
    }

    private static void EnumViewsPreserveFullUnderlyingRange()
    {
        using var fixture = new Fixture();
        fixture.Run(() =>
        {
            SignedCode.Minimum.AssertSnapshot("signed", new() { Representation = new() { Enums = SnapshotEnumRepresentation.Number } });
            UnsignedCode.Maximum.AssertSnapshot("unsigned", new() { Representation = new() { Enums = SnapshotEnumRepresentation.Number } });
            ((SignedCode)(-1)).AssertSnapshot("unnamed");
            Mode.Fast.AssertSnapshot(static (writer, _, _) => writer.WriteStringValue("custom"), "custom",
                new()
                {
                    Representation = new()
                    {
                        Enums = SnapshotEnumRepresentation.Number
                    }
                });
        }, SnapshotUpdate.All);
        Check.Equal("-9223372036854775808\n", File.ReadAllText(fixture.FilePath("signed.json")));
        Check.Equal("18446744073709551615\n", File.ReadAllText(fixture.FilePath("unsigned.json")));
        Check.Equal("-1\n", File.ReadAllText(fixture.FilePath("unnamed.json")));
        Check.Equal("\"custom\"\n", File.ReadAllText(fixture.FilePath("custom.json")));
    }
}

internal enum SignedCode : long
{
    Minimum = long.MinValue
}
internal enum UnsignedCode : ulong
{
    Maximum = ulong.MaxValue
}
