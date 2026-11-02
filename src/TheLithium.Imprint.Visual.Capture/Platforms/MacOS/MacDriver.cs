using System.Globalization;
using TheLithium.Imprint.Configuration;
using System.Runtime.Versioning;
using TheLithium.Imprint.Visual.Sources;
using TheLithium.Imprint.Visual.Tools;

namespace TheLithium.Imprint.Visual.Platforms.MacOS;

/// <summary>
/// Starts the app's executable, finds its window in the window server's list and captures that window with
/// screencapture, which works even when the window is covered. It needs Screen Recording for the test process.
/// </summary>
internal sealed class MacDriver : IPlatformDriver
{
    private const string ScreenCapture = "/usr/sbin/screencapture";

    public Task<string?> FindProblemAsync(AppTarget target, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return Task.FromResult<string?>("macOS apps need a macOS test machine.");
        }
        if (target is MacApp app && !File.Exists(app.Path) && !Directory.Exists(app.Path))
        {
            return Task.FromResult<string?>($"The app {app.Path} doesn't exist.");
        }
        return Task.FromResult(PermissionProblem());
    }

    public async Task<IPlatformSession> StartAsync(LaunchPlan plan, SessionSettings settings, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS() || plan.Target is not MacApp app)
        {
            throw new ArgumentException("The macOS driver starts MacApp targets.", nameof(plan));
        }

        var executable = await ExecutableAsync(app.Path, cancellationToken).ConfigureAwait(false);
        var environment = app.Environment.Concat(plan.ExtraEnvironment).ToDictionary(variable => variable.Key, variable => (string?)variable.Value, StringComparer.Ordinal);
        var process = OwnedProcess.Start(executable, [.. app.Arguments, .. plan.ExtraArguments], environment, null);
        var session = new MacSession(process.Id, process, settings);
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

    public async Task<IPlatformSession> AttachAsync(int processId, SessionSettings settings, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new SnapshotCaptureException("macOS apps need a macOS test machine.");
        }
        if (PermissionProblem() is { } problem)
        {
            throw new SnapshotCaptureException(problem);
        }

        var session = new MacSession(processId, process: null, settings);
        await session.WaitForWindowAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    [SupportedOSPlatform("macos")]
    private static string? PermissionProblem()
    {
        if (!File.Exists(ScreenCapture))
        {
            return "screencapture isn't at /usr/sbin/screencapture.";
        }
        if (!MacNative.CGPreflightScreenCaptureAccess())
        {
            return "Screen Recording isn't granted to the process that runs the tests. Enable it for your terminal or IDE in System Settings, "
                + "Privacy and Security, Screen and System Audio Recording, then restart it. Without it, captures show only the desktop.";
        }
        return null;
    }

    /// <summary>The executable inside a .app folder, from its Info.plist, or the path itself.</summary>
    private static async Task<string> ExecutableAsync(string path, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path))
        {
            return path;
        }

        var plist = Path.Combine(path, "Contents", "Info.plist");
        var name = await ToolProcess.RunTextAsync("/usr/libexec/PlistBuddy", ["-c", "Print :CFBundleExecutable", plist],
            ToolRun.Text(TimeSpan.FromSeconds(10)), cancellationToken).ConfigureAwait(false);
        var executable = Path.Combine(path, "Contents", "MacOS", name);
        return File.Exists(executable) ? executable : throw new SnapshotCaptureException($"The app's executable {executable} doesn't exist.");
    }

    [SupportedOSPlatform("macos")]
    private sealed class MacSession(int processId, OwnedProcess? process, SessionSettings settings) : IPlatformSession
    {
        private uint _window;
        private int _disposed;

        internal async Task WaitForWindowAsync(CancellationToken cancellationToken)
            => _window = (await PlatformDrivers.WaitForAsync<MacWindow>(async token =>
            {
                var tree = await ProcessTree.OfAsync(processId, token).ConfigureAwait(false);
                var matching = MacNative.Windows()
                    .Where(window => tree.Contains(window.ProcessId) && window.Bounds.Width > 1 && window.Bounds.Height > 1)
                    .Where(window => settings.Window?.Matches(window.Title) ?? true).ToList();
                if (matching.Count == 1)
                {
                    return (matching[0], "");
                }

                var problem = PlatformDrivers.WindowProblem(matching.Select(window => window.Describe()).ToList(), settings.Window, settings.StartTimeout);
                if (process?.HasExited == true)
                {
                    problem += $" The app exited. Its last output:{Environment.NewLine}{process.Tail()}";
                }
                return (null, problem);
            }, settings.StartTimeout, cancellationToken).ConfigureAwait(false)).Number;

        public async Task<RgbaImage> CaptureAsync(CancellationToken cancellationToken)
        {
            if (MacNative.Windows().All(window => window.Number != _window))
            {
                await WaitForWindowAsync(cancellationToken).ConfigureAwait(false);
            }

            var file = Path.Combine(Path.GetTempPath(), $"imprint-capture-{Guid.NewGuid():N}.png");
            try
            {
                var id = _window.ToString(CultureInfo.InvariantCulture);
                await ToolProcess.RunTextAsync(ScreenCapture, ["-x", "-o", "-t", "png", "-l", id, file], ToolRun.Text(TimeSpan.FromSeconds(20)), cancellationToken)
                    .ConfigureAwait(false);
                if (!File.Exists(file))
                {
                    throw new SnapshotCaptureException("screencapture wrote no file. The window might have closed during the capture.");
                }
                return RgbaImage.FromPng(await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false), SnapshotLimits.MaximumImagePixels);
            }
            finally
            {
                File.Delete(file);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && process is not null)
            {
                await process.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
