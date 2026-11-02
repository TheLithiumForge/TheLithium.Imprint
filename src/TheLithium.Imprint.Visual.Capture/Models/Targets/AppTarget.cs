namespace TheLithium.Imprint.Visual;

/// <summary>
/// What to start and where. An app is any process with a window, whatever framework draws it. Arguments and
/// environment reach the app the platform's own way.
/// </summary>
public abstract record AppTarget
{
    private protected AppTarget()
    {
    }

    public abstract VisualPlatform Platform
    {
        get;
    }
}

/// <summary>A Windows executable. Its window is the one top-level window of the process or of a process it starts.</summary>
public sealed record WindowsApp(string Executable) : AppTarget
{
    public override VisualPlatform Platform => VisualPlatform.Windows;

    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Variables added to the test process's environment for the app.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    public string? WorkingDirectory
    {
        get; init;
    }
}

/// <summary>
/// A Linux executable, run on a display server the capture starts for it: Xvfb with a window manager for X11, or a
/// headless compositor for Wayland. The desktop you're logged in to is never used.
/// </summary>
public sealed record LinuxApp(string Executable) : AppTarget
{
    public override VisualPlatform Platform => Display == LinuxDisplay.Wayland ? VisualPlatform.LinuxWayland : VisualPlatform.LinuxX11;

    public LinuxDisplay Display { get; init; } = LinuxDisplay.X11;

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    public string? WorkingDirectory
    {
        get; init;
    }
}

/// <summary>A macOS app, given as a <c>.app</c> folder or an executable.</summary>
public sealed record MacApp(string Path) : AppTarget
{
    public override VisualPlatform Platform => VisualPlatform.MacOS;

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}

/// <summary>An Android app on a running device or emulator. Its screen is captured whole, with the system bars fixed.</summary>
public sealed record AndroidApp(string PackageName) : AppTarget
{
    public override VisualPlatform Platform => VisualPlatform.Android;

    /// <summary>An APK to install first, or null when the app is already installed.</summary>
    public string? Apk
    {
        get; init;
    }

    /// <summary>The activity to start, e.g. <c>.MainActivity</c>. Null starts the launcher activity.</summary>
    public string? Activity
    {
        get; init;
    }

    /// <summary>The adb serial, e.g. <c>emulator-5554</c>. Null uses <c>ANDROID_SERIAL</c>, then the only connected device.</summary>
    public string? DeviceSerial
    {
        get; init;
    }

    /// <summary>String extras on the start intent, the way an app on Android receives parameters.</summary>
    public IReadOnlyDictionary<string, string> Extras { get; init; } = new Dictionary<string, string>();
}

/// <summary>An iOS app on a simulator. Its screen is captured whole, with the status bar fixed.</summary>
public sealed record IosSimulatorApp(string BundleId) : AppTarget
{
    public override VisualPlatform Platform => VisualPlatform.IOS;

    /// <summary>A built <c>.app</c> to install first, or null when the app is already installed.</summary>
    public string? AppBundle
    {
        get; init;
    }

    /// <summary>The simulator's UDID or name. Null uses the only booted simulator.</summary>
    public string? SimulatorId
    {
        get; init;
    }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Variables for the app, passed through <c>SIMCTL_CHILD_</c>.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}

/// <summary>The display server a Linux app runs on.</summary>
public enum LinuxDisplay
{
    X11 = 1,
    Wayland = 2
}
