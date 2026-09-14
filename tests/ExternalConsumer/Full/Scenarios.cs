using System.Text.Json;
using TheLithium.Imprint;
using TheLithium.Imprint.Comparison;
using TheLithium.Imprint.Generation;

namespace ExternalConsumer;

internal static class Scenarios
{
    internal static (string Name, Func<Task> Run)[] All =>
    [
        Sync(nameof(DefaultAndAllAssertionOverloads), DefaultAndAllAssertionOverloads),
        Sync(nameof(FormatsAndApplicationJson), FormatsAndApplicationJson),
        Sync(nameof(RepresentationScopes), RepresentationScopes),
        Sync(nameof(ComparisonLayers), ComparisonLayers),
        Sync(nameof(ComparisonBehavior), ComparisonBehavior),
        Sync(nameof(UpdateLayersAndWholeSetAuthorization), UpdateLayersAndWholeSetAuthorization),
        Sync(nameof(EnvironmentPolicies), EnvironmentPolicies),
        Sync(nameof(NamesPathsAndIdentity), NamesPathsAndIdentity),
        Sync(nameof(NamingModes), NamingModes),
        Sync(nameof(LimitsAndInvalidConfiguration), LimitsAndInvalidConfiguration),
        Sync(nameof(ScopesReportsAndArtifacts), ScopesReportsAndArtifacts),
        Sync(nameof(ArtifactFailureAndUnusedEntries), ArtifactFailureAndUnusedEntries),
        (nameof(AsyncScopesAndIsolation), AsyncScopesAndIsolation),
        Sync(nameof(CustomWriterAndRooting), CustomWriterAndRooting),
        Sync(nameof(AnonymousShapes), AnonymousShapes),
        (nameof(OrdinaryGeneratedLifetimes), OrdinaryGeneratedLifetimes),
        Sync(nameof(UnsafeConfigurationAndCapture), UnsafeConfigurationAndCapture)
    ];

    private static (string, Func<Task>) Sync(string name, Action action)
        => (name, () => { action(); return Task.CompletedTask; }
    );

    private static Payload Value() => new(7, Mode.Ready, [0, 255], new() { ["state"] = Mode.Ready });

    private static void DefaultAndAllAssertionOverloads()
    {
        var test = new ConsumerCase();
        var value = Value();
        var created = test.Run(() =>
        {
            value.AssertSnapshot();
            1.AssertSnapshot(static (writer, number, _) => writer.WriteNumberValue(number), "explicit");
        });
        Expect.True(created.Success);
        Expect.True(created.Entries.All(entry => entry.Status == SnapshotStatus.Created));
        Expect.True(File.Exists(Path.Combine(test.DirectoryPath, "value.json")));
        test.Run(() =>
        {
            (value with { Id = 8 }).UpdateSnapshot("value");
            2.UpdateSnapshot(static (writer, number, _) => writer.WriteNumberValue(number), "explicit", new() { Update = SnapshotUpdate.Verify });
        });
        Expect.Equal("2\n", test.Read("explicit.json"));
        var matched = test.Run(() =>
        {
            (value with { Id = 8 }).AssertSnapshot("value");
            2.AssertSnapshot("explicit");
        });
        Expect.True(matched.Entries.All(entry => entry.Status == SnapshotStatus.Matched));
    }

