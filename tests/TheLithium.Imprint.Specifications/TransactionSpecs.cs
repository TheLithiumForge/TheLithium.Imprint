using TheLithium.Imprint;

namespace TheLithium.Imprint.Specifications;

public static partial class Specs
{
    private static (string Name, Func<Task> Run)[] Transactions =>
    [
        (nameof(PartialApplyFailureRestoresTheBeforeState), Check.Sync(PartialApplyFailureRestoresTheBeforeState)),
        (nameof(RecoveryDoesNotNeedAnAfterCopy), Check.Sync(RecoveryDoesNotNeedAnAfterCopy))
    ];

    private static void PartialApplyFailureRestoresTheBeforeState()
    {
        using var fixture = new Fixture();
        fixture.Run(() => 1.AssertSnapshot("first"), SnapshotUpdate.All);
        var original = File.ReadAllText(fixture.FilePath("first.json"));
        Directory.CreateDirectory(fixture.FilePath("second.json"));
        var error = Check.Throws<Exception>(() => fixture.Run(() =>
        {
            2.AssertSnapshot("first");
            3.AssertSnapshot("second");
        }, SnapshotUpdate.All));
        Check.True(error is IOException or UnauthorizedAccessException);
        Check.Equal(original, File.ReadAllText(fixture.FilePath("first.json")));
        Check.True(!File.Exists(fixture.FilePath("second.json")));
        Check.Equal(0, Directory.GetDirectories(Path.Combine(fixture.Root, "artifacts", "imprint", "storage", "transactions")).Length);
        Directory.Delete(fixture.FilePath("second.json"));
        fixture.Run(() =>
        {
            2.AssertSnapshot("first");
            3.AssertSnapshot("second");
        }, SnapshotUpdate.All);
        Check.Equal("2\n", File.ReadAllText(fixture.FilePath("first.json")));
        Check.Equal("3\n", File.ReadAllText(fixture.FilePath("second.json")));
    }

    private static void RecoveryDoesNotNeedAnAfterCopy()
    {
        using var fixture = new Fixture();
        var journal = SimulateInterruptedCommit(fixture);
        Directory.Delete(Path.Combine(journal, "after"));
        fixture.Run(() => 1.AssertSnapshot("value"));
        Check.Equal("1\n", File.ReadAllText(fixture.FilePath("value.json")));
        Check.True(!Directory.Exists(journal));
    }
}
