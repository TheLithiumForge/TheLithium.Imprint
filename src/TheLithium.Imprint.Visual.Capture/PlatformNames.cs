using TheLithium.Imprint.Configuration;

namespace TheLithium.Imprint.Visual;

/// <summary>Maps each platform to its key in <c>visual.platforms</c> and <c>IMPRINT_VISUAL_PLATFORMS</c>.</summary>
internal static class PlatformNames
{
    internal static string Of(VisualPlatform platform) => platform switch
    {
        VisualPlatform.Windows => VisualPlatformNames.Windows,
        VisualPlatform.LinuxX11 => VisualPlatformNames.LinuxX11,
        VisualPlatform.LinuxWayland => VisualPlatformNames.LinuxWayland,
        VisualPlatform.MacOS => VisualPlatformNames.MacOS,
        VisualPlatform.Android => VisualPlatformNames.Android,
        VisualPlatform.IOS => VisualPlatformNames.IOS,
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
    };
}
