namespace TheLithium.Imprint.Storage.Models;

internal sealed record BaselineState(Dictionary<string, SnapshotFile> Files, string Fingerprint);
