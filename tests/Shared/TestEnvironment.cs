using System.Runtime.CompilerServices;

namespace TheLithium.Imprint.Testing;

internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var temporary = Path.Combine(AppContext.BaseDirectory, "TestResults", "temp");
        Directory.CreateDirectory(temporary);
        Environment.SetEnvironmentVariable("TMP", temporary);
        Environment.SetEnvironmentVariable("TEMP", temporary);
        Environment.SetEnvironmentVariable("TMPDIR", temporary);
    }
}
