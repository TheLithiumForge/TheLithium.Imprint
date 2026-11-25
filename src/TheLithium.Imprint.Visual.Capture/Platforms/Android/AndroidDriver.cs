using System.Text;
using TheLithium.Imprint.Visual.Sources;
using TheLithium.Imprint.Visual.Tools;

namespace TheLithium.Imprint.Visual.Platforms.Android;

/// <summary>
/// Drives one device through adb. It holds the device for the whole session, fixes the status bar and turns off
/// animations, starts the app with its extras, waits until it's in front, and captures the screen with screencap.
/// Every setting it changes is read first and put back when the session ends.
/// </summary>
internal sealed class AndroidDriver : IPlatformDriver
{
    private static readonly TimeSpan Command = TimeSpan.FromSeconds(30);

    public async Task<string?> FindProblemAsync(AppTarget target, CancellationToken cancellationToken)
    {
        if (Adb() is not { } adb)
        {
            return "adb isn't on PATH or under ANDROID_HOME/platform-tools. Install the Android SDK platform tools.";
        }
        if (target is AndroidApp { Apk: { } apk } && !File.Exists(apk))
        {
            return $"The APK {apk} doesn't exist.";
        }

        var (serial, problem) = await SerialAsync(adb, (target as AndroidApp)?.DeviceSerial, cancellationToken).ConfigureAwait(false);
        if (serial is null)
        {
            return problem;
        }

        var booted = await AdbDevice.ShellTextAsync(adb, serial, ["getprop", "sys.boot_completed"], cancellationToken).ConfigureAwait(false);
        return booted == "1" ? null : $"The device {serial} hasn't finished booting.";
    }

