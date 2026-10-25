using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ScreenRecorder.Services;

public sealed class Preferences
{
    public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Gravador de Tela");
    public string Format { get; set; } = "MP4";
    public string Codec { get; set; } = "H.264";
    public bool SystemAudio { get; set; } = true;
    public bool Microphone { get; set; }
    public bool RecordMouse { get; set; } = true;
    public bool HighlightMouse { get; set; }
    public string StartShortcut { get; set; } = "Ctrl+Shift+F8";
    public string PauseShortcut { get; set; } = "Ctrl+Shift+F9";
    public string StopShortcut { get; set; } = "Ctrl+Shift+F10";
    public int Fps { get; set; } = 30;
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenRecorder", "settings.json");
    public static Preferences Load()
    {
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public static class MediaTools
{
    public static string Tool(string name) => Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", name + ".exe");
    public static async Task<string> RunAsync(string name, params string[] args)
    {
        var start = new ProcessStartInfo(Tool(name)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar o processamento do vídeo.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stderr = await error;
        var stdout = await output;
        if (process.ExitCode != 0) throw new IOException($"Falha ao processar o vídeo: {stderr}");
        return stdout;
    }
}
