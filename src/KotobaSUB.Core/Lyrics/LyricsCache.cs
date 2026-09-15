using System.Text.Json;
using KotobaSUB.Core.Adapted.FlyingLyrics;

namespace KotobaSUB.Core.Lyrics;

public sealed class LyricsCache(string directory, Action<string> log)
{
    private sealed record Entry(int Version, DateTimeOffset SavedAt, LyricsCandidate Candidate);
    private string FilePath(MediaTrack track) => Path.Combine(directory, track.Identity + ".json");
    public LyricsCandidate? Load(MediaTrack track)
    {
        string path = FilePath(track);
        if (!File.Exists(path)) return null;
        try
        {
            if (new FileInfo(path).Length > 2_100_000) throw new InvalidDataException("Oversized cache file");
            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path));
            if (entry is null || entry.Version != 1 || DateTimeOffset.UtcNow - entry.SavedAt > TimeSpan.FromDays(30)) return null;
            var c = entry.Candidate;
            if (c is null || string.IsNullOrWhiteSpace(c.Title) || string.IsNullOrWhiteSpace(c.Artist)) return null;
            if (!MetadataMatching.Evaluate(track, c.Title, c.Artist, c.Duration).Accepted || string.IsNullOrWhiteSpace(c.Original)) return null;
            return new LyricTimeline(c.Original, c.Translation, track.Duration).HasText ? c : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { log($"cache read failed: {ex.Message}"); return null; }
    }
    public void Save(MediaTrack track, LyricsCandidate candidate)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string path = FilePath(track), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Entry(1, DateTimeOffset.UtcNow, candidate)));
            File.Move(temp, path, true);
            foreach (var old in new DirectoryInfo(directory).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(128)) old.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log($"cache write failed: {ex.Message}"); }
    }
}

public sealed class SongOffsets
{
    private readonly string path;
    private readonly Action<string> log;
    private Dictionary<string, double> offsets = new();
    public SongOffsets(string path, Action<string> log)
    {
        this.path = path; this.log = log;
        try { if (File.Exists(path)) offsets = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(path)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { log($"offset load failed: {ex.Message}"); }
    }
    public double Get(string identity) => offsets.TryGetValue(identity, out double value) && double.IsFinite(value) ? Math.Clamp(value, -30, 30) : 0;
    public void Set(string identity, double seconds)
    {
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        offsets[identity] = Math.Clamp(seconds, -30, 30);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(offsets)); File.Move(path + ".tmp", path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log($"offset save failed: {ex.Message}"); }
    }
}
