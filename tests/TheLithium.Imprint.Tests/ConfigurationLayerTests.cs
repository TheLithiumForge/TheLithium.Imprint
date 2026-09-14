using System.Text.Json;
using TheLithium.Imprint.Configuration;
using TheLithium.Imprint.Specifications;
using Xunit;

namespace TheLithium.Imprint.Tests;

public sealed class ConfigurationLayerTests
{
    [Fact]
    public void JsonSnapshotsKeepReadableTextAndRoundTripEscapedCharacters()
    {
        using var fixture = new Fixture();
        const string text = "it's <b>café — naïve</b> & 注文が完了しました \"quoted\" C:\\temp\\file.txt\t\n\b\f";
        var value = new Dictionary<string, string> { ["注文"] = text };
        fixture.Run(() => value.AssertSnapshot("value"), SnapshotUpdate.All);
        var json = File.ReadAllText(fixture.FilePath("value.json"));
        Assert.Contains("\"注文\": \"it's <b>café — naïve</b> & 注文が完了しました", json);
        Assert.Contains("\\\"quoted\\\" C:\\\\temp\\\\file.txt\\t\\n\\b\\f", json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(text, document.RootElement.GetProperty("注文").GetString());
    }

    [Fact]
    public void JsonByteBudgetUsesReadableUtf8DuringInitialCapture()
    {
        using var fixture = new Fixture();
        var value = new string('é', 20);
        Snapshots.Run(() => value.AssertSnapshot("value", new() { Format = SnapshotFormat.Json }),
            fixture.Options(SnapshotUpdate.All) with
            {
                MaxBytesPerSnapshot = 43
            });
        Assert.Equal($"\"{value}\"\n", File.ReadAllText(fixture.FilePath("value.json")));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("{\"a\":1}")]
    [InlineData("first\nsecond")]
    public void AutomaticStringsStayTextAndExplicitJsonCapturesAStringValue(string value)
    {
        using var fixture = new Fixture();
        fixture.Run(() =>
        {
            value.AssertSnapshot("automatic");
            value.AssertSnapshot("json", new() { Format = SnapshotFormat.Json });
        }, SnapshotUpdate.All);
        Assert.Equal(value, File.ReadAllText(fixture.FilePath("automatic.txt")));
        using var document = JsonDocument.Parse(File.ReadAllText(fixture.FilePath("json.json")));
        Assert.Equal(JsonValueKind.String, document.RootElement.ValueKind);
        Assert.Equal(value, document.RootElement.GetString());
    }

    [Theory]
    [InlineData(" {\"b\":2,\"a\":1} \r\n")]
    [InlineData("[1,1]")]
    [InlineData("1.0")]
    [InlineData("true")]
    [InlineData("null")]
    public void SuppliedJsonPreservesItsContentsThroughBothEntryPoints(string json)
    {
        using var fixture = new Fixture();
        fixture.Run(() => json.UpdateSnapshot("value", new() { StringContent = SnapshotStringContent.Json }));
        fixture.Run(() => json.AssertSnapshot("value", new() { StringContent = SnapshotStringContent.Json }));
        Assert.Equal(json, File.ReadAllText(fixture.FilePath("value.json")));
    }

    [Fact]
    public void NullDefaultsToJsonAndTextRejectsIt()
    {
        using var fixture = new Fixture();
        string? value = null;
        fixture.Run(() => value.AssertSnapshot("value"), SnapshotUpdate.All);
        Assert.Equal("null\n", File.ReadAllText(fixture.FilePath("value.json")));
        Assert.Throws<SnapshotCaptureException>(() => fixture.Run(() =>
            value.AssertSnapshot("text", new()
            {
                Format = SnapshotFormat.Text
            }), SnapshotUpdate.All));
    }

    [Fact]
    public void ContradictoryJsonInterpretationPoisonsTheScope()
    {
        Action[] captures =
        [
            () => "1".AssertSnapshot("value", new() { Format = SnapshotFormat.Text, StringContent = SnapshotStringContent.Json }),
            () => 1.AssertSnapshot("value", new() { StringContent = SnapshotStringContent.Json }),
            () => "1".AssertSnapshot(static (writer, value, _) => writer.WriteStringValue(value), "value", new() { StringContent = SnapshotStringContent.Json }),
            () => "1".AssertSnapshot("value", new() { StringContent = (SnapshotStringContent)99 }),
            () => "1".AssertSnapshot("value", new() { Format = (SnapshotFormat)99 })
        ];
        foreach (var capture in captures)
        {
            using var fixture = new Fixture();
            using var scope = Snapshots.Begin(fixture.Options(SnapshotUpdate.All));
            Assert.Throws<SnapshotConfigurationException>(capture);
            Assert.Throws<SnapshotCaptureException>(() => scope.Complete());
            Assert.False(File.Exists(fixture.FilePath("value.json")));
        }
    }

    [Fact]
    public void ComparisonPatchesPreserveOtherFieldsAndExplicitResets()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"comparison":{"numericTolerance":2,"ignoreStringCase":true,"ignoreLineEndings":false}}""");
        using var scope = Snapshots.Begin(fixture.Options(SnapshotUpdate.All) with
        {
            Comparison = new()
            {
                NumericTolerance = 0,
                IgnoreStringCase = false
            }
        });
        Assert.Equal(0, scope.Comparison.NumericTolerance);
        Assert.False(scope.Comparison.IgnoreStringCase);
        Assert.False(scope.Comparison.IgnoreLineEndings);
        scope.Comparison = new() { IgnoreTrailingWhitespace = true };
        Assert.False(scope.Comparison.IgnoreLineEndings);
        "hello".AssertSnapshot("value");
        Assert.Throws<SnapshotConfigurationException>(() => scope.Comparison = new());
        Assert.Throws<SnapshotConfigurationException>(() => scope.Representation = new());
        Assert.Throws<SnapshotConfigurationException>(() => scope.StringContent = SnapshotStringContent.Json);
        scope.Complete();
    }

    [Fact]
    public void RepresentationPatchesInheritByCategoryAndKeepAssertionsIsolated()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"representation":{"enums":"number","byteArrays":"base64","dictionaries":"entries"}}""");
        var defaults = new SnapshotRepresentationOptions { Enums = SnapshotEnumRepresentation.NameOrNumber };
        using var scope = Snapshots.Begin(fixture.Options(SnapshotUpdate.All) with { Representation = defaults });
        Assert.Equal(SnapshotEnumRepresentation.NameOrNumber, scope.Representation.Enums);
        Assert.Equal(SnapshotByteArrayRepresentation.Base64, scope.Representation.ByteArrays);
        Assert.Equal(SnapshotDictionaryRepresentation.Entries, scope.Representation.Dictionaries);
        scope.Representation = new() { ByteArrays = SnapshotByteArrayRepresentation.Numbers };
        scope.Representation = new() { Dictionaries = SnapshotDictionaryRepresentation.Automatic };

        var value = new { Day = DayOfWeek.Friday, Bytes = new byte[] { 0, 255 }, Labels = new Dictionary<string, DayOfWeek> { ["day"] = DayOfWeek.Friday } };
        value.AssertSnapshot("before");
        value.AssertSnapshot("override", new()
        {
            Representation = defaults with
            {
                Enums = SnapshotEnumRepresentation.Number,
                ByteArrays = SnapshotByteArrayRepresentation.Base64
            }
        });
        value.AssertSnapshot("after");
        scope.Complete();

        using var before = JsonDocument.Parse(File.ReadAllText(fixture.FilePath("before.json")));
        Assert.Equal("Friday", before.RootElement.GetProperty("Day").GetString());
        Assert.Equal(255, before.RootElement.GetProperty("Bytes")[1].GetInt32());
        Assert.Equal("Friday", before.RootElement.GetProperty("Labels").GetProperty("day").GetString());
        using var changed = JsonDocument.Parse(File.ReadAllText(fixture.FilePath("override.json")));
        Assert.Equal(5, changed.RootElement.GetProperty("Day").GetInt32());
        Assert.Equal("AP8=", changed.RootElement.GetProperty("Bytes").GetString());
        Assert.Equal(5, changed.RootElement.GetProperty("Labels").GetProperty("day").GetInt32());
        Assert.Equal(File.ReadAllText(fixture.FilePath("before.json")), File.ReadAllText(fixture.FilePath("after.json")));
        Assert.Equal(SnapshotEnumRepresentation.NameOrNumber, defaults.Enums);
        Assert.Null(defaults.ByteArrays);
        Assert.Null(defaults.Dictionaries);
    }

    [Fact]
    public void InvalidRepresentationPreferencesAreRejectedAtEachScope()
    {
        SnapshotRepresentationOptions[] invalid =
        [
            new() { Enums = (SnapshotEnumRepresentation)99 },
            new() { ByteArrays = (SnapshotByteArrayRepresentation)99 },
            new() { Dictionaries = (SnapshotDictionaryRepresentation)99 }
        ];
        foreach (var patch in invalid)
        {
            using var fixture = new Fixture();
            Assert.Throws<SnapshotConfigurationException>(() => Snapshots.Begin(fixture.Options() with { Representation = patch }));
            using var scope = Snapshots.Begin(fixture.Options(SnapshotUpdate.All));
            Assert.Throws<SnapshotConfigurationException>(() => scope.Representation = patch);
            Assert.Throws<SnapshotConfigurationException>(() => 1.AssertSnapshot("invalid", new() { Representation = patch }));
            Assert.Throws<SnapshotCaptureException>(() => scope.Complete());
        }
    }

    [Theory]
    [InlineData("{\"limits\":{\"maxNestingDepth\":0}}")]
    [InlineData("{\"comparison\":{\"ignoreStringCase\":null}}")]
    [InlineData("{\"representation\":{\"enums\":1}}")]
    [InlineData("{\"representation\":{\"enums\":null}}")]
    [InlineData("{\"representation\":{\"unknown\":true}}")]
    [InlineData("{\"files\":{\"textFileExtension\":\"txt\"}}")]
    [InlineData("{\"representation\":null}")]
    public void InvalidBroaderConfigurationIsNotHiddenByOverrides(string json)
    {
        using var fixture = new Fixture();
        fixture.Configure(json);
        Assert.Throws<SnapshotConfigurationException>(() => Snapshots.Begin(fixture.Options() with
        {
            MaxNestingDepth = 64,
            Comparison = new()
            {
                IgnoreStringCase = false
            },
            Representation = new()
            {
                Enums = SnapshotEnumRepresentation.Number
            }
        }));
    }

    [Fact]
    public void EmptyConfigPreservesDefaultsAndTheBuildSelectedArtifactRoot()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "snapshots.config.json");
        fixture.Configure("{}");
        var config = ProjectConfigurationReader.Read(path, required: true);
        Assert.Equal(SnapshotUpdate.Missing, config.Update);
        Assert.Equal(64, config.MaxNestingDepth);
        Assert.Null(config.ArtifactDirectory);
        Assert.True(config.Comparison.IgnoreLineEndings);
    }

    [Fact]
    public async Task ConcurrentScopeConfigurationIsIsolated()
    {
        await Task.WhenAll(Enumerable.Range(0, 2).Select(async index =>
        {
            using var fixture = new Fixture();
            using var scope = Snapshots.Begin(fixture.Options(SnapshotUpdate.All));
            scope.StringContent = index == 0 ? SnapshotStringContent.Value : SnapshotStringContent.Json;
            scope.Representation = new() { ByteArrays = index == 0 ? SnapshotByteArrayRepresentation.Numbers : SnapshotByteArrayRepresentation.Base64 };
            await Task.Yield();
            "123".AssertSnapshot("value");
            scope.Complete();
            Assert.True(File.Exists(fixture.FilePath(index == 0 ? "value.txt" : "value.json")));
        }));
    }
}
