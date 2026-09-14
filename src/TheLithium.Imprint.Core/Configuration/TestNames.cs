namespace TheLithium.Imprint.Configuration;

/// <summary>Owns the method, case, and variant naming shared by storage, selectors, and diagnostics.</summary>
internal static class TestNames
{
    internal static string FolderName(SnapshotTestIdentity identity)
    {
        var caseSuffix = identity.Case is null ? string.Empty : $" [{identity.Case}]";
        var variantSuffix = identity.Variant is null ? string.Empty : $" [{identity.Variant}]";
        return $"{identity.Test}{caseSuffix}{variantSuffix}";
    }

    internal static string DisplayName(SnapshotTestIdentity identity) => $"{identity.Suite}.{FolderName(identity)}";
}
