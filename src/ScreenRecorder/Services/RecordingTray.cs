using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ScreenRecorder.Services;

internal sealed class RecordingTray : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Forms.ToolStripMenuItem start, pause, stop;
    private readonly Drawing.Icon image;
    public RecordingTray(Action onStart, Action onPause, Action onStop, Action onShow, Action onFolder, Action onExit)
    {
        var assembly = typeof(MainWindow).Assembly.GetName().Name;
        using var resource = System.Windows.Application.GetResourceStream(new Uri($"/{assembly};component/Assets/App.ico", UriKind.Relative)).Stream;
        using var sharedIcon = new Drawing.Icon(resource, 32, 32);
        image = (Drawing.Icon)sharedIcon.Clone();
        start = new Forms.ToolStripMenuItem("Iniciar gravação", null, (_, _) => onStart());
        pause = new Forms.ToolStripMenuItem("Pausar", null, (_, _) => onPause());
        stop = new Forms.ToolStripMenuItem("Finalizar e salvar", null, (_, _) => onStop());
        menu.Items.AddRange([start, pause, stop, new Forms.ToolStripSeparator(),
            new Forms.ToolStripMenuItem("Mostrar janela", null, (_, _) => onShow()),
            new Forms.ToolStripMenuItem("Abrir pasta das gravações", null, (_, _) => onFolder()),
            new Forms.ToolStripSeparator(), new Forms.ToolStripMenuItem("Sair", null, (_, _) => onExit())]);
        icon = new Forms.NotifyIcon { Icon = image, Text = "Gravador de Tela", ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) menu.Show(Forms.Cursor.Position); };
        Update(SessionState.Idle, TimeSpan.Zero, true);
    }
    public void Update(SessionState state, TimeSpan elapsed, bool canStart)
    {
        start.Enabled = state == SessionState.Idle && canStart;
        pause.Enabled = stop.Enabled = state is SessionState.Recording or SessionState.Paused;
        pause.Text = state == SessionState.Paused ? "Retomar" : "Pausar";
        var status = state switch { SessionState.Idle => "Pronto", SessionState.Starting => "Preparando", SessionState.Recording => "Gravando", SessionState.Paused => "Pausado", _ => "Salvando" };
        icon.Text = $"Gravador de Tela · {status} · {elapsed:hh\\:mm\\:ss}";
    }
    public void Dispose() { icon.Visible = false; icon.Dispose(); menu.Dispose(); image.Dispose(); }
}