    private static void FormatsAndApplicationJson()
    {
        var test = new ConsumerCase();
        const string readable = "it's <b>café — naïve</b> & 注文が完了しました \"quoted\" C:\\temp\\file.txt\t\n\b\f";
        var wire = JsonSerializer.Serialize(new WirePayload(9), WireJsonContext.Default.WirePayload);
        test.Run(() =>
        {
            "{\"looks\":true}".AssertSnapshot("auto");
            "hello".AssertSnapshot("quoted", new() { Format = SnapshotFormat.Json });
            new Dictionary<string, string> { ["注文"] = readable }.AssertSnapshot("readable");
            "first\nsecond".AssertSnapshot("text", new() { Format = SnapshotFormat.Text });
            ((string?)null).AssertSnapshot("null");
            wire.AssertSnapshot("wire", new() { StringContent = SnapshotStringContent.Json });
            "[1,1]".UpdateSnapshot("array", new() { Format = SnapshotFormat.Json, StringContent = SnapshotStringContent.Json });
        });
        Expect.Equal("{\"looks\":true}", test.Read("auto.txt"));
        Expect.Equal("\"hello\"\n", test.Read("quoted.json"));
        Expect.Equal("first\nsecond", test.Read("text.txt"));
        Expect.Equal("null\n", test.Read("null.json"));
        Expect.Equal(wire, test.Read("wire.json"));
        var readableJson = test.Read("readable.json");
        Expect.True(readableJson.Contains("\"注文\": \"it's <b>café — naïve</b> & 注文が完了しました", StringComparison.Ordinal));
        using var readableDocument = JsonDocument.Parse(readableJson);
        Expect.Equal(readable, readableDocument.RootElement.GetProperty("注文").GetString());
        using var parsed = JsonDocument.Parse(test.Read("wire.json"));
        Expect.Equal(9, parsed.RootElement.GetProperty("wire_id").GetInt32());

        var layered = new ConsumerCase();
        layered.Configure("""{"stringContent":"json"}""");
        layered.Run(() => "1".AssertSnapshot("project"));
        Expect.Equal("1", layered.Read("project.json"));
        layered.Run(() =>
        {
            "1".AssertSnapshot("project", new() { StringContent = SnapshotStringContent.Json });
            "hello".AssertSnapshot("test");
        }, layered.Options with
        {
            StringContent = SnapshotStringContent.Value
        });
        layered.Run(() =>
        {
            Snapshots.Current.StringContent = SnapshotStringContent.Value;
            "1".AssertSnapshot("project", new() { StringContent = SnapshotStringContent.Json });
            "hello".AssertSnapshot("test");
        });
    }

    private static void RepresentationScopes()
    {
        var test = new ConsumerCase();
        test.Configure("""{"representation":{"enums":"number","byteArrays":"base64","dictionaries":"entries"}}""");
        test.Run(() => Value().AssertSnapshot("project"));
        using (var document = JsonDocument.Parse(test.Read("project.json")))
        {
            Expect.Equal(1, document.RootElement.GetProperty("Mode").GetInt32());
            Expect.Equal("AP8=", document.RootElement.GetProperty("Bytes").GetString());
            Expect.Equal(JsonValueKind.Array, document.RootElement.GetProperty("Labels").ValueKind);
        }
        var caseKey = SnapshotCases.Create(new { State = Mode.Ready });
        test.Run(() =>
        {
            Snapshots.Current.Representation = new() { ByteArrays = SnapshotByteArrayRepresentation.Numbers };
            Snapshots.Current.Representation = new() { Dictionaries = SnapshotDictionaryRepresentation.Automatic };
            Value().AssertSnapshot("test");
            Value().AssertSnapshot("nearer-category", new()
            {
                Representation = new()
                {
                    Enums = SnapshotEnumRepresentation.Number
                }
            });
            Value().AssertSnapshot("assertion", new()
            {
                Representation = new()
                {
                    Enums = SnapshotEnumRepresentation.NameOrNumber
                }
            });
            Expect.Equal(caseKey, SnapshotCases.Create(new { State = Mode.Ready }));
            Expect.Throws<SnapshotConfigurationException>(() => Snapshots.Current.Representation = new());
        }, test.Options with
        {
            Update = SnapshotUpdate.All,
            Representation = new()
            {
                Enums = SnapshotEnumRepresentation.NameOrNumber
            }
        });
        using var actual = JsonDocument.Parse(test.Read("assertion.json"));
        Expect.Equal("Ready", actual.RootElement.GetProperty("Mode").GetString());
        Expect.Equal(255, actual.RootElement.GetProperty("Bytes")[1].GetInt32());
        Expect.Equal("Ready", actual.RootElement.GetProperty("Labels").GetProperty("state").GetString());
        using var nearer = JsonDocument.Parse(test.Read("nearer-category.json"));
        Expect.Equal(1, nearer.RootElement.GetProperty("Mode").GetInt32());
        Expect.Throws<SnapshotConfigurationException>(() => test.Run(() => Value().AssertSnapshot("invalid", new()
        {
            Representation = new() { Enums = (SnapshotEnumRepresentation)99 }
        })));
    }

