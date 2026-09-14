namespace TheLithium.Imprint.Configuration;

/// <summary>Combines generated source identity with explicit integration overrides and checkout relocation.</summary>
internal static class TestIdentityResolver
{
    internal static SnapshotTestIdentity Resolve(SnapshotTestOptions options, SnapshotDescriptor? descriptor)
    {
        var original = options.Identity ?? FromDescriptor(descriptor);
        var project = Environment.GetEnvironmentVariable("IMPRINT_PROJECT_ROOT") ?? original.ProjectDirectory;
        if (string.IsNullOrWhiteSpace(project) || !Path.IsPathRooted(project))
        {
            throw new SnapshotConfigurationException("The test-project root must be an absolute path.");
        }
        project = Path.GetFullPath(project);
        if (!Directory.Exists(project))
        {
            throw new SnapshotConfigurationException(
                $"Test-project root does not exist: {project}. Set IMPRINT_PROJECT_ROOT for a relocated checkout or supply an explicit identity.");
        }

        var sourceFile = original.SourceFile;
        if (!Path.IsPathRooted(sourceFile))
        {
            sourceFile = Path.Combine(project, sourceFile);
        }
        else if (options.Identity is null && project != original.ProjectDirectory)
        {
            sourceFile = Path.Combine(project, Path.GetRelativePath(original.ProjectDirectory, sourceFile));
        }

        var caseKey = options.Case ?? original.Case;
        var variant = options.Variant ?? original.Variant;
        if (options.Identity is null && descriptor is { RequiresCase: true } && string.IsNullOrWhiteSpace(caseKey))
        {
            throw new SnapshotConfigurationException(
                "A parameterized test needs a stable case key. Rebuild with the package integration or supply Case in an explicit runner integration.");
        }
        if (caseKey is not null && string.IsNullOrWhiteSpace(caseKey))
        {
            throw new SnapshotConfigurationException("Case cannot be empty.");
        }
        if (variant is not null && string.IsNullOrWhiteSpace(variant))
        {
            throw new SnapshotConfigurationException("Variant cannot be empty.");
        }

        var suite = options.Suite ?? original.Suite;
        var test = options.Name ?? original.Test;
        return original with
        {
            ProjectDirectory = project,
            SourceFile = Path.GetFullPath(sourceFile),
            Suite = suite,
            Test = test,
            Case = caseKey,
            Variant = variant,
            LogicalId = original.LogicalId ?? $"{suite}::{test}"
        };
    }

    private static SnapshotTestIdentity FromDescriptor(SnapshotDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            throw new SnapshotConfigurationException(
                "No generated test identity exists. Reference TheLithium.Imprint in the test project and rebuild. " +
                "A custom Core-only integration must supply SnapshotTestOptions.Identity.");
        }
        return new SnapshotTestIdentity(
            ProjectDirectory: descriptor.ProjectDirectory,
            SourceFile: descriptor.SourceFile,
            Suite: descriptor.Suite,
            Test: descriptor.Test,
            LogicalId: $"{descriptor.ProjectName}::{descriptor.LogicalId}");
    }
}
