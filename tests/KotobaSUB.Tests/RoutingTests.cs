using System.Diagnostics;
using KotobaSUB.Core;
using KotobaSUB.Core.Audio;

internal static class RoutingTests
{
    private static void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
    private static long Tick(double seconds) => (long)(seconds * Stopwatch.Frequency);
    private static SubtitleLine Line(string text) => new(TimeSpan.Zero, TimeSpan.FromSeconds(1), text, []);

    public static void Run(Action<string, Action> test)
    {
        test("routing delays ASR fallback and expires stale transcript", () =>
        {
            var router = new SubtitleSourceRouter();
            var first = router.Evaluate(false, false, new(null), AudioSourceHealth.Stopped, Tick(10));
            Check(first.Source == SubtitleSourceKind.None && !first.ShouldRunAsr && first.NextEvaluation == TimeSpan.FromMilliseconds(750));
            var ready = router.Evaluate(false, false, new(null), AudioSourceHealth.Stopped, Tick(10.75));
            Check(ready.ShouldRunAsr);
            router.AcceptTranscript(new("今日は", TimeSpan.Zero, TimeSpan.FromSeconds(1), .9f, .1f), Tick(11));
            Check(router.Evaluate(false, false, new(null), AudioSourceHealth.Running, Tick(11)).Frame.Current?.OriginalText == "今日は");
            router.AcceptTranscript(new("天気です", TimeSpan.Zero, TimeSpan.FromSeconds(2), .9f, .1f, true), Tick(11.2));
            Check(router.Evaluate(false, false, new(null), AudioSourceHealth.Running, Tick(11.2)).Frame.Current?.OriginalText == "今日は天気です");
            router.AcceptTranscript(new("明日", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), .9f, .1f, true), Tick(12));
            Check(router.Evaluate(false, false, new(null), AudioSourceHealth.Running, Tick(12)).Frame.Current?.OriginalText == "明日");
            Check(router.Evaluate(false, false, new(null), AudioSourceHealth.Running, Tick(16.01)).Frame.Current is null);
        });
        test("structured timeline owns blank cues and stops ASR", () =>
        {
            var router = new SubtitleSourceRouter(TimeSpan.Zero);
            router.AcceptTranscript(new("音声", TimeSpan.Zero, TimeSpan.FromSeconds(1), .9f, .1f, true), Tick(1));
            var blankLyrics = router.Evaluate(false, true, new(null, Line("前"), Line("次")), AudioSourceHealth.Running, Tick(1));
            Check(blankLyrics.Source == SubtitleSourceKind.StructuredLyrics && !blankLyrics.ShouldRunAsr && blankLyrics.Frame.Current is null);
            var after = router.Evaluate(false, false, new(null), AudioSourceHealth.Stopped, Tick(1.1));
            Check(after.ShouldRunAsr && after.Frame.Current is null);
        });
        test("internet lyrics only disables fallback ASR while keeping structured lyrics", () =>
        {
            var router = new SubtitleSourceRouter(TimeSpan.Zero);
            var none = router.Evaluate(false, false, new(null), AudioSourceHealth.Stopped, Tick(1), asrEnabled: false);
            Check(none.Source == SubtitleSourceKind.None && !none.ShouldRunAsr && none.NextEvaluation is null);
            router.AcceptTranscript(new("無視", TimeSpan.Zero, TimeSpan.FromSeconds(1), .9f, .1f), Tick(1.1), asrEnabled: false);
            Check(router.Evaluate(false, false, new(null), AudioSourceHealth.Stopped, Tick(1.2), asrEnabled: false).Frame.Current is null);
            var structured = router.Evaluate(false, true, new(Line("歌詞")), AudioSourceHealth.Stopped, Tick(2), asrEnabled: false);
            Check(structured.Source == SubtitleSourceKind.StructuredLyrics && structured.Frame.Current?.OriginalText == "歌詞");
        });
        test("routing clears ASR on faults and suspension resets hysteresis", () =>
        {
            var router = new SubtitleSourceRouter();
            router.Evaluate(false, false, new(null), AudioSourceHealth.Stopped, Tick(1));
            router.AcceptTranscript(new("音声", TimeSpan.Zero, TimeSpan.FromSeconds(1), .9f, .1f, true), Tick(2));
            var fault = router.Evaluate(false, false, new(null), AudioSourceHealth.Faulted, Tick(2));
            Check(!fault.ShouldRunAsr && fault.Frame.Current is null);
            router.Evaluate(true, false, new(null), AudioSourceHealth.Stopped, Tick(3));
            var resumed = router.Evaluate(false, false, new(null), AudioSourceHealth.Stopped, Tick(10));
            Check(!resumed.ShouldRunAsr && resumed.NextEvaluation == TimeSpan.FromMilliseconds(750));
        });
    }
}