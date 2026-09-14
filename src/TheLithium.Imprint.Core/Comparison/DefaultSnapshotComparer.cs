using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace TheLithium.Imprint.Comparison;

/// <summary>Structural JSON and text equality. No filtering, mutation or fuzzy approval.</summary>
public sealed class DefaultSnapshotComparer : ISnapshotComparer
{
    private const int MaximumExponentMagnitude = 1_000_000;
    private const int MaximumJsonDisplayBytes = 1024 * 1024;
    private const int MaximumFallbackCharacters = 16_000;
    private const int MaximumComparisonWork = 2_000_000;
    private const int MaximumToleranceDigits = 4096;
    private const int MaximumToleranceScale = 20_000;
    private const int MaximumExcerptCharacters = 160;
    /// <summary>Shared stateless comparer used when no custom comparer is supplied.</summary>
    public static DefaultSnapshotComparer Instance { get; } = new();

    /// <summary>Compares canonical JSON or text according to the supplied equality rules.</summary>
    /// <param name="expected">The stored baseline representation.</param>
    /// <param name="received">The captured representation.</param>
    /// <param name="format">The resolved representation format.</param>
    /// <param name="options">Comparison rules for this entry.</param>
    /// <returns>A match or a bounded human-readable difference.</returns>
    public SnapshotComparisonResult Compare(string expected, string received,
        SnapshotFormat format, ResolvedSnapshotComparison options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Settings.ValidateComparison(options.ToOptions());
        return Compare(expected, received, format, options, CancellationToken.None);
    }