    private static void ComparisonLayers()
    {
        var test = new ConsumerCase();
        test.Configure("""{"comparison":{"numericTolerance":2,"ignoreArrayOrder":true,"ignoreStringCase":true,"ignoreLineEndings":false,"ignoreTrailingWhitespace":true,"maxUnorderedArrayLength":9}}""");
        test.Run(() => 1.AssertSnapshot("value"));
        var called = false;
        test.Run(() =>
        {
            Expect.Equal(3m, Snapshots.Current.Comparison.NumericTolerance);
            Snapshots.Current.Comparison = new() { NumericTolerance = 4 };
            20.AssertSnapshot("value", new()
            {
                Comparison = new()
                {
                    NumericTolerance = 0,
                    IgnoreArrayOrder = false
                },
                Comparer = new InspectingComparer(options =>
                {
                    called = true;
                    Expect.Equal(0m, options.NumericTolerance);
                    Expect.True(!options.IgnoreArrayOrder);
                    Expect.True(!options.IgnoreStringCase);
                    Expect.True(!options.IgnoreLineEndings);
                    Expect.True(options.IgnoreTrailingWhitespace);
                    Expect.Equal(9, options.MaxUnorderedArrayLength);
                })
            });
            Expect.Throws<SnapshotConfigurationException>(() => Snapshots.Current.Comparison = new());
            Expect.Throws<SnapshotConfigurationException>(() => Snapshots.Current.StringContent = SnapshotStringContent.Json);
        }, test.Options with
        {
            Comparison = new()
            {
                NumericTolerance = 3,
                IgnoreStringCase = false
            }
        });
        Expect.True(called);
        Expect.Equal("1\n", test.Read("value.json"));
    }

    private static void ComparisonBehavior()
    {
        var comparer = DefaultSnapshotComparer.Instance;
        Expect.True(comparer.Compare("1", "1.0", SnapshotFormat.Json, new()).Equal);
        Expect.True(comparer.Compare("[1,2,2]", "[2,1,2]", SnapshotFormat.Json, new() { IgnoreArrayOrder = true }).Equal);
        Expect.True(!comparer.Compare("[1,1,2]", "[1,2,2]", SnapshotFormat.Json, new() { IgnoreArrayOrder = true }).Equal);
        Expect.True(comparer.Compare("1", "1.2", SnapshotFormat.Json, new() { NumericTolerance = .2m }).Equal);
        Expect.True(comparer.Compare("\"A\"", "\"a\"", SnapshotFormat.Json, new() { IgnoreStringCase = true }).Equal);
        Expect.True(!comparer.Compare("{\"A\":1}", "{\"a\":1}", SnapshotFormat.Json, new() { IgnoreStringCase = true }).Equal);
        Expect.True(comparer.Compare("a \r\n", "a\n", SnapshotFormat.Text, new() { IgnoreTrailingWhitespace = true }).Equal);
        Expect.True(!comparer.Compare("a\r\n", "a\n", SnapshotFormat.Text, new() { IgnoreLineEndings = false }).Equal);
        Expect.Throws<SnapshotException>(() => comparer.Compare("[1,2]", "[2,1]", SnapshotFormat.Json, new() { IgnoreArrayOrder = true, MaxUnorderedArrayLength = 1 }));
        var difference = comparer.Compare("{\"a\":1}", "{\"a\":2}", SnapshotFormat.Json, new());
        Expect.True(difference.Difference?.Contains("$[\"a\"]", StringComparison.Ordinal) == true);
        Expect.True(difference.Difference?.Contains("--- expected", StringComparison.Ordinal) == true);
    }

    private static void UpdateLayersAndWholeSetAuthorization()
    {
        var test = new ConsumerCase();
        test.Configure("""{"update":"verify"}""");
        Expect.Throws<SnapshotMismatchException>(() => test.Run(() => 1.AssertSnapshot("value")));
        test.Run(() => 1.AssertSnapshot("value"), test.Options with { Update = SnapshotUpdate.Missing });
        test.Run(() => { Snapshots.UpdateCurrentTest(); 2.AssertSnapshot("value"); });
        test.Run(() => 3.AssertSnapshot("value", new() { Update = SnapshotUpdate.All }));
        Expect.Equal("3\n", test.Read("value.json"));
        Expect.Throws<SnapshotMismatchException>(() => test.Run(() =>
        {
            4.AssertSnapshot("value");
            5.UpdateSnapshot("new");
        }, test.Options with
        {
            Update = SnapshotUpdate.Missing
        }));
        Expect.Equal("3\n", test.Read("value.json"));
        Expect.True(!File.Exists(Path.Combine(test.DirectoryPath, "new.json")));
        var removed = test.Run(() => { }, test.Options with { Update = SnapshotUpdate.All, AllowEmpty = true });
        Expect.Equal(SnapshotStatus.Removed, removed.Entries.Single().Status);
    }

