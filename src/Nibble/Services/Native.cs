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

    /// <summary>
    /// Expands the window to fill its monitor, over the taskbar.
    ///
    /// Sizing alone is not enough: the taskbar is an always-on-top window, so a window that is
    /// merely the size of the monitor still has its bottom edge underneath it. Measured on a
    /// 1920x1080 screen with the window at 0,0 1920x1080: WindowFromPoint at 960,1050 and
    /// 960,1075 returned MSTaskSwWClass, the taskbar, not the page - the bottom 60 px of the
    /// screen belonged to Windows. So the window also goes topmost, and the shell is told it is
    /// full-screen (MarkFullscreenWindow), which is what makes Explorer take the taskbar away
    /// instead of leaving it behind the page.
    /// </summary>
    public static bool CoverMonitor(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (monitor == IntPtr.Zero) return false;

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return false;

            // SWP_NOZORDER: the window's `Topmost` property owns the z-order (WPF rewrites the
            // extended style whenever it syncs the window, which quietly undoes a poke from
            // here - measured: the topmost bit read back false a moment after setting it).
            SetWindowPos(hwnd, IntPtr.Zero,
                info.Monitor.Left, info.Monitor.Top,
                info.Monitor.Right - info.Monitor.Left,
                info.Monitor.Bottom - info.Monitor.Top,
                0x0040 /* SWP_SHOWWINDOW */ | 0x0004 /* SWP_NOZORDER */);
            TellShellFullscreen(hwnd, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Undoes <see cref="CoverMonitor"/>: not full-screen, and not above everything.</summary>
    public static void LeaveFullscreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            TellShellFullscreen(hwnd, false);
        }
        catch
        {
            // Cosmetic: the window still goes back to its size.
        }
    }

    // ITaskbarList2, for the one call that tells Explorer a window is full-screen. Declared by
    // hand because it is not worth a dependency for a single method; the five methods before it
    // are ITaskbarList's, in vtable order.
    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList
    {
    }

    [ComImport]
    [Guid("602D4995-B13A-429b-A66E-1935E44F4317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList2
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
    }

    private static ITaskbarList2? _taskbar;

    private static void TellShellFullscreen(IntPtr hwnd, bool fullscreen)
    {
        try
        {
            _taskbar ??= (ITaskbarList2)new TaskbarList();
            _taskbar.HrInit();
            _taskbar.MarkFullscreenWindow(hwnd, fullscreen);
        }
        catch
        {
            // Without it the taskbar stays where it is; being topmost still keeps the page whole.
        }
    }
}
