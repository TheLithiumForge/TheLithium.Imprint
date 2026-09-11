using System.Text;

namespace TheLithium.Imprint.Comparison;

/// <summary>A bounded line diff for assertion output; complete values remain available in artifacts.</summary>
internal static class SnapshotDiff
{
    private const int Context = 3;
    private const int MaxOutputLines = 80;
    private const int MaxLineLength = 240;
    private const long MaxCells = 1_000_000;
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

        var a = left.Length - prefix - suffix;
        var b = right.Length - prefix - suffix;
        var lines = new List<Line>();
        var start = Math.Max(0, prefix - Context);
        for (var i = start; i < prefix; i++)
        {
            lines.Add(new(' ', left[i]));
        }

        var abbreviated = (long)(a + 1) * (b + 1) > MaxCells || a + b > 10_000;
        if (abbreviated)
        {
            // Never allocate a quadratic matrix for a large changed region.
            for (var i = 0; i < Math.Min(a, 15); i++)
            {
                lines.Add(new('-', left[prefix + i]));
            }

            for (var i = 0; i < Math.Min(b, 15); i++)
            {
                lines.Add(new('+', right[prefix + i]));
            }
        }
        else
        {
            var lengths = new int[a + 1, b + 1];
            for (var i = a - 1; i >= 0; i--)
            {
                for (var j = b - 1; j >= 0; j--)
                {
                    lengths[i, j] = left[prefix + i] == right[prefix + j]
                        ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
                }
            }

            var x = 0;
            var y = 0;
            while (x < a || y < b)
            {
                if (x < a && y < b && left[prefix + x] == right[prefix + y])
                {
                    lines.Add(new(' ', left[prefix + x++]));
                    y++;
                }
                else if (x < a && (y == b || lengths[x + 1, y] >= lengths[x, y + 1]))
                {
                    lines.Add(new('-', left[prefix + x++]));
                }
                else
                {
                    lines.Add(new('+', right[prefix + y++]));
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
        var at = 0;
        while (at < lines.Count)
        {
            var first = at;
            var lastChange = at;
            var end = at;
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
            output.Append("@@ -").Append(oldLine).Append(',').Append(oldCount)
                .Append(" +").Append(newLine).Append(',').Append(newCount).Append(" @@\n");
            for (; at < end; at++)
            {
                if (emitted++ == MaxOutputLines)
                {
                    return output.Append("... diff truncated; inspect the full expected and received artifacts.").ToString();
                }

                var line = lines[at];
                var text = line.Text.Length > MaxLineLength ? line.Text[..MaxLineLength] + "..." : line.Text;
                output.Append(line.Kind).Append(' ').Append(text.Replace("\r", "\\r").Replace("\t", "\\t")).Append('\n');
                if (line.Kind != '+')
                {
                    oldLine++;
                }

                if (line.Kind != '-')
                {
                    newLine++;
                }
            }
            var nextChange = at;
            while (nextChange < lines.Count && lines[nextChange].Kind == ' ')
            {
                nextChange++;
            }

            var nextStart = Math.Max(at, nextChange - Context);
            oldLine += nextStart - at;
            newLine += nextStart - at;
            at = nextStart;
        }
        if (abbreviated)
        {
            output.Append("... large diff abbreviated; inspect the full expected and received artifacts.\n");
        }

        return output.ToString().TrimEnd('\n');
    }
}
