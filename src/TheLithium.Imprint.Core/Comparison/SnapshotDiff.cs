using System.Globalization;
using System.Text;

namespace TheLithium.Imprint.Comparison;

/// <summary>A bounded line diff for assertion output; complete values remain available in artifacts.</summary>
internal static class SnapshotDiff
{
    private const int Context = 3;
    private const int MaxOutputLines = 80;
    private const int MaxLineLength = 240;
    private const long MaxCells = 1_000_000;
    private const int MaxChangedLines = 10_000;
    private const int AbbreviatedLinesPerSide = 15;
    private readonly record struct Line(char Kind, string Text);

    internal static string Create(string expected, string received)
    {
        var left = expected.Split('\n');
        var right = received.Split('\n');
        var prefix = 0;
        while (prefix < Math.Min(left.Length, right.Length) && left[prefix] == right[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < Math.Min(left.Length, right.Length) - prefix
            && left[^(suffix + 1)] == right[^(suffix + 1)])
        {
            suffix++;
        }

        var expectedChangeCount = left.Length - prefix - suffix;
        var receivedChangeCount = right.Length - prefix - suffix;
        var lines = new List<Line>();
        var start = Math.Max(0, prefix - Context);
        for (var i = start; i < prefix; i++)
        {
            lines.Add(new(' ', left[i]));
        }

        var abbreviated = (long)(expectedChangeCount + 1) * (receivedChangeCount + 1) > MaxCells || expectedChangeCount + receivedChangeCount > MaxChangedLines;
        if (abbreviated)
        {
            // Never allocate a quadratic matrix for a large changed region.
            for (var i = 0; i < Math.Min(expectedChangeCount, AbbreviatedLinesPerSide); i++)
            {
                lines.Add(new('-', left[prefix + i]));
            }

            for (var i = 0; i < Math.Min(receivedChangeCount, AbbreviatedLinesPerSide); i++)
            {
                lines.Add(new('+', right[prefix + i]));
            }
        }
        else
        {
            var lengths = new int[expectedChangeCount + 1, receivedChangeCount + 1];
            for (var i = expectedChangeCount - 1; i >= 0; i--)
            {
                for (var j = receivedChangeCount - 1; j >= 0; j--)
                {
                    lengths[i, j] = left[prefix + i] == right[prefix + j]
                        ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
                }
            }

            var expectedIndex = 0;
            var receivedIndex = 0;
            while (expectedIndex < expectedChangeCount || receivedIndex < receivedChangeCount)
            {
                if (expectedIndex < expectedChangeCount && receivedIndex < receivedChangeCount && left[prefix + expectedIndex] == right[prefix + receivedIndex])
                {
                    lines.Add(new(' ', left[prefix + expectedIndex++]));
                    receivedIndex++;
                }
                else if (expectedIndex < expectedChangeCount
                    && (receivedIndex == receivedChangeCount || lengths[expectedIndex + 1, receivedIndex] >= lengths[expectedIndex, receivedIndex + 1]))
                {
                    lines.Add(new('-', left[prefix + expectedIndex++]));
                }
                else
                {
                    lines.Add(new('+', right[prefix + receivedIndex++]));
                }
            }
        }
        for (var i = 0; i < Math.Min(suffix, Context); i++)
        {
            lines.Add(new(' ', left[left.Length - suffix + i]));
        }

        var output = new StringBuilder("--- expected\n+++ received\n");
        var oldLine = start + 1;
        var newLine = start + 1;
        var emitted = 0;
        var lineIndex = 0;
        while (lineIndex < lines.Count)
        {
            var first = lineIndex;
            var lastChange = lineIndex;
            var end = lineIndex;
            for (; end < lines.Count; end++)
            {
                if (lines[end].Kind != ' ')
                {
                    lastChange = end;
                }

                if (end - lastChange > Context * 2)
                {
                    break;
                }
            }
            if (end < lines.Count)
            {
                end = lastChange + Context + 1;
            }

            var oldCount = 0;
            var newCount = 0;
            for (var i = first; i < end; i++)
            {
                if (lines[i].Kind != '+')
                {
                    oldCount++;
                }

                if (lines[i].Kind != '-')
                {
                    newCount++;
                }
            }
            output.Append(CultureInfo.InvariantCulture, $"@@ -{oldLine},{oldCount} +{newLine},{newCount} @@\n");
            for (; lineIndex < end; lineIndex++)
            {
                if (emitted++ == MaxOutputLines)
                {
                    return output.Append("... diff truncated; inspect the full expected and received artifacts.").ToString();
                }

                var line = lines[lineIndex];
                var text = line.Text.Length > MaxLineLength ? $"{line.Text[..MaxLineLength]}..." : line.Text;
                var escapedText = text.Replace("\r", "\\r").Replace("\t", "\\t");
                output.Append($"{line.Kind} {escapedText}\n");
                if (line.Kind != '+')
                {
                    oldLine++;
                }

                if (line.Kind != '-')
                {
                    newLine++;
                }
            }
            var nextChange = lineIndex;
            while (nextChange < lines.Count && lines[nextChange].Kind == ' ')
            {
                nextChange++;
            }

            var nextStart = Math.Max(lineIndex, nextChange - Context);
            oldLine += nextStart - lineIndex;
            newLine += nextStart - lineIndex;
            lineIndex = nextStart;
        }
        if (abbreviated)
        {
            output.Append("... large diff abbreviated; inspect the full expected and received artifacts.\n");
        }

        return output.ToString().TrimEnd('\n');
    }
}
