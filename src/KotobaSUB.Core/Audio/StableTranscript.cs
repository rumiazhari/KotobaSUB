using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KotobaSUB.Core.Audio;

public sealed class StableTranscript
{
    private string previous = "";
    private string committed = "";
    public string? Submit(IReadOnlyList<TranscriptionSegment> segments, bool finalWindow)
    {
        var accepted = segments.Where(s => s.Probability >= .30f && s.NoSpeechProbability <= .65f)
            .Select(s => Normalize(s.Text)).Where(s => s.Length > 0 && !Noise(s)).ToArray();
        string candidate = string.Concat(accepted);
        if (candidate.Length == 0) { if (finalWindow) ResetPending(); return null; }
        string stable = finalWindow ? candidate : CommonPrefix(previous, candidate);
        previous = finalWindow ? "" : candidate;
        if (!finalWindow && new StringInfo(stable).LengthInTextElements < 2) return null;
        string delta = RemoveOverlap(committed, stable);
        if (delta.Length == 0) return null;
        committed += delta;
        if (committed.Length > 512) committed = committed[^256..];
        return delta;
    }
    public void SilenceReset() { previous = ""; committed = ""; }
    private void ResetPending() => previous = "";
    internal static string Normalize(string text)
    {
        string value = text.Normalize(NormalizationForm.FormKC).Trim();
        return Regex.Replace(value, @"\s+", value.Any(c => c >= 0x3000) ? "" : " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    }
    private static bool Noise(string text) => text is "[音楽]" or "(音楽)" or "[Music]" or "(Music)" or "♪" or "ご視聴ありがとうございました" or "字幕をご覧いただきありがとうございました";
    private static string CommonPrefix(string a, string b)
    {
        int length = 0, limit = Math.Min(a.Length, b.Length);
        while (length < limit && a[length] == b[length]) length++;
        if (length > 0 && length < a.Length && char.IsHighSurrogate(a[length - 1])) length--;
        return b[..length].TrimEnd();
    }
    private static string RemoveOverlap(string prior, string next)
    {
        if (next.Length == 0) return "";
        int max = Math.Min(prior.Length, next.Length);
        for (int length = max; length > 0; length--)
            if (prior.EndsWith(next[..length], StringComparison.Ordinal)) return next[length..];
        return next;
    }
}