    private static void EnvironmentPolicies()
    {
        var test = new ConsumerCase();
        using (new EnvironmentSetting("CI", "true"))
        {
            Expect.Throws<SnapshotMismatchException>(() => test.Run(() => 1.UpdateSnapshot("value"), test.Options with { Update = SnapshotUpdate.All }));
            using (new EnvironmentSetting("IMPRINT_UPDATE", "all")) test.Run(() => 1.AssertSnapshot("value"));
        }
        using (new EnvironmentSetting("IMPRINT_UPDATE", "verify"))
        {
            Expect.Throws<SnapshotMismatchException>(() => test.Run(() => 2.UpdateSnapshot("value")));
            Expect.Equal("1\n", test.Read("value.json"));
        }
        using (new EnvironmentSetting("IMPRINT_UPDATE", "invalid"))
            Expect.Throws<SnapshotConfigurationException>(() => test.Run(() => 1.AssertSnapshot("value")));
        var relocated = new ConsumerCase();
        using (new EnvironmentSetting("IMPRINT_PROJECT_ROOT", relocated.Root)) test.Run(() => 7.AssertSnapshot("relocated"));
        Expect.True(test.DirectoryPath.StartsWith(relocated.Root, StringComparison.Ordinal));
    }

    private static void NamesPathsAndIdentity()
    {
        var test = new ConsumerCase();
        test.Configure("""{"files":{"snapshotFolderName":"approved","failureArtifactPath":"failures"}}""", "selected.json");
        var options = test.Options with { ConfigurationFile = "selected.json", Name = "renamed", Suite = "suite", Case = "row", Variant = "net" };
        test.Run(() => 1.AssertSnapshot("value"), options);
        Expect.True(test.DirectoryPath.Contains("approved", StringComparison.Ordinal));
        Expect.True(test.DirectoryPath.Contains("renamed", StringComparison.Ordinal));
        Expect.True(test.DirectoryPath.Contains("row", StringComparison.Ordinal));
        Expect.True(test.DirectoryPath.Contains("net", StringComparison.Ordinal));
        var error = Expect.Throws<SnapshotMismatchException>(() => test.Run(() => 2.AssertSnapshot("value"), options));
        Expect.True(error.Report.ArtifactDirectory?.StartsWith(Path.Combine(test.Root, "failures"), StringComparison.Ordinal) == true);
        test.Configure("""{"files":{"snapshotRootPath":"project-root"}}""");
        test.Run(() => 3.AssertSnapshot("value"));
        Expect.True(test.DirectoryPath.StartsWith(Path.Combine(test.Root, "project-root"), StringComparison.Ordinal));
        test.Run(() => 4.AssertSnapshot("value"), test.Options with { RootDirectory = "test-root", ArtifactDirectory = "test-failures" });
        Expect.True(test.DirectoryPath.StartsWith(Path.Combine(test.Root, "test-root"), StringComparison.Ordinal));
        var identity = new SnapshotTestIdentity(test.Root, "Case.cs", "suite", "name", "case", "variant", "logical");
        var (_, _, suite, name, row, variant, logical) = identity;
        Expect.Equal("suite/name/case/variant/logical", $"{suite}/{name}/{row}/{variant}/{logical}");
    }

    private static void NamingModes()
    {
        var test = new ConsumerCase();
        test.Configure("""{"naming":{"unnamedCaptures":"order"}}""");
        var count = 1;
        test.Run(() => { count.AssertSnapshot(); (count + 1).AssertSnapshot(); });
        Expect.Equal("1\n", test.Read("snapshot-1.json"));
        Expect.Equal("2\n", test.Read("snapshot-2.json"));
        var explicitTest = new ConsumerCase();
        explicitTest.Configure("""{"naming":{"unnamedCaptures":"explicit-only"}}""");
        Expect.Throws<SnapshotCaptureException>(() => explicitTest.Run(() => count.AssertSnapshot()));
        explicitTest.Run(() => count.AssertSnapshot(), explicitTest.Options with { Naming = SnapshotNaming.NameThenOrder });
        Expect.Equal("1\n", explicitTest.Read("count.json"));
    }

