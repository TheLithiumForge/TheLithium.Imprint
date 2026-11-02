using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using TheLithium.Imprint.Visual.Sources;
using TheLithium.Imprint.Visual.Tools;
using static TheLithium.Imprint.Visual.Platforms.Windows.WindowsNative;

namespace TheLithium.Imprint.Visual.Platforms.Windows;

/// <summary>
/// Starts an executable suspended inside a job that dies with the test process, finds its window among the process
/// tree's top-level windows, and copies that window's area of the composed desktop, frame included.
/// </summary>
internal sealed class WindowsDriver : IPlatformDriver
{
    public Task<string?> FindProblemAsync(AppTarget target, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<string?>("Windows apps need a Windows test machine.");
        }
        if (target is WindowsApp app && !File.Exists(app.Executable))
        {
            return Task.FromResult<string?>($"The app {app.Executable} doesn't exist.");
        }
        return Task.FromResult(DesktopProblem());
    }

    public async Task<IPlatformSession> StartAsync(LaunchPlan plan, SessionSettings settings, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || plan.Target is not WindowsApp app)
        {
            throw new ArgumentException("The Windows driver starts WindowsApp targets.", nameof(plan));
        }

        var started = WindowsLaunch.Start(app, plan);
        var session = new WindowsSession(started.ProcessId, started, settings);
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
        if (!OperatingSystem.IsWindows())
        {
            throw new SnapshotCaptureException("Windows apps need a Windows test machine.");
        }
        if (DesktopProblem() is { } problem)
        {
            throw new SnapshotCaptureException(problem);
        }

        var session = new WindowsSession(processId, launch: null, settings);
        await session.WaitForWindowAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    [SupportedOSPlatform("windows")]
    private static string? DesktopProblem()
    {
        var desktop = OpenInputDesktop(0, false, SwitchDesktop);
        if (desktop == 0)
        {
            return "There's no unlocked desktop to capture. Run the tests in a signed-in, unlocked session.";
        }
        CloseDesktop(desktop);
        return null;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsSession(int processId, WindowsLaunch? launch, SessionSettings settings) : IPlatformSession
{
    private nint _window;
    private Point? _cursor;
    private int _disposed;

    internal async Task WaitForWindowAsync(CancellationToken cancellationToken)
        => _window = await FindWindowAsync(cancellationToken).ConfigureAwait(false);

    public async Task<RgbaImage> CaptureAsync(CancellationToken cancellationToken)
    {
        if (!IsWindowVisible(_window))
        {
            // The window was replaced, e.g. a splash screen closed. Find the app's window again.
            _window = await FindWindowAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var desktop = await MachineLock.AcquireAsync("windows-desktop", TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
        return await Task.Run(Copy, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_cursor is { } cursor)
        {
            SetCursorPos(cursor.X, cursor.Y);
        }
        if (launch is not null)
        {
            await launch.StopAsync().ConfigureAwait(false);
        }
    }

    private async Task<nint> FindWindowAsync(CancellationToken cancellationToken)
        => (await PlatformDrivers.WaitForAsync<Box>(async token =>
        {
            var tree = await ProcessTree.OfAsync(processId, token).ConfigureAwait(false);
            var windows = TopLevelWindows().Where(window => tree.Contains(window.ProcessId)).ToList();
            var matching = windows.Where(window => settings.Window?.Matches(window.Title) ?? true).ToList();
            if (matching.Count == 1)
            {
                return (new Box(matching[0].Handle), "");
            }

            var problem = PlatformDrivers.WindowProblem(matching.Select(window => window.Describe()).ToList(), settings.Window, settings.StartTimeout);
            if (launch?.ExitCode is { } code && windows.Count == 0)
            {
                problem += $" The app exited with code {code}.";
            }
            return (null, problem);
        }, settings.StartTimeout, cancellationToken).ConfigureAwait(false)).Handle;

    /// <summary>Visible, unowned, uncloaked top-level windows that aren't tool windows, with a frame of some size.</summary>
    private static unsafe List<WindowInfo> TopLevelWindows()
    {
        var handles = new List<nint>();
        var state = GCHandle.Alloc(handles);
        try
        {
            EnumWindows(&Collect, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        var windows = new List<WindowInfo>();
        foreach (var handle in handles)
        {
            if (!IsWindowVisible(handle) || GetWindow(handle, Owner) != 0 || (GetWindowLongPtr(handle, ExtendedStyle) & (nint)ToolWindow) != 0)
            {
                continue;
            }

            var cloaked = 0;
            if (DwmGetWindowAttribute(handle, Cloaked, &cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                continue;
            }

            var bounds = Bounds(handle);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                continue;
            }

            GetWindowThreadProcessId(handle, out var owner);
            windows.Add(new(handle, (int)owner, Title(handle), bounds));
        }
        return windows;
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint window, nint state)
    {
        if (GCHandle.FromIntPtr(state).Target is List<nint> handles)
        {
            handles.Add(window);
        }
        return 1;
    }

    /// <summary>The frame as DWM draws it, in physical pixels. The caller's thread is per-monitor aware.</summary>
    private static unsafe Rect Bounds(nint window)
    {
        var previous = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        try
        {
            Rect bounds = default;
            DwmGetWindowAttribute(window, ExtendedFrameBounds, &bounds, sizeof(Rect));
            return bounds;
        }
        finally
        {
            SetThreadDpiAwarenessContext(previous);
        }
    }

    /// <summary>Raises the window, moves the pointer off it, and copies its frame from the composed desktop.</summary>
    private unsafe RgbaImage Copy()
    {
        var previous = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        try
        {
            if (IsIconic(_window))
            {
                ShowWindow(_window, Restore);
            }
            SetWindowPos(_window, Topmost, 0, 0, 0, 0, NoMove | NoSize | NoActivate | ShowWindowFlag);
            SetForegroundWindow(_window);
            try
            {
                Rect bounds = default;
                DwmGetWindowAttribute(_window, ExtendedFrameBounds, &bounds, sizeof(Rect));
                MovePointerAway(bounds);
                DwmFlush();
                Thread.Sleep(50);
                DwmFlush();
                DwmGetWindowAttribute(_window, ExtendedFrameBounds, &bounds, sizeof(Rect));
                EnsureOnScreen(bounds);
                EnsureUncovered(bounds);
                return CopyScreen(bounds);
            }
            finally
            {
                SetWindowPos(_window, NotTopmost, 0, 0, 0, 0, NoMove | NoSize | NoActivate);
            }
        }
        finally
        {
            SetThreadDpiAwarenessContext(previous);
        }
    }

    private void MovePointerAway(Rect bounds)
    {
        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        _cursor ??= cursor;
        if (cursor.X < bounds.Left || cursor.X >= bounds.Right || cursor.Y < bounds.Top || cursor.Y >= bounds.Bottom)
        {
            return;
        }

        // A corner of the virtual screen outside the window keeps hover effects out of the capture.
        var left = GetSystemMetrics(VirtualLeft);
        var top = GetSystemMetrics(VirtualTop);
        var right = left + GetSystemMetrics(VirtualWidth) - 1;
        var bottom = top + GetSystemMetrics(VirtualHeight) - 1;
        var x = bounds.Left > left ? left : right;
        var y = bounds.Top > top ? top : bottom;
        SetCursorPos(x, y);
    }

    private static void EnsureOnScreen(Rect bounds)
    {
        var left = GetSystemMetrics(VirtualLeft);
        var top = GetSystemMetrics(VirtualTop);
        if (bounds.Left < left || bounds.Top < top
            || bounds.Right > left + GetSystemMetrics(VirtualWidth) || bounds.Bottom > top + GetSystemMetrics(VirtualHeight))
        {
            throw new SnapshotCaptureException(
                $"The window at {bounds.Left},{bounds.Top} to {bounds.Right},{bounds.Bottom} is partly off screen, so its pixels can't be copied. Place it fully on a monitor.");
        }
    }

    private void EnsureUncovered(Rect bounds)
    {
        var inset = Math.Min(4, Math.Min(bounds.Width, bounds.Height) / 2);
        Span<Point> probes =
        [
            new() { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 },
            new() { X = bounds.Left + inset, Y = bounds.Top + inset },
            new() { X = bounds.Right - 1 - inset, Y = bounds.Top + inset },
            new() { X = bounds.Left + inset, Y = bounds.Bottom - 1 - inset },
            new() { X = bounds.Right - 1 - inset, Y = bounds.Bottom - 1 - inset }
        ];
        foreach (var probe in probes)
        {
            var hit = GetAncestor(WindowFromPoint(probe), Root);
            if (hit != 0 && hit != _window)
            {
                throw new SnapshotCaptureException($"Another window covers the app at {probe.X},{probe.Y}: \"{Title(hit)}\". Close it, or run the tests on a clear desktop.");
            }
        }
    }

    private static unsafe RgbaImage CopyScreen(Rect bounds)
    {
        var width = bounds.Width;
        var height = bounds.Height;
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32
        };
        var bitmap = CreateDIBSection(screen, &header, 0, out var bits, 0, 0);
        try
        {
            if (bitmap == 0 || bits == 0)
            {
                throw new SnapshotCaptureException("Windows couldn't create a bitmap for the capture.");
            }

            var old = SelectObject(memory, bitmap);
            var copied = BitBlt(memory, 0, 0, width, height, screen, bounds.Left, bounds.Top, SourceCopy | CaptureLayered);
            SelectObject(memory, old);
            if (!copied)
            {
                throw new SnapshotCaptureException("Windows couldn't copy the window's pixels from the desktop.");
            }

            var stride = width * 4;
            return RgbaImage.FromBgra(width, height, new ReadOnlySpan<byte>((void*)bits, stride * height), stride, opaque: true);
        }
        finally
        {
            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    private sealed record WindowInfo(nint Handle, int ProcessId, string Title, Rect Bounds)
    {
        internal string Describe() => $"\"{Title}\", process {ProcessId}, {Bounds.Width}x{Bounds.Height} at {Bounds.Left},{Bounds.Top}";
    }

    private sealed record Box(nint Handle);
}

/// <summary>
/// A process created suspended, assigned to a kill-on-close job, then resumed, so nothing it starts can escape the
/// job even if the test process dies.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsLaunch
{
    private readonly nint _job;
    private readonly Process _process;

    private WindowsLaunch(nint job, Process process)
    {
        _job = job;
        _process = process;
    }

    internal int ProcessId => _process.Id;

    internal int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    internal static unsafe WindowsLaunch Start(WindowsApp app, LaunchPlan plan)
    {
        var job = CreateJobObject(0, 0);
        if (job == 0)
        {
            throw new SnapshotCaptureException($"Windows couldn't create a job object: error {Marshal.GetLastPInvokeError()}.");
        }

        try
        {
            var limits = new JobLimits { LimitFlags = JobKillOnClose };
            if (!SetInformationJobObject(job, JobExtendedLimitInformation, &limits, sizeof(JobLimits)))
            {
                throw new SnapshotCaptureException($"Windows couldn't configure the job object: error {Marshal.GetLastPInvokeError()}.");
            }

            var commandLine = WindowsCommandLine.For(app.Executable, [.. app.Arguments, .. plan.ExtraArguments]) + '\0';
            var environment = EnvironmentBlock([.. app.Environment, .. plan.ExtraEnvironment]);
            var startup = new StartupInfo { Size = (uint)sizeof(StartupInfo) };
            ProcessInformation information;
            fixed (char* command = commandLine)
            fixed (char* block = environment)
            {
                if (!CreateProcess(app.Executable, command, 0, 0, false, CreateSuspended | CreateUnicodeEnvironment, block,
                    app.WorkingDirectory ?? Path.GetDirectoryName(Path.GetFullPath(app.Executable)), &startup, out information))
                {
                    throw new SnapshotCaptureException($"Windows couldn't start {app.Executable}: error {Marshal.GetLastPInvokeError()}.");
                }
            }

            try
            {
                if (!AssignProcessToJobObject(job, information.Process))
                {
                    throw new SnapshotCaptureException($"Windows couldn't put the app in its job object: error {Marshal.GetLastPInvokeError()}.");
                }
                var process = Process.GetProcessById((int)information.ProcessId);
                ResumeThread(information.Thread);
                return new(job, process);
            }
            catch
            {
                TerminateJobObject(job, 1);
                throw;
            }
            finally
            {
                CloseHandle(information.Thread);
                CloseHandle(information.Process);
            }
        }
        catch
        {
            CloseHandle(job);
            throw;
        }
    }

    /// <summary>Asks the app's windows to close, waits up to 5 seconds, then ends everything in the job.</summary>
    internal async Task StopAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                foreach (var window in await WindowsOfAsync().ConfigureAwait(false))
                {
                    PostMessage(window, Close, 0, 0);
                }
                using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await _process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }
        }
        finally
        {
            TerminateJobObject(_job, 1);
            CloseHandle(_job);
            _process.Dispose();
        }
    }

    private async Task<List<nint>> WindowsOfAsync()
    {
        var tree = await ProcessTree.OfAsync(_process.Id, CancellationToken.None).ConfigureAwait(false);
        return AllWindows().Where(handle => GetWindowThreadProcessId(handle, out var owner) != 0 && tree.Contains((int)owner) && GetWindow(handle, Owner) == 0).ToList();
    }

    private static unsafe List<nint> AllWindows()
    {
        var handles = new List<nint>();
        var state = GCHandle.Alloc(handles);
        try
        {
            EnumWindows(&Gather, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }
        return handles;
    }

    [UnmanagedCallersOnly]
    private static int Gather(nint window, nint state)
    {
        if (GCHandle.FromIntPtr(state).Target is List<nint> handles)
        {
            handles.Add(window);
        }
        return 1;
    }

    /// <summary>The test process's environment with the app's variables over it, sorted the way Windows expects.</summary>
    private static string EnvironmentBlock(IEnumerable<KeyValuePair<string, string>> extra)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                variables[key] = value;
            }
        }
        foreach (var variable in extra)
        {
            variables[variable.Key] = variable.Value;
        }

        var block = new StringBuilder();
        foreach (var variable in variables)
        {
            block.Append(variable.Key).Append('=').Append(variable.Value).Append('\0');
        }
        return block.Append('\0').ToString();
    }
}
