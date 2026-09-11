using System.Text.Json;
using System.Text.RegularExpressions;
using TheLithium.Imprint.Specifications;
using Xunit;

namespace TheLithium.Imprint.Tests;

/// <summary>
/// Keeps snapshots.schema.json and the configuration reader describing the same file.
/// The schema is what editors use for completion, so a drift between the two would
/// silently offer keys that do not work, or hide keys that do.
/// </summary>
public sealed class ConfigurationSchemaTests : IDisposable
{
    private readonly Fixture _fixture = new();
    private readonly EnvironmentValue _ci = new("CI", "false");
    private readonly EnvironmentValue _update = new("IMPRINT_UPDATE", null);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "snapshots.schema.json")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "snapshots.schema.json was not found above " + AppContext.BaseDirectory);
        return directory!.FullName;
    }

    private static JsonDocument Schema()
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "snapshots.schema.json")));

    private static string ReaderSource() => File.ReadAllText(Path.Combine(RepositoryRoot(),
        "src", "TheLithium.Imprint.Core", "Configuration", "ProjectConfigurationReader.cs"));

    /// <summary>Every "key" arm that dispatches on a property name, as group.key or key.</summary>
    private static SortedSet<string> ReaderKeys()
    {
        var source = ReaderSource();
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (method, group) in new[]
        {
            ("Read", ""), ("ReadFiles", "files."), ("ReadNaming", "naming."),
            ("ReadLimits", "limits."), ("ReadComparison", "comparison.")
        })
        {
            var body = MethodBody(source, method);
            // Property arms assign into the config/options record or delegate to a group reader.
            // Value arms (for example "snap" => SnapshotFormat.Snap) deliberately do not match.
            foreach (Match match in Regex.Matches(body,
                @"""(\$?\w+)""\s*(?:when.*?)?=>\s*(config|options|Read[A-Za-z]+\()"))
            {
                keys.Add(group + match.Groups[1].Value);
            }
        }

        return keys;
    }

    private static string MethodBody(string source, string name)
    {
        // Anchor on the declaration, not a call site: ReadComparison( also appears inside Read.
        var declaration = Regex.Match(source, @"(internal|private) static \w+ " + name + @"\(");
        Assert.True(declaration.Success, "Reader method not found: " + name);
        var start = declaration.Index;
        // Each group reader is followed by the next member declaration; a bounded window is enough
        // to cover one switch and keeps this test from needing a full C# parser.
        var end = source.IndexOf("\n    private static ", start + 1, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
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
