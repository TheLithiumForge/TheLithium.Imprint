using Xunit;

namespace TheLithium.Imprint.Visual.Tests;

/// <summary>
/// Starts the fixture app on each platform this machine can drive and checks the capture's pixels. A row whose
/// platform isn't requested passes without capturing. CI lists its platforms in IMPRINT_VISUAL_PLATFORMS, so there a
/// missing prerequisite fails instead.
/// </summary>
public sealed class DriverTests
{
    private static readonly Rgba32 Green = Rgba32.Opaque(40, 180, 90);

    [Theory]
    [InlineData(VisualPlatform.Windows), InlineData(VisualPlatform.LinuxX11), InlineData(VisualPlatform.LinuxWayland), InlineData(VisualPlatform.MacOS)]
    [InlineData(VisualPlatform.Android), InlineData(VisualPlatform.IOS)]
    public async Task TheFixtureWindowIsCapturedWithItsArguments(VisualPlatform platform)
    {
        var target = Target(platform);
        if ((await VisualApp.CheckAsync(target)).Status == VisualCheckStatus.NotRequested)
        {
            return;
        }

        await using var app = await VisualApp.StartAsync(target, new() { StartTimeout = TimeSpan.FromSeconds(90) });
        // Both Windows fixtures open at the same position. The covering window's right half is blue.
        await using var cover = target is WindowsApp windows
            ? await VisualApp.StartAsync(windows with { Arguments = ["--title", "Cover", "--topmost"] })
            : null;
        var screenshot = await app.CaptureAsync(new() { Stability = new() { Timeout = TimeSpan.FromSeconds(30) } });
        Assert.Equal(platform, screenshot.Platform);
        Assert.Equal(CaptureSource.External, screenshot.Source);

        // Sample inside each half so window frames, scale and system bars can vary.
        var image = screenshot.Image;
        var left = image[image.Width / 4, image.Height / 2];
        var right = image[3 * image.Width / 4, image.Height / 2];
        Assert.True(left is { R: > 150, G: < 110, B: < 110 }, $"Expected the red half, got {left}.");
        Assert.True(right is { G: > 120, R: < 130, B: < 160 }, $"Expected the green half, got {right}.");
    }

    [Fact]
    public async Task AnUnmatchedWindowSelectorFailsWithTheCandidates()
    {
        var target = Target(Current());
        if ((await VisualApp.CheckAsync(target)).Status != VisualCheckStatus.Ready)
        {
            return;
        }

        var failure = await Assert.ThrowsAsync<SnapshotCaptureException>(() => VisualApp.StartAsync(target, new()
        {
            Window = new()
            {
                Title = "No such window"
            },
            StartTimeout = TimeSpan.FromSeconds(10)
        }));
        Assert.Contains("showed no window", failure.Message, StringComparison.Ordinal);
    }

    private static VisualPlatform Current()
    {
        if (OperatingSystem.IsWindows())
        {
            return VisualPlatform.Windows;
        }
        return OperatingSystem.IsMacOS() ? VisualPlatform.MacOS : VisualPlatform.LinuxX11;
    }

    private static AppTarget Target(VisualPlatform platform)
    {
        string[] arguments = ["--title", "Imprint fixture", "--right", $"{Green.R},{Green.G},{Green.B}"];
        var fixture = Environment.GetEnvironmentVariable("IMPRINT_VISUAL_FIXTURE") ?? DesktopFixture();
        var fixtureEnvironment = new Dictionary<string, string> { ["GSK_RENDERER"] = "cairo" };
        return platform switch
        {
            VisualPlatform.Windows => new WindowsApp(fixture) { Arguments = arguments },
            VisualPlatform.LinuxX11 => new LinuxApp(fixture) { Arguments = arguments, Environment = fixtureEnvironment },
            VisualPlatform.LinuxWayland => new LinuxApp(fixture) { Display = LinuxDisplay.Wayland, Arguments = arguments, Environment = fixtureEnvironment },
            VisualPlatform.MacOS => new MacApp(fixture) { Arguments = arguments },
            VisualPlatform.Android => new AndroidApp("dev.thelithium.imprint.fixture")
            {
                Apk = Environment.GetEnvironmentVariable("IMPRINT_VISUAL_APK") ?? "fixture.apk",
                Activity = ".FixtureActivity",
                Extras = new Dictionary<string, string> { ["right"] = $"{Green.R},{Green.G},{Green.B}" }
            },
            VisualPlatform.IOS => new IosSimulatorApp("dev.thelithium.imprint.fixture")
            {
                AppBundle = Environment.GetEnvironmentVariable("IMPRINT_VISUAL_IOS_APP") ?? "Fixture.app",
                Arguments = arguments
            },
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
        };
    }

    [Fact]
    public async Task WaylandCapturesTheSelectedWindowWhenItsProcessOpensTwo()
    {
        var fixture = (LinuxApp)Target(VisualPlatform.LinuxWayland);
        var target = fixture with { Arguments = [.. fixture.Arguments, "--second-window"] };
        if ((await VisualApp.CheckAsync(target)).Status == VisualCheckStatus.NotRequested)
        {
            return;
        }
        await using var app = await VisualApp.StartAsync(target, new() { Window = new() { Title = "Imprint fixture" } });
        var image = (await app.CaptureAsync()).Image;
        var right = image[image.Width * 3 / 4, image.Height / 2];
        Assert.True(right.G > right.B && right.G > right.R, $"The selected window is green on the right, the second window is blue. Found {right}.");
    }

    /// <summary>The fixture next to this test project's output, built through the project reference.</summary>
    private static string DesktopFixture()
    {
        var output = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pivot = Path.GetFileName(output);
        var bin = Path.GetDirectoryName(Path.GetDirectoryName(output)) ?? output;
        var name = OperatingSystem.IsWindows() ? "TheLithium.Imprint.Visual.Fixture.exe" : "TheLithium.Imprint.Visual.Fixture";
        return Path.Combine(bin, "TheLithium.Imprint.Visual.Fixture", pivot, name);
    }

}
