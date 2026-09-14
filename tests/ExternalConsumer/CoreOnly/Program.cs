using System.Runtime.CompilerServices;
using TheLithium.Imprint;

if (args.Length != 2 || args[0] is not ("--managed" or "--aot"))
    throw new ArgumentException("Usage: CoreOnly --managed|--aot <fresh-run-directory>");
if (RuntimeFeature.IsDynamicCodeSupported != (args[0] == "--managed")) throw new Exception("Wrong execution mode");
var root = Path.GetFullPath(args[1]);
Directory.CreateDirectory(root);
var options = new SnapshotTestOptions { Identity = new(root, "Test.cs", "CoreOnly", "Manual") };
var directory = string.Empty;
var report = Snapshots.Run(() =>
{
    directory = Snapshots.Current.BaselineDirectory;
    1.AssertSnapshot("primitive");
    new PrivateDto(7).AssertSnapshot(static (writer, dto, _) => writer.WriteNumberValue(dto.Id), "dto");
}, options);
if (!report.Success || report.Entries.Count != 2) throw new Exception("Core-only explicit runner failed");
await Snapshots.RunAsync(async () =>
{
    await Task.Yield();
    1.AssertSnapshot("primitive");
    new PrivateDto(7).AssertSnapshot(static (writer, dto, _) => writer.WriteNumberValue(dto.Id), "dto");
}, options with
{
    Update = SnapshotUpdate.Verify
});
if (File.ReadAllText(Path.Combine(directory, "dto.json")) != "7\n") throw new Exception("Core custom writer failed");
try
{
    Snapshots.Run(() => new PrivateDto(7).AssertSnapshot("unsupported"), options);
    throw new Exception("Missing writer was accepted");
}
catch (SnapshotCaptureException) { }
try
{
    Snapshots.Begin();
    throw new Exception("Core-only implicitly resolved an identity");
}
catch (SnapshotConfigurationException) { }
Console.WriteLine($"PASS Core-only explicit sync/async, writer and missing-integration checks; dynamic code: {RuntimeFeature.IsDynamicCodeSupported}");

internal sealed record PrivateDto(int Id);
