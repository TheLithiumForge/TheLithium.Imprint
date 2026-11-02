namespace TheLithium.Imprint.Visual;

/// <summary>
/// How the pixels of a screenshot are taken. The default stays <see cref="External"/>: another source gives other
/// pixels, so switching a test to it is an update of its baselines.
/// </summary>
public enum CaptureSource
{
    /// <summary>Use the source from the app options, then the project file.</summary>
    Inherit = 0,

    /// <summary>The test process captures the window through the OS's own tools and calls. The app needs no changes.</summary>
    External = 1
}
