namespace TheLithium.Imprint.Comparison.Models;

/// <summary>
/// A failure artifact written next to received and expected as <c>&lt;name&gt;.&lt;Suffix&gt;</c>, e.g. <c>settings.diff.png</c>.
/// </summary>
internal sealed record SnapshotAttachment(string Suffix, byte[] Content);
