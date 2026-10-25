using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenRecorder.Services;

internal static class MouseTests
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    public static async Task<int> RunAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new List<object>();
        var fixture = new Window { Title = "Teste de cursor", Width = 640, Height = 480, Background = Brushes.White, Cursor = Cursors.Arrow, WindowStyle = WindowStyle.None, Topmost = true };
        var failures = 0;
        try
        {
            GetCursorPos(out var pointer);
            var monitor = Monitors.GetAll().First(m => pointer.X >= m.Bounds.X && pointer.X < m.Bounds.X + m.Bounds.Width && pointer.Y >= m.Bounds.Y && pointer.Y < m.Bounds.Y + m.Bounds.Height);
            var x = Math.Clamp(pointer.X - 320, monitor.Bounds.X, monitor.Bounds.X + monitor.Bounds.Width - 640);
            var y = Math.Clamp(pointer.Y - 240, monitor.Bounds.Y, monitor.Bounds.Y + monitor.Bounds.Height - 480);
            fixture.Show();
            SetWindowPos(new WindowInteropHelper(fixture).Handle, -1, x, y, 640, 480, 0x0040);
            await Task.Delay(300);
            var area = new CaptureArea(Math.Clamp(pointer.X - monitor.Bounds.X - 80, 0, monitor.Bounds.Width - 160), Math.Clamp(pointer.Y - monitor.Bounds.Y - 80, 0, monitor.Bounds.Height - 160), 160, 160);
            foreach (var mode in new[] { (Cursor: false, Halo: false), (Cursor: true, Halo: false), (Cursor: false, Halo: true), (Cursor: true, Halo: true) })
            {
                var session = new RecordingSession();
                try
                {
                    await session.StartAsync(new(monitor, area, folder, "MP4", "H.264", false, false, 30, mode.Cursor, mode.Halo));
                    await Task.Delay(1500);
                    var video = await session.StopAsync();
                    GetCursorPos(out var now);
                    if (now.X != pointer.X || now.Y != pointer.Y) throw new Exception("O mouse foi movido durante o teste visual. Repita mantendo o ponteiro parado.");
                    var png = Path.ChangeExtension(video, ".png");
                    await MediaTools.RunAsync("ffmpeg", "-v", "error", "-y", "-ss", "0.7", "-i", video, "-frames:v", "1", png);
                    using var stream = File.OpenRead(png);
                    var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                    var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                    var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                    bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                    int yellow = 0, dark = 0;
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
                        if (r > 220 && g > 220 && b < 225) yellow++;
                        if (r < 90 && g < 90 && b < 90) dark++;
                    }
                    if ((yellow > 150) != mode.Halo) throw new Exception($"Realce incorreto: {yellow} pixels amarelos.");
                    if ((dark > 5) != mode.Cursor) throw new Exception($"Visibilidade do cursor incorreta: {dark} pixels escuros.");
                    report.Add(new { mode.Cursor, mode.Halo, Result = "PASS", YellowPixels = yellow, CursorPixels = dark, Path = video });
                    Console.WriteLine($"PASS cursor={mode.Cursor}, halo={mode.Halo}: yellow={yellow}, dark={dark}");
                }
                catch (Exception error)
                {
                    if (session.State is SessionState.Recording or SessionState.Paused) { try { await session.StopAsync(); } catch { } }
                    failures++;
                    report.Add(new { mode.Cursor, mode.Halo, Result = "FAIL", Error = error.ToString() });
                    Console.WriteLine($"FAIL cursor={mode.Cursor}, halo={mode.Halo}: {error.Message}");
                }
            }
        }
        catch (Exception error) { failures++; report.Add(new { Result = "FAIL", Error = error.ToString() }); Console.WriteLine(error); }
        finally
        {
            fixture.Close();
            File.WriteAllText(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        return failures == 0 ? 0 : 1;
    }
}
