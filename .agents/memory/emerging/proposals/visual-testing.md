# Visual testing

Visual testing is a planned pair of Imprint packages that start a desktop or mobile app, take a screenshot of its window, and compare it with a committed PNG baseline the same way `AssertSnapshot` compares JSON.

It works with any UI framework, e.g. WPF, WinForms, WinUI, MAUI, Avalonia, GTK, Qt, Electron or a native app, because it captures the window from outside the app. E.g. a test starts an app with the parameter `screen=settings`, waits until the window stops changing, and captures it. The first run writes `settings.png` next to the test. A later run where a button moved fails, and the failure folder gets `settings.received.png`, `settings.expected.png` and `settings.diff.png`.

Status: spec on 2026-11-02. The MVP is being built on the branch `spec/screenshot-testing-mvp`.

## Decisions

These were decided in review:

- AOT and trimming - every piece is Native AOT and trim compatible, with no third-party package.
- Platforms - Windows, Linux with X11 and Wayland, macOS, Android and iOS. The browser is left out of the MVP.
- Any UI framework - an app is a process with a window. Nothing in the app needs to reference Imprint.
- App screenshots - the whole app window as the OS shows it, with the native frame and title bar.
- External capture first - the test process captures through each OS's own tools and calls. The app needs no capture code, no manifest entry and no permission.
- In-process capture later - a capture source setting exists from the start, so in-process capture can be added later without changing existing tests or their baselines.
- Same storage as JSON - images live in the same test folders, follow the same update policies, and are pruned the same way.
- One image format - every baseline is PNG on every platform. Tolerances can be set per test and per platform.

## Example

The platform is a test parameter, so each platform gets its own case folder and baseline.

Proposed:

```csharp
[Theory]
[InlineData(VisualPlatform.Windows)]
[InlineData(VisualPlatform.Linux)]
public async Task Settings(VisualPlatform platform)
{
    await using var app = await VisualApp.StartAsync(TestApps.For(platform), new VisualAppOptions
    {
        Parameters = new Dictionary<string, string> { ["screen"] = "settings" },
    });

    var screen = await app.CaptureAsync(); // waits for two identical frames
    screen.AssertImage("settings");        // compares with settings.png
}
```

`TestApps.For` is the test project's own helper. It returns a `WindowsApp` or a `LinuxApp` with the executable path. The run gives these files:

```text
__snapshots__/SettingsTests/Settings [platform=Windows]/settings.png
__snapshots__/SettingsTests/Settings [platform=Linux]/settings.png
```

A row whose platform this run doesn't drive is skipped by the test, through [Platform selection](#platform-selection). Its baseline stays, because pruning keeps the folders of a test that didn't run all its rows.

## Packages

Both packages live in this repo, target `net10.0` only and ship with the same version as Imprint:

| Package | Holds | Depends on |
| --- | --- | --- |
| `TheLithium.Imprint.Visual` | `RgbaImage`, the PNG codec, the comparer, the diff image, `AssertImage` and `UpdateImage`. It makes no OS call, so it behaves the same everywhere. | `TheLithium.Imprint`, the same version |
| `TheLithium.Imprint.Visual.Capture` | `VisualApp`, the app targets, capture sources, the platform drivers and stable capture. Every OS call and tool call is here. | `TheLithium.Imprint.Visual`, the same version |

`TheLithium.Imprint.Visual` also works alone, e.g. to check a chart your own code renders to PNG, or frames from a headless renderer.

Neither package has a platform TFM. Imprint itself is one `net10.0` project that branches with `OperatingSystem.IsMacOS()` where an OS differs, and the drivers do the same. A host test project needs no workload.

Visual reaches Core's internals through `InternalsVisibleTo`, so Core's public API doesn't change. The release workflow publishes all four packages together.

## Images

`RgbaImage` is an image as 8-bit RGBA pixels, top row first:

Proposed:

