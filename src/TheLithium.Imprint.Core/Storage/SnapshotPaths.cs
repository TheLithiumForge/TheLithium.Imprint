namespace TheLithium.Imprint.Storage;

/// <summary>Shared cooperative filesystem boundary for baselines, recovery and diagnostics.</summary>
internal static class SnapshotPaths
{
    private static readonly string[] MacSystemAliases = ["/tmp", "/var", "/etc"];

    internal static bool Contains(string root, string path)
    {
        var relative = Path.GetRelativePath(CanonicalPath(root), CanonicalPath(path));
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    internal static void RequireSeparate(string first, string second)
    {
        if (Contains(first, second) || Contains(second, first))
        {
            throw new SnapshotConfigurationException($"Baseline, artifact and recovery roots must not overlap: {first} and {second}.");
        }
    }

    internal static void CheckPath(string project, string path)
    {
        CheckLink(Path.GetFullPath(path));
        path = CanonicalPath(path);
        // The project location is supplied by the build or explicit integration. Its
        // ancestors are trusted; links at or below it are not. External roots are
        // checked from the filesystem root, including their configured ancestors.
        var anchor = CanonicalPath(project);
        if (!Contains(anchor, path))
        {
            anchor = Path.GetPathRoot(path)
                ?? throw new SnapshotConfigurationException($"Snapshot path has no filesystem root: {path}.");
        }

        CheckLink(anchor);
        var cursor = anchor;
        foreach (var part in Path.GetRelativePath(anchor, path).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            cursor = Path.Combine(cursor, part);
            CheckLink(cursor);
        }
    }

    internal static void CheckLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new SnapshotConfigurationException($"Symlinks/reparse points inside snapshot paths are not supported: {path}");
            }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static string CanonicalPath(string path)
    {
        path = Path.GetFullPath(path);
        if (OperatingSystem.IsMacOS())
        {
            // Resolve only verified OS aliases. Use the same physical spelling
            // for containment, so /tmp and /private/tmp cannot conceal overlap.
            foreach (var alias in MacSystemAliases)
            {
                if ((path == alias || path.StartsWith($"{alias}/", StringComparison.Ordinal))
                    && Directory.ResolveLinkTarget(alias, returnFinalTarget: true)?.FullName == $"/private{alias}")
                {
                    return $"/private{path}";
                }
            }
        }
        return path;
    }
}