    private static void LimitsAndInvalidConfiguration()
    {
        foreach (var json in new[]
        {
            """{"limits":{"maxNestingDepth":0}}""", """{"limits":{"maxValuesPerSnapshot":0}}""",
            """{"limits":{"maxBytesPerSnapshot":0}}""", """{"limits":{"lockTimeoutSeconds":0}}""",
            """{"unknown":true}""", """{"update":"all","update":"missing"}""",
            """{"representation":{"enums":null}}""", """{"files":{"failureArtifactPath":null}}""",
            """{"version":2}""", """{"update":"inherit"}""", """{"comparison":{"numericTolerance":-1}}"""
        })
        {
            var invalid = new ConsumerCase();
            invalid.Configure(json);
            Expect.Throws<SnapshotConfigurationException>(() => invalid.Run(() => 1.AssertSnapshot("value"), invalid.Options with
            {
                MaxNestingDepth = 64,
                MaxValuesPerSnapshot = 100,
                MaxBytesPerSnapshot = 1024
            }));
        }
        var test = new ConsumerCase();
        test.Configure("""{"limits":{"maxNestingDepth":1,"maxValuesPerSnapshot":2,"maxBytesPerSnapshot":16,"lockTimeoutSeconds":1}}""");
        var nodes = Expect.Throws<SnapshotCaptureException>(() => test.Run(() => new[] { 1, 2 }.AssertSnapshot("nodes"), test.Options with { MaxNestingDepth = 8 }));
        Expect.True(nodes.Message.Contains("node", StringComparison.OrdinalIgnoreCase));
        Expect.Throws<SnapshotCaptureException>(() => test.Run(() => new string('x', 17).AssertSnapshot("bytes")));
        Expect.Throws<SnapshotCaptureException>(() => test.Run(() => new[] { new[] { 1 } }.AssertSnapshot("depth")));
        test.Run(() => new[] { new[] { 1, 2 } }.AssertSnapshot("value"), test.Options with
        {
            MaxNestingDepth = 8,
            MaxValuesPerSnapshot = 8,
            MaxBytesPerSnapshot = 1024
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Expect.Throws<OperationCanceledException>(() => test.Run(() => 1.AssertSnapshot("value"), test.Options with { CancellationToken = cancellation.Token }));
        Expect.Throws<SnapshotConfigurationException>(() => test.Run(() => { }, test.Options with { ConfigurationFile = "absent.json" }));
        var empty = new ConsumerCase();
        Expect.Throws<SnapshotCaptureException>(() => empty.Run(() => { }));
        empty.Configure("""{"allowEmptyTests":true}""");
        Expect.True(empty.Run(() => { }).Success);
    }

    private static void ScopesReportsAndArtifacts()
    {
        var test = new ConsumerCase();
        Expect.Throws<SnapshotConfigurationException>(() => _ = Snapshots.Current);
        using (var scope = Snapshots.Begin(test.Options))
        {
            test.DirectoryPath = scope.BaselineDirectory;
            Expect.True(scope.BaselineFingerprint.Length > 0);
            Expect.Equal(SnapshotUpdate.Missing, scope.EffectiveUpdate);
            1.AssertSnapshot("value");
            var report = scope.Complete();
            Expect.Equal(SnapshotStatus.Created, report.Entries.Single().Status);
            Expect.Throws<SnapshotConfigurationException>(() => scope.Complete());
        }
        using (var scope = Snapshots.Begin(test.Options with
        {
            Update = SnapshotUpdate.All
        }))
        {
            2.AssertSnapshot("value");
            var original = new InvalidOperationException("application failure");
            scope.Abort(original);
            Expect.True(original.Data.Contains("TheLithium.Imprint.Artifacts"));
            Expect.True(scope.ArtifactDirectory is not null);
        }
        Expect.Equal("1\n", test.Read("value.json"));
        using (Snapshots.Begin(test.Options with { Update = SnapshotUpdate.All })) 3.AssertSnapshot("value");
        Expect.Equal("1\n", test.Read("value.json"));
        var mismatch = Expect.Throws<SnapshotMismatchException>(() => test.Run(() => 4.AssertSnapshot("value")));
        Expect.Equal(SnapshotStatus.Changed, mismatch.Report.Entries.Single().Status);
        Expect.True(mismatch.Report.ArtifactError is null);
        Expect.True(File.Exists(Path.Combine(mismatch.Report.ArtifactDirectory ?? "missing", "run.json")));
        using var artifact = JsonDocument.Parse(File.ReadAllText(Path.Combine(mismatch.Report.ArtifactDirectory ?? "missing", "run.json")));
        Expect.Equal("mismatch", artifact.RootElement.GetProperty("status").GetString());
        using var outer = Snapshots.Begin(test.Options with { AllowEmpty = true, Name = "outer" });
        using (var inner = Snapshots.Begin(test.Options with { AllowEmpty = true, Name = "inner" })) inner.Complete();
        Expect.True(ReferenceEquals(outer, Snapshots.Current));
        outer.Complete();
    }

    private static void ArtifactFailureAndUnusedEntries()
    {
        var test = new ConsumerCase();
        test.Run(() => { 1.AssertSnapshot("retained"); 2.AssertSnapshot("unused"); });
        var unused = Expect.Throws<SnapshotMismatchException>(() => test.Run(() => 1.AssertSnapshot("retained")));
        Expect.Equal(SnapshotStatus.Unused, unused.Report.Entries.Single(entry => entry.Name == "unused").Status);
        Expect.Equal("2\n", test.Read("unused.json"));

        var artifactRoot = Path.Combine(test.Root, "blocked-artifacts");
        using var scope = Snapshots.Begin(test.Options with { ArtifactDirectory = artifactRoot });
        3.AssertSnapshot("retained");
        2.AssertSnapshot("unused");
        File.WriteAllText(artifactRoot, "A file prevents creating the artifact directory.");
        var mismatch = Expect.Throws<SnapshotMismatchException>(() => scope.Complete());
        Expect.True(!mismatch.Report.Success);
        Expect.True(!string.IsNullOrEmpty(mismatch.Report.ArtifactError));
        Expect.True(mismatch.Report.ArtifactDirectory is null);
        Expect.Equal("1\n", test.Read("retained.json"));
    }

    private static async Task AsyncScopesAndIsolation()
    {
        var first = new ConsumerCase();
        var second = new ConsumerCase();
        await Task.WhenAll(
            Snapshots.RunAsync(async () =>
            {
                Snapshots.Current.Representation = new() { Enums = SnapshotEnumRepresentation.Number };
                first.DirectoryPath = Snapshots.Current.BaselineDirectory;
                await Task.Yield();
                Mode.Ready.AssertSnapshot("value");
            }, first.Options),
            Snapshots.RunAsync(async () =>
            {
                second.DirectoryPath = Snapshots.Current.BaselineDirectory;
                await Task.Yield();
                Mode.Ready.AssertSnapshot("value");
            }, second.Options));
        Expect.Equal("1\n", first.Read("value.json"));
        Expect.Equal("\"Ready\"\n", second.Read("value.json"));
        Expect.Throws<SnapshotConfigurationException>(() => _ = Snapshots.Current);
    }

    private static void CustomWriterAndRooting()
    {
        SnapshotWriters.Register<RegisteredId>(static (writer, value, _) => writer.WriteNumberValue(value.Id));
        SnapshotWriters.TryRegister<RegisteredId>(static (writer, _, _) => writer.WriteNumberValue(-1));
        SnapshotWriters.TryRegister(new RegisteredId(0), static (writer, _, _) => writer.WriteNumberValue(-2));
        var test = new ConsumerCase();
        test.Run(() =>
        {
            new RegisteredId(9).AssertSnapshot("registered");
            CaptureGeneric(new IncludedPayload(10));
            Value().AssertSnapshot(static (writer, value, context) =>
            {
                Expect.Equal("$", context.Path);
                SnapshotEnumRepresentation enums = context.Representation.Enums;
                Expect.Equal(SnapshotEnumRepresentation.NameOrNumber, enums);
                Expect.Equal(SnapshotByteArrayRepresentation.Numbers, context.Representation.ByteArrays);
                Expect.Equal(SnapshotDictionaryRepresentation.Automatic, context.Representation.Dictionaries);
                writer.WriteStartArray();
                using (context.At(0))
                {
                    Expect.Equal("$[0]", context.Path);
                    SnapshotWriters.Write(writer, value.Mode, context);
                }
                using (context.At("a")) Expect.Equal("$[\"a\"]", context.Path);
                Expect.Equal("$", context.Path);
                writer.WriteEndArray();
            }, "custom");
        });
        Expect.Equal("9\n", test.Read("registered.json"));
        using var included = JsonDocument.Parse(test.Read("included.json"));
        Expect.Equal(10, included.RootElement.GetProperty("Id").GetInt32());
    }

    private static void CaptureGeneric<T>(T value) => value.AssertSnapshot("included");

    private static void AnonymousShapes()
    {
        var test = new ConsumerCase();
        var value = new { Name = "entry", State = Mode.Ready };
        test.Run(() =>
        {
            new[] { value }.AssertSnapshot("array");
            new List<object>().AssertSnapshot(static (writer, _, _) => { writer.WriteStartArray(); writer.WriteEndArray(); }, "explicit");
            new[,] { { 1, 2 } }.AssertSnapshot("matrix");
            new[, ,] { { { 3 } } }.AssertSnapshot("cube");
            new Dictionary<string, int> { ["a"] = 1 }.AssertSnapshot("dictionary");
            new Dictionary<int, string> { [1] = "a" }.AssertSnapshot("entries");
            (value, 1).AssertSnapshot("tuple");
        });
        using var array = JsonDocument.Parse(test.Read("array.json"));
        Expect.Equal("entry", array.RootElement[0].GetProperty("Name").GetString());
    }

    private static async Task OrdinaryGeneratedLifetimes()
    {
        var test = new ConsumerCase();
        test.Configure("""{"update":"verify","naming":{"useFrameworkDisplayNames":true}}""");
        using var relocated = new EnvironmentSetting("IMPRINT_PROJECT_ROOT", test.Root);
        OrdinaryTests.ClassPolicy();
        Expect.Equal(14, OrdinaryTests.ReturnsValue());
        await OrdinaryTests.ReturnsTask();
        Expect.Equal(16, await OrdinaryTests.ReturnsTaskValue());
        await OrdinaryTests.ReturnsValueTask();
        Expect.Equal(18, await OrdinaryTests.ReturnsValueTaskValue());
        OrdinaryTests.Parameterized(1);
        OrdinaryTests.Parameterized(2);
        OrdinaryTests.DisplayName();
        OrdinaryTests.IndirectCapture = static () => Mode.Ready.AssertSnapshot("value");
        OrdinaryTests.Indirect();
        var mismatch = Expect.Throws<SnapshotMismatchException>(OrdinaryTests.MethodPolicy);
        Expect.True(mismatch.Report.Test.Contains("Method name", StringComparison.Ordinal));
        try { await OrdinaryTests.FailingFinally(); throw new Exception("Expected finally exception"); }
        catch (InvalidOperationException error) { Expect.Equal("finally failed", error.Message); }
        var root = Path.Combine(test.Root, "__snapshots__", "Attributed suite");
        Expect.Equal("12\n", File.ReadAllText(Path.Combine(root, "ClassPolicy", "value.json")));
        Expect.Equal("1\n", File.ReadAllText(Path.Combine(root, "Indirect", "value.json")));
        Expect.True(File.Exists(Path.Combine(root, "Readable test", "value.json")));
        Expect.Equal(2, Directory.GetDirectories(root, "Parameterized*").Length);
        Expect.True(!File.Exists(Path.Combine(root, "FailingFinally", "value.json")));
    }

    private static void UnsafeConfigurationAndCapture()
    {
        foreach (var content in new[] { "{\"a\":1,\"a\":2}", "[1,]", "/*comment*/1", "not json" })
        {
            var test = new ConsumerCase();
            Expect.Throws<SnapshotCaptureException>(() => test.Run(() => content.AssertSnapshot("value", new() { StringContent = SnapshotStringContent.Json })));
        }
        var unsafeTest = new ConsumerCase();
        Expect.Throws<SnapshotConfigurationException>(() => unsafeTest.Run(() => { }, unsafeTest.Options with
        {
            RootDirectory = "snapshots",
            ArtifactDirectory = "snapshots/errors"
        }));
        var poisoned = new ConsumerCase();
        Expect.Throws<SnapshotCaptureException>(() => poisoned.Run(() =>
        {
            Expect.Throws<SnapshotConfigurationException>(() => "1".AssertSnapshot("bad", new() { StringContent = SnapshotStringContent.Json, Format = SnapshotFormat.Text }));
            1.UpdateSnapshot("value");
        }));
        Expect.True(!File.Exists(Path.Combine(poisoned.DirectoryPath, "value.json")));
    }
}
