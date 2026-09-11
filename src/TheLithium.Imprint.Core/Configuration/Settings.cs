using System.Text.Json;
using TheLithium.Imprint.Generation;

namespace TheLithium.Imprint.Configuration;

internal static class Settings
{
    internal static EffectiveSettings Resolve(SnapshotTestOptions? requested,
        string file, int line)
    {
        var options = requested ?? new();
        var descriptor = SnapshotMetadata.Find(file, line);
        var identity = TestIdentityResolver.Resolve(options, descriptor);
        var project = identity.ProjectDirectory;
        var sourceFile = identity.SourceFile;
        var suite = identity.Suite;
        var logicalId = identity.LogicalId ?? $"{identity.Suite}::{identity.Test}";

        var configFile = Environment.GetEnvironmentVariable("IMPRINT_CONFIG")
            ?? options.ConfigurationFile;
        var explicitConfig = configFile is not null;
        configFile = configFile is null ? Path.Combine(project, "snapshots.config.json") : FullPath(project, configFile);
        var config = ProjectConfigurationReader.Read(configFile, explicitConfig);
        if (options.Identity is null && config.PreferDisplayNames && descriptor?.DisplayName is { Length: > 0 } displayName)
        {
            identity = identity with { Test = displayName };
        }
        ValidateUpdate(options.Update);
        var update = ResolvePolicy(options, descriptor, config);

        ValidateUpdate(update);
        var runText = Environment.GetEnvironmentVariable("IMPRINT_UPDATE");
        SnapshotUpdate? runOverride = runText is null ? null : ParseUpdate(runText);
        var selector = Environment.GetEnvironmentVariable("IMPRINT_TEST");
        if (selector is not null && runOverride is null)
        {
            throw new SnapshotConfigurationException("IMPRINT_TEST requires IMPRINT_UPDATE.");
        }

        if (selector is not null && !PortableNames.Glob(selector, TestNames.DisplayName(identity))
            && !PortableNames.Glob(selector, logicalId))
        {
            runOverride = SnapshotUpdate.Verify;
        }

        var readOnly = EnvironmentFlag("IMPRINT_READ_ONLY")
            || (EnvironmentFlag("CI") && !EnvironmentFlag("IMPRINT_ALLOW_CI_UPDATE"));
        var rootSetting = options.RootDirectory ?? config.RootDirectory;
        var baselineRoot = rootSetting is null ? Path.Combine(project, config.DirectoryName) : FullPath(project, rootSetting);
        var relativeSource = Path.GetRelativePath(project, sourceFile);
        if (options.Identity is null && (Path.IsPathRooted(relativeSource) || relativeSource == ".." || relativeSource.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            throw new SnapshotConfigurationException("The test source file must be inside its project root. Supply an explicit identity for linked source files.");
        }
        var testPath = Path.GetDirectoryName(relativeSource) ?? string.Empty;
        var testFolder = TestNames.FolderName(identity);
        var testDirectory = Path.Combine(baselineRoot, testPath, PortableNames.Segment(suite), PortableNames.Segment(testFolder));
        var buildArtifacts = Path.Combine(project, "artifacts");
        if (options.Identity is null && project == descriptor?.ProjectDirectory && descriptor?.BuildArtifactsDirectory is { Length: > 0 } generatedArtifacts)
        {
            buildArtifacts = FullPath(project, generatedArtifacts);
        }
        var artifacts = FullPath(project, options.ArtifactDirectory ?? config.ArtifactDirectory ?? Path.Combine(buildArtifacts, "imprint", "failures"));
        var storage = Path.Combine(buildArtifacts, "imprint", "storage");
        // Diagnostics and recovery files must stay outside the reviewed snapshot tree.
        var relativeArtifact = Path.GetRelativePath(baselineRoot, artifacts);
        if (relativeArtifact == "." || (!Path.IsPathRooted(relativeArtifact)
            && relativeArtifact != ".." && !relativeArtifact.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            throw new SnapshotConfigurationException("ArtifactDirectory must be outside the baseline root.");
        }

        var comparison = options.Comparison ?? config.Comparison;
        ValidateComparison(comparison);
        var depth = options.MaxDepth ?? config.MaxDepth;
        var nodes = options.MaxNodes ?? config.MaxNodes;
        var bytes = options.MaxBytes ?? config.MaxBytes;
        if (depth is < 1 or > SnapshotLimits.MaximumDepth || nodes is < 1 or > SnapshotLimits.MaximumNodes || bytes is < 1 or > SnapshotLimits.MaximumBytes)
        {
            throw new SnapshotConfigurationException("Limits: MaxDepth 1..256, MaxNodes 1..10000000, MaxBytes 1..268435456.");
        }

        var naming = options.Naming ?? config.Naming;
        if ((int)naming < (int)SnapshotNaming.NameThenOrder || (int)naming > (int)SnapshotNaming.ExplicitOnly)
        {
            throw new SnapshotConfigurationException("Invalid naming policy.");
        }

        return new EffectiveSettings
        {
            Identity = identity,
            IdentityKey = IdentityKey(identity),
            BaselineRoot = Path.GetFullPath(baselineRoot),
            TestDirectory = Path.GetFullPath(testDirectory),
            ArtifactRoot = artifacts,
            StorageRoot = storage,
            Update = update,
            RunOverride = runOverride,
            ReadOnly = readOnly,
            Comparison = comparison,
            Naming = naming,
            TextFormat = config.TextFormat,
            AllowEmpty = options.AllowEmpty ?? config.AllowEmpty,
            MaxDepth = depth,
            MaxNodes = nodes,
            MaxBytes = bytes,
            LockTimeout = TimeSpan.FromSeconds(config.LockTimeoutSeconds),
            Cancellation = options.CancellationToken
        };
    }

    private static SnapshotUpdate ResolvePolicy(SnapshotTestOptions options, SnapshotDescriptor? descriptor, ProjectConfiguration config)
    {
        if (options.Update != SnapshotUpdate.Inherit)
        {
            return options.Update;
        }
        if (descriptor is { Update: not SnapshotUpdate.Inherit })
        {
            return descriptor.Update;
        }
        return config.Update;
    }

    internal static void ValidateUpdate(SnapshotUpdate update)
    {
        if ((int)update < (int)SnapshotUpdate.Inherit || (int)update > (int)SnapshotUpdate.All)
        {
            throw new SnapshotConfigurationException("Invalid snapshot update policy.");
        }
    }

    internal static void ValidateComparison(SnapshotComparison comparison)
    {
        if (comparison.NumericTolerance < 0)
        {
            throw new SnapshotConfigurationException("NumericTolerance cannot be negative.");
        }

        if (comparison.MaxUnorderedArrayLength is < 1 or > 1024)
        {
            throw new SnapshotConfigurationException("MaxUnorderedArrayLength must be 1..1024.");
        }
    }

    internal static SnapshotUpdate ParseUpdate(string value) => value.ToLowerInvariant() switch
    {
        "verify" => SnapshotUpdate.Verify,
        "missing" => SnapshotUpdate.Missing,
        "all" => SnapshotUpdate.All,
        _ => throw new SnapshotConfigurationException("Update must be 'verify', 'missing', or 'all'.")
    };

    private static bool EnvironmentFlag(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.ToLowerInvariant() switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => throw new SnapshotConfigurationException(name + " must be true, false, 1, or 0.")
        };
    }

    private static string FullPath(string project, string path)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(project, path));

    private static string IdentityKey(SnapshotTestIdentity identity)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(SnapshotProtocol.Version);
            writer.WriteStringValue(identity.LogicalId);
            writer.WriteStringValue(identity.Case);
            writer.WriteStringValue(identity.Variant);
            writer.WriteEndArray();
        }
        return SnapshotEncoding.Utf8.GetString(stream.ToArray());
    }

}
