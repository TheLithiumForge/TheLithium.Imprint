using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheLithium.Imprint;

namespace TheLithium.Imprint.Specifications;

public static partial class Specs
{
    public static (string Name, Func<Task> Run)[] All => [.. Original, .. Additional, .. Security, .. JsonBoundaries, .. Representations, .. Transactions];

    private static (string Name, Func<Task> Run)[] Original => new (string, Func<Task>)[]
    {
        (nameof(MultipleFiles), Check.Sync(MultipleFiles)),
        (nameof(AutomaticNames), Check.Sync(AutomaticNames)),
        (nameof(AnonymousGraphs), Check.Sync(AnonymousGraphs)),
        (nameof(TuplesAndDictionaries), Check.Sync(TuplesAndDictionaries)),
        (nameof(MultidimensionalArrays), Check.Sync(MultidimensionalArrays)),
        (nameof(NullableAndEnums), Check.Sync(NullableAndEnums)),
        (nameof(BuiltInValues), Check.Sync(BuiltInValues)),
        (nameof(CaptureIsImmediate), Check.Sync(CaptureIsImmediate)),
        (nameof(MissingDefaultsCreate), Check.Sync(MissingDefaultsCreate)),
        (nameof(MissingFails), Check.Sync(MissingFails)),
        (nameof(UpdateReplaces), Check.Sync(UpdateReplaces)),
        (nameof(MissingPolicyIsTransactional), Check.Sync(MissingPolicyIsTransactional)),
        (nameof(AggregatesDifferences), Check.Sync(AggregatesDifferences)),
        (nameof(GeneratedAttributePolicies), Check.Sync(GeneratedAttributePolicies)),
        (nameof(ParameterizedIdentity), Check.Sync(ParameterizedIdentity)),
        (nameof(ProjectConfiguration), Check.Sync(ProjectConfiguration)),
        (nameof(MethodLevelUpdate), Check.Sync(MethodLevelUpdate)),
        (nameof(LatePolicyChangeFails), Check.Sync(LatePolicyChangeFails)),
        (nameof(EntryUpdateCannotDeleteOthers), Check.Sync(EntryUpdateCannotDeleteOthers)),
        (nameof(WholeTestUpdatePrunes), Check.Sync(WholeTestUpdatePrunes)),
        (nameof(EmptyRequiresOptIn), Check.Sync(EmptyRequiresOptIn)),
        (nameof(LaterAssertionPreventsApproval), Check.Sync(LaterAssertionPreventsApproval)),
        (nameof(CaughtCaptureStillPoisonsTest), Check.Sync(CaughtCaptureStillPoisonsTest)),
        (nameof(GenericIncludeAndUnsupportedType), Check.Sync(GenericIncludeAndUnsupportedType)),
        (nameof(ExplicitPrivateWriter), Check.Sync(ExplicitPrivateWriter)),
        (nameof(StructuralJson), Check.Sync(StructuralJson)),
        (nameof(UnorderedToleranceUsesMaximumMatching), Check.Sync(UnorderedToleranceUsesMaximumMatching)),
        (nameof(ExactLargeNumbers), Check.Sync(ExactLargeNumbers)),
        (nameof(ComparisonDoesNotRewriteData), Check.Sync(ComparisonDoesNotRewriteData)),
        (nameof(RawJsonAndNull), Check.Sync(RawJsonAndNull)),
        (nameof(InvalidJsonCannotBeApproved), Check.Sync(InvalidJsonCannotBeApproved)),
        (nameof(DuplicateJsonPropertiesFail), Check.Sync(DuplicateJsonPropertiesFail)),
        (nameof(CancellationPreventsApproval), Check.Sync(CancellationPreventsApproval)),
        (nameof(DisposalDoesNotApprove), Check.Sync(DisposalDoesNotApprove)),
        (nameof(CompleteOnlyOnce), Check.Sync(CompleteOnlyOnce)),
        (nameof(AsyncContextFlows), AsyncContextFlows),
        (nameof(ConcurrentTestsAreIsolated), ConcurrentTestsAreIsolated),
        (nameof(ConcurrentUpdatesConflict), Check.Sync(ConcurrentUpdatesConflict)),
        (nameof(OwnershipCollisionsFail), Check.Sync(OwnershipCollisionsFail)),
        (nameof(PortableAndDuplicateNames), Check.Sync(PortableAndDuplicateNames)),
        (nameof(SizeAndDepthLimits), Check.Sync(SizeAndDepthLimits)),
        (nameof(StrictConfiguration), Check.Sync(StrictConfiguration)),
        (nameof(NoImplicitScope), Check.Sync(NoImplicitScope)),
        (nameof(EntryFormatMigration), Check.Sync(EntryFormatMigration)),
        (nameof(AmbiguousBaselinesFail), Check.Sync(AmbiguousBaselinesFail)),
        (nameof(InterruptedCommitRecovers), Check.Sync(InterruptedCommitRecovers)),
        (nameof(ReadOnlyRefusesRecovery), Check.Sync(ReadOnlyRefusesRecovery)),
        (nameof(GlobalVerifyOverridesSource), Check.Sync(GlobalVerifyOverridesSource)),
        (nameof(CiVerifiesUnlessTheRunAsksOtherwise), Check.Sync(CiVerifiesUnlessTheRunAsksOtherwise)),
        (nameof(CustomComparer), Check.Sync(CustomComparer)),
        (nameof(CultureDoesNotChangeJson), Check.Sync(CultureDoesNotChangeJson)),
        (nameof(VerificationDetectsConcurrentChanges), Check.Sync(VerificationDetectsConcurrentChanges)),
        (nameof(CorruptJournalRefusesRollback), Check.Sync(CorruptJournalRefusesRollback)),
        (nameof(NullConfigurationStringsFailClearly), Check.Sync(NullConfigurationStringsFailClearly))
    };

