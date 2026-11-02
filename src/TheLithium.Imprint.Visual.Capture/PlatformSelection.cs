using TheLithium.Imprint.Configuration;
using TheLithium.Imprint.Visual.Platforms;

namespace TheLithium.Imprint.Visual;

/// <summary>
/// Decides whether this run drives a platform. IMPRINT_VISUAL_PLATFORMS lists the platforms to drive, or <c>none</c>,
/// and a listed platform never skips. Without the variable, a platform runs when its prerequisites are there. In CI the
/// machine's own desktop platform fails instead of skipping, so a runner that lacks a tool can't pass silently.
/// </summary>
internal static class PlatformSelection
{
    internal const string Variable = "IMPRINT_VISUAL_PLATFORMS";
    private const string None = "none";

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
        if (Settings.EnvironmentFlag("CI") && IsHost(target.Platform))
        {
            return new(VisualCheckStatus.Missing, $"{problem} To run without {name} in CI, set {Variable} to the platforms this job drives, or to {None}.");
        }
        return new(VisualCheckStatus.NotRequested, problem);
    }

    /// <summary>The desktop platform of this machine's own OS.</summary>
    internal static bool IsHost(VisualPlatform platform) => platform switch
    {
        VisualPlatform.Windows => OperatingSystem.IsWindows(),
        VisualPlatform.LinuxX11 or VisualPlatform.LinuxWayland => OperatingSystem.IsLinux(),
        VisualPlatform.MacOS => OperatingSystem.IsMacOS(),
        VisualPlatform.Android or VisualPlatform.IOS => false,
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
    };

    private static HashSet<string>? Listed()
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var names = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(name => name.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        if (names.SetEquals([None]))
        {
            return [];
        }
        foreach (var name in names)
        {
            if (!VisualPlatformNames.All.Contains(name))
            {
                var known = string.Join(", ", VisualPlatformNames.All.Order(StringComparer.Ordinal));
                throw new SnapshotConfigurationException($"{Variable} lists {name}, which isn't a platform. Use {known}, or {None}.");
            }
        }
        return names;
    }
}
