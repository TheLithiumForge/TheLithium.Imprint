using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TheLithium.Imprint.Visual.Sources;
using TheLithium.Imprint.Visual.Tools;
using static TheLithium.Imprint.Visual.Platforms.Linux.X11Native;

namespace TheLithium.Imprint.Visual.Platforms.Linux;

/// <summary>
/// Runs the app on an Xvfb display of its own, managed by Openbox so the window gets a frame like on a desktop, and
/// copies the frame's area of the screen with XGetImage. The desktop you're logged in to is never touched.
/// </summary>
internal sealed class X11Driver : IPlatformDriver
{
    internal const int Width = 1920;
    internal const int Height = 1080;

    public Task<string?> FindProblemAsync(AppTarget target, CancellationToken cancellationToken)
        => Task.FromResult(Problem(target));

    public async Task<IPlatformSession> StartAsync(LaunchPlan plan, SessionSettings settings, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || plan.Target is not LinuxApp app)
        {
            throw new ArgumentException("The X11 driver starts LinuxApp targets.", nameof(plan));
        }

        var display = await X11Display.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var environment = LinuxEnvironment.For(app, plan, new Dictionary<string, string?>
            {
                ["DISPLAY"] = display.Name,
                ["XAUTHORITY"] = display.AuthorityFile,
                ["WAYLAND_DISPLAY"] = null,
                ["GDK_BACKEND"] = "x11",
                ["QT_QPA_PLATFORM"] = "xcb"
            });
            var process = OwnedProcess.Start(app.Executable, [.. app.Arguments, .. plan.ExtraArguments], environment, app.WorkingDirectory);
            var session = new X11Session(display, process, settings);
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
        catch
        {
            await display.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task<IPlatformSession> AttachAsync(int processId, SessionSettings settings, CancellationToken cancellationToken)
        => throw new SnapshotCaptureException("Attaching works on Windows and macOS. Start Linux apps with StartAsync.");

    private static string? Problem(AppTarget target)
    {
        if (!OperatingSystem.IsLinux())
        {
            return "Linux apps need a Linux test machine.";
        }
        if (target is LinuxApp app && !File.Exists(app.Executable))
        {
            return $"The app {app.Executable} doesn't exist.";
        }
        if (ToolLocator.Find("Xvfb") is null)
        {
            return "Xvfb isn't on PATH. Install it, e.g. apt install xvfb.";
        }
        if (ToolLocator.Find("openbox") is null)
        {
            return "Openbox isn't on PATH. Install it, e.g. apt install openbox. It gives windows a frame, so captures look like a desktop.";
        }
        if (!NativeLibrary.TryLoad("libX11.so.6", out var library))
        {
            return "libX11.so.6 can't be loaded. Install it, e.g. apt install libx11-6.";
        }
        NativeLibrary.Free(library);
        return null;
    }
}

/// <summary>
/// An Xvfb server with Openbox on a free display number, reserved by a machine-wide lock. The server accepts only
/// clients with its random cookie, kept in an Xauthority file only this user can read.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class X11Display : IAsyncDisposable
{
    private readonly FileStream _reservation;
    private readonly OwnedProcess _server;
    private readonly OwnedProcess _manager;
    private readonly string _directory;

    private X11Display(string name, X11Authority authority, FileStream reservation, OwnedProcess server, OwnedProcess manager, nint connection)
    {
        Name = name;
        AuthorityFile = authority.File;
        _directory = authority.Directory;
        _reservation = reservation;
        _server = server;
        _manager = manager;
        Connection = connection;
    }

    internal string Name
    {
        get;
    }

    /// <summary>The Xauthority file with this display's cookie, for XAUTHORITY.</summary>
    internal string AuthorityFile
    {
        get;
    }

    /// <summary>This session's own connection, used only by its capture calls.</summary>
    internal nint Connection
    {
        get;
    }

    internal static async Task<X11Display> StartAsync(CancellationToken cancellationToken)
    {
        Initialize();
        for (var number = 90; number < 200; number++)
        {
            if (File.Exists($"/tmp/.X{number}-lock") || File.Exists($"/tmp/.X11-unix/X{number}"))
            {
                continue;
            }
            var reservation = MachineLock.TryAcquire($"x11-display-{number}");
            if (reservation is null)
            {
                continue;
            }

            var name = string.Create(CultureInfo.InvariantCulture, $":{number}");
            var authority = X11Authority.Create(number);
            var server = OwnedProcess.Start(ToolLocator.Find("Xvfb") ?? "Xvfb",
                [name, "-auth", authority.File, "-screen", "0", $"{X11Driver.Width}x{X11Driver.Height}x24", "-nolisten", "tcp", "-noreset"],
                new Dictionary<string, string?>(), null);
            var connection = await ConnectAsync(name, authority, server, cancellationToken).ConfigureAwait(false);
            if (connection == 0)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                await reservation.DisposeAsync().ConfigureAwait(false);
                authority.Delete();
                continue;
            }

            OwnedProcess? manager = null;
            try
            {
                manager = OwnedProcess.Start(ToolLocator.Find("openbox") ?? "openbox", ["--sm-disable"],
                    new Dictionary<string, string?> { ["DISPLAY"] = name, ["XAUTHORITY"] = authority.File }, null);
                await WaitForManagerAsync(connection, manager, cancellationToken).ConfigureAwait(false);
                return new(name, authority, reservation, server, manager, connection);
            }
            catch
            {
                XCloseDisplay(connection);
                if (manager is not null)
                {
                    await manager.DisposeAsync().ConfigureAwait(false);
                }
                await server.DisposeAsync().ConfigureAwait(false);
                await reservation.DisposeAsync().ConfigureAwait(false);
                authority.Delete();
                throw;
            }
        }
        throw new SnapshotCaptureException("No free X display between :90 and :199 for Xvfb.");
    }

