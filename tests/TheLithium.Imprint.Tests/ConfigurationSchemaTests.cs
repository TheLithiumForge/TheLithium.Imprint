using System.Text.Json;

using TheLithium.Imprint.Specifications;
using Xunit;

namespace TheLithium.Imprint.Tests;

/// <summary>
/// Keeps the versioned snapshots schema and the configuration reader describing the same file.
/// The schema is what editors use for completion, so a drift between the two would
/// silently offer keys that do not work, or hide keys that do.
/// </summary>
public sealed class ConfigurationSchemaTests : IDisposable
{
    private const string SchemaVersion = "1.0.0";
    private readonly Fixture _fixture = new();
    private readonly EnvironmentValue _ci = new("CI", "false");
    private readonly EnvironmentValue _update = new("IMPRINT_UPDATE", null);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(SchemaPath(directory.FullName)))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "schemas/" + SchemaVersion + "/snapshots.schema.json was not found above " + AppContext.BaseDirectory);
        return directory!.FullName;
    }

    private static string SchemaPath(string repositoryRoot)
        => Path.Combine(repositoryRoot, "schemas", SchemaVersion, "snapshots.schema.json");

    private static JsonDocument Schema()
        => JsonDocument.Parse(File.ReadAllText(SchemaPath(RepositoryRoot())));

    /// <summary>Read the generated JSON contract, not C# source text or a second property inventory.</summary>
    private static SortedSet<string> ReaderKeys()
    {
        var context = TheLithium.Imprint.Configuration.ProjectConfigurationJsonContext.Instance;
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        Collect(context.ProjectConfigurationFile, string.Empty);
        return keys;

        void Collect(System.Text.Json.Serialization.Metadata.JsonTypeInfo contract, string prefix)
        {
            foreach (var property in contract.Properties)
            {
                var name = prefix + property.Name;
                keys.Add(name);
                if (context.GetTypeInfo(property.PropertyType) is { Kind: System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object } child)
                {
                    Collect(child, name + ".");
                }
            }
        }
    }
    /// <summary>Schema leaves as group.key or key, so a key in the wrong group is a failure.</summary>
    private static SortedSet<string> SchemaKeys()
    {
        using var schema = Schema();
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var property in schema.RootElement.GetProperty("properties").EnumerateObject())
        {
            keys.Add(property.Name);
            if (property.Value.TryGetProperty("properties", out var children))
            {
                foreach (var child in children.EnumerateObject())
                {
                    keys.Add(property.Name + "." + child.Name);
                }
            }
        }

        return keys;
    }

    [Fact]
    public void SchemaAndReaderDescribeTheSameKeys()
    {
        var schema = SchemaKeys();
        var reader = ReaderKeys();
        Assert.Equal(string.Join("\n", schema), string.Join("\n", reader));
    }

    [Fact]
    public void EverySchemaKeyIsAcceptedByTheReader()
    {
        using var schema = Schema();
        foreach (var property in schema.RootElement.GetProperty("properties").EnumerateObject())
        {
            if (property.Name == "$schema")
            {
                continue;
            }

            if (property.Value.TryGetProperty("properties", out var children))
            {
                foreach (var child in children.EnumerateObject())
                {
                    Accepts("{\"" + property.Name + "\":{\"" + child.Name + "\":" + Sample(child.Value) + "}}");
                }
            }
            else
            {
                Accepts("{\"" + property.Name + "\":" + Sample(property.Value) + "}");
            }
        }
    }

    [Fact]
    public void TheDocumentedExampleIsAccepted()
    {
        var example = File.ReadAllText(Path.Combine(RepositoryRoot(), "examples", "snapshots.config.json"));
        // The example carries a $schema path relative to the repository, which is not a reader concern.
        Accepts(example);
    }

    [Fact]
    public void AnUndocumentedKeyIsStillRejected()
    {
        // Guards the tests above: they would pass trivially if the reader accepted anything.
        _fixture.Configure("""{"files":{"notARealKey":1}}""");
        Assert.Throws<SnapshotConfigurationException>(
            () => _fixture.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All));
    }

    private void Accepts(string json)
    {
        _fixture.Configure(json);
        var error = Record.Exception(() => _fixture.Run(() => 1.AssertSnapshot("value"), SnapshotUpdate.All));
        Assert.True(error is null, "The reader rejected a documented configuration:\n" + json + "\n" + error?.Message);
    }

    /// <summary>A value the schema says is valid: its default, else its first enum entry, else its type's simplest value.</summary>
    private static string Sample(JsonElement definition)
    {
        if (definition.TryGetProperty("default", out var fallback))
        {
            return fallback.GetRawText();
        }
        if (definition.TryGetProperty("enum", out var choices))
        {
            return choices[0].GetRawText();
        }
        if (definition.TryGetProperty("const", out var constant))
        {
            return constant.GetRawText();
        }

        var types = definition.GetProperty("type");
        var type = types.ValueKind == JsonValueKind.Array ? types[0].GetString() : types.GetString();
        return type switch
        {
            "boolean" => "false",
            "integer" or "number" => definition.TryGetProperty("minimum", out var minimum) ? minimum.GetRawText() : "0",
            "string" when types.ValueKind == JsonValueKind.Array => "null",
            "string" => "\"sample\"",
            _ => "null"
        };
    }

    public void Dispose()
    {
        _update.Dispose();
        _ci.Dispose();
        _fixture.Dispose();
    }
}
