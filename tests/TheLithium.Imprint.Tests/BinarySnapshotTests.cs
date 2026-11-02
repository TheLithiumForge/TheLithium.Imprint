using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using TheLithium.Imprint.Comparison;
using TheLithium.Imprint.Comparison.Models;
using TheLithium.Imprint.Specifications;
using TheLithium.Imprint.Storage;
using TheLithium.Imprint.Storage.Models;
using Xunit;

namespace TheLithium.Imprint.Tests;

public sealed class BinarySnapshotTests : IDisposable
{
    // CI forces a run-wide verify, which would stop these tests from creating baselines.
    private readonly EnvironmentValue _ci = new("CI", "false");
    private readonly EnvironmentValue _update = new("IMPRINT_UPDATE", null);
    private const string TextJournal = "TheLithium.Imprint/1\n";
    private const string BinaryJournal = "TheLithium.Imprint/2\n";
    private static readonly byte[] First = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
    private static readonly byte[] Second = [0x89, 0x50, 0x4E, 0x47, 1, 2, 4];

    [Fact]
    public void MissingCreatesTheExactBytes()
    {
        using var fixture = new Fixture();
        var comparer = new CountingComparer(Outcome.Different);
        fixture.Run(() => Capture("screen", First, comparer), SnapshotUpdate.Missing);
        Assert.Equal(First, File.ReadAllBytes(fixture.FilePath("screen.png")));
        Assert.Equal(0, comparer.Calls);
    }

    [Fact]
    public void IdenticalBytesMatchWithoutTheComparer()
    {
        using var fixture = new Fixture();
        Seed(fixture, First);
        var comparer = new CountingComparer(Outcome.Different);
        var report = fixture.Run(() => Capture("screen", First, comparer));
        Assert.Equal(SnapshotStatus.Matched, Assert.Single(report.Entries).Status);
        Assert.Equal(0, comparer.Calls);
    }

    [Fact]
    public void IdenticalContentInOtherBytesKeepsTheStoredFileUnderAll()
    {
        using var fixture = new Fixture();
        Seed(fixture, First);
        var comparer = new CountingComparer(Outcome.Identical);
        var report = fixture.Run(() => Capture("screen", Second, comparer), SnapshotUpdate.All);
        Assert.Equal(SnapshotStatus.Matched, Assert.Single(report.Entries).Status);
        Assert.Equal(1, comparer.Calls);
        Assert.Equal(First, File.ReadAllBytes(fixture.FilePath("screen.png")));
    }

    [Fact]
    public void AMatchWithinToleranceIsStoredUnderAllAndKeptOtherwise()
    {
        using var fixture = new Fixture();
        Seed(fixture, First);
        var report = fixture.Run(() => Capture("screen", Second, new CountingComparer(Outcome.WithinTolerance)), SnapshotUpdate.Missing);
        Assert.Equal(SnapshotStatus.Matched, Assert.Single(report.Entries).Status);
        Assert.Equal(First, File.ReadAllBytes(fixture.FilePath("screen.png")));

        report = fixture.Run(() => Capture("screen", Second, new CountingComparer(Outcome.WithinTolerance)), SnapshotUpdate.All);
        Assert.Equal(SnapshotStatus.Updated, Assert.Single(report.Entries).Status);
        Assert.Equal(Second, File.ReadAllBytes(fixture.FilePath("screen.png")));
    }

    [Fact]
    public void AMismatchFailsAndWritesEveryArtifact()
    {
        using var fixture = new Fixture();
        Seed(fixture, First);
        var failure = Assert.Throws<SnapshotMismatchException>(() => fixture.Run(() => Capture("screen", Second, new CountingComparer(Outcome.Different))));
        var entry = Assert.Single(failure.Report.Entries);
        Assert.Equal(SnapshotStatus.Changed, entry.Status);
        Assert.Equal("3 pixels differ.", entry.Difference);
        var artifacts = Assert.IsType<string>(failure.Report.ArtifactDirectory);
        Assert.Equal(Second, File.ReadAllBytes(Path.Combine(artifacts, "screen.received.png")));
        Assert.Equal(First, File.ReadAllBytes(Path.Combine(artifacts, "screen.expected.png")));
        Assert.Equal([7], File.ReadAllBytes(Path.Combine(artifacts, "screen.diff.png")));
        Assert.Equal(First, File.ReadAllBytes(fixture.FilePath("screen.png")));
    }

