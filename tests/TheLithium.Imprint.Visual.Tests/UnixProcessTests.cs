using System.Globalization;
using TheLithium.Imprint.Visual.Tools;
using Xunit;

namespace TheLithium.Imprint.Visual.Tests;

public sealed class UnixProcessTests
{
    [Fact]
    public async Task AChildIsFoundAndStoppedAfterItsLauncherExits()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var pidFile = Path.Combine(Path.GetTempPath(), $"imprint-launcher-{Guid.NewGuid():N}");
        try
        {
            await using var launcher = OwnedProcess.Start("/bin/sh", ["-c", "sleep 60 & echo $! > \"$1\"", "fixture", pidFile],
                new Dictionary<string, string?>(), null);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!launcher.HasExited)
            {
                await Task.Delay(20, timeout.Token);
            }

            var child = int.Parse(await File.ReadAllTextAsync(pidFile, timeout.Token), CultureInfo.InvariantCulture);
            Assert.Contains(child, await launcher.MembersAsync(timeout.Token));

            await launcher.DisposeAsync();
            Assert.DoesNotContain(child, await ProcessTree.GroupAsync(launcher.Id, timeout.Token));
        }
        finally
        {
            File.Delete(pidFile);
        }
    }
}
