using TheLithium.Imprint.Configuration.Models;
using TheLithium.Imprint.Execution.Models;

namespace TheLithium.Imprint.Visual;

/// <summary>Stages an image in the active test, the way AssertSnapshot stages a value.</summary>
internal static class ImageAssertion
{
    internal const string Extension = ".png";

    /// <summary>
    /// Encodes the image and resolves comparison settings in project, platform, app and assertion order.
    /// </summary>
    internal static BinaryCapture Encode(RgbaImage image, ImageStaging request, VisualSettings visual,
        string? platform = null, ImageComparison? appComparison = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if ((long)image.Width * image.Height > visual.MaxPixelsPerImage)
        {
            throw new SnapshotCaptureException(
                $"The image is {image.Width}x{image.Height}, more than the {visual.MaxPixelsPerImage}-pixel limit. Raise visual.maxPixelsPerImage if it's expected.");
        }

        var comparison = ResolvedImageComparison.Defaults.Apply(Patch(visual.Comparison));
        if (platform is not null && visual.Platforms.TryGetValue(platform, out var platformSettings))
        {
            comparison = comparison.Apply(Patch(platformSettings));
        }
        comparison = comparison.Apply(appComparison).Apply(request.Options?.Comparison);
        Validate(comparison, image);

        return new()
        {
            Name = request.Name,
            Expression = request.Expression,
            Extension = Extension,
            Content = image.ToPng(),
            Update = request.Update ?? request.Options?.Update ?? SnapshotUpdate.Inherit,
            Comparer = new ImageSnapshotComparer(comparison, request.Options?.Comparer ?? DefaultImageComparer.Instance, visual.MaxPixelsPerImage)
        };
    }

    private static ImageComparison Patch(ImageComparisonSettings settings) => new()
    {
        ChannelTolerance = settings.ChannelTolerance,
        MaxDifferingPercent = settings.MaxDifferingPercent,
        IgnoreAntialiasing = settings.IgnoreAntialiasing
    };

    /// <summary>Checks regions against this image now, so a bad region fails at the assertion and not at the end.</summary>
    private static void Validate(ResolvedImageComparison comparison, RgbaImage image)
    {
        long covered = 0;
        foreach (var region in comparison.IgnoreRegions)
        {
            if (!region.FitsIn(image.Width, image.Height))
            {
                throw new SnapshotCaptureException($"Ignore region {region} doesn't fit inside the {image.Width}x{image.Height} image.");
            }
            covered += (long)region.Width * region.Height;
        }
        if (covered >= (long)image.Width * image.Height && Covers(comparison.IgnoreRegions, image))
        {
            throw new SnapshotCaptureException("The ignore regions cover the whole image, so nothing would be compared.");
        }
    }

    private static bool Covers(IReadOnlyList<PixelRect> regions, RgbaImage image)
    {
        var ignored = new bool[image.Width * image.Height];
        foreach (var region in regions)
        {
            for (var y = region.Y; y < region.Y + region.Height; y++)
            {
                ignored.AsSpan(y * image.Width + region.X, region.Width).Fill(true);
            }
        }
        return !ignored.Contains(false);
    }
}

/// <summary>Who called and what they asked for, kept together so staging takes one request.</summary>
internal sealed record ImageStaging(string? Name, string? Expression, ImageSnapshotOptions? Options, SnapshotUpdate? Update);
