using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nibble.Services;

/// <summary>Small Win32 helpers: rounded window corners and a matching dark frame.</summary>
public static class Native
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    public static void RoundCorners(Window window)
    {
        var hwnd = Handle(window);
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
        }
        catch
        {
            // Windows 10 and older simply keep square corners.
        }
    }

    public static void SetDarkFrame(Window window, bool dark)
    {
        var hwnd = Handle(window);
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref value, sizeof(int));
        }
        catch
        {
            // Non-fatal: only affects the frame tint.
        }
    }

    private static IntPtr Handle(Window window)
    {
        try
        {
            return new WindowInteropHelper(window).Handle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ---- child-window subclassing: lets the shell hear clicks that land on the page ----

    public delegate IntPtr SubclassProc(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc callback, IntPtr id, IntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc callback, IntPtr id);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Watches a child window's messages (used to catch clicks on the page surface).</summary>
    public static bool WatchClicks(IntPtr hwnd, SubclassProc callback, IntPtr id)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            return SetWindowSubclass(hwnd, callback, id, IntPtr.Zero);
        }
        catch
        {
            return false;
        }
    }

    public static void StopWatchingClicks(IntPtr hwnd, SubclassProc callback, IntPtr id)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            RemoveWindowSubclass(hwnd, callback, id);
        }
        catch
        {
            // The window is going away anyway.
        }
    }

    public static IntPtr ContinueDefault(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        DefSubclassProc(hWnd, msg, wParam, lParam);

    // ---- real full screen: cover the whole monitor, taskbar included ----

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    /// <summary>Expands the window to fill its monitor, over the taskbar.</summary>
    public static bool CoverMonitor(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (monitor == IntPtr.Zero) return false;

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return false;

            SetWindowPos(hwnd, IntPtr.Zero,
                info.Monitor.Left, info.Monitor.Top,
                info.Monitor.Right - info.Monitor.Left,
                info.Monitor.Bottom - info.Monitor.Top,
                0x0040 /* SWP_SHOWWINDOW */);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
