# Images and screenshots

Imprint's visual packages snapshot images the way `AssertSnapshot` snapshots values. `TheLithium.Imprint.Visual` compares any image with a PNG baseline, and `TheLithium.Imprint.Visual.Capture` starts an app, takes a screenshot of its window and hands it over.

E.g. a test starts your app with `--screen settings`, waits until the window stops changing, and captures it. The first run writes `settings.png` next to the test. A later run where a button moved fails, and the failure folder gets `settings.received.png`, `settings.expected.png` and `settings.diff.png`.

The capture works from outside the app, so it works with any UI framework: WPF, WinForms, WinUI, MAUI, Avalonia, GTK, Qt, Electron or a native app. The app needs no capture code and no reference to Imprint.

[Back to the README](../README.md)

## Install

Add the package you need to your test project:

```bash
dotnet add package TheLithium.Imprint.Visual.Capture
```

`TheLithium.Imprint.Visual.Capture` brings `TheLithium.Imprint.Visual`, which brings `TheLithium.Imprint`. If you only compare images your own code produces, e.g. charts or frames from a headless renderer, `TheLithium.Imprint.Visual` is enough.

Both packages target `net10.0` only and are Native AOT and trim compatible. They need no workload and no platform TFM.

## Image snapshots

`RgbaImage` is an image as 8-bit RGBA pixels. You can build one from a PNG or from pixels, then assert it:

```csharp
[Fact]
public void RendersTheChart()
{
    var chart = RgbaImage.FromPng(ChartRenderer.Render(sales));
    chart.AssertImage(); // chart.png
}
```

Everything works as for JSON:

