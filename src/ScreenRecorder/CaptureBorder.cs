using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenRecorder.Services;

namespace ScreenRecorder;

public sealed class CaptureBorder : Window
{
    private readonly Border outline = new() { BorderBrush = Brushes.DodgerBlue, BorderThickness = new Thickness(3), Background = Brushes.Transparent };
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    public CaptureBorder(MonitorInfo monitor, CaptureArea area)
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Content = outline;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; Focusable = false; IsHitTestVisible = false;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, -20, GetWindowLongPtr(handle, -20) | 0x080000A0);
            // Place the border outside the capture area; clip to the physical monitor edges.
            var left = Math.Max(monitor.Bounds.X, monitor.Bounds.X + area.X - 3);
            var top = Math.Max(monitor.Bounds.Y, monitor.Bounds.Y + area.Y - 3);
            var right = Math.Min(monitor.Bounds.X + monitor.Bounds.Width, monitor.Bounds.X + area.X + area.Width + 3);
            var bottom = Math.Min(monitor.Bounds.Y + monitor.Bounds.Height, monitor.Bounds.Y + area.Y + area.Height + 3);
            SetWindowPos(handle, -1, left, top, right - left, bottom - top, 0x0010);
            outline.BorderThickness = new Thickness(3.0 * 96 / GetDpiForWindow(handle));
            // Older Windows versions do not support exclusion; the outside border still marks the region.
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) SetWindowDisplayAffinity(handle, 0x11);
        };
    }
    public void SetState(SessionState state) => outline.BorderBrush = state switch
    {
        SessionState.Recording => Brushes.Red,
        SessionState.Paused => Brushes.Orange,
        _ => Brushes.DodgerBlue
    };
}
