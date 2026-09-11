namespace TheLithium.Imprint;

/// <summary>Configures a test method or suite without changing its ordinary test body.</summary>
/// <remarks>
/// A class policy is inherited by its test methods; a method policy wins.
/// Applying this attribute also establishes a lifetime for tests that reach captures indirectly, for example through a delegate.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class SnapshotSettingsAttribute : Attribute
{
    /// <summary>Overrides the project policy for this method or suite. All updates are still staged until the method succeeds.</summary>
    public SnapshotUpdate Update { get; set; } = SnapshotUpdate.Inherit;
    /// <summary>Optional stable folder name: a suite on a class or a test on a method. Defaults to the C# type or method name.</summary>
    public string? Name
    {
        get; set;
    }
}
