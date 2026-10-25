using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ScreenRecorder.Services;

public sealed record ShortcutBinding(int Id, string Action, KeyGesture Gesture)
{
    public string Text => new KeyGestureConverter().ConvertToInvariantString(Gesture)!;
    public uint Modifiers => ((Gesture.Modifiers.HasFlag(ModifierKeys.Alt) ? 1u : 0) |
                              (Gesture.Modifiers.HasFlag(ModifierKeys.Control) ? 2u : 0) |
                              (Gesture.Modifiers.HasFlag(ModifierKeys.Shift) ? 4u : 0));
    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Gesture.Key);
}

public sealed class GlobalShortcuts(nint window) : IDisposable
{
    private List<ShortcutBinding> current = [];
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    public static List<ShortcutBinding> Parse(Preferences preferences)
    {
        var bindings = new List<ShortcutBinding>();
        foreach (var entry in new[] { (1, "Iniciar", preferences.StartShortcut), (2, "Pausar/retomar", preferences.PauseShortcut), (3, "Finalizar", preferences.StopShortcut) })
        {
            if (string.IsNullOrWhiteSpace(entry.Item3)) continue;
            KeyGesture gesture;
            try { gesture = (KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(entry.Item3)!; }
            catch { throw new ArgumentException($"Atalho inválido para {entry.Item2}. Pressione a combinação desejada no campo."); }
            if (gesture.Key == Key.None || gesture.Modifiers.HasFlag(ModifierKeys.Windows) ||
                (gesture.Modifiers == ModifierKeys.Alt && gesture.Key == Key.F4))
                throw new ArgumentException($"O atalho de {entry.Item2} é reservado pelo Windows. Escolha outra combinação.");
            var binding = new ShortcutBinding(entry.Item1, entry.Item2, gesture);
            if (bindings.Any(b => b.Modifiers == binding.Modifiers && b.VirtualKey == binding.VirtualKey))
                throw new ArgumentException("Cada ação precisa ter um atalho diferente.");
            bindings.Add(binding);
        }
        return bindings;
    }
    public bool TryApply(Preferences preferences, out string? error)
    {
        List<ShortcutBinding> desired;
        try { desired = Parse(preferences); }
        catch (ArgumentException e) { error = e.Message; return false; }
        var old = current.ToList();
        foreach (var binding in current) UnregisterHotKey(window, binding.Id);
        current.Clear();
        foreach (var binding in desired)
        {
            if (RegisterHotKey(window, binding.Id, binding.Modifiers | 0x4000, binding.VirtualKey)) { current.Add(binding); continue; }
            foreach (var added in current) UnregisterHotKey(window, added.Id);
            current.Clear();
            foreach (var previous in old)
                if (RegisterHotKey(window, previous.Id, previous.Modifiers | 0x4000, previous.VirtualKey)) current.Add(previous);
            error = $"{binding.Text} não está disponível para {binding.Action}. Outro aplicativo pode estar usando esse atalho. Escolha outra combinação.";
            return false;
        }
        error = null; return true;
    }
    public void Dispose()
    {
        foreach (var binding in current) UnregisterHotKey(window, binding.Id);
        current.Clear();
    }
}
