using System.Runtime.CompilerServices;
using TheLithium.Imprint.Generation;
using TheLithium.Imprint.Visual;

namespace TheLithium.Imprint;

/// <summary>Screenshot assertions. The comparison resolves from the project, then the platform, then the app, then the options.</summary>
public static class ScreenshotExtensions
{
    /// <summary>Compares a screenshot with its PNG baseline after the test body succeeds.</summary>
    /// <param name="screenshot">The capture.</param>
    /// <param name="name">Optional file name without an extension. Defaults to the variable or member name, then capture order.</param>
    /// <param name="options">Optional update policy, comparison or comparer for this screenshot.</param>
    /// <param name="expression">Compiler-supplied expression for name inference. Normally omit this argument.</param>
    [SnapshotCaptureMethod]
    public static void AssertImage(this Screenshot screenshot, string? name = null, ImageSnapshotOptions? options = null,
        [CallerArgumentExpression(nameof(screenshot))] string? expression = null)
        => Stage(screenshot, new(name, expression, options, Update: null));

    /// <summary>Stages this screenshot for creation or replacement after the test body succeeds.</summary>
    [SnapshotCaptureMethod]
    public static void UpdateImage(this Screenshot screenshot, string? name = null, ImageSnapshotOptions? options = null,
        [CallerArgumentExpression(nameof(screenshot))] string? expression = null)
        => Stage(screenshot, new(name, expression, options, SnapshotUpdate.All));

    private static void Stage(Screenshot screenshot, ImageStaging request)
    {
        ArgumentNullException.ThrowIfNull(screenshot);
        ImageAssertion.Stage(screenshot.Image, request, ImageAssertion.Platform(PlatformNames.Of(screenshot.Platform)), screenshot.AppComparison);
    }
}
