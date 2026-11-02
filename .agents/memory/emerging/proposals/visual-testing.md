# Visual testing

Visual testing is the pair of Imprint packages that snapshot images as PNG baselines and capture screenshots of desktop and mobile apps from outside, for any UI framework.

E.g. a test starts an app with `--screen settings`, waits until its window stops changing, captures it and calls `AssertImage`. The baseline is `settings.png` in the test's folder, under the same update policies, approval and pruning as JSON. [Images and screenshots](../../../../docs/VISUAL.md) is the public reference, and this page records the design and why.

Status: MVP built on 2026-11-02, on the branch `spec/screenshot-testing-mvp`. Windows, Linux, macOS, Android and iOS capture are tested in CI by `test-visual.yml`.

## Decisions

These were decided in review, and with a second design opinion on 2026-11-02:

- AOT and trimming - every piece is Native AOT and trim compatible, with no third-party package. The specifications executable publishes both packages with ILC and trim warnings as errors.
- Platforms - Windows, Linux with X11 and Wayland, macOS, Android and the iOS simulator. The browser is left out for now.
- Any UI framework - an app is a process with a window. Nothing in the app references Imprint.
- External capture first - the test process captures through each OS's own tools and calls. A capture source setting keeps that the default forever, so in-process capture can come later without changing tests or baselines.
- Same storage as JSON - images share test folders, names, update policies, approval, artifacts and pruning with JSON.
- Strict by default - an exact comparison. Tolerance, anti-aliasing allowance and ignore regions are opt in, because together they hide small regressions. At 1920x1080, 0.1 percent of pixels is more than a missing 32x32 icon.
- Exactly one window - a capture with more than one candidate window fails and lists them, instead of guessing.

## Packages

The work splits into three layers:

| Layer | Holds |
| --- | --- |
| Core | Byte storage, the binary capture path, the capture marker, the `visual` config group. No image code. |
| `TheLithium.Imprint.Visual` | `RgbaImage`, the PNG codec, `DefaultImageComparer`, the diff image, `AssertImage` and `UpdateImage`. Depends on `TheLithium.Imprint`, so a test that only calls `AssertImage` is still instrumented. |
| `TheLithium.Imprint.Visual.Capture` | `VisualApp`, the targets, capture sources, the six drivers, stable capture, locks and cleanup. Every OS call and tool call is here. |

Visual and Capture reach Core's internals through `InternalsVisibleTo`, so Core's public API is unchanged. All four packages ship with one version, and the release pushes them in dependency order.

## Core changes

Core stores every snapshot file as bytes. Text formats also keep their strict UTF-8 text, which round-trips to the same bytes, so text fingerprints are the same as before binary support. The changes are:

| Part | Change |
| --- | --- |
| `SnapshotFile` | One file's bytes, with text for `.json` and `.txt`. |
| `SnapshotScope.CaptureBinary` | Stages bytes with a comparer, under the same name, limit and budget rules as `Capture`. |
| `IBinarySnapshotComparer` | Called only when bytes differ. Returns equal, identical, a difference and attachments such as `diff.png`. |
| `SnapshotPlan` | Under `all`, a binary capture is written unless its decoded content is identical, so a zlib change never churns baselines. |
| Journal | A journal that holds a binary file uses the marker `TheLithium.Imprint/2`, which earlier versions refuse to recover. |
| Limits | `visual.maxBytesPerImage` limits PNG files separately from `limits.maxBytesPerSnapshot`. |
| `SnapshotScope.AddDiagnostic` | Keeps files such as a capture's last frames for the failure artifacts. |
| Capture marker | `[SnapshotCaptureMethod]` makes the build task wrap, and the generator register, tests that only call `AssertImage`. Without it, pruning would see their folders as stale. |
| `IMP002` | `[SnapshotCaptureType]` makes `AssertSnapshot` on an `RgbaImage` or `Screenshot` a build error. |
| Configuration | Core's strict reader validates the `visual` group into typed settings, so the file keeps one reader. |

## Capture sources

`CaptureSource` has `Inherit` and `External`. Internally a source prepares the launch, then attaches to the running app:

Illustrative:

```csharp
interface ICaptureSource
{
    LaunchPlan Prepare(LaunchPlan plan);                                           // e.g. pass an agent endpoint
    Task<ICaptureSession> AttachAsync(IPlatformSession session, CancellationToken token);
}
```

A later `InProcess` source adds an enum value, an agent package for the app, and its own `Prepare`. The adapter stays internal until then, so its shape can still change.

## Drivers

Each driver checks its prerequisites first and fails with what to install or enable:

| Platform | Start | Capture |
| --- | --- | --- |
| Windows | `CreateProcess` suspended, inside a kill-on-close job, then resumed | The window's DWM frame, copied from the composed desktop under a machine-wide lock |
| Linux X11 | Its own Xvfb with a random MIT cookie, managed by Openbox | `XGetImage` of the frame, converted through the image's channel masks |
| Linux Wayland | Its own headless sway in a 0700 runtime folder, windows floating | `grim -g` of the container, decorations included |
| macOS | The `.app`'s executable | `screencapture -l`, after `CGPreflightScreenCaptureAccess` |
| Android | `adb` by serial, demo mode, animations off, `am start -W`, then a check that the app is resumed | `adb exec-out screencap -p` |
| iOS | `simctl` by UDID, status bar override, `launch --terminate-running-process` | `simctl io screenshot` |

Every setting a driver changes is read first and put back. A device or simulator is held for the whole session.

## Changes after the second opinion

The second design opinion changed these parts of the first spec:

- `Parameters` became each target's own `Arguments` and Android's `Extras`, so Imprint doesn't invent an argument protocol.
- The comparison defaults went from 8 channels and 0.1 percent with anti-aliasing to exact.
- The anti-aliasing check became the intensity-slope detector from V. Vysniauskas (2009), because "a neighbor has this color" excuses a one-pixel shift.
- Stable capture went from 2 frames to 3 frames held for 500 ms.
- "The largest window" became exactly one window, or a `WindowSelector`.
- `LinuxX11` and `LinuxWayland` became separate platforms, so each has its own baselines.
- Per-screenshot capture sources were dropped. The source is chosen when the app starts.
- Xvfb got cookie authentication, and Linux display servers got private folders.
- The journal got its binary marker, and `all` keeps pixel-identical images.

## Tests and CI

`test-visual.yml` runs the driver tests on each platform with `IMPRINT_VISUAL_PLATFORMS` set, so a missing tool fails the job. The general lanes set it to `none`. The fixtures are:

- Desktop - one console app that opens a half red, half colored window through Win32, GTK 4 or AppKit. The right half's color comes from its arguments, so lost arguments fail.
- Android - one Java activity, built with `javac`, `d8`, `aapt2`, `zipalign` and `apksigner` on the runner.
- iOS - one Objective-C app, built with `clang` for the simulator and signed ad hoc.

The codec, comparer, stability, selection and quoting have their own tests on every runner. The instrumented image tests check the capture marker end to end against committed baselines.

## Not decided yet

- When the browser target comes back, and whether it uses the DevTools protocol.
- Whether the Windows driver adds `Windows.Graphics.Capture`, for covered windows.
- Whether desktop drivers set the window size, or leave it to the app.
- Whether the drivers send input, for flows through several screens.
- Where the `InProcess` agent package lives.
- Whether Core gets a public bytes API, so other formats can use it.
- Whether the Android and iOS lanes stay on every PR or move to nightly.
