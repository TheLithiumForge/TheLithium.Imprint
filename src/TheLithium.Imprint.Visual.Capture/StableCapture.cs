using System.Diagnostics;
using System.Globalization;
using TheLithium.Imprint.Configuration.Models;
using TheLithium.Imprint.Storage.Models;

namespace TheLithium.Imprint.Visual;

/// <summary>
/// Captures until enough identical frames in a row have been held long enough. Frames compare exactly, size
/// included, so an animation, a blinking caret or late layout keeps the wait going until the timeout.
/// </summary>
internal static class StableCapture
{
    internal static async Task<RgbaImage> CaptureAsync(Func<CancellationToken, Task<RgbaImage>> capture, ResolvedStability stability, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        RgbaImage? previous = null;
        RgbaImage? last = null;
        var matching = 0;
        var stableSince = TimeSpan.Zero;
        while (true)
        {
            var started = clock.Elapsed;
            var frame = await capture(cancellationToken).ConfigureAwait(false);
            if (last is not null && frame.PixelsEqual(last))
            {
                matching++;
            }
            else
            {
                previous = last;
                last = frame;
                matching = 1;
                stableSince = started;
            }

            if (matching >= stability.MatchingFrames && clock.Elapsed - stableSince >= stability.MinimumDuration)
            {
                return last;
            }
            if (clock.Elapsed >= stability.Timeout)
            {
                throw TimedOut(previous, last, stability);
            }

            var wait = stability.Interval - (clock.Elapsed - started);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static SnapshotCaptureException TimedOut(RgbaImage? previous, RgbaImage last, ResolvedStability stability)
    {
        var detail = "";
        if (previous is not null)
        {
            var changed = DefaultImageComparer.Instance.Compare(previous, last, ResolvedImageComparison.Defaults);
            detail = changed.Difference is null ? "" : $" Between the last two frames: {changed.Difference}";
            Diagnose("capture-timeout.previous.png", previous);
        }
        Diagnose("capture-timeout.last.png", last);
        return new(string.Create(CultureInfo.InvariantCulture,
            $"The window kept changing for {stability.Timeout.TotalSeconds:0.#} seconds, so no frame was stable enough to compare.{detail} Stop animations for the test, or mask the changing area."));
    }

    /// <summary>Keeps a frame with the test's failure artifacts when a test is running.</summary>
    private static void Diagnose(string fileName, RgbaImage frame)
    {
        if (Snapshots.Active is { } scope)
        {
            scope.AddDiagnostic(new ArtifactFile(fileName, frame.ToPng()));
        }
    }
}

/// <summary>Stability with every field resolved.</summary>
internal sealed record ResolvedStability(int MatchingFrames, TimeSpan Interval, TimeSpan MinimumDuration, TimeSpan Timeout)
{
    internal static ResolvedStability Defaults { get; } = new(3, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(10));

    internal ResolvedStability Apply(CaptureStabilitySettings settings) => Apply(new CaptureStability
    {
        MatchingFrames = settings.MatchingFrames,
        Interval = settings.Interval,
        MinimumDuration = settings.MinimumDuration,
        Timeout = settings.Timeout
    });

    internal ResolvedStability Apply(CaptureStability? patch)
    {
        if (patch is null)
        {
            return this;
        }

        var result = new ResolvedStability(patch.MatchingFrames ?? MatchingFrames, patch.Interval ?? Interval,
            patch.MinimumDuration ?? MinimumDuration, patch.Timeout ?? Timeout);
        if (result.MatchingFrames < 1 || result.Interval < TimeSpan.Zero || result.MinimumDuration < TimeSpan.Zero || result.Timeout <= TimeSpan.Zero)
        {
            throw new SnapshotConfigurationException("CaptureStability needs MatchingFrames of at least 1, no negative times and a positive Timeout.");
        }
        return result;
    }
}
