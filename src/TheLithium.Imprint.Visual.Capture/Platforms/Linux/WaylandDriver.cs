using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using TheLithium.Imprint.Visual.Sources;
using TheLithium.Imprint.Visual.Tools;

namespace TheLithium.Imprint.Visual.Platforms.Linux;

/// <summary>
/// Runs the app on a headless sway compositor of its own, in a private runtime folder, and captures the window's
/// area with grim. sway floats every window at its own size, so a window looks the same as on a desktop.
/// </summary>
internal sealed class WaylandDriver : IPlatformDriver
{
    public Task<string?> FindProblemAsync(AppTarget target, CancellationToken cancellationToken)
    {
        if (LinuxEnvironment.FindProblem(target) is { } problem)
        {
            return Task.FromResult<string?>(problem);
        }
        foreach (var tool in new[] { "sway", "swaymsg", "grim" })
        {
            if (ToolLocator.Find(tool) is null)
            {
                return Task.FromResult<string?>($"{tool} isn't on PATH. Install sway and grim, e.g. apt install sway grim.");
            }
        }
        return Task.FromResult<string?>(null);
    }

    public async Task<IPlatformSession> StartAsync(LaunchPlan plan, SessionSettings settings, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || plan.Target is not LinuxApp app)
        {
            throw new ArgumentException("The Wayland driver starts LinuxApp targets.", nameof(plan));
        }

