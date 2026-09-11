using System.Runtime.CompilerServices;

namespace TheLithium.Imprint.Specifications;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--concurrency-worker", var directory])
        {
            return await ConcurrentWorker(directory);
        }

        if (args.Length >= 2 && args[0] == "--probe")
        {
            using var writer = new System.Text.Json.Utf8JsonWriter(Console.OpenStandardOutput());
            writer.WriteStartObject();
            writer.WriteStartArray("arguments");
            foreach (var argument in args.Skip(2))
            {
                writer.WriteStringValue(argument);
            }

            writer.WriteEndArray();
            foreach (var variable in new[] { "IMPRINT_UPDATE", "IMPRINT_TEST", "IMPRINT_READ_ONLY", "IMPRINT_ALLOW_CI_UPDATE" })
            {
                writer.WriteString(variable, Environment.GetEnvironmentVariable(variable));
            }

            writer.WriteEndObject();
            writer.Flush();
            return int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
        }
        if (args.Contains("--expect-aot", StringComparer.Ordinal) && RuntimeFeature.IsDynamicCodeSupported)
        {
            Console.Error.WriteLine("Expected a Native AOT executable, but dynamic code is supported.");
            return 1;
        }
        var names = new[] { "IMPRINT_UPDATE", "IMPRINT_TEST", "IMPRINT_READ_ONLY", "IMPRINT_CONFIG",
            "IMPRINT_PROJECT_ROOT", "IMPRINT_ALLOW_CI_UPDATE", "CI" };
        var isolated = names.Select(name => new EnvironmentValue(name, null)).ToArray();
        try
        {
            var tests = Specs.All;
            var failed = 0;
            foreach (var test in tests)
            {
                try
                {
                    await test.Run();
                    Console.WriteLine("PASS " + test.Name);
                }
                catch (Exception error)
                {
                    failed++;
                    Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message);
                    Console.Error.WriteLine(error.StackTrace);
                }
            }
            Console.WriteLine($"{tests.Length - failed}/{tests.Length} specifications passed.");
            Console.WriteLine("Dynamic code supported: " + RuntimeFeature.IsDynamicCodeSupported);
            return failed == 0 ? 0 : 1;
        }
        finally
        {
            foreach (var value in isolated.Reverse())
            {
                value.Dispose();
            }
        }
    }

    private static async Task<int> ConcurrentWorker(string directory)
    {
        using var scope = Snapshots.Begin(new()
        {
            Identity = new SnapshotTestIdentity(
                ProjectDirectory: directory,
                SourceFile: Path.Combine(directory, "Fixture.cs"),
                Suite: "Suite",
                Test: "Example",
                LogicalId: "TheLithium.Imprint.Specifications/Example"),
            Update = SnapshotUpdate.All
        });
        1.AssertSnapshot("value");
        var ready = Path.Combine(directory, "ready-" + Environment.ProcessId);
        await File.WriteAllTextAsync(ready, "ready");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (Directory.GetFiles(directory, "ready-*").Length < 2)
        {
            await Task.Delay(20, timeout.Token);
        }

        try
        {
            scope.Complete();
            return 0;
        }
        catch (SnapshotConflictException) { return 3; }
    }
}
