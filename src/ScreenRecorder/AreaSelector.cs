using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using ScreenRecorder.Services;

namespace ScreenRecorder;

public sealed class AreaSelector : Window
{
    private readonly Canvas canvas = new();
    private readonly Rectangle border = new() { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(40, 0, 180, 255)) };
    private readonly MonitorInfo monitor;
    private Point? origin;
    public CaptureArea? Selection { get; private set; }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    public AreaSelector(MonitorInfo display)
    {
        monitor = display;
        Title = "Selecionar área";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
        Topmost = true; ShowInTaskbar = true; Cursor = Cursors.Cross;
        Content = canvas;
        canvas.Children.Add(new TextBlock { Text = "Arraste para selecionar a área • Esc para cancelar", Foreground = Brushes.White, Background = Brushes.Black, Padding = new Thickness(12), FontSize = 18, IsHitTestVisible = false });
        canvas.Children.Add(border);
        SourceInitialized += (_, _) => { var b = monitor.Bounds; SetWindowPos(new WindowInteropHelper(this).Handle, -1, b.X, b.Y, b.Width, b.Height, 0x0040); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
        MouseLeftButtonDown += (_, e) => { origin = e.GetPosition(canvas); CaptureMouse(); };
        MouseMove += (_, e) => { if (origin is { } start) Draw(start, e.GetPosition(canvas)); };
        MouseLeftButtonUp += (_, e) =>
        {
            if (origin is not { } start) return;
            var end = e.GetPosition(canvas);
            ReleaseMouseCapture();
            var a = PointToScreen(start); var b = PointToScreen(end);
            var x = Math.Clamp((int)Math.Min(a.X, b.X) - monitor.Bounds.X, 0, monitor.Bounds.Width - 2);
            var y = Math.Clamp((int)Math.Min(a.Y, b.Y) - monitor.Bounds.Y, 0, monitor.Bounds.Height - 2);
            var width = Math.Min((int)Math.Abs(a.X - b.X), monitor.Bounds.Width - x) / 2 * 2;
            var height = Math.Min((int)Math.Abs(a.Y - b.Y), monitor.Bounds.Height - y) / 2 * 2;
            if (width < 16 || height < 16) { origin = null; border.Width = border.Height = 0; return; }
            Selection = new(x, y, width, height); DialogResult = true;
        };
    }
    private void Draw(Point a, Point b)
    {
        Canvas.SetLeft(border, Math.Min(a.X, b.X)); Canvas.SetTop(border, Math.Min(a.Y, b.Y));
        border.Width = Math.Abs(a.X - b.X); border.Height = Math.Abs(a.Y - b.Y);
    }
}