    [Fact]
    public void AllReplacesAnImageThatNoLongerMatches()
    {
        using var fixture = new Fixture();
        Seed(fixture, First);
        var report = fixture.Run(() => Capture("screen", Second, new CountingComparer(Outcome.Different)), SnapshotUpdate.All);
        Assert.Equal(SnapshotStatus.Updated, Assert.Single(report.Entries).Status);
        Assert.Equal(Second, File.ReadAllBytes(fixture.FilePath("screen.png")));
    }

    [Fact]
    public void TextAndImagesShareOneTestFolderAndOneApproval()
    {
        using var fixture = new Fixture();
        fixture.Run(() =>
        {
            new { Title = "Settings" }.AssertSnapshot("state");
            Capture("screen", First, new CountingComparer(Outcome.Different));
        }, SnapshotUpdate.Missing);
        Assert.True(File.Exists(fixture.FilePath("state.json")));
        Assert.True(File.Exists(fixture.FilePath("screen.png")));

        Assert.Throws<InvalidOperationException>(() => fixture.Run(() =>
        {
            Capture("screen", Second, new CountingComparer(Outcome.Different));
            throw new InvalidOperationException("The test failed after capturing.");
        }, SnapshotUpdate.All));
        Assert.Equal(First, File.ReadAllBytes(fixture.FilePath("screen.png")));
    }

    [Fact]
    public void AllDeletesAnImageTheTestNoLongerCaptures()
    {
        using var fixture = new Fixture();
        Seed(fixture, First);
        fixture.Run(() => new { Title = "Settings" }.AssertSnapshot("state"), SnapshotUpdate.All);
        Assert.False(File.Exists(fixture.FilePath("screen.png")));
    }

    [Fact]
    public void ATextAndAnImageCaptureCanNotShareAName()
    {
        using var fixture = new Fixture();
        fixture.Run(() => new { Title = "Settings" }.AssertSnapshot("screen"), SnapshotUpdate.Missing);
        var failure = Assert.Throws<SnapshotMismatchException>(() => fixture.Run(() => Capture("screen", First, new CountingComparer(Outcome.Identical))));
        Assert.Equal("Snapshot name or file format changed.", Assert.Single(failure.Report.Entries, entry => entry.Status == SnapshotStatus.Changed).Difference);
    }

