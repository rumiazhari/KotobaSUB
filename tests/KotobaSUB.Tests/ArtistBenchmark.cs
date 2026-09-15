using System.Text.Json;
using KotobaSUB.Core;
using KotobaSUB.Core.Lyrics;

internal static class ArtistBenchmark
{
    // Public metadata only. Durations are intentionally unknown: these checks do
    // not measure audio, timing, or establish that provider recordings match.
    internal static readonly (string VideoId, string Title, string Artist)[] Tracks =
    [
        ("Wx-5gsuzyZs", "可愛くてごめん / HoneyWorks (Kotoha Ver.)", "Kotoha"),
        ("fEv7tsjx7TU", "だきしめるまで。 / MIMI【Covered by Kotoha】", "Kotoha"),
        ("wMaergT5JX4", "涼海ネモ『 カタチのないもの 』 -Official Music Video-", "涼海ネモ / Nemo Channel【ななしいんく】"),
        ("GPKaY-py33A", "涼海ネモ『 楔 』 -Official Music Video-", "涼海ネモ / Nemo Channel【ななしいんく】"),
        ("_CK1kzr3myE", "晩餐歌 - tuki. / covered by 涼海ネモ", "涼海ネモ / Nemo Channel【ななしいんく】"),
        ("0S96hH2bSFc", "涼海ネモ『 カタチのないもの - Another ver. 』", "涼海ネモ / Nemo Channel【ななしいんく】")
    ];
    public static async Task<int> RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        using var http = new HttpClient();
        var client = new LyricsHttpClient(http);
        var reports = new List<object>();
        foreach (var item in Tracks)
        {
            var diagnostics = new List<string>();
            var resolver = new LyricsResolver([new LrcLibSource(client), new NetEaseSource(client)],
                new LyricsCache(Path.Combine(output, "unused-cache"), diagnostics.Add), diagnostics.Add);
            try
            {
                var track = new MediaTrack("public-benchmark", item.Title, item.Artist, "", TimeSpan.Zero);
                var results = await resolver.ResolveCandidatesAsync(track, new HashSet<string>(), true, CancellationToken.None);
                reports.Add(new { url = "https://www.youtube.com/watch?v=" + item.VideoId, item.Title, item.Artist,
                    kind = "metadata/provider availability only; audio not tested",
                    candidates = results.Select(x => new { x.Candidate.Key, x.Candidate.Title, x.Candidate.Artist,
                        seconds = x.Candidate.Duration.TotalSeconds, x.Match.Score }).ToArray(),
                    failures = diagnostics.Where(x => x.Contains("failed")).ToArray() });
                Console.WriteLine($"{item.VideoId}: {results.Count} timed candidates");
            }
            catch (Exception ex) { reports.Add(new { item.VideoId, error = ex.Message }); }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "coverage.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
