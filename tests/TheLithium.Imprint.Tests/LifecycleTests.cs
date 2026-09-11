using System.Runtime.CompilerServices;
using TheLithium.Imprint.Specifications;
using Xunit;

namespace TheLithium.Imprint.Tests;

/// <summary>Invokes instrumented subjects through delegates so each subject owns its own lifetime.</summary>
public sealed class LifecycleTests : IDisposable
{
    private readonly Fixture _fixture = new();
    private readonly EnvironmentValue _project;
    private readonly EnvironmentValue _ci = new("CI", "false");

    public LifecycleTests()
    {
        _project = new("IMPRINT_PROJECT_ROOT", _fixture.Root);
        Subjects.Fail = false;
        Subjects.Value = 1;
        Subjects.Directory = string.Empty;
    }

    [Fact]
    public void MissingBaselineIsCreatedOnlyAfterBodyAndFinallySucceed()
    {
        Action body = Subjects.ReturnsThroughFinally;
        body();
        Assert.Equal("1\n", File.ReadAllText(Path.Combine(Subjects.Directory, "value.json")));
        Assert.Equal("finished", File.ReadAllText(Path.Combine(Subjects.Directory, "finally.txt")));
    }

    [Fact]
    public void FailureAfterCaptureDoesNotCreateBaseline()
    {
        Action body = Subjects.FailsAfterCapture;
        Assert.Throws<InvalidOperationException>(body);
        Assert.False(File.Exists(Path.Combine(Subjects.Directory, "value.json")));
    }

    [Fact]
    public void FailedFinallyCannotApproveAnUpdate()
    {
        Action body = Subjects.UpdatesWithFinally;
        body();
        Subjects.Value = 2;
        Subjects.Fail = true;
        Assert.Throws<InvalidOperationException>(body);
        Assert.Equal("1\n", File.ReadAllText(Path.Combine(Subjects.Directory, "value.json")));
    }

    [Fact]
    public async Task FailedAsyncFinallyCannotApproveAnUpdate()
    {
        Func<Task> body = Subjects.UpdatesWithAsyncFinally;
        await body();
        Subjects.Value = 2;
        Subjects.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(body);
        Assert.Equal("1\n", File.ReadAllText(Path.Combine(Subjects.Directory, "value.json")));
    }

    [Fact]
    public void HelperCaptureBelongsToItsCallingTest()
    {
        Action body = Subjects.FailsAfterHelper;
        Assert.Throws<InvalidOperationException>(body);
        Assert.False(Directory.Exists(Path.Combine(_fixture.Root, "__snapshots__")));
    }

    [Fact]
    public void ExistingDifferencesStillFail()
    {
        Action body = Subjects.VerifiesValue;
        body();
        Subjects.Value = 2;
        var error = Assert.Throws<SnapshotMismatchException>(body);
        Assert.Contains("value.json", error.Message);
        Assert.Equal("1\n", File.ReadAllText(Path.Combine(Subjects.Directory, "value.json")));
    }

    [Fact]
    public void PreservesCallerMemberName()
    {
        Action body = Subjects.ChecksCallerName;
        body();
    }

    [Fact]
    public async Task SupportsExpressionBodiesAndGenericTaskResults()
    {
        Func<Task<int>> task = Subjects.TaskResult;
        Assert.Equal(42, await task());
        Func<ValueTask<int>> valueTask = Subjects.ValueTaskResult;
        Assert.Equal(43, await valueTask());
        Func<Task> expression = Subjects.ExpressionTask;
        await expression();
    }

    public void Dispose()
    {
        _project.Dispose();
        _ci.Dispose();
        _fixture.Dispose();
    }

    private static class Subjects
    {
        internal static bool Fail;
        internal static int Value;
        internal static string Directory = string.Empty;

        internal static void ReturnsThroughFinally()
        {
            Directory = Snapshots.Current.BaselineDirectory;
            try
            {
                1.AssertSnapshot("value");
                Assert.False(File.Exists(Path.Combine(Directory, "value.json")));
                return;
            }
            finally
            {
                "finished".AssertSnapshot("finally");
                Assert.False(File.Exists(Path.Combine(Directory, "finally.txt")));
            }
        }

        internal static void FailsAfterCapture()
        {
            Directory = Snapshots.Current.BaselineDirectory;
            1.AssertSnapshot("value");
            throw new InvalidOperationException("Later assertion failed.");
        }

        internal static void UpdatesWithFinally()
        {
            Directory = Snapshots.Current.BaselineDirectory;
            try
            {
                Value.UpdateSnapshot("value");
                return;
            }
            finally
            {
                if (Fail)
                {
                    throw new InvalidOperationException("Finally failed.");
                }
            }
        }

        internal static async Task UpdatesWithAsyncFinally()
        {
            Directory = Snapshots.Current.BaselineDirectory;
            try
            {
                Value.UpdateSnapshot("value");
                await Task.Yield();
                return;
            }
            finally
            {
                await Task.Yield();
                if (Fail)
                {
                    throw new InvalidOperationException("Async finally failed.");
                }
            }
        }

        internal static void FailsAfterHelper()
        {
            CaptureHelper();
            throw new InvalidOperationException("The caller failed.");
        }

        private static void CaptureHelper() => 1.AssertSnapshot("value");

        internal static void VerifiesValue()
        {
            Directory = Snapshots.Current.BaselineDirectory;
            Value.AssertSnapshot("value");
        }

        internal static void ChecksCallerName()
        {
            Assert.Equal(nameof(ChecksCallerName), CallerName());
            1.AssertSnapshot("value");
        }

        private static string CallerName([CallerMemberName] string member = "") => member;

        internal static async Task<int> TaskResult()
        {
            await Task.Yield();
            42.AssertSnapshot("value");
            return 42;
        }

        internal static async ValueTask<int> ValueTaskResult()
        {
            await Task.Yield();
            43.AssertSnapshot("value");
            return 43;
        }

        [SnapshotSettings]
        internal static async Task ExpressionTask() => await ExpressionHelper();

        private static async Task ExpressionHelper()
        {
            await Task.Yield();
            44.AssertSnapshot("value");
        }
    }
}
