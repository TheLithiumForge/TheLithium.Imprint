using System.Globalization;
using System.Runtime.Versioning;

namespace TheLithium.Imprint.Visual.Tools;

/// <summary>A process and every process it started, so a launcher that starts the real app still finds its window.</summary>
internal static class ProcessTree
{
    internal static async Task<IReadOnlySet<int>> OfAsync(int root, CancellationToken cancellationToken)
    {
        IEnumerable<(int Id, int Parent)> pairs;
        if (OperatingSystem.IsWindows())
        {
            pairs = Platforms.Windows.WindowsNative.ProcessParents();
        }
        else if (OperatingSystem.IsLinux())
        {
            pairs = LinuxParents();
        }
        else
        {
            pairs = await PsParentsAsync(cancellationToken).ConfigureAwait(false);
        }
        return Descendants(root, pairs);
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

    [SupportedOSPlatform("linux")]
    private static IEnumerable<(int, int)> LinuxParents()
    {
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            string stat;
            try
            {
                stat = File.ReadAllText(Path.Combine(directory, "stat"));
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            // The command name is in parentheses and can hold spaces, so fields are counted after the last ')'.
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            if (fields.Length > 1 && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parent))
            {
                yield return (id, parent);
            }
        }
    }

    private static async Task<IEnumerable<(int, int)>> PsParentsAsync(CancellationToken cancellationToken)
    {
        var text = await ToolProcess.RunTextAsync("/bin/ps", ["-A", "-o", "pid=", "-o", "ppid="], ToolRun.Text(TimeSpan.FromSeconds(10)), cancellationToken)
            .ConfigureAwait(false);
        var pairs = new List<(int, int)>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parent))
            {
                pairs.Add((id, parent));
            }
        }
        return pairs;
    }
}
