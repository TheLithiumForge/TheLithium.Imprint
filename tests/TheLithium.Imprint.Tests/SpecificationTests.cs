using TheLithium.Imprint.Specifications;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TheLithium.Imprint.Tests;

public sealed class SpecificationTests
{
    public static IEnumerable<object[]> Cases => Specs.All.Select(test => new object[] { test.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Specification(string name)
    {
        var variables = new[] { "IMPRINT_UPDATE", "IMPRINT_TEST", "IMPRINT_READ_ONLY", "IMPRINT_CONFIG",
            "IMPRINT_PROJECT_ROOT", "IMPRINT_ALLOW_CI_UPDATE", "CI" };
        var saved = variables.Select(variable => new EnvironmentValue(variable, null)).ToArray();
        try
        {
            await Specs.All.Single(test => test.Name == name).Run();
        }
        finally
        {
            foreach (var variable in saved.Reverse())
            {
                variable.Dispose();
            }
        }
    }
}
