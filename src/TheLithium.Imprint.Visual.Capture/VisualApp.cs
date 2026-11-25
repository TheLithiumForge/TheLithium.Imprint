using TheLithium.Imprint.Configuration;
using TheLithium.Imprint.Visual.Platforms;
using TheLithium.Imprint.Visual.Sources;

namespace TheLithium.Imprint.Visual;

/// <summary>
/// One app running on a platform, for screenshot tests. Start it, capture it, and dispose it to stop the app and put
/// back anything the session changed, such as an emulator's settings.
/// </summary>
public sealed class VisualApp : IAsyncDisposable
{
    private static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromSeconds(30);
    private readonly IPlatformSession _session;
    private readonly ICaptureSession _capture;
    private readonly VisualAppOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task? _disposal;

    private VisualApp(VisualPlatform platform, CaptureSource source, IPlatformSession session, ICaptureSession capture, VisualAppOptions options)
    {
        Platform = platform;
        Source = source;
        _session = session;
        _capture = capture;
        _options = options;
    }

    public VisualPlatform Platform
    {
        get;
    }

    public CaptureSource Source
    {
        get;
    }

    /// <summary>Whether this run drives the target's platform. Use it to skip a platform row through the test framework.</summary>
    public static async Task<VisualCheck> CheckAsync(AppTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return await PlatformSelection.CheckAsync(target, PlatformDrivers.For(target.Platform), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts the app and waits until its window shows. A missing prerequisite fails with what to install or enable.</summary>
    public static async Task<VisualApp> StartAsync(AppTarget target, VisualAppOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        options ??= new();
        var driver = PlatformDrivers.For(target.Platform);
        var check = await PlatformSelection.CheckAsync(target, driver, cancellationToken).ConfigureAwait(false);
        if (check.Status != VisualCheckStatus.Ready)
        {
            throw new SnapshotCaptureException(check.Problem ?? $"{PlatformNames.Of(target.Platform)} can't be driven in this run.");
        }

        var source = CaptureSources.For(ResolveSource(options.Source));
        if (!source.Supports(target.Platform))
        {
            throw new SnapshotCaptureException($"The {source.Kind} capture source doesn't support {PlatformNames.Of(target.Platform)}.");
        }

        var plan = source.Prepare(LaunchPlan.For(target));
        var settings = new SessionSettings(options.Window, options.StartTimeout ?? DefaultStartTimeout);
        var session = await driver.StartAsync(plan, settings, cancellationToken).ConfigureAwait(false);
        return await AttachSourceAsync(target.Platform, source, session, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Attaches to a desktop app that's already running, e.g. one the test framework started. The app keeps running
    /// when this session is disposed. Windows and macOS only.
    /// </summary>
    public static async Task<VisualApp> AttachAsync(VisualPlatform platform, int processId, VisualAppOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (platform is not (VisualPlatform.Windows or VisualPlatform.MacOS))
        {
            throw new SnapshotCaptureException($"Attaching works on Windows and macOS. Start {PlatformNames.Of(platform)} apps with StartAsync.");
        }

        options ??= new();
        var source = CaptureSources.For(ResolveSource(options.Source));
        var settings = new SessionSettings(options.Window, options.StartTimeout ?? DefaultStartTimeout);
        var session = await PlatformDrivers.For(platform).AttachAsync(processId, settings, cancellationToken).ConfigureAwait(false);
        return await AttachSourceAsync(platform, source, session, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Captures the window once it has settled, as <see cref="CaptureStability"/> describes.</summary>
    public async Task<Screenshot> CaptureAsync(CaptureOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposing();
        var stability = ResolvedStability.Defaults;
        var maxPixels = SnapshotLimits.DefaultImagePixels;
        if (Snapshots.Active is { } scope)
        {
            stability = stability.Apply(scope.ResolvedSettings.Visual.Stability);
            maxPixels = scope.ResolvedSettings.Visual.MaxPixelsPerImage;
        }
        stability = stability.Apply(_options.Stability).Apply(options?.Stability);

        var entered = false;
        Task lastRead = Task.CompletedTask;
        try
        {
            var image = await StableCapture.CaptureAsync(StartFrame, stability, options?.Region, cancellationToken).ConfigureAwait(false);
            return new(image, Platform, Source, _options.Comparison);
        }
        finally
        {
            // A timed-out native read can still be using the session. It keeps the gate until it returns.
            _ = ReleaseAfterReadAsync();
        }

        Task<RgbaImage> StartFrame(CancellationToken token)
        {
            // Some drivers enter a synchronous OS call before returning a Task.
            var frame = Task.Run(() => ReadFrameAsync(token), token);
            lastRead = frame;
            return frame;
        }

        async Task<RgbaImage> ReadFrameAsync(CancellationToken token)
        {
            if (!entered)
            {
                await _gate.WaitAsync(token).ConfigureAwait(false);
                entered = true;
                ThrowIfDisposing();
            }
            return await _capture.CaptureAsync(maxPixels, token).ConfigureAwait(false);
        }

        async Task ReleaseAfterReadAsync()
        {
            await lastRead.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (entered)
            {
                _gate.Release();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            return new(_disposal ??= DisposeSessionsAsync());
        }
    }

    private void ThrowIfDisposing()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
        }
    }

    private async Task DisposeSessionsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                await _capture.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await _session.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            // Calls that were queued before disposal still need to enter the gate and see the disposed state.
            _gate.Release();
        }
    }

    internal static async Task<VisualApp> AttachSourceAsync(VisualPlatform platform, ICaptureSource source, IPlatformSession session,
        VisualAppOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var capture = await source.AttachAsync(session, cancellationToken).ConfigureAwait(false);
            return new(platform, source.Kind, session, capture, options);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The app's source, else the project file's, else External.</summary>
    private static CaptureSource ResolveSource(CaptureSource requested)
    {
        if (requested != CaptureSource.Inherit)
        {
            return requested;
        }

        var configured = Snapshots.Active?.ResolvedSettings.Visual.CaptureSource ?? VisualCaptureSource.External;
        return configured switch
        {
            VisualCaptureSource.External => CaptureSource.External,
            _ => throw new ArgumentOutOfRangeException(nameof(requested), configured, null)
        };
    }
}
