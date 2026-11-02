using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TheLithium.Imprint.Visual.Platforms.Windows;

/// <summary>The Win32 calls the Windows driver makes. Marshalling is generated at compile time, so it works under Native AOT.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsNative
{
    internal const uint CreateSuspended = 0x4;
    internal const uint CreateUnicodeEnvironment = 0x400;
    internal const uint JobKillOnClose = 0x2000;
    internal const int JobExtendedLimitInformation = 9;
    internal const int ExtendedStyle = -20;
    internal const long ToolWindow = 0x80;
    internal const uint Owner = 4;
    internal const uint Root = 2;
    internal const int Restore = 9;
    internal const uint NoSize = 0x1;
    internal const uint NoMove = 0x2;
    internal const uint NoActivate = 0x10;
    internal const uint ShowWindowFlag = 0x40;
    internal const uint Close = 0x10;
    internal const uint ExtendedFrameBounds = 9;
    internal const uint Cloaked = 14;
    internal const uint SourceCopy = 0x00CC0020;
    internal const uint CaptureLayered = 0x40000000;
    internal const int VirtualLeft = 76;
    internal const int VirtualTop = 77;
    internal const int VirtualWidth = 78;
    internal const int VirtualHeight = 79;
    internal const uint SwitchDesktop = 0x100;
    internal const uint SnapshotProcesses = 0x2;
    internal static readonly nint Topmost = -1;
    internal static readonly nint NotTopmost = -2;
    internal static readonly nint PerMonitorAwareV2 = -4;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly int Width => Right - Left;
        internal readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        internal uint Size;
        internal int Width;
        internal int Height;
        internal ushort Planes;
        internal ushort BitCount;
        internal uint Compression;
        internal uint SizeImage;
        internal int XPelsPerMeter;
        internal int YPelsPerMeter;
        internal uint ClrUsed;
        internal uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfo
    {
        internal uint Size;
        internal nint Reserved;
        internal nint Desktop;
        internal nint Title;
        internal uint X;
        internal uint Y;
        internal uint XSize;
        internal uint YSize;
        internal uint XCountChars;
        internal uint YCountChars;
        internal uint FillAttribute;
        internal uint Flags;
        internal ushort ShowWindow;
        internal ushort Reserved2Size;
        internal nint Reserved2;
        internal nint StandardInput;
        internal nint StandardOutput;
        internal nint StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        internal nint Process;
        internal nint Thread;
        internal uint ProcessId;
        internal uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobLimits
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal nuint MinimumWorkingSetSize;
        internal nuint MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal nuint Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
        internal nuint ProcessMemoryLimit;
        internal nuint JobMemoryLimit;
        internal nuint PeakProcessMemoryUsed;
        internal nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessEntry
    {
        internal uint Size;
        internal uint Usage;
        internal uint ProcessId;
        internal nuint DefaultHeapId;
        internal uint ModuleId;
        internal uint Threads;
        internal uint ParentProcessId;
        internal int BasePriority;
        internal uint Flags;
        internal fixed char ExeFile[260];
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint state);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    internal static partial nint GetWindow(nint window, uint relation);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    internal static partial int GetWindowTextLength(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    internal static partial int GetWindowText(nint window, char* text, int capacity);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    internal static partial nint WindowFromPoint(Point point);

    [LibraryImport("user32.dll")]
    internal static partial nint GetAncestor(nint window, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    internal static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(nint window, nint context);

    [LibraryImport("user32.dll", EntryPoint = "OpenInputDesktop")]
    internal static partial nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseDesktop(nint desktop);

    [LibraryImport("user32.dll")]
    internal static partial nint SetThreadDpiAwarenessContext(nint context);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateCompatibleDC(nint context);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateDIBSection(nint context, BitmapInfoHeader* info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    internal static partial nint SelectObject(nint context, nint item);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint item);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint context);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint window, uint attribute, void* value, int size);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmFlush();

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcess(string? application, char* commandLine, nint processAttributes, nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, char* environment, string? directory, StartupInfo* startup,
        out ProcessInformation information);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    internal static partial nint CreateJobObject(nint attributes, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetInformationJobObject(nint job, int kind, JobLimits* information, int size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AssignProcessToJobObject(nint job, nint process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TerminateJobObject(nint job, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint ResumeThread(nint thread);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Process32First(nint snapshot, ProcessEntry* entry);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Process32Next(nint snapshot, ProcessEntry* entry);

    /// <summary>Every process with its parent, from one Toolhelp snapshot.</summary>
    internal static List<(int Id, int Parent)> ProcessParents()
    {
        var pairs = new List<(int, int)>();
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == -1)
        {
            return pairs;
        }

        try
        {
            ProcessEntry entry = default;
            entry.Size = (uint)sizeof(ProcessEntry);
            for (var more = Process32First(snapshot, &entry); more; more = Process32Next(snapshot, &entry))
            {
                pairs.Add(((int)entry.ProcessId, (int)entry.ParentProcessId));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return pairs;
    }

    internal static string Title(nint window)
    {
        var length = GetWindowTextLength(window);
        if (length <= 0)
        {
            return "";
        }

        var buffer = new char[length + 1];
        fixed (char* text = buffer)
        {
            var copied = GetWindowText(window, text, buffer.Length);
            return new string(text, 0, copied);
        }
    }
}
