using System.Text.Json;

namespace TheLithium.Imprint.Configuration;

internal static class ProjectConfigurationReader
{
    internal static ProjectConfiguration Read(string path, bool required, CancellationToken cancellation = default)
    {
        if (!File.Exists(path))
        {
            if (required)
            {
                throw new SnapshotConfigurationException($"Configuration file not found: {path}");
            }
            return new();
        }
        try
        {
            var text = SnapshotFileReader.ReadText(path, SnapshotLimits.ConfigurationBytes, cancellation);
            if (text.StartsWith('\uFEFF'))
            {
                text = text[1..];
            }
            using var document = StrictJson.Parse(text, SnapshotLimits.ConfigurationDepth, cancellation);
            // Absence uses the build-provided artifact directory; explicit null is a configuration error.
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Object
                && files.TryGetProperty("failureArtifactPath", out var artifact) && artifact.ValueKind == JsonValueKind.Null)
            {
                throw new SnapshotConfigurationException("files.failureArtifactPath must be a nonempty string.");
            }
            var file = document.RootElement.Deserialize(ProjectConfigurationJsonContext.Instance.ProjectConfigurationFile)
                ?? throw new SnapshotConfigurationException("Project configuration must be an object.");
            return Resolve(file);
        }
        catch (SnapshotConfigurationException) { throw; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or ArgumentException or SnapshotException)
        {
            throw new SnapshotConfigurationException($"Invalid configuration in {path}: {error.Message}");
        }
    }

    private static ProjectConfiguration Resolve(ProjectConfigurationFile file)
    {
        if (file.Version != 1 || file.Update == SnapshotUpdate.Inherit)
        {
            throw new SnapshotConfigurationException("Configuration requires version 1 and update verify, missing or all.");
        }
        if (file.Prune is < SnapshotPrune.Auto or > SnapshotPrune.Off)
        {
            throw new SnapshotConfigurationException("prune must be auto, report or off.");
        }
        if (PortableNames.Segment(file.Files.SnapshotFolderName) != file.Files.SnapshotFolderName)
        {
            throw new SnapshotConfigurationException("files.snapshotFolderName must be a portable single folder name.");
        }
        if (file.Files.FailureArtifactPath is { } artifactPath && string.IsNullOrWhiteSpace(artifactPath)
            || file.Files.SnapshotRootPath is { } root && string.IsNullOrWhiteSpace(root))
        {
            throw new SnapshotConfigurationException("Configured file paths must be nonempty strings.");
        }
        if (file.Limits.LockTimeoutSeconds is < 1 or > 300)
        {
            throw new SnapshotConfigurationException("limits.lockTimeoutSeconds must be 1..300.");
        }
        Settings.ValidateLimits(file.Limits.MaxNestingDepth, file.Limits.MaxValuesPerSnapshot, file.Limits.MaxBytesPerSnapshot);
        var comparison = new SnapshotComparison
        {
            NumericTolerance = file.Comparison.NumericTolerance,
            IgnoreArrayOrder = file.Comparison.IgnoreArrayOrder,
            IgnoreStringCase = file.Comparison.IgnoreStringCase,
            IgnoreLineEndings = file.Comparison.IgnoreLineEndings,
            IgnoreTrailingWhitespace = file.Comparison.IgnoreTrailingWhitespace,
            MaxUnorderedArrayLength = file.Comparison.MaxUnorderedArrayLength
        };
        Settings.ValidateComparison(comparison);
        return new()
        {
            Update = file.Update,
            Prune = file.Prune,
            AllowEmpty = file.AllowEmptyTests,
            StringContent = file.StringContent,
            SnapshotFolderName = file.Files.SnapshotFolderName,
            RootDirectory = file.Files.SnapshotRootPath,
            ArtifactDirectory = file.Files.FailureArtifactPath,
            Naming = file.Naming.UnnamedCaptures,
            UseFrameworkDisplayNames = file.Naming.UseFrameworkDisplayNames,
            Comparison = comparison,
            Representation = new()
            {
                Enums = file.Representation.Enums,
                ByteArrays = file.Representation.ByteArrays,
                Dictionaries = file.Representation.Dictionaries
            },
            MaxNestingDepth = file.Limits.MaxNestingDepth,
            MaxValuesPerSnapshot = file.Limits.MaxValuesPerSnapshot,
            MaxBytesPerSnapshot = file.Limits.MaxBytesPerSnapshot,
            LockTimeoutSeconds = file.Limits.LockTimeoutSeconds,
            Visual = ResolveVisual(file.Visual)
        };
    }

    private static VisualSettings ResolveVisual(VisualFileConfiguration visual)
    {
        if (visual.CaptureSource != VisualCaptureSource.External)
        {
            throw new SnapshotConfigurationException("visual.captureSource must be external.");
        }
        if (visual.MaxBytesPerImage is < 1 or > SnapshotLimits.MaximumBytes)
        {
            throw new SnapshotConfigurationException($"visual.maxBytesPerImage must be 1..{SnapshotLimits.MaximumBytes}.");
        }
        if (visual.MaxPixelsPerImage is < 1 or > SnapshotLimits.MaximumImagePixels)
        {
            throw new SnapshotConfigurationException($"visual.maxPixelsPerImage must be 1..{SnapshotLimits.MaximumImagePixels}.");
        }

        var stability = visual.Stability;
        if (stability.MatchingFrames is < 1 or > 100 || stability.IntervalMilliseconds is < 10 or > 10_000
            || stability.MinimumDurationMilliseconds is < 0 or > 60_000 || stability.TimeoutSeconds is < 1 or > 600)
        {
            throw new SnapshotConfigurationException(
                "visual.stability needs matchingFrames 1..100, intervalMilliseconds 10..10000, minimumDurationMilliseconds 0..60000 and timeoutSeconds 1..600.");
        }

        var platforms = new Dictionary<string, ImageComparisonSettings>(StringComparer.Ordinal);
        foreach (var platform in visual.Platforms)
        {
            if (!VisualPlatformNames.All.Contains(platform.Key))
            {
                var known = string.Join(", ", VisualPlatformNames.All.Order(StringComparer.Ordinal));
                throw new SnapshotConfigurationException($"visual.platforms.{platform.Key} isn't a platform. Use one of {known}.");
            }
            platforms.Add(platform.Key, ImageComparison(platform.Value.Comparison, $"visual.platforms.{platform.Key}.comparison"));
        }

        return new()
        {
            CaptureSource = visual.CaptureSource,
            MaxBytesPerImage = visual.MaxBytesPerImage,
            MaxPixelsPerImage = visual.MaxPixelsPerImage,
            Comparison = ImageComparison(visual.Comparison, "visual.comparison"),
            Stability = new(stability.MatchingFrames, Milliseconds(stability.IntervalMilliseconds),
                Milliseconds(stability.MinimumDurationMilliseconds), Seconds(stability.TimeoutSeconds)),
            Platforms = platforms
        };
    }

    private static ImageComparisonSettings ImageComparison(ImageComparisonFileConfiguration comparison, string path)
    {
        if (comparison.ChannelTolerance is < 0 or > 255 || comparison.MaxDifferingPercent is < 0 or > 100)
        {
            throw new SnapshotConfigurationException($"{path} needs channelTolerance 0..255 and maxDifferingPercent 0..100.");
        }
        return new(comparison.ChannelTolerance, comparison.MaxDifferingPercent, comparison.IgnoreAntialiasing);
    }

    private static TimeSpan? Milliseconds(int? value) => value is { } milliseconds ? TimeSpan.FromMilliseconds(milliseconds) : null;

    private static TimeSpan? Seconds(int? value) => value is { } seconds ? TimeSpan.FromSeconds(seconds) : null;
}
