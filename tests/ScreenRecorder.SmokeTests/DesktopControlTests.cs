using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenRecorder;
using ScreenRecorder.Services;

internal static class DesktopControlTests
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(target, args);
    private static object Property(object target, string name) => target.GetType().GetProperty(name)!.GetValue(target)!;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Until(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(65);
        while (!condition()) { if (DateTime.UtcNow > timeout) throw new TimeoutException("O controle não mudou para o estado esperado."); await Task.Delay(100); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    public static async Task<int> RunAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new List<object>();
        MainWindow? main = null;
        try
        {
            var preferences = new Preferences { Folder = Path.GetFullPath(folder), SystemAudio = false, Microphone = false,
                StartShortcut = "Ctrl+Alt+F6", PauseShortcut = "Ctrl+Alt+F7", StopShortcut = "Ctrl+Alt+F8" };
            var duplicate = new Preferences { StartShortcut = "Ctrl+F8", PauseShortcut = "Ctrl+F8" };
            try { GlobalShortcuts.Parse(duplicate); throw new Exception("Atalhos duplicados foram aceitos."); }
            catch (ArgumentException) { }
            Check(GlobalShortcuts.Parse(new Preferences { StartShortcut = "", PauseShortcut = "", StopShortcut = "" }).Count == 0, "Não foi possível desativar os atalhos.");
            main = new MainWindow();
            main.GetType().GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, preferences);
            main.Show();
            Check(main.Title == "Gravador de Tela" && main.Icon != null, "Nome ou ícone da janela incorreto.");
            report.Add(new { Test = "portuguese-name-and-window-icon", Result = "PASS" });
            var shortcuts = Field<GlobalShortcuts>(main, "shortcuts");
            Check(shortcuts.TryApply(preferences, out var registrationError), registrationError ?? "Registro dos atalhos falhou.");
            using (var blockerWindow = new HwndSource(new HwndSourceParameters("Shortcut blocker") { Width = 1, Height = 1, WindowStyle = 0 }))
            using (var blocker = new GlobalShortcuts(blockerWindow.Handle))
            {
                Check(blocker.TryApply(new Preferences { StartShortcut = "Ctrl+Alt+F12", PauseShortcut = "", StopShortcut = "" }, out _), "O atalho reservado ao teste já está em uso.");
                Check(!shortcuts.TryApply(new Preferences { StartShortcut = "Ctrl+Alt+F6", PauseShortcut = "Ctrl+Alt+F12", StopShortcut = "Ctrl+Alt+F8" }, out _), "Conflito entre atalhos não foi detectado.");
                Check(!blocker.TryApply(new Preferences { StartShortcut = "Ctrl+Alt+F6", PauseShortcut = "", StopShortcut = "" }, out _), "Os atalhos antigos não foram restaurados após conflito.");
            }
            report.Add(new { Test = "custom-shortcuts-conflict-rollback", Result = "PASS" });
            main.GetType().GetField("selectedArea", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, new CaptureArea(200, 180, 640, 360));
            Invoke(main, "UpdateAreaLabel");
            var border = Field<CaptureBorder>(main, "captureBorder");
            var monitor = (MonitorInfo)Field<ComboBox>(main, "MonitorBox").SelectedItem;
            var borderHandle = new WindowInteropHelper(border).Handle;
            GetWindowRect(borderHandle, out var rect);
            Check(rect.Left == monitor.Bounds.X + 197 && rect.Top == monitor.Bounds.Y + 177 && rect.Right - rect.Left == 646 && rect.Bottom - rect.Top == 366, "A borda não corresponde aos limites físicos da seleção.");
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) Check(GetWindowDisplayAffinity(borderHandle, out var affinity) && affinity == 0x11, "A borda não foi excluída do vídeo.");
            report.Add(new { Test = "area-border-bounds-and-capture-exclusion", Result = "PASS" });
            var tray = Field<object>(main, "tray");
            var menu = Field<object>(tray, "menu");
            var items = (IList)Property(menu, "Items");
            var icon = Field<object>(tray, "icon");
            Check((bool)Property(icon, "Visible"), "O ícone não foi exibido na bandeja.");
            var session = Field<RecordingSession>(main, "session");
            Invoke(items[0]!, "PerformClick");
            await Until(() => session.State == SessionState.Recording);
            await Task.Delay(500);
            Check(!main.IsVisible && main.WindowState == WindowState.Minimized, "A janela não foi recolhida na bandeja ao iniciar.");
            Check(!(bool)Property(items[0]!, "Enabled") && (bool)Property(items[1]!, "Enabled") && (bool)Property(items[2]!, "Enabled"), "Estado incorreto dos controles da bandeja.");
            Check(((SolidColorBrush)((Border)border.Content).BorderBrush).Color == Colors.Red, "A borda não ficou vermelha durante a gravação.");
            Field<Button>(main, "StartButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => session.State == SessionState.Paused);
            await Task.Delay(150);
            Check(((SolidColorBrush)((Border)border.Content).BorderBrush).Color == Colors.Orange, "A borda não indicou a pausa.");
            Check((string)Property(items[1]!, "Text") == "Retomar", "A bandeja não ofereceu Retomar.");
            Check((string)Field<Button>(main, "StartButton").ToolTip == "Retomar gravação", "O botão principal não virou Retomar.");
            Field<Button>(main, "StartButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => session.State == SessionState.Recording);
            await Task.Delay(700);
            Invoke(items[2]!, "PerformClick");
            await Until(() => session.State == SessionState.Idle && main.IsVisible);
            Check(Field<CaptureArea?>(main, "selectedArea") == null && Field<CaptureBorder?>(main, "captureBorder") == null && !border.IsVisible, "Finalizar deixou a seleção ou sua borda ativa.");
            Check(Field<System.Windows.Controls.TextBlock>(main, "AreaText").Text == "Todo o monitor selecionado", "O modo de tela inteira não foi restaurado.");
            Check((string)Field<Button>(main, "StartButton").ToolTip == "Iniciar gravação", "O botão principal não voltou a Iniciar.");
            report.Add(new { Test = "shared-start-pause-resume-button", Result = "PASS" });
            report.Add(new { Test = "selection-and-border-cleared-on-finish", Result = "PASS" });
            report.Add(new { Test = "tray-start-auto-hide-pause-resume-stop", Result = "PASS" });
            var video = Directory.GetFiles(folder, "*.mp4").OrderByDescending(File.GetLastWriteTimeUtc).First();
            var json = await MediaTools.RunAsync("ffprobe", "-v", "error", "-show_streams", "-of", "json", video);
            using (var document = JsonDocument.Parse(json))
            {
                var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                Check(streams.Length == 1 && streams[0].GetProperty("codec_type").GetString() == "video", "A gravação de teste acessou áudio ou perdeu a trilha de vídeo.");
                Check(streams[0].GetProperty("width").GetInt32() == 640 && streams[0].GetProperty("height").GetInt32() == 360, "Dimensões incorretas no arquivo.");
            }
            await MediaTools.RunAsync("ffmpeg", "-v", "error", "-xerror", "-i", video, "-fps_mode", "passthrough", "-f", "null", "-");
            report.Add(new { Test = "recording-output", Result = "PASS", Video = video });
            main.WindowState = WindowState.Minimized;
            await Task.Delay(100);
            Check(!main.IsVisible, "A minimização manual não recolheu a janela.");
            Invoke(items[4]!, "PerformClick");
            Check(main.IsVisible && main.WindowState == WindowState.Normal, "Mostrar janela não restaurou o aplicativo.");
            Invoke(items[7]!, "PerformClick");
            Check(!(bool)Property(icon, "Visible") && !border.IsVisible, "Sair deixou ícone ou borda órfãos.");
            report.Add(new { Test = "tray-show-exit-cleanup", Result = "PASS" });
            foreach (var entry in report) Console.WriteLine(JsonSerializer.Serialize(entry));
            return 0;
        }
        catch (Exception error) { Console.WriteLine(error); report.Add(new { Result = "FAIL", Error = error.ToString() }); return 1; }
        finally
        {
            if (main is { IsLoaded: true }) main.Close();
            File.WriteAllText(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
