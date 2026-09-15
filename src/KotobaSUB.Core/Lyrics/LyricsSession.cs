using System.Diagnostics;
using KotobaSUB.Core.Audio;

namespace KotobaSUB.Core.Lyrics;

// All public mutations are made on the UI synchronization context. Async results
// are generation-checked even if a provider ignores its cancellation token.
public sealed class LyricsSession(ILyricsProvider provider, Action<string> log, LyricVerificationStore? learning = null) : IDisposable
{
    private CancellationTokenSource? request;
    private long generation;
    private readonly HashSet<string> rejected = new();
    private readonly Dictionary<string, LyricCandidateAssessment> assessments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<LyricCandidateEvidence>> evidence = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LyricAlignmentModel> learnedAlignments = new(StringComparer.Ordinal);
    private readonly LyricVerifier verifier = new();
    public MediaSnapshot? Snapshot { get; private set; }
    public LyricsCandidate? Selected { get; private set; }
    public LyricTimeline? Timeline { get; private set; }
    public LyricAlignmentModel AutomaticAlignment { get; private set; } = new();
    public IReadOnlyList<LyricsCandidateMatch> Candidates { get; private set; } = [];
    public IReadOnlyDictionary<string, LyricCandidateAssessment> Assessments => assessments;
    public string Status { get; private set; } = "Waiting for media";
    public Task Pending { get; private set; } = Task.CompletedTask;
    public event Action? Changed;
    private bool disposed;

    public void Update(MediaSnapshot? value)
    {
        if (disposed) return;
        bool changed = Snapshot?.Track.Identity != value?.Track.Identity;
        bool durationArrived = Snapshot?.Track.Duration <= TimeSpan.Zero && value?.Track.Duration > TimeSpan.Zero;
        bool durationChanged = Snapshot is { } old && value is { } incoming && Math.Abs((old.Track.Duration - incoming.Track.Duration).TotalSeconds) > 6;
        Snapshot = value;
        if (changed || durationArrived || durationChanged)
        {
            rejected.Clear(); ResetCandidates(); Start(false);
        }
        Changed?.Invoke();
    }

    public void Retry(bool rejectCurrent)
    {
        if (disposed) return;
        if (rejectCurrent && Selected is not null)
        {
            rejected.Add(Selected.Key);
            if (Snapshot is { } snapshot)
            {
                var current = assessments.TryGetValue(Selected.Key, out var assessment) ? assessment : LyricConfidenceEvaluator.Start(Selected.Key, 0);
                learning?.MarkRejected(snapshot.Track.Identity, Selected.Key, current.MetadataScore);
            }
        }
        ResetCandidates(); Start(true); Changed?.Invoke();
    }

    private void ResetCandidates()
    {
        Candidates = []; assessments.Clear(); evidence.Clear(); learnedAlignments.Clear(); AutomaticAlignment = new(); Selected = null; Timeline = null;
    }

    private void Start(bool bypassCache)
    {
        request?.Cancel(); request = null; generation++;
        Selected = null; Timeline = null; AutomaticAlignment = new();
        if (Snapshot is not { } snapshot || string.IsNullOrWhiteSpace(snapshot.Track.Title) || string.IsNullOrWhiteSpace(snapshot.Track.Artist))
        { Status = "No music metadata"; return; }
        Status = "Finding synchronized lyrics";
        var cancellation = new CancellationTokenSource(); request = cancellation;
        Pending = ResolveAsync(snapshot.Track, generation, cancellation, bypassCache);
    }

    private async Task ResolveAsync(MediaTrack track, long expectedGeneration, CancellationTokenSource cancellation, bool bypassCache)
    {
        try
        {
            if (provider is IMultiLyricsProvider multi)
            {
                var shortlist = await multi.ResolveCandidatesAsync(track, new HashSet<string>(rejected), bypassCache, cancellation.Token).ConfigureAwait(false);
                if (disposed || cancellation.IsCancellationRequested || expectedGeneration != generation) return;
                Candidates = shortlist;
                foreach (var item in shortlist)
                {
                    var profile = learning?.Get(track.Identity, item.Candidate.Key);
                    assessments[item.Candidate.Key] = profile?.Assessment ?? LyricConfidenceEvaluator.Start(item.Candidate.Key, item.Match.Score);
                    if (profile is not null) learnedAlignments[item.Candidate.Key] = profile.Alignment;
                }
                var first = shortlist.Select(x => x.Candidate).FirstOrDefault(x => assessments[x.Key].State != LyricConfidenceState.Rejected);
                SetSelected(first);
                Status = first is null ? "No timed lyrics" : $"{first.Source} candidate shortlist";
            }
            else
            {
                var result = await provider.ResolveAsync(track, new HashSet<string>(rejected), bypassCache, cancellation.Token).ConfigureAwait(false);
                if (disposed || cancellation.IsCancellationRequested || expectedGeneration != generation) return;
                SetSelected(result);
                Status = result is null ? "No timed lyrics" : result.Source;
            }
            Changed?.Invoke();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            log($"lyrics session failed: {ex}");
            if (!disposed && expectedGeneration == generation) { Status = "Lyrics unavailable"; Changed?.Invoke(); }
        }
        finally { if (request == cancellation) request = null; cancellation.Dispose(); }
    }

