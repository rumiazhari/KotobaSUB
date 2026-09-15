using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KotobaSUB.Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace KotobaSUB.Windows;

internal static class RoutingSmoke
{
    public static int Run(string[] args)
    {
        string Value(string key, string fallback) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        string fixture = Path.GetFullPath(Value("--fixture", ".data/fixtures/konnichiwa.mp3"));
        string output = Path.GetFullPath(Value("--output", "artifacts/routing-smoke"));
        if (!File.Exists(fixture)) { Console.Error.WriteLine($"Fixture missing: {fixture}"); return 1; }
        Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var overlay = new OverlayWindow(new() { Furigana = true, Gloss = true });
        var statuses = new List<string>();
        var log = new LocalLog(Path.Combine(output, "logs"));
        using var player = new global::Windows.Media.Playback.MediaPlayer { Volume = .25, AutoPlay = false, IsLoopingEnabled = false, Source = MediaSource.CreateFromUri(new Uri(fixture)) };
        using var controller = new PlaybackController(overlay, output, value => { statuses.Add(value); Console.WriteLine("Source: " + value); }, log.Write);
        var elapsed = Stopwatch.StartNew();
        int cycle = 0; bool clearedAfterSuspend = false; string? firstText = null; double firstElapsedMilliseconds = 0;
        var playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        playbackTimer.Tick += (_, _) => { playbackTimer.Stop(); player.PlaybackSession.Position = TimeSpan.Zero; player.Play(); };
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        poll.Tick += (_, _) =>
        {
            string? text = overlay.DisplayedFrame.Current?.OriginalText;
            bool japanese = text?.Contains("こんにちは") == true || text?.Contains("今日は") == true;
            if (cycle == 0 && japanese)
            {
                firstText = text; firstElapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds; player.Pause();
                controller.SetPreview(true); controller.SetPreview(false); cycle = 1;
                clearedAfterSuspend = overlay.DisplayedFrame.Current is null;
                playbackTimer.Start();
            }
            else if (cycle == 1 && japanese && clearedAfterSuspend)
            {
                poll.Stop(); player.Pause(); overlay.UpdateLayout(); SaveCapture(overlay, Path.Combine(output, "routed-asr.png"));
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { firstText, firstElapsedMilliseconds, secondText = text, secondElapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds, clearedAfterSuspend, statuses }, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) }));
                Console.WriteLine($"PASS routed ASR, suspended, and routed fresh ASR: {firstText} / {text}; {elapsed.Elapsed.TotalMilliseconds:F0}ms"); app.Shutdown(0);
            }
            else if (elapsed.Elapsed > TimeSpan.FromSeconds(25))
            {
                poll.Stop(); player.Pause(); File.WriteAllText(Path.Combine(output, "failure.txt"), string.Join(Environment.NewLine, statuses));
                Console.Error.WriteLine("Routing smoke timed out before two clean ASR cycles"); app.Shutdown(1);
            }
        };
        app.Exit += (_, _) => { playbackTimer.Stop(); poll.Stop(); overlay.Close(); };
        overlay.Show(); controller.SetPreview(false); playbackTimer.Start(); poll.Start();
        return app.Run();
    }

    private static void SaveCapture(OverlayWindow overlay, string path)
    {
        int width = Math.Max(1, (int)Math.Ceiling(overlay.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(overlay.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(overlay);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}