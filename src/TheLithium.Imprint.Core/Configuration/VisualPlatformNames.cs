namespace TheLithium.Imprint.Configuration;

/// <summary>The platform keys of <c>visual.platforms</c>. The capture package maps its platform enum to these names.</summary>
internal static class VisualPlatformNames
{
    internal const string Windows = "windows";
    internal const string LinuxX11 = "linux-x11";
    internal const string LinuxWayland = "linux-wayland";
    internal const string MacOS = "macos";
    internal const string Android = "android";
    internal const string IOS = "ios";

    internal static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Windows, LinuxX11, LinuxWayland, MacOS, Android, IOS
    };
}
