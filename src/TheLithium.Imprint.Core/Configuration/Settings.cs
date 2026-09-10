using System.Text.Json;
using TheLithium.Imprint.Generation;

namespace TheLithium.Imprint;

internal sealed record EffectiveSettings(
    SnapshotTestIdentity Identity,
    string Owner,
    string BaselineRoot,
    string TestDirectory,
    string ArtifactRoot,
    SnapshotUpdate Update,
    SnapshotUpdate? RunOverride,
    bool ReadOnly,
    SnapshotComparison Comparison,
    SnapshotNaming Naming,
    SnapshotFormat TextFormat,
    bool AllowEmpty,
    int MaxDepth,
    int MaxNodes,
    int MaxBytes,
    TimeSpan LockTimeout,
    CancellationToken Cancellation)
{
    internal string DisplayName => Identity.Suite + "." + Identity.Test
        + (Identity.Case is null ? "" : " [" + Identity.Case + "]")
        + (Identity.Variant is null ? "" : " [" + Identity.Variant + "]");

    internal SnapshotUpdate ResolveUpdate(SnapshotUpdate entry, SnapshotUpdate test)
        => ReadOnly ? SnapshotUpdate.Verify : RunOverride
            ?? (entry != SnapshotUpdate.Inherit ? entry : test);
}

internal sealed record ProjectConfiguration
{
    internal SnapshotUpdate Update { get; init; } = SnapshotUpdate.Verify;
    internal string DirectoryName { get; init; } = "__snapshots__";
    internal string? RootDirectory { get; init; }
    internal string ArtifactDirectory { get; init; } = "TestResults/TheLithium.Imprint";
    internal SnapshotFormat TextFormat { get; init; } = SnapshotFormat.Snap;
    internal SnapshotNaming Naming { get; init; } = SnapshotNaming.NameThenOrder;
    internal SnapshotComparison Comparison { get; init; } = new();
    internal bool AllowEmpty { get; init; }
    internal int MaxDepth { get; init; } = 64;
    internal int MaxNodes { get; init; } = 100_000;
    internal int MaxBytes { get; init; } = 4 * 1024 * 1024;
    internal int LockTimeoutSeconds { get; init; } = 10;
}

