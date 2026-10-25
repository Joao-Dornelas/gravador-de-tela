using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ScreenRecorder.Services;

namespace ScreenRecorder;

public partial class SettingsWindow : Window
{
    private readonly Func<Preferences, string?>? applySettings;
    public Preferences? SavedPreferences { get; private set; }
    public SettingsWindow(Preferences preferences, Func<Preferences, string?>? applySettings = null)
    {
        InitializeComponent();
        this.applySettings = applySettings;
        StartShortcutBox.Text = preferences.StartShortcut;
        PauseShortcutBox.Text = preferences.PauseShortcut;
        StopShortcutBox.Text = preferences.StopShortcut;
        FormatBox.SelectedIndex = preferences.Format == "MKV" ? 1 : 0;
        CodecBox.SelectedIndex = preferences.Codec == "H.265" ? 1 : 0;
        FpsBox.SelectedIndex = preferences.Fps == 60 ? 1 : 0;
        FolderText.Text = preferences.Folder;
        SystemAudioCheck.IsChecked = preferences.SystemAudio;
        MicrophoneCheck.IsChecked = preferences.Microphone;
        RecordMouseCheck.IsChecked = preferences.RecordMouse;
        HighlightMouseCheck.IsChecked = preferences.HighlightMouse;
    }
    private void CaptureShortcut(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape) return;
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var box = (TextBox)sender;
        if ((key is Key.Back or Key.Delete) && Keyboard.Modifiers == ModifierKeys.None) { box.Text = ""; return; }
        try
        {
            var gesture = new KeyGesture(key, Keyboard.Modifiers);
            box.Text = new KeyGestureConverter().ConvertToInvariantString(gesture)!;
        }
        catch (NotSupportedException) { box.ToolTip = "Use Ctrl/Alt com uma tecla ou uma tecla F1–F24."; }
        catch (ArgumentException) { box.ToolTip = "Use Ctrl/Alt com uma tecla ou uma tecla F1–F24."; }
    }
    private static string Choice(ComboBox box) => (string)((ComboBoxItem)box.SelectedItem).Content;
    private void ChooseFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Escolher pasta das gravações", Multiselect = false };
        if (Directory.Exists(FolderText.Text)) dialog.InitialDirectory = FolderText.Text;
        if (dialog.ShowDialog(this) == true) FolderText.Text = dialog.FolderName;
    }
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var updated = new Preferences
            {
                Folder = Path.GetFullPath(FolderText.Text), Format = Choice(FormatBox), Codec = Choice(CodecBox), Fps = int.Parse(Choice(FpsBox)),
                SystemAudio = SystemAudioCheck.IsChecked == true, Microphone = MicrophoneCheck.IsChecked == true,
                RecordMouse = RecordMouseCheck.IsChecked == true, HighlightMouse = HighlightMouseCheck.IsChecked == true,
                StartShortcut = StartShortcutBox.Text, PauseShortcut = PauseShortcutBox.Text, StopShortcut = StopShortcutBox.Text
            };
            GlobalShortcuts.Parse(updated);
            if (applySettings is null) updated.Save();
            else if (applySettings(updated) is { } error) throw new InvalidOperationException(error);
            SavedPreferences = updated;
            DialogResult = true;
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Não foi possível salvar", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