    internal SnapshotComparisonResult Compare(string expected, string received,
        SnapshotFormat format, ResolvedSnapshotComparison options, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(received);
        ArgumentNullException.ThrowIfNull(options);

        if (format == SnapshotFormat.Text)
        {
            var left = ComparableText(expected, options);
            var right = ComparableText(received, options);
            cancellation.ThrowIfCancellationRequested();
            if (string.Equals(left, right, options.IgnoreStringCase
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return SnapshotComparisonResult.Match;
            }

            var expectedLines = left.Split('\n');
            var receivedLines = right.Split('\n');
            var count = Math.Min(expectedLines.Length, receivedLines.Length);
            for (var i = 0; i < count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!string.Equals(expectedLines[i], receivedLines[i], options.IgnoreStringCase
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    return new(false, $"Line {i + 1}: expected {Excerpt(expectedLines[i])}, received {Excerpt(receivedLines[i])}.\n{SnapshotDiff.Create(left, right)}");
                }
            }

            return new(false, $"Line count differs: expected {expectedLines.Length}, received {receivedLines.Length}.\n{SnapshotDiff.Create(left, right)}");
        }
        if (format != SnapshotFormat.Json)
        {
            throw new SnapshotConfigurationException("The comparer requires a resolved format.");
        }

        using var expectedDocument = StrictJson.Parse(expected, SnapshotLimits.MaximumDepth, cancellation);
        using var receivedDocument = StrictJson.Parse(received, SnapshotLimits.MaximumDepth, cancellation);
        var budget = new Budget(cancellation);
        var difference = CompareJson(expectedDocument.RootElement, receivedDocument.RootElement, "$", options, budget);
        if (difference is null)
        {
            return SnapshotComparisonResult.Match;
        }
        var expectedDisplay = JsonDisplay(expectedDocument.RootElement, expected, cancellation);
        var receivedDisplay = JsonDisplay(receivedDocument.RootElement, received, cancellation);
        return new(false, $"{difference}\n{SnapshotDiff.Create(expectedDisplay, receivedDisplay)}");
    }

    private static string JsonDisplay(JsonElement document, string original, CancellationToken cancellation)
    {
        try
        {
            return SnapshotEncoding.FormatDocument(document, MaximumJsonDisplayBytes, cancellation);
        }
        catch (SnapshotCaptureException)
        {
            var length = Math.Min(original.Length, MaximumFallbackCharacters);
            if (length > 0 && char.IsHighSurrogate(original[length - 1]))
            {
                length--;
            }
            return $"{original[..length]}\n[JSON display truncated; inspect the expected and received artifact files.]";
        }
    }

    private sealed class Budget(CancellationToken cancellation)
    {
        private int _remaining = MaximumComparisonWork;
        internal void Consume()
        {
            cancellation.ThrowIfCancellationRequested();
            if (--_remaining < 0)
            {
                throw new SnapshotException("Comparison work limit exceeded. Compare smaller values or use an explicit comparer.");
            }
        }
    }

    private static string? CompareJson(JsonElement left, JsonElement right, string path,
        ResolvedSnapshotComparison options, Budget budget)
    {
        budget.Consume();
        if (left.ValueKind != right.ValueKind)
        {
            return $"{path}: value kinds differ.";
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                var expectedProperties = left.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
                var receivedProperties = right.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
                foreach (var name in expectedProperties.Keys.Order(StringComparer.Ordinal))
                {
                    var escapedName = name.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    var child = $"{path}[\"{escapedName}\"]";
                    if (!receivedProperties.TryGetValue(name, out var other))
                    {
                        return $"{child}: property is missing.";
                    }

                    var difference = CompareJson(expectedProperties[name], other, child, options, budget);
                    if (difference is not null)
                    {
                        return difference;
                    }
                }
                foreach (var name in receivedProperties.Keys.Order(StringComparer.Ordinal))
                {
                    if (!expectedProperties.ContainsKey(name))
                    {
                        return $"{path}: unexpected property {Excerpt(name)}.";
                    }
                }

                return null;
            case JsonValueKind.Array:
                if (left.GetArrayLength() != right.GetArrayLength())
                {
                    return $"{path}: array length differs ({left.GetArrayLength()} vs {right.GetArrayLength()}).";
                }

                var length = left.GetArrayLength();
                if (options.IgnoreArrayOrder)
                {
                    if (length > options.MaxUnorderedArrayLength)
                    {
                        throw new SnapshotException($"Unordered array exceeds its comparison limit at {path}.");
                    }
                    // Maximum bipartite matching, not greedy matching. Tolerance is not transitive.
                    var edges = new bool[length, length];
                    for (var i = 0; i < length; i++)
                    {
                        for (var j = 0; j < length; j++)
                        {
                            edges[i, j] = CompareJson(left[i], right[j], path, options, budget) is null;
                        }
                    }

                    var assigned = Enumerable.Repeat(-1, length).ToArray();
                    for (var i = 0; i < length; i++)
                    {
                        if (!Augment(i, edges, assigned, new bool[length], budget))
                        {
                            return $"{path}: arrays differ when treated as multisets.";
                        }
                    }

                    return null;
                }
                for (var i = 0; i < length; i++)
                {
                    var difference = CompareJson(left[i], right[i], $"{path}[{i}]", options, budget);
                    if (difference is not null)
                    {
                        return difference;
                    }
                }
                return null;
            case JsonValueKind.String:
                var leftText = left.GetString() ?? string.Empty;
                var rightText = right.GetString() ?? string.Empty;
                return string.Equals(leftText, rightText, options.IgnoreStringCase
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    ? null : $"{path}: expected {Excerpt(leftText)}, received {Excerpt(rightText)}.";
            case JsonValueKind.Number:
                return NumbersEqual(left, right, options.NumericTolerance)
                    ? null : $"{path}: expected {Excerpt(left.GetRawText())}, received {Excerpt(right.GetRawText())}.";
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                return null;
            default:
                throw new SnapshotException("Undefined JSON cannot be compared.");
        }
    }

    private static bool Augment(int row, bool[,] edges, int[] assigned, bool[] visited, Budget budget)
    {
        budget.Consume();
        for (var j = 0; j < assigned.Length; j++)
        {
            if (!edges[row, j] || visited[j])
            {
                continue;
            }

            visited[j] = true;
            if (assigned[j] < 0 || Augment(assigned[j], edges, assigned, visited, budget))
            {
                assigned[j] = row;
                return true;
            }
        }
        return false;
    }

    private sealed record Number(string Digits, BigInteger Exponent, bool Negative);

    private static Number ParseNumber(string value)
    {
        var negative = value.StartsWith('-');
        if (negative)
        {
            value = value[1..];
        }

        var exponentPosition = value.IndexOf('e');
        if (exponentPosition < 0)
        {
            exponentPosition = value.IndexOf('E');
        }

        var exponent = exponentPosition < 0 ? BigInteger.Zero : BigInteger.Parse(value[(exponentPosition + 1)..], CultureInfo.InvariantCulture);
        if (exponentPosition >= 0)
        {
            value = value[..exponentPosition];
        }

        var dot = value.IndexOf('.');
        if (dot >= 0)
        {
            exponent -= value.Length - dot - 1;
            value = value.Remove(dot, 1);
        }
        value = value.TrimStart('0');
        if (value.Length == 0)
        {
            return new("0", BigInteger.Zero, false);
        }

        var length = value.Length;
        while (length > 1 && value[length - 1] == '0')
        {
            length--;
        }

        exponent += value.Length - length;
        return new(value[..length], exponent, negative);
    }

    private static bool NumbersEqual(JsonElement left, JsonElement right, decimal tolerance)
    {
        var leftText = left.GetRawText();
        var rightText = right.GetRawText();
        ValidateExponent(leftText);
        ValidateExponent(rightText);
        if (JsonElement.DeepEquals(left, right))
        {
            return true;
        }

        if (tolerance == 0)
        {
            return false;
        }

        var expectedNumber = ParseNumber(leftText);
        var receivedNumber = ParseNumber(rightText);
        var toleranceNumber = ParseNumber(tolerance.ToString("G29", CultureInfo.InvariantCulture));
        var minimum = BigInteger.Min(expectedNumber.Exponent, BigInteger.Min(receivedNumber.Exponent, toleranceNumber.Exponent));
        foreach (var number in new[] { expectedNumber, receivedNumber, toleranceNumber })
        {
            if (number.Digits.Length > MaximumToleranceDigits || number.Exponent - minimum > MaximumToleranceScale)
            {
                throw new SnapshotException("Exact numeric tolerance exceeds its arithmetic budget. Supply expectedNumber custom comparer.");
            }
        }

        BigInteger Scale(Number number)
        {
            var mantissa = BigInteger.Parse(number.Digits, CultureInfo.InvariantCulture);
            if (number.Negative)
            {
                mantissa = -mantissa;
            }

            return mantissa * BigInteger.Pow(10, (int)(number.Exponent - minimum));
        }
        return BigInteger.Abs(Scale(expectedNumber) - Scale(receivedNumber)) <= Scale(toleranceNumber);
    }

    private static void ValidateExponent(string number)
    {
        var position = number.AsSpan().IndexOfAny('e', 'E');
        if (position >= 0 && (!int.TryParse(number.AsSpan(position + 1), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var exponent)
            || exponent is < -MaximumExponentMagnitude or > MaximumExponentMagnitude))
        {
            throw new SnapshotException($"JSON number exponent exceeds the supported range -{MaximumExponentMagnitude}..{MaximumExponentMagnitude}. Supply a custom comparer.");
        }
    }

    private static string ComparableText(string value, ResolvedSnapshotComparison options)
    {
        if (options.IgnoreLineEndings)
        {
            value = value.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        if (options.IgnoreTrailingWhitespace)
        {
            value = string.Join("\n", value.Split('\n').Select(static line =>
                line.EndsWith('\r') ? $"{line[..^1].TrimEnd(' ', '\t')}\r" : line.TrimEnd(' ', '\t')));
        }

        return value;
    }

    private static string Excerpt(string value)
    {
        var escaped = value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\t", "\\t").Replace("\n", "\\n");
        if (escaped.Length > MaximumExcerptCharacters)
        {
            escaped = $"{escaped[..MaximumExcerptCharacters]}...";
        }

        return $"\"{escaped.Replace("\"", "\\\"")}\"";
    }
}
