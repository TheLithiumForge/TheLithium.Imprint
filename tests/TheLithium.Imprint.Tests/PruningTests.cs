using TheLithium.Imprint.Configuration;
using TheLithium.Imprint.Generation.Models;
using TheLithium.Imprint.Specifications;
using TheLithium.Imprint.Storage;
using Xunit;

namespace TheLithium.Imprint.Tests;

/// <summary>
/// Drives a private pruner with descriptors for a temporary project, the way generated
/// registrations and scope openings drive the process-wide one.
/// </summary>
public sealed class PruningTests : IDisposable
{
    private readonly Fixture _fixture = new();
    private readonly EnvironmentValue[] _environment = new[] { "CI", "IMPRINT_UPDATE", "IMPRINT_PROJECT_ROOT", "IMPRINT_PRUNE" }
        .Select(name => new EnvironmentValue(name, null)).ToArray();
    private readonly List<SnapshotDescriptor> _tests = [];
    private readonly StringWriter _log = new();
    private SnapshotPruner? _pruner;

    private SnapshotPruner Pruner => _pruner ??= new(() => _tests.ToArray(), () => 1, _log);

    public void Dispose()
    {
        foreach (var value in _environment.Reverse())
        {
            value.Dispose();
        }

        _fixture.Dispose();
    }

    [Fact]
    public void ASuiteIsPrunedOnlyAfterEveryTestInItRan()
    {
        var first = Test("Orders", "Creates");
        var second = Test("Orders", "Cancels");
        Plant("Orders", "Creates");
        Plant("Orders", "Cancels");
        var renamed = Plant("Orders", "Renamed");

        Run(first);
        Assert.True(Directory.Exists(renamed));

        Run(second);
        Assert.False(Directory.Exists(renamed));
        Assert.True(Directory.Exists(Folder("Orders", "Creates")));
        Assert.Contains("removed 1 stale snapshot folder", _log.ToString());
    }

    [Fact]
    public void AFilteredRunDoesNotTouchSuitesItDidNotRun()
    {
        var ran = Test("Orders", "Creates");
        Test("Invoices", "Sends");
        var staleInRun = Plant("Orders", "Old");
        var staleElsewhere = Plant("Invoices", "Old");

        Run(ran);

        Assert.False(Directory.Exists(staleInRun));
        Assert.True(Directory.Exists(staleElsewhere));
    }

    [Fact]
    public void TheProjectLayerRemovesDeletedSuitesAndMovedFiles()
    {
        var first = Test("Orders", "Creates", "Api/OrderTests.cs");
        var second = Test("Invoices", "Sends");
        var current = Plant("Api", "Orders", "Creates");
        var deletedSuite = Plant("Gone", "Test");
        var movedFile = Plant("OldFolder", "Orders", "Creates");

        Run(first);
        Assert.True(Directory.Exists(deletedSuite));
        Assert.True(Directory.Exists(movedFile));

        Run(second);
        Assert.False(Directory.Exists(deletedSuite));
        Assert.False(Directory.Exists(movedFile));
        Assert.False(Directory.Exists(Folder("OldFolder")));
        Assert.True(Directory.Exists(current));
    }

    [Fact]
    public void RowsArePrunedOnlyWhenEveryRowRan()
    {
        var rows = Test("Parser", "Parses", rows: 2);
        Test("Parser", "Other");
        var first = Plant("Parser", "Parses [value=1]");
        var second = Plant("Parser", "Parses [value=2]");
        var old = Plant("Parser", "Parses [value=3]");

        Run(rows, "value=1");
        Assert.True(Directory.Exists(old));

        Run(rows, "value=2");
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
    }

    [Fact]
    public void RowsWithAnUnknownCountAreKept()
    {
        var rows = Test("Parser", "Parses", parameterized: true);
        var old = Plant("Parser", "Parses [value=3]");
        var deleted = Plant("Parser", "Deleted");

        Run(rows, "value=1");

        Assert.True(Directory.Exists(old));
        Assert.False(Directory.Exists(deleted));
    }

    [Fact]
    public void AnOptionalTestNeitherBlocksItsSuiteNorLosesItsFolder()
    {
        var test = Test("Orders", "Creates");
        Test("Orders", "Skipped", optional: true);
        var skipped = Plant("Orders", "Skipped");
        var stale = Plant("Orders", "Stale");

        Run(test);

        Assert.True(Directory.Exists(skipped));
        Assert.False(Directory.Exists(stale));
    }

    [Fact]
    public void AnExplicitLifetimeMustRunAndWriteElsewhereFirst()
    {
        var test = Test("Orders", "Creates");
        var custom = Test("Runner", "Custom", explicitLifetime: true);
        var stale = Plant("Orders", "Stale");

        Run(test);
        Assert.True(Directory.Exists(stale));

        using var elsewhere = new Fixture();
        Run(custom, project: elsewhere.Root);
        Assert.False(Directory.Exists(stale));
    }

