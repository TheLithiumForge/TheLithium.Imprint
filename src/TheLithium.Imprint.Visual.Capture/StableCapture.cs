using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using TheLithium.Imprint.Configuration.Models;
using TheLithium.Imprint.Storage.Models;

namespace TheLithium.Imprint.Visual;

/// <summary>
/// Captures until enough identical frames in a row have been held long enough. Frames compare exactly, size
/// included, so an animation, a blinking caret or late layout keeps the wait going until the timeout.
/// </summary>
internal static class StableCapture
{
    private static readonly TimeSpan LongestTimer = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    internal static async Task<RgbaImage> CaptureAsync(Func<CancellationToken, Task<RgbaImage>> capture, ResolvedStability stability,
        PixelRect? region, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timer = ArmDeadlineAsync(deadline, stability.Timeout);
        RgbaImage? previous = null;
        RgbaImage? last = null;
        var matching = 0;
        var stableSince = TimeSpan.Zero;
        try
        {
            while (clock.Elapsed < stability.Timeout)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var started = clock.Elapsed;
                var frame = await capture(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
                previous = last;
                last = region is { } area ? frame.Crop(area) : frame;
                if (previous is not null && last.PixelsEqual(previous))
                {
                    matching++;
                }
                else
                {
                    matching = 1;
                    stableSince = clock.Elapsed;
                }

                deadline.Token.ThrowIfCancellationRequested();
                var now = clock.Elapsed;
                if (now >= stability.Timeout)
                {
                    break;
                }
                if (matching >= stability.MatchingFrames && now - stableSince >= stability.MinimumDuration)
                {
                    return last;
                }

                var wait = stability.Interval - (now - started);
                if (wait > TimeSpan.Zero)
                {
                    await DelayAsync(wait, deadline.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The deadline reports the sampled frames below. Caller cancellation flows out unchanged.
        }
        finally
        {
            if (!timer.IsCompleted)
            {
                await deadline.CancelAsync().ConfigureAwait(false);
                await timer.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        throw TimedOut(previous, last, stability);
    }

    // A .NET timer can cover about 49 days. Code-level capture options accept any positive timeout.
    private static async Task ArmDeadlineAsync(CancellationTokenSource deadline, TimeSpan timeout)
    {
        if (timeout > LongestTimer)
        {
            await DelayAsync(timeout - LongestTimer, deadline.Token).ConfigureAwait(false);
            timeout = LongestTimer;
        }
        deadline.CancelAfter(timeout);
    }

    private static async Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        while (duration > LongestTimer)
        {
            await Task.Delay(LongestTimer, cancellationToken).ConfigureAwait(false);
            duration -= LongestTimer;
        }
        await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
    }

    private static SnapshotCaptureException TimedOut(RgbaImage? previous, RgbaImage? last, ResolvedStability stability)
    {
        var detail = "";
        if (last is not null)
        {
            if (previous is not null)
            {
                detail = " Between the last two frames: " + Difference(previous, last);
                Diagnose("capture-timeout.previous.png", previous);
            }
            Diagnose("capture-timeout.last.png", last);
        }
        return new(string.Create(CultureInfo.InvariantCulture,
            $"The capture didn't return a stable frame within {stability.Timeout.TotalSeconds:0.###} seconds.{detail}"));
    }

    private static string Difference(RgbaImage previous, RgbaImage last)
    {
        if (previous.Width != last.Width || previous.Height != last.Height)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"the size changed from {previous.Width}x{previous.Height} to {last.Width}x{last.Height}.");
        }

        var first = MemoryMarshal.Cast<Rgba32, uint>(previous.Pixels);
        var second = MemoryMarshal.Cast<Rgba32, uint>(last.Pixels);
        var changed = 0;
        for (var i = 0; i < first.Length; i++)
        {
            if (first[i] != second[i])
            {
                changed++;
            }
        }
        return string.Create(CultureInfo.InvariantCulture, $"{changed:N0} of {first.Length:N0} pixels differ.");
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
