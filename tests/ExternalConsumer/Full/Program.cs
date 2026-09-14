using System.Runtime.CompilerServices;
using ExternalConsumer;

if (args.Length != 2 || args[0] is not ("--managed" or "--aot"))
    throw new ArgumentException("Usage: Full --managed|--aot <fresh-run-directory>");
Expect.Equal(args[0] == "--managed", RuntimeFeature.IsDynamicCodeSupported);
ConsumerCase.RunRoot = Path.GetFullPath(args[1]);
Directory.CreateDirectory(ConsumerCase.RunRoot);
using var update = new EnvironmentSetting("IMPRINT_UPDATE", null);
using var project = new EnvironmentSetting("IMPRINT_PROJECT_ROOT", null);
using var ci = new EnvironmentSetting("CI", null);
var failed = 0;
foreach (var scenario in Scenarios.All)
{
    try
    {
        await scenario.Run();
        Console.WriteLine($"PASS {scenario.Name}");
    }
    catch (Exception error)
    {
        failed++;
        Console.WriteLine($"FAIL {scenario.Name}: {error}");
    }
}
Console.WriteLine($"{Scenarios.All.Length - failed}/{Scenarios.All.Length} external scenarios passed; dynamic code: {RuntimeFeature.IsDynamicCodeSupported}");
return failed == 0 ? 0 : 1;
