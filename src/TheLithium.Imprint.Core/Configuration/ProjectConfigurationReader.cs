using System.Text.Json;

namespace TheLithium.Imprint.Configuration;

internal static class ProjectConfigurationReader
{
    internal static ProjectConfiguration Read(string path, bool required)
    {
        if (!File.Exists(path))
        {
            if (required)
            {
                throw new SnapshotConfigurationException("Configuration file not found: " + path);
            }

            return new();
        }
        try
        {
            if (new FileInfo(path).Length > 1024 * 1024)
            {
                throw new SnapshotConfigurationException("Configuration file exceeds 1 MiB.");
            }

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
                    "update" => config with { Update = Settings.ParseUpdate(RequiredString(value, property.Name)) },
                    "allowEmptyTests" => config with { AllowEmpty = value.GetBoolean() },
                    "files" => ReadFiles(config, Group(value, property.Name)),
                    "naming" => ReadNaming(config, Group(value, property.Name)),
                    "comparison" => config with { Comparison = ReadComparison(Group(value, property.Name)) },
                    "limits" => ReadLimits(config, Group(value, property.Name)),
                    _ => throw new SnapshotConfigurationException(Unknown(property.Name))
                };
            }
            if (PortableNames.Segment(config.SnapshotFolderName) != config.SnapshotFolderName)
            {
                throw new SnapshotConfigurationException(
                    "files.snapshotFolderName must be a portable single folder name. Use files.snapshotRootPath for a path.");
            }

            if (config.ArtifactDirectory is not null && string.IsNullOrWhiteSpace(config.ArtifactDirectory))
            {
                throw new SnapshotConfigurationException("files.failureArtifactPath must be a nonempty string.");
            }

            if (config.LockTimeoutSeconds is < 1 or > 300)
            {
                throw new SnapshotConfigurationException("limits.lockTimeoutSeconds must be 1..300.");
            }

