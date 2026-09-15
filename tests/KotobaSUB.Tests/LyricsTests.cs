using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using KotobaSUB.Core;
using KotobaSUB.Core.Adapted.FlyingLyrics;
using KotobaSUB.Core.Lyrics;

internal static class LyricsTests
{
    private static void Check(bool condition, string message = "assertion failed") { if (!condition) throw new Exception(message); }
    private static MediaTrack Track(string title = "アイドル", double seconds = 213) => new("test", title, "YOASOBI", "", TimeSpan.FromSeconds(seconds));
    public static void Run(Action<string, Action> test, string directory)
    {
        test("HTTP size limit cancellation and rate-limit fallback", () => RunAsync(async () =>
        {
            using var oversizedHttp = new HttpClient(new FixedHandler(new string('x', 2_000_001), HttpStatusCode.OK));
            bool oversized = false;
            try { using var _ = await new LyricsHttpClient(oversizedHttp).GetAsync("https://lrclib.net/api/search", CancellationToken.None); }
            catch (InvalidDataException) { oversized = true; }
            Check(oversized);
            using var limitedHttp = new HttpClient(new FixedHandler("{}", HttpStatusCode.TooManyRequests));
            var good = new LyricsCandidate("fallback", "1", "アイドル", "YOASOBI", TimeSpan.FromSeconds(213), "[00:01]私");
            var resolver = new LyricsResolver([new LrcLibSource(new LyricsHttpClient(limitedHttp)), new TestSource("fallback", [good])], new LyricsCache(Path.Combine(directory, "limits"), _ => { }), _ => { });
            Check(await resolver.ResolveAsync(Track(), new HashSet<string>(), true, CancellationToken.None) == good);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            bool stopped = false;
            try { await resolver.ResolveAsync(Track(), new HashSet<string>(), true, cancelled.Token); }
            catch (OperationCanceledException) { stopped = true; }
            Check(stopped);
        }));
        test("track removal and disposal reject in-flight results", () => RunAsync(async () =>
        {
            var provider = new DeferredProvider(); using var session = new LyricsSession(provider, _ => { });
            session.Update(new(Track(), TimeSpan.Zero, true, 1, Stopwatch.GetTimestamp())); var pending = session.Pending;
            session.Update(null);
            provider.Completions[0].SetResult(new("test", "1", "アイドル", "YOASOBI", TimeSpan.FromSeconds(213), "[00:00]私")); await pending;
            Check(session.Selected is null && session.Timeline is null);
            session.Update(new(Track(), TimeSpan.Zero, true, 1, Stopwatch.GetTimestamp())); pending = session.Pending;
            session.Dispose(); provider.Completions[1].SetResult(new("test", "2", "アイドル", "YOASOBI", TimeSpan.FromSeconds(213), "[00:00]私")); await pending;
            Check(session.Selected is null);
        }));
        test("local kana romanization handles digraphs gemination and artist aliases", () =>
        {
            Check(KotobaSUB.Core.Japanese.KanaRomanizer.Convert("ガッコウ") == "gakkou");
            Check(KotobaSUB.Core.Japanese.KanaRomanizer.Convert("マッチャ") == "matcha");
            Check(KotobaSUB.Core.Japanese.KanaRomanizer.Convert("シンヨウ") == "shin'you");
            Check(KotobaSUB.Core.Japanese.KanaRomanizer.Convert("コーヒー") == "koohii");
            Check(KotobaSUB.Core.Japanese.KanaRomanizer.Convert("学校") == "学校");
            Check(MetadataMatching.Evaluate(new("test", "ヒカリ", "コトハ", "", TimeSpan.FromSeconds(200)), "Hikari", "Kotoha", TimeSpan.FromSeconds(200)).Accepted);
        });
        test("Japanese title noise and compatibility normalization", () =>
        {
            Check(MetadataMatching.CleanTitle("【推しの子】主題歌「アイドル」 Official Video") == "アイドル");
            Check(MetadataMatching.CleanTitle("「アイドル」 (Official Music Video)") == "アイドル");
            Check(MetadataMatching.Normalize("ＹＯＡＳＯＢＩ・アイドル") == "yoasobiアイドル");
        });
        test("artist collaboration cleanup preserves Japanese name punctuation", () =>
        {
            Check(MetadataMatching.CleanArtist("Aimer feat. milet") == "Aimer");
            Check(MetadataMatching.PrimaryArtist("YOASOBI＆milet") == "YOASOBI");
            Check(MetadataMatching.PrimaryArtist("ナナ・ムスクーリ") == "ナナ・ムスクーリ");
        });
        test("candidate scoring rejects wrong duration artist and recording", () =>
        {
            Check(MetadataMatching.Evaluate(Track(), "アイドル", "YOASOBI", TimeSpan.FromSeconds(214)).Accepted);
            Check(!MetadataMatching.Evaluate(Track(), "アイドル", "YOASOBI", TimeSpan.FromSeconds(90)).Accepted);
            Check(!MetadataMatching.Evaluate(Track(), "アイドル", "Other Artist", TimeSpan.FromSeconds(213)).Accepted);
            Check(!MetadataMatching.Evaluate(Track(), "アイドル (Live)", "YOASOBI", TimeSpan.FromSeconds(213)).Accepted);
            Check(!MetadataMatching.Evaluate(Track(), "夜に駆ける", "YOASOBI", TimeSpan.FromSeconds(213)).Accepted);
            Check(MetadataMatching.Evaluate(Track("夜に駆ける (Yoru ni Kakeru)"), "Yoru ni Kakeru", "YOASOBI", TimeSpan.FromSeconds(213)).Accepted);
        });
        test("LRC multiple tags integer seconds offsets and duplicate ordering", () =>
        {
            var cues = LrcParser.Parse("[offset:+500]\n[00:02.50][00:04]学校\n[00:01]私\n[00:02.500]学校\n[00:05]\n[00:99]bad");
            Check(cues.Count == 4); Check(cues[0] == new TimedLyric(TimeSpan.FromSeconds(.5), "私"));
            Check(cues[1].Text == "学校" && cues[1].Start.TotalSeconds == 2); Check(cues[^1].Text == "");
            Check(LrcParser.Parse("plain untimed lyrics").Count == 0);
            Check(LrcParser.Parse("[00:00.123]私")[0].Start.TotalMilliseconds == 123);
        });
        test("LRC blank cues clear and final line expires; translations remain aligned", () =>
        {
            var timeline = new LyricTimeline("[00:01]私\n[00:03]\n[00:06]学校", "[00:01.1]I\n[00:09]wrong", TimeSpan.FromSeconds(10));
            Check(timeline.At(TimeSpan.Zero).Current is null);
            Check(timeline.At(TimeSpan.FromSeconds(1)).Current?.Translation == "I");
            Check(timeline.At(TimeSpan.FromSeconds(3)).Current is null);
            Check(timeline.At(TimeSpan.FromSeconds(6)).Current?.Translation is null);
            Check(timeline.At(TimeSpan.FromSeconds(10)).Current is null);
            Check(timeline.NextBoundary(TimeSpan.FromSeconds(3)) == TimeSpan.FromSeconds(6));
            Check(timeline.NextBoundary(TimeSpan.FromSeconds(10)) is null);
            var unknownDuration = new LyricTimeline("[00:01]私", null, TimeSpan.Zero);
            Check(unknownDuration.At(TimeSpan.FromSeconds(9)).Current is null);
        });
        test("playback projection preserves pause speed seek and duration", () =>
        {
            var snapshot = new MediaSnapshot(Track(), TimeSpan.FromSeconds(20), true, 2, 100);
            long later = 100 + Stopwatch.Frequency * 3;
            Check(snapshot.PositionAt(later) == TimeSpan.FromSeconds(26));
            Check((snapshot with { Playing = false }).PositionAt(later) == TimeSpan.FromSeconds(20));
            Check((snapshot with { Position = TimeSpan.FromSeconds(212) }).PositionAt(later) == TimeSpan.FromSeconds(213));
            Check((snapshot with { Position = TimeSpan.FromSeconds(1), ObservedAt = later }).PositionAt(later) == TimeSpan.FromSeconds(1));
        });
        test("lyrics cache roundtrip retains translation and romalrc", () =>
        {
            var cache = new LyricsCache(Path.Combine(directory, "lyrics"), _ => { });
            var candidate = new LyricsCandidate("NetEase", "1", "アイドル", "YOASOBI", TimeSpan.FromSeconds(213), "[00:01]私", "[00:01]I", "[00:01]watashi");
            cache.Save(Track(), candidate); Check(cache.Load(Track()) == candidate); Check(cache.Clear() == 1); Check(cache.Load(Track()) is null); cache.Save(Track(), candidate);
            Check(cache.Load(Track(seconds: 90)) is null);
            File.WriteAllText(Path.Combine(directory, "lyrics", Track().Identity + ".json"), "invalid"); Check(cache.Load(Track()) is null);
            var offsets = new SongOffsets(Path.Combine(directory, "offsets.json"), _ => { }); offsets.Set("song", 1.5);
            Check(new SongOffsets(Path.Combine(directory, "offsets.json"), _ => { }).Get("song") == 1.5);
        });
        test("source fallback skips wrong and untimed candidates", () => RunAsync(async () =>
        {
            var wrong = new LyricsCandidate("first", "1", "アイドル", "YOASOBI", TimeSpan.FromSeconds(90), "[00:01]wrong");
            var plain = wrong with { Id = "2", Duration = TimeSpan.FromSeconds(213), Original = "untimed" };
            var good = plain with { Source = "second", Id = "3", Original = "[00:01]私" };
            var first = new TestSource("first", [wrong, plain]); var second = new TestSource("second", [good]);
            var resolver = new LyricsResolver([first, second], new LyricsCache(Path.Combine(directory, "resolver"), _ => { }), _ => { });
            Check(await resolver.ResolveAsync(Track(), new HashSet<string>(), true, CancellationToken.None) == good);
            Check(!first.Fetched.Contains("1"));
            Check(await resolver.ResolveAsync(Track(), new HashSet<string> { good.Key }, true, CancellationToken.None) is null);
        }));
        test("old track response cannot replace current track", () => RunAsync(async () =>
        {
            var provider = new DeferredProvider(); using var session = new LyricsSession(provider, _ => { });
            session.Update(new(Track(), TimeSpan.Zero, true, 1, Stopwatch.GetTimestamp())); var old = session.Pending;
            session.Update(new(Track("学校"), TimeSpan.Zero, true, 1, Stopwatch.GetTimestamp())); var latest = session.Pending;
            provider.Completions[1].SetResult(new("test", "new", "学校", "YOASOBI", TimeSpan.FromSeconds(213), "[00:00]学校")); await latest;
            provider.Completions[0].SetResult(new("test", "old", "アイドル", "YOASOBI", TimeSpan.FromSeconds(213), "[00:00]私")); await old;
            Check(session.Selected?.Id == "new");
            session.Update(null); Check(session.Frame(0).Current is null && session.NextDelay(0) is null);
        }));
        test("pause and seek update displayed line without new fetch", () => RunAsync(async () =>
        {
            var provider = new DeferredProvider(); using var session = new LyricsSession(provider, _ => { });
            var snapshot = new MediaSnapshot(Track(), TimeSpan.Zero, false, 1, Stopwatch.GetTimestamp());
            session.Update(snapshot); provider.Completions[0].SetResult(new("test", "1", "アイドル", "YOASOBI", TimeSpan.FromSeconds(213), "[00:00]私\n[00:05]学校")); await session.Pending;
            session.Update(snapshot with { Position = TimeSpan.FromSeconds(6) });
            Check(session.Frame(0).Current?.OriginalText == "学校"); Check(session.NextDelay(0) is null); Check(provider.Completions.Count == 1);
            session.Update(snapshot with { Position = TimeSpan.FromSeconds(4.5) }); Check(session.Frame(.5).Current?.OriginalText == "学校");
        }));
        test("provider JSON parsing retains original translation and romanization", () => RunAsync(async () =>
        {
            using var http = new HttpClient(new FixtureHandler()); var client = new LyricsHttpClient(http);
            var candidates = await new LrcLibSource(client).SearchAsync("アイドル", CancellationToken.None);
            Check(candidates.Single().Original == "[00:01]私");
            var net = new NetEaseSource(client); var found = await net.SearchAsync("アイドル", CancellationToken.None);
            var resolved = await net.FetchAsync(found.Single(), CancellationToken.None);
            Check(resolved.Translation == "[00:01]I" && resolved.Romanized == "[00:01]watashi");
        }));
    }
    private static void RunAsync(Func<Task> action) => action().GetAwaiter().GetResult();
    private sealed class DeferredProvider : ILyricsProvider
    {
        public List<TaskCompletionSource<LyricsCandidate?>> Completions { get; } = new();
        public Task<LyricsCandidate?> ResolveAsync(MediaTrack track, IReadOnlySet<string> rejected, bool bypassCache, CancellationToken token)
        {
            var completion = new TaskCompletionSource<LyricsCandidate?>(TaskCreationOptions.RunContinuationsAsynchronously); Completions.Add(completion); return completion.Task;
        }
    }
    private sealed class TestSource(string name, IReadOnlyList<LyricsCandidate> candidates) : ILyricsSource
    {
        public string Name => name;
        public List<string> Fetched { get; } = new();
        public Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string query, CancellationToken token) => Task.FromResult(candidates);
        public Task<LyricsCandidate> FetchAsync(LyricsCandidate c, CancellationToken token) { Fetched.Add(c.Id); return Task.FromResult(c); }
    }
    private sealed class FixedHandler(string text, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(text) });
    }
    private sealed class FixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string json = request.RequestUri!.Host == "lrclib.net" ? """[{"id":1,"trackName":"アイドル","artistName":"YOASOBI","duration":213,"syncedLyrics":"[00:01]私"}]""" :
                request.RequestUri.AbsolutePath.Contains("cloudsearch") ? """{"result":{"songs":[{"id":2,"name":"アイドル","ar":[{"name":"YOASOBI"}],"dt":213000}]}}""" :
                """{"lrc":{"lyric":"[00:01]私"},"tlyric":{"lyric":"[00:01]I"},"romalrc":{"lyric":"[00:01]watashi"}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
