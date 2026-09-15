using System.Net.Http;
using KotobaSUB.Core.Japanese;
using System.Text.Json;
using KotobaSUB.Core.Adapted.FlyingLyrics;

namespace KotobaSUB.Core.Lyrics;

public sealed record LyricsCandidate(string Source, string Id, string Title, string Artist, TimeSpan Duration, string? Original = null, string? Translation = null, string? Romanized = null)
{
    public string Key => Source + ":" + Id;
}
public interface ILyricsProvider
{
    Task<LyricsCandidate?> ResolveAsync(MediaTrack track, IReadOnlySet<string> rejected, bool bypassCache, CancellationToken token);
}
public interface ILyricsSource
{
    string Name { get; }
    Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string query, CancellationToken token);
    Task<LyricsCandidate> FetchAsync(LyricsCandidate candidate, CancellationToken token);
}

public sealed record LyricsCandidateMatch(LyricsCandidate Candidate, MatchDecision Match);
public interface ICachedLyricsProvider
{
    LyricsCandidate? LoadCached(MediaTrack track);
}
public interface IMultiLyricsProvider
{
    Task<IReadOnlyList<LyricsCandidateMatch>> ResolveCandidatesAsync(MediaTrack track, IReadOnlySet<string> rejected, bool bypassCache, CancellationToken token);
}

public sealed class LyricsResolver(IReadOnlyList<ILyricsSource> sources, LyricsCache cache, Action<string> log, Func<string, string?>? readingAlias = null) : ILyricsProvider, IMultiLyricsProvider, ICachedLyricsProvider
{
    public LyricsCandidate? LoadCached(MediaTrack track) => cache.Load(track, readingAlias);

    public const int MaximumRetainedCandidates = 6;

    public Task<LyricsCandidate?> ResolveAsync(MediaTrack track, IReadOnlySet<string> rejected, bool bypassCache, CancellationToken token) =>
        Task.Run(async () =>
        {
            if (!bypassCache && cache.Load(track, readingAlias) is { } cached && !rejected.Contains(cached.Key))
            {
                log($"lyrics cache hit {cached.Key}");
                return cached;
            }
            var shortlist = await ResolveCandidatesAsync(track, rejected, bypassCache, token).ConfigureAwait(false);
            var selected = shortlist.FirstOrDefault()?.Candidate;
            if (selected is not null) { cache.Save(track, selected); log($"selected {selected.Key}"); }
            return selected;
        }, token);
    public Task<IReadOnlyList<LyricsCandidateMatch>> ResolveCandidatesAsync(MediaTrack track, IReadOnlySet<string> rejected, bool bypassCache, CancellationToken token) =>
        Task.Run(() => ResolveCandidatesCoreAsync(track, rejected, bypassCache, token), token);

    private async Task<IReadOnlyList<LyricsCandidateMatch>> ResolveCandidatesCoreAsync(MediaTrack track, IReadOnlySet<string> rejected, bool bypassCache, CancellationToken token)
    {
        LyricsCandidate? cached = !bypassCache && cache.Load(track, readingAlias) is { } hit && !rejected.Contains(hit.Key) ? hit : null;
        if (cached is not null) log($"lyrics cache hit {cached.Key}; searching alternates");

        var titles = MetadataMatching.TitleAliases(track.Title);
        string artist = MetadataMatching.PrimaryArtist(track.Artist);
        string localArtist = readingAlias?.Invoke(artist) ?? KanaRomanizer.Convert(artist);
        var queries = titles.SelectMany(t => new[]
        {
            artist + " " + t,
            localArtist + " " + (readingAlias?.Invoke(t) ?? KanaRomanizer.Convert(t))
        }).Append(titles[0]).Distinct().Take(3).ToArray();
        var discovered = new Dictionary<string, (LyricsCandidate Candidate, MatchDecision Match, ILyricsSource Source)>();

        foreach (var source in sources)
        {
            foreach (string query in queries)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    foreach (var candidate in await source.SearchAsync(query, token).ConfigureAwait(false))
                    {
                        if (rejected.Contains(candidate.Key)) continue;
                        var match = MetadataMatching.Evaluate(track, candidate.Title, candidate.Artist, candidate.Duration, readingAlias);
                        log($"candidate {candidate.Key}: {match.Reason}; score={match.Score:F1}");
                        if (match.Accepted && (!discovered.TryGetValue(candidate.Key, out var prior) || match.Score > prior.Match.Score)) discovered[candidate.Key] = (candidate, match, source);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException && !token.IsCancellationRequested)
                { log($"{source.Name} search failed: {ex.Message}"); }
            }
        }

