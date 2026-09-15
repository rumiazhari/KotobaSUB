using System.Diagnostics;
using KotobaSUB.Core;
using KotobaSUB.Core.Audio;
using KotobaSUB.Core.Adapted.FlyingLyrics;
using KotobaSUB.Core.Lyrics;

internal static class LyricVerificationTests
{
    private static void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
    private static LyricCandidateEvidence Evidence(string key, double provider, double audio, double similarity = .9, float confidence = .9f, double progress = .5) =>
        new(key, 0, TimeSpan.FromSeconds(provider), TimeSpan.FromSeconds(audio), similarity, confidence, progress);

    public static void Run(Action<string, Action> test)
    {
        test("Japanese lyric comparison accepts partial singing text", () =>
        {
            var match = LyricComparison.Compare("君の知らない...", "君の知らない物語");
            var wrong = LyricComparison.Compare("君の知らない...", "明日の空を見上げて");
            Check(match.Score >= .72 && match.OrderedCoverage >= .9);
            Check(match.Score > wrong.Score + .4);
            Check(LyricComparison.Normalize("カタカナ、かな！") == "かたかなかな");
        });
        test("different kanji homophones remain distinct", () =>
        {
            var match = LyricComparison.Compare("橋", "箸");
            Check(match.Score == 0);
        });
        test("verifier chooses the nearby matching lyric line", () =>
        {
            var correct = new LyricTimeline("[00:10]君の知らない物語", null, TimeSpan.FromSeconds(20));
            var wrong = new LyricTimeline("[00:10]明日の空を見上げて", null, TimeSpan.FromSeconds(20));
            var segment = new TranscriptionSegment("君の知らない", TimeSpan.Zero, TimeSpan.FromSeconds(1), .9f, .1f, true);
            var verifier = new LyricVerifier();
            Check(verifier.Probe("good", correct, segment, TimeSpan.FromSeconds(10), .5).Similarity > verifier.Probe("wrong", wrong, segment, TimeSpan.FromSeconds(10), .5).Similarity);
        });
        test("one bad ASR fragment does not reject a candidate", () =>
        {
            var assessment = LyricConfidenceEvaluator.Start("candidate", 96);
            assessment = LyricConfidenceEvaluator.Apply(assessment, Evidence("candidate", 20, 20, .1, .9f, .2));
            Check(assessment.State != LyricConfidenceState.Rejected && assessment.ContradictoryAnchorCount == 1);
        });
        test("repeated separated contradiction rejects a candidate", () =>
        {
            var assessment = LyricConfidenceEvaluator.Start("candidate", 96);
            assessment = LyricConfidenceEvaluator.Apply(assessment, Evidence("candidate", 20, 20, .1, .9f, .2));
            assessment = LyricConfidenceEvaluator.Apply(assessment, Evidence("candidate", 80, 80, .1, .9f, .8));
            Check(assessment.State == LyricConfidenceState.Rejected && assessment.ContradictoryAnchorCount == 2);
        });
        test("three separated positive anchors verify a candidate", () =>
        {
            var assessment = LyricConfidenceEvaluator.Start("candidate", 96);
            assessment = LyricConfidenceEvaluator.Apply(assessment, Evidence("candidate", 20, 20.7, .9, .9f, .2));
            assessment = LyricConfidenceEvaluator.Apply(assessment, Evidence("candidate", 90, 90.68, .88, .85f, .5));
            assessment = LyricConfidenceEvaluator.Apply(assessment, Evidence("candidate", 160, 160.66, .92, .9f, .8));
            Check(assessment.State == LyricConfidenceState.Verified && assessment.PositiveAnchorCount == 3);
        });
        test("robust alignment ignores a timing outlier", () =>
        {
            var anchors = new[]
            {
                Evidence("candidate", 30, 30.66, .9, .9f), Evidence("candidate", 60, 60.68, .9, .9f),
                Evidence("candidate", 90, 90.65, .9, .9f), Evidence("candidate", 120, 117.6, .65, .9f)
            };
            var model = LyricAlignmentEstimator.Estimate(anchors);
            Check(Math.Abs(model.OffsetSeconds - .66) < .03 && model.AnchorCount == 3);
        });
        test("automatic alignment sign maps provider cue to observed audio", () =>
        {
            var model = new LyricAlignmentModel(1, .68);
            Check(Math.Abs(model.CorrectedCueTime(TimeSpan.FromSeconds(74.5)).TotalSeconds - 75.18) < .001);
            Check(Math.Abs(model.ProviderTimeAtAudio(TimeSpan.FromSeconds(75.18)).TotalSeconds - 74.5) < .001);
        });
        test("small drift is estimated and absurd drift is rejected", () =>
        {
            var anchors = new[] { 0d, 30d, 60d, 90d }.Select(t => Evidence("candidate", t, t * 1.002 + .5, .9, .9f)).ToArray();
            var model = LyricAlignmentEstimator.Estimate(anchors);
            Check(Math.Abs(model.Scale - 1.002) < .0005 && Math.Abs(model.OffsetSeconds - .5) < .03);
            var absurd = LyricAlignmentEstimator.Estimate(new[] { Evidence("candidate", 0, 0), Evidence("candidate", 30, 60), Evidence("candidate", 60, 120) });
            Check(absurd.Scale == 1);
        });
        test("corrected lyric cue times remain monotonic", () =>
        {
            var corrected = LyricAlignmentEstimator.CorrectMonotonic([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)], new LyricAlignmentModel(1, .6));
            Check(corrected.Zip(corrected.Skip(1)).All(p => p.Second >= p.First));
        });
    test("learning store persists verified and rejected profiles safely", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "KotobaSUB-learning-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "learning.json");
                var anchors = new[] { Evidence("source:1", 10, 10.6), Evidence("source:1", 50, 50.62), Evidence("source:1", 90, 90.61) };
                var verified = new LyricCandidateAssessment("source:1", 97, LyricConfidenceState.Verified, .91, 3, 0);
                var store = new LyricVerificationStore(path, _ => { }); store.Save("track", verified, new LyricAlignmentModel(1.001, .61, .02, 3), anchors);
                store.MarkRejected("track", "source:2", 95);
                var restarted = new LyricVerificationStore(path, _ => { });
                Check(restarted.Get("track", "source:1")?.State == LyricConfidenceState.Verified);
                Check(Math.Abs(restarted.Get("track", "source:1")!.Alignment.OffsetSeconds - .61) < .001);
                Check(restarted.Get("track", "source:2")?.State == LyricConfidenceState.Rejected);
                string raw = File.ReadAllText(path); Check(!raw.Contains("Samples", StringComparison.OrdinalIgnoreCase));
                File.WriteAllText(path, "{bad"); var reports = new List<string>(); Check(new LyricVerificationStore(path, reports.Add).ForTrack("track").Count == 0 && reports.Count == 1);
            }
            finally { Directory.Delete(directory, true); }
        });
        test("session loads learned candidate and alignment after restart", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "KotobaSUB-session-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "learning.json");
                var good = new LyricsCandidate("LRCLIB", "good", "アイドル", "YOASOBI", TimeSpan.FromSeconds(100), "[00:10]君の知らない\n[00:50]物語を歌う\n[01:30]あの日僕は");
                var bad = good with { Id = "bad", Original = "[00:10]明日の空\n[00:50]別の歌\n[01:30]遠い街" };
                var provider = new MultiProvider([new(good, new(true, 96, "good")), new(bad, new(true, 95, "bad"))]);
                var track = new MediaTrack("track", "アイドル", "YOASOBI", "", TimeSpan.FromSeconds(100));
                var store = new LyricVerificationStore(path, _ => { });
                using (var session = new LyricsSession(provider, _ => { }, store))
                {
                    session.Update(new(track, TimeSpan.Zero, true, 1, Stopwatch.GetTimestamp())); session.Pending.GetAwaiter().GetResult();
                    foreach (var point in new[] { (10, "君の知らない", .1), (50, "物語を歌う", .5), (90, "あの日僕は", .9) })
                        session.AcceptEvidence(good.Key, new(point.Item2, TimeSpan.Zero, TimeSpan.FromSeconds(1), .9f, .1f, true), TimeSpan.FromSeconds(point.Item1 + .6), point.Item3);
                    Check(session.Assessments[good.Key].State == LyricConfidenceState.Verified, $"{session.Assessments[good.Key].State} positives={session.Assessments[good.Key].PositiveAnchorCount} coverage={session.Assessments[good.Key].CoverageStart}-{session.Assessments[good.Key].CoverageEnd}");
                }
                using var restarted = new LyricsSession(provider, _ => { }, new LyricVerificationStore(path, _ => { }));
                restarted.Update(new(track, TimeSpan.Zero, true, 1, Stopwatch.GetTimestamp())); restarted.Pending.GetAwaiter().GetResult();
                Check(restarted.Selected?.Key == good.Key, $"selected={restarted.Selected?.Key}"); Check(Math.Abs(restarted.AutomaticAlignment.OffsetSeconds - .6) < .05, $"offset={restarted.AutomaticAlignment.OffsetSeconds}");
            }
            finally { Directory.Delete(directory, true); }
        });
        test("session switches away from repeatedly rejected candidate", () =>
        {
            var first = new LyricsCandidate("a", "1", "アイドル", "YOASOBI", TimeSpan.FromSeconds(100), "[00:10]wrong lyrics here");
            var second = first with { Source = "b", Id = "2", Original = "[00:10]君の知らない物語" };
            var provider = new MultiProvider([new(first, new(true, 96, "a")), new(second, new(true, 95, "b"))]);
            using var session = new LyricsSession(provider, _ => { });
            var track = new MediaTrack("switch", "アイドル", "YOASOBI", "", TimeSpan.FromSeconds(100));
            session.Update(new(track, TimeSpan.Zero, true, 1, Stopwatch.GetTimestamp())); session.Pending.GetAwaiter().GetResult();
            session.AcceptEvidence(first.Key, new("君の知らない物語", TimeSpan.Zero, TimeSpan.FromSeconds(1), .95f, .05f, true), TimeSpan.FromSeconds(10), .2);
            session.AcceptEvidence(first.Key, new("君の知らない物語", TimeSpan.Zero, TimeSpan.FromSeconds(1), .95f, .05f, true), TimeSpan.FromSeconds(30), .4);
            Check(session.Selected?.Key == second.Key && session.Assessments[first.Key].State == LyricConfidenceState.Rejected);
        });    }
    private sealed class MultiProvider(IReadOnlyList<LyricsCandidateMatch> matches) : ILyricsProvider, IMultiLyricsProvider
    {
        public Task<LyricsCandidate?> ResolveAsync(MediaTrack track, IReadOnlySet<string> rejected, bool bypassCache, CancellationToken token) => Task.FromResult(matches.FirstOrDefault()?.Candidate);
        public Task<IReadOnlyList<LyricsCandidateMatch>> ResolveCandidatesAsync(MediaTrack track, IReadOnlySet<string> rejected, bool bypassCache, CancellationToken token) => Task.FromResult(matches);
    }
}
