using System.Text.Json;
using TheLithium.Imprint.Generation;

namespace TheLithium.Imprint.Storage;

/// <summary>
/// Removes snapshot folders that no current test owns, one layer at a time: a test's rows, a suite,
/// a source directory, the project. A layer is pruned only once every test in it has run in this
/// process, at the moment its last test enters, so a filtered run never touches what it did not run.
/// It never throws into a test: a failure is reported and the folder stays.
/// </summary>
internal sealed class SnapshotPruner
{
    internal static SnapshotPruner Process { get; } = new(SnapshotMetadata.All, () => SnapshotMetadata.Version, Console.Error);

    // Ignoring case keeps more folders on a case-sensitive file system, never fewer.
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
    private readonly object _gate = new();
    private readonly Func<SnapshotDescriptor[]> _registry;
    private readonly Func<int> _version;
    private readonly TextWriter _log;
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly Dictionary<SnapshotDescriptor, Entry> _entries = new();
    private readonly HashSet<string> _visited = new(Paths);
    private readonly HashSet<string> _visitedAncestors = new(Paths);
    private readonly Dictionary<string, Project> _projects = new(Paths);

    internal SnapshotPruner(Func<SnapshotDescriptor[]> registry, Func<int> version, TextWriter log)
    {
        _registry = registry;
        _version = version;
        _log = log;
    }

    /// <summary>A scope opened. Its folder is in use, and its test has run.</summary>
    internal void Visited(EffectiveSettings settings)
    {
        lock (_gate)
        {
            try
            {
                var directory = Normalize(settings.TestDirectory);
                _visited.Add(directory);
                for (var parent = Path.GetDirectoryName(directory); parent is not null && _visitedAncestors.Add(parent);)
                {
                    parent = Path.GetDirectoryName(parent);
                }

                if (settings.Descriptor is not { } descriptor)
                {
                    return;
                }

                var entry = EntryFor(descriptor);
                entry.Entered = true;
                entry.Outer = true;
                entry.Directories.Add(directory);
                entry.Variant |= settings.Identity.Variant is not null;
                Advance(descriptor);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                _log.WriteLine($"Imprint could not check for stale snapshot folders: {error.Message}");
            }
        }
    }

    /// <summary>A lifetime method ran inside another test's scope.</summary>
    internal void Entered(string sourceFile, int sourceLine)
    {
        try
        {
            Entered(SnapshotMetadata.Find(sourceFile, sourceLine));
        }
        catch (SnapshotException) { }
    }

