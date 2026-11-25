using System.Globalization;

namespace TheLithium.Imprint.Visual.Tools;

internal static class ProcessTree
{
    /// <summary>A process and its descendants, for an attached app that shares its host's process group.</summary>
    internal static async Task<IReadOnlySet<int>> OfAsync(int root, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return Descendants(root, Platforms.Windows.WindowsNative.ProcessParents());
        }
        var processes = await UnixProcessesAsync(cancellationToken).ConfigureAwait(false);
        return Descendants(root, processes.Select(process => (process.Id, process.Parent)));
    }

    /// <summary>Live members of a private Unix process group, including children whose launcher has exited.</summary>
    internal static async Task<IReadOnlySet<int>> GroupAsync(int group, CancellationToken cancellationToken)
    {
        var processes = await UnixProcessesAsync(cancellationToken).ConfigureAwait(false);
        return processes.Where(process => process.Group == group).Select(process => process.Id).ToHashSet();
    }

    internal static HashSet<int> Descendants(int root, IEnumerable<(int Id, int Parent)> pairs)
    {
        var children = pairs.Where(pair => pair.Id != pair.Parent).ToLookup(pair => pair.Parent, pair => pair.Id);
        var tree = new HashSet<int> { root };
        var pending = new Stack<int>([root]);
        while (pending.Count != 0)
        {
            foreach (var child in children[pending.Pop()])
            {
                if (tree.Add(child))
                {
                    pending.Push(child);
                }
            }
        }
        return tree;
    }

    private static async Task<List<(int Id, int Parent, int Group)>> UnixProcessesAsync(CancellationToken cancellationToken)
    {
        var processes = new List<(int, int, int)>();
        if (OperatingSystem.IsLinux())
        {
            foreach (var directory in Directory.EnumerateDirectories("/proc"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Pid(Path.GetFileName(directory), out var id))
                {
                    continue;
                }
                string stat;
                try
                {
                    stat = File.ReadAllText(Path.Combine(directory, "stat"));
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                // The command in parentheses can contain spaces and closing parentheses.
                var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ', 4);
                if (fields.Length >= 3 && fields[0] != "Z" && Pid(fields[1], out var parent) && Pid(fields[2], out var group))
                {
                    processes.Add((id, parent, group));
                }
            }
            return processes;
        }

        var text = await ToolProcess.RunTextAsync("/bin/ps", ["-A", "-o", "pid=", "-o", "ppid=", "-o", "pgid=", "-o", "stat="],
            ToolRun.Text(TimeSpan.FromSeconds(10)), cancellationToken).ConfigureAwait(false);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && parts[3][0] != 'Z' && Pid(parts[0], out var id)
                && Pid(parts[1], out var parent) && Pid(parts[2], out var group))
            {
                processes.Add((id, parent, group));
            }
        }
        return processes;
    }

    private static bool Pid(string text, out int pid) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out pid);
}
