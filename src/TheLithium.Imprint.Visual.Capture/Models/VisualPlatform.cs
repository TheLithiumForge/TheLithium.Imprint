namespace TheLithium.Imprint.Visual;

/// <summary>
/// Where an app runs. Each value is its own baseline, so use it as a test parameter. X11 and Wayland are separate
/// because the same app draws differently on each.
/// </summary>
public enum VisualPlatform
{
    Windows = 1,
    LinuxX11 = 2,
    LinuxWayland = 3,
    MacOS = 4,
    Android = 5,
    IOS = 6
}
