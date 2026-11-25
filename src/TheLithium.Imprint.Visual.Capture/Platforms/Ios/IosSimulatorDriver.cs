using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using TheLithium.Imprint.Visual.Sources;
using TheLithium.Imprint.Visual.Tools;

namespace TheLithium.Imprint.Visual.Platforms.Ios;

/// <summary>
/// Drives one simulator through simctl, by UDID. It holds the simulator for the whole session, fixes the status bar,
/// launches the app with its arguments, and captures the simulator's screen. A simulator it booted is shut down again.
/// </summary>
internal sealed class IosSimulatorDriver : IPlatformDriver
{
    private const string Xcrun = "/usr/bin/xcrun";

    public async Task<string?> FindProblemAsync(AppTarget target, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return "iOS simulators need a macOS test machine with Xcode.";
        }
        if (target is IosSimulatorApp { AppBundle: { } bundle } && !Directory.Exists(bundle))
        {
            return $"The app {bundle} doesn't exist.";
        }

        try
        {
            var (simulator, problem) = await SimulatorAsync((target as IosSimulatorApp)?.SimulatorId, cancellationToken).ConfigureAwait(false);
            return simulator is null ? problem : null;
        }
        catch (SnapshotCaptureException error)
        {
            return $"simctl isn't available. Install Xcode and select it with xcode-select. {error.Message}";
        }
    }

    public async Task<IPlatformSession> StartAsync(LaunchPlan plan, SessionSettings settings, CancellationToken cancellationToken)
    {
        if (plan.Target is not IosSimulatorApp app)
        {
            throw new ArgumentException("The iOS driver starts IosSimulatorApp targets.", nameof(plan));
        }

        var (simulator, problem) = await SimulatorAsync(app.SimulatorId, cancellationToken).ConfigureAwait(false);
        if (simulator is null)
        {
            throw new SnapshotCaptureException(problem);
        }

        var hold = await MachineLock.AcquireAsync($"ios-{simulator.Udid}", TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
        var session = new IosSession(simulator.Udid, hold, app.BundleId);
        try
        {
            // The simulator can shut down while this session waits for the previous one to release its lock.
            var (current, issue) = await SimulatorAsync(simulator.Udid, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                throw new SnapshotCaptureException(issue);
            }
            await session.PrepareAsync(app.AppBundle, current.State == "Shutdown", cancellationToken).ConfigureAwait(false);
            var environment = app.Environment.Concat(plan.ExtraEnvironment)
                .ToDictionary(variable => "SIMCTL_CHILD_" + variable.Key, variable => (string?)variable.Value, StringComparer.Ordinal);
            await session.LaunchAsync([.. app.Arguments, .. plan.ExtraArguments], environment, settings.StartTimeout, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task<IPlatformSession> AttachAsync(int processId, SessionSettings settings, CancellationToken cancellationToken)
        => throw new SnapshotCaptureException("Attaching works on Windows and macOS. Start iOS apps with StartAsync.");

    internal static Task<ToolResult> SimctlAsync(IReadOnlyList<string> arguments, ToolRun run, CancellationToken cancellationToken)
        => ToolProcess.RunAsync(Xcrun, ["simctl", .. arguments], run, cancellationToken);

    /// <summary>The simulator with this UDID or name, else the only booted one.</summary>
    private static async Task<(Simulator? Simulator, string Problem)> SimulatorAsync(string? requested, CancellationToken cancellationToken)
    {
        var listed = await SimctlAsync(["list", "devices", "-j"], ToolRun.Text(TimeSpan.FromSeconds(60)), cancellationToken).ConfigureAwait(false);
        listed.EnsureSuccess(Xcrun, ["simctl", "list", "devices"]);
        using var document = JsonDocument.Parse(listed.Output);
        var simulators = new List<Simulator>();
        foreach (var runtime in document.RootElement.GetProperty("devices").EnumerateObject())
        {
            if (!runtime.Name.StartsWith("com.apple.CoreSimulator.SimRuntime.iOS-", StringComparison.Ordinal))
            {
                continue;
            }
            foreach (var device in runtime.Value.EnumerateArray())
            {
                if (device.TryGetProperty("isAvailable", out var available) && available.ValueKind == JsonValueKind.False)
                {
                    continue;
                }
                simulators.Add(new(device.GetProperty("udid").GetString() ?? "", device.GetProperty("name").GetString() ?? "", device.GetProperty("state").GetString() ?? ""));
            }
        }

        if (!string.IsNullOrEmpty(requested))
        {
            var matching = simulators.Where(simulator => simulator.Udid == requested || simulator.Name == requested).ToList();
            return matching.Count switch
            {
                1 => (matching[0], ""),
                0 => (null, $"No available simulator has the UDID or name \"{requested}\"."),
                _ => (null, $"Several simulators are named \"{requested}\". Use a UDID: {string.Join(", ", matching.Select(simulator => simulator.Udid))}.")
            };
        }

        var booted = simulators.Where(simulator => simulator.State == "Booted").ToList();
        return booted.Count switch
        {
            1 => (booted[0], ""),
            0 => (null, "No simulator is booted. Boot one, or set IosSimulatorApp.SimulatorId."),
            _ => (null, $"Several simulators are booted: {string.Join(", ", booted.Select(simulator => simulator.Name))}. Set IosSimulatorApp.SimulatorId to pick one.")
        };
    }

    private sealed record Simulator(string Udid, string Name, string State);
}

internal sealed class IosSession(string udid, FileStream hold, string bundleId) : IPlatformSession
{
    private static readonly ToolRun Short = ToolRun.Text(TimeSpan.FromSeconds(60));
    private readonly IosForeground _foreground = new(udid, bundleId);
    private bool _statusBar;
    private bool _booted;
    private Process? _app;
    private int _disposed;

    internal async Task PrepareAsync(string? appBundle, bool bootHere, CancellationToken cancellationToken)
    {
        if (bootHere)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (await IosSimulatorDriver.SimctlAsync(["boot", udid], Short, CancellationToken.None).ConfigureAwait(false)).EnsureSuccess("simctl", ["boot", udid]);
            _booted = true;
            cancellationToken.ThrowIfCancellationRequested();
        }
        (await IosSimulatorDriver.SimctlAsync(["bootstatus", udid], ToolRun.Text(TimeSpan.FromMinutes(5)), cancellationToken).ConfigureAwait(false))
            .EnsureSuccess("simctl", ["bootstatus", udid]);

        var status = await IosSimulatorDriver.SimctlAsync(["status_bar", udid, "list"], Short, cancellationToken).ConfigureAwait(false);
        status.EnsureSuccess("simctl", ["status_bar", udid, "list"]);
        // A cleared list contains just its heading and underline. Keep any existing or unrecognized overrides.
        if (status.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) is
            ["Current Status Bar Overrides:", var line] && line.Length > 0 && line.All(c => c == '='))
        {
            _statusBar = true;
            (await IosSimulatorDriver.SimctlAsync(["status_bar", udid, "override", "--time", "9:41", "--dataNetwork", "wifi", "--wifiMode", "active", "--wifiBars", "3",
                "--cellularMode", "active", "--cellularBars", "4", "--batteryState", "charged", "--batteryLevel", "100"], Short, cancellationToken).ConfigureAwait(false))
                .EnsureSuccess("simctl", ["status_bar", udid, "override"]);
        }

        if (appBundle is not null)
        {
            (await IosSimulatorDriver.SimctlAsync(["install", udid, appBundle], ToolRun.Text(TimeSpan.FromMinutes(3)), cancellationToken).ConfigureAwait(false))
                .EnsureSuccess("simctl", ["install", udid, appBundle]);
        }
        await _foreground.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task LaunchAsync(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string?> environment, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var run = new ToolRun(TimeSpan.FromMinutes(2), 1024 * 1024, environment);
        // Once simctl submits a launch, obtain its PID before honoring cancellation so cleanup can stop that instance.
        cancellationToken.ThrowIfCancellationRequested();
        var launched = await IosSimulatorDriver.SimctlAsync(["launch", "--terminate-running-process", udid, bundleId, .. arguments], run, CancellationToken.None).ConfigureAwait(false);
        launched.EnsureSuccess("simctl", ["launch", udid, bundleId]);
        // CoreSimulator apps run as host processes. Keep the specific launch so a restarted bundle can't replace it.
        var pid = launched.Text.Split(':').LastOrDefault()?.Trim();
        if (!int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
        {
            throw new SnapshotCaptureException($"simctl launch didn't report a process for {bundleId}: {launched.Text}");
        }
        try
        {
            _app = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            throw new SnapshotCaptureException($"{bundleId} exited before reaching the foreground.");
        }
        cancellationToken.ThrowIfCancellationRequested();

        using var launch = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        launch.CancelAfter(timeout);
        try
        {
            while (!await _foreground.IsForegroundAsync(launch.Token).ConfigureAwait(false))
            {
                EnsureAppRunning();
                await Task.Delay(100, launch.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SnapshotCaptureException($"{bundleId} didn't reach the foreground within {timeout.TotalSeconds:0.#} seconds.");
        }
    }

    public async Task<RgbaImage> CaptureAsync(int maxPixels, CancellationToken cancellationToken)
    {
        EnsureAppRunning();
        await _foreground.RequireForegroundAsync(cancellationToken).ConfigureAwait(false);
        var file = _foreground.FrameFile;
        try
        {
            var frame = await IosSimulatorDriver.SimctlAsync(["io", udid, "screenshot", "--type=png", file], Short, cancellationToken).ConfigureAwait(false);
            frame.EnsureSuccess("simctl", ["io", udid, "screenshot"]);
            await _foreground.RequireForegroundAsync(cancellationToken).ConfigureAwait(false);
            EnsureAppRunning();
            return RgbaImage.FromPng(await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false), maxPixels);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private void EnsureAppRunning()
    {
        if (_app is null || _app.HasExited)
        {
            throw new SnapshotCaptureException($"The launched {bundleId} exited and is no longer the foreground app on {udid}.");
        }
    }

    private async Task CleanAsync(IReadOnlyList<string> command)
    {
        try
        {
            await IosSimulatorDriver.SimctlAsync(command, Short, CancellationToken.None).ConfigureAwait(false);
        }
        catch (SnapshotCaptureException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _foreground.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (_app is { HasExited: false })
                {
                    await CleanAsync(["terminate", udid, bundleId]).ConfigureAwait(false);
                }
                if (_statusBar)
                {
                    await CleanAsync(["status_bar", udid, "clear"]).ConfigureAwait(false);
                }
                if (_booted)
                {
                    await CleanAsync(["shutdown", udid]).ConfigureAwait(false);
                }
            }
            finally
            {
                _app?.Dispose();
                await hold.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
