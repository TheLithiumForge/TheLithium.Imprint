using System.Diagnostics;
using System.Text;

namespace TheLithium.Imprint.Visual.Tools;

/// <summary>
/// Runs a command-line tool to completion with an argument list, so no shell parses it on the host. Output is
/// bounded, both streams are drained at once so neither can block the other, and a tool that outlives its
/// timeout is killed with its children.
/// </summary>
internal static class ToolProcess
{
    private const int ErrorBytes = 64 * 1024;

    internal static async Task<ToolResult> RunAsync(string file, IReadOnlyList<string> arguments, ToolRun run, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(file)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        foreach (var variable in run.Environment)
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

        using var process = Process.Start(start) ?? throw new SnapshotCaptureException($"Couldn't start {file}.");
        process.StandardInput.Close();
        var output = ReadAsync(process.StandardOutput.BaseStream, run.MaxOutputBytes);
        var errors = ReadAsync(process.StandardError.BaseStream, ErrorBytes);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(run.Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new SnapshotCaptureException($"{Path.GetFileName(file)} didn't finish within {run.Timeout.TotalSeconds:0.#} seconds: {string.Join(' ', arguments)}");
        }

        var (bytes, overflow) = await output.ConfigureAwait(false);
        var (errorBytes, _) = await errors.ConfigureAwait(false);
        if (overflow)
        {
            throw new SnapshotCaptureException($"{Path.GetFileName(file)} wrote more than {run.MaxOutputBytes} bytes.");
        }
        return new(process.ExitCode, bytes, Encoding.UTF8.GetString(errorBytes).Trim());
    }

    /// <summary>Runs a tool that must succeed, and returns its standard output as trimmed text.</summary>
    internal static async Task<string> RunTextAsync(string file, IReadOnlyList<string> arguments, ToolRun run, CancellationToken cancellationToken)
    {
        var result = await RunAsync(file, arguments, run, cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess(file, arguments);
        return result.Text;
    }

    internal static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    /// <summary>Reads a stream to its end, keeping at most <paramref name="maximum"/> bytes but always draining it.</summary>
    private static async Task<(byte[] Bytes, bool Overflow)> ReadAsync(Stream stream, int maximum)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        var overflow = false;
        int read;
        while ((read = await stream.ReadAsync(chunk).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > maximum)
            {
                overflow = true;
                continue;
            }
            buffer.Write(chunk, 0, read);
        }
        return (buffer.ToArray(), overflow);
    }
}

/// <summary>Limits for one tool run. A null environment value removes the variable.</summary>
internal sealed record ToolRun(TimeSpan Timeout, int MaxOutputBytes, IReadOnlyDictionary<string, string?> Environment)
{
    internal static ToolRun Text(TimeSpan timeout) => new(timeout, 4 * 1024 * 1024, new Dictionary<string, string?>());
}

internal sealed record ToolResult(int ExitCode, byte[] Output, string Errors)
{
    internal string Text => Encoding.UTF8.GetString(Output).Trim();

    internal void EnsureSuccess(string file, IReadOnlyList<string> arguments)
    {
        if (ExitCode != 0)
        {
            var detail = Errors.Length != 0 ? Errors : Text;
            throw new SnapshotCaptureException($"{Path.GetFileName(file)} {string.Join(' ', arguments)} failed with exit code {ExitCode}: {detail}");
        }
    }
}