    [Fact]
    public void BinaryCapturesInferTheirNameAndFollowTheImageLimit()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{ "visual": { "maxBytesPerImage": 4 } }""");
        var screen = First;
        var failure = Assert.Throws<SnapshotCaptureException>(() => fixture.Run(() => Snapshots.Current.CaptureBinary(new()
        {
            Name = null,
            Expression = nameof(screen),
            Extension = ".png",
            Content = screen,
            Update = SnapshotUpdate.Inherit,
            Comparer = new CountingComparer(Outcome.Identical)
        }), SnapshotUpdate.Missing));
        Assert.Contains("visual.maxBytesPerImage", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.FilePath("screen.png")));
    }

    [Fact]
    public void ImagesAreNotLimitedByTheTextSnapshotLimit()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{ "limits": { "maxBytesPerSnapshot": 4 } }""");
        fixture.Run(() => Capture("screen", First, new CountingComparer(Outcome.Identical)), SnapshotUpdate.Missing);
        var report = fixture.Run(() => Capture("screen", First, new CountingComparer(Outcome.Different)));
        Assert.Equal(SnapshotStatus.Matched, Assert.Single(report.Entries).Status);
    }

    [Fact]
    public void OnlyKnownBinaryExtensionsAreAccepted()
    {
        using var fixture = new Fixture();
        Assert.Throws<SnapshotCaptureException>(() => fixture.Run(() => Snapshots.Current.CaptureBinary(new()
        {
            Name = "screen",
            Expression = null,
            Extension = ".bmp",
            Content = First,
            Update = SnapshotUpdate.Inherit,
            Comparer = new CountingComparer(Outcome.Identical)
        }), SnapshotUpdate.Missing));
    }

    [Fact]
    public void TextFingerprintsMatchTheTextAlgorithm()
    {
        using var fixture = new Fixture();
        fixture.Run(() => new { Title = "Ünïcode" }.AssertSnapshot("state"), SnapshotUpdate.Missing);
        using var scope = Snapshots.Begin(fixture.Options(SnapshotUpdate.Verify));
        var text = File.ReadAllText(fixture.FilePath("state.json"), Encoding.UTF8);
        Assert.Equal(TextFingerprint(("state.json", text)), scope.BaselineFingerprint);
        new { Title = "Ünïcode" }.AssertSnapshot("state");
        scope.Complete();
    }

    [Fact]
    public void AJournalWithAnImageUsesTheBinaryMarker()
    {
        var text = new Dictionary<string, SnapshotFile> { ["state.json"] = SnapshotFile.FromText("{}") };
        var image = new Dictionary<string, SnapshotFile> { ["screen.png"] = SnapshotFile.FromBytes(First) };
        Assert.Equal(TextJournal, SnapshotStore.JournalMarkerFor(text, text));
        Assert.Equal(BinaryJournal, SnapshotStore.JournalMarkerFor(text, image));
        Assert.Equal(BinaryJournal, SnapshotStore.JournalMarkerFor(image, text));
    }

    [Fact]
    public void AnInterruptedImageCommitIsRolledBack()
    {
        using var fixture = new Fixture();
        Seed(fixture, First);
        string journal;
        using (var scope = Snapshots.Begin(fixture.Options(SnapshotUpdate.Missing)))
        {
            var settings = scope.ResolvedSettings;
            journal = SnapshotStore.JournalPath(settings.StorageRoot, settings.TestDirectory);
            Capture("screen", First, new CountingComparer(Outcome.Different));
            scope.Complete();
        }

        var before = new Dictionary<string, SnapshotFile> { ["screen.png"] = SnapshotFile.FromBytes(First) };
        Directory.CreateDirectory(Path.Combine(journal, "before"));
        File.WriteAllBytes(Path.Combine(journal, "before", "screen.png"), First);
        File.WriteAllText(Path.Combine(journal, "before.fingerprint"), SnapshotStore.Fingerprint(before));
        File.WriteAllText(Path.Combine(journal, "prepared"), BinaryJournal);
        File.WriteAllBytes(fixture.FilePath("screen.png"), Second);

        fixture.Run(() => Capture("screen", First, new CountingComparer(Outcome.Different)));
        Assert.Equal(First, File.ReadAllBytes(fixture.FilePath("screen.png")));
        Assert.False(Directory.Exists(journal));
    }

    public void Dispose()
    {
        _update.Dispose();
        _ci.Dispose();
    }

    private static void Capture(string name, byte[] content, IBinarySnapshotComparer comparer)
        => Snapshots.Current.CaptureBinary(new()
        {
            Name = name,
            Expression = null,
            Extension = ".png",
            Content = content,
            Update = SnapshotUpdate.Inherit,
            Comparer = comparer
        });

    private static void Seed(Fixture fixture, byte[] content)
        => fixture.Run(() => Capture("screen", content, new CountingComparer(Outcome.Different)), SnapshotUpdate.Missing);

    /// <summary>The fingerprint of a text-only folder before binary snapshots existed. An independent oracle.</summary>
    private static string TextFingerprint(params (string Name, string Text)[] files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (var (name, text) in files.OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            foreach (var bytes in new[] { Encoding.UTF8.GetBytes(name), Encoding.UTF8.GetBytes(text) })
            {
                BinaryPrimitives.WriteInt64LittleEndian(length, bytes.LongLength);
                hash.AppendData(length);
                hash.AppendData(bytes);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private enum Outcome
    {
        Identical, WithinTolerance, Different
    }

    private sealed class CountingComparer(Outcome outcome) : IBinarySnapshotComparer
    {
        internal int Calls
        {
            get; private set;
        }

        public BinaryComparisonResult Compare(byte[] expected, byte[] received, CancellationToken cancellation)
        {
            Calls++;
            return outcome switch
            {
                Outcome.Identical => BinaryComparisonResult.Same,
                Outcome.WithinTolerance => new(true, null, []),
                Outcome.Different => new(false, "3 pixels differ.", [new("diff.png", [7])]),
                _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
            };
        }
    }
}
