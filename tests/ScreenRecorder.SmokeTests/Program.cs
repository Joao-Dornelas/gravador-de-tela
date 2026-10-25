using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenRecorder.Services;

internal static class Program
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern bool PlaySound(string? sound, nint module, uint flags);
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            if (args.Contains("--desktop-controls"))
            {
                app.Shutdown(await DesktopControlTests.RunAsync(args.FirstOrDefault(a => a != "--desktop-controls") ?? Path.GetFullPath("artifacts/desktop-controls")));
                return;
            }
            if (args.Contains("--mouse-only"))
            {
                app.Shutdown(await MouseTests.RunAsync(args.FirstOrDefault(a => a != "--mouse-only") ?? Path.GetFullPath("artifacts/mouse-tests")));
                return;
            }
            var folder = args.FirstOrDefault() ?? Path.GetFullPath("artifacts/smoke-tests");
            Directory.CreateDirectory(folder);
            var report = new List<object>();
            var failures = 0;
            var label = new TextBlock { FontSize = 32, Foreground = Brushes.White, Margin = new Thickness(30) };
            var fixture = new Window { Title = "Screen Recorder · gravação de teste", Width = 640, Height = 400, Content = label, Background = Brushes.DarkSlateBlue, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) => label.Text = $"Teste de captura\n{DateTime.Now:HH:mm:ss.fff}";
            fixture.Show(); timer.Start();
            try
            {
                var monitor = Monitors.GetAll().First();
                var scenarios = new[] {
                    (Name: "static-area", Area: (CaptureArea?)new CaptureArea(500,100,640,360), Format: "MP4", Codec: "H.264", Pc: false, Mic: false),
                    (Name: "full-h264-silent", Area: (CaptureArea?)null, Format: "MP4", Codec: "H.264", Pc: false, Mic: false),
                    (Name: "area-h264-pc-mkv", Area: (CaptureArea?)new CaptureArea(40,40,640,360), Format: "MKV", Codec: "H.264", Pc: true, Mic: false),
                    (Name: "area-h265-silent", Area: (CaptureArea?)new CaptureArea(40,40,640,360), Format: "MP4", Codec: "H.265", Pc: false, Mic: false),
                    (Name: "area-mic", Area: (CaptureArea?)new CaptureArea(40,40,640,360), Format: "MP4", Codec: "H.264", Pc: false, Mic: true),
                    (Name: "area-pc-mic", Area: (CaptureArea?)new CaptureArea(40,40,640,360), Format: "MP4", Codec: "H.264", Pc: true, Mic: true)
                };
                var tone = Path.Combine(folder, "tone.wav"); CreateTone(tone);
                foreach (var scenario in scenarios)
                {
                    if (scenario.Name == "static-area") timer.Stop(); else timer.Start();
                    var session = new RecordingSession();
                    try
                    {
                        if (scenario.Pc) PlaySound(tone, 0, 0x00020009);
                        await session.StartAsync(new(monitor, scenario.Area, folder, scenario.Format, scenario.Codec, scenario.Pc, scenario.Mic, 30));
                        await Task.Delay(2200);
                        session.Pause(); var pausedAt = session.Elapsed;
                        await Task.Delay(1800);
                        if (session.Elapsed - pausedAt > TimeSpan.FromMilliseconds(100)) throw new Exception("O relógio avançou durante a pausa.");
                        session.Resume(); await Task.Delay(2200);
                        var path = await session.StopAsync();
                        PlaySound(null, 0, 0);
                        var json = await MediaTools.RunAsync("ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", path);
                        using var probe = JsonDocument.Parse(json);
                        var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                        var video = streams.Single(s => s.GetProperty("codec_type").GetString() == "video");
                        var audioCount = streams.Count(s => s.GetProperty("codec_type").GetString() == "audio");
                        var expected = scenario.Area ?? monitor.Bounds;
                        if (video.GetProperty("width").GetInt32() != expected.Width / 2 * 2 || video.GetProperty("height").GetInt32() != expected.Height / 2 * 2) throw new Exception("Dimensões incorretas.");
                        var codec = video.GetProperty("codec_name").GetString();
                        if (codec != (scenario.Codec == "H.265" ? "hevc" : "h264")) throw new Exception("Codec incorreto.");
                        if (audioCount != (scenario.Pc || scenario.Mic ? 1 : 0)) throw new Exception("Trilha de áudio incorreta.");
                        var duration = double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                        if (duration < 3.7 || duration > 5.7) throw new Exception($"Duração {duration}: a pausa deveria ser removida.");
                        // Decode the complete file to detect corrupt packets or an invalid finalization.
                        await MediaTools.RunAsync("ffmpeg", "-v", "error", "-xerror", "-i", path, "-f", "null", "-");
                        if (scenario.Pc)
                        {
                            var pcm = Path.ChangeExtension(path, ".pcm");
                            await MediaTools.RunAsync("ffmpeg", "-v", "error", "-y", "-i", path, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", pcm);
                            var bytes = File.ReadAllBytes(pcm);
                            var peak = Enumerable.Range(0, bytes.Length / 4).Select(i => Math.Abs(BitConverter.ToSingle(bytes, i * 4))).Max();
                            if (peak < 0.005) throw new Exception("O áudio do PC está silencioso apesar do tom de teste.");
                        }
                        report.Add(new { scenario.Name, Result = "PASS", Path = path, Duration = duration, Codec = codec, AudioTracks = audioCount });
                        Console.WriteLine($"PASS {scenario.Name}: {duration:F2}s / {codec} / audio={audioCount}");
                    }
                    catch (Exception error)
                    {
                        PlaySound(null, 0, 0);
                        if (session.State is SessionState.Recording or SessionState.Paused) { try { await session.StopAsync(); } catch { } }
                        var unavailable = (scenario.Mic && ScreenRecorderLib.CaptureAudioSource.Default == null) || (scenario.Codec == "H.265" && error.Message.Contains("gravação"));
                        if (!unavailable) failures++;
                        report.Add(new { scenario.Name, Result = unavailable ? "UNAVAILABLE" : "FAIL", Error = error.ToString() });
                        Console.WriteLine($"{(unavailable ? "UNAVAILABLE" : "FAIL")} {scenario.Name}: {error.Message}");
                    }
                }
            }
            catch (Exception error) { failures++; report.Add(new { Result = "FAIL", Error = error.ToString() }); Console.WriteLine(error); }
            finally
            {
                PlaySound(null, 0, 0); timer.Stop(); fixture.Close();
                File.WriteAllText(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(failures == 0 ? 0 : 1);
            }
        };
        return app.Run();
    }
    private static void CreateTone(string path)
    {
        const int sampleRate = 48000, samples = sampleRate;
        using var file = File.Create(path); using var writer = new BinaryWriter(file);
        writer.Write("RIFF"u8.ToArray()); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8.ToArray()); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate); writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8.ToArray()); writer.Write(samples * 2);
        for (int i = 0; i < samples; i++) writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 6000));
    }
}