    private void SetSelected(LyricsCandidate? candidate)
    {
        Selected = candidate;
        Timeline = candidate?.Original is { } text ? new LyricTimeline(text, candidate.Translation, Snapshot?.Track.Duration ?? TimeSpan.Zero) : null;
        AutomaticAlignment = candidate is null ? new() : evidence.TryGetValue(candidate.Key, out var anchors) ? LyricAlignmentEstimator.Estimate(anchors) : learnedAlignments.GetValueOrDefault(candidate.Key, new());
    }

    public void AcceptEvidence(string candidateKey, TranscriptionSegment segment, TimeSpan observedAudioTime, double songProgress)
    {
        if (disposed || Snapshot is null) return;
        var candidate = Candidates.FirstOrDefault(x => x.Candidate.Key == candidateKey)?.Candidate;
        if (candidate?.Original is not { } original) return;
        var timeline = new LyricTimeline(original, candidate.Translation, Snapshot.Track.Duration);
        var probe = verifier.Probe(candidateKey, timeline, segment, observedAudioTime, Math.Clamp(songProgress, 0, 1));
        var anchors = evidence.TryGetValue(candidateKey, out var existing) ? existing : evidence[candidateKey] = new();
        anchors.Add(probe);
        if (anchors.Count > LyricAlignmentEstimator.MaximumAnchors) anchors.RemoveAt(0);
        var current = assessments.TryGetValue(candidateKey, out var assessment) ? assessment : LyricConfidenceEvaluator.Start(candidateKey, 0);
        var next = LyricConfidenceEvaluator.Apply(current, probe);
        assessments[candidateKey] = next;
        log($"lyrics evidence {candidateKey}: line={probe.LineIndex}; similarity={probe.Similarity:F2}; residual={probe.ResidualSeconds:+0.000;-0.000;0.000}s; state={next.State}");

        if (Selected?.Key == candidateKey)
        {
            if (next.State == LyricConfidenceState.Rejected)
            {
                var replacement = Candidates.Select(x => x.Candidate).FirstOrDefault(x => x.Key != candidateKey && assessments.TryGetValue(x.Key, out var other) && other.State != LyricConfidenceState.Rejected);
                if (replacement is not null)
                {
                    SetSelected(replacement); Status = $"Trying alternate {replacement.Source} lyrics"; log($"lyrics candidate switch {candidateKey} -> {replacement.Key}");
                }
                else
                {
                    SetSelected(null); Status = "Lyrics candidate rejected; awaiting another source";
                }
            }
            else
            {
                AutomaticAlignment = LyricAlignmentEstimator.Estimate(anchors);
                learnedAlignments[candidateKey] = AutomaticAlignment;
            }
        }
        learning?.Save(Snapshot.Track.Identity, next, LyricAlignmentEstimator.Estimate(anchors), anchors);
        Changed?.Invoke();
    }

    public void ClearLearnedDecisions()
    {
        if (Snapshot is { } snapshot) learning?.Clear(snapshot.Track.Identity);
        rejected.Clear();
        ResetCandidates();
        Start(true);
        Changed?.Invoke();
    }

    public SubtitleFrame Frame(double offsetSeconds, long? timestamp = null)
    {
        if (Snapshot is not { } snapshot || Timeline is null) return new(null);
        var audioPosition = snapshot.PositionAt(timestamp ?? Stopwatch.GetTimestamp()) + TimeSpan.FromSeconds(offsetSeconds);
        return Timeline.At(AutomaticAlignment.ProviderTimeAtAudio(audioPosition));
    }

    public TimeSpan? NextDelay(double offsetSeconds)
    {
        if (Snapshot is not { Playing: true, Rate: > 0 } snapshot || Timeline is null) return null;
        var audioPosition = snapshot.PositionAt(Stopwatch.GetTimestamp()) + TimeSpan.FromSeconds(offsetSeconds);
        var providerPosition = AutomaticAlignment.ProviderTimeAtAudio(audioPosition);
        var next = Timeline.NextBoundary(providerPosition);
        if (next is null) return null;
        var correctedAudio = AutomaticAlignment.CorrectedCueTime(next.Value);
        return TimeSpan.FromMilliseconds(Math.Max(15, (correctedAudio - audioPosition).TotalMilliseconds / snapshot.Rate));
    }

    public void Dispose() { disposed = true; generation++; request?.Cancel(); request = null; }
}