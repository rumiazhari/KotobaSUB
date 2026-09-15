using System.Diagnostics;
using KotobaSUB.Core;
using KotobaSUB.Core.Audio;
using KotobaSUB.Core.Lyrics;

internal static class AudioMediaClockTests
{
    private static void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
    private static MediaTrack Track(string app = "app") => new(app, "song", "artist", "", TimeSpan.FromMinutes(4));
    private static MediaSnapshot Media(MediaTrack track, double position, bool playing = true, double rate = 1, long observed = 1_000) => new(track, TimeSpan.FromSeconds(position), playing, rate, observed);
    private static AudioCaptureObservation Capture(long first, long end, long observed, bool discontinuity = false, long generation = 0) => new(first, end, observed, discontinuity, generation);

    public static void Run(Action<string, Action> test)
    {
        test("capture clock ignores inference completion latency", () =>
        {
            var clock = new AudioMediaClock(); var track = Track(); clock.ObserveMedia(Media(track, 74.5)); clock.ObserveCapture(Capture(0, 16_000, 1_000));
            var captured = clock.MapCaptureTime(TimeSpan.FromSeconds(.5));
            Check(captured is not null && Math.Abs(captured!.Value.TotalSeconds - 75) < .001);
            var latency = clock.EstimateInferenceLatency(TimeSpan.FromSeconds(1), 1_000 + Stopwatch.Frequency * 2); Check(latency is not null && latency.Value.TotalSeconds > .9);
            var delayed = clock.MapCaptureTime(TimeSpan.FromSeconds(.5));
            Check(Math.Abs(delayed!.Value.TotalSeconds - captured!.Value.TotalSeconds) < .001);
        });
        test("capture clock reanchors after seek and pause resume", () =>
        {
            var clock = new AudioMediaClock(); var track = Track(); clock.ObserveMedia(Media(track, 10)); clock.ObserveCapture(Capture(0, 16_000, 1_000));
            clock.ObserveMedia(Media(track, 120, true, 1, 2_000)); clock.ObserveCapture(Capture(32_000, 48_000, 2_000, true));
            Check(Math.Abs(clock.MapCaptureTime(TimeSpan.FromSeconds(2))!.Value.TotalSeconds - 120) < .001);
            clock.ObserveMedia(Media(track, 120, false, 1, 3_000)); clock.ObserveMedia(Media(track, 120, true, 1, 4_000)); clock.ObserveCapture(Capture(48_000, 64_000, 4_000));
            Check(Math.Abs(clock.MapCaptureTime(TimeSpan.FromSeconds(3))!.Value.TotalSeconds - 120) < .001);
        });
        test("capture clock respects playback rate and capture restart", () =>
        {
            var clock = new AudioMediaClock(); var track = Track(); clock.ObserveMedia(Media(track, 20, true, 2, 1_000)); clock.ObserveCapture(Capture(0, 16_000, 1_000));
            Check(Math.Abs(clock.MapCaptureTime(TimeSpan.FromSeconds(1))!.Value.TotalSeconds - 22) < .001);
            clock.ObserveCapture(Capture(0, 16_000, 5_000, true)); Check(Math.Abs(clock.MapCaptureTime(TimeSpan.Zero)!.Value.TotalSeconds - 20) < .001);
        });
        test("absolute capture samples are not double-counted after non-zero reanchor", () =>
        {
            var clock = new AudioMediaClock(); var track = Track();
            clock.ObserveMedia(Media(track, 120, true, 1, 1_000));
            clock.ObserveCapture(Capture(800_000, 816_000, 1_000, false, 7));
            Check(Math.Abs(clock.MapCaptureTime(TimeSpan.FromSeconds(51))!.Value.TotalSeconds - 121) < .001);
            Check(Math.Abs(clock.MapCaptureTime(TimeSpan.FromSeconds(50))!.Value.TotalSeconds - 120) < .001);
        });
        test("capture generation change reanchors repeated zero-based sessions", () =>
        {
            var clock = new AudioMediaClock(); var track = Track();
            clock.ObserveMedia(Media(track, 30, true, 1, 1_000));
            clock.ObserveCapture(Capture(0, 16_000, 1_000, false, 1));
            Check(Math.Abs(clock.MapCaptureTime(TimeSpan.FromSeconds(.5))!.Value.TotalSeconds - 30.5) < .001);
            clock.ObserveMedia(Media(track, 70, true, 1, 2_000));
            clock.ObserveCapture(Capture(0, 16_000, 2_000, false, 2));
            Check(Math.Abs(clock.MapCaptureTime(TimeSpan.FromSeconds(.5))!.Value.TotalSeconds - 70.5) < .001);
        });
        test("probe scheduler closes windows and backs off slow inference", () =>
        {
            var scheduler = new LyricProbeScheduler(); long now = 0;
            Check(scheduler.TryStart(now, LyricConfidenceState.Tentative) && scheduler.IsActive(now));
            Check(scheduler.NextDelay(now)!.Value.TotalSeconds > 3.9 && scheduler.NextDelay(now)!.Value.TotalSeconds < 4.1);
            Check(!scheduler.IsActive((long)(4.01 * Stopwatch.Frequency)));
            scheduler.RecordInference(TimeSpan.FromSeconds(7), LyricConfidenceState.Tentative);
            Check(scheduler.Interval.TotalSeconds > 10);
            scheduler.RecordInference(TimeSpan.Zero, LyricConfidenceState.Verified);
            Check(scheduler.NextDelay((long)(20 * Stopwatch.Frequency)) is null);
        });
        test("capture clock rejects stale track and discontinuous audio", () =>
        {
            var clock = new AudioMediaClock(); var first = Track("first"); var second = Track("second"); clock.ObserveMedia(Media(first, 5)); clock.ObserveCapture(Capture(0, 16_000, 1_000));
            clock.ObserveMedia(Media(second, 40)); Check(!clock.IsValid && clock.MapCaptureTime(TimeSpan.Zero) is null);
            clock.ObserveCapture(Capture(12_000, 28_000, 2_000, true)); Check(Math.Abs(clock.MapCaptureTime(TimeSpan.FromSeconds(.75))!.Value.TotalSeconds - 40) < .001);
        });
    }
}
