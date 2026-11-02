namespace TheLithium.Imprint.Visual.Tools;

/// <summary>Finds command-line tools on PATH or in fixed locations, without running them.</summary>
internal static class ToolLocator
{
    internal static string? Find(string name, params ReadOnlySpan<string?> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var file in Names(name))
            {
                var full = Path.Combine(directory, file);
                if (File.Exists(full))
                {
                    return full;
                }
            }
        }
        return null;
    }

    private static IEnumerable<string> Names(string name)
    {
        yield return name;
        if (OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            yield return name + ".exe";
        }
    }
}
