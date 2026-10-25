using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ScreenRecorder.Services;

namespace ScreenRecorder;

/// <summary>A click-through, nonactivating halo. All coordinates from Win32 are physical pixels.</summary>
internal sealed class CursorHighlight : Window
{
    private const int Diameter = 64;
    private readonly MonitorInfo monitor;
    private readonly CaptureArea area;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private nint handle;
    private bool paused;
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size; public uint Flags; public nint Cursor; public Point Position; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    public CursorHighlight(MonitorInfo display, CaptureArea? selectedArea)
    {
        monitor = display;
        area = selectedArea ?? new CaptureArea(0, 0, display.Bounds.Width, display.Bounds.Height);
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Focusable = false; IsHitTestVisible = false;
        Topmost = true; Width = Height = Diameter; Opacity = 0;
        Content = new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(102, 255, 255, 0)), Margin = new Thickness(4) };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            // WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE.
            SetWindowLongPtr(handle, -20, GetWindowLongPtr(handle, -20) | 0x080000A0);
            UpdatePosition(); timer.Start();
        };
        timer.Tick += (_, _) => UpdatePosition();
        Closed += (_, _) => timer.Stop();
    }
    public void SetPaused(bool value) { paused = value; UpdatePosition(); }
    private void UpdatePosition()
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (paused || !GetCursorInfo(ref info) || (info.Flags & 1) == 0) { Opacity = 0; return; }
        var x = info.Position.X - monitor.Bounds.X;
        var y = info.Position.Y - monitor.Bounds.Y;
        if (x < area.X || y < area.Y || x >= area.X + area.Width || y >= area.Y + area.Height) { Opacity = 0; return; }
        // Keep the window physically centered on the cursor hotspot, including mixed-DPI monitors.
        SetWindowPos(handle, -1, info.Position.X - Diameter / 2, info.Position.Y - Diameter / 2, Diameter, Diameter, 0x0010);
        Opacity = 1;
    }
}