        var retained = new List<LyricsCandidateMatch>(MaximumRetainedCandidates);
        if (cached is not null) retained.Add(new(cached, MetadataMatching.Evaluate(track, cached.Title, cached.Artist, cached.Duration, readingAlias)));
        foreach (var item in discovered.Values.Where(x => cached is null || x.Candidate.Key != cached.Key).OrderByDescending(x => x.Match.Score).Take(Math.Max(0, MaximumRetainedCandidates - retained.Count)))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var resolved = await item.Source.FetchAsync(item.Candidate, token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(resolved.Original) || !new LyricTimeline(resolved.Original, resolved.Translation, track.Duration).HasText) continue;
                retained.Add(new(resolved, item.Match));
                log($"retained {resolved.Key}; metadata={item.Match.Score:F1}");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException && !token.IsCancellationRequested)
            { log($"{item.Source.Name} lyrics failed: {ex.Message}"); }
        }
        return retained;
    }
}
public sealed class LyricsHttpClient(HttpClient client)
{
    public async Task<JsonDocument> GetAsync(string uri, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("KotobaSUB/0.2");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + count > 2_000_000) throw new InvalidDataException("Provider response exceeds 2 MB");
            bytes.Write(buffer, 0, count);
        }
        return JsonDocument.Parse(bytes.ToArray());
    }
    internal static string Text(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    internal static double Number(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) && double.IsFinite(n) ? n : 0;
}
public sealed class LrcLibSource(LyricsHttpClient client) : ILyricsSource
{
    public string Name => "LRCLIB";
    public async Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string query, CancellationToken token)
    {
        using var data = await client.GetAsync("https://lrclib.net/api/search?q=" + Uri.EscapeDataString(query), token).ConfigureAwait(false);
        if (data.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("LRCLIB response is not an array");
        return data.RootElement.EnumerateArray().Take(30).Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("id", out _)).Where(x => !(x.TryGetProperty("instrumental", out var v) && v.ValueKind == JsonValueKind.True)).Select(x =>
            new LyricsCandidate(Name, x.GetProperty("id").ToString(), LyricsHttpClient.Text(x, "trackName"), LyricsHttpClient.Text(x, "artistName"),
                TimeSpan.FromSeconds(Math.Clamp(LyricsHttpClient.Number(x, "duration"), 0, 86400)), LyricsHttpClient.Text(x, "syncedLyrics"))).ToArray();
    }
    public Task<LyricsCandidate> FetchAsync(LyricsCandidate candidate, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(candidate); }
}
public sealed class NetEaseSource(LyricsHttpClient client) : ILyricsSource
{
    public string Name => "NetEase";
    public async Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string query, CancellationToken token)
    {
        using var data = await client.GetAsync("https://music.163.com/api/cloudsearch/pc?type=1&limit=30&s=" + Uri.EscapeDataString(query), token).ConfigureAwait(false);
        if (data.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("NetEase response is not an object");
        if (!data.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("songs", out var songs)) return [];
        if (songs.ValueKind != JsonValueKind.Array) throw new InvalidDataException("NetEase songs are not an array");
        return songs.EnumerateArray().Take(30).Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("id", out _) && ((x.TryGetProperty("ar", out var a) && a.ValueKind == JsonValueKind.Array) || (x.TryGetProperty("artists", out a) && a.ValueKind == JsonValueKind.Array))).Select(x => new LyricsCandidate(Name, x.GetProperty("id").ToString(), LyricsHttpClient.Text(x, "name"),
            string.Join(", ", (x.TryGetProperty("ar", out var artists) && artists.ValueKind == JsonValueKind.Array ? artists : x.GetProperty("artists")).EnumerateArray().Select(a => LyricsHttpClient.Text(a, "name"))),
            TimeSpan.FromMilliseconds(Math.Clamp(LyricsHttpClient.Number(x, "dt") is var dt && dt > 0 ? dt : LyricsHttpClient.Number(x, "duration"), 0, 86_400_000)))).ToArray();
    }
    public async Task<LyricsCandidate> FetchAsync(LyricsCandidate candidate, CancellationToken token)
    {
        using var data = await client.GetAsync("https://music.163.com/api/song/lyric?lv=1&tv=-1&rv=-1&id=" + Uri.EscapeDataString(candidate.Id), token).ConfigureAwait(false);
        string Field(string name) => data.RootElement.ValueKind == JsonValueKind.Object && data.RootElement.TryGetProperty(name, out var value) ? LyricsHttpClient.Text(value, "lyric") : "";
        return candidate with { Original = Field("lrc"), Translation = Field("tlyric"), Romanized = Field("romalrc") };
    }
}
