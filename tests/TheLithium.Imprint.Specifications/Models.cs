using TheLithium.Imprint;

[assembly: SnapshotInclude<TheLithium.Imprint.Specifications.GenericOnly>]

namespace TheLithium.Imprint.Specifications;

internal sealed record Result(int ExitCode, string Message, List<string> Files);
internal sealed class MutableState
{
    public int Count
    {
        get; set;
    }
}
internal sealed class Link
{
    public string Name { get; set; } = "node";
    public Link? Next
    {
        get; set;
    }
}
internal sealed record GenericOnly(int Id);
internal sealed class NotRooted
{
    public int Value { get; set; } = 1;
}
internal enum Mode
{
    None = 0, AliasForNone = 0, Fast = 1
}

[SnapshotSettings(Update = SnapshotUpdate.All, Name = "Generated suite")]
internal static class GeneratedFixtures
{
    internal static void ClassDefault() => Snapshots.Run(() => 1.AssertSnapshot("value"));

    [SnapshotSettings(Update = SnapshotUpdate.Verify)]
    internal static void MethodVerify() => Snapshots.Run(() => 1.AssertSnapshot("value"));

    [SnapshotSettings(Name = "Readable method")]
    internal static void RenamedMethod() => Snapshots.Run(() => new { Value = 1 }.AssertSnapshot("value"));

    [Xunit.Fact(DisplayName = "Readable generated display")]
    internal static void PreferredDisplayName() => 1.AssertSnapshot("value");

    [SnapshotSettings]
    internal static void ConfigureOrdinaryLifetime()
    {
        Snapshots.Current.Comparison = new() { IgnoreStringCase = true };
        Snapshots.Current.Representation = new() { Enums = SnapshotEnumRepresentation.Number };
        Snapshots.Current.StringContent = SnapshotStringContent.Value;
        Check.True(Snapshots.Current.Comparison.IgnoreStringCase == true);
    }

    internal static void Parameterized(string name) => Snapshots.Run(() => name.AssertSnapshot("value"));

    internal static void ParameterizedWithCase(string name) => Snapshots.Run(
        () => name.AssertSnapshot("value"), new()
        {
            Case = name
        });
}
