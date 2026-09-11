namespace TheLithium.Imprint;

/// <summary>An explicit identity for a custom runner or relocated executable. Ordinary package consumers use generated identities.</summary>
public sealed record SnapshotTestIdentity
{
    /// <summary>Creates an identity with required project, source, suite, and test names.</summary>
    /// <param name="ProjectDirectory">Absolute project root, also the base for relative configuration paths.</param>
    /// <param name="SourceFile">Test source file inside the project root; relative paths are project-relative.</param>
    /// <param name="Suite">Stable suite folder name.</param>
    /// <param name="Test">Stable method folder name.</param>
    /// <param name="Case">Optional stable discriminator for a parameterized invocation.</param>
    /// <param name="Variant">Optional target or configuration discriminator.</param>
    /// <param name="LogicalId">Optional unique identity used to detect overlapping test directories within a process.</param>
    public SnapshotTestIdentity(string ProjectDirectory, string SourceFile, string Suite, string Test,
        string? Case = null, string? Variant = null, string? LogicalId = null)
    {
        this.ProjectDirectory = Required(ProjectDirectory, nameof(ProjectDirectory));
        this.SourceFile = Required(SourceFile, nameof(SourceFile));
        this.Suite = Required(Suite, nameof(Suite));
        this.Test = Required(Test, nameof(Test));
        this.Case = Optional(Case, nameof(Case));
        this.Variant = Optional(Variant, nameof(Variant));
        this.LogicalId = Optional(LogicalId, nameof(LogicalId));
    }

    /// <summary>Absolute project root, also the base for relative configuration paths.</summary>
    public string ProjectDirectory
    {
        get; init;
    }
    /// <summary>Test source file inside the project root; relative paths are project-relative.</summary>
    public string SourceFile
    {
        get; init;
    }
    /// <summary>Stable suite folder name.</summary>
    public string Suite
    {
        get; init;
    }
    /// <summary>Stable method folder name.</summary>
    public string Test
    {
        get; init;
    }
    /// <summary>Optional stable discriminator for a parameterized invocation.</summary>
    public string? Case
    {
        get; init;
    }
    /// <summary>Optional target or configuration discriminator.</summary>
    public string? Variant
    {
        get; init;
    }
    /// <summary>Optional unique identity used to detect overlapping test directories within a process.</summary>
    public string? LogicalId
    {
        get; init;
    }

    /// <summary>Deconstructs the identity into its stable naming parts.</summary>
    public void Deconstruct(out string projectDirectory, out string sourceFile, out string suite, out string test,
        out string? @case, out string? variant, out string? logicalId)
        => (projectDirectory, sourceFile, suite, test, @case, variant, logicalId)
            = (ProjectDirectory, SourceFile, Suite, Test, Case, Variant, LogicalId);

    private static string Required(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        return value;
    }

    private static string? Optional(string? value, string parameter)
    {
        if (value is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        }

        return value;
    }
}
