using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KotobaSUB.Core.Lyrics;
using KotobaSUB.Windows.Media;

namespace KotobaSUB.Windows;

internal static class MediaSmoke
{
    public static int Run(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        string directory = Path.GetFullPath(args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? "artifacts/media-smoke");
        Directory.CreateDirectory(directory);
        var results = new System.Collections.Concurrent.ConcurrentQueue<string>(); int exit = 1;
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(55));
                using var metadata = new SmtcMetadataProvider(app.Dispatcher, results.Enqueue);
                metadata.Changed += snapshot => results.Enqueue(snapshot is null ? "SMTC no active metadata" : $"SMTC received {snapshot.Track.Title}; playing={snapshot.Playing}; position={snapshot.Position.TotalSeconds:F1}");
                await metadata.StartAsync(lifetime.Token); results.Enqueue("PASS SMTC manager initialized");
                if (args.Contains("--session-fixture"))
                {
                    MediaSnapshot? latest = null;
                    metadata.Changed += s => { if (s?.Track.Title == SmtcFixture.Title) latest = s; };
                    using var fixture = new SmtcFixture(directory);
                    async System.Threading.Tasks.Task WaitFor(Func<bool> condition, string name)
                    {
                        using var limit = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); limit.CancelAfter(8000);
                        while (!condition()) await Task.Delay(50, limit.Token);
                        results.Enqueue("PASS " + name);
                    }
                    await fixture.StartAsync(lifetime.Token);
                    await WaitFor(() => latest is { Playing: true }, "SMTC generated media selected");
                    fixture.SetState(TimeSpan.FromSeconds(6), false);
                    await WaitFor(() => latest is { Playing: false } && Math.Abs(latest.Position.TotalSeconds - 6) < .5, "SMTC pause and seek events");
                    fixture.SetState(TimeSpan.FromSeconds(12), true);
                    await WaitFor(() => latest is { Playing: true } && Math.Abs(latest.Position.TotalSeconds - 12) < 1, "SMTC resume and seek events");
                }
                if (args.Contains("--network"))
                {
                    using var http = new HttpClient(); var client = new LyricsHttpClient(http);
                    var resolver = new LyricsResolver([new LrcLibSource(client), new NetEaseSource(client)], new LyricsCache(Path.Combine(directory, "cache"), results.Enqueue), results.Enqueue);
                    var track = new MediaTrack("smoke", "アイドル", "YOASOBI", "", TimeSpan.FromSeconds(213));
                    var candidate = await resolver.ResolveAsync(track, new HashSet<string>(), true, lifetime.Token);
                    if (candidate is null) throw new InvalidOperationException("Live providers returned no accepted timed lyrics");
                    results.Enqueue($"PASS live timed lyrics {candidate.Key}");
                    var cached = await resolver.ResolveAsync(track, new HashSet<string>(), false, lifetime.Token);
                    if (cached != candidate) throw new InvalidOperationException("Live cache roundtrip mismatch");
                    results.Enqueue("PASS live cache reuse");
                    if (args.Contains("--netease"))
                    {
                        var net = new NetEaseSource(client);
                        var found = await net.SearchAsync("YOASOBI アイドル", lifetime.Token);
                        results.Enqueue($"NetEase live candidates: {found.Count}");
                        var accepted = found.FirstOrDefault(c => KotobaSUB.Core.Adapted.FlyingLyrics.MetadataMatching.Evaluate(track, c.Title, c.Artist, c.Duration).Accepted);
                        if (accepted is null) throw new InvalidOperationException("NetEase has no accepted candidate");
                        var raw = await net.FetchAsync(accepted, lifetime.Token);
                        if (string.IsNullOrWhiteSpace(raw.Original) || !new LyricTimeline(raw.Original, raw.Translation, track.Duration).HasText) throw new InvalidOperationException("NetEase candidate has no synchronized text");
                        results.Enqueue($"PASS NetEase live lyric/tlyric/romalrc fetch {raw.Key}");
                    }
                }
                await Task.Delay(1000, lifetime.Token); exit = 0;
            }
            catch (Exception ex) { results.Enqueue("FAIL " + ex); }
            finally { File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); app.Shutdown(); }
        }));
        app.Run(); return exit;
    }
}
