using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace QingMou;

internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y,
        int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(Point point, uint flags);

    [LibraryImport("Shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    public static TimeSpan IdleTime
    {
        get
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
        }
    }

    public static bool ForegroundIsFullscreen()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return false;
        GetWindowThreadProcessId(handle, out var processId);
        if (processId == Environment.ProcessId) return false;
        var bounds = Forms.Screen.FromHandle(handle).Bounds;
        const int tolerance = 3;
        return rect.Left <= bounds.Left + tolerance && rect.Top <= bounds.Top + tolerance &&
               rect.Right >= bounds.Right - tolerance && rect.Bottom >= bounds.Bottom - tolerance;
    }

    public static void SetBoundsInPixels(Window window, System.Drawing.Rectangle bounds)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
            SetWindowPos(handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0004 | 0x0010);
    }

    public static double GetScaleForScreen(Forms.Screen screen)
    {
        try
        {
            var point = new Point { X = screen.Bounds.Left + screen.Bounds.Width / 2,
                                    Y = screen.Bounds.Top + screen.Bounds.Height / 2 };
            var monitor = MonitorFromPoint(point, 2);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var x, out _) == 0)
                return x / 96.0;
        }
        catch (Exception) { }
        return 1.0;
    }
}
