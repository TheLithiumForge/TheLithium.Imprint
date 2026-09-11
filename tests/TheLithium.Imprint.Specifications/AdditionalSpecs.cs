using System.Diagnostics;
using System.Text.Json;
using TheLithium.Imprint.Generation;

namespace TheLithium.Imprint.Specifications;

public static partial class Specs
{
    private static (string Name, Func<Task> Run)[] Additional =>
    [
        (nameof(ReadableCaseKeysOmitTheHash), Check.Sync(ReadableCaseKeysOmitTheHash)),
        (nameof(AmbiguousCaseKeysKeepTheHash), Check.Sync(AmbiguousCaseKeysKeepTheHash)),
        (nameof(TextDiffIncludesContext), Check.Sync(TextDiffIncludesContext)),
        (nameof(JsonDiffIncludesPathAndValues), Check.Sync(JsonDiffIncludesPathAndValues)),
        (nameof(DiffSeparatesDistantChanges), Check.Sync(DiffSeparatesDistantChanges)),
        (nameof(DiffBoundsLargeChanges), Check.Sync(DiffBoundsLargeChanges)),
        (nameof(DiffBoundsLongLines), Check.Sync(DiffBoundsLongLines)),
        (nameof(DiffExposesLineEndings), Check.Sync(DiffExposesLineEndings)),
        (nameof(ExplicitStringWriterUsesJson), Check.Sync(ExplicitStringWriterUsesJson)),
        (nameof(FormatMigrationKeepsExpectedArtifact), Check.Sync(FormatMigrationKeepsExpectedArtifact)),
        (nameof(NestedScopesRestoreParent), Check.Sync(NestedScopesRestoreParent)),
        (nameof(ParallelNamedCaptures), ParallelNamedCaptures),
        (nameof(AsyncExceptionPreventsApproval), AsyncExceptionPreventsApproval),
        (nameof(AsyncCancellationPreventsApproval), AsyncCancellationPreventsApproval),
        (nameof(UnicodeNamesArePortable), Check.Sync(UnicodeNamesArePortable)),
        (nameof(VariantIdentitiesAreSeparate), Check.Sync(VariantIdentitiesAreSeparate)),
        (nameof(GetterFailurePreservesBaseline), Check.Sync(GetterFailurePreservesBaseline)),
        (nameof(DeclaredContractLimitsSerialization), Check.Sync(DeclaredContractLimitsSerialization)),
        (nameof(SharedReferencesAreNotCycles), Check.Sync(SharedReferencesAreNotCycles)),
        (nameof(InvalidEntryOptionsPoisonApproval), Check.Sync(InvalidEntryOptionsPoisonApproval)),
        (nameof(CustomComparerFailureCannotBeApproved), Check.Sync(CustomComparerFailureCannotBeApproved)),
        (nameof(UnorderedArraysPreserveMultiplicity), Check.Sync(UnorderedArraysPreserveMultiplicity)),
        (nameof(VerifyDoesNotRewriteBaselines), Check.Sync(VerifyDoesNotRewriteBaselines)),
        (nameof(PublicContractsRejectNullAtIngress), Check.Sync(PublicContractsRejectNullAtIngress)),
        (nameof(ConcurrentWritersInSeparateProcesses), ConcurrentWritersInSeparateProcesses)
    ];

    private static void PublicContractsRejectNullAtIngress()
    {
        Check.Throws<ArgumentNullException>(() => new SnapshotTestIdentity(null!, "source.cs", "Suite", "Test"));
        Check.Throws<ArgumentNullException>(() => new SnapshotEntryResult(null!, "value.json", SnapshotStatus.Matched));
        Check.Throws<ArgumentNullException>(() => new SnapshotReport("Suite.Test", true, null!));
        Check.Throws<ArgumentNullException>(() => new SnapshotException(null!));
    }

    private static void ReadableCaseKeysOmitTheHash()
    {
        // A label that already names every argument is the whole identity. Committed snapshot
        // paths stay shorter without a suffix that distinguishes nothing.
        Check.Equal("count=1, label=first", SnapshotCases.Create(new { count = 1, label = "first" }));
        Check.Equal("enabled=true, ratio=0.5", SnapshotCases.Create(new { enabled = true, ratio = 0.5 }));
    }

