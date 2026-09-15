// Adapted concepts and cleanup rules from Flying Lyrics by Crlyzd and contributors.
// Upstream src/background/searchEngine.js at 06b6d01e275f7db45331e6c6eba59ae480d4a7d3.
// GPL-3.0; see LICENSE and THIRD_PARTY_NOTICES.md. Ranking below deliberately uses
// hard identity/duration gates rather than upstream's large synchronized bonus.
using System.Text;
using KotobaSUB.Core.Japanese;
using System.Text.RegularExpressions;

namespace KotobaSUB.Core.Adapted.FlyingLyrics;

public static class MetadataMatching
{
    private const string Noise = @"official(?: music)? (?:video|audio)|lyrics? video|mv|remaster(?:ed)?|remix|live|acoustic|instrumental|cover|tv(?: size)?(?: ver\.?)?|anime ver\.?|ost|4k|1080p|主題歌|挿入歌";
    private static string Replace(string text, string pattern, string replacement) => Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static string CleanTitle(string value)
    {
        string text = value.Normalize(NormalizationForm.FormKC).Trim();
        var quoted = Regex.Match(text, @"^[【『「《\[].*?[】』」》\]].*?[「『]([^」』]+)[」』]", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        if (quoted.Success) text = quoted.Groups[1].Value;
        text = Replace(text, @"\s*[\(\[【](?:[^\)\]】]*?(?:" + Noise + @")[^\)\]】]*)[\)\]】]", "");
        text = Replace(text, @"\s*[-–—]\s*(?:" + Noise + @").*$", "");
        return text.Trim(' ', '「', '」', '『', '』', '《', '》');
    }
    public static string CleanArtist(string value)
    {
        string text = Replace(value.Normalize(NormalizationForm.FormKC), @"\s*\(CV[.:：]?[^)]*\)", "");
        text = Replace(text, @"\s+(?:feat\.?|ft\.?|featuring|with|vs\.?)\s+.*$", "");
        return text.Trim().TrimEnd(',', ';', '&').Trim();
    }
    public static string PrimaryArtist(string value) => Regex.Split(CleanArtist(value), @"\s*[,;&、]\s*|\s+x\s+", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))[0];
    public static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(char.IsLetterOrDigit));
    public static IReadOnlyList<string> TitleAliases(string value)
    {
        string clean = CleanTitle(value);
        var aliases = new List<string> { clean };
        var match = Regex.Match(clean, @"^(.+?)\s*[\(\[]([^\)\]]+)[\)\]]$", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        if (match.Success) { aliases.Add(match.Groups[1].Value.Trim()); aliases.Add(match.Groups[2].Value.Trim()); }
        return aliases.Concat(aliases.Select(KanaRomanizer.Convert)).Distinct().ToArray();
    }
    public static string Version(string title)
    {
        string text = title.Normalize(NormalizationForm.FormKC);
        var versions = new List<string>();
        foreach (string variant in new[] { "live", "remix", "acoustic", "instrumental", "cover", "tv", "remaster" })
            if (Regex.IsMatch(text, @"(?:^|[\s(\[\-])" + variant + @"(?:ed)?(?:$|[\s)\].\-])", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) versions.Add(variant);
        return string.Join("+", versions);
    }
    public static double Similarity(string a, string b)
    {
        a = Normalize(a); b = Normalize(b);
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a.Length > 512 || b.Length > 512) return 0;
        int[] previous = Enumerable.Range(0, b.Length + 1).ToArray(), next = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            next[0] = i;
            for (int j = 1; j <= b.Length; j++) next[j] = Math.Min(Math.Min(previous[j] + 1, next[j - 1] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, next) = (next, previous);
        }
        return 1 - previous[b.Length] / (double)Math.Max(a.Length, b.Length);
    }
    public static MatchDecision Evaluate(MediaTrack track, string title, string artist, TimeSpan duration)
    {
        if (Version(track.Title) != Version(title)) return new(false, 0, "different recording version");
        double delta = Math.Abs((duration - track.Duration).TotalSeconds);
        bool known = duration > TimeSpan.Zero && track.Duration > TimeSpan.Zero;
        if (known && delta > Math.Clamp(track.Duration.TotalSeconds * .03, 6, 10)) return new(false, 0, "duration mismatch");
        double titleScore = TitleAliases(track.Title).SelectMany(a => TitleAliases(title).Select(b => Similarity(a, b))).Max();
        double artistScore = Math.Max(Similarity(CleanArtist(track.Artist), CleanArtist(artist)), Similarity(PrimaryArtist(track.Artist), PrimaryArtist(artist)));
        artistScore = Math.Max(artistScore, Similarity(KanaRomanizer.Convert(PrimaryArtist(track.Artist)), KanaRomanizer.Convert(PrimaryArtist(artist))));
        if (titleScore < .82 || artistScore < .8) return new(false, 0, "title or artist mismatch");
        if (!known && (titleScore < .99 || artistScore < .99)) return new(false, 0, "duration unknown and identity is not exact");
        return new(true, titleScore * 65 + artistScore * 30 + (known ? Math.Max(0, 5 - delta) : 0), "identity and duration accepted");
    }
}
public sealed record MatchDecision(bool Accepted, double Score, string Reason);
