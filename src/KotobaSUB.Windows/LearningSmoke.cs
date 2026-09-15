using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using KotobaSUB.Windows.Learning;

namespace KotobaSUB.Windows;

internal static class LearningSmoke
{
    public static int Run()
    {
        string directory = Path.GetFullPath("artifacts/learning-smoke"); Directory.CreateDirectory(directory);
        var results = new List<string>(); int exit = 1;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var overlay = new OverlayWindow(new OverlaySettings { Left = 60, Top = 60, Width = 1000, Height = 300 });
        using var renderer = new LearningRenderer(overlay, Path.Combine(directory, "cache"), results.Add);
        overlay.Show();
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); results.Add("PASS " + name); }
                Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "Data", "jmdict.sqlite")), "full offline dictionary deployed");
                var line = new SubtitleLine(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(7), "私は明日学校に行きます", []);
                var elapsed = Stopwatch.StartNew(); renderer.RenderFrame(new(line)); await renderer.Completion; elapsed.Stop();
                results.Add($"First annotation including lazy initialization: {elapsed.Elapsed.TotalMilliseconds:F1} ms");
                var annotated = overlay.DisplayedFrame.Current!;
                Check(annotated.Tokens.Count == 6 && annotated.Tokens[5].Lemma == "行く" && annotated.Tokens[5].Glosses.Single() == "go", "actual worker annotation reaches overlay");
                overlay.UpdateLayout(); NativeSmoke.Capture(overlay, Path.Combine(directory, "learning.png"));
                File.WriteAllText(Path.Combine(directory, "tokens.json"), JsonSerializer.Serialize(annotated, new JsonSerializerOptions { WriteIndented = true }));
                overlay.Apply(overlay.Snapshot() with { Romaji = true, Furigana = false });
                renderer.RenderFrame(new(line)); await renderer.Completion; overlay.UpdateLayout(); NativeSmoke.Capture(overlay, Path.Combine(directory, "romaji.png"));
                Check(overlay.DisplayedFrame.Current?.Tokens.Count == 6, "settings re-render keeps annotated tokens");
                renderer.RenderFrame(new(new(TimeSpan.Zero, TimeSpan.FromSeconds(5), "食べました", []))); Task old = renderer.Completion;
                renderer.RenderFrame(new(null)); await old;
                Check(overlay.DisplayedFrame.Current is null, "clearing source rejects pending annotation");
                renderer.RenderFrame(new(new(TimeSpan.Zero, TimeSpan.FromSeconds(5), "学校", []))); old = renderer.Completion;
                renderer.RenderFrame(new(new(TimeSpan.Zero, TimeSpan.FromSeconds(5), "行きます", []))); await renderer.Completion; await old;
                Check(overlay.DisplayedFrame.Current?.OriginalText == "行きます", "new source wins over stale annotation");
                overlay.Apply(overlay.Snapshot() with { Romaji = false, Furigana = true });
                renderer.RenderFrame(new(new(TimeSpan.Zero, TimeSpan.FromSeconds(5), "こんにちは", []))); await renderer.Completion;
                overlay.UpdateLayout(); NativeSmoke.Capture(overlay, Path.Combine(directory, "kana-only.png"));
                renderer.RenderFrame(new(line with { Start = TimeSpan.FromSeconds(20) })); await renderer.Completion;
                Check(overlay.DisplayedFrame.Current?.Start == TimeSpan.FromSeconds(20), "cached tokens retain new source timing");
                exit = 0;
            }
            catch (Exception ex) { results.Add("FAIL " + ex); }
            finally { File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); renderer.Dispose(); overlay.Close(); app.Shutdown(); }
        }));
        app.Run(); return exit;
    }
}
