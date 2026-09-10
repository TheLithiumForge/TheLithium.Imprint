using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace TheLithium.Imprint.Tests;

public sealed class ToolTests
{
    [Fact]
    public async Task ForwardsArgumentsWithoutShellExpansionAndPreservesExitCode()
    {
        var result = await RunTool(["run", "--update", "all", "--test", "Suite.*", "--", "dotnet", Harness,
            "--probe", "17", "argument with spaces", "semi;colon", "$literal", "\"quoted\"", ""]);
        Assert.Equal(17, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("all", json.RootElement.GetProperty("IMPRINT_UPDATE").GetString());
        Assert.Equal("Suite.*", json.RootElement.GetProperty("IMPRINT_TEST").GetString());
        Assert.Equal(new[] { "argument with spaces", "semi;colon", "$literal", "\"quoted\"", "" },
            json.RootElement.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task WholeRunOverrideClearsInheritedSelector()
    {
        var result = await RunTool(["run", "--update", "verify", "--", "dotnet", Harness, "--probe", "0"],
            new() { ["IMPRINT_TEST"] = "old-filter", ["IMPRINT_UPDATE"] = "all" });
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("verify", json.RootElement.GetProperty("IMPRINT_UPDATE").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("IMPRINT_TEST").ValueKind);
    }

    [Fact]
    public async Task ReadOnlyAndCiFlagsReachTheRunner()
    {
        var result = await RunTool(["run", "--read-only", "--allow-ci-update", "--", "dotnet", Harness, "--probe", "0"]);
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("true", json.RootElement.GetProperty("IMPRINT_READ_ONLY").GetString());
        Assert.Equal("true", json.RootElement.GetProperty("IMPRINT_ALLOW_CI_UPDATE").GetString());
    }

    [Theory]
    [InlineData("--update", "invalid")]
    [InlineData("--test", "Suite.*")]
    [InlineData("--unknown", "value")]
    public async Task RejectsInvalidOptions(string option, string value)
    {
        var result = await RunTool(["run", option, value, "--", "dotnet", Harness, "--probe", "0"]);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("imprint --help", result.Error);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task MissingExecutableReturnsAnActionableFailure()
    {
        var result = await RunTool(["run", "--", "imprint-nonexistent-" + Guid.NewGuid().ToString("N")]);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("imprint --help", result.Error);
    }

    private static string Harness => Path.Combine(AppContext.BaseDirectory, "TheLithium.Imprint.Specifications.dll");

    private static async Task<(int ExitCode, string Output, string Error)> RunTool(string[] arguments, Dictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "TheLithium.Imprint.Tool.dll"));
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var name in new[] { "IMPRINT_UPDATE", "IMPRINT_TEST", "IMPRINT_READ_ONLY", "IMPRINT_ALLOW_CI_UPDATE" })
            start.Environment.Remove(name);
        if (environment is not null)
            foreach (var variable in environment) start.Environment[variable.Key] = variable.Value;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output, await error);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
