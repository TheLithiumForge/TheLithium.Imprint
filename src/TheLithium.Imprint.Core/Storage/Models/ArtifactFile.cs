namespace TheLithium.Imprint.Storage.Models;

/// <summary>One extra failure artifact, written into the test's failure folder under <see cref="FileName"/>.</summary>
internal sealed record ArtifactFile(string FileName, byte[] Content);
