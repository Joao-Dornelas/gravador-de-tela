using System.Runtime.InteropServices;

namespace ScreenRecorder.Services;

public record CaptureArea(int X, int Y, int Width, int Height);
public record MonitorInfo(string Device, CaptureArea Bounds, bool Primary)
{
    public override string ToString() => $"{Device.Replace(@"\\.\", "")} · {Bounds.Width} × {Bounds.Height}{(Primary ? " · principal" : "")}";
}

public static class Monitors
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Info { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    private delegate bool MonitorCallback(nint monitor, nint dc, nint rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref Info info);
    public static IReadOnlyList<MonitorInfo> GetAll()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(0, 0, (nint handle, nint dc, nint rect, nint data) =>
        {
            var info = new Info { Size = Marshal.SizeOf<Info>(), Device = "" };
            if (GetMonitorInfo(handle, ref info))
                monitors.Add(new(info.Device, new(info.Monitor.Left, info.Monitor.Top,
                    info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top), (info.Flags & 1) != 0));
            return true;
        }, 0);
        return monitors.OrderByDescending(m => m.Primary).ToList();
    }
}
