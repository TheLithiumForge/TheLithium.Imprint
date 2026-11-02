using TheLithium.Imprint.Visual.Platforms.Android;
using TheLithium.Imprint.Visual.Platforms.Ios;
using TheLithium.Imprint.Visual.Platforms.Linux;
using TheLithium.Imprint.Visual.Platforms.MacOS;
using TheLithium.Imprint.Visual.Platforms.Windows;
using TheLithium.Imprint.Visual.Sources;

namespace TheLithium.Imprint.Visual.Platforms;

/// <summary>Starts apps on one platform and captures them from outside.</summary>
internal interface IPlatformDriver
{
    /// <summary>What's missing to drive this target on this machine, or null when nothing is.</summary>
    Task<string?> FindProblemAsync(AppTarget target, CancellationToken cancellationToken);

    Task<IPlatformSession> StartAsync(LaunchPlan plan, SessionSettings settings, CancellationToken cancellationToken);

    /// <summary>Attaches to a running process, for desktop platforms that can capture one.</summary>
    Task<IPlatformSession> AttachAsync(int processId, SessionSettings settings, CancellationToken cancellationToken);
}

/// <summary>One running app. Captures are in physical pixels and stopping it releases everything it holds.</summary>
internal interface IPlatformSession : IAsyncDisposable
{
    Task<RgbaImage> CaptureAsync(CancellationToken cancellationToken);
}

internal sealed record SessionSettings(WindowSelector? Window, TimeSpan StartTimeout);

internal static class PlatformDrivers
{
    internal static IPlatformDriver For(VisualPlatform platform) => platform switch
    {
        VisualPlatform.Windows => new WindowsDriver(),
        VisualPlatform.LinuxX11 => new X11Driver(),
        VisualPlatform.LinuxWayland => new WaylandDriver(),
        VisualPlatform.MacOS => new MacDriver(),
        VisualPlatform.Android => new AndroidDriver(),
        VisualPlatform.IOS => new IosSimulatorDriver(),
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
    };

    /// <summary>Waits until <paramref name="find"/> returns a value, or fails with what it last reported.</summary>
    internal static async Task<T> WaitForAsync<T>(Func<CancellationToken, Task<(T? Value, string Problem)>> find, TimeSpan timeout, CancellationToken cancellationToken)
        where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var (value, problem) = await find(cancellationToken).ConfigureAwait(false);
            if (value is not null)
            {
                return value;
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new SnapshotCaptureException(problem);
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The message for a window search that found zero windows, or more than one.</summary>
    internal static string WindowProblem(IReadOnlyCollection<string> candidates, WindowSelector? selector, TimeSpan timeout)
    {
        if (candidates.Count == 0)
        {
            return $"The app showed no window within {timeout.TotalSeconds:0} seconds.";
        }
        var list = string.Join(Environment.NewLine, candidates.Select(candidate => "  " + candidate));
        var hint = selector is null ? " Set VisualAppOptions.Window to pick one." : " Make VisualAppOptions.Window match exactly one.";
        return $"The app showed {candidates.Count} windows, so the capture can't tell which one to use.{hint}{Environment.NewLine}{list}";
    }
}