```csharp
public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }
    public ReadOnlySpan<Rgba32> Pixels { get; }

    public static RgbaImage FromPng(ReadOnlySpan<byte> png);
    public static RgbaImage FromPixels(int width, int height, ReadOnlySpan<Rgba32> pixels);
    public byte[] ToPng();
    public RgbaImage Crop(PixelRect area);
}

public readonly record struct Rgba32(byte R, byte G, byte B, byte A);
public readonly record struct PixelRect(int X, int Y, int Width, int Height);
```

PNG follows these rules:

- Read - 8-bit and 16-bit grayscale, gray with alpha, RGB, palette and RGBA, every filter type, no interlacing. 16-bit channels are reduced to 8 bits. An interlaced PNG fails with a clear message.
- Write - always 8-bit RGBA, one fixed filter and compression level, and no metadata chunk. The same pixels always give the same bytes, so git never shows a change that isn't one.
- Color - ICC, gamma and sRGB chunks are dropped on read. The stored values are the captured values.
- Size - a PNG over the size limits fails before anything is decoded.

PNG is the only format, because a lossy format would break pixel comparison.

## AssertImage

`AssertImage` stages an image like `AssertSnapshot` stages a value. It's compared when the test method finishes, under the same update policy:

Proposed:

```csharp
public static class ImageSnapshotExtensions
{
    public static void AssertImage(this RgbaImage image, string? name = null, ImageOptions? options = null,
        [CallerArgumentExpression(nameof(image))] string? expression = null);
    public static void UpdateImage(this RgbaImage image, string? name = null, ImageOptions? options = null,
        [CallerArgumentExpression(nameof(image))] string? expression = null);
}

public sealed record ImageOptions
{
    public SnapshotUpdate Update { get; init; } = SnapshotUpdate.Inherit;
    public ImageComparison? Comparison { get; init; }
    public IImageComparer? Comparer { get; init; }
}
```

The class is in the `TheLithium.Imprint` namespace, next to `SnapshotExtensions`, so `AssertImage` shows up without another `using`. The name works like `AssertSnapshot`: `screen.AssertImage()` writes `screen.png`.

Everything around a capture works as for JSON:

- Folders - the image goes in the same test folder as the test's JSON snapshots, under the same naming rules.
- Update policies - `missing` creates the baseline, `verify` only compares, and `all` overwrites or deletes.
- All-or-nothing - if the test throws, no image is written. JSON and images in one test are approved together.
- Failure artifacts - `<name>.received.png` and `<name>.expected.png` as today, plus `<name>.diff.png`.
- Pruning - `.png` counts as a snapshot file, so a stale screenshot folder is removed like a JSON one.

`AssertSnapshot` on an `RgbaImage` or a `Screenshot` fails the build with a new `IMP002` error, because it would serialize the image's properties instead of its pixels.

## Comparison

Two images match when they have the same size and the pixels pass `ImageComparison`. Each property is nullable and inherits when left out, like `SnapshotComparison`:

| Property | Defaults to | Meaning |
| --- | --- | --- |
| `ChannelTolerance` | `8` | The largest difference in any of R, G, B and A, out of 255, for a pixel to count as equal. |
| `MaxDifferingPercent` | `0.1` | The share of pixels that can differ beyond the tolerance. |
| `IgnoreAntialiasing` | `true` | Ignores a differing pixel when a neighbor in the other image has its color. Two renders often draw edges slightly differently. |
| `IgnoreRegions` | none | Areas left out of the comparison, e.g. a clock. Code only. |

A size mismatch fails before any pixel is compared, e.g. "Expected 960x640, received 1920x1280. The display scale differs." A pixel mismatch gives one line per image:

```text
settings.png: 1,204 of 614,400 pixels differ (0.20 percent, limit 0.10). Largest channel difference 143, at 412,96.
```

`<name>.diff.png` shows the received image faded, differing pixels in red, and pixels ignored as anti-aliasing in yellow.

A custom comparer implements `IImageComparer`. It mirrors `ISnapshotComparer`:

Proposed:

```csharp
public interface IImageComparer
{
    ImageComparisonResult Compare(RgbaImage expected, RgbaImage received, ResolvedImageComparison options);
}

public sealed record ImageComparisonResult(bool Equal, string? Difference = null, RgbaImage? Diff = null);
```

