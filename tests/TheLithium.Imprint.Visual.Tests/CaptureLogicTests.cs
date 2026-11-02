using TheLithium.Imprint.Specifications;
using TheLithium.Imprint.Visual.Platforms;
using TheLithium.Imprint.Visual.Platforms.Android;
using TheLithium.Imprint.Visual.Platforms.Windows;
using TheLithium.Imprint.Visual.Sources;
using TheLithium.Imprint.Visual.Tools;
using Xunit;

namespace TheLithium.Imprint.Visual.Tests;

/// <summary>The parts of capture that don't need a window: quoting, process trees, stability and platform selection.</summary>
[Collection(nameof(EnvironmentCollection))]
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
    public async Task AStableCaptureWaitsForIdenticalFrames()
    {
        var frames = new Queue<RgbaImage>([Solid(1), Solid(2), Solid(3), Solid(3), Solid(3), Solid(3)]);
        var captured = await StableCapture.CaptureAsync(_ => Task.FromResult(frames.Dequeue()),
            new(MatchingFrames: 3, Interval: TimeSpan.Zero, MinimumDuration: TimeSpan.Zero, Timeout: TimeSpan.FromSeconds(10)), CancellationToken.None);
        Assert.Equal(3, captured.Pixels[0].R);
        Assert.Single(frames);
    }

    [Fact]
    public async Task OneMatchingFrameTakesTheFirstFrame()
    {
        var calls = 0;
        await StableCapture.CaptureAsync(_ => Task.FromResult(Solid((byte)++calls)),
            new(MatchingFrames: 1, Interval: TimeSpan.Zero, MinimumDuration: TimeSpan.Zero, Timeout: TimeSpan.FromSeconds(10)), CancellationToken.None);
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
            () => StableCapture.CaptureAsync(_ => Task.FromResult(Solid((byte)++calls)), stability, CancellationToken.None).GetAwaiter().GetResult(),
            SnapshotUpdate.Missing));
        Assert.Contains("kept changing", error.Message, StringComparison.Ordinal);
        Assert.Contains("Between the last two frames: 1 of 1 pixels differ", error.Message, StringComparison.Ordinal);

        // The failed test's artifacts keep the last two frames.
        var frames = Directory.EnumerateFiles(fixture.Root, "capture-timeout.*.png", SearchOption.AllDirectories).Select(Path.GetFileName);
        Assert.Equal(["capture-timeout.last.png", "capture-timeout.previous.png"], frames.Order());
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
    [InlineData("windows, android", null, "missing", VisualCheckStatus.Missing)]
    [InlineData("WINDOWS", "true", null, VisualCheckStatus.Ready)]
    public async Task PlatformSelectionNeverLetsCiSkipSilently(string? platforms, string? ci, string? problem, VisualCheckStatus expected)
    {
        using var listed = new EnvironmentValue(PlatformSelection.Variable, platforms);
        using var build = new EnvironmentValue("CI", ci);
        var check = await PlatformSelection.CheckAsync(new WindowsApp("app.exe"), new FakeDriver(problem), CancellationToken.None);
        Assert.Equal(expected, check.Status);
    }

    [Fact]
    public async Task AnUnknownPlatformNameIsRejected()
    {
        using var listed = new EnvironmentValue(PlatformSelection.Variable, "windows,browser");
        var failure = await Assert.ThrowsAsync<SnapshotConfigurationException>(() => PlatformSelection.CheckAsync(new WindowsApp("app.exe"), new FakeDriver(null), CancellationToken.None));
        Assert.Contains("browser", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxTargetsNameTheirDisplay()
    {
        Assert.Equal(VisualPlatform.LinuxX11, new LinuxApp("app").Platform);
        Assert.Equal(VisualPlatform.LinuxWayland, new LinuxApp("app") { Display = LinuxDisplay.Wayland }.Platform);
    }

    [Fact]
    public void WindowSelectorsMatchTitles()
    {
        Assert.True(new WindowSelector().Matches("anything"));
        Assert.True(new WindowSelector { Title = "Settings" }.Matches("Settings"));
        Assert.False(new WindowSelector { Title = "Settings" }.Matches("Settings 2"));
        Assert.True(new WindowSelector { TitleContains = "ett" }.Matches("Settings"));
    }

    private static RgbaImage Solid(byte red) => RgbaImage.FromPixels(1, 1, [Rgba32.Opaque(red, 0, 0)]);

    private sealed class FakeDriver(string? problem) : IPlatformDriver
    {
        public Task<string?> FindProblemAsync(AppTarget target, CancellationToken cancellationToken) => Task.FromResult(problem);

        public Task<IPlatformSession> StartAsync(LaunchPlan plan, SessionSettings settings, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IPlatformSession> AttachAsync(int processId, SessionSettings settings, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

/// <summary>Tests that set environment variables don't run alongside each other.</summary>
[CollectionDefinition(nameof(EnvironmentCollection), DisableParallelization = true)]
public sealed class EnvironmentCollection;
