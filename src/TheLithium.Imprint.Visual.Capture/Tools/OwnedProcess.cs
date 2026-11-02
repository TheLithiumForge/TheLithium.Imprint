using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TheLithium.Imprint.Visual.Tools;

/// <summary>
/// A long-running process the capture started, such as the app or its display server. It keeps the last lines of
/// output for error messages, and stops politely first, then kills the whole tree.
/// </summary>
internal sealed partial class OwnedProcess : IAsyncDisposable
{
    private const int TailLines = 40;
    private readonly Process _process;
    private readonly Queue<string> _tail = new();
    private readonly string _name;
    private int _disposed;

    private OwnedProcess(Process process, string name)
    {
        _process = process;
        _name = name;
    }

    internal int Id => _process.Id;

    internal bool HasExited => _process.HasExited;

    internal static OwnedProcess Start(string file, IEnumerable<string> arguments, IReadOnlyDictionary<string, string?> environment, string? workingDirectory)
    {
        var start = new ProcessStartInfo(file)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (workingDirectory is not null)
        {
            start.WorkingDirectory = workingDirectory;
        }
        foreach (var argument in arguments)
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
        var owned = new OwnedProcess(process, Path.GetFileName(file));
        process.OutputDataReceived += (_, line) => owned.Keep(line.Data);
        process.ErrorDataReceived += (_, line) => owned.Keep(line.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();
        return owned;
    }

    /// <summary>The last lines the process wrote, for an error message.</summary>
    internal string Tail()
    {
        lock (_tail)
        {
            return string.Join(Environment.NewLine, _tail);
        }
    }

    /// <summary>Throws with the process's output when it exited before it should have.</summary>
    internal void EnsureRunning(string waitingFor)
    {
        if (_process.HasExited)
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
            if (!_process.HasExited)
            {
                Terminate();
                using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await _process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }
            ToolProcess.Kill(_process);
        }
        finally
        {
            _process.Dispose();
        }
    }

    private void Keep(string? line)
    {
        if (line is null)
        {
            return;
        }
        lock (_tail)
        {
            _tail.Enqueue(line);
            if (_tail.Count > TailLines)
            {
                _tail.Dequeue();
            }
        }
    }

    /// <summary>Asks the process to stop: SIGTERM on Linux and macOS, the main window's close on Windows.</summary>
    private void Terminate()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _process.CloseMainWindow();
            }
            else
            {
                _ = SendSignal(_process.Id, Terminated);
            }
        }
        catch (InvalidOperationException) { }
    }

    private const int Terminated = 15;

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int SendSignal(int processId, int signal);
}
