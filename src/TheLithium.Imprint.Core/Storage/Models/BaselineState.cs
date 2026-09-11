namespace TheLithium.Imprint.Storage.Models;

internal sealed record BaselineState(Dictionary<string, string> Files, string Fingerprint);
