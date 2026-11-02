using System.Runtime.CompilerServices;
using TheLithium.Imprint.Generation;
using TheLithium.Imprint.Visual;

namespace TheLithium.Imprint;

/// <summary>Image assertions. They stage a PNG in the test's snapshot folder and follow the same rules as AssertSnapshot.</summary>
public static class ImageSnapshotExtensions
{
    /// <summary>Captures an image now and compares it with its PNG baseline after the test body succeeds.</summary>
    /// <param name="image">The image. It's encoded right away, so later work can't change the capture.</param>
    /// <param name="name">Optional file name without an extension. Defaults to the variable or member name, then capture order.</param>
    /// <param name="options">Optional update policy, comparison or comparer for this image.</param>
    /// <param name="expression">Compiler-supplied expression for name inference. Normally omit this argument.</param>
    [SnapshotCaptureMethod]
    public static void AssertImage(this RgbaImage image, string? name = null, ImageSnapshotOptions? options = null,
        [CallerArgumentExpression(nameof(image))] string? expression = null)
        => ImageAssertion.Stage(image, new(name, expression, options, Update: null));

    /// <summary>Stages this image for creation or replacement after the test body succeeds.</summary>
    /// <remarks>A run-wide verify, or CI, still prevents writes. Change back to AssertImage after reviewing the update.</remarks>
    [SnapshotCaptureMethod]
    public static void UpdateImage(this RgbaImage image, string? name = null, ImageSnapshotOptions? options = null,
        [CallerArgumentExpression(nameof(image))] string? expression = null)
        => ImageAssertion.Stage(image, new(name, expression, options, SnapshotUpdate.All));
}
