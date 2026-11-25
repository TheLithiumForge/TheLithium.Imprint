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
/// tree's top-level windows, and captures that window using Windows Graphics Capture.
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
        return Task.Run(DesktopProblem, cancellationToken);
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
        if (await Task.Run(DesktopProblem, cancellationToken).ConfigureAwait(false) is { } problem)
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
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return "Window capture needs Windows 10 version 2004 or newer.";
        }
        var desktop = OpenInputDesktop(0, false, SwitchDesktop);
        if (desktop == 0)
        {
            return "There's no unlocked desktop to capture. Run the tests in a signed-in, unlocked session.";
        }
        CloseDesktop(desktop);
        try
        {
            return InRuntime(WindowsCaptureItem.IsSupported) ? null : "Windows Graphics Capture isn't available in this desktop session.";
        }
        catch (SnapshotCaptureException error)
        {
            return error.Message;
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsSession(int processId, WindowsLaunch? launch, SessionSettings settings) : IPlatformSession
{
    private WindowsCaptureDevice? _device;
    private WindowsCaptureItem? _item;
    private WindowInfo? _target;
    private RgbaImage? _latest;
    private nint _mta;
    private int _disposed;

    internal async Task WaitForWindowAsync(CancellationToken cancellationToken)
        => _ = await FindWindowAsync(cancellationToken).ConfigureAwait(false);

    public async Task<RgbaImage> CaptureAsync(int maxPixels, CancellationToken cancellationToken)
    {
        var window = await FindWindowAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => InRuntime(() => ReadFrame(window, maxPixels, cancellationToken)), cancellationToken).ConfigureAwait(false);
    }

    private RgbaImage ReadFrame(WindowInfo window, int maxPixels, CancellationToken cancellationToken)
    {
        if (_mta == 0)
        {
            // The capture pool lives across samples, which can run on different thread-pool threads.
            Marshal.ThrowExceptionForHR(CoIncrementMTAUsage(out _mta));
        }
        _device ??= WindowsCaptureDevice.Create();
        if (_target?.Handle != window.Handle || _target.ProcessId != window.ProcessId)
        {
            ResetItem();
            _target = window;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCapturable(window);
            _item ??= WindowsCaptureItem.Open(window.Handle, _device.Device, maxPixels);
            if ((long)_item.PoolWidth * _item.PoolHeight > maxPixels)
            {
                throw new SnapshotCaptureException($"The window is {_item.PoolWidth}x{_item.PoolHeight}, more than the {maxPixels}-pixel limit.");
            }

            bool resized;
            using (var frame = _item.TakeLatestFrame())
            {
                resized = frame is not null && (frame.Width != _item.PoolWidth || frame.Height != _item.PoolHeight);
                if (frame is not null && !resized)
                {
                    _latest = _device.Read(frame.Texture, frame.Width, frame.Height, maxPixels, cancellationToken);
                }
            }
            if (resized)
            {
                // The compositor clips a resized window to the old pool. Release its frame before closing it.
                ResetItem();
                continue;
            }
            if (_latest is not null)
            {
                // A stationary window doesn't have to produce another compositor frame.
                EnsureCapturable(window);
                return _latest;
            }
            cancellationToken.WaitHandle.WaitOne(10);
        }
    }

    private static void EnsureCapturable(WindowInfo window)
    {
        if (GetWindowThreadProcessId(window.Handle, out var pid) == 0 || pid != window.ProcessId || !IsWindowVisible(window.Handle))
        {
            throw new SnapshotCaptureException("The selected window closed before its frame could be captured.");
        }
        if (IsIconic(window.Handle))
        {
            throw new SnapshotCaptureException("The selected window is minimized. Restore it before capturing.");
        }
        if (GetWindowDisplayAffinity(window.Handle, out var affinity) && affinity != 0)
        {
            throw new SnapshotCaptureException("The selected window has disabled screen capture.");
        }
    }

    private void ResetItem()
    {
        _item?.Dispose();
        _item = null;
        _latest = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (_mta != 0)
            {
                await Task.Run(() => InRuntime(() =>
                {
                    ResetItem();
                    _device?.Dispose();
                    CoDecrementMTAUsage(_mta);
                    return true;
                })).ConfigureAwait(false);
            }
        }
        finally
        {
            if (launch is not null)
            {
                await launch.StopAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<WindowInfo> FindWindowAsync(CancellationToken cancellationToken)
        => await PlatformDrivers.WaitForAsync<WindowInfo>(async token =>
        {
            var tree = await ProcessTree.OfAsync(processId, token).ConfigureAwait(false);
            var windows = TopLevelWindows(tree);
            var matching = windows.Where(window => settings.Window?.Matches(window.Title) ?? true).ToList();
            if (matching.Count == 1)
            {
                return (matching[0], "");
            }

            var problem = PlatformDrivers.WindowProblem(matching.Select(window => window.Describe()).ToList(), settings.Window, settings.StartTimeout);
            if (launch?.ExitCode is { } code && windows.Count == 0)
            {
                problem += $" The app exited with code {code}.";
            }
            return (null, problem);
        }, settings.StartTimeout, cancellationToken).ConfigureAwait(false);

    /// <summary>Visible, unowned, uncloaked top-level windows that aren't tool windows, with a frame of some size.</summary>
    private static unsafe List<WindowInfo> TopLevelWindows(IReadOnlySet<int> tree)
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
            GetWindowThreadProcessId(handle, out var owner);
            if (!tree.Contains((int)owner) || !IsWindowVisible(handle) || GetWindow(handle, Owner) != 0 || (GetWindowLong(handle, ExtendedStyle) & ToolWindow) != 0)
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

    private sealed record WindowInfo(nint Handle, int ProcessId, string Title, Rect Bounds)
    {
        internal string Describe() => $"\"{Title}\", process {ProcessId}, {Bounds.Width}x{Bounds.Height} at {Bounds.Left},{Bounds.Top}";
    }
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
                if (ResumeThread(information.Thread) == uint.MaxValue)
                {
                    throw new SnapshotCaptureException($"Windows couldn't resume the app: error {Marshal.GetLastPInvokeError()}.");
                }
                var process = Process.GetProcessById((int)information.ProcessId);
                return new(job, process);
            }
            catch
            {
                // Assignment can fail while the new process is still suspended outside the job.
                TerminateProcess(information.Process, 1);
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
