using TheLithium.Imprint.Visual.Tools;

namespace TheLithium.Imprint.Visual.Platforms.Ios;

/// <summary>Asks a separate XCTest runner whether the target app is in the foreground on this simulator.</summary>
internal sealed class IosForeground(string udid, string bundleId) : IAsyncDisposable
{
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromMinutes(3);
    private readonly string _runnerId = "dev.thelithium.imprint.foreground.s" + Guid.NewGuid().ToString("N");
    private DirectoryInfo? _workspace;
    private string? _channel;
    private OwnedProcess? _runner;

    internal string FrameFile => Path.Combine(_workspace!.FullName, "frame.png");

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        _workspace = Directory.CreateTempSubdirectory("imprint-ios-");
        var project = Path.Combine(_workspace.FullName, "ImprintForeground.xcodeproj");
        var scheme = Path.Combine(project, "xcshareddata", "xcschemes");
        Directory.CreateDirectory(scheme);
        foreach (var (resource, path) in new[]
        {
            ("Foreground.m", Path.Combine(_workspace.FullName, "Foreground.m")),
            ("project.pbxproj", Path.Combine(project, "project.pbxproj")),
            ("ImprintForeground.xcscheme", Path.Combine(scheme, "ImprintForeground.xcscheme"))
        })
        {
            using var source = typeof(IosForeground).Assembly.GetManifestResourceStream("Imprint.Ios." + resource)
                ?? throw new SnapshotCaptureException($"The capture package is missing the XCTest resource {resource}.");
            await using var target = File.Create(path);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        // Each runner gets its own app container. Only this session installs or removes its bundle ID.
        var environment = new Dictionary<string, string?> { ["TEST_RUNNER_IMPRINT_BUNDLE_ID"] = bundleId };
        string[] arguments = ["xcodebuild", "test", "-project", project, "-scheme", "ImprintForeground", "-destination", $"id={udid}",
            "-derivedDataPath", Path.Combine(_workspace.FullName, "build"), "-parallel-testing-enabled", "NO", "-test-timeouts-enabled", "NO",
            "-quiet", "PRODUCT_BUNDLE_IDENTIFIER=" + _runnerId];
        _runner = OwnedProcess.Start("/usr/bin/xcrun", arguments, environment, _workspace.FullName);

        using var setup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        setup.CancelAfter(SetupTimeout);
        try
        {
            while (_channel is null || !File.Exists(Path.Combine(_channel, "ready")))
            {
                _runner.EnsureRunning("XCTest could check the foreground app on " + udid);
                if (_channel is null)
                {
                    var container = await IosSimulatorDriver.SimctlAsync(["get_app_container", udid, _runnerId + ".xctrunner", "data"],
                        ToolRun.Text(SetupTimeout), setup.Token).ConfigureAwait(false);
                    if (container.ExitCode == 0)
                    {
                        _channel = Path.Combine(container.Text, "tmp", "imprint");
                    }
                }
                await Task.Delay(250, setup.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SnapshotCaptureException($"XCTest didn't start its foreground check on {udid} within {SetupTimeout.TotalMinutes:0} minutes. {_runner.Tail()}");
        }
    }

    internal async Task<bool> IsForegroundAsync(CancellationToken cancellationToken)
    {
        var query = Guid.NewGuid().ToString("N");
        var request = Path.Combine(_channel!, "request");
        var response = Path.Combine(_channel!, query);
        // Publish a complete request. A cancelled poll's answer uses its own name and cannot satisfy a later poll.
        await File.WriteAllTextAsync(request + ".tmp", query, cancellationToken).ConfigureAwait(false);
        File.Move(request + ".tmp", request, overwrite: true);
        while (!File.Exists(response))
        {
            _runner!.EnsureRunning("XCTest could check the foreground app on " + udid);
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
        var state = await File.ReadAllTextAsync(response, cancellationToken).ConfigureAwait(false);
        File.Delete(response);
        return state == "foreground";
    }

    internal async Task RequireForegroundAsync(CancellationToken cancellationToken)
    {
        if (!await IsForegroundAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new SnapshotCaptureException($"{bundleId} is no longer the foreground app on {udid}.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_runner is not null)
            {
                // Give XCTest time to finish its test before cancelling xcodebuild or removing the runner.
                try
                {
                    if (_channel is not null && Directory.Exists(_channel))
                    {
                        using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await File.WriteAllTextAsync(Path.Combine(_channel, "stop"), "", grace.Token).ConfigureAwait(false);
                        while (!_runner.HasExited)
                        {
                            await Task.Delay(100, grace.Token).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    await _runner.DisposeAsync().ConfigureAwait(false);
                    await IosSimulatorDriver.SimctlAsync(["uninstall", udid, _runnerId + ".xctrunner"],
                        ToolRun.Text(TimeSpan.FromSeconds(30)), CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _workspace?.Delete(recursive: true);
        }
    }
}