            Settings.ValidateComparison(config.Comparison);
            return config;
        }
        catch (SnapshotConfigurationException) { throw; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            throw new SnapshotConfigurationException("Invalid configuration in " + path + ": " + error.Message);
        }
    }

    private static ProjectConfiguration ReadFiles(ProjectConfiguration config, JsonElement element)
    {
        foreach (var property in UniqueProperties(element))
        {
            var value = property.Value;
            config = property.Name switch
            {
                "snapshotFolderName" => config with { SnapshotFolderName = RequiredString(value, "files.snapshotFolderName") },
                "snapshotRootPath" => config with { RootDirectory = value.GetString() },
                "failureArtifactPath" => config with { ArtifactDirectory = RequiredString(value, "files.failureArtifactPath") },
                "textFileExtension" => config with
                {
                    TextFormat = value.GetString() switch
                    {
                        "snap" => SnapshotFormat.Snap,
                        "txt" => SnapshotFormat.Text,
                        _ => throw new SnapshotConfigurationException("files.textFileExtension must be \"snap\" or \"txt\".")
                    }
                },
                _ => throw new SnapshotConfigurationException(Unknown("files." + property.Name))
            };
        }

        return config;
    }

    private static ProjectConfiguration ReadNaming(ProjectConfiguration config, JsonElement element)
    {
        foreach (var property in UniqueProperties(element))
        {
            var value = property.Value;
            config = property.Name switch
            {
                "unnamedCaptures" => config with
                {
                    Naming = value.GetString() switch
                    {
                        "name-then-order" => SnapshotNaming.NameThenOrder,
                        "order" => SnapshotNaming.Order,
                        "explicit-only" => SnapshotNaming.ExplicitOnly,
                        _ => throw new SnapshotConfigurationException(
                            "naming.unnamedCaptures must be \"name-then-order\", \"order\", or \"explicit-only\".")
                    }
                },
                "useFrameworkDisplayNames" => config with { UseFrameworkDisplayNames = value.GetBoolean() },
                _ => throw new SnapshotConfigurationException(Unknown("naming." + property.Name))
            };
        }

        return config;
    }

    private static ProjectConfiguration ReadLimits(ProjectConfiguration config, JsonElement element)
    {
        foreach (var property in UniqueProperties(element))
        {
            var value = property.Value;
            config = property.Name switch
            {
                "maxNestingDepth" => config with { MaxNestingDepth = value.GetInt32() },
                "maxValuesPerSnapshot" => config with { MaxValuesPerSnapshot = value.GetInt32() },
                "maxBytesPerSnapshot" => config with { MaxBytesPerSnapshot = value.GetInt32() },
                "lockTimeoutSeconds" => config with { LockTimeoutSeconds = value.GetInt32() },
                _ => throw new SnapshotConfigurationException(Unknown("limits." + property.Name))
            };
        }

        return config;
    }

    private static SnapshotComparison ReadComparison(JsonElement element)
    {
        var options = new SnapshotComparison();
        foreach (var property in UniqueProperties(element))
        {
            options = property.Name switch
            {
                "numericTolerance" => options with { NumericTolerance = property.Value.GetDecimal() },
                "ignoreArrayOrder" => options with { IgnoreArrayOrder = property.Value.GetBoolean() },
                "ignoreStringCase" => options with { IgnoreStringCase = property.Value.GetBoolean() },
                "ignoreLineEndings" => options with { IgnoreLineEndings = property.Value.GetBoolean() },
                "ignoreTrailingWhitespace" => options with { IgnoreTrailingWhitespace = property.Value.GetBoolean() },
                "maxUnorderedArrayLength" => options with { MaxUnorderedArrayLength = property.Value.GetInt32() },
                _ => throw new SnapshotConfigurationException(Unknown("comparison." + property.Name))
            };
        }

        return options;
    }

    /// <summary>Names a group whose value must be an object, so a stale flat value reports the new location.</summary>
    private static JsonElement Group(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object
            ? value
            : throw new SnapshotConfigurationException("\"" + name + "\" must be an object, for example "
                + name + ": { " + Example(name) + " }.");

    private static string Example(string group) => group switch
    {
        "files" => "\"snapshotFolderName\": \"__snapshots__\"",
        "naming" => "\"unnamedCaptures\": \"name-then-order\"",
        "limits" => "\"maxNestingDepth\": 64",
        _ => "\"ignoreLineEndings\": true"
    };

    /// <summary>Sends a property that was renamed or grouped to its new spelling instead of only rejecting it.</summary>
    /// <param name="name">The property as written, qualified with its group when it was inside one.</param>
    private static string Unknown(string name)
    {
        // Match on the leaf so an old spelling is recognised whether or not it was written in a group.
        var leaf = name[(name.LastIndexOf('.') + 1)..];
        var moved = leaf switch
        {
            "directoryName" => "files.snapshotFolderName",
            "rootDirectory" => "files.snapshotRootPath",
            "artifactDirectory" => "files.failureArtifactPath",
            "textExtension" => "files.textFileExtension",
            "preferDisplayNames" => "naming.useFrameworkDisplayNames",
            "maxDepth" => "limits.maxNestingDepth",
            "maxNodes" => "limits.maxValuesPerSnapshot",
            "maxBytes" => "limits.maxBytesPerSnapshot",
            "allowEmpty" => "allowEmptyTests",
            _ => null
        };

        return moved is null
            ? "Unknown or invalid configuration property: " + name
            : "Unknown configuration property: " + name + ". It is now \"" + moved + "\".";
    }

    private static string RequiredString(JsonElement value, string property)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new SnapshotConfigurationException(property + " must be a nonempty string.");
        }

        return text;
    }

    private static IEnumerable<JsonProperty> UniqueProperties(JsonElement element)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new SnapshotConfigurationException("Duplicate configuration property: " + property.Name);
            }

            yield return property;
        }
    }
}