    private static void AmbiguousCaseKeysKeepTheHash()
    {
        // A string that reads as a literal, an embedded separator, or a truncated label can all
        // let two different rows render the same text, so those keep their hash.
        var numeric = SnapshotCases.Create(new { value = "1" });
        var separator = SnapshotCases.Create(new { value = "a, b=c" });
        var nested = SnapshotCases.Create(new { value = new { inner = 1 } });
        var truncated = SnapshotCases.Create(new { value = new string('x', 80) });
        foreach (var key in new[] { numeric, separator, nested, truncated })
        {
            Check.True(key.Contains('~'), "An ambiguous case label must keep its hash: " + key);
        }

        // The hash must actually separate two rows that render identically.
        Check.True(SnapshotCases.Create(new
        {
            value = "1"
        }) != SnapshotCases.Create(new
        {
            value = 1
        }),
            "A string and a number rendering the same text must not share a case folder.");
    }

    private static void TextDiffIncludesContext()
    {
        using var f = new Fixture();
        f.Run(() => "header\nold line\nfooter".AssertSnapshot("text"), SnapshotUpdate.All);
        var error = Check.Throws<SnapshotMismatchException>(() => f.Run(() => "header\nnew line\nfooter".AssertSnapshot("text")));
        Check.True(error.Message.Contains("--- expected\n", StringComparison.Ordinal));
        Check.True(error.Message.Contains("- old line", StringComparison.Ordinal));
        Check.True(error.Message.Contains("+ new line", StringComparison.Ordinal));
        Check.True(error.Message.Contains("  footer", StringComparison.Ordinal));
        Check.True(error.Message.Contains("UpdateSnapshot()", StringComparison.Ordinal));
    }

    private static void JsonDiffIncludesPathAndValues()
    {
        using var f = new Fixture();
        f.Run(() => new { Customer = new { Name = "Alice" } }.AssertSnapshot("customer"), SnapshotUpdate.All);
        var error = Check.Throws<SnapshotMismatchException>(() => f.Run(() => new { Customer = new { Name = "Bob" } }.AssertSnapshot("customer")));
        Check.True(error.Message.Contains("$[\"Customer\"][\"Name\"]", StringComparison.Ordinal));
        Check.True(error.Message.Contains("Alice", StringComparison.Ordinal) && error.Message.Contains("Bob", StringComparison.Ordinal));
        Check.True(File.Exists(Path.Combine(error.Report.ArtifactDirectory!, "customer.expected.json")));
        Check.True(File.Exists(Path.Combine(error.Report.ArtifactDirectory!, "customer.received.json")));
    }

    private static void DiffSeparatesDistantChanges()
    {
        var left = Enumerable.Range(0, 40).Select(i => "line " + i).ToArray();
        var right = left.ToArray();
        right[2] = "first change";
        right[35] = "second change";
        var diff = DefaultSnapshotComparer.Instance.Compare(string.Join('\n', left), string.Join('\n', right), SnapshotFormat.Snap, new()).Difference!;
        Check.True(diff.Contains("+ first change", StringComparison.Ordinal));
        Check.True(diff.Contains("+ second change", StringComparison.Ordinal));
        Check.True(!diff.Contains("line 20", StringComparison.Ordinal));
        Check.Equal(2, diff.Split("@@ -", StringSplitOptions.None).Length - 1);
    }

    private static void DiffBoundsLargeChanges()
    {
        var left = string.Join('\n', Enumerable.Range(0, 10_000).Select(i => "old " + i));
        var right = string.Join('\n', Enumerable.Range(0, 10_000).Select(i => "new " + i));
        var diff = DefaultSnapshotComparer.Instance.Compare(left, right, SnapshotFormat.Snap, new()).Difference!;
        Check.True(diff.Length < 20_000);
        Check.True(diff.Contains("abbreviated", StringComparison.Ordinal));
        Check.True(diff.Contains("- old 0", StringComparison.Ordinal) && diff.Contains("+ new 0", StringComparison.Ordinal));
    }

    private static void DiffBoundsLongLines()
    {
        var diff = DefaultSnapshotComparer.Instance.Compare(new string('a', 100_000), new string('b', 100_000), SnapshotFormat.Text, new()).Difference!;
        Check.True(diff.Length < 2_000);
    }

    private static void DiffExposesLineEndings()
    {
        var difference = DefaultSnapshotComparer.Instance.Compare("a\r\nb", "a\nb", SnapshotFormat.Text,
            new()
            {
                IgnoreLineEndings = false
            });
        Check.True(!difference.Equal && difference.Difference!.Contains("- a\\r", StringComparison.Ordinal));
        Check.True(DefaultSnapshotComparer.Instance.Compare("a\r\nb", "a\nb", SnapshotFormat.Text, new()).Equal);
    }

