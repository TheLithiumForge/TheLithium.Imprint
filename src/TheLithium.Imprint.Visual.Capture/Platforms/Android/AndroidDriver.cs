using System.Text;
using TheLithium.Imprint.Configuration;
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
            await session.PrepareAsync(cancellationToken).ConfigureAwait(false);
            if (app.Apk is { } apk)
            {
                var installed = await ToolProcess.RunTextAsync(adb, ["-s", serial, "install", "-r", "-t", apk], ToolRun.Text(TimeSpan.FromMinutes(3)), cancellationToken)
                    .ConfigureAwait(false);
                if (!installed.Contains("Success", StringComparison.Ordinal))
                {
                    throw new SnapshotCaptureException($"adb install {apk} didn't succeed: {installed}");
                }
            }
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

    private static string? Adb()
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
    private bool _demo;
    private int _disposed;

    internal async Task PrepareAsync(CancellationToken cancellationToken)
    {
        foreach (var name in Animations.Append("sysui_demo_allowed"))
        {
            var value = await device.ShellAsync(["settings", "get", "global", name], cancellationToken).ConfigureAwait(false);
            _saved[name] = value == "null" ? null : value;
        }
        foreach (var name in Animations)
        {
            await device.ShellAsync(["settings", "put", "global", name, "0"], cancellationToken).ConfigureAwait(false);
        }

        await device.ShellAsync(["settings", "put", "global", "sysui_demo_allowed", "1"], cancellationToken).ConfigureAwait(false);
        _demo = true;
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
        await device.ShellAsync(["am", "force-stop", package], cancellationToken).ConfigureAwait(false);
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

        var started = await device.ShellAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (started.Contains("Error", StringComparison.Ordinal) || !started.Contains("Status: ok", StringComparison.Ordinal))
        {
            throw new SnapshotCaptureException($"am start {component} didn't succeed: {started}");
        }

        // am start -W returns once the activity is launched, not drawn. Wait until it's the resumed activity.
        var deadline = DateTime.UtcNow + timeout;
        while (!(await device.ShellAsync(["dumpsys", "activity", "activities"], cancellationToken).ConfigureAwait(false))
            .Split('\n').Any(line => line.Contains("ResumedActivity", StringComparison.Ordinal) && line.Contains(package + "/", StringComparison.Ordinal)))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new SnapshotCaptureException($"{package} didn't come to the front within {timeout.TotalSeconds:0} seconds.");
            }
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<RgbaImage> CaptureAsync(CancellationToken cancellationToken)
        => RgbaImage.FromPng(await device.ScreenshotAsync(cancellationToken).ConfigureAwait(false), SnapshotLimits.MaximumImagePixels);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await Quietly(device.ShellAsync(["am", "force-stop", package], cleanup.Token)).ConfigureAwait(false);
            if (_demo)
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
