using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace TheLithium.Imprint.Comparison;

/// <summary>Structural JSON and text equality. No filtering, mutation or fuzzy approval.</summary>
public sealed class DefaultSnapshotComparer : ISnapshotComparer
{
    /// <summary>Shared stateless comparer used when no custom comparer is supplied.</summary>
    public static DefaultSnapshotComparer Instance { get; } = new();

    /// <summary>Compares canonical JSON or text according to the supplied equality rules.</summary>
    /// <param name="expected">The stored baseline representation.</param>
    /// <param name="received">The captured representation.</param>
    /// <param name="format">The resolved representation format.</param>
    /// <param name="options">Comparison rules for this entry.</param>
    /// <returns>A match or a bounded human-readable difference.</returns>
    public SnapshotComparisonResult Compare(string expected, string received,
        SnapshotFormat format, SnapshotComparison options)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(received);
        ArgumentNullException.ThrowIfNull(options);
        Settings.ValidateComparison(options);
        if (format is SnapshotFormat.Snap or SnapshotFormat.Text)
        {
            var left = ComparableText(expected, options);
            var right = ComparableText(received, options);
            if (string.Equals(left, right, options.IgnoreStringCase
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return SnapshotComparisonResult.Match;
            }

            var a = left.Split('\n');
            var b = right.Split('\n');
            var count = Math.Min(a.Length, b.Length);
            for (var i = 0; i < count; i++)
            {
                if (!string.Equals(a[i], b[i], options.IgnoreStringCase
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    return new(false, $"Line {i + 1}: expected {Excerpt(a[i])}, received {Excerpt(b[i])}.\n" + SnapshotDiff.Create(left, right));
                }
            }

            return new(false, $"Line count differs: expected {a.Length}, received {b.Length}.\n" + SnapshotDiff.Create(left, right));
        }
        if (format != SnapshotFormat.Json)
        {
            throw new SnapshotConfigurationException("The comparer requires a resolved format.");
        }

        using var expectedDocument = JsonDocument.Parse(expected, new JsonDocumentOptions { MaxDepth = 256 });
        using var receivedDocument = JsonDocument.Parse(received, new JsonDocumentOptions { MaxDepth = 256 });
        ValidateJson(expectedDocument.RootElement);
        ValidateJson(receivedDocument.RootElement);
        var budget = new Budget();
        var difference = CompareJson(expectedDocument.RootElement, receivedDocument.RootElement, "$", options, budget);
        return difference is null ? SnapshotComparisonResult.Match : new(false, difference + "\n" + SnapshotDiff.Create(expected, received));
    }

    private sealed class Budget
    {
        private int _remaining = 2_000_000;
        internal void Consume()
        {
            if (--_remaining < 0)
            {
                throw new SnapshotException("Comparison work limit exceeded. Compare smaller values or use an explicit comparer.");
            }
        }
    }

    private static string? CompareJson(JsonElement left, JsonElement right, string path,
        SnapshotComparison options, Budget budget)
    {
        budget.Consume();
        if (left.ValueKind != right.ValueKind)
        {
            return path + ": value kinds differ.";
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                var a = left.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
                var b = right.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
                foreach (var name in a.Keys.Order(StringComparer.Ordinal))
                {
                    var child = path + "[\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"]";
                    if (!b.TryGetValue(name, out var other))
                    {
                        return child + ": property is missing.";
                    }

                    var difference = CompareJson(a[name], other, child, options, budget);
                    if (difference is not null)
                    {
                        return difference;
                    }
                }
                foreach (var name in b.Keys.Order(StringComparer.Ordinal))
                {
                    if (!a.ContainsKey(name))
                    {
                        return path + ": unexpected property " + Excerpt(name) + ".";
                    }
                }

                return null;
            case JsonValueKind.Array:
                if (left.GetArrayLength() != right.GetArrayLength())
                {
                    return path + $": array length differs ({left.GetArrayLength()} vs {right.GetArrayLength()}).";
                }

                var length = left.GetArrayLength();
                if (options.IgnoreArrayOrder)
                {
                    if (length > options.MaxUnorderedArrayLength)
                    {
                        throw new SnapshotException("Unordered array exceeds its comparison limit at " + path + ".");
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
                            return path + ": arrays differ when treated as multisets.";
                        }
                    }

                    return null;
                }
                for (var i = 0; i < length; i++)
                {
                    var difference = CompareJson(left[i], right[i], path + "[" + i + "]", options, budget);
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
                    ? null : path + ": expected " + Excerpt(leftText) + ", received " + Excerpt(rightText) + ".";
            case JsonValueKind.Number:
                return NumbersEqual(left.GetRawText(), right.GetRawText(), options.NumericTolerance)
                    ? null : path + ": expected " + Excerpt(left.GetRawText()) + ", received " + Excerpt(right.GetRawText()) + ".";
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

        var e = value.IndexOf('e');
        if (e < 0)
        {
            e = value.IndexOf('E');
        }

        var exponent = e < 0 ? BigInteger.Zero : BigInteger.Parse(value[(e + 1)..], CultureInfo.InvariantCulture);
        if (e >= 0)
        {
            value = value[..e];
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

    private static bool NumbersEqual(string left, string right, decimal tolerance)
    {
        var a = ParseNumber(left);
        var b = ParseNumber(right);
        if (a == b)
        {
            return true;
        }

        if (tolerance == 0)
        {
            return false;
        }

        var t = ParseNumber(tolerance.ToString("G29", CultureInfo.InvariantCulture));
        var minimum = BigInteger.Min(a.Exponent, BigInteger.Min(b.Exponent, t.Exponent));
        foreach (var number in new[] { a, b, t })
        {
            if (number.Digits.Length > 4096 || number.Exponent - minimum > 20000)
            {
                throw new SnapshotException("Exact numeric tolerance exceeds its arithmetic budget. Supply a custom comparer.");
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
        return BigInteger.Abs(Scale(a) - Scale(b)) <= Scale(t);
    }

    private static void ValidateJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new SnapshotException("JSON baseline contains a duplicate property: " + property.Name);
                }

                ValidateJson(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                ValidateJson(child);
            }
        }
    }

    private static string ComparableText(string value, SnapshotComparison options)
    {
        if (options.IgnoreLineEndings)
        {
            value = value.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        if (options.IgnoreTrailingWhitespace)
        {
            value = string.Join("\n", value.Split('\n').Select(static line =>
                line.EndsWith('\r') ? line[..^1].TrimEnd(' ', '\t') + "\r" : line.TrimEnd(' ', '\t')));
        }

        return value;
    }

    private static string Excerpt(string value)
    {
        var escaped = value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\t", "\\t").Replace("\n", "\\n");
        if (escaped.Length > 160)
        {
            escaped = escaped[..160] + "...";
        }

        return "\"" + escaped.Replace("\"", "\\\"") + "\"";
    }
}