- Files - the image goes in the test's snapshot folder, named after the variable or the name you pass. `AssertImage("revenue")` writes `revenue.png`.
- Update policies - `missing` creates the baseline, `verify` only compares, and `all` overwrites or deletes. `UpdateImage` updates one image, like `UpdateSnapshot`.
- Approval - images and JSON in one test are approved together, after the method finishes. If the test throws, nothing is written.
- Pruning - a stale screenshot folder is removed like a stale JSON folder, as [Stale folders](../README.md#stale-folders) describes.

`AssertSnapshot` on an `RgbaImage` or a `Screenshot` fails the build with `IMP002`, because it would serialize the image's properties instead of its pixels.

Every baseline is PNG. Imprint writes it with fixed settings and no metadata, so the same pixels give the same file. When a newer runtime compresses the same pixels differently, the comparison still matches and `all` keeps the stored file, so git never shows a change that isn't one.

## Screenshots

`VisualApp` starts an app on a platform and captures it. The platform is a test parameter, so each one gets its own baseline:

```csharp
[Theory]
[InlineData(VisualPlatform.Windows)]
[InlineData(VisualPlatform.LinuxWayland)]
public async Task Settings(VisualPlatform platform)
{
    var app = TestApps.For(platform, ["--screen", "settings"]);
    if ((await VisualApp.CheckAsync(app)).Status == VisualCheckStatus.NotRequested)
    {
        Assert.Skip("This run doesn't drive " + platform);
    }

    await using var running = await VisualApp.StartAsync(app);
    var settings = await running.CaptureAsync();
    settings.AssertImage();
}
```

`TestApps.For` is your own helper that returns the target for each platform, e.g. `new WindowsApp(path) { Arguments = arguments }`. The run gives these files:

```text
__snapshots__/SettingsTests/Settings [platform=Windows]/settings.png
__snapshots__/SettingsTests/Settings [platform=LinuxWayland]/settings.png
```

`CaptureAsync` waits for the window to settle first, as [Stable capture](#stable-capture) describes. `DisposeAsync` stops the app and puts back anything the session changed.

The capture is the whole window as the OS shows it, with its frame and title bar. On Android and iOS the app fills the screen, so the capture is the whole screen, with a fixed status bar.

### Which window

The window is the one top-level window of the started process or of any process it started, so a launcher that starts the real app still works. If the app shows more than one window, e.g. a splash screen, the capture fails and lists them. Pick one with `Window`:

```csharp
await VisualApp.StartAsync(app, new() { Window = new() { Title = "Settings" } });
```

To capture an app that's already running, e.g. one your test framework started, attach to its process on Windows or macOS with `VisualApp.AttachAsync`. An attached app keeps running when the session ends.

## Platforms

Each platform has one target type and needs a few things on the machine that runs the tests:

| Platform | Target | The machine needs | Capture |
| --- | --- | --- | --- |
| `Windows` | `WindowsApp(exe)` | An unlocked desktop | The window's frame, copied from the composed desktop |
| `LinuxX11` | `LinuxApp(executable)` | `Xvfb`, `openbox` and `libX11` | `XGetImage` on a display the capture starts for the app |
| `LinuxWayland` | `LinuxApp(executable) { Display = LinuxDisplay.Wayland }` | `sway` and `grim` | `grim` on a headless sway the capture starts for the app |
| `MacOS` | `MacApp(path)` | Screen Recording for the process that runs the tests | `screencapture -l`, which captures a covered window too |
| `Android` | `AndroidApp(package)` | `adb` and a running device or emulator | `adb exec-out screencap -p` |
| `IOS` | `IosSimulatorApp(bundleId)` | macOS with Xcode and a booted simulator | `simctl io screenshot` |

Arguments reach the app the platform's own way: command-line arguments on the desktop and iOS, and intent extras on Android through `AndroidApp.Extras`.

On Linux the app always runs on a display server of its own, never on the desktop you're logged in to. Its Xvfb accepts only clients with its random cookie, and its sway sockets sit in a folder only you can read.

A missing prerequisite fails with what to install or enable, e.g. "Screen Recording isn't granted to the process that runs the tests. Enable it for your terminal or IDE in System Settings, Privacy and Security, Screen and System Audio Recording, then restart it."

### Platform selection

`IMPRINT_VISUAL_PLATFORMS` lists the platforms a run drives, e.g. `windows,android`, or `none`. `VisualApp.CheckAsync` tells a test what to do with a row:

| Status | When | What the test does |
| --- | --- | --- |
| `Ready` | The platform runs here. | Runs. |
| `NotRequested` | The variable doesn't list it, or without the variable, its prerequisites are missing. | Skips through its framework, e.g. `Assert.Skip` in xUnit v3, `Assert.Ignore` in NUnit or `Assert.Inconclusive` in MSTest. |
| `Missing` | The variable lists it, or it's this machine's own desktop platform in CI, and a prerequisite is missing. | Fails. `StartAsync` throws the same message. |

A listed platform never skips, so set the variable in CI to the platforms each job drives. A local run without it drives whatever the machine can. The platform names are `windows`, `linux-x11`, `linux-wayland`, `macos`, `android` and `ios`.

## Stable capture

`CaptureAsync` captures until enough identical frames in a row have been held long enough, then returns the last one. This keeps animations and late layout out of baselines, and needs no signal from the app.

`CaptureStability` sets the wait:

| Property | Defaults to | Meaning |
| --- | --- | --- |
| `MatchingFrames` | `3` | Identical frames in a row that end the wait. |
| `Interval` | 100 ms | The least time between the starts of two captures. |
| `MinimumDuration` | 500 ms | How long the frames have to stay identical. |
| `Timeout` | 10 seconds | The longest wait. After it, the capture fails. |

Frames compare exactly, so a blinking caret or an endless animation keeps the wait going. When the timeout ends it, the failure says how much the last two frames differed and keeps both frames with the failure artifacts. Stop animations for the test, or crop the changing area away with `CaptureOptions.Region`.

`MatchingFrames = 1` with no minimum duration takes one frame without waiting.

## Comparison

Two images match when they have the same size and every compared pixel passes. The default is exact. `ImageComparison` relaxes it:

| Property | Defaults to | Meaning |
| --- | --- | --- |
| `ChannelTolerance` | `0` | The largest difference in any of R, G, B and A, out of 255, for two pixels to count as equal. |
| `MaxDifferingPercent` | `0` | The share of compared pixels that can differ beyond the tolerance. |
| `IgnoreAntialiasing` | `false` | Ignores a differing pixel that looks like an anti-aliased edge in either image. A moved line or a changed glyph still fails. |
| `IgnoreRegions` | none | Areas left out of the comparison, e.g. a clock. The share of differing pixels is taken of the rest. |

Two fully transparent pixels are equal whatever their color. A size mismatch fails before any pixel is compared, e.g. "Expected 960x640, received 1920x1280. A different size usually means a different display scale or window size."

A pixel mismatch says how many pixels differ, the largest channel difference and where the differences are:

```text
settings.png: Changed
  1,204 of 614,400 pixels differ (0.196 percent, limit 0). Largest channel difference 143, at 412,96. The differences span 380,80 to 520,140.
```

`<name>.diff.png` shows the received image faded, changed pixels in red, pixels ignored as anti-aliasing in yellow and ignored regions in blue. For a size mismatch it shows both images side by side.

A custom comparer implements `IImageComparer`, the way a JSON comparer implements `ISnapshotComparer`. Imprint calls it only for images that differ in at least one pixel.

### Updates

`all` keeps a stored image whose pixels are identical to the new capture, even when the file's bytes differ. A capture that differs but stays within tolerance is written, the way `all` writes changed JSON.

## Settings

The comparison and the wait resolve field by field, and the most specific layer wins:

1. Built-in - the defaults above.
2. Project - the `visual` group in `snapshots.config.json`.
3. Platform - `visual.platforms.<platform>` in `snapshots.config.json`, for screenshots from one platform.
4. App - `VisualAppOptions.Comparison` and `VisualAppOptions.Stability`.
5. Capture - `CaptureOptions.Stability`.
6. Assertion - `ImageSnapshotOptions.Comparison`.

The `visual` group has every setting with its default:

```json
{
  "visual": {
    "captureSource": "external",
    "maxBytesPerImage": 33554432,
    "maxPixelsPerImage": 8388608,
    "comparison": { "channelTolerance": 0, "maxDifferingPercent": 0, "ignoreAntialiasing": false },
    "stability": { "matchingFrames": 3, "intervalMilliseconds": 100, "minimumDurationMilliseconds": 500, "timeoutSeconds": 10 },
    "platforms": {
      "android": { "comparison": { "channelTolerance": 8 } }
    }
  }
}
```

`maxBytesPerImage` limits one PNG file, separately from `limits.maxBytesPerSnapshot` for JSON. `maxPixelsPerImage` limits the size of an image before anything is decoded. [API](API.md#project-file) covers the rest of the file.

## Capture sources

A capture source is how the pixels are taken. There's one today, `CaptureSource.External`, where the test process captures through the OS's own tools and calls.

`VisualAppOptions.Source` and `visual.captureSource` choose the source. The default stays `External`. A source that captures from inside the app would give other pixels, e.g. without the OS-drawn frame, so switching a test to it is an update of its baselines.

## Determinism

Pixel tests are only as stable as the machine they run on:

- Platforms - every platform has its own baseline. Imprint never compares across platforms.
- Machines - baselines match the CI image they were made on. A local run on another OS version, GPU or display scale can fail against them, so update them from CI or in the same container image.
- Fonts - bundle your app's fonts. System fallbacks differ between machines.
- Time and live data - start the app with fixed data, e.g. through its arguments.
- Mobile system bars - Android gets demo mode and no animations, and iOS gets a fixed status bar. Both are put back afterwards.
- Pointer - the Windows capture moves the pointer off the window, so no hover effect shows. It moves it back when the session ends.

## Limits

- Windows - the capture copies the screen, so the window has to be fully on screen and not covered. The capture brings it to the front. A locked or disconnected session can't be captured.
- Linux - your own Wayland desktop can't be captured from outside without a prompt, which is why the app runs on its own display server. A window that draws its own title bar, such as GTK's, looks the same there. Other windows get Openbox's frame on X11 and sway's on Wayland.
- macOS - Screen Recording is needed for the terminal, IDE or CI agent that runs the tests. GitHub's macOS runner images grant it. macOS 15 might ask again from time to time on your own machine.
- iOS - only the simulator is supported. A physical device needs signing and pairing.
- Console apps - a console app has no window of its own, so capture its output with `AssertSnapshot` instead.
- One launch, one state - the capture sends no input, so a flow through several screens needs a launch per screen.
- Repository size - every baseline is a PNG. Dozens of screens per platform push toward Git LFS, which Imprint doesn't know about.
- The browser isn't a target yet.
