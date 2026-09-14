using System.Diagnostics;

namespace TheLithium.Imprint.Specifications;

/// <summary>Owns one fixture link; disposal removes the link itself, never its target.</summary>
internal sealed class DirectoryLink : IDisposable
{
    private readonly string _path;

    internal DirectoryLink(string path, string target)
    {
        _path = path;
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(path, target);
            return;
        }

        // Junction creation works on CI without the Windows symbolic-link privilege.
        // Pass fixture paths as environment data, not interpolated shell code.
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("New-Item -ItemType Junction -Path $env:IMPRINT_FIXTURE_LINK -Target $env:IMPRINT_FIXTURE_TARGET -ErrorAction Stop | Out-Null");
        start.Environment["IMPRINT_FIXTURE_LINK"] = path;
        start.Environment["IMPRINT_FIXTURE_TARGET"] = target;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the fixture junction creator.");
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Fixture junction creation timed out.");
        }
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Fixture junction creation failed: {process.StandardError.ReadToEnd()}");
        }
    }

    public void Dispose() => Directory.Delete(_path);
}
