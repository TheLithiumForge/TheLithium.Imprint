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
        var screenshot = await app.CaptureAsync(new() { Stability = new() { Timeout = TimeSpan.FromSeconds(30) } });
        Assert.Equal(platform, screenshot.Platform);
        Assert.Equal(CaptureSource.External, screenshot.Source);

        // The fixture is 400x300 points, half red and half green. Scale, window frames and display color spaces
        // vary, so look for the two halves by their strongest channel anywhere, side by side.
        var red = Region(screenshot.Image, pixel => pixel.R > 150 && pixel.G < 110 && pixel.B < 110);
        var green = Region(screenshot.Image, pixel => pixel.G > 120 && pixel.R < 130 && pixel.B < 160);
        Assert.True(red.Count >= 200 * 300 * 9 / 10, $"Found {red.Count} red pixels in a {screenshot.Image.Width}x{screenshot.Image.Height} capture.");
        Assert.True(green.Count >= 200 * 300 * 9 / 10, $"Found {green.Count} green pixels in a {screenshot.Image.Width}x{screenshot.Image.Height} capture.");
        Assert.True(red.MeanX < green.MeanX, "The red half should be left of the green half.");
    }

    [Fact]
    public async Task AStoppedSessionRefusesToCapture()
    {
        var target = Target(Current());
        if ((await VisualApp.CheckAsync(target)).Status != VisualCheckStatus.Ready)
        {
            return;
        }

        var app = await VisualApp.StartAsync(target);
        await app.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => app.CaptureAsync());
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

    /// <summary>The fixture next to this test project's output, built through the project reference.</summary>
    private static string DesktopFixture()
    {
        var output = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pivot = Path.GetFileName(output);
        var bin = Path.GetDirectoryName(Path.GetDirectoryName(output)) ?? output;
        var name = OperatingSystem.IsWindows() ? "TheLithium.Imprint.Visual.Fixture.exe" : "TheLithium.Imprint.Visual.Fixture";
        return Path.Combine(bin, "TheLithium.Imprint.Visual.Fixture", pivot, name);
    }

    /// <summary>How many pixels match, and their mean column.</summary>
    private static (int Count, double MeanX) Region(RgbaImage image, Func<Rgba32, bool> matches)
    {
        var count = 0;
        double sum = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (matches(image[x, y]))
                {
                    count++;
                    sum += x;
                }
            }
        }
        return (count, count == 0 ? 0 : sum / count);
    }
}
