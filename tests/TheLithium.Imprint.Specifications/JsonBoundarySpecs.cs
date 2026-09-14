using System.Text.Json;
using TheLithium.Imprint;

namespace TheLithium.Imprint.Specifications;

public static partial class Specs
{
    private static (string Name, Func<Task> Run)[] JsonBoundaries =>
    [
        (nameof(EmittedJsonNodesAreBounded), Check.Sync(EmittedJsonNodesAreBounded)),
        (nameof(CustomWriterCancellationPoisonsCapture), Check.Sync(CustomWriterCancellationPoisonsCapture)),
        (nameof(DuplicatesPoisonEveryJsonCaptureRoute), Check.Sync(DuplicatesPoisonEveryJsonCaptureRoute)),
        (nameof(CustomComparerCancellationPreventsApproval), Check.Sync(CustomComparerCancellationPreventsApproval)),
        (nameof(AttributeSettingsProvideConfigurationLifetime), Check.Sync(AttributeSettingsProvideConfigurationLifetime)),
        (nameof(StructDescendantsRetainReferenceCycleProtection), Check.Sync(StructDescendantsRetainReferenceCycleProtection))
    ];

    public readonly record struct StructLink(StructLinkedNode? Target);

    public sealed class StructLinkedNode
    {
        public StructLink Link
        {
            get; set;
        }
    }

    private static void StructDescendantsRetainReferenceCycleProtection()
    {
        using var fixture = new Fixture();
        var node = new StructLinkedNode();
        node.Link = new(node);
        var link = new StructLink(node);
        var error = Check.Throws<SnapshotCaptureException>(() =>
            fixture.Run(() => link.AssertSnapshot("cycle"), SnapshotUpdate.All));
        Check.True(error.Message.Contains("Reference cycle", StringComparison.Ordinal));
        Check.True(error.Message.Contains("$[\"Target\"][\"Link\"][\"Target\"]", StringComparison.Ordinal));
        Check.True(!File.Exists(fixture.FilePath("cycle.json")));

        node.Link = default;
        fixture.Run(() => new[] { link, link }.AssertSnapshot("shared"), SnapshotUpdate.All);
        using var document = JsonDocument.Parse(File.ReadAllText(fixture.FilePath("shared.json")));
        Check.Equal(2, document.RootElement.GetArrayLength());
        Check.Equal(JsonValueKind.Null, document.RootElement[0].GetProperty("Target").GetProperty("Link").GetProperty("Target").ValueKind);
    }

    private static void EmittedJsonNodesAreBounded()
    {
        using var document = JsonDocument.Parse("[1,1]");
        Action[] captures =
        [
            () => "[1,1]".AssertSnapshot("value", new() { StringContent = SnapshotStringContent.Json, Format = SnapshotFormat.Json }),
            () => document.AssertSnapshot("value"),
            () => document.RootElement.AssertSnapshot("value"),
            () => new[] { 1, 1 }.AssertSnapshot("value"),
            () => 0.AssertSnapshot(static (writer, _, _) => writer.WriteRawValue("[1,1]"), "value")
        ];
        foreach (var capture in captures)
        {
            using var fixture = new Fixture();
            Check.Throws<SnapshotCaptureException>(() => Snapshots.Run(capture,
                fixture.Options(SnapshotUpdate.All) with
                {
                    MaxValuesPerSnapshot = 2
                }));
            Check.True(!File.Exists(fixture.FilePath("value.json")));
            Snapshots.Run(capture, fixture.Options(SnapshotUpdate.All) with { MaxValuesPerSnapshot = 3 });
            Snapshots.Run(capture, fixture.Options() with { MaxValuesPerSnapshot = 4 });
        }
    }

    private static void CustomWriterCancellationPoisonsCapture()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        using var scope = Snapshots.Begin(fixture.Options(SnapshotUpdate.All) with { CancellationToken = cancellation.Token });
        Check.Throws<OperationCanceledException>(() => 1.AssertSnapshot((writer, value, _) =>
        {
            writer.WriteNumberValue(value);
            cancellation.Cancel();
        }, "value"));
        Check.Throws<OperationCanceledException>(() => scope.Complete());
        Check.True(!File.Exists(fixture.FilePath("value.json")));
    }

    private static void DuplicatesPoisonEveryJsonCaptureRoute()
    {
        const string duplicate = "{\"nested\":{\"a\":1,\"\\u0061\":2}}";
        using var document = JsonDocument.Parse(duplicate);
        Action[] captures =
        [
            () => duplicate.AssertSnapshot("value", new() { StringContent = SnapshotStringContent.Json, Format = SnapshotFormat.Json }),
            () => document.AssertSnapshot("value"),
            () => document.RootElement.AssertSnapshot("value"),
            () => 0.AssertSnapshot(static (writer, _, _) => writer.WriteRawValue(duplicate), "value")
        ];
        foreach (var capture in captures)
        {
            using var fixture = new Fixture();
            using var scope = Snapshots.Begin(fixture.Options(SnapshotUpdate.All));
            Check.Throws<SnapshotCaptureException>(capture);
            Check.Throws<SnapshotCaptureException>(() => scope.Complete());
            Check.True(!File.Exists(fixture.FilePath("value.json")));
        }
    }

    private static void CustomComparerCancellationPreventsApproval()
    {
        using var fixture = new Fixture();
        fixture.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
        var original = File.ReadAllText(fixture.FilePath("value.json"));
        using var cancellation = new CancellationTokenSource();
        Check.Throws<OperationCanceledException>(() => Snapshots.Run(() =>
            2.AssertSnapshot("value", new()
            {
                Comparer = new CancellingComparer(cancellation)
            }),
            fixture.Options(SnapshotUpdate.All) with
            {
                CancellationToken = cancellation.Token
            }));
        Check.Equal(original, File.ReadAllText(fixture.FilePath("value.json")));
    }

    private static void AttributeSettingsProvideConfigurationLifetime()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"allowEmptyTests":true,"stringContent":"json"}""");
        using var project = new EnvironmentValue("IMPRINT_PROJECT_ROOT", fixture.Root);
        GeneratedFixtures.ConfigureOrdinaryLifetime();
    }
}

internal sealed class CancellingComparer(CancellationTokenSource cancellation) : ISnapshotComparer
{
    public SnapshotComparisonResult Compare(string expected, string received,
        SnapshotFormat format, ResolvedSnapshotComparison options)
    {
        cancellation.Cancel();
        return SnapshotComparisonResult.Match;
    }
}
