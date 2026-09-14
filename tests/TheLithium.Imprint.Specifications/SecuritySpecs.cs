using TheLithium.Imprint;

namespace TheLithium.Imprint.Specifications;

public static partial class Specs
{
    private static (string Name, Func<Task> Run)[] Security =>
    [
        (nameof(ArtifactRootLinkRejected), Check.Sync(ArtifactRootLinkRejected)),
        (nameof(ArtifactParentLinkRejected), Check.Sync(ArtifactParentLinkRejected)),
        (nameof(ExternalArtifactParentLinkRejected), Check.Sync(ExternalArtifactParentLinkRejected)),
        (nameof(StorageOverlapRejected), Check.Sync(StorageOverlapRejected)),
        (nameof(ArtifactTraversalIntoBaselinesRejected), Check.Sync(ArtifactTraversalIntoBaselinesRejected)),
        (nameof(LateArtifactLinkPreservesMismatch), Check.Sync(LateArtifactLinkPreservesMismatch)),
        (nameof(SafeExternalArtifactsWorkInVerify), Check.Sync(SafeExternalArtifactsWorkInVerify)),
        (nameof(BaselineParentLinkRejected), Check.Sync(BaselineParentLinkRejected)),
        (nameof(ArtifactAncestorOverlapRejected), Check.Sync(ArtifactAncestorOverlapRejected)),
        (nameof(MacSystemAliasesCannotConcealOverlap), Check.Sync(MacSystemAliasesCannotConcealOverlap))
    ];

