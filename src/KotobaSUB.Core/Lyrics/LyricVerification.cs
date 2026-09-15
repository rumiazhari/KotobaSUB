using System.Globalization;
using System.Text;
using KotobaSUB.Core.Audio;

namespace KotobaSUB.Core.Lyrics;

public sealed record LyricTextComparison(
    double CharacterSimilarity,
    double OrderedCoverage,
    double TokenOverlap,
    double Score,
    string NormalizedAsr,
    string NormalizedLyric);

public static class LyricComparison
{
    public const int MaximumComparisonLength = 512;
    public const int MinimumMeaningfulCharacters = 3;

    public static LyricTextComparison Compare(string asrText, string lyricText, Func<string, IReadOnlyList<string>>? tokenizer = null)
    {
        string asr = Normalize(asrText);
        string lyric = Normalize(lyricText);
        if (asr.Length == 0 || lyric.Length == 0 || asr.Length < MinimumMeaningfulCharacters)
            return new(0, 0, 0, 0, asr, lyric);
        if (asr.Length > MaximumComparisonLength || lyric.Length > MaximumComparisonLength)
            return new(0, 0, 0, 0, asr, lyric);
        double character = 1 - Levenshtein(asr, lyric) / (double)Math.Max(asr.Length, lyric.Length);
        double ordered = LongestCommonSubsequence(asr, lyric) / (double)asr.Length;
        double token = tokenizer is null ? 0 : Overlap(tokenizer(asr), tokenizer(lyric));
        double score = tokenizer is null ? character * .55 + ordered * .45 : character * .45 + ordered * .4 + token * .15;
        return new(character, ordered, token, Math.Clamp(score, 0, 1), asr, lyric);
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string text = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            char normalized = c is >= '\u30A1' and <= '\u30F6' ? (char)(c - 0x60) : c;
            if (char.IsLetterOrDigit(normalized) || normalized == '\u30FC' || normalized is >= '\u3040' and <= '\u30FF') builder.Append(normalized);
        }
        return builder.ToString();
    }

    private static double Overlap(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        var a = first.Select(Normalize).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
        var b = second.Select(Normalize).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
        return a.Count == 0 || b.Count == 0 ? 0 : a.Intersect(b, StringComparer.Ordinal).Count() / (double)a.Union(b, StringComparer.Ordinal).Count();
    }

    private static int Levenshtein(string a, string b)
    {
        int[] previous = Enumerable.Range(0, b.Length + 1).ToArray();
        int[] next = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            next[0] = i;
            for (int j = 1; j <= b.Length; j++) next[j] = Math.Min(Math.Min(previous[j] + 1, next[j - 1] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, next) = (next, previous);
        }
        return previous[b.Length];
    }

    private static int LongestCommonSubsequence(string a, string b)
    {
        int[] row = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            int diagonal = 0;
            for (int j = 1; j <= b.Length; j++)
            {
                int prior = row[j];
                row[j] = a[i - 1] == b[j - 1] ? diagonal + 1 : Math.Max(row[j], row[j - 1]);
                diagonal = prior;
            }
        }
        return row[b.Length];
    }
}

public enum LyricConfidenceState { Unknown, Tentative, Probable, Verified, Rejected }