When the stored bytes equal the new bytes, the images match without decoding. The fixed PNG writer makes that the common path for a passing test.

## Apps

An `AppTarget` says what to start and where. There is one record per platform:

Proposed:

```csharp
public abstract record AppTarget
{
    public abstract VisualPlatform Platform { get; }
}

public sealed record WindowsApp(string Executable) : AppTarget;            // any exe
public sealed record WindowsPackagedApp(string AppUserModelId) : AppTarget; // an installed MSIX
public sealed record LinuxApp(string Executable) : AppTarget { public LinuxDisplay Display { get; init; } = LinuxDisplay.X11; }
public sealed record MacApp(string AppBundle) : AppTarget;                  // a .app folder
public sealed record AndroidApp(string Package) : AppTarget { public string? Apk { get; init; } public string? Activity { get; init; } public string? Device { get; init; } }
public sealed record IosSimulatorApp(string AppBundle, string BundleId) : AppTarget { public string? Device { get; init; } }

public enum VisualPlatform { Windows = 1, Linux = 2, MacOS = 3, Android = 4, IOS = 5 }
public enum LinuxDisplay { X11 = 1, Wayland = 2 }
```

Each record returns its own `Platform`. A desktop app that's already running can be attached by process ID, e.g. `WindowsApp.Attach(processId)`. An attached app isn't stopped at the end.

`VisualApp` is one running app:

Proposed:

```csharp
public sealed class VisualApp : IAsyncDisposable
{
    public static Task<VisualApp> StartAsync(AppTarget target, VisualAppOptions? options = null, CancellationToken cancellation = default);
    public static Task<VisualCheck> CheckAsync(AppTarget target, CancellationToken cancellation = default);

    public AppTarget Target { get; }
    public CaptureSource Source { get; }

    public Task<Screenshot> CaptureAsync(CaptureOptions? options = null, CancellationToken cancellation = default);
    public ValueTask DisposeAsync(); // stops the app it started, and restores device settings
}

public sealed record VisualAppOptions
{
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
    public CaptureSource Source { get; init; } = CaptureSource.Inherit;
    public ImageComparison? Comparison { get; init; }
    public StableCapture? Stable { get; init; }
    public string? WindowTitle { get; init; }      // picks one window when the app has several
    public TimeSpan? StartTimeout { get; init; }  // defaults to 30 seconds
}
```

The window is the largest visible top-level window of the started process or of any process it started, so a launcher that starts the real app still works. `WindowTitle` picks one window when there are several.

`Parameters` are mapped to what each platform has, so the app reads them the native way:

| Platform | A parameter `screen=settings` becomes |
| --- | --- |
| Windows, Linux, macOS | The arguments `--screen settings` |
| Windows packaged | The activation arguments `--screen settings` |
| Android | The intent extra `--es screen settings` |
| iOS | The launch arguments `--screen settings` |

`Screenshot` is the captured image with where it came from:

Proposed:

```csharp
public sealed record Screenshot(RgbaImage Image, VisualPlatform Platform, CaptureSource Source);
```

