using System.Text.Json;
using System.Text.Json.Serialization;

namespace KotobaSUB.Core.Lyrics;

public sealed record LyricLearningAnchor(
    int LineIndex,
    double ProviderSeconds,
    double AudioSeconds,
    double Similarity,
    float AsrConfidence,
    double SongProgress);

public sealed record LyricLearningProfile(
    string TrackIdentity,
    string CandidateKey,
    LyricConfidenceState State,
    double MetadataConfidence,
    double AudioConfidence,
    int VerificationCount,
    int RejectCount,
    double AutoOffsetSeconds,
    double DriftScale,
    double ResidualSeconds,
    int AnchorCount,
    DateTimeOffset LastVerified,
    IReadOnlyList<LyricLearningAnchor> Anchors,
    string? RejectionReason = null)
{
    [JsonIgnore]
    public LyricCandidateAssessment Assessment => new(
        CandidateKey,
        MetadataConfidence,
        State,
        AudioConfidence,
        VerificationCount,
        RejectCount,
        double.NaN,
        double.NaN,
        null,
        RejectionReason);

    [JsonIgnore]
    public LyricAlignmentModel Alignment => new(
        Math.Clamp(double.IsFinite(DriftScale) ? DriftScale : 1, LyricAlignmentModel.MinimumScale, LyricAlignmentModel.MaximumScale),
        double.IsFinite(AutoOffsetSeconds) ? Math.Clamp(AutoOffsetSeconds, -30, 30) : 0,
        double.IsFinite(ResidualSeconds) ? Math.Max(0, ResidualSeconds) : 0,
        Math.Clamp(AnchorCount, 0, LyricAlignmentEstimator.MaximumAnchors));
}

public sealed class LyricVerificationStore
{
    private const int CurrentVersion = 1;
    public const int MaximumProfiles = 512;
    private const int MaximumAnchorsPerProfile = 64;
    private sealed record Document(int Version, List<LyricLearningProfile> Profiles);

    private readonly string path;
    private readonly Action<string> log;
    private readonly Dictionary<string, LyricLearningProfile> profiles = new(StringComparer.Ordinal);

    public LyricVerificationStore(string path, Action<string> log)
    {
        this.path = path;
        this.log = log;
        Load();
    }

    public IReadOnlyList<LyricLearningProfile> ForTrack(string trackIdentity) =>
        profiles.Values.Where(x => string.Equals(x.TrackIdentity, trackIdentity, StringComparison.Ordinal)).ToArray();

    public LyricLearningProfile? Get(string trackIdentity, string candidateKey) =>
        profiles.TryGetValue(Key(trackIdentity, candidateKey), out var profile) ? profile : null;

    public void Save(string trackIdentity, LyricCandidateAssessment assessment, LyricAlignmentModel alignment, IEnumerable<LyricCandidateEvidence> anchors)
    {
        if (string.IsNullOrWhiteSpace(trackIdentity) || string.IsNullOrWhiteSpace(assessment.CandidateKey)) return;
        var compactAnchors = anchors.Where(IsValidAnchor).TakeLast(MaximumAnchorsPerProfile).Select(x => new LyricLearningAnchor(
            x.LineIndex, x.ProviderTime.TotalSeconds, x.AudioTime.TotalSeconds, x.Similarity, x.AsrConfidence, x.SongProgress)).ToArray();
        var profile = new LyricLearningProfile(
            trackIdentity,
            assessment.CandidateKey,
            assessment.State,
            Clamp(assessment.MetadataScore, 0, 100),
            Clamp(assessment.AudioScore, 0, 1),
            Math.Clamp(assessment.PositiveAnchorCount, 0, MaximumAnchorsPerProfile),
            Math.Clamp(assessment.ContradictoryAnchorCount, 0, MaximumAnchorsPerProfile),
            Clamp(alignment.OffsetSeconds, -30, 30),
            Clamp(alignment.Scale, LyricAlignmentModel.MinimumScale, LyricAlignmentModel.MaximumScale),
            Math.Max(0, Clamp(alignment.ResidualSeconds, 0, 30)),
            Math.Clamp(alignment.AnchorCount, 0, MaximumAnchorsPerProfile),
            assessment.State == LyricConfidenceState.Verified ? DateTimeOffset.UtcNow : (Get(trackIdentity, assessment.CandidateKey)?.LastVerified ?? DateTimeOffset.MinValue),
            compactAnchors,
            assessment.RejectionReason);
        profiles[Key(trackIdentity, assessment.CandidateKey)] = profile;
        Trim();
        Write();
    }

    public void MarkRejected(string trackIdentity, string candidateKey, double metadataScore = 0, string reason = "manual wrong lyrics")
    {
        var current = Get(trackIdentity, candidateKey);
        Save(trackIdentity, new LyricCandidateAssessment(candidateKey, metadataScore > 0 ? metadataScore : current?.MetadataConfidence ?? 0, LyricConfidenceState.Rejected, current?.AudioConfidence ?? 0, current?.VerificationCount ?? 0, Math.Max(1, current?.RejectCount ?? 0), RejectionReason: reason), current?.Alignment ?? new(), []);
    }

    public void Clear(string? trackIdentity = null, string? candidateKey = null)
    {
        foreach (var key in profiles.Keys.Where(k => (trackIdentity is null || k.StartsWith(trackIdentity + "\u001f", StringComparison.Ordinal)) && (candidateKey is null || k.EndsWith("\u001f" + candidateKey, StringComparison.Ordinal))).ToArray()) profiles.Remove(key);
        Write();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 4_000_000) throw new InvalidDataException("Oversized learning store");
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
            if (document is null || document.Version != CurrentVersion || document.Profiles is null) return;
            foreach (var profile in document.Profiles.Take(MaximumProfiles).Where(IsValidProfile)) profiles[Key(profile.TrackIdentity, profile.CandidateKey)] = profile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { log($"lyric learning load failed: {ex.Message}"); profiles.Clear(); }
    }

    private void Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Document(CurrentVersion, profiles.Values.OrderByDescending(x => x.LastVerified).Take(MaximumProfiles).ToList())));
            File.Move(temp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { log($"lyric learning save failed: {ex.Message}"); }
    }

    private void Trim()
    {
        foreach (var key in profiles.OrderByDescending(x => x.Value.LastVerified).Skip(MaximumProfiles).Select(x => x.Key).ToArray()) profiles.Remove(key);
    }

    private static string Key(string trackIdentity, string candidateKey) => trackIdentity + "\u001f" + candidateKey;
    private static double Clamp(double value, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : min;
    private static bool IsValidAnchor(LyricCandidateEvidence x) => x.ProviderTime >= TimeSpan.Zero && x.AudioTime >= TimeSpan.Zero && double.IsFinite(x.Similarity) && double.IsFinite(x.SongProgress) && float.IsFinite(x.AsrConfidence);
    private static bool IsValidProfile(LyricLearningProfile x) => !string.IsNullOrWhiteSpace(x.TrackIdentity) && !string.IsNullOrWhiteSpace(x.CandidateKey) && Enum.IsDefined(x.State) && x.Anchors.Count <= MaximumAnchorsPerProfile;
}