        var compositor = await SwayCompositor.StartAsync(cancellationToken).ConfigureAwait(false);
        WaylandSession session;
        try
        {
            var environment = LinuxEnvironment.For(app, plan, new Dictionary<string, string?>
            {
                ["XDG_RUNTIME_DIR"] = compositor.RuntimeDirectory,
                ["WAYLAND_DISPLAY"] = compositor.Display,
                ["DISPLAY"] = null,
                ["GDK_BACKEND"] = "wayland",
                ["QT_QPA_PLATFORM"] = "wayland"
            });
            var process = OwnedProcess.Start(app.Executable, [.. app.Arguments, .. plan.ExtraArguments], environment, app.WorkingDirectory);
            session = new WaylandSession(compositor, process, settings);
        }
        catch
        {
            await compositor.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        // From here the session owns the compositor and the app, and disposing it stops both.
        try
        {
            await session.WaitForWindowAsync(cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task<IPlatformSession> AttachAsync(int processId, SessionSettings settings, CancellationToken cancellationToken)
        => throw new SnapshotCaptureException("Attaching works on Windows and macOS. Start Linux apps with StartAsync.");
}

/// <summary>A headless sway with one 1920x1080 output at scale 1, software rendering and no Xwayland.</summary>
[SupportedOSPlatform("linux")]
internal sealed class SwayCompositor : IAsyncDisposable
{
    private static readonly string Config = $"""
        output HEADLESS-1 resolution {LinuxEnvironment.Width}x{LinuxEnvironment.Height} position 0 0 scale 1
        default_border normal 1
        default_floating_border normal 1
        font pango:monospace 10
        for_window [all] floating enable
        focus_follows_mouse no
        seat * hide_cursor 100
        xwayland disable
        """;

    private readonly OwnedProcess _sway;
    private int _disposed;

    private SwayCompositor(string runtimeDirectory, string display, string socket, OwnedProcess sway)
    {
        RuntimeDirectory = runtimeDirectory;
        Display = display;
        Socket = socket;
        _sway = sway;
    }

    internal string RuntimeDirectory
    {
        get;
    }

    internal string Display
    {
        get;
    }

    /// <summary>sway's IPC socket, for swaymsg.</summary>
    internal string Socket
    {
        get;
    }

    internal static async Task<SwayCompositor> StartAsync(CancellationToken cancellationToken)
    {
        // A short path under /tmp, because a Unix socket path is limited to about 100 bytes.
        var runtime = Path.Combine("/tmp", "imprint-wl-" + Guid.NewGuid().ToString("N")[..12]);
        // Only this user can reach the compositor's sockets in it.
        Directory.CreateDirectory(runtime, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        OwnedProcess? sway = null;
        try
        {
            var config = Path.Combine(runtime, "sway.config");
            await File.WriteAllTextAsync(config, Config, cancellationToken).ConfigureAwait(false);
            sway = OwnedProcess.Start(ToolLocator.Find("sway") ?? "sway", ["--config", config], new Dictionary<string, string?>
            {
                ["XDG_RUNTIME_DIR"] = runtime,
                ["WLR_BACKENDS"] = "headless",
                ["WLR_HEADLESS_OUTPUTS"] = "1",
                ["WLR_LIBINPUT_NO_DEVICES"] = "1",
                ["WLR_RENDERER"] = "pixman",
                ["WAYLAND_DISPLAY"] = null,
                ["DISPLAY"] = null,
                ["SWAYSOCK"] = null
            }, null);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (true)
            {
                sway.EnsureRunning("its sockets were ready");
                var display = Directory.EnumerateFileSystemEntries(runtime, "wayland-*").Select(Path.GetFileName)
                    .FirstOrDefault(name => name is not null && !name.EndsWith(".lock", StringComparison.Ordinal));
                var socket = Directory.EnumerateFileSystemEntries(runtime, "sway-ipc.*.sock").FirstOrDefault();
                if (display is not null && socket is not null)
                {
                    var compositor = new SwayCompositor(runtime, display, socket, sway);
                    var version = await compositor.MessageAsync(["-t", "get_version"], cancellationToken).ConfigureAwait(false);
                    if (version.ExitCode == 0)
                    {
                        return compositor;
                    }
                }
                if (DateTime.UtcNow >= deadline)
                {
                    throw new SnapshotCaptureException($"sway didn't get ready within 20 seconds. Its last output:{Environment.NewLine}{sway.Tail()}");
                }
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            try
            {
                if (sway is not null)
                {
                    await sway.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                Delete(runtime);
            }
            throw;
        }
    }

    internal Task<ToolResult> MessageAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        => ToolProcess.RunAsync(ToolLocator.Find("swaymsg") ?? "swaymsg", ["-s", Socket, .. arguments], ToolRun.Text(TimeSpan.FromSeconds(10)), cancellationToken);

    internal Dictionary<string, string?> ClientEnvironment => new()
    {
        ["XDG_RUNTIME_DIR"] = RuntimeDirectory,
        ["WAYLAND_DISPLAY"] = Display
    };

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _sway.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            Delete(RuntimeDirectory);
        }
    }

    private static void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

[SupportedOSPlatform("linux")]
internal sealed class WaylandSession(SwayCompositor compositor, OwnedProcess process, SessionSettings settings) : IPlatformSession
{
    private const int MaxPng = 64 * 1024 * 1024;
    private int _windowId;
    private int _disposed;

    internal async Task WaitForWindowAsync(CancellationToken cancellationToken)
        => _windowId = (await FindAsync(cancellationToken).ConfigureAwait(false)).Id;

    public async Task<RgbaImage> CaptureAsync(int maxPixels, CancellationToken cancellationToken)
    {
        var focus = string.Create(CultureInfo.InvariantCulture, $"[con_id={_windowId}] focus");
        (await compositor.MessageAsync([focus], cancellationToken).ConfigureAwait(false)).EnsureSuccess("swaymsg", [focus]);
        var window = await SelectedWindowAsync(cancellationToken).ConfigureAwait(false);
        var bounds = window.Bounds;
        LinuxEnvironment.CheckBounds(bounds, maxPixels);
        var geometry = string.Create(CultureInfo.InvariantCulture, $"{bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}");
        var run = new ToolRun(TimeSpan.FromSeconds(20), MaxPng, compositor.ClientEnvironment);
        var result = await ToolProcess.RunAsync(ToolLocator.Find("grim") ?? "grim", ["-s", "1", "-t", "png", "-g", geometry, "-"], run, cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess("grim", ["-g", geometry]);
        if ((await SelectedWindowAsync(cancellationToken).ConfigureAwait(false)).Bounds != bounds)
        {
            throw new SnapshotCaptureException("The selected Wayland window moved or resized during the capture.");
        }
        return RgbaImage.FromPng(result.Output, maxPixels);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        try
        {
            await process.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await compositor.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<SwayWindow> FindAsync(CancellationToken cancellationToken)
        => await PlatformDrivers.WaitForAsync<SwayWindow>(async token =>
        {
            var tree = await process.MembersAsync(token).ConfigureAwait(false);
            var windows = await WindowsAsync(token).ConfigureAwait(false);
            var matching = windows.Where(window => tree.Contains(window.ProcessId) && (settings.Window?.Matches(window.Title) ?? true)).ToList();
            if (matching.Count == 1)
            {
                return (matching[0], "");
            }

            var problem = PlatformDrivers.WindowProblem(matching.Select(window => window.Describe()).ToList(), settings.Window, settings.StartTimeout);
            if (process.HasExited)
            {
                problem += $" The app exited. Its last output:{Environment.NewLine}{process.Tail()}";
            }
            return (null, problem);
        }, settings.StartTimeout, cancellationToken).ConfigureAwait(false);

    private async Task<SwayWindow> SelectedWindowAsync(CancellationToken cancellationToken)
    {
        var windows = await WindowsAsync(cancellationToken).ConfigureAwait(false);
        var matching = windows.Where(window => settings.Window?.Matches(window.Title) ?? true).ToList();
        if (matching.Count > 1)
        {
            var tree = await process.MembersAsync(cancellationToken).ConfigureAwait(false);
            matching.RemoveAll(window => !tree.Contains(window.ProcessId));
            if (matching.Count > 1)
            {
                throw new SnapshotCaptureException(PlatformDrivers.WindowProblem(matching.Select(window => window.Describe()).ToList(), settings.Window, settings.StartTimeout));
            }
        }
        return matching.Find(window => window.Id == _windowId && window.Focused)
            ?? throw new SnapshotCaptureException("The selected Wayland window closed, no longer matched the selector or lost focus during the capture.");
    }

    private async Task<List<SwayWindow>> WindowsAsync(CancellationToken cancellationToken)
    {
        var result = await compositor.MessageAsync(["-t", "get_tree", "-r"], cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess("swaymsg", ["-t", "get_tree"]);
        using var document = JsonDocument.Parse(result.Output);
        var windows = new List<SwayWindow>();
        Collect(document.RootElement, windows);
        return windows;
    }

    /// <summary>The private compositor's visible windows, including tiled and floating nodes.</summary>
    private static void Collect(JsonElement node, List<SwayWindow> windows)
    {
        if (node.TryGetProperty("pid", out var pid) && pid.ValueKind == JsonValueKind.Number
            && node.TryGetProperty("visible", out var visible) && visible.ValueKind == JsonValueKind.True)
        {
            var rect = node.GetProperty("rect");
            var decoration = node.TryGetProperty("deco_rect", out var deco) ? deco.GetProperty("height").GetInt32() : 0;
            var title = node.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? "" : "";
            var bounds = new PixelRect(rect.GetProperty("x").GetInt32(), rect.GetProperty("y").GetInt32() - decoration,
                rect.GetProperty("width").GetInt32(), rect.GetProperty("height").GetInt32() + decoration);
            windows.Add(new(node.GetProperty("id").GetInt32(), pid.GetInt32(), title, bounds, node.GetProperty("focused").GetBoolean()));
        }

        foreach (var children in new[] { "nodes", "floating_nodes" })
        {
            if (node.TryGetProperty(children, out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in list.EnumerateArray())
                {
                    Collect(child, windows);
                }
            }
        }
    }

    private sealed record SwayWindow(int Id, int ProcessId, string Title, PixelRect Bounds, bool Focused)
    {
        internal string Describe() => $"\"{Title}\", process {ProcessId}, {Bounds.Width}x{Bounds.Height} at {Bounds.X},{Bounds.Y}";
    }
}