`screen.AssertImage()` on a `Screenshot` resolves the comparison with the platform layer from [Settings](#settings), then calls `AssertImage` on its image.

## Capture sources

A capture source is how the pixels are taken. The MVP has one:

| Source | How | Status |
| --- | --- | --- |
| `External` | The test process captures through the OS's own tools and calls. Covered in [Drivers](#drivers). | MVP |
| `InProcess` | The app captures its own window, through an agent package the app references in test builds. | Later. Not built. |

Proposed:

```csharp
public enum CaptureSource { Inherit = 0, External = 1 } // later: InProcess = 2
```

The source is set in three places, and the most specific one wins:

1. Project - `visual.captureSource` in `snapshots.config.json`. Defaults to `external`.
2. App - `VisualAppOptions.Source`, for one test's app.
3. Screenshot - `CaptureOptions.Source`, for one capture.

The default stays `External` forever. In-process pixels differ from external ones, e.g. they have no OS-drawn frame, so a default that flipped would break every baseline. Switching a test to another source is an update of its baselines.

Inside `TheLithium.Imprint.Visual.Capture`, every source is one implementation of an internal adapter:

Illustrative:

```csharp
interface ICaptureSource
{
    bool Supports(AppTarget target);
    LaunchPlan Prepare(LaunchPlan plan);                              // can add parameters, e.g. an agent endpoint
    Task<ICaptureSession> AttachAsync(RunningApp app, CancellationToken cancellation);
}

interface ICaptureSession : IAsyncDisposable
{
    Task<RgbaImage> CaptureAsync(CancellationToken cancellation);
}
```

`Prepare` runs before the app starts, so a later `InProcess` source can pass the app an endpoint to connect back to. A per-screenshot source has to be prepared when the app starts. `VisualApp` prepares the app's source plus every source named in `VisualAppOptions`, and a capture that asks for an unprepared source fails with what to set.

The adapter stays internal until `InProcess` ships, so its shape can still change. Adding `InProcess` later adds one enum value and one agent package, and changes no existing call.

An app or a renderer that tests itself from inside, e.g. a headless UI test, doesn't need any of this. It can build an `RgbaImage` from its own pixels and call `AssertImage`, since `TheLithium.Imprint.Visual` doesn't care where pixels come from.

## Drivers

Each platform has one `External` driver. A driver checks its prerequisites first, starts the app, finds its window, captures and stops it:

| Platform | Checks first | Starts | Captures |
| --- | --- | --- | --- |
| Windows | An unlocked desktop, through `OpenInputDesktop`. | The exe through `Process.Start`. An MSIX through `IApplicationActivationManager`, which returns the process ID. | Finds the window, brings it to the front, reads its frame with `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` in physical pixels, and copies that area from the desktop DC with `BitBlt`. The desktop DC holds the composed screen, so the DWM frame and GPU content such as WebView2 or DirectX are in it. |
| Linux X11 | `Xvfb` on `PATH` and `libX11.so.6` loadable. | Starts its own `Xvfb` on a free display at 1920x1080, then the app with `DISPLAY` set, `GDK_BACKEND=x11`, `QT_QPA_PLATFORM=xcb` and scale 1. | Finds the window by its `_NET_WM_PID` and copies its area with `XGetImage`. |
| Linux Wayland | `sway` and `grim` on `PATH`. | Starts its own headless sway, with `WLR_BACKENDS=headless`, one 1920x1080 output at scale 1, and a config that floats every window at its own size. Then the app with `WAYLAND_DISPLAY` set, `GDK_BACKEND=wayland` and `QT_QPA_PLATFORM=wayland`. | Finds the window by process ID in `swaymsg -t get_tree`, then runs `grim -g` on its area. |
| macOS | Screen Recording permission, through `CGPreflightScreenCaptureAccess`, which never shows a prompt. | The binary inside the `.app` through `Process.Start`. | Finds the window ID of the process with `CGWindowListCopyWindowInfo`, then runs `screencapture -x -o -l <id>`. `-l` captures the window even when it's covered, and `-o` leaves out the shadow. |
| Android | `adb` found, the device in state `device`, and `sys.boot_completed` set. | `adb install -r` when `Apk` is set, then `am start -W` with the extras. | `adb exec-out screencap -p`. |
| iOS | macOS, `xcrun simctl`, and a booted simulator. The driver boots the named one if needed. | `simctl install`, then `simctl launch --terminate-running-process` with the arguments. | `simctl io <device> screenshot --type=png`. |

A missing prerequisite fails the test with what to install or enable, e.g. "Screen Recording isn't granted to the process that runs the tests. Enable it for your terminal or IDE in System Settings, Privacy and Security, Screen and System Audio Recording, then restart it." A driver never returns a blank or partial image.

On Android and iOS the app fills the screen, so the driver captures the whole screen. It fixes the system bars first, and puts them back on `DisposeAsync`:

- Android - demo mode with a fixed clock, battery and signal, and the three animation scales at 0.
- iOS - `simctl status_bar override` with a fixed time, battery and signal.

## Stable capture

`CaptureAsync` captures until the last frames are identical, then returns the last one. This keeps animations and late layout out of baselines, and needs no signal from the app.

`StableCapture` has three properties:

| Property | Defaults to | Meaning |
| --- | --- | --- |
| `MatchingFrames` | `2` | How many identical frames in a row end the wait. |
| `Interval` | 100 ms | The time between captures. |
| `Timeout` | 10 seconds | The longest wait. After it, the capture fails and the last two frames go to the failure folder. |

`CaptureOptions.Stable = false` takes one frame without waiting. An app with an endless animation never settles, so it has to stop the animation for the test.

## Platform selection

`IMPRINT_VISUAL_PLATFORMS` lists the platforms this run drives, e.g. `windows,android`. `VisualApp.CheckAsync` tells a test what to do with a row:

| Status | When | What the test does |
| --- | --- | --- |
| `Ready` | The platform is listed and its prerequisites are there. Without the variable, outside CI, every platform whose prerequisites are there. | Runs. |
| `NotRequested` | The variable doesn't list the platform. Without the variable, outside CI, a platform whose prerequisites are missing. | Skips, through its framework, e.g. `Assert.Skip` in xUnit. |
| `Missing` | The platform is listed, but a prerequisite isn't there. In CI without the variable, every platform, with a message to set it. | Fails. `StartAsync` throws the same message. |

A listed platform never skips, and CI has to list its platforms. So a CI job can't pass without capturing anything. A local run without the variable drives whatever the machine can.

## Settings

Visual settings follow Imprint's layers. Each property inherits when left out:

| Layer | Where | Covers |
| --- | --- | --- |
| Built-in | Imprint | The defaults in this spec. |
| Project | The `visual` group in `snapshots.config.json` | Capture source, comparison, stable capture, size limits. |
| Platform | `visual.platforms.<platform>` in `snapshots.config.json` | Comparison for screenshots from one platform. |
| App | `VisualAppOptions` | Source, comparison and stable capture for one app. |
| Screenshot | `CaptureOptions` | Source and stable capture for one capture. |
| Assertion | `ImageOptions` | Update policy, comparison and comparer for one image. |
| Run environment | `IMPRINT_UPDATE`, `CI`, `IMPRINT_VISUAL_PLATFORMS` | The update policy, and which platforms the run drives. |

The project file gets one new group. Core's strict reader validates it, and the schema describes it:

Proposed:

```json
{
  "visual": {
    "captureSource": "external",
    "maxBytesPerImage": 33554432,
    "maxPixelsPerImage": 33554432,
    "comparison": { "channelTolerance": 8, "maxDifferingPercent": 0.1, "ignoreAntialiasing": true },
    "stableCapture": { "matchingFrames": 2, "intervalMilliseconds": 100, "timeoutSeconds": 10 },
    "platforms": {
      "android": { "comparison": { "channelTolerance": 16 } }
    }
  }
}
```

A `RgbaImage` that doesn't come from a capture has no platform, so only the project, assertion and run layers apply to it.

## Parallel runs

Test frameworks run tests in parallel, and `dotnet test` runs test projects in parallel processes. Some captures share one screen or one device, so they take a lock:

| Platform | Shared thing | Lock |
| --- | --- | --- |
| Windows | The desktop, because the capture brings the window to the front. | One machine-wide lock around each capture. |
| Android, iOS | The device, because one app is in front at a time. | One lock per device for the whole app session. |
| Linux, macOS | Nothing. Each Linux app has its own display server, and `screencapture -l` works on a covered window. | None. |

The locks are lock files in the temp folder, opened with `FileShare.None`, the same way the snapshot store locks a test folder. So they work across processes too.

## Cleanup

A driver stops only what it started, and the order is fixed: the app, then the display server, then device settings. A clean stop gets 5 seconds, then the process tree is killed.

If the test process dies, children could be left running:

- Windows - every started process joins a job object that kills its members when the test process exits.
- Linux and macOS - each started process is recorded with its start time under the temp folder. The next `VisualApp` start stops the ones whose test process is gone. The start time keeps a reused process ID from being killed.
- Android and iOS - the next start of the same app stops the old one, through `am force-stop` and `--terminate-running-process`.

## Core changes

Core changes inside, with no public API change:

| Part | Change |
| --- | --- |
| `SnapshotStore` | Holds file bytes, with text as a view of them. Fingerprints and the journal work on bytes. |
| `SnapshotFileNames` | Knows `.png`, so the store reads it and the pruner counts it. |
| `CapturedValue` | Carries bytes and a comparer for them, next to the text path. |
| `SnapshotScope` | Gets an internal `CaptureBinary` for Visual. It uses the same name rules, limits and budget as `Capture`. |
| `SnapshotPlan` | Sends a binary capture to its comparer. |
| `SnapshotArtifacts` | Writes bytes, and extra files from a comparison such as `<name>.diff.png`. |
| Configuration | Reads and validates the `visual` group. The schema gets it too. |
| Capture marker | An internal `[SnapshotCaptureMethod]` attribute. The MSBuild call graph and the generator treat any method with it as a capture. `AssertImage` and `UpdateImage` carry it. |

The capture marker matters for safety. Without it, a test that only calls `AssertImage` wouldn't be wrapped, so it would have no scope. The generator also wouldn't register it, so pruning would see its folder as stale.

## Safety

The parts that touch untrusted data or the machine follow these rules:

- PNG input - every length and CRC is checked. The size limits apply before anything is decoded. The decompressed size has to equal what the header says, so a small file can't expand into a huge one.
- Files - snapshots and artifacts go only where Imprint writes today. Temp files go in an owned temp folder that's removed afterwards.
- Processes - a driver never stops a process it didn't start or attach.
- Device settings - every setting a driver changes is read first and put back on `DisposeAsync`.

## Performance

Not measured yet. The expected costs are:

- Windows and X11 copy pixels in the test process, in milliseconds.
- `adb`, `simctl`, `screencapture` and `grim` take roughly 100 to 500 ms per capture.
- Stable capture usually takes 2 to 3 captures.
- A passing comparison is a byte comparison, because the PNG writer is fixed. Pixels are decoded only when bytes differ.
- One 1920x1080 frame is about 8 MB of pixels. Stable capture holds two.

## AOT and trimming

Both packages follow Core's rules:

- No reflection, and no `JsonSerializer` without a source-generated context.
- Native calls use `LibraryImport`, and COM calls use `[GeneratedComInterface]`, so marshalling is generated at compile time. That covers `user32`, `gdi32`, `dwmapi` and the activation manager on Windows, `libX11` on Linux, and CoreGraphics and CoreFoundation on macOS.
- Each OS call is marked `[SupportedOSPlatform]`.
- `IsAotCompatible` and `IsTrimmable` are on. The AOT lane publishes a consumer of both packages with ILC and trim warnings as errors, on Windows, Linux and macOS.

The PNG codec is plain loops over spans with `ZLibStream` and its own CRC-32. The AOT requirement rules out codec packages that aren't tested under `IlcTreatWarningsAsErrors`. SkiaSharp needs native binaries per target. ImageSharp says it supports AOT but brings a split license. StbImageSharp is pure C# but decode-only.

## Determinism

Each source of flaky pixel tests has an answer:

| Source | Answer |
| --- | --- |
| Different renderers per platform | One baseline per platform row. Never compare across platforms. |
| OS and runtime version drift | Pin the CI image. Android by emulator image, iOS by Xcode and simulator runtime, embedded browser engines by a fixed runtime. |
| Fonts | Bundle the app's fonts. System fallbacks differ per machine. |
| Display scale | Fixed at 1 on Linux, and at the runner's scale on Windows and macOS. A size mismatch names the scale. |
| Animations | Stable capture, plus animation scales at 0 on Android. |
| Time, random data, live data | The app's parameters seed them. |
| Anti-aliasing | `IgnoreAntialiasing`. |
| System bars on mobile | Demo mode and the status bar override. |
| Pointer hover | The Windows and macOS drivers move the pointer off the window before capturing. |
| Color profiles | Profiles are dropped and baselines come from the same image, so the values match. |

Baselines match the CI image they were made on. A local run on another OS version, GPU or scale can fail against them. Update them from CI, or in the same container image.

## Tests and CI

Each driver test starts a small fixture with known pixels, and checks colors at known points instead of comparing with a committed baseline. That keeps the lanes stable when GitHub updates its images.

| Lane | Runner | Fixture |
| --- | --- | --- |
| Codec, comparer, assertion | All three | Generated images for every color type, depth and filter, plus edge cases. |
| Windows driver | `windows-latest` | An AOT console app that opens a Win32 window and fills known rectangles. |
| Linux X11 and Wayland drivers | `ubuntu-latest`, with `xvfb`, `sway` and `grim` from apt | An app that opens a GTK window with known colors. The same app runs on both displays. |
| macOS driver | `macos-latest` | An AOT console app that opens an `NSWindow` with known colors, through `objc_msgSend`. GitHub's image grants Screen Recording to `/bin/bash`. |
| Android driver | `ubuntu-latest` with KVM | A small fixture app with known colors, on a pinned emulator image. |
| iOS driver | `macos-latest` | A small Objective-C app with known colors, built with `clang` for the simulator and signed ad hoc. |
| Package consumers | All three | xUnit, NUnit and MSTest consumers of both packages. They check `AssertImage`, the update policies, artifacts and a stale `.png` folder. |
| Native AOT | All three | The consumers, published with warnings as errors, capturing the desktop fixtures. |

## Plan

The work goes in this order, in small commits:

1. Core - the changes in [Core changes](#core-changes), with tests for bytes, pruning of `.png` and the capture marker.
2. Images - `RgbaImage`, the codec, the comparer, the diff image, `AssertImage`, `UpdateImage` and `IMP002`, with the codec lane.
3. Desktop - `VisualApp`, sources, stable capture, locks, cleanup, and the Windows, Linux and macOS drivers with their fixtures and lanes.
4. Mobile - the Android and iOS drivers with their fixtures and lanes.
5. Release - consumers, the AOT lane and four packages in the release workflow.
6. Docs - a README section with a link to a new `docs/VISUAL.md`, the `visual` group in `docs/API.md` and the schema, and the Not included list updated.
7. PR - one PR, then CI on all three runners.

## Limits

- Linux under your own Wayland desktop can't be captured from outside without a portal prompt, so the driver always runs the app on its own display server. The app looks the way that display server shows it. A window that draws its own title bar, such as GTK's, looks the same. Server-side title bars don't appear.
- The Windows capture copies the screen, so a locked or disconnected session can't be captured. Not tested yet on `windows-latest`.
- macOS needs Screen Recording for the process that runs the tests. macOS 15 might ask again from time to time. Not tested yet.
- An MSIX app is only tested by hand, because the Windows lane has no signed package to install.
- iOS runs on the simulator only. A physical device needs signing and pairing.
- A console app has no window of its own. Its text belongs to the terminal, so capture its output with `AssertSnapshot` instead.
- One launch shows the app as its parameters set it. A flow through several screens needs input, which the MVP doesn't send, or in-process capture later.
- PNG baselines grow the repository. A 960x640 window is roughly 50 KB to 300 KB per image. Dozens of screens per platform push toward Git LFS, which the store doesn't know about.
- A failing visual test shows where pixels differ, not why. The diff image is the whole diagnostic.

## Not decided yet

- The package names. `TheLithium.Imprint.Visual` and `TheLithium.Imprint.Visual.Capture` are used here.
- Whether the Wayland lane uses sway, or labwc or weston.
- Whether the Android and iOS lanes run on every PR or nightly.
- Whether the Windows driver adds `Windows.Graphics.Capture` later, for covered windows. It needs a WinRT projection and a `net10.0-windows` TFM.
- Whether desktop drivers set the window size, or leave it to the app.
- Whether the drivers send input, such as a click or a key, for flows through several screens.
- Where the `InProcess` agent package lives.
- Whether Core gets a public bytes API later, so other formats can use it.
- When the browser target comes back.