    private static void ArtifactRootLinkRejected()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.Baselines());
        File.WriteAllText(fixture.FilePath("protected.txt"), "untouched");
        var artifactRoot = Path.Combine(fixture.Root, "diagnostics");
        using var link = new DirectoryLink(artifactRoot, fixture.Baselines());
        Check.Throws<SnapshotConfigurationException>(() =>
        {
            using var scope = Snapshots.Begin(fixture.Options() with { ArtifactDirectory = artifactRoot });
        });
        Check.Equal("untouched", File.ReadAllText(fixture.FilePath("protected.txt")));
        Check.Equal(1, Directory.GetFileSystemEntries(fixture.Baselines()).Length);
    }

    private static void ArtifactParentLinkRejected()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.Baselines());
        var parent = Path.Combine(fixture.Root, "redirect");
        using var link = new DirectoryLink(parent, fixture.Baselines());
        Check.Throws<SnapshotConfigurationException>(() =>
        {
            using var scope = Snapshots.Begin(fixture.Options() with { ArtifactDirectory = Path.Combine(parent, "failures") });
        });
        Check.Equal(0, Directory.GetFileSystemEntries(fixture.Baselines()).Length);
    }

    private static void ExternalArtifactParentLinkRejected()
    {
        using var project = new Fixture();
        using var external = new Fixture();
        Directory.CreateDirectory(project.Baselines());
        var parent = Path.Combine(external.Root, "redirect");
        using var link = new DirectoryLink(parent, project.Baselines());
        Check.Throws<SnapshotConfigurationException>(() =>
        {
            using var scope = Snapshots.Begin(project.Options() with { ArtifactDirectory = Path.Combine(parent, "failures") });
        });
        Check.Equal(0, Directory.GetFileSystemEntries(project.Baselines()).Length);
    }

    private static void StorageOverlapRejected()
    {
        using var fixture = new Fixture();
        Check.Throws<SnapshotConfigurationException>(() =>
        {
            using var scope = Snapshots.Begin(fixture.Options() with { ArtifactDirectory = "artifacts/imprint/storage" });
        });
        Check.Throws<SnapshotConfigurationException>(() =>
        {
            using var scope = Snapshots.Begin(fixture.Options() with { RootDirectory = "artifacts/imprint/storage", ArtifactDirectory = "failures" });
        });
        Check.True(!Directory.Exists(Path.Combine(fixture.Root, "artifacts")));
    }

    private static void ArtifactTraversalIntoBaselinesRejected()
    {
        using var fixture = new Fixture();
        Check.Throws<SnapshotConfigurationException>(() =>
        {
            using var scope = Snapshots.Begin(fixture.Options() with { ArtifactDirectory = "diagnostics/../__snapshots__/failures" });
        });
        Check.True(!Directory.Exists(Path.Combine(fixture.Root, "__snapshots__")));
    }

    private static void LateArtifactLinkPreservesMismatch()
    {
        using var fixture = new Fixture();
        fixture.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
        var original = File.ReadAllText(fixture.FilePath("value.json"));
        var artifactRoot = Path.Combine(fixture.Root, "diagnostics");
        using var scope = Snapshots.Begin(fixture.Options() with { ArtifactDirectory = artifactRoot });
        2.AssertSnapshot("value");
        using var link = new DirectoryLink(artifactRoot, fixture.Baselines());
        var error = Check.Throws<SnapshotMismatchException>(() => scope.Complete());
        Check.True(error.Message.Contains("Failure artifacts could not be written", StringComparison.Ordinal));
        Check.True(error.Report.ArtifactDirectory is null);
        Check.Equal(SnapshotStatus.Changed, error.Report.Entries.Single().Status);
        Check.Equal(original, File.ReadAllText(fixture.FilePath("value.json")));
        Check.Equal(1, Directory.GetFileSystemEntries(fixture.Baselines()).Length);
    }

    private static void SafeExternalArtifactsWorkInVerify()
    {
        using var fixture = new Fixture();
        using var external = new Fixture();
        fixture.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
        var original = File.ReadAllText(fixture.FilePath("value.json"));
        using var verify = new EnvironmentValue("IMPRINT_UPDATE", "verify");
        var error = Check.Throws<SnapshotMismatchException>(() =>
            Snapshots.Run(() => 2.AssertSnapshot("value"), fixture.Options() with
            {
                ArtifactDirectory = external.Root
            }));
        var directory = error.Report.ArtifactDirectory ?? throw new Exception("Expected diagnostic artifacts under Verify.");
        Check.True(directory.StartsWith(external.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Check.Equal("2\n", File.ReadAllText(Path.Combine(directory, "value.received.json")));
        Check.Equal(original, File.ReadAllText(Path.Combine(directory, "value.expected.json")));
        Check.Equal(original, File.ReadAllText(fixture.FilePath("value.json")));
    }

    private static void BaselineParentLinkRejected()
    {
        using var fixture = new Fixture();
        using var target = new Fixture();
        var parent = Path.Combine(fixture.Root, "redirect");
        using var link = new DirectoryLink(parent, target.Root);
        Check.Throws<SnapshotConfigurationException>(() =>
        {
            using var scope = Snapshots.Begin(fixture.Options() with { RootDirectory = Path.Combine(parent, "baselines") });
        });
        Check.Equal(0, Directory.GetFileSystemEntries(target.Root).Length);
    }

    private static void ArtifactAncestorOverlapRejected()
    {
        using var fixture = new Fixture();
        Check.Throws<SnapshotConfigurationException>(() =>
        {
            using var scope = Snapshots.Begin(fixture.Options() with { ArtifactDirectory = fixture.Root });
        });
        Check.True(!Directory.Exists(Path.Combine(fixture.Root, "artifacts")));
    }

    private static void MacSystemAliasesCannotConcealOverlap()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }
        using var fixture = new Fixture();
        var unique = $"imprint-alias-{Guid.NewGuid():N}";
        Check.Throws<SnapshotConfigurationException>(() =>
        {
            using var scope = Snapshots.Begin(fixture.Options() with
            {
                RootDirectory = $"/private/tmp/{unique}",
                ArtifactDirectory = $"/tmp/{unique}/failures"
            });
        });
        Check.True(!Directory.Exists($"/private/tmp/{unique}"));
    }
}