    [Fact]
    public void AnExplicitLifetimeUnderTheRootStopsPruning()
    {
        var test = Test("Orders", "Creates");
        var custom = Test("Runner", "Custom", explicitLifetime: true);
        var stale = Plant("Orders", "Stale");

        Run(custom);
        Run(test);

        Assert.True(Directory.Exists(stale));
    }

    [Fact]
    public void AFolderWithOtherFilesIsKept()
    {
        var test = Test("Orders", "Creates");
        var notes = Plant("Orders", "Notes");
        File.WriteAllText(Path.Combine(notes, "README.md"), "keep");

        Run(test);

        Assert.True(Directory.Exists(notes));
        Assert.Contains("\"kept\"", Report());
    }

    [Theory]
    [InlineData("IMPRINT_PRUNE", "report", "prune is set to report")]
    [InlineData("IMPRINT_UPDATE", "verify", "this run only verifies")]
    [InlineData("CI", "true", "this run only verifies")]
    public void ReportModesFindWithoutRemoving(string variable, string value, string reason)
    {
        using var mode = new EnvironmentValue(variable, value);
        var test = Test("Orders", "Creates");
        var stale = Plant("Orders", "Stale");

        Run(test);

        Assert.True(Directory.Exists(stale));
        Assert.Contains(reason, _log.ToString());
        Assert.Contains("\"found\"", Report());
    }

    [Fact]
    public void ConditionalCompilationOnlyReports()
    {
        var test = Test("Orders", "Creates", conditional: true);
        var stale = Plant("Orders", "Stale");

        Run(test);

        Assert.True(Directory.Exists(stale));
        Assert.Contains("#if", _log.ToString());
    }

    [Fact]
    public void PruneOffDoesNothing()
    {
        _fixture.Configure("""{"prune":"off"}""");
        var test = Test("Orders", "Creates");
        var stale = Plant("Orders", "Stale");

        Run(test);

        Assert.True(Directory.Exists(stale));
        Assert.Empty(_log.ToString());
    }

    [Fact]
    public void AConfiguredRootKeepsFoldersOutsideKnownSuites()
    {
        _fixture.Configure("""{"files":{"snapshotRootPath":"shared"}}""");
        var test = Test("Orders", "Creates");
        var otherProject = PlantUnder(Path.Combine(_fixture.Root, "shared"), "Billing", "Charges");
        var stale = PlantUnder(Path.Combine(_fixture.Root, "shared"), "Orders", "Stale");

        Run(test);

        Assert.True(Directory.Exists(otherProject));
        Assert.False(Directory.Exists(stale));
    }

    [Fact]
    public void AnInvalidPruneVariableFailsTheTest()
    {
        using var invalid = new EnvironmentValue("IMPRINT_PRUNE", "sometimes");
        var error = Assert.Throws<SnapshotConfigurationException>(() => _fixture.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All));
        Assert.Contains("IMPRINT_PRUNE", error.Message);
    }

    private SnapshotDescriptor Test(string suite, string test, string source = "Tests.cs", int? rows = null,
        bool parameterized = false, bool optional = false, bool explicitLifetime = false, bool conditional = false)
    {
        var descriptor = new SnapshotDescriptor(_fixture.Root, "Project", Path.Combine(_fixture.Root, source), suite, test,
            $"{suite}.{test}", SnapshotUpdate.Inherit, parameterized || rows is not null)
        {
            CaseCount = rows,
            Optional = optional,
            Explicit = explicitLifetime,
            Conditional = conditional
        };
        _tests.Add(descriptor);
        return descriptor;
    }

    /// <summary>Opens a scope's worth of facts: the folder it resolves to and the descriptor it came from.</summary>
    private void Run(SnapshotDescriptor descriptor, string? caseKey = null, string? project = null)
    {
        var identity = new SnapshotTestIdentity(project ?? _fixture.Root, project is null ? descriptor.SourceFile : Path.Combine(project, "Tests.cs"),
            descriptor.Suite, descriptor.Test, caseKey);
        var settings = Settings.Resolve(new SnapshotTestOptions { Identity = identity }, "", 0) with { Descriptor = descriptor };
        Pruner.Visited(settings);
    }

    private string Folder(params string[] parts) => Path.Combine([_fixture.Root, "__snapshots__", .. parts]);

    private string Plant(params string[] parts) => PlantAt(Folder(parts));

    private static string PlantUnder(string root, params string[] parts) => PlantAt(Path.Combine([root, .. parts]));

    private static string PlantAt(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "value.json"), "1\n");
        return directory;
    }

    private string Report()
    {
        var reports = Path.Combine(_fixture.Root, "artifacts", "imprint", "prune");
        return File.ReadAllText(Assert.Single(Directory.GetFiles(reports, "*.json")));
    }
}