    private static Result Sample() => new(0, "ok", new() { "one", "two" });

    private static void MultipleFiles()
    {
        using var f = new Fixture();
        var result = Sample();
        f.Run(() => { result.AssertSnapshot(); result.Message.AssertSnapshot("stdout"); result.Files.AssertSnapshot("files"); }, SnapshotUpdate.All);
        Check.True(File.Exists(f.FilePath("result.json")));
        Check.True(File.Exists(f.FilePath("stdout.txt")));
        using var json = JsonDocument.Parse(File.ReadAllText(f.FilePath("result.json")));
        Check.Equal(0, json.RootElement.GetProperty("ExitCode").GetInt32());
        Check.True(!json.RootElement.TryGetProperty("result", out _), "JSON must contain the object, not an entry wrapper.");
        f.Run(() => { result.AssertSnapshot(); result.Message.AssertSnapshot("stdout"); result.Files.AssertSnapshot("files"); });
    }

    private static void AutomaticNames()
    {
        using var f = new Fixture();
        var result = Sample();
        var state = new MutableState();
        f.Run(() =>
        {
            result.AssertSnapshot();
            result.Message.AssertSnapshot();
            state.AssertSnapshot();
            state.Count++;
            state.AssertSnapshot();
            Sample().AssertSnapshot();
        }, SnapshotUpdate.All);
        foreach (var name in new[] { "result.json", "Message.txt", "state.json", "state-2.json", "snapshot-1.json" })
        {
            Check.True(File.Exists(f.FilePath(name)), "Missing automatic name: " + name);
        }
    }

    private static void AnonymousGraphs()
    {
        using var f = new Fixture();
        var summary = new
        {
            ExitCode = 0,
            Items = new[] { new { Name = "A", Count = 1 }, new { Name = "B", Count = 2 } }.ToList()
        };
        var dictionary = new Dictionary<string, object>(); // Not captured: static object values are deliberately opaque.
        Check.Equal(0, dictionary.Count);
        f.Run(() => summary.AssertSnapshot(), SnapshotUpdate.All);
        f.Run(() => summary.AssertSnapshot());
        using var json = JsonDocument.Parse(File.ReadAllText(f.FilePath("summary.json")));
        Check.Equal("B", json.RootElement.GetProperty("Items")[1].GetProperty("Name").GetString());
    }