    internal void Entered(SnapshotDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                var entry = EntryFor(descriptor);
                if (!entry.Entered)
                {
                    entry.Entered = true;
                    Advance(descriptor);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                _log.WriteLine($"Imprint could not check for stale snapshot folders: {error.Message}");
            }
        }
    }

    private Entry EntryFor(SnapshotDescriptor descriptor)
    {
        if (!_entries.TryGetValue(descriptor, out var entry))
        {
            entry = new();
            _entries.Add(descriptor, entry);
        }

        return entry;
    }

    private void Advance(SnapshotDescriptor descriptor)
    {
        var key = Normalize(descriptor.ProjectDirectory);
        var version = _version();
        var known = _projects.TryGetValue(key, out var project);
        if (!known || project!.Version != version)
        {
            try
            {
                project = Build(descriptor, version, known ? project : null);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SnapshotException)
            {
                // The tests themselves report a broken configuration. Pruning stays off for this project.
                _log.WriteLine($"Imprint could not check for stale snapshot folders: {error.Message}");
                project = Disabled(descriptor, version);
            }

            _projects[key] = project;
            if (project.Mode == SnapshotPrune.Off)
            {
                return;
            }

            foreach (var test in project.Tests)
            {
                Refresh(project, test);
            }
        }
        else if (project.Mode == SnapshotPrune.Off)
        {
            return;
        }
        else if (project.Relocation != Environment.GetEnvironmentVariable("IMPRINT_PROJECT_ROOT"))
        {
            // The project moved during the run, so its snapshot root is not certain.
            project.Blocked = true;
        }
        else if (project.ByDescriptor.TryGetValue(descriptor, out var test))
        {
            Refresh(project, test);
        }

        if (Open(project))
        {
            Drain(project);
        }
    }

    /// <summary>Counts a test toward its layers once it has run, and queues whatever became complete.</summary>
    private void Refresh(Project project, Test test)
    {
        if (!_entries.TryGetValue(test.Descriptor, out var entry))
        {
            return;
        }

        if (!test.Satisfied && !test.Descriptor.Optional && Satisfied(test, entry))
        {
            test.Satisfied = true;
            foreach (var node in Nodes(project, test.Suite))
            {
                if (--project.Waiting[node] == 0)
                {
                    project.Ready.Add(node);
                }
            }
        }

        if (!test.Complete && Complete(test, entry))
        {
            test.Complete = true;
            project.ReadyFamilies.Add(test);
        }
    }

    /// <summary>The test counts toward its layer: it ran, and its case folders can be told apart from other tests' folders.</summary>
    private static bool Satisfied(Test test, Entry entry)
        => entry.Entered && (!test.Descriptor.RequiresCase || test.Reliable || Complete(test, entry));

    /// <summary>The test layer: the test ran on its own and every row of it ran.</summary>
    private static bool Complete(Test test, Entry entry)
        => entry.Outer && !entry.Variant
            && (!test.Descriptor.RequiresCase || test.Descriptor.CaseCount is int rows && entry.Directories.Count >= rows);

    /// <summary>
    /// A method that opens its own scope may write anywhere, so nothing is pruned before it has run.
    /// If it wrote under the snapshot root, its other runtime names are unknown and nothing is pruned at all.
    /// </summary>
    private bool Open(Project project)
    {
        if (project.Blocked)
        {
            return false;
        }

        foreach (var wildcard in project.Wildcards)
        {
            if (!_entries.TryGetValue(wildcard, out var entry) || !entry.Entered)
            {
                return false;
            }

            if (!wildcard.Explicit || entry.Directories.Any(directory => SnapshotPaths.Contains(project.Root, directory)))
            {
                project.Blocked = true;
                return false;
            }
        }

        return true;
    }

    private void Drain(Project project)
    {
        var before = project.Folders.Count;
        foreach (var node in project.Ready.OrderBy(node => node.Length))
        {
            Evaluate(project, node);
        }

        project.Ready.Clear();
        foreach (var test in project.ReadyFamilies)
        {
            EvaluateFamily(project, test);
        }

        project.ReadyFamilies.Clear();
        if (project.Folders.Count != before)
        {
            Publish(project, before);
        }
    }

    /// <summary>Every test in this layer has run, so any folder in it that no current test owns is stale.</summary>
    private void Evaluate(Project project, string node)
    {
        for (var current = node; current is not null; current = Path.GetDirectoryName(current))
        {
            if (project.Pruned.Contains(current))
            {
                return;
            }

            if (Paths.Equals(current, project.Root))
            {
                break;
            }
        }

        project.Pruned.Add(node);
        foreach (var folder in Leaves(node).ToArray())
        {
            if (_visited.Contains(folder) || _visitedAncestors.Contains(folder) || project.Known.Contains(folder))
            {
                continue;
            }

            var owners = Owners(project, folder);
            if (owners.Count != 0)
            {
                if (owners.All(owner => owner.Complete))
                {
                    Remove(project, folder, "The test ran every row, and none of them used this folder.");
                }

                continue;
            }

            if (!project.RootOwned && Paths.Equals(NearestKnown(project, folder), project.Root))
            {
                Keep(project, folder, "The snapshot root is configured with files.snapshotRootPath and may be shared with another project.");
                continue;
            }

            Remove(project, folder, "No current test owns this folder.");
        }
    }

    /// <summary>The test layer: every row ran, so a row folder no row used is stale.</summary>
    private void EvaluateFamily(Project project, Test test)
    {
        if (!Directory.Exists(test.Suite))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(test.Suite).Select(Normalize).ToArray())
        {
            if (_visited.Contains(folder) || project.Known.Contains(folder) || IsLink(folder)
                || Directory.EnumerateDirectories(folder).Any())
            {
                continue;
            }

            var owners = Owners(project, folder);
            if (owners.Contains(test) && owners.All(owner => owner.Complete))
            {
                Remove(project, folder, "The test ran every row, and none of them used this folder.");
            }
        }
    }

    private static List<Test> Owners(Project project, string folder)
    {
        var owners = new List<Test>();
        var parent = Path.GetDirectoryName(folder);
        if (parent is null || !project.BySuite.TryGetValue(parent, out var tests))
        {
            return owners;
        }

        var name = Path.GetFileName(folder);
        foreach (var test in tests)
        {
            if (Paths.Equals(name, test.Plain) || name.StartsWith(test.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                owners.Add(test);
            }
        }

        return owners;
    }

    private static string NearestKnown(Project project, string folder)
    {
        var current = Path.GetDirectoryName(folder);
        while (current is not null && !project.Known.Contains(current) && !Paths.Equals(current, project.Root))
        {
            current = Path.GetDirectoryName(current);
        }

        return current ?? project.Root;
    }

    /// <summary>Folders without subfolders below the node. Links are never entered.</summary>
    private static IEnumerable<string> Leaves(string node)
    {
        if (!Directory.Exists(node) || IsLink(node))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(node);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            var children = Directory.EnumerateDirectories(current).Select(Normalize).Where(child => !IsLink(child)).ToArray();
            if (children.Length == 0 && !Paths.Equals(current, node))
            {
                yield return current;
            }

            foreach (var child in children)
            {
                pending.Push(child);
            }
        }
    }

    private void Remove(Project project, string folder, string reason)
    {
        if (project.Folders.ContainsKey(folder))
        {
            return;
        }

        try
        {
            if (project.Mode == SnapshotPrune.Report || project.ReadOnly || project.Conditional)
            {
                if (HoldsOnlySnapshots(folder))
                {
                    project.Folders.Add(folder, new("found", reason));
                }
                else
                {
                    Keep(project, folder, "The folder holds files that are not snapshots.");
                }

                return;
            }

            SnapshotPaths.CheckPath(project.Directory, folder);
            var lockPath = SnapshotStore.LockPath(project.Storage, folder);
            var lockDirectory = Path.GetDirectoryName(lockPath)!;
            SnapshotPaths.CheckPath(project.Directory, lockDirectory);
            Directory.CreateDirectory(lockDirectory);
            SnapshotPaths.CheckLink(lockPath);
            using var held = SnapshotStore.WaitForLock(lockPath, project.LockTimeout, folder, CancellationToken.None);
            if (Directory.Exists(SnapshotStore.JournalPath(project.Storage, folder)))
            {
                Keep(project, folder, "An interrupted update of this folder needs recovery first.");
                return;
            }

            if (!Directory.Exists(folder))
            {
                return;
            }

            if (!HoldsOnlySnapshots(folder))
            {
                Keep(project, folder, "The folder holds files that are not snapshots.");
                return;
            }

            foreach (var file in Directory.GetFiles(folder))
            {
                SnapshotPaths.CheckLink(file);
                File.Delete(file);
            }

            Directory.Delete(folder);
            project.Folders.Add(folder, new("removed", reason));
            RemoveEmptyParents(project, folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SnapshotException)
        {
            Keep(project, folder, error.Message);
        }
    }

    private static void Keep(Project project, string folder, string reason) => project.Folders.TryAdd(folder, new("kept", reason));

    private static bool HoldsOnlySnapshots(string folder) => !Directory.EnumerateDirectories(folder).Any()
        && Directory.EnumerateFiles(folder).All(file => SnapshotFileNames.IsSnapshotFile(file)
            || Path.GetFileName(file).StartsWith(".imprint-tmp-", StringComparison.Ordinal));

    private void RemoveEmptyParents(Project project, string folder)
    {
        for (var parent = Path.GetDirectoryName(folder);
            parent is not null && !Paths.Equals(parent, project.Root) && SnapshotPaths.Contains(project.Root, parent)
                && !project.Known.Contains(parent) && !_visitedAncestors.Contains(parent) && !IsLink(parent)
                && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any();
            parent = Path.GetDirectoryName(parent))
        {
            Directory.Delete(parent);
        }
    }

    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Writes the cumulative report and one line for what this layer just found.</summary>
    private void Publish(Project project, int before)
    {
        var added = project.Folders.Skip(before).Select(folder => folder.Value.Outcome).ToArray();
        string? report = null;
        try
        {
            var directory = Path.Combine(project.Artifacts, "imprint", "prune");
            if (!SnapshotPaths.Contains(project.Root, directory))
            {
                SnapshotPaths.CheckPath(project.Directory, directory);
                Directory.CreateDirectory(directory);
                report = Path.Combine(directory, $"{_runId}-{PortableNames.Segment(project.Name)}.json");
                SnapshotPaths.CheckLink(report);
                using var stream = File.Create(report);
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                writer.WriteStartObject();
                writer.WriteNumber("version", 1);
                writer.WriteString("project", project.Name);
                writer.WriteString("root", project.Root);
                writer.WriteString("prune", project.Mode == SnapshotPrune.Report ? "report" : "auto");
                writer.WriteBoolean("verifyRun", project.ReadOnly);
                writer.WriteBoolean("conditionalCompilation", project.Conditional);
                writer.WriteStartArray("folders");
                foreach (var folder in project.Folders)
                {
                    writer.WriteStartObject();
                    writer.WriteString("path", Path.GetRelativePath(project.Root, folder.Key).Replace('\\', '/'));
                    writer.WriteString("outcome", folder.Value.Outcome);
                    writer.WriteString("reason", folder.Value.Reason);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SnapshotException)
        {
            report = null;
        }

        var removed = added.Count(outcome => outcome == "removed");
        var found = added.Count(outcome => outcome == "found");
        var kept = added.Count(outcome => outcome == "kept");
        var why = project.Mode == SnapshotPrune.Report ? "prune is set to report"
            : project.ReadOnly ? "this run only verifies"
            : "#if may compile different tests in another build";
        string? lead = null;
        var more = new List<string>();
        if (removed != 0)
        {
            lead = $"removed {Folders(removed)} under {project.Root}";
        }

        if (found != 0)
        {
            if (lead is null)
            {
                lead = $"found {Folders(found)} under {project.Root} and left {Them(found)}, because {why}";
            }
            else
            {
                more.Add($"It found {found} more and left {Them(found)}, because {why}.");
            }
        }

        if (kept != 0)
        {
            if (lead is null)
            {
                lead = $"kept {Folders(kept)} under {project.Root} that it could not safely remove";
            }
            else
            {
                more.Add($"It kept {kept} it could not safely remove.");
            }
        }

        more.Add(report is null ? string.Empty : $"See {report}");
        _log.WriteLine($"Imprint {lead}. {string.Join(" ", more)}".TrimEnd());

        static string Folders(int count) => count == 1 ? "1 stale snapshot folder" : $"{count} stale snapshot folders";
        static string Them(int count) => count == 1 ? "it" : "them";
    }

    private Project Build(SnapshotDescriptor descriptor, int version, Project? previous)
    {
        var relocation = Environment.GetEnvironmentVariable("IMPRINT_PROJECT_ROOT");
        var directory = Normalize(relocation ?? descriptor.ProjectDirectory);
        var relocated = !Paths.Equals(directory, Normalize(descriptor.ProjectDirectory));
        var config = ProjectConfigurationReader.Read(Path.Combine(directory, "snapshots.config.json"), required: false);
        var root = Normalize(Settings.BaselineRoot(directory, config.RootDirectory, config));
        var artifacts = Normalize(relocated ? Path.Combine(directory, "artifacts") : Settings.BuildArtifacts(directory, descriptor));
        var project = new Project
        {
            Name = descriptor.ProjectName,
            Directory = directory,
            Root = root,
            RootOwned = config.RootDirectory is null,
            Artifacts = artifacts,
            Storage = Path.Combine(artifacts, "imprint", "storage"),
            Mode = Settings.ResolvePrune(config),
            ReadOnly = Settings.ResolveRunOverride() == SnapshotUpdate.Verify,
            LockTimeout = TimeSpan.FromSeconds(config.LockTimeoutSeconds),
            Version = version,
            Relocation = relocation
        };
        if (previous is not null)
        {
            project.Blocked = previous.Blocked || previous.Relocation != relocation;
            project.Pruned.UnionWith(previous.Pruned);
            foreach (var folder in previous.Folders)
            {
                project.Folders.Add(folder.Key, folder.Value);
            }
        }

        if (project.Mode == SnapshotPrune.Off)
        {
            return project;
        }

        var key = Normalize(descriptor.ProjectDirectory);
        foreach (var candidate in _registry().Where(candidate => Paths.Equals(Normalize(candidate.ProjectDirectory), key)))
        {
            project.Conditional |= candidate.Conditional;
            if (candidate.Explicit || Predict(project, candidate, config) is not { } test)
            {
                project.Wildcards.Add(candidate);
                continue;
            }

            project.Tests.Add(test);
            project.ByDescriptor[candidate] = test;
            if (!project.BySuite.TryGetValue(test.Suite, out var suite))
            {
                project.BySuite.Add(test.Suite, suite = []);
            }

            suite.Add(test);
            foreach (var node in Nodes(project, test.Suite))
            {
                project.Known.Add(node);
                if (!candidate.Optional)
                {
                    project.Waiting[node] = project.Waiting.GetValueOrDefault(node) + 1;
                }
            }
        }

        return project;
    }

    private static Project Disabled(SnapshotDescriptor descriptor, int version) => new()
    {
        Name = descriptor.ProjectName,
        Directory = descriptor.ProjectDirectory,
        Root = descriptor.ProjectDirectory,
        RootOwned = false,
        Artifacts = descriptor.ProjectDirectory,
        Storage = descriptor.ProjectDirectory,
        Mode = SnapshotPrune.Off,
        ReadOnly = true,
        LockTimeout = TimeSpan.Zero,
        Version = version
    };

    /// <summary>Where a generated test writes, computed the same way its scope resolves it.</summary>
    private static Test? Predict(Project project, SnapshotDescriptor descriptor, ProjectConfiguration config)
    {
        try
        {
            var identity = TestIdentityResolver.Resolve(new SnapshotTestOptions { Case = descriptor.RequiresCase ? "case" : null }, descriptor);
            var family = config.UseFrameworkDisplayNames && descriptor.DisplayName is { Length: > 0 } displayName ? displayName : identity.Test;
            var suite = Normalize(Settings.SuiteDirectory(project.Root, identity.ProjectDirectory, identity.SourceFile, identity.Suite, explicitIdentity: false));
            if (!SnapshotPaths.Contains(project.Root, suite))
            {
                return null;
            }

            var prefix = PortableNames.Map(family) + " [";
            return new Test
            {
                Descriptor = descriptor,
                Suite = suite,
                Plain = PortableNames.Segment(family),
                Prefix = prefix,
                // Every case folder starts with the prefix unless the name is truncated or rewritten.
                Reliable = prefix.Length <= PortableNames.MaximumSegmentLength
                    && PortableNames.Segment(family + " [x]").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            };
        }
        catch (SnapshotException)
        {
            return null;
        }
    }

    /// <summary>The suite folder and each folder above it, up to and including the snapshot root.</summary>
    private static List<string> Nodes(Project project, string suite)
    {
        var nodes = new List<string>();
        for (var current = suite; current is not null; current = Path.GetDirectoryName(current))
        {
            nodes.Add(current);
            if (Paths.Equals(current, project.Root))
            {
                break;
            }
        }

        return nodes;
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private sealed class Entry
    {
        internal bool Entered;
        internal bool Outer;
        internal bool Variant;
        internal readonly HashSet<string> Directories = new(Paths);
    }

    private sealed class Test
    {
        internal required SnapshotDescriptor Descriptor
        {
            get; init;
        }
        internal required string Suite
        {
            get; init;
        }
        internal required string Plain
        {
            get; init;
        }
        internal required string Prefix
        {
            get; init;
        }
        internal required bool Reliable
        {
            get; init;
        }
        internal bool Satisfied;
        internal bool Complete;
    }

    private sealed record Finding(string Outcome, string Reason);

    private sealed class Project
    {
        internal required string Name
        {
            get; init;
        }
        internal required string Directory
        {
            get; init;
        }
        internal required string Root
        {
            get; init;
        }
        internal required bool RootOwned
        {
            get; init;
        }
        internal required string Artifacts
        {
            get; init;
        }
        internal required string Storage
        {
            get; init;
        }
        internal required SnapshotPrune Mode
        {
            get; init;
        }
        internal required bool ReadOnly
        {
            get; init;
        }
        internal required TimeSpan LockTimeout
        {
            get; init;
        }
        internal required int Version
        {
            get; init;
        }
        internal string? Relocation
        {
            get; init;
        }
        internal bool Conditional;
        internal bool Blocked;
        internal readonly List<Test> Tests = [];
        internal readonly Dictionary<SnapshotDescriptor, Test> ByDescriptor = new();
        internal readonly Dictionary<string, List<Test>> BySuite = new(Paths);
        internal readonly List<SnapshotDescriptor> Wildcards = [];
        internal readonly HashSet<string> Known = new(Paths);
        internal readonly Dictionary<string, int> Waiting = new(Paths);
        internal readonly HashSet<string> Ready = new(Paths);
        internal readonly HashSet<Test> ReadyFamilies = [];
        internal readonly HashSet<string> Pruned = new(Paths);
        internal readonly OrderedDictionary<string, Finding> Folders = new(Paths);
    }
}
