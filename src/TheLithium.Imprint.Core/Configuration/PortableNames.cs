using System.Security.Cryptography;
using System.Text;

namespace TheLithium.Imprint.Configuration;

internal static class PortableNames
{
    private const int MaximumSegmentLength = 96;
    private const int HashSuffixLength = 16;
    private static readonly HashSet<string> Devices = new(StringComparer.OrdinalIgnoreCase)
    { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
      "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "CONIN$", "CONOUT$" };

    internal static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal static string Segment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SnapshotConfigurationException("Snapshot names cannot be empty or whitespace.");
        }

        var original = value;
        value = value.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(ch < 32 || ch == 127 || "<>:\"/\\|?*".Contains(ch) ? '_' : ch);
        }

        var safe = builder.ToString().TrimEnd(' ', '.');
        if (safe is "" or "." or "..")
        {
            safe = "snapshot";
        }

        var dot = safe.IndexOf('.');
        var deviceName = dot < 0 ? safe : safe[..dot];
        if (Devices.Contains(deviceName))
        {
            safe = $"_{safe}";
        }

        if (safe.Length > MaximumSegmentLength)
        {
            var length = char.IsHighSurrogate(safe[MaximumSegmentLength - 1]) ? MaximumSegmentLength - 1 : MaximumSegmentLength;
            safe = safe[..length];
        }
        if (!string.Equals(safe, original, StringComparison.Ordinal))
        {
            safe = $"{safe}~{Hash(original)[..HashSuffixLength]}";
        }

        return safe;
    }

    internal static string? Infer(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var text = expression.Trim().TrimEnd('!');
        var parts = text.Split('.');
        foreach (var raw in parts)
        {
            var part = raw.Trim();
            if (part.StartsWith('@'))
            {
                part = part[1..];
            }

            if (part.Length == 0 || !(char.IsLetter(part[0]) || part[0] == '_'))
            {
                return null;
            }

            if (part.Skip(1).Any(c => !char.IsLetterOrDigit(c) && c != '_'))
            {
                return null;
            }
        }
        var name = parts[^1].Trim().TrimStart('@');
        return name is "null" or "true" or "false" or "this" or "base" or "default" ? null : name;
    }
}
