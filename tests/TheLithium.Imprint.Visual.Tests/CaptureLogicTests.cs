using TheLithium.Imprint.Specifications;
using TheLithium.Imprint.Visual.Platforms;
using TheLithium.Imprint.Visual.Platforms.Android;
using TheLithium.Imprint.Visual.Platforms.Windows;
using TheLithium.Imprint.Visual.Sources;
using TheLithium.Imprint.Visual.Tools;
using Xunit;

// Tests here set environment variables and drive the desktop, so they don't run alongside each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TheLithium.Imprint.Visual.Tests;

/// <summary>The parts of capture that don't need a window: quoting, process trees, stability and platform selection.</summary>
public sealed class CaptureLogicTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("", "\"\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\Program Files\", "\"C:\\Program Files\\\\\"")]
    [InlineData(@"a\\b", @"a\\b")]
    [InlineData(@"a\""b", "\"a\\\\\\\"b\"")]
    public void WindowsArgumentsAreQuotedForTheCRuntime(string argument, string quoted)
        => Assert.Equal($"app.exe {quoted}", WindowsCommandLine.For("app.exe", [argument]));

    [Theory]
    [InlineData("simple", "'simple'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("$(rm -rf /); `x`", "'$(rm -rf /); `x`'")]
    public void AndroidArgumentsAreQuotedForTheDeviceShell(string argument, string quoted)
        => Assert.Equal(quoted, AdbDevice.Quote(argument));

    [Fact]
    public void AProcessTreeHoldsEveryDescendant()
    {
        var tree = ProcessTree.Descendants(10, [(10, 1), (11, 10), (12, 11), (13, 1), (14, 12), (10, 10)]);
        Assert.Equal([10, 11, 12, 14], tree.Order());
    }

    [Fact]
    public async Task CaptureWaitsForMatchingPixelsInsideTheRequestedRegion()
    {
        var frames = new Queue<RgbaImage>([Pair(1, 0), Pair(2, 1), Pair(2, 2), Pair(2, 3)]);
        await using var app = await new FrameSession(_ => Task.FromResult(frames.Dequeue())).StartAsync();
        var shot = await app.CaptureAsync(new()
        {
            Region = new(0, 0, 1, 1),
            Stability = new() { MatchingFrames = 3, Interval = TimeSpan.Zero, MinimumDuration = TimeSpan.Zero }
        });
        Assert.Equal(1, shot.Image.Width);
        Assert.Equal(2, shot.Image[0, 0].R);
        Assert.Empty(frames);
    }

    [Fact]
    public async Task OneMatchingFrameReturnsWithoutWaitingForTheNextSample()
    {
        var calls = 0;
        await using var app = await new FrameSession(_ => Task.FromResult(Pair((byte)++calls, 0))).StartAsync();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var shot = await app.CaptureAsync(new()
        {
            Stability = new() { MatchingFrames = 1, MinimumDuration = TimeSpan.Zero, Interval = TimeSpan.MaxValue, Timeout = TimeSpan.MaxValue }
        }, cancel.Token);
        Assert.Equal(1, shot.Image[0, 0].R);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void AWindowThatNeverSettlesFailsWithItsLastFrames()
    {
        using var fixture = new Fixture();
        var calls = 0;
        var stability = new ResolvedStability(MatchingFrames: 2, Interval: TimeSpan.FromMilliseconds(5), MinimumDuration: TimeSpan.Zero,
            Timeout: TimeSpan.FromMilliseconds(200));
        var error = Assert.Throws<SnapshotCaptureException>(() => fixture.Run(
            () => StableCapture.CaptureAsync(_ => Task.FromResult(Pair((byte)++calls, 42)), stability, new(0, 0, 1, 1), CancellationToken.None).GetAwaiter().GetResult(),
            SnapshotUpdate.Missing));
        Assert.Contains("Between the last two frames: 1 of 1 pixels differ", error.Message, StringComparison.Ordinal);

        var frames = Directory.GetFiles(fixture.Root, "capture-timeout.*.png", SearchOption.AllDirectories);
        Assert.Equal(["capture-timeout.last.png", "capture-timeout.previous.png"], frames.Select(Path.GetFileName).Order());
        Assert.All(frames, file => Assert.Equal(1, RgbaImage.FromPng(File.ReadAllBytes(file)).Width));
    }

    [Fact]
    public async Task AFrameDeadlineLetsTheCallerReturnAndShutdownWaitsForTheDriver()
    {
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<RgbaImage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new FrameSession(token =>
        {
            started.SetResult(token);
            return release.Task;
        });
        await using var app = await driver.StartAsync();
        try
        {
            var read = app.CaptureAsync(new() { Stability = new() { MatchingFrames = 1, MinimumDuration = TimeSpan.Zero, Timeout = TimeSpan.FromSeconds(1) } });
            var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<SnapshotCaptureException>(() => read.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(token.IsCancellationRequested);

            var queued = app.CaptureAsync();
            var stopping = app.DisposeAsync().AsTask();
            Assert.False(driver.Disposed);
            Assert.False(stopping.IsCompleted);
            release.SetResult(Pair(1, 2));
            await stopping.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => app.CaptureAsync());
            Assert.True(driver.Disposed);
        }
        finally
        {
            release.TrySetResult(Pair(1, 2));
        }
    }

    [Fact]
    public void StabilityPatchesValidate()
    {
        Assert.Throws<SnapshotConfigurationException>(() => ResolvedStability.Defaults.Apply(new CaptureStability { MatchingFrames = 0 }));
        Assert.Throws<SnapshotConfigurationException>(() => ResolvedStability.Defaults.Apply(new CaptureStability { Timeout = TimeSpan.Zero }));
        Assert.Equal(new ResolvedStability(5, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(10)),
            ResolvedStability.Defaults.Apply(new CaptureStability { MatchingFrames = 5 }));
    }

    [Theory]
    [InlineData(null, null, null, VisualCheckStatus.Ready)]
    [InlineData(null, null, "missing", VisualCheckStatus.NotRequested)]
    [InlineData(null, "true", "missing", VisualCheckStatus.Missing)]
    [InlineData(null, "true", null, VisualCheckStatus.Ready)]
    [InlineData("android", null, null, VisualCheckStatus.NotRequested)]
    [InlineData("HOST, android", null, "missing", VisualCheckStatus.Missing)]
    [InlineData("HOST", "true", null, VisualCheckStatus.Ready)]
    [InlineData("none", "true", "missing", VisualCheckStatus.NotRequested)]
    public async Task PlatformSelectionNeverLetsCiSkipItsOwnPlatform(string? platforms, string? ci, string? problem, VisualCheckStatus expected)
    {
        var host = HostTarget();
        using var listed = new EnvironmentValue(PlatformSelection.Variable, platforms?.Replace("HOST", PlatformNames.Of(host.Platform), StringComparison.Ordinal));
        using var build = new EnvironmentValue("CI", ci);
        var check = await PlatformSelection.CheckAsync(host, new FakeDriver(problem), CancellationToken.None);
        Assert.Equal(expected, check.Status);
    }

    [Fact]
    public async Task CiSkipsAPlatformOfAnotherMachine()
    {
        using var listed = new EnvironmentValue(PlatformSelection.Variable, null);
        using var build = new EnvironmentValue("CI", "true");
        var check = await PlatformSelection.CheckAsync(new AndroidApp("app"), new FakeDriver("No device."), CancellationToken.None);
        Assert.Equal(VisualCheckStatus.NotRequested, check.Status);
    }

    [Fact]
    public async Task AnUnknownPlatformNameIsRejected()
    {
        using var listed = new EnvironmentValue(PlatformSelection.Variable, "windows,browser");
        var failure = await Assert.ThrowsAsync<SnapshotConfigurationException>(() => PlatformSelection.CheckAsync(new WindowsApp("app.exe"), new FakeDriver(null), CancellationToken.None));
        Assert.Contains("browser", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowSelectorsMatchTitles()
    {
        Assert.True(new WindowSelector().Matches("anything"));
        Assert.True(new WindowSelector { Title = "Settings" }.Matches("Settings"));
        Assert.False(new WindowSelector { Title = "Settings" }.Matches("Settings 2"));
        Assert.True(new WindowSelector { TitleContains = "ett" }.Matches("Settings"));
    }

    private static RgbaImage Pair(byte left, byte right) => RgbaImage.FromPixels(2, 1, [Rgba32.Opaque(left, 0, 0), Rgba32.Opaque(right, 0, 0)]);

    private sealed class FrameSession(Func<CancellationToken, Task<RgbaImage>> read) : IPlatformSession
    {
        internal bool Disposed { get; private set; }

        internal Task<VisualApp> StartAsync() => VisualApp.AttachSourceAsync(VisualPlatform.Windows, ExternalCaptureSource.Instance, this, new(), CancellationToken.None);

        public Task<RgbaImage> CaptureAsync(int maxPixels, CancellationToken cancellationToken) => read(cancellationToken);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A target on this machine's own desktop platform.</summary>
    private static AppTarget HostTarget()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsApp("app.exe");
        }
        return OperatingSystem.IsMacOS() ? new MacApp("app") : new LinuxApp("app");
    }

    private sealed class FakeDriver(string? problem) : IPlatformDriver
    {
        public Task<string?> FindProblemAsync(AppTarget target, CancellationToken cancellationToken) => Task.FromResult(problem);

        public Task<IPlatformSession> StartAsync(LaunchPlan plan, SessionSettings settings, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IPlatformSession> AttachAsync(int processId, SessionSettings settings, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
