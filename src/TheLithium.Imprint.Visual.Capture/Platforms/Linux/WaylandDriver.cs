using System.Globalization;
using TheLithium.Imprint.Configuration;
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
        if (!OperatingSystem.IsLinux())
        {
            return Task.FromResult<string?>("Linux apps need a Linux test machine.");
        }
        if (target is LinuxApp app && !File.Exists(app.Executable))
        {
            return Task.FromResult<string?>($"The app {app.Executable} doesn't exist.");
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
    private const string Config = """
        output HEADLESS-1 resolution 1920x1080 position 0 0 scale 1
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
        var config = Path.Combine(runtime, "sway.config");
        await File.WriteAllTextAsync(config, Config, cancellationToken).ConfigureAwait(false);
        var sway = OwnedProcess.Start(ToolLocator.Find("sway") ?? "sway", ["--config", config], new Dictionary<string, string?>
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

        try
        {
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
            await sway.DisposeAsync().ConfigureAwait(false);
            Delete(runtime);
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

        await _sway.DisposeAsync().ConfigureAwait(false);
        Delete(RuntimeDirectory);
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
    private int _windowProcess;
    private int _disposed;

    internal async Task WaitForWindowAsync(CancellationToken cancellationToken)
        => _windowProcess = (await FindAsync(cancellationToken).ConfigureAwait(false)).ProcessId;

    public async Task<RgbaImage> CaptureAsync(CancellationToken cancellationToken)
    {
        var window = await FindAsync(cancellationToken).ConfigureAwait(false);
        await compositor.MessageAsync([string.Create(CultureInfo.InvariantCulture, $"[pid={window.ProcessId}] focus")], cancellationToken).ConfigureAwait(false);
        var geometry = string.Create(CultureInfo.InvariantCulture, $"{window.X},{window.Y} {window.Width}x{window.Height}");
        var run = new ToolRun(TimeSpan.FromSeconds(20), MaxPng, compositor.ClientEnvironment);
        var result = await ToolProcess.RunAsync(ToolLocator.Find("grim") ?? "grim", ["-s", "1", "-t", "png", "-g", geometry, "-"], run, cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess("grim", ["-g", geometry]);
        return RgbaImage.FromPng(result.Output, SnapshotLimits.MaximumImagePixels);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        await process.DisposeAsync().ConfigureAwait(false);
        await compositor.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<SwayWindow> FindAsync(CancellationToken cancellationToken)
        => await PlatformDrivers.WaitForAsync<SwayWindow>(async token =>
        {
            var tree = await ProcessTree.OfAsync(process.Id, token).ConfigureAwait(false);
            var result = await compositor.MessageAsync(["-t", "get_tree", "-r"], token).ConfigureAwait(false);
            result.EnsureSuccess("swaymsg", ["-t", "get_tree"]);
            using var document = JsonDocument.Parse(result.Output);
            var windows = new List<SwayWindow>();
            Collect(document.RootElement, tree, windows);
            var matching = windows.Where(window => settings.Window?.Matches(window.Title) ?? true).ToList();
            if (_windowProcess != 0)
            {
                // Once found, keep capturing the same app's window even if another one of its processes shows one.
                matching = matching.Where(window => window.ProcessId == _windowProcess).ToList();
            }
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

    /// <summary>Visible windows of the app's process tree, from both tiled and floating nodes.</summary>
    private static void Collect(JsonElement node, IReadOnlySet<int> tree, List<SwayWindow> windows)
    {
        if (node.TryGetProperty("pid", out var pid) && pid.ValueKind == JsonValueKind.Number && tree.Contains(pid.GetInt32())
            && node.TryGetProperty("visible", out var visible) && visible.ValueKind == JsonValueKind.True)
        {
            var rect = node.GetProperty("rect");
            var decoration = node.TryGetProperty("deco_rect", out var deco) ? deco.GetProperty("height").GetInt32() : 0;
            var title = node.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? "" : "";
            windows.Add(new(pid.GetInt32(), title, rect.GetProperty("x").GetInt32(), rect.GetProperty("y").GetInt32() - decoration,
                rect.GetProperty("width").GetInt32(), rect.GetProperty("height").GetInt32() + decoration));
        }

        foreach (var children in new[] { "nodes", "floating_nodes" })
        {
            if (node.TryGetProperty(children, out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in list.EnumerateArray())
                {
                    Collect(child, tree, windows);
                }
            }
        }
    }

    private sealed record SwayWindow(int ProcessId, string Title, int X, int Y, int Width, int Height)
    {
        internal string Describe() => $"\"{Title}\", process {ProcessId}, {Width}x{Height} at {X},{Y}";
    }
}
