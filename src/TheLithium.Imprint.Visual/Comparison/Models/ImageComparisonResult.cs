namespace TheLithium.Imprint.Visual;

/// <summary>A comparison outcome. <see cref="Diff"/> is written as <c>&lt;name&gt;.diff.png</c> next to the failure artifacts.</summary>
public sealed record ImageComparisonResult(bool Equal, string? Difference = null, RgbaImage? Diff = null)
{
    public static ImageComparisonResult Match { get; } = new(true);
}
