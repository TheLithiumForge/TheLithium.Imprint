namespace TheLithium.Imprint.Visual;

/// <summary>What a test does with one platform row in this run.</summary>
public enum VisualCheckStatus
{
    /// <summary>The run drives this platform and its prerequisites are there.</summary>
    Ready = 1,

    /// <summary>The run doesn't drive this platform. Skip the row through the test framework.</summary>
    NotRequested = 2,

    /// <summary>The run drives this platform, but a prerequisite is missing. Starting the app fails with <see cref="VisualCheck.Problem"/>.</summary>
    Missing = 3
}

/// <summary>Whether this run can drive a platform, and why not.</summary>
public sealed record VisualCheck(VisualCheckStatus Status, string? Problem = null);
