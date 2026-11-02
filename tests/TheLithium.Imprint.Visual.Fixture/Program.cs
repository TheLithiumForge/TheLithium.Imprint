using System.Globalization;

namespace TheLithium.Imprint.Visual.Fixture;

/// <summary>
/// Opens one 400x300 window: the left half red, the right half a color from <c>--right r,g,b</c>, blue by default.
/// The capture driver tests start it, capture it and look for both halves, so a wrong window or lost arguments fail.
/// </summary>
internal static class Program
{
    internal const int Width = 400;
    internal const int Height = 300;
    internal static readonly (byte R, byte G, byte B) Left = (220, 30, 30);

    private static int Main(string[] args)
    {
        var title = Option(args, "--title") ?? "Imprint fixture";
        var right = Color(Option(args, "--right") ?? "30,60,220");
        if (OperatingSystem.IsWindows())
        {
            return Win32Window.Run(title, right);
        }
        if (OperatingSystem.IsLinux())
        {
            return GtkWindow.Run(title, right);
        }
        if (OperatingSystem.IsMacOS())
        {
            return AppKitWindow.Run(title, right);
        }
        Console.Error.WriteLine("The fixture opens windows on Windows, Linux and macOS.");
        return 2;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static (byte R, byte G, byte B) Color(string text)
    {
        var parts = text.Split(',').Select(part => byte.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray();
        return (parts[0], parts[1], parts[2]);
    }
}
