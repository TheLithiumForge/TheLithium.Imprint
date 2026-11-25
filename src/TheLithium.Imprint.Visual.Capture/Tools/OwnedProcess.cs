using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace TheLithium.Imprint.Visual.Tools;

/// <summary>A Unix app or display server. Its process group survives a launcher that exits before the app.</summary>
internal sealed partial class OwnedProcess : IAsyncDisposable
{
    private const int TailLines = 40;

    // Bash creates the group before any app code runs. One member waits on our private input pipe. A line asks it
    // to terminate the group. EOF kills the group even if the test host died or the launcher PID was recycled.
    // The command and its arguments are positional parameters, including empty arguments and literal shell syntax.
    private const string Launch = """
        set -m
        (
            set +m
            IFS= read -r || exit 1
            (
                trap '' TERM
                if IFS= read -r; then
                    kill -TERM 0
                    IFS= read -r
                fi
                kill -KILL 0
            ) <&0 &
            exec -- "$@" </dev/null
        ) <&0 &
        group=$!
        set +m
        printf '%d\n' "$group"
        wait "$group"
        """;

    private readonly Process _process;
    private readonly Task _output;
    private readonly Queue<string> _tail = new();
    private readonly string _name;
    private int _disposed;

    private OwnedProcess(Process process, int group, string name)
    {
        _process = process;
        Id = group;
        _name = name;
        _output = Task.WhenAll(ReadLinesAsync(process.StandardOutput), ReadLinesAsync(process.StandardError));
    }

    internal int Id { get; }
    internal bool HasExited => _process.HasExited;

    internal Task<IReadOnlySet<int>> MembersAsync(CancellationToken cancellationToken)
        => ProcessTree.GroupAsync(Id, cancellationToken);

    internal static OwnedProcess Start(string file, IEnumerable<string> arguments, IReadOnlyDictionary<string, string?> environment, string? workingDirectory)
    {
        var start = new ProcessStartInfo("/bin/bash")
        {
            WorkingDirectory = workingDirectory ?? "",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        // -p ignores startup scripts and inherited shell functions. The app still gets the requested environment.
        foreach (var argument in new[] { "-p", "-c", Launch, "imprint", file }.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }
        foreach (var variable in environment)
        {
            if (variable.Value is null)
            {
                start.Environment.Remove(variable.Key);
            }
            else
            {
                start.Environment[variable.Key] = variable.Value;
            }
        }

        var process = Process.Start(start) ?? throw new SnapshotCaptureException($"Couldn't start {file}.");
        try
        {
            // The subshell waits for this acknowledgement, so the first line belongs to Bash and the group is live.
            if (!int.TryParse(process.StandardOutput.ReadLine(), NumberStyles.None, CultureInfo.InvariantCulture, out var group)
                || group <= 1 || GetProcessGroup(group) != group)
            {
                throw new SnapshotCaptureException($"Couldn't create a private process group for {file}.");
            }
            var owned = new OwnedProcess(process, group, Path.GetFileName(file));
            process.StandardInput.WriteLine("start");
            process.StandardInput.Flush();
            return owned;
        }
        catch
        {
            process.StandardInput.Close();
            process.Dispose();
            throw;
        }
    }

    internal string Tail()
    {
        lock (_tail)
        {
            return string.Join(Environment.NewLine, _tail);
        }
    }

    internal void EnsureRunning(string waitingFor)
    {
        if (HasExited)
        {
            throw new SnapshotCaptureException($"{_name} exited with code {_process.ExitCode} before {waitingFor}. Its last output:{Environment.NewLine}{Tail()}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.StandardInput.WriteLineAsync("stop").ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(grace.Token).ConfigureAwait(false);
            // The process watching stdin stays in the group throughout the grace period.
            while ((await MembersAsync(grace.Token).ConfigureAwait(false)).Count > 1)
            {
                await Task.Delay(50, grace.Token).ConfigureAwait(false);
            }
        }
        catch (IOException) { } // The group already exited and closed its pipe.
        catch (OperationCanceledException) { }
        finally
        {
            try
            {
                _process.StandardInput.BaseStream.Dispose();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                await _output.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _process.StandardOutput.Dispose();
                _process.StandardError.Dispose();
                _process.Dispose();
            }
        }
    }

    private async Task ReadLinesAsync(StreamReader stream)
    {
        while (await stream.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            lock (_tail)
            {
                _tail.Enqueue(line);
                if (_tail.Count > TailLines)
                {
                    _tail.Dequeue();
                }
            }
        }
    }

    [LibraryImport("libc", EntryPoint = "getpgid")]
    private static partial int GetProcessGroup(int processId);
}