internal static class Settings
{
    internal static EffectiveSettings Resolve(SnapshotTestOptions? requested,
        string file, int line)
    {
        var options = requested ?? new();
        var descriptor = SnapshotMetadata.Find(file, line);
        var identity = options.Identity;
        if (identity is null && descriptor is null)
            throw new SnapshotConfigurationException(
                "No generated identity exists for this Snapshots.Run/RunAsync/Begin call. " +
                "Reference the TheLithium.Imprint package (including its analyzer), or supply SnapshotTestOptions.Identity. " +
                "Snapshot scopes opened inside generic helpers should receive an explicit test identity.");

        var originalProject = identity?.ProjectDirectory ?? descriptor!.ProjectDirectory;
        var project = Environment.GetEnvironmentVariable("IMPRINT_PROJECT_ROOT") ?? originalProject;
        if (string.IsNullOrWhiteSpace(project) || !Path.IsPathRooted(project))
            throw new SnapshotConfigurationException("The test-project root must be an absolute path.");
        project = Path.GetFullPath(project);
        if (!Directory.Exists(project))
            throw new SnapshotConfigurationException("Test-project root does not exist: " + project +
                ". Set IMPRINT_PROJECT_ROOT for a relocated checkout or use an explicit identity.");

        var sourceFile = identity?.SourceFile ?? descriptor!.SourceFile;
        if (Path.IsPathRooted(sourceFile))
        {
            // Only remap generated source paths. Explicit identities already describe runtime paths.
            if (identity is null && project != originalProject)
                sourceFile = Path.Combine(project, Path.GetRelativePath(originalProject, sourceFile));
        }
        else sourceFile = Path.Combine(project, sourceFile);
        sourceFile = Path.GetFullPath(sourceFile);

        var suite = options.Suite ?? identity?.Suite ?? descriptor!.Suite;
        var test = options.Name ?? identity?.Test ?? descriptor!.Test;
        var caseKey = options.Case ?? identity?.Case;
        var variant = options.Variant ?? identity?.Variant;
        if (identity is null && descriptor!.RequiresCase && string.IsNullOrWhiteSpace(caseKey))
            throw new SnapshotConfigurationException("A parameterized test must supply a stable Case in SnapshotTestOptions.");
        if (caseKey is not null && string.IsNullOrWhiteSpace(caseKey))
            throw new SnapshotConfigurationException("Case cannot be empty.");
        if (variant is not null && string.IsNullOrWhiteSpace(variant))
            throw new SnapshotConfigurationException("Variant cannot be empty.");
        var logicalId = identity is null ? descriptor!.ProjectName + "::" + descriptor.LogicalId
            : identity.LogicalId ?? (suite + "::" + test);
        identity = new(project, sourceFile, suite, test, caseKey, variant, logicalId);

        var configFile = Environment.GetEnvironmentVariable("IMPRINT_CONFIG")
            ?? options.ConfigurationFile;
        var explicitConfig = configFile is not null;
        configFile = configFile is null ? Path.Combine(project, "snapshots.config.json") : FullPath(project, configFile);
        var config = ReadConfiguration(configFile, explicitConfig);
        ValidateUpdate(options.Update);
        var update = options.Update != SnapshotUpdate.Inherit ? options.Update
            : descriptor is { Update: not SnapshotUpdate.Inherit } ? descriptor.Update : config.Update;

        ValidateUpdate(update);
        var runText = Environment.GetEnvironmentVariable("IMPRINT_UPDATE");
        SnapshotUpdate? runOverride = runText is null ? null : ParseUpdate(runText);
        var selector = Environment.GetEnvironmentVariable("IMPRINT_TEST");
        if (selector is not null && runOverride is null)
            throw new SnapshotConfigurationException("IMPRINT_TEST requires IMPRINT_UPDATE.");
        if (selector is not null && !PortableNames.Glob(selector, suite + "." + test
                + (caseKey is null ? "" : " [" + caseKey + "]"))
            && !PortableNames.Glob(selector, logicalId))
            runOverride = SnapshotUpdate.Verify;

        var readOnly = EnvironmentFlag("IMPRINT_READ_ONLY")
            || (EnvironmentFlag("CI") && !EnvironmentFlag("IMPRINT_ALLOW_CI_UPDATE"));
        var rootSetting = options.RootDirectory ?? config.RootDirectory;
        var baselineRoot = rootSetting is null
            ? Path.Combine(Path.GetDirectoryName(sourceFile)!, config.DirectoryName)
            : FullPath(project, rootSetting);
        var testFolder = test + (caseKey is null ? "" : " [" + caseKey + "]")
            + (variant is null ? "" : " [" + variant + "]");
        var testDirectory = Path.Combine(baselineRoot, PortableNames.Segment(suite), PortableNames.Segment(testFolder));
        var artifacts = FullPath(project, options.ArtifactDirectory ?? config.ArtifactDirectory);
        // Artifact output must not become part of the baseline tree or its ownership metadata.
        var relativeArtifact = Path.GetRelativePath(baselineRoot, artifacts);
        if (relativeArtifact == "." || (!Path.IsPathRooted(relativeArtifact)
            && relativeArtifact != ".." && !relativeArtifact.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new SnapshotConfigurationException("ArtifactDirectory must be outside the baseline root.");

        var comparison = options.Comparison ?? config.Comparison;
        ValidateComparison(comparison);
        var depth = options.MaxDepth ?? config.MaxDepth;
        var nodes = options.MaxNodes ?? config.MaxNodes;
        var bytes = options.MaxBytes ?? config.MaxBytes;
        if (depth is < 1 or > 256 || nodes is < 1 or > 10_000_000 || bytes is < 1 or > 256 * 1024 * 1024)
            throw new SnapshotConfigurationException("Limits: MaxDepth 1..256, MaxNodes 1..10000000, MaxBytes 1..268435456.");
        var naming = options.Naming ?? config.Naming;
        if ((int)naming < (int)SnapshotNaming.NameThenOrder || (int)naming > (int)SnapshotNaming.ExplicitOnly)
            throw new SnapshotConfigurationException("Invalid naming policy.");
        var owner = Owner(logicalId, caseKey, variant);
        return new(identity, owner, Path.GetFullPath(baselineRoot), Path.GetFullPath(testDirectory),
            artifacts, update, runOverride, readOnly, comparison, naming, config.TextFormat,
            options.AllowEmpty ?? config.AllowEmpty, depth, nodes, bytes,
            TimeSpan.FromSeconds(config.LockTimeoutSeconds), options.CancellationToken);
    }

    internal static void ValidateUpdate(SnapshotUpdate update)
    {
        if ((int)update < (int)SnapshotUpdate.Inherit || (int)update > (int)SnapshotUpdate.All)
            throw new SnapshotConfigurationException("Invalid snapshot update policy.");
    }

    internal static void ValidateComparison(SnapshotComparison comparison)
    {
        if (comparison.NumericTolerance < 0)
            throw new SnapshotConfigurationException("NumericTolerance cannot be negative.");
        if (comparison.MaxUnorderedArrayLength is < 1 or > 1024)
            throw new SnapshotConfigurationException("MaxUnorderedArrayLength must be 1..1024.");
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
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.ToLowerInvariant() switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => throw new SnapshotConfigurationException(name + " must be true, false, 1, or 0.")
        };
    }

    private static string FullPath(string project, string path)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(project, path));

