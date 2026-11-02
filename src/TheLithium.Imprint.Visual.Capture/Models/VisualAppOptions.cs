namespace TheLithium.Imprint.Visual;

/// <summary>How one app session starts and captures. Unset values inherit from the project file, then the defaults.</summary>
public sealed record VisualAppOptions
{
    public CaptureSource Source { get; init; } = CaptureSource.Inherit;

    /// <summary>Which window to capture when the app has several. Without one, the app must show exactly one window.</summary>
    public WindowSelector? Window
    {
        get; init;
    }

    /// <summary>A comparison patch for every screenshot of this app. It applies over the platform layer.</summary>
    public ImageComparison? Comparison
    {
        get; init;
    }

    /// <summary>How each capture waits for the window to settle.</summary>
    public CaptureStability? Stability
    {
        get; init;
    }

    /// <summary>How long the app gets to show its window. Defaults to 30 seconds.</summary>
    public TimeSpan? StartTimeout
    {
        get; init;
    }
}

/// <summary>Picks one window by its title. Leave both null to accept the app's only window.</summary>
public sealed record WindowSelector
{
    /// <summary>The exact title.</summary>
    public string? Title
    {
        get; init;
    }

    /// <summary>Part of the title, compared ordinally.</summary>
    public string? TitleContains
    {
        get; init;
    }

    internal bool Matches(string title)
        => (Title is null || string.Equals(title, Title, StringComparison.Ordinal))
            && (TitleContains is null || title.Contains(TitleContains, StringComparison.Ordinal));
}

/// <summary>Options for one capture.</summary>
public sealed record CaptureOptions
{
    /// <summary>Overrides the app's stability settings for this capture.</summary>
    public CaptureStability? Stability
    {
        get; init;
    }

    /// <summary>Keeps only this part of the capture, in physical pixels.</summary>
    public PixelRect? Region
    {
        get; init;
    }
}

/// <summary>
/// When a capture counts as settled: <see cref="MatchingFrames"/> identical frames in a row, held for at least
/// <see cref="MinimumDuration"/>. Each property is a patch. <c>MatchingFrames = 1</c> with no minimum takes one frame.
/// </summary>
public sealed record CaptureStability
{
    /// <summary>Identical frames in a row that end the wait. Defaults to 3.</summary>
    public int? MatchingFrames
    {
        get; init;
    }

    /// <summary>The least time between the starts of two captures. Defaults to 100 ms.</summary>
    public TimeSpan? Interval
    {
        get; init;
    }

    /// <summary>How long the frames have to stay identical. Defaults to 500 ms.</summary>
    public TimeSpan? MinimumDuration
    {
        get; init;
    }

    /// <summary>The longest wait before the capture fails. Defaults to 10 seconds.</summary>
    public TimeSpan? Timeout
    {
        get; init;
    }
}
