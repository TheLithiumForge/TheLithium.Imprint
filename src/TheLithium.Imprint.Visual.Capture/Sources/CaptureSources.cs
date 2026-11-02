using TheLithium.Imprint.Visual.Platforms;

namespace TheLithium.Imprint.Visual.Sources;

/// <summary>
/// What a capture source does around an app session. A source prepares the launch before the app starts, so a later
/// in-process source can pass the app an endpoint to connect back to, then attaches to the running app.
/// </summary>
internal interface ICaptureSource
{
    CaptureSource Kind
    {
        get;
    }

    bool Supports(VisualPlatform platform);

    LaunchPlan Prepare(LaunchPlan plan);

    Task<ICaptureSession> AttachAsync(IPlatformSession session, CancellationToken cancellationToken);
}

/// <summary>One source attached to one running app.</summary>
internal interface ICaptureSession : IAsyncDisposable
{
    Task<RgbaImage> CaptureAsync(CancellationToken cancellationToken);
}

/// <summary>The app as the platform starts it, plus what a source adds, such as arguments for an agent.</summary>
internal sealed record LaunchPlan(AppTarget Target, IReadOnlyList<string> ExtraArguments, IReadOnlyDictionary<string, string> ExtraEnvironment)
{
    internal static LaunchPlan For(AppTarget target) => new(target, [], new Dictionary<string, string>());
}

/// <summary>The test process captures through the OS's own tools and calls. Every platform supports it.</summary>
internal sealed class ExternalCaptureSource : ICaptureSource
{
    internal static ExternalCaptureSource Instance { get; } = new();

    public CaptureSource Kind => CaptureSource.External;

    public bool Supports(VisualPlatform platform) => true;

    public LaunchPlan Prepare(LaunchPlan plan) => plan;

    public Task<ICaptureSession> AttachAsync(IPlatformSession session, CancellationToken cancellationToken)
        => Task.FromResult<ICaptureSession>(new Session(session));

    private sealed class Session(IPlatformSession platform) : ICaptureSession
    {
        public Task<RgbaImage> CaptureAsync(CancellationToken cancellationToken) => platform.CaptureAsync(cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal static class CaptureSources
{
    internal static ICaptureSource For(CaptureSource source) => source switch
    {
        CaptureSource.External => ExternalCaptureSource.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Use a capture source other than Inherit.")
    };
}
