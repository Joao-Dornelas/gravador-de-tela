using System.ComponentModel;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using ScreenRecorder.Services;

namespace ScreenRecorder;

public partial class MainWindow : Window
{
    private readonly RecordingSession session = new();
    private Preferences preferences = Preferences.Load();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CaptureArea? selectedArea;
    private CaptureBorder? captureBorder;
    private RecordingTray? tray;
    private GlobalShortcuts? shortcuts;
    private HwndSource? source;
    private bool settingsOpen, closingAfterSave, exitRequested, startPending;

    public MainWindow()
    {
        InitializeComponent();
        MonitorBox.ItemsSource = Monitors.GetAll(); MonitorBox.SelectedIndex = 0;
        session.StateChanged += state => Dispatcher.BeginInvoke(() => UpdateState(state));
        session.Failed += error => Dispatcher.BeginInvoke(async () =>
        {
            if (session.State is SessionState.Recording or SessionState.Paused)
            {
                try { await session.StopAsync(); } catch { }
                selectedArea = null; UpdateAreaLabel();
                ShowError(new IOException(error));
            }
        });
        timer.Tick += (_, _) =>
        {
            TimerText.Text = session.Elapsed.ToString(@"hh\:mm\:ss");
            UpdateTray();
        };
        timer.Start();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            source = HwndSource.FromHwnd(handle); source.AddHook(WindowMessage);
            shortcuts = new GlobalShortcuts(handle);
            if (!shortcuts.TryApply(preferences, out var error))
            {
                ShortcutText.Text = "Atalho indisponível. Ajuste na engrenagem.";
                ShortcutText.ToolTip = error;
            }
            else RefreshShortcutText();
            tray = new RecordingTray(
                () => StartClick(this, new RoutedEventArgs()),
                () => PauseClick(this, new RoutedEventArgs()),
                () => StopClick(this, new RoutedEventArgs()),
                RestoreWindow,
                () => OpenFolderClick(this, new RoutedEventArgs()), Close);
            UpdateTray();
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && tray != null) Hide(); };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            timer.Stop(); captureBorder?.Close(); tray?.Dispose(); shortcuts?.Dispose();
            source?.RemoveHook(WindowMessage);
        };
        if (MonitorBox.Items.Count == 0) { StartButton.IsEnabled = false; StatusText.Text = "Nenhum monitor disponível."; }
    }
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && !settingsOpen)
        {
            if (wParam == 1) StartClick(this, new RoutedEventArgs());
            if (wParam == 2) PauseClick(this, new RoutedEventArgs());
            if (wParam == 3) StopClick(this, new RoutedEventArgs());
            handled = true;
        }
        return 0;
    }
    private void RestoreWindow()
    {
        Show(); WindowState = WindowState.Normal; Activate();
    }
    private void MinimizeToTray()
    {
        WindowState = WindowState.Minimized;
        if (tray != null) Hide();
    }
    private void UpdateTray() => tray?.Update(session.State, session.Elapsed, !settingsOpen && !startPending && MonitorBox.SelectedItem != null);
    private void RefreshShortcutText()
    {
        static string Display(string text) => string.IsNullOrWhiteSpace(text) ? "desativado" : text;
        ShortcutText.Text = $"Iniciar: {Display(preferences.StartShortcut)} · Pausar: {Display(preferences.PauseShortcut)} · Finalizar: {Display(preferences.StopShortcut)}";
        ShortcutText.ToolTip = ShortcutText.Text;
    }
    private string? ApplySettings(Preferences updated)
    {
        if (shortcuts == null) return "A janela ainda está sendo preparada. Tente novamente.";
        if (!shortcuts.TryApply(updated, out var error)) return error;
        try { updated.Save(); preferences = updated; RefreshShortcutText(); return null; }
        catch (Exception e) { shortcuts.Dispose(); return e.Message; }
    }
    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (session.State != SessionState.Idle || settingsOpen) return;
        settingsOpen = true; shortcuts?.Dispose(); UpdateTray();
        try { new SettingsWindow(preferences, ApplySettings) { Owner = this }.ShowDialog(); }
        finally
        {
            settingsOpen = false;
            if (shortcuts != null && !shortcuts.TryApply(preferences, out var error))
            {
                ShortcutText.Text = "Atalho indisponível. Ajuste na engrenagem.";
                ShortcutText.ToolTip = error;
            }
            else RefreshShortcutText();
            UpdateTray();
        }
    }
    private void UpdateState(SessionState state)
    {
        OptionsPanel.IsEnabled = state == SessionState.Idle;
        SettingsButton.IsEnabled = state == SessionState.Idle;
        FullScreenButton.IsEnabled = SelectAreaButton.IsEnabled = state == SessionState.Idle;
        var recording = state == SessionState.Recording;
        var paused = state == SessionState.Paused;
        StartButton.IsEnabled = recording || paused || (state == SessionState.Idle && !startPending && MonitorBox.SelectedItem != null);
        StopButton.IsEnabled = recording || paused;
        RecordingGlyph.Data = recording ? Geometry.Parse("M0,0 H6 V18 H0 Z M11,0 H17 V18 H11 Z")
            : paused ? Geometry.Parse("M0,0 L16,9 L0,18 Z") : new EllipseGeometry(new Point(9, 9), 9, 9);
        RecordingGlyph.Fill = (SolidColorBrush)new BrushConverter().ConvertFromString(recording ? "#A16207" : paused ? "#15803D" : "#DC2626")!;
        StartButton.Background = (SolidColorBrush)new BrushConverter().ConvertFromString(recording ? "#FEF3C7" : paused ? "#DCFCE7" : "#FEE2E2")!;
        StartButton.ToolTip = recording ? "Pausar gravação" : paused ? "Retomar gravação" : "Iniciar gravação";
        System.Windows.Automation.AutomationProperties.SetName(StartButton, (string)StartButton.ToolTip);
        captureBorder?.SetState(state); UpdateTray();
        if (state != SessionState.Idle) StatusText.ToolTip = null;
        if (state != SessionState.Idle) StatusText.Text = state switch
        {
            SessionState.Starting => "Preparando a gravação…",
            SessionState.Recording => "Gravando na bandeja",
            SessionState.Paused => "Gravação pausada",
            _ => "Finalizando e salvando o vídeo…"
        };
    }
    private void PrimaryRecordingClick(object sender, RoutedEventArgs e)
    {
        if (session.State is SessionState.Recording or SessionState.Paused) PauseClick(sender, e);
        else if (session.State == SessionState.Idle) StartClick(sender, e);
    }
    private async void StartClick(object sender, RoutedEventArgs e)
    {
        if (session.State != SessionState.Idle || settingsOpen || startPending || MonitorBox.SelectedItem is not MonitorInfo monitor) return;
        startPending = true; StartButton.IsEnabled = false; UpdateTray();
        try
        {
            MinimizeToTray();
            // Let the compositor remove the main window before capturing the first frame.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await session.StartAsync(new(monitor, selectedArea, preferences.Folder, preferences.Format, preferences.Codec,
                preferences.SystemAudio, preferences.Microphone, preferences.Fps, preferences.RecordMouse, preferences.HighlightMouse));
            if (exitRequested) await FinishAsync();
        }
        catch (Exception error) { ShowError(error); UpdateState(session.State); if (exitRequested) { closingAfterSave = true; Close(); } }
        finally { startPending = false; if (!closingAfterSave) UpdateState(session.State); }
    }
    private void PauseClick(object sender, RoutedEventArgs e)
    {
        try { if (session.State == SessionState.Paused) session.Resume(); else session.Pause(); }
        catch (Exception error) { ShowError(error); }
    }
    private async void StopClick(object sender, RoutedEventArgs e)
    {
        if (session.State is not (SessionState.Recording or SessionState.Paused)) return;
        await FinishAsync();
    }
    private async Task FinishAsync()
    {
        Exception? failure = null;
        try { var path = await session.StopAsync(); StatusText.Text = $"Salvo: {Path.GetFileName(path)}"; StatusText.ToolTip = path; }
        catch (Exception error) { failure = error; }
        finally
        {
            // A finished session must not leave the previous region or its border active.
            selectedArea = null;
            UpdateAreaLabel();
        }
        if (failure != null) ShowError(failure);
        if (exitRequested) { closingAfterSave = true; Close(); }
        else RestoreWindow();
    }
    private bool SelectArea()
    {
        if (MonitorBox.SelectedItem is not MonitorInfo monitor) return false;
        captureBorder?.Hide(); Hide();
        try
        {
            var selector = new AreaSelector(monitor);
            if (selector.ShowDialog() != true) return false;
            selectedArea = selector.Selection; UpdateAreaLabel(); return true;
        }
        finally { captureBorder?.Show(); RestoreWindow(); }
    }
    private void SelectAreaClick(object sender, RoutedEventArgs e) { try { SelectArea(); } catch (Exception error) { ShowError(error); } }
    private void MonitorChanged(object sender, SelectionChangedEventArgs e) { selectedArea = null; UpdateAreaLabel(); }
    private void FullScreenClick(object sender, RoutedEventArgs e) { selectedArea = null; UpdateAreaLabel(); }
    private void UpdateAreaLabel()
    {
        if (AreaText == null) return;
        AreaText.Text = selectedArea is { } a ? $"Área: {a.Width} × {a.Height} px · posição {a.X}, {a.Y}" : "Todo o monitor selecionado";
        FullScreenButton.Background = selectedArea == null ? new SolidColorBrush(Color.FromRgb(221, 233, 255)) : Brushes.White;
        SelectAreaButton.Background = selectedArea != null ? new SolidColorBrush(Color.FromRgb(237, 225, 255)) : Brushes.White;
        FullScreenButton.BorderBrush = selectedArea == null ? Brushes.RoyalBlue : Brushes.LightGray;
        SelectAreaButton.BorderBrush = selectedArea != null ? Brushes.MediumPurple : Brushes.LightGray;
        System.Windows.Automation.AutomationProperties.SetItemStatus(FullScreenButton, selectedArea == null ? "Selecionado" : "");
        System.Windows.Automation.AutomationProperties.SetItemStatus(SelectAreaButton, selectedArea != null ? "Selecionado" : "");
        captureBorder?.Close(); captureBorder = null;
        if (selectedArea is { } area && MonitorBox.SelectedItem is MonitorInfo monitor)
        {
            captureBorder = new CaptureBorder(monitor, area); captureBorder.Show();
        }
    }
    private void OpenFolderClick(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(preferences.Folder); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { preferences.Folder }, UseShellExecute = true }); }
        catch (Exception error) { ShowError(error); }
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closingAfterSave || (session.State == SessionState.Idle && !startPending)) return;
        e.Cancel = true; exitRequested = true;
        if (session.State is SessionState.Starting or SessionState.Stopping || (startPending && session.State == SessionState.Idle)) return;
        await FinishAsync();
    }
    private void ShowError(Exception error)
    {
        RestoreWindow(); StatusText.Text = "Não foi possível concluir a operação.";
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenRecorder");
            Directory.CreateDirectory(folder); File.AppendAllText(Path.Combine(folder, "errors.log"), $"{DateTimeOffset.Now:O} {error}\n");
        }
        catch { }
        MessageBox.Show(this, error.Message, "Gravador de Tela", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
