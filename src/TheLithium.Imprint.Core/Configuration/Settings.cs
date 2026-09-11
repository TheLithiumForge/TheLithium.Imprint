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

        var configFile = options.ConfigurationFile;
        var explicitConfig = configFile is not null;
        configFile = configFile is null ? Path.Combine(project, "snapshots.config.json") : FullPath(project, configFile);
        var config = ProjectConfigurationReader.Read(configFile, explicitConfig);
        if (options.Identity is null && config.UseFrameworkDisplayNames && descriptor?.DisplayName is { Length: > 0 } displayName)
        {
            identity = identity with { Test = displayName };
        }
        ValidateUpdate(options.Update);
        var update = ResolvePolicy(options, descriptor, config);

        ValidateUpdate(update);

        // IMPRINT_UPDATE is the one policy channel outside the typed API. A per-run approval
        // must not require editing and reverting a committed file, and this library has no
        // runner adapter, so no command line or .runsettings value can reach it.
        // Continuous integration verifies unless that particular run asks for something else.
        var runOverride = ResolveRunOverride();

        // Read-only is a property of the run, never of one test: a run-wide Verify means nothing
        // on disk changes, including recovery of an interrupted commit. A test or project policy
        // of Verify is ordinary precedence that a narrower scope is still allowed to override.
        var readOnly = runOverride == SnapshotUpdate.Verify;
        var rootSetting = options.RootDirectory ?? config.RootDirectory;
        var baselineRoot = rootSetting is null ? Path.Combine(project, config.SnapshotFolderName) : FullPath(project, rootSetting);
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
        var depth = options.MaxNestingDepth ?? config.MaxNestingDepth;
        var nodes = options.MaxValuesPerSnapshot ?? config.MaxValuesPerSnapshot;
        var bytes = options.MaxBytesPerSnapshot ?? config.MaxBytesPerSnapshot;
        if (depth is < 1 or > SnapshotLimits.MaximumDepth || nodes is < 1 or > SnapshotLimits.MaximumNodes || bytes is < 1 or > SnapshotLimits.MaximumBytes)
        {
            throw new SnapshotConfigurationException("Limits: MaxNestingDepth 1..256, MaxValuesPerSnapshot 1..10000000, MaxBytesPerSnapshot 1..268435456.");
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
            MaxNestingDepth = depth,
            MaxValuesPerSnapshot = nodes,
            MaxBytesPerSnapshot = bytes,
            LockTimeout = TimeSpan.FromSeconds(config.LockTimeoutSeconds),
            Cancellation = options.CancellationToken
        };
    }

    /// <summary>The policy this run asks for, above any test, attribute, or project setting.</summary>
    private static SnapshotUpdate? ResolveRunOverride()
    {
        var requested = Environment.GetEnvironmentVariable("IMPRINT_UPDATE");
        if (requested is not null)
        {
            return ParseUpdate(requested);
        }
        if (EnvironmentFlag("CI"))
        {
            return SnapshotUpdate.Verify;
        }

        return null;
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
