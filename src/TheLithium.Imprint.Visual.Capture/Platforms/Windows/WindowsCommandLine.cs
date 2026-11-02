using System.Text;

namespace TheLithium.Imprint.Visual.Platforms.Windows;

/// <summary>Builds a Windows command line that the C runtime splits back into exactly these arguments.</summary>
internal static class WindowsCommandLine
{
    /// <summary>Quotes each argument the way the C runtime splits a command line back into arguments.</summary>
    internal static string For(string executable, IEnumerable<string> arguments)
    {
        var line = new StringBuilder();
        Quote(line, executable);
        foreach (var argument in arguments)
        {
            line.Append(' ');
            Quote(line, argument);
        }
        return line.ToString();
    }

    private static void Quote(StringBuilder line, string argument)
    {
        if (argument.Length != 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            line.Append(argument);
            return;
        }

        line.Append('"');
        for (var i = 0; i < argument.Length; i++)
        {
            var backslashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                backslashes++;
                i++;
            }

            if (i == argument.Length)
            {
                line.Append('\\', backslashes * 2);
                break;
            }
            if (argument[i] == '"')
            {
                line.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                line.Append('\\', backslashes).Append(argument[i]);
            }
        }
        line.Append('"');
    }
}
