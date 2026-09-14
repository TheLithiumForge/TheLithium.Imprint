using System.Text.Json;
using TheLithium.Imprint.Comparison;
using Xunit;

namespace TheLithium.Imprint.Tests;

public sealed class JsonComparisonTests
{
    [Fact]
    public void DirectComparerCallsValidateConcreteRules()
    {
        var comparer = DefaultSnapshotComparer.Instance;
        Assert.Throws<SnapshotConfigurationException>(() => comparer.Compare("1", "1", SnapshotFormat.Json, new() { NumericTolerance = -1 }));
        Assert.Throws<SnapshotConfigurationException>(() => comparer.Compare("[]", "[]", SnapshotFormat.Json, new() { MaxUnorderedArrayLength = 0 }));
        Assert.Throws<SnapshotConfigurationException>(() => comparer.Compare("[]", "[]", SnapshotFormat.Json, new() { MaxUnorderedArrayLength = 1025 }));
    }

    [Theory]
    [InlineData("1", "1.0", true)]
    [InlineData("1e1000000", "10e999999", true)]
    [InlineData("1e-1000000", "0.1e-999999", true)]
    [InlineData("-0", "0.0", true)]
    [InlineData("9007199254740992", "9007199254740993", false)]
    public void FirstPartyExactArithmeticMatchesTheSupportedBoundary(string left, string right, bool equal)
    {
        using var expected = JsonDocument.Parse(left);
        using var received = JsonDocument.Parse(right);
        Assert.Equal(equal, JsonElement.DeepEquals(expected.RootElement, received.RootElement));
        Assert.Equal(equal, DefaultSnapshotComparer.Instance.Compare(left, right, SnapshotFormat.Json, new()).Equal);
    }

    [Fact]
    public void UnsupportedExponentHasAnActionableBoundedFailure()
        => Assert.Throws<SnapshotException>(() => DefaultSnapshotComparer.Instance.Compare("1e1000001", "1e9999999999999999999999999", SnapshotFormat.Json, new()));

    [Fact]
    public void MinifiedJsonDiffHasAPathAndSeparateChangedPropertyLines()
    {
        var result = DefaultSnapshotComparer.Instance.Compare(
            "{\"payload\":{\"name\":\"before\",\"unchanged\":42}}",
            "{\"payload\":{\"name\":\"after\",\"unchanged\":42}}", SnapshotFormat.Json, new());
        Assert.False(result.Equal);
        Assert.Contains("$[\"payload\"][\"name\"]", result.Difference);
        Assert.Contains("-     \"name\": \"before\",", result.Difference);
        Assert.Contains("+     \"name\": \"after\",", result.Difference);
    }
}
