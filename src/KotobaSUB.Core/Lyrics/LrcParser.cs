using System.Globalization;
using System.Text.RegularExpressions;

namespace KotobaSUB.Core.Lyrics;

public sealed record TimedLyric(TimeSpan Start, string Text);
public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,4}):([0-5]\d)(?:\.(\d{1,3}))?\]", RegexOptions.CultureInvariant)]
    private static partial Regex Timestamp();
    [GeneratedRegex(@"\[offset:([+-]?\d{1,8})\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Offset();
    public static IReadOnlyList<TimedLyric> Parse(string raw)
    {
        if (raw.Length > 2_000_000) throw new InvalidDataException("LRC exceeds size limit");
        var offsetMatch = Offset().Matches(raw).LastOrDefault();
        double offsetMs = offsetMatch is null ? 0 : double.Parse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var lines = new List<TimedLyric>();
        foreach (string line in raw.Split('\n'))
        {
            // Only leading timestamps are accepted; bracketed text in a lyric is preserved.
            string remaining = line.Trim();
            var times = new List<TimeSpan>();
            while (Timestamp().Match(remaining) is { Success: true, Index: 0 } match)
            {
                double fraction = match.Groups[3].Success ? double.Parse("0." + match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                double seconds = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) + fraction;
                // LRC positive offset advances the lyric relative to audio.
                times.Add(TimeSpan.FromMilliseconds(Math.Max(0, seconds * 1000 - offsetMs)));
                remaining = remaining[match.Length..].TrimStart();
            }
            foreach (var time in times) lines.Add(new(time, remaining.Trim()));
        }
        return lines.OrderBy(x => x.Start).GroupBy(x => x.Start)
            .Select(g => new TimedLyric(g.Key, string.Join("\n", g.Select(x => x.Text).Where(x => x.Length > 0).Distinct())))
            .ToArray();
    }
}

public sealed class LyricTimeline
{
    private readonly SubtitleLine[] lines;
    public LyricTimeline(string original, string? translation, TimeSpan duration)
    {
        var cues = LrcParser.Parse(original);
        var translations = LrcParser.Parse(translation ?? "");
        lines = cues.Select((cue, i) =>
        {
            TimeSpan end = i + 1 < cues.Count ? cues[i + 1].Start : duration > cue.Start ? duration : cue.Start + TimeSpan.FromSeconds(8);
            if (duration > TimeSpan.Zero) end = end > duration ? duration : end;
            string? text = translations.Where(t => Math.Abs((t.Start - cue.Start).TotalSeconds) <= .25).MinBy(t => Math.Abs((t.Start - cue.Start).TotalSeconds))?.Text;
            return new SubtitleLine(cue.Start, end, cue.Text, [], text);
        }).Where(l => l.End > l.Start).ToArray();
    }
    public bool HasText => lines.Any(l => !string.IsNullOrWhiteSpace(l.OriginalText));
    public SubtitleFrame At(TimeSpan position)
    {
        int i = Array.FindLastIndex(lines, l => l.Start <= position);
        if (i < 0) return new(null, null, lines.FirstOrDefault());
        if (position >= lines[i].End) return new(null);
        // Blank timed cues clear the current line, including long instrumental breaks.
        var current = string.IsNullOrWhiteSpace(lines[i].OriginalText) ? null : lines[i];
        return new(current, i > 0 ? lines[i - 1] : null, i + 1 < lines.Length ? lines[i + 1] : null);
    }
    public TimeSpan? NextBoundary(TimeSpan position) => lines.SelectMany(l => new[] { l.Start, l.End }).Where(t => t > position).Cast<TimeSpan?>().Min();
}