    private static string Owner(string logicalId, string? caseKey, string? variant)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            writer.WriteStringValue("TheLithium.Imprint/1");
            writer.WriteStringValue(logicalId);
            writer.WriteStringValue(caseKey);
            writer.WriteStringValue(variant);
            writer.WriteEndArray();
        }
        return SnapshotEncoding.Utf8.GetString(stream.ToArray());
    }

    private static ProjectConfiguration ReadConfiguration(string path, bool required)
    {
        if (!File.Exists(path))
        {
            if (required) throw new SnapshotConfigurationException("Configuration file not found: " + path);
            return new();
        }
        try
        {
            if (new FileInfo(path).Length > 1024 * 1024)
                throw new SnapshotConfigurationException("Configuration file exceeds 1 MiB.");
            using var document = JsonDocument.Parse(File.ReadAllText(path, SnapshotEncoding.Utf8),
                new JsonDocumentOptions { MaxDepth = 16 });
            var config = new ProjectConfiguration();
            foreach (var property in UniqueProperties(document.RootElement))
            {
                var value = property.Value;
                config = property.Name switch
                {
                    "version" when value.GetInt32() == 1 => config,
                    "$schema" when value.ValueKind == JsonValueKind.String => config,
                    "update" => config with { Update = ParseUpdate(RequiredString(value, property.Name)) },
                    "directoryName" => config with { DirectoryName = RequiredString(value, property.Name) },
                    "rootDirectory" => config with { RootDirectory = value.GetString() },
                    "artifactDirectory" => config with { ArtifactDirectory = RequiredString(value, property.Name) },
                    "textExtension" => config with { TextFormat = value.GetString() switch
                    {
                        "snap" => SnapshotFormat.Snap, "txt" => SnapshotFormat.Text,
                        _ => throw new SnapshotConfigurationException("textExtension must be snap or txt.")
                    } },
                    "naming" => config with { Naming = value.GetString() switch
                    {
                        "name-then-order" => SnapshotNaming.NameThenOrder,
                        "order" => SnapshotNaming.Order,
                        "explicit-only" => SnapshotNaming.ExplicitOnly,
                        _ => throw new SnapshotConfigurationException("Invalid naming policy.")
                    } },
                    "comparison" => config with { Comparison = ReadComparison(value) },
                    "allowEmpty" => config with { AllowEmpty = value.GetBoolean() },
                    "maxDepth" => config with { MaxDepth = value.GetInt32() },
                    "maxNodes" => config with { MaxNodes = value.GetInt32() },
                    "maxBytes" => config with { MaxBytes = value.GetInt32() },
                    "lockTimeoutSeconds" => config with { LockTimeoutSeconds = value.GetInt32() },
                    _ => throw new SnapshotConfigurationException("Unknown or invalid configuration property: " + property.Name)
                };
            }
            if (PortableNames.Segment(config.DirectoryName) != config.DirectoryName)
                throw new SnapshotConfigurationException("directoryName must be a portable single folder name. Use rootDirectory for a path.");
            if (string.IsNullOrWhiteSpace(config.ArtifactDirectory) || config.LockTimeoutSeconds is < 1 or > 300)
                throw new SnapshotConfigurationException("artifactDirectory must be nonempty and lockTimeoutSeconds must be 1..300.");
            ValidateComparison(config.Comparison);
            return config;
        }
        catch (SnapshotConfigurationException) { throw; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            throw new SnapshotConfigurationException("Invalid configuration in " + path + ": " + error.Message);
        }
    }

    private static string RequiredString(JsonElement value, string property)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new SnapshotConfigurationException(property + " must be a nonempty string.");
        return value.GetString()!;
    }

    private static SnapshotComparison ReadComparison(JsonElement element)
    {
        var options = new SnapshotComparison();
        foreach (var property in UniqueProperties(element))
            options = property.Name switch
            {
                "numericTolerance" => options with { NumericTolerance = property.Value.GetDecimal() },
                "ignoreArrayOrder" => options with { IgnoreArrayOrder = property.Value.GetBoolean() },
                "ignoreStringCase" => options with { IgnoreStringCase = property.Value.GetBoolean() },
                "ignoreLineEndings" => options with { IgnoreLineEndings = property.Value.GetBoolean() },
                "ignoreTrailingWhitespace" => options with { IgnoreTrailingWhitespace = property.Value.GetBoolean() },
                "maxUnorderedArrayLength" => options with { MaxUnorderedArrayLength = property.Value.GetInt32() },
                _ => throw new SnapshotConfigurationException("Unknown comparison property: " + property.Name)
            };
        return options;
    }

    private static IEnumerable<JsonProperty> UniqueProperties(JsonElement element)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw new SnapshotConfigurationException("Duplicate configuration property: " + property.Name);
            yield return property;
        }
    }
}