    public async ValueTask DisposeAsync()
    {
        XCloseDisplay(Connection);
        await _manager.DisposeAsync().ConfigureAwait(false);
        await _server.DisposeAsync().ConfigureAwait(false);
        await _reservation.DisposeAsync().ConfigureAwait(false);
        new X11Authority(_directory, AuthorityFile, []).Delete();
    }

    /// <summary>Connects once Xvfb accepts clients, or returns 0 if it exited, e.g. because the display was taken.</summary>
    private static async Task<nint> ConnectAsync(string name, X11Authority authority, OwnedProcess server, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (server.HasExited)
            {
                return 0;
            }
            var connection = Open(name, authority.Cookie);
            if (connection != 0)
            {
                return connection;
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        throw new SnapshotCaptureException($"Xvfb didn't accept connections on {name} within 15 seconds. Its last output:{Environment.NewLine}{server.Tail()}");
    }

    /// <summary>Openbox announces itself through _NET_SUPPORTING_WM_CHECK on the root window.</summary>
    private static async Task WaitForManagerAsync(nint connection, OwnedProcess manager, CancellationToken cancellationToken)
    {
        var check = XInternAtom(connection, "_NET_SUPPORTING_WM_CHECK", 0);
        var root = XDefaultRootWindow(connection);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (Cardinals(connection, root, check).Length == 0)
        {
            manager.EnsureRunning("it managed the display");
            if (DateTime.UtcNow >= deadline)
            {
                throw new SnapshotCaptureException("Openbox didn't start managing the Xvfb display within 15 seconds.");
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }
}

[SupportedOSPlatform("linux")]
internal sealed class X11Session(X11Display display, OwnedProcess process, SessionSettings settings) : IPlatformSession
{
    private nuint _frame;
    private int _disposed;

    internal async Task WaitForWindowAsync(CancellationToken cancellationToken)
        => _frame = (await PlatformDrivers.WaitForAsync<Box>(async token =>
        {
            var tree = await ProcessTree.OfAsync(process.Id, token).ConfigureAwait(false);
            var matching = Windows(tree).Where(window => settings.Window?.Matches(window.Title) ?? true).ToList();
            if (matching.Count == 1)
            {
                return (new Box(matching[0].Frame), "");
            }

            var problem = PlatformDrivers.WindowProblem(matching.Select(window => window.Describe()).ToList(), settings.Window, settings.StartTimeout);
            if (process.HasExited)
            {
                problem += $" The app exited. Its last output:{Environment.NewLine}{process.Tail()}";
            }
            return (null, problem);
        }, settings.StartTimeout, cancellationToken).ConfigureAwait(false)).Frame;

    public unsafe Task<RgbaImage> CaptureAsync(CancellationToken cancellationToken)
    {
        var connection = display.Connection;
        if (XGetWindowAttributes(connection, _frame, out var frame) == 0 || frame.MapState != IsViewable)
        {
            throw new SnapshotCaptureException("The app's window closed or was hidden during the capture.");
        }

        XRaiseWindow(connection, _frame);
        XSync(connection, 0);
        var border = frame.BorderWidth;
        var left = Math.Max(0, frame.X);
        var top = Math.Max(0, frame.Y);
        var width = Math.Min(frame.Width + 2 * border, X11Driver.Width - left);
        var height = Math.Min(frame.Height + 2 * border, X11Driver.Height - top);
        var image = XGetImage(connection, XDefaultRootWindow(connection), left, top, (uint)width, (uint)height, nuint.MaxValue, ZPixmap);
        if (image is null)
        {
            throw new SnapshotCaptureException("The X server returned no image for the window.");
        }

        try
        {
            return Task.FromResult(Convert(image));
        }
        finally
        {
            image->DestroyImage(image);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        await process.DisposeAsync().ConfigureAwait(false);
        await display.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Normal client windows from _NET_CLIENT_LIST whose process is in the app's tree, with their frames.</summary>
    private List<WindowInfo> Windows(IReadOnlySet<int> tree)
    {
        var connection = display.Connection;
        var root = XDefaultRootWindow(connection);
        var pid = XInternAtom(connection, "_NET_WM_PID", 0);
        var name = XInternAtom(connection, "_NET_WM_NAME", 0);
        var type = XInternAtom(connection, "_NET_WM_WINDOW_TYPE", 0);
        var normal = XInternAtom(connection, "_NET_WM_WINDOW_TYPE_NORMAL", 0);
        var dialog = XInternAtom(connection, "_NET_WM_WINDOW_TYPE_DIALOG", 0);
        var windows = new List<WindowInfo>();
        foreach (var client in Cardinals(connection, root, XInternAtom(connection, "_NET_CLIENT_LIST", 0)))
        {
            var owner = Cardinals(connection, client, pid);
            if (owner.Length == 0 || !tree.Contains((int)owner[0]))
            {
                continue;
            }

            var types = Cardinals(connection, client, type);
            if (types.Length != 0 && !types.Contains(normal) && !types.Contains(dialog))
            {
                continue;
            }

            var frame = client;
            for (var parent = Parent(connection, frame); parent != 0 && parent != root; parent = Parent(connection, frame))
            {
                frame = parent;
            }
            if (XGetWindowAttributes(connection, frame, out var attributes) == 0 || attributes.MapState != IsViewable || attributes.Width <= 1 || attributes.Height <= 1)
            {
                continue;
            }
            windows.Add(new(frame, (int)owner[0], Text(connection, client, name), attributes.Width, attributes.Height));
        }
        return windows;
    }

    private sealed record WindowInfo(nuint Frame, int ProcessId, string Title, int Width, int Height)
    {
        internal string Describe() => $"\"{Title}\", process {ProcessId}, {Width}x{Height}";
    }

    private sealed record Box(nuint Frame);
}

/// <summary>
/// A random MIT-MAGIC-COOKIE-1 in an Xauthority file. The folder is created for this user only and the file with mode
/// 0600, so no other local user can connect to the display, read its screen or send it input.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed record X11Authority(string Directory, string File, byte[] Cookie)
{
    private const ushort AnyHost = 0xFFFF;

    internal static X11Authority Create(int display)
    {
        var directory = Path.Combine("/tmp", "imprint-x11-" + Guid.NewGuid().ToString("N")[..12]);
        System.IO.Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var cookie = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var file = Path.Combine(directory, "Xauthority");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        };
        using (var output = new FileStream(file, options))
        {
            // One entry: family, address, display number, protocol name and cookie, each length-prefixed big-endian.
            Span<byte> family = stackalloc byte[2];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(family, AnyHost);
            output.Write(family);
            Field(output, []);
            Field(output, System.Text.Encoding.ASCII.GetBytes(display.ToString(CultureInfo.InvariantCulture)));
            Field(output, "MIT-MAGIC-COOKIE-1"u8);
            Field(output, cookie);
        }
        return new(directory, file, cookie);
    }

    internal void Delete()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Field(Stream output, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)value.Length);
        output.Write(length);
        output.Write(value);
    }
}

/// <summary>The environment for a Linux app: fixed scale, the display, then the target's and the source's variables.</summary>
internal static class LinuxEnvironment
{
    internal static Dictionary<string, string?> For(LinuxApp app, LaunchPlan plan, IReadOnlyDictionary<string, string?> display)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GDK_SCALE"] = "1",
            ["GDK_DPI_SCALE"] = "1",
            ["QT_SCALE_FACTOR"] = "1",
            ["QT_AUTO_SCREEN_SCALE_FACTOR"] = "0",
            ["QT_ENABLE_HIGHDPI_SCALING"] = "0",
            ["NO_AT_BRIDGE"] = "1"
        };
        foreach (var variable in display)
        {
            environment[variable.Key] = variable.Value;
        }
        foreach (var variable in app.Environment.Concat(plan.ExtraEnvironment))
        {
            environment[variable.Key] = variable.Value;
        }
        return environment;
    }
}
