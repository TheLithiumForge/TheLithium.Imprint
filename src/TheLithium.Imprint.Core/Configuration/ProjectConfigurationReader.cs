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
                    "directoryName" => config with { DirectoryName = RequiredString(value, property.Name) },
                    "preferDisplayNames" => config with { PreferDisplayNames = value.GetBoolean() },
                    "rootDirectory" => config with { RootDirectory = value.GetString() },
                    "artifactDirectory" => config with { ArtifactDirectory = RequiredString(value, property.Name) },
                    "textExtension" => config with
                    {
                        TextFormat = value.GetString() switch
                        {
                            "snap" => SnapshotFormat.Snap,
                            "txt" => SnapshotFormat.Text,
                            _ => throw new SnapshotConfigurationException("textExtension must be snap or txt.")
                        }
                    },
                    "naming" => config with
                    {
                        Naming = value.GetString() switch
                        {
                            "name-then-order" => SnapshotNaming.NameThenOrder,
                            "order" => SnapshotNaming.Order,
                            "explicit-only" => SnapshotNaming.ExplicitOnly,
                            _ => throw new SnapshotConfigurationException("Invalid naming policy.")
                        }
                    },
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
            {
                throw new SnapshotConfigurationException("directoryName must be a portable single folder name. Use rootDirectory for a path.");
            }

            if ((config.ArtifactDirectory is not null && string.IsNullOrWhiteSpace(config.ArtifactDirectory)) || config.LockTimeoutSeconds is < 1 or > 300)
            {
                throw new SnapshotConfigurationException("artifactDirectory must be nonempty and lockTimeoutSeconds must be 1..300.");
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

    private static string RequiredString(JsonElement value, string property)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new SnapshotConfigurationException(property + " must be a nonempty string.");
        }

        return text;
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
                _ => throw new SnapshotConfigurationException("Unknown comparison property: " + property.Name)
            };
        }

        return options;
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
