using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TheLithium.Imprint.Generator;
using Xunit;

namespace TheLithium.Imprint.Generator.Tests;

public sealed class GeneratorTests
{
    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path))
        .Append(MetadataReference.CreateFromFile(typeof(Snapshots).Assembly.Location)).ToArray();

    [Theory]
    [InlineData("new { Code = 1, Nested = new { Name = \"value\" } }")]
    [InlineData("new System.Collections.Generic.List<int> { 1, 2, 3 }")]
    [InlineData("new System.Collections.Generic.Dictionary<string, int> { [\"a\"] = 1 }")]
    [InlineData("new int[2, 3, 4]")]
    [InlineData("(1, \"two\", 3, 4, 5, 6, 7, 8, 9)")]
    [InlineData("(int?)42")]
    [InlineData("new[] { new { Name = \"first\" }, new { Name = \"second\" } }")]
    public void SupportedShapesCompile(string expression)
    {
        var result = Generate("using TheLithium.Imprint; public class Tests { public void Test() => Snapshots.Run(() => (" + expression + ").AssertSnapshot()); }");
        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "IMP001");
        Assert.Contains("SnapshotMetadata.Register", result.Source);
    }

    [Fact]
    public void PrivateTypeHasActionableDiagnostic()
    {
        var result = Generate("""
            using TheLithium.Imprint;
            public class Tests
            {
                private record Hidden(int Value);
                public void Test() => Snapshots.Run(() => new Hidden(1).AssertSnapshot());
            }
            """);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "IMP001");
        Assert.Contains("explicit writer", diagnostic.GetMessage());
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ExplicitWriterDoesNotRequestAnAutomaticWriter()
    {
        var result = Generate("""
            using TheLithium.Imprint;
            public class Tests
            {
                private record Hidden(int Value);
                public void Test() => Snapshots.Run(() => new Hidden(1).AssertSnapshot(static (w, v, c) => w.WriteNumberValue(v.Value)));
            }
            """);
        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "IMP001");
    }

    [Fact]
    public void UnrelatedSnapshotMethodsAreIgnored()
    {
        var result = Generate("public class Tests { public void Test() => Snapshot(new object()); private void Snapshot(object value) { } }");
        Assert.Empty(result.Errors);
        Assert.DoesNotContain("SnapshotMetadata.Register", result.Source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "IMP001");
    }

    [Theory]
    [InlineData("Xunit", "Fact", "DisplayName", "Readable test")]
    [InlineData("NUnit.Framework", "Test", "Description", "Readable test")]
    [InlineData("Microsoft.VisualStudio.TestTools.UnitTesting", "TestMethod", "DisplayName", "Readable test")]
    public void FrameworkDisplayMetadataIsKeptAsAnOptInName(string ns, string attribute, string property, string name)
    {
        var result = Generate($$"""
            using TheLithium.Imprint;
            namespace {{ns}} { public class {{attribute}}Attribute : System.Attribute { public string {{property}} { get; set; } } }
            public class Tests {
                [{{ns}}.{{attribute}}({{property}} = "{{name}}")]
                public void Test() => Snapshots.Run(() => 1.AssertSnapshot());
            }
            """);
        Assert.Empty(result.Errors);
        Assert.Contains("\"Readable test\"", result.Source);
        Assert.Contains("\"Tests\", \"Test\"", result.Source);
    }

    [Fact]
    public void SettingsOverrideDisplayNameAndInheritedUpdate()
    {
        var result = Generate("""
            using TheLithium.Imprint;
            [SnapshotSettings(Update = SnapshotUpdate.All)] public class Parent { }
            [SnapshotSettings(Name = "Suite")] public class Tests : Parent
            {
                [SnapshotSettings(Name = "Test title", Update = SnapshotUpdate.Verify)]
                public void Test() => Snapshots.Run(() => 1.AssertSnapshot());
            }
            """);
        Assert.Empty(result.Errors);
        Assert.Contains("\"Suite\", \"Test title\"", result.Source);
        Assert.Contains("SnapshotUpdate)1, false", result.Source);
    }

    [Fact]
    public void ParameterizedTestsRequireCase()
    {
        var result = Generate("using TheLithium.Imprint; public class Tests { public void Test(int value) => Snapshots.Run(() => value.AssertSnapshot()); }");
        Assert.Empty(result.Errors);
        Assert.Contains("SnapshotUpdate)0, true", result.Source);
    }

    [Fact]
    public void CancellationTokenDoesNotRequireCaseIdentity()
    {
        var result = Generate("using TheLithium.Imprint; using System.Threading; public class Tests { public void Test(CancellationToken token) => Snapshots.Run(() => 1.AssertSnapshot()); }");
        Assert.Empty(result.Errors);
        Assert.Contains("SnapshotUpdate)0, false", result.Source);
    }

    [Fact]
    public void EnumAliasesDoNotGenerateDuplicateCases()
    {
        var result = Generate("using TheLithium.Imprint; public enum State { A=0, B=0, C=1 } public class Tests { public void Test() => Snapshots.Run(() => State.B.AssertSnapshot()); }");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void GenericIncludesRootWriters()
    {
        var result = Generate("""
            using TheLithium.Imprint;
            [assembly: SnapshotInclude<Result>]
            public record Result(int Value);
            public class Tests { public void Capture<T>(T value) => value.AssertSnapshot(); }
            """);
        Assert.Empty(result.Errors);
        Assert.Contains("default(global::Result)", result.Source);
    }

    [Fact]
    public void InaccessibleBaseContractDoesNotBreakGeneratedCompilation()
    {
        var result = Generate("""
            using TheLithium.Imprint;
            public class Container
            {
                private class Parent { public int Value { get; set; } }
                private class Child : Parent { }
                public void Test() => Snapshots.Run(() => new Child().AssertSnapshot());
            }
            """);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Diagnostics, d => d.Id == "IMP001");
    }

    [Fact]
    public void MemberEditsRegenerateWriters()
    {
        var before = Generate("using TheLithium.Imprint; public record Model(int A); public class Tests { public void Test() => Snapshots.Run(() => new Model(1).AssertSnapshot()); }");
        var after = Generate("using TheLithium.Imprint; public record Model(int A, int B); public class Tests { public void Test() => Snapshots.Run(() => new Model(1, 2).AssertSnapshot()); }");
        Assert.Empty(before.Errors);
        Assert.Empty(after.Errors);
        Assert.DoesNotContain("WritePropertyName(\"B\")", before.Source);
        Assert.Contains("WritePropertyName(\"B\")", after.Source);
    }

    [Fact]
    public void DerivedFrameworkAttributeKeepsDisplayMetadataAvailable()
    {
        var result = Generate("""
            using TheLithium.Imprint;
            namespace Xunit { public class FactAttribute : System.Attribute { public string DisplayName { get; set; } } }
            public class ScenarioAttribute : Xunit.FactAttribute { }
            public class Tests {
                [Scenario(DisplayName = "Custom scenario")]
                public void Test() => Snapshots.Run(() => 1.AssertSnapshot());
            }
            """);
        Assert.Empty(result.Errors);
        Assert.True(result.Source.Contains("\"Custom scenario\"", StringComparison.Ordinal), result.Source);
        Assert.Contains("\"Tests\", \"Test\"", result.Source);
    }

    [Fact]
    public void RowDisplayNamesDoNotRenameTheWholeMethod()
    {
        var result = Generate("""
            using TheLithium.Imprint;
            namespace Microsoft.VisualStudio.TestTools.UnitTesting {
                public class DataRowAttribute : System.Attribute { public string DisplayName { get; set; } }
            }
            public class Tests {
                [Microsoft.VisualStudio.TestTools.UnitTesting.DataRow(DisplayName = "Only one row")]
                public void Test(int value) => Snapshots.Run(() => value.AssertSnapshot());
            }
            """);
        Assert.Empty(result.Errors);
        Assert.DoesNotContain("Only one row", result.Source);
        Assert.Contains("\"Tests\", \"Test\"", result.Source);
    }

    [Fact]
    public void ConstructorDisplayNameIsKeptAsAnOptInName()
    {
        var result = Generate("""
            using TheLithium.Imprint;
            namespace Microsoft.VisualStudio.TestTools.UnitTesting {
                public class TestMethodAttribute : System.Attribute { public TestMethodAttribute(string displayName) { } }
            }
            public class Tests {
                [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod("Legacy display name")]
                public void Test() => Snapshots.Run(() => 1.AssertSnapshot());
            }
            """);
        Assert.Empty(result.Errors);
        Assert.Contains("\"Legacy display name\"", result.Source);
        Assert.Contains("\"Tests\", \"Test\"", result.Source);
    }

    [Fact]
    public void DescriptionAttributeIsKeptAsAnOptInName()
    {
        var result = Generate("""
            using TheLithium.Imprint;
            using System.ComponentModel;
            public class Tests
            {
                [Description("Readable description")]
                public void Test() => Snapshots.Run(() => 1.AssertSnapshot());
            }
            """);
        Assert.Empty(result.Errors);
        Assert.Contains("\"Readable description\"", result.Source);
        Assert.Contains("\"Tests\", \"Test\"", result.Source);
    }

    [Fact]
    public void UnknownFrameworkDisplayMetadataIsKeptAsAnOptInName()
    {
        var result = Generate("""
            using TheLithium.Imprint;
            namespace Other.Framework
            {
                public sealed class ScenarioAttribute : System.Attribute
                {
                    public string? DisplayName { get; set; }
                }
            }
            public class Tests
            {
                [Other.Framework.Scenario(DisplayName = "Custom scenario")]
                public void Test() => Snapshots.Run(() => 1.AssertSnapshot());
            }
            """);
        Assert.Empty(result.Errors);
        Assert.True(result.Source.Contains("\"Custom scenario\"", StringComparison.Ordinal), result.Source);
        Assert.Contains("\"Tests\", \"Test\"", result.Source);
    }

    private static GenerationResult Generate(string source)
    {
        source = """
            using TheLithium.Imprint.Configuration;
            using TheLithium.Imprint.Configuration.Models;
            using TheLithium.Imprint.Serialization;
            """ + "\n" + source;
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create("Consumer", [CSharpSyntaxTree.ParseText(source, options, "Tests.cs")],
            References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new SnapshotGenerator().AsSourceGenerator()], parseOptions: options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var result = driver.GetRunResult();
        return new(string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString())),
            output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(), diagnostics.ToArray());
    }

    private sealed record GenerationResult(string Source, Diagnostic[] Errors, Diagnostic[] Diagnostics);
}