public sealed record LyricCandidateEvidence(
    string CandidateKey,
    int LineIndex,
    TimeSpan ProviderTime,
    TimeSpan AudioTime,
    double Similarity,
    float AsrConfidence,
    double SongProgress)
{
    public double ResidualSeconds => AudioTime.TotalSeconds - ProviderTime.TotalSeconds;
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record LyricCandidateAssessment(
    string CandidateKey,
    double MetadataScore,
    LyricConfidenceState State = LyricConfidenceState.Unknown,
    double AudioScore = 0,
    int PositiveAnchorCount = 0,
    int ContradictoryAnchorCount = 0,
    double CoverageStart = double.NaN,
    double CoverageEnd = double.NaN,
    double? LastContradictionAudioSeconds = null,
    string? RejectionReason = null);

public static class LyricConfidenceEvaluator
{
    public const double MinimumMetadataScore = 80;
    public const double PositiveSimilarity = .72;
    public const float PositiveAsrConfidence = .35f;
    public const double NegativeSimilarity = .28;
    public const float NegativeAsrConfidence = .60f;
    public const int MinimumVerifiedAnchors = 3;
    public const double MinimumVerifiedCoverage = .25;
    public const double MinimumContradictionSeparationSeconds = 5;

    public static LyricCandidateAssessment Start(string candidateKey, double metadataScore) =>
        new(candidateKey, metadataScore, metadataScore >= MinimumMetadataScore ? LyricConfidenceState.Tentative : LyricConfidenceState.Unknown);

    public static LyricCandidateAssessment Apply(LyricCandidateAssessment current, LyricCandidateEvidence evidence)
    {
        if (current.State == LyricConfidenceState.Rejected || current.State == LyricConfidenceState.Unknown) return current;
        bool positive = double.IsFinite(evidence.Similarity) && evidence.Similarity >= PositiveSimilarity && float.IsFinite(evidence.AsrConfidence) && evidence.AsrConfidence >= PositiveAsrConfidence;
        bool negative = double.IsFinite(evidence.Similarity) && evidence.Similarity <= NegativeSimilarity && float.IsFinite(evidence.AsrConfidence) && evidence.AsrConfidence >= NegativeAsrConfidence;
        if (positive)
        {
            int count = current.PositiveAnchorCount + 1;
            double start = double.IsNaN(current.CoverageStart) ? evidence.SongProgress : Math.Min(current.CoverageStart, evidence.SongProgress);
            double end = double.IsNaN(current.CoverageEnd) ? evidence.SongProgress : Math.Max(current.CoverageEnd, evidence.SongProgress);
            double score = current.PositiveAnchorCount == 0 ? evidence.Similarity : (current.AudioScore * current.PositiveAnchorCount + evidence.Similarity) / count;
            var state = count >= MinimumVerifiedAnchors && end - start >= MinimumVerifiedCoverage ? LyricConfidenceState.Verified : LyricConfidenceState.Probable;
            return current with { State = state, AudioScore = score, PositiveAnchorCount = count, CoverageStart = start, CoverageEnd = end };
        }
        if (negative)
        {
            bool separated = current.LastContradictionAudioSeconds is { } previous && Math.Abs(previous - evidence.AudioTime.TotalSeconds) >= MinimumContradictionSeparationSeconds;
            int count = current.ContradictoryAnchorCount + (separated || current.ContradictoryAnchorCount == 0 ? 1 : 0);
            if (count >= 2) return current with { State = LyricConfidenceState.Rejected, ContradictoryAnchorCount = count, LastContradictionAudioSeconds = evidence.AudioTime.TotalSeconds, RejectionReason = "repeated high-confidence audio disagreement" };
            return current with { ContradictoryAnchorCount = count, LastContradictionAudioSeconds = evidence.AudioTime.TotalSeconds };
        }
        return current;
    }
}

public sealed record LyricAlignmentModel(
    double Scale = 1,
    double OffsetSeconds = 0,
    double ResidualSeconds = 0,
    int AnchorCount = 0)
{
    public const double MinimumScale = .98;
    public const double MaximumScale = 1.02;
    public TimeSpan CorrectedCueTime(TimeSpan providerTime) => TimeSpan.FromSeconds(Math.Max(0, Scale * providerTime.TotalSeconds + OffsetSeconds));
    public TimeSpan ProviderTimeAtAudio(TimeSpan audioTime) => TimeSpan.FromSeconds(Math.Max(0, (audioTime.TotalSeconds - OffsetSeconds) / Scale));
    public TimeSpan? ProjectedNext(TimeSpan previousProviderTime, TimeSpan nextProviderTime)
    {
        if (nextProviderTime < previousProviderTime) return null;
        TimeSpan projected = CorrectedCueTime(nextProviderTime);
        return projected < CorrectedCueTime(previousProviderTime) ? CorrectedCueTime(previousProviderTime) : projected;
    }
}

public static class LyricAlignmentEstimator
{
    public const int MaximumAnchors = 64;
    public const double MinimumAnchorSimilarity = .60;
    public const float MinimumAnchorConfidence = .30f;
    public const double MinimumDriftSpanSeconds = 20;
    public const double MaximumResidualToleranceSeconds = .75;

    public static LyricAlignmentModel Estimate(IEnumerable<LyricCandidateEvidence> source)
    {
        var anchors = source.Where(IsUsable).Take(MaximumAnchors).ToArray();
        if (anchors.Length == 0) return new();
        double initialOffset = WeightedMedian(anchors.Select(a => (a.ResidualSeconds, Weight(a))));
        double[] deviations = anchors.Select(a => Math.Abs(a.ResidualSeconds - initialOffset)).OrderBy(x => x).ToArray();
        double medianDeviation = Median(deviations);
        double tolerance = Math.Min(MaximumResidualToleranceSeconds, Math.Max(.20, medianDeviation * 3 + .05));
        var inliers = anchors.Where(a => Math.Abs(a.ResidualSeconds - initialOffset) <= tolerance).ToArray();
        if (inliers.Length == 0) inliers = [anchors.OrderBy(a => Math.Abs(a.ResidualSeconds - initialOffset)).First()];
        double scale = 1;
        if (inliers.Length >= 3 && inliers.Max(a => a.ProviderTime.TotalSeconds) - inliers.Min(a => a.ProviderTime.TotalSeconds) >= MinimumDriftSpanSeconds)
        {
            var slopes = new List<double>();
            for (int i = 0; i < inliers.Length; i++)
                for (int j = i + 1; j < inliers.Length; j++)
                {
                    double dx = inliers[j].ProviderTime.TotalSeconds - inliers[i].ProviderTime.TotalSeconds;
                    if (Math.Abs(dx) >= 1) slopes.Add((inliers[j].AudioTime.TotalSeconds - inliers[i].AudioTime.TotalSeconds) / dx);
                }
            if (slopes.Count > 0)
            {
                double candidate = Median(slopes.OrderBy(x => x).ToArray());
                if (double.IsFinite(candidate) && candidate is >= LyricAlignmentModel.MinimumScale and <= LyricAlignmentModel.MaximumScale) scale = candidate;
            }
        }
        double offset = WeightedMedian(inliers.Select(a => (a.AudioTime.TotalSeconds - scale * a.ProviderTime.TotalSeconds, Weight(a))));
        double residual = Math.Sqrt(inliers.Average(a => Math.Pow(a.AudioTime.TotalSeconds - (scale * a.ProviderTime.TotalSeconds + offset), 2)));
        return new(scale, offset, residual, inliers.Length);
    }

    public static IReadOnlyList<TimeSpan> CorrectMonotonic(IEnumerable<TimeSpan> providerTimes, LyricAlignmentModel model)
    {
        var result = new List<TimeSpan>();
        TimeSpan previous = TimeSpan.Zero;
        foreach (TimeSpan value in providerTimes)
        {
            TimeSpan corrected = model.CorrectedCueTime(value);
            if (result.Count > 0 && corrected < previous) corrected = previous;
            result.Add(corrected); previous = corrected;
        }
        return result;
    }

    private static bool IsUsable(LyricCandidateEvidence anchor) => anchor.ProviderTime >= TimeSpan.Zero && anchor.AudioTime >= TimeSpan.Zero && double.IsFinite(anchor.Similarity) && anchor.Similarity >= MinimumAnchorSimilarity && float.IsFinite(anchor.AsrConfidence) && anchor.AsrConfidence >= MinimumAnchorConfidence;
    private static double Weight(LyricCandidateEvidence anchor) => Math.Max(.01, anchor.Similarity * anchor.AsrConfidence);
    private static double WeightedMedian(IEnumerable<(double Value, double Weight)> values)
    {
        var ordered = values.Where(v => double.IsFinite(v.Value) && double.IsFinite(v.Weight) && v.Weight > 0).OrderBy(v => v.Value).ToArray();
        if (ordered.Length == 0) return 0;
        double total = ordered.Sum(v => v.Weight), cumulative = 0;
        foreach (var value in ordered) { cumulative += value.Weight; if (cumulative >= total / 2) return value.Value; }
        return ordered[^1].Value;
    }
    private static double Median(double[] values) => values.Length == 0 ? 0 : values.Length % 2 == 1 ? values[values.Length / 2] : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2;
}

public sealed class LyricVerifier
{
    private readonly TimeSpan searchWindow;
    public LyricVerifier(TimeSpan? searchWindow = null) => this.searchWindow = searchWindow ?? TimeSpan.FromSeconds(8);

    public LyricCandidateEvidence Probe(string candidateKey, LyricTimeline timeline, TranscriptionSegment segment, TimeSpan observedAudioTime, double songProgress)
    {
        var nearby = timeline.Lines.Select((line, index) => (line, index, distance: Math.Abs((line.Start - observedAudioTime).TotalSeconds))).Where(x => x.distance <= searchWindow.TotalSeconds).OrderBy(x => x.distance).ThenBy(x => x.index).ToArray();
        var best = nearby.Select(x => (x, comparison: LyricComparison.Compare(segment.Text, x.line.OriginalText))).OrderByDescending(x => x.comparison.Score).ThenBy(x => x.x.distance).FirstOrDefault();
        if (best.x.line is null) return new(candidateKey, -1, observedAudioTime, observedAudioTime, 0, segment.Probability, songProgress);
        return new(candidateKey, best.x.index, best.x.line.Start, observedAudioTime, best.comparison.Score, segment.Probability, songProgress);
    }
}