    private static void TuplesAndDictionaries()
    {
        using var f = new Fixture();
        var tuple = (Code: 1, Text: "a");
        var map = new Dictionary<string, int> { ["z"] = 1, ["a"] = 2 };
        var numbers = new Dictionary<int, string> { [3] = "three" };
        f.Run(() => { tuple.AssertSnapshot(); map.AssertSnapshot(); numbers.AssertSnapshot(); }, SnapshotUpdate.All);
        using var json = JsonDocument.Parse(File.ReadAllText(f.FilePath("tuple.json")));
        Check.Equal(1, json.RootElement.GetProperty("Item1").GetInt32());
        var text = File.ReadAllText(f.FilePath("map.json"));
        Check.True(text.IndexOf("\"a\"", StringComparison.Ordinal) < text.IndexOf("\"z\"", StringComparison.Ordinal));
        using var pairs = JsonDocument.Parse(File.ReadAllText(f.FilePath("numbers.json")));
        Check.Equal(3, pairs.RootElement[0].GetProperty("Key").GetInt32());
    }

    private static void MultidimensionalArrays()
    {
        using var f = new Fixture();
        var matrix = new int[,] { { 1, 2 }, { 3, 4 } };
        f.Run(() => matrix.AssertSnapshot(), SnapshotUpdate.All);
        using var json = JsonDocument.Parse(File.ReadAllText(f.FilePath("matrix.json")));
        Check.Equal(4, json.RootElement[1][1].GetInt32());
    }

    private static void NullableAndEnums()
    {
        using var f = new Fixture();
        int? absent = null;
        int? present = 42;
        var mode = Mode.Fast;
        var unknown = (Mode)17;
        f.Run(() => { absent.AssertSnapshot(); present.AssertSnapshot(); mode.AssertSnapshot(); unknown.AssertSnapshot(); }, SnapshotUpdate.All);
        Check.Equal("null\n", File.ReadAllText(f.FilePath("absent.json")));
        Check.Equal("42\n", File.ReadAllText(f.FilePath("present.json")));
        Check.Equal("\"Fast\"\n", File.ReadAllText(f.FilePath("mode.json")));
        Check.Equal("17\n", File.ReadAllText(f.FilePath("unknown.json")));
    }

    private static void BuiltInValues()
    {
        using var f = new Fixture();
        var values = new
        {
            Date = new DateOnly(2026, 9, 10),
            Time = new TimeOnly(12, 34, 56),
            Id = Guid.Parse("d2719cfb-3bde-4052-826f-a83920f4cf9d"),
            Large = BigInteger.Parse("123456789012345678901234567890123456789", CultureInfo.InvariantCulture),
            Money = 12.34m,
            Flag = true,
            Letter = 'x',
            Duration = TimeSpan.FromSeconds(30),
            Address = new Uri("https://example.invalid/path")
        };
        f.Run(() => values.AssertSnapshot(), SnapshotUpdate.All);
        f.Run(() => values.AssertSnapshot());
    }

    private static void CaptureIsImmediate()
    {
        using var f = new Fixture();
        var value = new MutableState { Count = 1 };
        f.Run(() => { value.AssertSnapshot("before"); value.Count = 2; value.AssertSnapshot("after"); }, SnapshotUpdate.All);
        using var before = JsonDocument.Parse(File.ReadAllText(f.FilePath("before.json")));
        using var after = JsonDocument.Parse(File.ReadAllText(f.FilePath("after.json")));
        Check.Equal(1, before.RootElement.GetProperty("Count").GetInt32());
        Check.Equal(2, after.RootElement.GetProperty("Count").GetInt32());
    }

    private static void MissingFails()
    {
        using var f = new Fixture();
        var error = Check.Throws<SnapshotMismatchException>(() => f.Run(() => 1.AssertSnapshot("value")));
        Check.Equal(SnapshotStatus.Missing, error.Report.Entries.Single().Status);
        Check.True(!Directory.Exists(f.Baselines()));
        Check.True(error.Report.ArtifactDirectory is not null);
    }

