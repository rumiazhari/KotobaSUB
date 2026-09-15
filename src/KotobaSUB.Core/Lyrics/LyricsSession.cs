using System.Diagnostics;

namespace KotobaSUB.Core.Lyrics;

// All public mutations are made on the UI synchronization context. Async results
// are generation-checked even if a provider ignores its cancellation token.
public sealed class LyricsSession(ILyricsProvider provider, Action<string> log) : IDisposable
{
    private CancellationTokenSource? request;
    private long generation;
    private readonly HashSet<string> rejected = new();
    public MediaSnapshot? Snapshot { get; private set; }
    public LyricsCandidate? Selected { get; private set; }
    public LyricTimeline? Timeline { get; private set; }
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
            rejected.Clear(); Start(false);
        }
        Changed?.Invoke();
    }
    public void Retry(bool rejectCurrent)
    {
        if (disposed) return;
        if (rejectCurrent && Selected is not null) rejected.Add(Selected.Key);
        Start(true); Changed?.Invoke();
    }
    private void Start(bool bypassCache)
    {
        request?.Cancel(); request = null; generation++;
        Selected = null; Timeline = null;
        if (Snapshot is not { } snapshot || string.IsNullOrWhiteSpace(snapshot.Track.Title) || string.IsNullOrWhiteSpace(snapshot.Track.Artist))
        { Status = "No music metadata — ASR not installed"; return; }
        Status = "Finding synchronized lyrics";
        var cancellation = new CancellationTokenSource(); request = cancellation;
        Pending = ResolveAsync(snapshot.Track, generation, cancellation, bypassCache);
    }
    private async Task ResolveAsync(MediaTrack track, long expectedGeneration, CancellationTokenSource cancellation, bool bypassCache)
    {
        try
        {
            var result = await provider.ResolveAsync(track, new HashSet<string>(rejected), bypassCache, cancellation.Token);
            if (disposed || cancellation.IsCancellationRequested || expectedGeneration != generation) return;
            Selected = result;
            Timeline = result?.Original is { } text ? new LyricTimeline(text, result.Translation, track.Duration) : null;
            Status = result is null ? "No timed lyrics — ASR not installed" : result.Source;
            Changed?.Invoke();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            log($"lyrics session failed: {ex}");
            if (!disposed && expectedGeneration == generation) { Status = "Lyrics unavailable — ASR not installed"; Changed?.Invoke(); }
        }
        finally { if (request == cancellation) request = null; cancellation.Dispose(); }
    }
    public SubtitleFrame Frame(double offsetSeconds, long? timestamp = null)
    {
        if (Snapshot is not { } snapshot || Timeline is null) return new(null);
        var position = snapshot.PositionAt(timestamp ?? Stopwatch.GetTimestamp()) + TimeSpan.FromSeconds(offsetSeconds);
        return Timeline.At(position);
    }
    public TimeSpan? NextDelay(double offsetSeconds)
    {
        if (Snapshot is not { Playing: true, Rate: > 0 } snapshot || Timeline is null) return null;
        var position = snapshot.PositionAt(Stopwatch.GetTimestamp()) + TimeSpan.FromSeconds(offsetSeconds);
        var next = Timeline.NextBoundary(position);
        return next is null ? null : TimeSpan.FromMilliseconds(Math.Max(15, (next.Value - position).TotalMilliseconds / snapshot.Rate));
    }
    public void Dispose() { disposed = true; generation++; request?.Cancel(); request = null; }
}
