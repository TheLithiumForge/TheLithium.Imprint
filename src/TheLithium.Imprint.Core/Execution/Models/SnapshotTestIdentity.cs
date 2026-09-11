namespace TheLithium.Imprint;

/// <summary>An explicit identity for a custom runner or relocated executable. Ordinary package consumers use generated identities.</summary>
/// <param name="ProjectDirectory">Absolute project root, also the base for relative configuration paths.</param>
/// <param name="SourceFile">Test source file inside the project root; relative paths are project-relative.</param>
/// <param name="Suite">Stable suite folder name.</param>
/// <param name="Test">Stable method folder name.</param>
/// <param name="Case">Optional stable discriminator for a parameterized invocation.</param>
/// <param name="Variant">Optional target or configuration discriminator.</param>
/// <param name="LogicalId">Optional unique identity used to detect overlapping test directories within a process.</param>
public sealed record SnapshotTestIdentity(
    string ProjectDirectory,
    string SourceFile,
    string Suite,
    string Test,
    string? Case = null,
    string? Variant = null,
    string? LogicalId = null);