    private static void MissingDefaultsCreate()
    {
        using var f = new Fixture();
        var report = f.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.Inherit);
        Check.True(report.Success);
        Check.Equal(SnapshotStatus.Created, report.Entries.Single().Status);
        Check.Equal("1\n", File.ReadAllText(f.FilePath("value.json")));
    }

    private static void UpdateReplaces()
    {
        using var f = new Fixture();
        f.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
        f.Run(() => 2.AssertSnapshot("value"), SnapshotUpdate.All);
        f.Run(() => 2.AssertSnapshot("value"));
    }

    private static void MissingPolicyIsTransactional()
    {
        using var f = new Fixture();
        f.Run(() => 1.AssertSnapshot("one"), SnapshotUpdate.All);
        f.Run(() => { 1.AssertSnapshot("one"); 2.AssertSnapshot("two"); }, SnapshotUpdate.Missing);
        Check.Throws<SnapshotMismatchException>(() => f.Run(() =>
        {
            9.AssertSnapshot("one");
            2.AssertSnapshot("two");
            3.AssertSnapshot("three");
        }, SnapshotUpdate.Missing));
        Check.True(!File.Exists(f.FilePath("three.json")));
        Check.Equal("1\n", File.ReadAllText(f.FilePath("one.json")));
    }

    private static void AggregatesDifferences()
    {
        using var f = new Fixture();
        f.Run(() => { 1.AssertSnapshot("a"); 2.AssertSnapshot("b"); }, SnapshotUpdate.All);
        var error = Check.Throws<SnapshotMismatchException>(() => f.Run(() => { 8.AssertSnapshot("a"); 9.AssertSnapshot("b"); }));
        Check.Equal(2, error.Report.Entries.Count(x => x.Status == SnapshotStatus.Changed));
    }

    private static void GeneratedAttributePolicies()
    {
        using var f = new Fixture();
        using var project = new EnvironmentValue("IMPRINT_PROJECT_ROOT", f.Root);
        GeneratedFixtures.ClassDefault();
        Check.True(File.Exists(Path.Combine(f.Root, "__snapshots__", "Generated suite", "ClassDefault", "value.json")));
        Check.Throws<SnapshotMismatchException>(() => GeneratedFixtures.MethodVerify());
        GeneratedFixtures.RenamedMethod();
        Check.True(File.Exists(Path.Combine(f.Root, "__snapshots__", "Generated suite", "Readable method", "value.json")));
    }

    private static void ParameterizedIdentity()
    {
        using var f = new Fixture();
        using var project = new EnvironmentValue("IMPRINT_PROJECT_ROOT", f.Root);
        Check.Throws<SnapshotConfigurationException>(() => GeneratedFixtures.Parameterized("a"));
        GeneratedFixtures.ParameterizedWithCase("a");
        GeneratedFixtures.ParameterizedWithCase("b");
        Check.True(Directory.Exists(Path.Combine(f.Root, "__snapshots__", "Generated suite", "ParameterizedWithCase [a]")));
        Check.True(Directory.Exists(Path.Combine(f.Root, "__snapshots__", "Generated suite", "ParameterizedWithCase [b]")));
    }

    private static void ProjectConfiguration()
    {
        using var f = new Fixture();
        f.Configure("{\"update\":\"all\",\"naming\":{\"useFrameworkDisplayNames\":true}}");
        using var project = new EnvironmentValue("IMPRINT_PROJECT_ROOT", f.Root);
        GeneratedFixtures.PreferredDisplayName();
        Check.True(File.Exists(Path.Combine(f.Root, "__snapshots__", "Generated suite", "Readable generated display", "value.json")));
        f.Run(() => "text".AssertSnapshot("value"), SnapshotUpdate.Inherit);
        Check.True(File.Exists(f.FilePath("value.txt")));
    }

    private static void MethodLevelUpdate()
    {
        using var f = new Fixture();
        f.Run(() => { Snapshots.UpdateCurrentTest(); 1.AssertSnapshot("value"); });
        f.Run(() => 1.AssertSnapshot("value"));
    }

    private static void LatePolicyChangeFails()
    {
        using var f = new Fixture();
        Check.Throws<SnapshotConfigurationException>(() => f.Run(() =>
        {
            1.AssertSnapshot("value");
            Snapshots.UpdateCurrentTest();
        }));
        Check.True(!File.Exists(f.FilePath("value.json")));
    }

    private static void EntryUpdateCannotDeleteOthers()
    {
        using var f = new Fixture();
        f.Run(() => { 1.AssertSnapshot("a"); 2.AssertSnapshot("b"); }, SnapshotUpdate.All);
        Check.Throws<SnapshotMismatchException>(() => f.Run(() => 9.AssertSnapshot("a", new() { Update = SnapshotUpdate.All })));
        Check.Equal("1\n", File.ReadAllText(f.FilePath("a.json")));
        Check.True(File.Exists(f.FilePath("b.json")));
    }

    private static void WholeTestUpdatePrunes()
    {
        using var f = new Fixture();
        f.Run(() => { 1.AssertSnapshot("a"); 2.AssertSnapshot("b"); }, SnapshotUpdate.All);
        f.Run(() => 1.AssertSnapshot("a"), SnapshotUpdate.All);
        Check.True(!File.Exists(f.FilePath("b.json")));
    }

    private static void EmptyRequiresOptIn()
    {
        using var f = new Fixture();
        f.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
        Check.Throws<SnapshotCaptureException>(() => f.Run(() => { }, SnapshotUpdate.All));
        Snapshots.Run(() => { }, f.Options(SnapshotUpdate.All) with
        {
            AllowEmpty = true
        });
        Check.True(!File.Exists(f.FilePath("value.json")));
    }

    private static void LaterAssertionPreventsApproval()
    {
        using var f = new Fixture();
        f.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
        var sentinel = new InvalidOperationException("later assertion");
        var error = Check.Throws<InvalidOperationException>(() => f.Run(() =>
        {
            2.AssertSnapshot("value");
            throw sentinel;
        }, SnapshotUpdate.All));
        Check.True(ReferenceEquals(sentinel, error));
        Check.Equal("1\n", File.ReadAllText(f.FilePath("value.json")));
    }

    private static void CaughtCaptureStillPoisonsTest()
    {
        using var f = new Fixture();
        var node = new Link();
        node.Next = node;
        Check.Throws<SnapshotCaptureException>(() => f.Run(() =>
        {
            Check.Throws<SnapshotCaptureException>(() => node.AssertSnapshot("cycle"));
            1.AssertSnapshot("other");
        }, SnapshotUpdate.All));
        Check.True(!File.Exists(f.FilePath("other.json")));
    }

    private static void CaptureGeneric<T>(T value) => value.AssertSnapshot("generic");

    private static void GenericIncludeAndUnsupportedType()
    {
        using var f = new Fixture();
        f.Run(() => CaptureGeneric(new GenericOnly(7)), SnapshotUpdate.All);
        f.Run(() => CaptureGeneric(new GenericOnly(7)));
        Check.Throws<SnapshotCaptureException>(() => f.Run(() => CaptureGeneric(new NotRooted()), SnapshotUpdate.All, "Unsupported"));
    }

    private sealed record PrivateValue(int Code);
    private static void ExplicitPrivateWriter()
    {
        using var f = new Fixture();
        var value = new PrivateValue(7);
        f.Run(() => value.AssertSnapshot(static (writer, item, _) =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("Code", item.Code);
            writer.WriteEndObject();
        }, "private"), SnapshotUpdate.All);
        Check.True(File.ReadAllText(f.FilePath("private.json")).Contains("7", StringComparison.Ordinal));
    }

    private static void StructuralJson()
    {
        var comparer = DefaultSnapshotComparer.Instance;
        Check.True(comparer.Compare("{\"b\":2,\"a\":1}", "{\"a\":1.0,\"b\":2}", SnapshotFormat.Json, new()).Equal);
        Check.True(!comparer.Compare("[1,2]", "[2,1]", SnapshotFormat.Json, new()).Equal);
        Check.True(comparer.Compare("[1,2]", "[2,1]", SnapshotFormat.Json, new()
        {
            IgnoreArrayOrder = true
        }).Equal);
        Check.True(!comparer.Compare("[1,1]", "[1,2]", SnapshotFormat.Json, new()
        {
            IgnoreArrayOrder = true
        }).Equal);
    }

    private static void UnorderedToleranceUsesMaximumMatching()
    {
        Check.True(DefaultSnapshotComparer.Instance.Compare("[0,1]", "[0.5,-0.5]", SnapshotFormat.Json,
            new()
            {
                IgnoreArrayOrder = true,
                NumericTolerance = 0.6m
            }).Equal);
    }

    private static void ExactLargeNumbers()
    {
        var c = DefaultSnapshotComparer.Instance;
        Check.True(!c.Compare("9007199254740992", "9007199254740993", SnapshotFormat.Json, new()).Equal);
        Check.True(c.Compare("1e999999", "10e999998", SnapshotFormat.Json, new()).Equal);
        Check.True(c.Compare("0.10000000000000000000000000001", "0.1", SnapshotFormat.Json,
            new()
            {
                NumericTolerance = 0.0000000000000000000000000001m
            }).Equal);
    }

    private static void ComparisonDoesNotRewriteData()
    {
        using var f = new Fixture();
        const string original = "hello  \r\n";
        f.Run(() => original.AssertSnapshot("text"), SnapshotUpdate.All);
        f.Run(() => "HELLO\n".AssertSnapshot("text", new()
        {
            Comparison = new()
            {
                IgnoreStringCase = true,
                IgnoreTrailingWhitespace = true,
                IgnoreLineEndings = true
            }
        }));
        Check.Equal(original, File.ReadAllText(f.FilePath("text.txt")));
    }

    private static void RawJsonAndNull()
    {
        using var f = new Fixture();
        string? empty = null;
        f.Run(() =>
        {
            "{\"answer\":42}".AssertSnapshot("json", new()
            {
                StringContent = SnapshotStringContent.Json,
                Format = SnapshotFormat.Json
            });
            "{\"answer\":42}".AssertSnapshot("text");
            empty.AssertSnapshot();
        }, SnapshotUpdate.All);
        Check.True(File.Exists(f.FilePath("json.json")) && File.Exists(f.FilePath("text.txt")));
        Check.Equal("null\n", File.ReadAllText(f.FilePath("empty.json")));
    }

    private static void InvalidJsonCannotBeApproved()
    {
        using var f = new Fixture();
        f.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
        File.WriteAllText(f.FilePath("value.json"), "{broken");
        Check.Throws<JsonException>(() => f.Run(() => 2.AssertSnapshot("value"), SnapshotUpdate.All));
        Check.Equal("{broken", File.ReadAllText(f.FilePath("value.json")));
    }

    private static void DuplicateJsonPropertiesFail()
    {
        using var f = new Fixture();
        Check.Throws<SnapshotCaptureException>(() => f.Run(() =>
            "{\"a\":1,\"a\":2}".AssertSnapshot("value", new()
            {
                StringContent = SnapshotStringContent.Json,
                Format = SnapshotFormat.Json
            }), SnapshotUpdate.All));
        Check.True(!File.Exists(f.FilePath("value.json")));
    }

    private static void CancellationPreventsApproval()
    {
        using var f = new Fixture();
        using var token = new CancellationTokenSource();
        Check.Throws<OperationCanceledException>(() => Snapshots.Run(() =>
        {
            1.AssertSnapshot("value");
            token.Cancel();
        }, f.Options(SnapshotUpdate.All) with
        {
            CancellationToken = token.Token
        }));
        Check.True(!File.Exists(f.FilePath("value.json")));
    }

    private static void DisposalDoesNotApprove()
    {
        using var f = new Fixture();
        using (Snapshots.Begin(f.Options(SnapshotUpdate.All)))
        {
            1.AssertSnapshot("value");
        }

        Check.True(!File.Exists(f.FilePath("value.json")));
    }

    private static void CompleteOnlyOnce()
    {
        using var f = new Fixture();
        using var scope = Snapshots.Begin(f.Options(SnapshotUpdate.All));
        1.AssertSnapshot("value");
        scope.Complete();
        Check.Throws<SnapshotConfigurationException>(() => scope.Complete());
    }

    private static async Task AsyncContextFlows()
    {
        using var f = new Fixture();
        await Snapshots.RunAsync(async () =>
        {
            await Task.Yield();
            await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(() => i.AssertSnapshot("item-" + i))));
        }, f.Options(SnapshotUpdate.All));
        Check.Equal(10, Directory.GetFiles(f.Baselines(), "*.json").Length);
    }

    private static async Task ConcurrentTestsAreIsolated()
    {
        using var f = new Fixture();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Snapshots.RunAsync(async () =>
        {
            await Task.Delay(1);
            i.AssertSnapshot("value");
        }, f.Options(SnapshotUpdate.All, "Test" + i))));
        for (var i = 0; i < 8; i++)
        {
            Check.Equal(i.ToString(CultureInfo.InvariantCulture) + "\n", File.ReadAllText(f.FilePath("value.json", "Test" + i)));
        }
    }

    private static void ConcurrentUpdatesConflict()
    {
        using var f = new Fixture();
        f.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
        using var left = Snapshots.Begin(f.Options(SnapshotUpdate.All));
        2.AssertSnapshot("value");
        using (var right = Snapshots.Begin(f.Options(SnapshotUpdate.All)))
        {
            3.AssertSnapshot("value");
            right.Complete();
        }
        Check.Throws<SnapshotConflictException>(() => left.Complete());
        Check.Equal("3\n", File.ReadAllText(f.FilePath("value.json")));
    }

    private static void OwnershipCollisionsFail()
    {
        using var f = new Fixture();
        f.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
        var options = f.Options(SnapshotUpdate.All);
        options = options with
        {
            Identity = options.Identity! with
            {
                LogicalId = "another-logical-test"
            }
        };
        Check.Throws<SnapshotConflictException>(() => Snapshots.Run(() => 1.AssertSnapshot("value"), options));
    }

    private static void PortableAndDuplicateNames()
    {
        using var f = new Fixture();
        f.Run(() => 1.AssertSnapshot("../../escape"), SnapshotUpdate.All);
        Check.Equal(1, Directory.GetFiles(f.Baselines(), "*.json").Length);
        Check.True(!File.Exists(Path.Combine(f.Root, "escape.json")));
        Check.Throws<SnapshotCaptureException>(() => f.Run(() =>
        {
            1.AssertSnapshot("Same");
            2.AssertSnapshot("same");
        }, SnapshotUpdate.All, "Duplicates"));
    }

    private static void SizeAndDepthLimits()
    {
        using var f = new Fixture();
        Check.Throws<SnapshotCaptureException>(() => Snapshots.Run(() => new string('x', 100).AssertSnapshot("value"),
            f.Options(SnapshotUpdate.All) with
            {
                MaxBytesPerSnapshot = 16
            }));
        var nested = new
        {
            A = new
            {
                B = new
                {
                    C = 1
                }
            }
        };
        Check.Throws<SnapshotCaptureException>(() => Snapshots.Run(() => nested.AssertSnapshot("value"),
            f.Options(SnapshotUpdate.All) with
            {
                MaxNestingDepth = 2
            }));
    }

    private static void StrictConfiguration()
    {
        using var f = new Fixture();
        f.Configure("{\"udpate\":\"all\"}");
        Check.Throws<SnapshotConfigurationException>(() => f.Run(() => 1.AssertSnapshot("value")));
    }

    private static void NoImplicitScope() => Check.Throws<SnapshotConfigurationException>(() => 1.AssertSnapshot("value"));

    private static void EntryFormatMigration()
    {
        using var f = new Fixture();
        f.Run(() => { "{\"v\":1}".AssertSnapshot("value"); 2.AssertSnapshot("other"); }, SnapshotUpdate.All);
        f.Run(() =>
        {
            "{\"v\":1}".AssertSnapshot("value", new()
            {
                StringContent = SnapshotStringContent.Json,
                Format = SnapshotFormat.Json,
                Update = SnapshotUpdate.All
            });
            2.AssertSnapshot("other");
        });
        Check.True(!File.Exists(f.FilePath("value.txt")) && File.Exists(f.FilePath("value.json")));
    }

    private static void AmbiguousBaselinesFail()
    {
        using var f = new Fixture();
        f.Run(() => "one".AssertSnapshot("value"), SnapshotUpdate.All);
        File.WriteAllText(f.FilePath("value.json"), "one");
        Check.Throws<SnapshotConflictException>(() => f.Run(() => "two".AssertSnapshot("value"), SnapshotUpdate.All));
    }

    private static string SimulateInterruptedCommit(Fixture f)
    {
        f.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(f.Baselines().Replace('\\', '/').ToUpperInvariant()))).ToLowerInvariant();
        string fingerprint;
        using (var scope = Snapshots.Begin(f.Options()))
        {
            fingerprint = scope.BaselineFingerprint;
        }

        var metadata = Path.Combine(f.Root, "artifacts", "imprint", "storage");
        var journal = Path.Combine(metadata, "transactions", key);
        Directory.CreateDirectory(Path.Combine(journal, "before"));
        Directory.CreateDirectory(Path.Combine(journal, "after"));
        File.Copy(f.FilePath("value.json"), Path.Combine(journal, "before", "value.json"));
        File.WriteAllText(Path.Combine(journal, "before.fingerprint"), fingerprint);
        File.WriteAllText(Path.Combine(journal, "prepared"), "TheLithium.Imprint/1\n");
        File.WriteAllText(f.FilePath("value.json"), "9\n");
        return journal;
    }

    private static void InterruptedCommitRecovers()
    {
        using var f = new Fixture();
        var journal = SimulateInterruptedCommit(f);
        f.Run(() => 1.AssertSnapshot("value"));
        Check.Equal("1\n", File.ReadAllText(f.FilePath("value.json")));
        Check.True(!Directory.Exists(journal));
    }

    private static void ReadOnlyRefusesRecovery()
    {
        using var f = new Fixture();
        SimulateInterruptedCommit(f);
        using (new EnvironmentValue("IMPRINT_UPDATE", "verify"))
        {
            Check.Throws<SnapshotConflictException>(() => f.Run(() => 1.AssertSnapshot("value")));
        }

        Check.Equal("9\n", File.ReadAllText(f.FilePath("value.json")));
        f.Run(() => 1.AssertSnapshot("value"));
    }

    private static void GlobalVerifyOverridesSource()
    {
        using var f = new Fixture();
        using var verify = new EnvironmentValue("IMPRINT_UPDATE", "verify");
        Check.Throws<SnapshotMismatchException>(() => f.Run(() =>
            1.AssertSnapshot("value", new()
            {
                Update = SnapshotUpdate.All
            }), SnapshotUpdate.All));
        Check.True(!File.Exists(f.FilePath("value.json")));
    }

    private static void CiVerifiesUnlessTheRunAsksOtherwise()
    {
        using var f = new Fixture();
        using var ci = new EnvironmentValue("CI", "true");
        // A committed "all" policy must not write in continuous integration.
        Check.Throws<SnapshotMismatchException>(() => f.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All));
        // An update requested for this run is a deliberate act and overrides the default.
        using var requested = new EnvironmentValue("IMPRINT_UPDATE", "all");
        f.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
    }

    private sealed class LengthComparer : ISnapshotComparer
    {
        public SnapshotComparisonResult Compare(string expected, string received, SnapshotFormat format, ResolvedSnapshotComparison options)
            => new(expected.Length == received.Length, "Text lengths differ.");
    }
    private static void CustomComparer()
    {
        using var f = new Fixture();
        f.Run(() => "abc".AssertSnapshot("value"), SnapshotUpdate.All);
        f.Run(() => "xyz".AssertSnapshot("value", new() { Comparer = new LengthComparer() }));
        Check.Equal("abc", File.ReadAllText(f.FilePath("value.txt")));
    }

    private static void CultureDoesNotChangeJson()
    {
        using var f = new Fixture();
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-CH");
            12.5m.ToString(CultureInfo.CurrentCulture); // This culture intentionally uses a different context.
            f.Run(() => new { Value = 12.5m }.AssertSnapshot("value"), SnapshotUpdate.All);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            f.Run(() => new { Value = 12.5m }.AssertSnapshot("value"));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
    private static void VerificationDetectsConcurrentChanges()
    {
        using var f = new Fixture();
        f.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All);
        using var scope = Snapshots.Begin(f.Options());
        1.AssertSnapshot("value");
        File.WriteAllText(f.FilePath("value.json"), "2\n");
        Check.Throws<SnapshotConflictException>(() => scope.Complete());
        Check.Equal("2\n", File.ReadAllText(f.FilePath("value.json")));
    }

    private static void CorruptJournalRefusesRollback()
    {
        using var f = new Fixture();
        var journal = SimulateInterruptedCommit(f);
        File.Delete(Path.Combine(journal, "before", "value.json"));
        Check.Throws<SnapshotConflictException>(() => f.Run(() => 1.AssertSnapshot("value")));
        Check.Equal("9\n", File.ReadAllText(f.FilePath("value.json")));
        Check.True(Directory.Exists(journal), "A corrupt backup must be preserved for inspection.");
    }

    private static void NullConfigurationStringsFailClearly()
    {
        using var f = new Fixture();
        foreach (var json in new[]
        {
            "{\"update\":null}",
            "{\"files\":{\"snapshotFolderName\":null}}",
            "{\"files\":{\"failureArtifactPath\":null}}",
            // A property that was grouped or renamed must be rejected under its old spelling.
            "{\"directoryName\":\"__snapshots__\"}",
            "{\"maxDepth\":64}",
            "{\"allowEmpty\":true}",
            "{\"files\":{\"textExtension\":\"txt\"}}",
            "{\"limits\":{\"maxNodes\":1000}}",
            // A group given a flat value must be rejected rather than silently ignored.
            "{\"naming\":\"order\"}"
        })
        {
            f.Configure(json);
            Check.Throws<SnapshotConfigurationException>(() => f.Run(() => 1.AssertSnapshot("value")));
        }
    }

}
