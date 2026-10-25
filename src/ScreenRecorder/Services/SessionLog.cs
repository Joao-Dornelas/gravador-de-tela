using System.IO;
namespace ScreenRecorder.Services;
internal static class SessionLog
{
    public static void Write(string message)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenRecorder");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "session.log"), $"{DateTimeOffset.Now:O} {message}\n");
        }
        catch { }
    }
}
