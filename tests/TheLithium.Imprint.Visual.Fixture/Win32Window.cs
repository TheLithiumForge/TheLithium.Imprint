using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TheLithium.Imprint.Visual.Fixture;

[SupportedOSPlatform("windows")]
internal static unsafe partial class Win32Window
{
    private const uint OverlappedWindow = 0x00CF0000;
    private const uint Visible = 0x10000000;
    private const uint Paint = 0x000F;
    private const uint Destroy = 0x0002;
    private static nint _rightBrush;
    private static nint _leftBrush;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        internal uint Size;
        internal uint Style;
        internal delegate* unmanaged<nint, uint, nint, nint, nint> Procedure;
        internal int ClassExtra;
        internal int WindowExtra;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint Background;
        internal char* MenuName;
        internal char* ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintInfo
    {
        internal nint Context;
        internal int Erase;
        internal Rect Area;
        internal int Restore;
        internal int IncUpdate;
        internal fixed byte Reserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nint WParam;
        internal nint LParam;
        internal uint Time;
        internal int X;
        internal int Y;
        internal uint Private;
    }

    internal static int Run(string title, (byte R, byte G, byte B) right)
    {
        SetProcessDpiAwarenessContext(-4);
        _leftBrush = CreateSolidBrush(Rgb(Program.Left));
        _rightBrush = CreateSolidBrush(Rgb(right));
        fixed (char* className = "ImprintFixture")
        fixed (char* caption = title)
        {
            var windowClass = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                Procedure = &Procedure,
                Instance = GetModuleHandleW(null),
                Cursor = LoadCursorW(0, 32512),
                ClassName = className
            };
            if (RegisterClassExW(&windowClass) == 0)
            {
                return 1;
            }

            var area = new Rect { Right = Program.Width, Bottom = Program.Height };
            AdjustWindowRect(&area, OverlappedWindow, 0);
            var window = CreateWindowExW(0, className, caption, OverlappedWindow | Visible, 100, 100,
                area.Right - area.Left, area.Bottom - area.Top, 0, 0, windowClass.Instance, 0);
            if (window == 0)
            {
                return 1;
            }

            Message message;
            while (GetMessageW(&message, 0, 0, 0) > 0)
            {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        }
        return 0;
    }

    private static uint Rgb((byte R, byte G, byte B) color) => color.R | (uint)color.G << 8 | (uint)color.B << 16;

    [UnmanagedCallersOnly]
    private static nint Procedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == Paint)
        {
            PaintInfo paint;
            var context = BeginPaint(window, &paint);
            Rect client;
            GetClientRect(window, &client);
            var middle = client.Right / 2;
            var left = new Rect { Right = middle, Bottom = client.Bottom };
            var right = new Rect { Left = middle, Right = client.Right, Bottom = client.Bottom };
            FillRect(context, &left, _leftBrush);
            FillRect(context, &right, _rightBrush);
            EndPaint(window, &paint);
            return 0;
        }
        if (message == Destroy)
        {
            PostQuitMessage(0);
            return 0;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    [LibraryImport("user32.dll")]
    private static partial nint SetProcessDpiAwarenessContext(nint context);

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WindowClass* windowClass);

    [LibraryImport("user32.dll")]
    private static partial nint CreateWindowExW(uint extendedStyle, char* className, char* title, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    private static partial int AdjustWindowRect(Rect* area, uint style, int menu);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(Message* message, nint window, uint first, uint last);

    [LibraryImport("user32.dll")]
    private static partial int TranslateMessage(Message* message);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(Message* message);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int code);

    [LibraryImport("user32.dll")]
    private static partial nint BeginPaint(nint window, PaintInfo* paint);

    [LibraryImport("user32.dll")]
    private static partial int EndPaint(nint window, PaintInfo* paint);

    [LibraryImport("user32.dll")]
    private static partial int GetClientRect(nint window, Rect* area);

    [LibraryImport("user32.dll")]
    private static partial int FillRect(nint context, Rect* area, nint brush);

    [LibraryImport("user32.dll")]
    private static partial nint LoadCursorW(nint instance, nint name);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateSolidBrush(uint color);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
