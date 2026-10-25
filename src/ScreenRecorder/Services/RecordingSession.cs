using System.Diagnostics;
using System.IO;
using ScreenRecorderLib;

namespace ScreenRecorder.Services;

public enum SessionState { Idle, Starting, Recording, Paused, Stopping }
public record RecordingSettings(MonitorInfo Monitor, CaptureArea? Area, string Folder, string Format, string Codec, bool SystemAudio, bool Microphone, int Fps, bool RecordMouse = true, bool HighlightMouse = false);

public sealed class RecordingSession
{
    private Recorder? recorder;
    private CursorHighlight? cursorHighlight;
    private TaskCompletionSource<string>? finished;
    private TaskCompletionSource? started;
    private RecordingSettings? settings;
    private string? capturePath;
    private string? destination;
    private readonly Stopwatch clock = new();
    public SessionState State { get; private set; }
    public TimeSpan Elapsed => clock.Elapsed;
    public event Action<SessionState>? StateChanged;
    public event Action<string>? Failed;
    private void SetState(SessionState state) { State = state; StateChanged?.Invoke(state); }

    public async Task StartAsync(RecordingSettings config)
    {
        if (State != SessionState.Idle) throw new InvalidOperationException("Já há uma gravação em andamento.");
        settings = config;
        SessionLog.Write($"Start: {config}; Base={AppContext.BaseDirectory}; Assembly={typeof(RecordingSession).Assembly.Location}");
        Directory.CreateDirectory(config.Folder);
        destination = Path.Combine(config.Folder, $"Gravacao_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid().ToString("N")[..6]}.{config.Format.ToLowerInvariant()}");
        capturePath = config.Format == "MP4" ? destination : Path.ChangeExtension(destination, ".capture.mp4");
        var options = RecorderOptions.Default;
        var source = new DisplayRecordingSource(config.Monitor.Device) { RecorderApi = RecorderApi.WindowsGraphicsCapture, IsCursorCaptureEnabled = config.RecordMouse };
        if (config.Area is { } area)
        {
            if (area.Width < 16 || area.Height < 16 || area.X < 0 || area.Y < 0 || area.X + area.Width > config.Monitor.Bounds.Width || area.Y + area.Height > config.Monitor.Bounds.Height)
                throw new ArgumentException("A área selecionada está fora do monitor.");
            source.SourceRect = new ScreenRect(area.X, area.Y, area.Width, area.Height);
        }
        options.SourceOptions.RecordingSources.Add(source);
        options.MouseOptions.IsMousePointerEnabled = config.RecordMouse;
        options.MouseOptions.IsMouseClicksDetected = false;
        var size = config.Area ?? config.Monitor.Bounds;
        options.OutputOptions.OutputFrameSize = new ScreenSize(size.Width / 2 * 2, size.Height / 2 * 2);
        options.VideoEncoderOptions.Framerate = config.Fps;
        options.VideoEncoderOptions.Quality = 75;
        options.VideoEncoderOptions.IsFixedFramerate = true;
        options.VideoEncoderOptions.IsHardwareEncodingEnabled = true;
        options.VideoEncoderOptions.Encoder = config.Codec == "H.265" ? new H265VideoEncoder() : new H264VideoEncoder();
        options.AudioOptions.IsAudioEnabled = config.SystemAudio || config.Microphone;
        if (config.SystemAudio) options.AudioOptions.AudioSources.Add(LoopbackAudioSource.Default ?? throw new InvalidOperationException("Nenhuma saída de áudio disponível. Desmarque Áudio do PC ou conecte um dispositivo."));
        if (config.Microphone) options.AudioOptions.AudioSources.Add(CaptureAudioSource.Default ?? throw new InvalidOperationException("Nenhum microfone disponível. Conecte um microfone ou desmarque essa opção."));
        clock.Reset();
        finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SetState(SessionState.Starting);
        try
        {
            SessionLog.Write("Creating recorder");
            options.LogOptions.IsLogEnabled = true;
            options.LogOptions.LogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenRecorder", "recorder-native.log");
            recorder = Recorder.CreateRecorder(options);
            SessionLog.Write("Recorder created");
            recorder.OnRecordingComplete += (_, e) => finished.TrySetResult(e.FilePath);
            recorder.OnRecordingFailed += (_, e) =>
            {
                SessionLog.Write($"Native failure: {e.Error}");
                var error = new IOException($"Falha na gravação ({config.Codec}): {e.Error}");
                started.TrySetException(error);
                finished.TrySetException(error);
                clock.Stop();
                Failed?.Invoke(error.Message);
            };
            recorder.OnStatusChanged += (_, e) =>
            {
                SessionLog.Write($"Native status: {e.Status}");
                if (e.Status == RecorderStatus.Recording && State == SessionState.Starting)
                {
                    clock.Start();
                    SetState(SessionState.Recording);
                    started.TrySetResult();
                }
            };
            if (config.HighlightMouse)
            {
                cursorHighlight = new CursorHighlight(config.Monitor, config.Area);
                cursorHighlight.Show();
            }
            recorder.Record(capturePath);
            SessionLog.Write("Record returned");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (Exception error)
        {
            CloseHighlight();
            recorder?.Stop();
            recorder?.Dispose();
            recorder = null;
            if (capturePath != null && File.Exists(capturePath) && new FileInfo(capturePath).Length == 0) File.Delete(capturePath);
            SetState(SessionState.Idle);
            if (error is TimeoutException) throw new IOException("A captura não iniciou em 60 segundos. Verifique o monitor e tente H.264. Consulte o diagnóstico local se o problema persistir.", error);
            throw;
        }
    }

    private void CloseHighlight()
    {
        cursorHighlight?.Close(); cursorHighlight = null;
    }

    public void Pause()
    {
        if (State != SessionState.Recording) return;
        recorder!.Pause(); cursorHighlight?.SetPaused(true); clock.Stop(); SetState(SessionState.Paused);
    }
    public void Resume()
    {
        if (State != SessionState.Paused) return;
        cursorHighlight?.SetPaused(false); recorder!.Resume(); clock.Start(); SetState(SessionState.Recording);
    }
    public async Task<string> StopAsync()
    {
        if (recorder is null || finished is null) throw new InvalidOperationException("Nenhuma gravação ativa.");
        SetState(SessionState.Stopping); clock.Stop();
        try
        {
            CloseHighlight();
            recorder.Stop();
            await finished.Task;
            recorder.Dispose(); recorder = null;
            if (settings!.Format == "MKV")
            {
                var partial = destination + ".partial.mkv";
                try
                {
                    await MediaTools.RunAsync("ffmpeg", "-hide_banner", "-nostdin", "-n", "-i", capturePath!, "-map", "0", "-c", "copy", partial);
                    File.Move(partial, destination!);
                    File.Delete(capturePath!);
                }
                catch (Exception e) { throw new IOException($"Não foi possível gerar o MKV. O MP4 foi preservado em {capturePath}. {e.Message}", e); }
            }
            return destination!;
        }
        finally { CloseHighlight(); recorder?.Dispose(); recorder = null; SetState(SessionState.Idle); }
    }
}