    public async Task<IPlatformSession> StartAsync(LaunchPlan plan, SessionSettings settings, CancellationToken cancellationToken)
    {
        if (plan.Target is not AndroidApp app)
        {
            throw new ArgumentException("The Android driver starts AndroidApp targets.", nameof(plan));
        }

        var adb = Adb() ?? throw new SnapshotCaptureException("adb isn't on PATH or under ANDROID_HOME/platform-tools.");
        var (serial, problem) = await SerialAsync(adb, app.DeviceSerial, cancellationToken).ConfigureAwait(false);
        if (serial is null)
        {
            throw new SnapshotCaptureException(problem);
        }

        var device = new AdbDevice(adb, serial);
        var hold = await MachineLock.AcquireAsync($"android-{serial}", TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
        var session = new AndroidSession(device, hold, app.PackageName);
        try
        {
            if (app.Apk is { } apk)
            {
                var installed = await ToolProcess.RunTextAsync(adb, ["-s", serial, "install", "-r", "-t", apk], ToolRun.Text(TimeSpan.FromMinutes(3)), cancellationToken)
                    .ConfigureAwait(false);
                if (!installed.Contains("Success", StringComparison.Ordinal))
                {
                    throw new SnapshotCaptureException($"adb install {apk} didn't succeed: {installed}");
                }
            }
            await session.PrepareAsync(cancellationToken).ConfigureAwait(false);
            await session.LaunchAsync(app.Activity, app.Extras.Concat(plan.ExtraEnvironment), settings.StartTimeout, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task<IPlatformSession> AttachAsync(int processId, SessionSettings settings, CancellationToken cancellationToken)
        => throw new SnapshotCaptureException("Attaching works on Windows and macOS. Start Android apps with StartAsync.");

    internal static string? Adb()
    {
        var name = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        return ToolLocator.Find("adb",
            Under(Environment.GetEnvironmentVariable("ANDROID_HOME"), name),
            Under(Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"), name));
    }

    private static string? Under(string? sdk, string name) => string.IsNullOrEmpty(sdk) ? null : Path.Combine(sdk, "platform-tools", name);

    /// <summary>The target's serial, else ANDROID_SERIAL, else the only device adb lists as ready.</summary>
    private static async Task<(string? Serial, string Problem)> SerialAsync(string adb, string? requested, CancellationToken cancellationToken)
    {
        var listed = await ToolProcess.RunTextAsync(adb, ["devices"], ToolRun.Text(Command), cancellationToken).ConfigureAwait(false);
        var ready = listed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1)
            .Select(line => line.Split('\t')).Where(parts => parts.Length == 2 && parts[1] == "device").Select(parts => parts[0]).ToList();
        var wanted = requested ?? Environment.GetEnvironmentVariable("ANDROID_SERIAL");
        if (!string.IsNullOrEmpty(wanted))
        {
            return ready.Contains(wanted, StringComparer.Ordinal) ? (wanted, "") : (null, $"The Android device {wanted} isn't connected and ready. adb lists: {listed}");
        }

        return ready.Count switch
        {
            1 => (ready[0], ""),
            0 => (null, "No Android device or emulator is connected and ready. Start an emulator or set AndroidApp.DeviceSerial."),
            _ => (null, $"Several Android devices are connected: {string.Join(", ", ready)}. Set AndroidApp.DeviceSerial or ANDROID_SERIAL to pick one.")
        };
    }
}

/// <summary>One device, addressed by serial in every command, with arguments quoted for the device's shell.</summary>
internal sealed class AdbDevice(string adb, string serial)
{
    private static readonly TimeSpan Command = TimeSpan.FromSeconds(30);

    internal string Serial => serial;

    internal Task<string> ShellAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        => ShellTextAsync(adb, serial, arguments, cancellationToken);

    internal async Task<byte[]> ScreenshotAsync(CancellationToken cancellationToken)
    {
        var result = await ToolProcess.RunAsync(adb, ["-s", serial, "exec-out", "screencap", "-p"], new(Command, 64 * 1024 * 1024, new Dictionary<string, string?>()), cancellationToken)
            .ConfigureAwait(false);
        result.EnsureSuccess(adb, ["exec-out", "screencap"]);
        return result.Output;
    }

    internal static Task<string> ShellTextAsync(string adb, string serial, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        => ToolProcess.RunTextAsync(adb, ["-s", serial, "shell", .. arguments.Select(Quote)], ToolRun.Text(Command), cancellationToken);

    /// <summary>adb joins shell arguments with spaces for the device's sh, so each one is single-quoted.</summary>
    internal static string Quote(string argument)
    {
        var quoted = new StringBuilder("'");
        foreach (var character in argument)
        {
            if (character == '\'')
            {
                quoted.Append("'\\''");
            }
            else
            {
                quoted.Append(character);
            }
        }
        return quoted.Append('\'').ToString();
    }
}

internal sealed class AndroidSession(AdbDevice device, FileStream hold, string package) : IPlatformSession
{
    private static readonly string[] Animations = ["window_animation_scale", "transition_animation_scale", "animator_duration_scale"];
    private readonly Dictionary<string, string?> _saved = new(StringComparer.Ordinal);
    private bool _enteredDemo;
    private bool _launchRequested;
    private int _disposed;

    internal async Task PrepareAsync(CancellationToken cancellationToken)
    {
        foreach (var name in Animations)
        {
            var value = await device.ShellAsync(["settings", "get", "global", name], cancellationToken).ConfigureAwait(false);
            if (value == "0")
            {
                continue;
            }
            _saved[name] = value == "null" ? null : value;
            await device.ShellAsync(["settings", "put", "global", name, "0"], cancellationToken).ConfigureAwait(false);
        }

        const string demoAllowed = "sysui_demo_allowed";
        var previous = await device.ShellAsync(["settings", "get", "global", demoAllowed], cancellationToken).ConfigureAwait(false);
        if (previous == "1")
        {
            // A broadcast can enter demo mode without changing the Settings app's demo switch.
            // Only change existing demo settings when SystemUI confirms that no demo is running.
            var state = await device.ShellAsync(["dumpsys", "activity", "service", "com.android.systemui/.SystemUIService", "DemoModeController"], cancellationToken)
                .ConfigureAwait(false);
            if (!state.Split('\n', StringSplitOptions.TrimEntries).Contains("isInDemoMode=false", StringComparer.Ordinal))
            {
                return;
            }
        }
        else
        {
            _saved[demoAllowed] = previous == "null" ? null : previous;
            await device.ShellAsync(["settings", "put", "global", demoAllowed, "1"], cancellationToken).ConfigureAwait(false);
        }
        _enteredDemo = true;
        string[][] demo =
        [
            ["command", "enter"],
            ["command", "clock", "hhmm", "1200"],
            ["command", "battery", "level", "100", "plugged", "false"],
            ["command", "network", "wifi", "show", "level", "4"],
            ["command", "network", "mobile", "show", "level", "4", "datatype", "none"],
            ["command", "notifications", "visible", "false"]
        ];
        foreach (var command in demo)
        {
            var extras = new List<string>();
            for (var i = 0; i < command.Length; i += 2)
            {
                extras.AddRange(["-e", command[i], command[i + 1]]);
            }
            await device.ShellAsync(["am", "broadcast", "-a", "com.android.systemui.demo", .. extras], cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task LaunchAsync(string? activity, IEnumerable<KeyValuePair<string, string>> extras, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var component = activity is null ? await LauncherAsync(cancellationToken).ConfigureAwait(false) : $"{package}/{activity}";
        var arguments = new List<string> { "am", "start", "-W", "-n", component };
        foreach (var extra in extras)
        {
            if (extra.Key.Length == 0 || !extra.Key.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
            {
                throw new SnapshotCaptureException($"The extra name \"{extra.Key}\" can only use letters, digits, '.', '_' and '-'.");
            }
            arguments.AddRange(["--es", extra.Key, extra.Value]);
        }

        await device.ShellAsync(["am", "force-stop", package], cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        // adb can be canceled after Android has started the app but before it reports success.
        _launchRequested = true;
        var started = await device.ShellAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (started.Contains("Error", StringComparison.Ordinal) || !started.Contains("Status: ok", StringComparison.Ordinal))
        {
            throw new SnapshotCaptureException($"am start {component} didn't succeed: {started}");
        }

        var deadline = DateTime.UtcNow + timeout;
        while (!await HasForegroundAsync(cancellationToken).ConfigureAwait(false))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new SnapshotCaptureException($"{package} didn't come to the front within {timeout.TotalSeconds:0} seconds.");
            }
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<RgbaImage> CaptureAsync(int maxPixels, CancellationToken cancellationToken)
    {
        await RequireForegroundAsync(cancellationToken).ConfigureAwait(false);
        var png = await device.ScreenshotAsync(cancellationToken).ConfigureAwait(false);
        await RequireForegroundAsync(cancellationToken).ConfigureAwait(false);
        return RgbaImage.FromPng(png, maxPixels);
    }

    private async Task RequireForegroundAsync(CancellationToken cancellationToken)
    {
        if (!await HasForegroundAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new SnapshotCaptureException($"{package} must be the only visible foreground app on the device's main display.");
        }
    }

    private async Task<bool> HasForegroundAsync(CancellationToken cancellationToken)
    {
        // visible-apps lists the focus for each display, then the visible app windows with their actual package names.
        var dump = await device.ShellAsync(["dumpsys", "window", "visible-apps"], cancellationToken).ConfigureAwait(false);
        return ShowsOnlyTarget(dump, package);
    }

    private static bool ShowsOnlyTarget(string dump, string package)
    {
        const string windows = "WINDOW MANAGER WINDOWS";
        var list = dump.IndexOf(windows, StringComparison.Ordinal);
        if (list < 0 || FocusedWindow(dump) is not { } focus)
        {
            return false;
        }
        var needsOwner = false;
        var focused = false;
        var foundTarget = false;
        var owner = $" package={package} ";
        foreach (var raw in dump.AsSpan(list).EnumerateLines())
        {
            var line = raw.Trim();
            if (line.StartsWith("Window #", StringComparison.Ordinal))
            {
                if (needsOwner)
                {
                    return false;
                }
                focused = line.EndsWith(focus, StringComparison.Ordinal);
            }
            else if (line.StartsWith("mDisplayId=", StringComparison.Ordinal))
            {
                needsOwner = line.StartsWith("mDisplayId=0 ", StringComparison.Ordinal);
            }
            else if (needsOwner && line.StartsWith("mOwnerUid=", StringComparison.Ordinal))
            {
                if (!line.Contains(owner, StringComparison.Ordinal))
                {
                    return false;
                }
                foundTarget |= focused;
                needsOwner = false;
            }
        }
        return foundTarget && !needsOwner;
    }

    private static string? FocusedWindow(string dump)
    {
        var primary = false;
        foreach (var raw in dump.AsSpan().EnumerateLines())
        {
            var line = raw.Trim();
            if (line.StartsWith("Display: mDisplayId=", StringComparison.Ordinal))
            {
                var display = line["Display: mDisplayId=".Length..];
                primary = display.SequenceEqual("0") || display.StartsWith("0 ", StringComparison.Ordinal);
            }
            else if (line.StartsWith("WINDOW MANAGER WINDOWS", StringComparison.Ordinal))
            {
                // Android 8 and 9 print the global focus after the window list.
                primary = true;
            }
            else if (primary && line.StartsWith("mCurrentFocus=", StringComparison.Ordinal))
            {
                return line["mCurrentFocus=".Length..].ToString() + ":";
            }
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            if (_launchRequested)
            {
                await Quietly(device.ShellAsync(["am", "force-stop", package], cleanup.Token)).ConfigureAwait(false);
            }
            if (_enteredDemo)
            {
                await Quietly(device.ShellAsync(["am", "broadcast", "-a", "com.android.systemui.demo", "-e", "command", "exit"], cleanup.Token)).ConfigureAwait(false);
            }
            foreach (var setting in _saved)
            {
                string[] restore = setting.Value is null
                    ? ["settings", "delete", "global", setting.Key]
                    : ["settings", "put", "global", setting.Key, setting.Value];
                await Quietly(device.ShellAsync(restore, cleanup.Token)).ConfigureAwait(false);
            }
        }
        finally
        {
            await hold.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<string> LauncherAsync(CancellationToken cancellationToken)
    {
        var resolved = await device.ShellAsync(["cmd", "package", "resolve-activity", "--brief", "-c", "android.intent.category.LAUNCHER", package], cancellationToken)
            .ConfigureAwait(false);
        var component = resolved.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (component is null || !component.StartsWith(package + "/", StringComparison.Ordinal))
        {
            throw new SnapshotCaptureException($"{package} has no launcher activity on {device.Serial}. Is it installed? Set AndroidApp.Activity or AndroidApp.Apk.");
        }
        return component;
    }

    /// <summary>Restoration keeps going after one failed step, so one broken setting doesn't leave the others changed.</summary>
    private static async Task Quietly(Task step)
    {
        try
        {
            await step.ConfigureAwait(false);
        }
        catch (SnapshotCaptureException) { }
        catch (OperationCanceledException) { }
    }
}
