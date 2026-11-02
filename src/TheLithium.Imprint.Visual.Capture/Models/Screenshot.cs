using TheLithium.Imprint.Generation;

namespace TheLithium.Imprint.Visual;

/// <summary>A captured window, with where it came from. Assert it with <c>AssertImage</c>.</summary>
[SnapshotCaptureType("AssertImage")]
public sealed class Screenshot
{
    internal Screenshot(RgbaImage image, VisualPlatform platform, CaptureSource source, ImageComparison? appComparison)
    {
        Image = image;
        Platform = platform;
        Source = source;
        AppComparison = appComparison;
    }

    public RgbaImage Image
    {
        get;
    }

    public VisualPlatform Platform
    {
        get;
    }

    public CaptureSource Source
    {
        get;
    }

    /// <summary>The app's comparison patch, applied between the platform layer and the assertion's options.</summary>
    internal ImageComparison? AppComparison
    {
        get;
    }
}
