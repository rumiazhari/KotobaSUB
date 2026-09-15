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

public sealed class LyricsResolver(IReadOnlyList<ILyricsSource> sources, LyricsCache cache, Action<string> log, Func<string, string?>? readingAlias = null) : ILyricsProvider
{
    public Task<LyricsCandidate?> ResolveAsync(MediaTrack track, IReadOnlySet<string> rejected, bool bypassCache, CancellationToken token) =>
        Task.Run(() => ResolveCoreAsync(track, rejected, bypassCache, token), token);
    private async Task<LyricsCandidate?> ResolveCoreAsync(MediaTrack track, IReadOnlySet<string> rejected, bool bypassCache, CancellationToken token)
    {
        if (!bypassCache && cache.Load(track, readingAlias) is { } cached && !rejected.Contains(cached.Key))
        { log($"lyrics cache hit {cached.Key}"); return cached; }
        foreach (var source in sources)
        {
            var candidates = new Dictionary<string, LyricsCandidate>();
            // Native title and explicit alternate-script aliases are retained; no online romanization.
            var titles = MetadataMatching.TitleAliases(track.Title);
            string artist = MetadataMatching.PrimaryArtist(track.Artist);
            string localArtist = readingAlias?.Invoke(artist) ?? KanaRomanizer.Convert(artist);
            var queries = titles.SelectMany(t => new[] { artist + " " + t, localArtist + " " + (readingAlias?.Invoke(t) ?? KanaRomanizer.Convert(t)) }).Append(titles[0]).Distinct().Take(3);
            foreach (string query in queries)
            {
                token.ThrowIfCancellationRequested();
                try { foreach (var candidate in await source.SearchAsync(query, token).ConfigureAwait(false)) candidates[candidate.Key] = candidate; }
                catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException && !token.IsCancellationRequested)
                { log($"{source.Name} search failed: {ex.Message}"); }
            }
            var ranked = candidates.Values.Where(c => !rejected.Contains(c.Key)).Select(c => (Candidate: c, Match: MetadataMatching.Evaluate(track, c.Title, c.Artist, c.Duration, readingAlias))).ToArray();
            foreach (var c in ranked) log($"candidate {c.Candidate.Key}: {c.Match.Reason}; score={c.Match.Score:F1}");
            foreach (var match in ranked.Where(c => c.Match.Accepted).OrderByDescending(c => c.Match.Score).Take(4))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var resolved = await source.FetchAsync(match.Candidate, token).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(resolved.Original) || !new LyricTimeline(resolved.Original, resolved.Translation, track.Duration).HasText) continue;
                    token.ThrowIfCancellationRequested();
                    cache.Save(track, resolved);
                    log($"selected {resolved.Key}; score={match.Match.Score:F1}");
                    return resolved;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException && !token.IsCancellationRequested)
                { log($"{source.Name} lyrics failed: {ex.Message}"); }
            }
        }
        return null;
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
