using TheLithium.Imprint.Configuration;
using TheLithium.Imprint.Visual.Platforms;

namespace TheLithium.Imprint.Visual;

/// <summary>
/// Decides whether this run drives a platform. A platform listed in IMPRINT_VISUAL_PLATFORMS never skips, and CI can't
/// skip a platform whose prerequisites are missing, so a CI job can't pass without capturing anything.
/// </summary>
internal static class PlatformSelection
{
    internal const string Variable = "IMPRINT_VISUAL_PLATFORMS";

    internal static async Task<VisualCheck> CheckAsync(AppTarget target, IPlatformDriver driver, CancellationToken cancellationToken)
    {
        var name = PlatformNames.Of(target.Platform);
        var listed = Listed();
        if (listed is not null && !listed.Contains(name))
        {
            return new(VisualCheckStatus.NotRequested, $"{Variable} doesn't list {name}.");
        }

        var problem = await driver.FindProblemAsync(target, cancellationToken).ConfigureAwait(false);
        if (problem is null)
        {
            return new(VisualCheckStatus.Ready);
        }
        if (listed is not null)
        {
            return new(VisualCheckStatus.Missing, problem);
        }
        if (Settings.EnvironmentFlag("CI"))
        {
            return new(VisualCheckStatus.Missing, $"{problem} In CI, set {Variable} to the platforms this job drives, e.g. windows,android.");
        }
        return new(VisualCheckStatus.NotRequested, problem);
    }

    private static HashSet<string>? Listed()
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var names = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(name => name.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!VisualPlatformNames.All.Contains(name))
            {
                var known = string.Join(", ", VisualPlatformNames.All.Order(StringComparer.Ordinal));
                throw new SnapshotConfigurationException($"{Variable} lists {name}, which isn't a platform. Use {known}.");
            }
        }
        return names;
    }
}