    private static void ExplicitStringWriterUsesJson()
    {
        using var f = new Fixture();
        f.Run(() => "source".AssertSnapshot(static (writer, value, _) =>
        {
            writer.WriteStartObject();
            writer.WriteString("Value", value);
            writer.WriteEndObject();
        }, "value"), SnapshotUpdate.All);
        using var json = JsonDocument.Parse(File.ReadAllText(f.FilePath("value.json")));
        Check.Equal("source", json.RootElement.GetProperty("Value").GetString());
    }

    private static void FormatMigrationKeepsExpectedArtifact()
    {
        using var f = new Fixture();
        f.Run(() => "1".AssertSnapshot("value"), SnapshotUpdate.All);
        var error = Check.Throws<SnapshotMismatchException>(() => f.Run(() => 1.AssertSnapshot("value")));
        Check.Equal("1", File.ReadAllText(Path.Combine(error.Report.ArtifactDirectory!, "value.expected.txt")));
        Check.True(File.Exists(Path.Combine(error.Report.ArtifactDirectory!, "value.received.json")));
    }

    private static void NestedScopesRestoreParent()
    {
        using var f = new Fixture();
        f.Run(() =>
        {
            var parent = Snapshots.Current;
            1.AssertSnapshot("before");
            f.Run(() => 2.AssertSnapshot("inner"), SnapshotUpdate.All, "Nested");
            Check.True(ReferenceEquals(parent, Snapshots.Current));
            3.AssertSnapshot("after");
        }, SnapshotUpdate.All);
        Check.True(File.Exists(f.FilePath("after.json")));
        Check.True(File.Exists(f.FilePath("inner.json", "Nested")));
        Check.Throws<SnapshotConfigurationException>(() => _ = Snapshots.Current);
    }

    private static async Task ParallelNamedCaptures()
    {
        using var f = new Fixture();
        await Snapshots.RunAsync(async () => await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(index => Task.Run(() => index.AssertSnapshot("item-" + index)))), f.Options(SnapshotUpdate.All));
        Check.Equal(32, Directory.GetFiles(f.Baselines(), "*.json").Length);
    }

    private static async Task AsyncExceptionPreventsApproval()
    {
        using var f = new Fixture();
        var expected = new InvalidOperationException("application assertion");
        var actual = await Check.ThrowsAsync<InvalidOperationException>(() => Snapshots.RunAsync(async () =>
        {
            1.AssertSnapshot("value");
            await Task.Yield();
            throw expected;
        }, f.Options(SnapshotUpdate.All)));
        Check.True(ReferenceEquals(expected, actual));
        Check.True(!File.Exists(f.FilePath("value.json")));
    }

    private static async Task AsyncCancellationPreventsApproval()
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await Check.ThrowsAsync<OperationCanceledException>(() => Snapshots.RunAsync(async () =>
        {
            1.AssertSnapshot("value");
            await Task.Yield();
            cancellation.Cancel();
        }, f.Options(SnapshotUpdate.All) with
        {
            CancellationToken = cancellation.Token
        }));
        Check.True(!File.Exists(f.FilePath("value.json")));
    }

    private static void UnicodeNamesArePortable()
    {
        using var f = new Fixture();
        f.Run(() =>
        {
            1.AssertSnapshot("café");
            2.AssertSnapshot("cafe\u0301");
            3.AssertSnapshot("NUL");
            4.AssertSnapshot("../../outside");
            5.AssertSnapshot(new string('x', 300) + "😀");
        }, SnapshotUpdate.All);
        var files = Directory.GetFiles(f.Baselines());
        Check.Equal(5, files.Length);
        Check.True(files.All(file => Path.GetFileName(file).Length < 125));
        Check.True(files.All(file => !Path.GetFileName(file).Contains('/')));
    }

    private static void VariantIdentitiesAreSeparate()
    {
        using var f = new Fixture();
        Snapshots.Run(() => 1.AssertSnapshot("value"), f.Options(SnapshotUpdate.All) with
        {
            Variant = "windows"
        });
        Snapshots.Run(() => 2.AssertSnapshot("value"), f.Options(SnapshotUpdate.All) with
        {
            Variant = "linux"
        });
        Check.True(File.Exists(f.FilePath("value.json", "Example [windows]")));
        Check.True(File.Exists(f.FilePath("value.json", "Example [linux]")));
    }

    private static void GetterFailurePreservesBaseline()
    {
        using var f = new Fixture();
        f.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
        var error = Check.Throws<SnapshotCaptureException>(() => f.Run(() => new ThrowingModel().AssertSnapshot("value"), SnapshotUpdate.All));
        Check.True(error.Message.Contains("$[\"Value\"]", StringComparison.Ordinal));
        Check.Equal("1\n", File.ReadAllText(f.FilePath("value.json")));
    }

    private static void DeclaredContractLimitsSerialization()
    {
        using var f = new Fixture();
        BaseModel value = new DerivedModel { Id = 1, Extra = "not in the declared contract" };
        f.Run(() => value.AssertSnapshot("value"), SnapshotUpdate.All);
        using var json = JsonDocument.Parse(File.ReadAllText(f.FilePath("value.json")));
        Check.Equal(1, json.RootElement.GetProperty("Id").GetInt32());
        Check.True(!json.RootElement.TryGetProperty("Extra", out _));
    }

    private static void SharedReferencesAreNotCycles()
    {
        using var f = new Fixture();
        var value = new MutableState { Count = 1 };
        f.Run(() => new { First = value, Second = value }.AssertSnapshot("value"), SnapshotUpdate.All);
        f.Run(() => new { First = value, Second = value }.AssertSnapshot("value"));
    }

    private static void InvalidEntryOptionsPoisonApproval()
    {
        using var f = new Fixture();
        Check.Throws<SnapshotCaptureException>(() => f.Run(() =>
        {
            try
            {
                1.AssertSnapshot("bad", new()
                {
                    Comparison = new()
                    {
                        NumericTolerance = -1
                    }
                });
            }
            catch (SnapshotConfigurationException) { }
            2.AssertSnapshot("good");
        }, SnapshotUpdate.All));
        Check.True(!File.Exists(f.FilePath("good.json")));
    }

    private static void CustomComparerFailureCannotBeApproved()
    {
        using var f = new Fixture();
        f.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
        Check.Throws<InvalidOperationException>(() => f.Run(() => 2.AssertSnapshot("value", new() { Comparer = new BrokenComparer() }), SnapshotUpdate.All));
        Check.Equal("1\n", File.ReadAllText(f.FilePath("value.json")));
    }

    private static void UnorderedArraysPreserveMultiplicity()
    {
        var comparer = DefaultSnapshotComparer.Instance;
        Check.True(!comparer.Compare("[1,1,2]", "[1,2,2]", SnapshotFormat.Json, new()
        {
            IgnoreArrayOrder = true
        }).Equal);
        Check.True(comparer.Compare("[1,1,2]", "[2,1,1]", SnapshotFormat.Json, new()
        {
            IgnoreArrayOrder = true
        }).Equal);
    }

    private static void VerifyDoesNotRewriteBaselines()
    {
        using var f = new Fixture();
        f.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
        var path = f.FilePath("value.json");
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);
        f.Run(() => 1.AssertSnapshot("value"));
        Check.Equal(stamp, File.GetLastWriteTimeUtc(path));
    }

    private static async Task ConcurrentWritersInSeparateProcesses()
    {
        // The executable owns this process scenario; the xUnit adapter also runs these same specs.
        using var f = new Fixture();
        var executable = Environment.ProcessPath!;
        var isHarness = Path.GetFileNameWithoutExtension(executable).Equals("TheLithium.Imprint.Specifications", StringComparison.Ordinal);
        var start = new ProcessStartInfo(isHarness ? executable : "dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (!isHarness)
        {
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "TheLithium.Imprint.Specifications.dll"));
        }

        start.ArgumentList.Add("--concurrency-worker");
        start.ArgumentList.Add(f.Root);
        using var first = Process.Start(start)!;
        using var second = Process.Start(start)!;
        var firstOutput = first.StandardOutput.ReadToEndAsync();
        var firstError = first.StandardError.ReadToEndAsync();
        var secondOutput = second.StandardOutput.ReadToEndAsync();
        var secondError = second.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await Task.WhenAll(first.WaitForExitAsync(timeout.Token), second.WaitForExitAsync(timeout.Token));
            var codes = new[] { first.ExitCode, second.ExitCode }.Order().ToArray();
            Check.True(codes.SequenceEqual(new[] { 0, 3 }), await firstError + await secondError + await firstOutput + await secondOutput);
            Check.Equal("1\n", File.ReadAllText(f.FilePath("value.json")));
        }
        finally
        {
            if (!first.HasExited)
            {
                first.Kill(entireProcessTree: true);
            }

            if (!second.HasExited)
            {
                second.Kill(entireProcessTree: true);
            }
        }
    }
}

internal sealed class ThrowingModel
{
    public int Value => throw new InvalidOperationException("getter failed");
}
internal class BaseModel
{
    public int Id
    {
        get; set;
    }
}
internal sealed class DerivedModel : BaseModel
{
    public string Extra { get; set; } = "";
}
internal sealed class BrokenComparer : ISnapshotComparer
{
    public SnapshotComparisonResult Compare(string expected, string received, SnapshotFormat format, SnapshotComparison options)
        => throw new InvalidOperationException("comparison failed");
}
