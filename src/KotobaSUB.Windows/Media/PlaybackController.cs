using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using KotobaSUB.Core.Audio;
using KotobaSUB.Core.Lyrics;
using KotobaSUB.Windows.Audio;
using KotobaSUB.Windows.Learning;

namespace KotobaSUB.Windows.Media;

internal sealed class PlaybackController : IDisposable
{
    private readonly OverlayWindow overlay;
    private readonly Action<string> status;
    private readonly Action<string> log;
    private readonly LearningRenderer learning;
    private readonly KotobaSUB.Japanese.MetadataReadings readings;
    private readonly HttpClient http = new();
    private readonly SmtcMetadataProvider metadata;
    private readonly LyricsSession session;
    private readonly LyricsCache lyricsCache;
    private readonly SongOffsets offsets;
    private readonly DispatcherTimer timer;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SubtitleSourceRouter router = new();
    private readonly AudioTranscriptionSession asr;
    private readonly AudioMediaClock audioClock = new();
    private readonly LyricProbeScheduler probeScheduler = new();
    private readonly object asrGate = new();
    private Task asrTransition = Task.CompletedTask;
    private AudioSourceStatus asrStatus = new(AudioSourceHealth.Stopped, "Local ASR stopped");
    private bool asrDesired;
    private bool flushProbeOnStop;
    private long asrGeneration;
    private long activeAsrGeneration;
    private long mediaGeneration;
    private bool preview;
    private bool paused;
    private bool frozen;
    private bool disposed;
    private MediaSnapshot? latest;
    private SubtitleFrame? rendered;

    public PlaybackController(OverlayWindow overlay, string directory, Action<string> status, Action<string> log)
    {
        this.overlay = overlay; this.status = status; this.log = log;
        learning = new LearningRenderer(overlay, Path.Combine(directory, "annotations"), log);
        readings = new(log);
        var client = new LyricsHttpClient(http);
        lyricsCache = new LyricsCache(Path.Combine(directory, "lyrics-v1"), log);
        var lyricLearning = new LyricVerificationStore(Path.Combine(directory, "lyric-learning-v1.json"), log);
        session = new LyricsSession(new LyricsResolver([new LrcLibSource(client), new NetEaseSource(client)], lyricsCache, log, readings.Romanize), log, lyricLearning);
        offsets = new(Path.Combine(directory, "song-offsets.json"), log);
        metadata = new(overlay.Dispatcher, log);
        metadata.Changed += OnMedia;
        session.Changed += Refresh;
        string modelPath = FindModelPath(directory);
        asr = new AudioTranscriptionSession(new WasapiLoopbackAudioSource(), new WhisperTranscriber(modelPath, log), log);
        asr.Transcript += OnTranscript;
        asr.CaptureObserved += OnCaptureObserved;
        asr.StatusChanged += OnAsrStatus;
        timer = new DispatcherTimer(DispatcherPriority.Background, overlay.Dispatcher);
        timer.Tick += (_, _) => { timer.Stop(); Refresh(); };
    }

    public async Task StartAsync()
    {
        try { await metadata.StartAsync(lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { log($"SMTC startup failed: {ex}"); status("Media sessions unavailable; local ASR available"); Refresh(); }
    }

    private void OnMedia(MediaSnapshot? snapshot)
    {
        bool mediaChanged = latest?.Track.Identity != snapshot?.Track.Identity;
        if (mediaChanged) Interlocked.Increment(ref mediaGeneration);
        latest = snapshot;
        audioClock.ObserveMedia(snapshot);
        if (mediaChanged) probeScheduler.Reset();
        if (!preview && !paused && !frozen) session.Update(snapshot);
    }

    public void SetPreview(bool value)
    {
        preview = value;
        session.Update(value || paused ? null : latest);
        rendered = null; Refresh();
    }

    public void SetPaused(bool value)
    {
        paused = value;
        session.Update(value || preview ? null : latest);
        rendered = null; Refresh();
    }

    public void SetFrozen(bool value)
    {
        if (frozen == value) return;
        frozen = value;
        session.Update(value ? null : latest);
        rendered = null; Refresh();
    }
    public void Retry(bool rejectCurrent)
    {
        if (preview || paused) return;
        session.Retry(rejectCurrent);
        if (!overlay.Preferences.InternetLyricsOnly && (asrStatus.Health is AudioSourceHealth.Faulted or AudioSourceHealth.Unavailable)) RestartAsr();
    }

    public void AdjustSync(double seconds)
    {
        if (session.Snapshot is not { } snapshot) return;
        offsets.Set(snapshot.Track.Identity, offsets.Get(snapshot.Track.Identity) + seconds); Refresh();
    }

    private void Refresh()
    {
        if (disposed) return;
        timer.Stop();
        if (preview) { SuspendAsr(); learning.RenderSample(PreviewSubtitle.Line); status("Sample preview"); return; }
        if (paused) { SuspendAsr(); learning.RenderSample(null); status("Paused"); return; }
        if (frozen) { SuspendAsr(); status("Frozen — Study mode"); return; }
        double offset = overlay.Preferences.GlobalOffsetSeconds + (session.Snapshot is { } snapshot ? offsets.Get(snapshot.Track.Identity) : 0);
        var structured = session.Frame(offset);
        AudioSourceStatus audioStatus; lock (asrGate) audioStatus = asrStatus;
        long now = Stopwatch.GetTimestamp();
        bool asrEnabled = !overlay.Preferences.InternetLyricsOnly;
        var decision = router.Evaluate(false, session.Timeline is not null, structured, audioStatus.Health, now, asrEnabled);
        bool probeActive = false;
        if (asrEnabled && session.Timeline is not null && session.Selected is not null && session.Assessments.TryGetValue(session.Selected.Key, out var selectedAssessment))
            probeActive = probeScheduler.IsActive(now) || probeScheduler.TryStart(now, selectedAssessment.State);
        bool flushProbe = session.Timeline is not null && !decision.ShouldRunAsr && !probeActive;
        SetAsrDesired(asrEnabled && (decision.ShouldRunAsr || probeActive), flushProbe && asrEnabled);
        status(SourceStatus(decision, audioStatus));
        if (decision.Frame != rendered) { learning.RenderFrame(decision.Frame); rendered = decision.Frame; }
        ScheduleSoonest(session.NextDelay(offset), decision.NextEvaluation, session.Timeline is not null ? probeScheduler.NextDelay(now) : null);
    }

    private void SuspendAsr()
    {
        AudioSourceStatus audioStatus; lock (asrGate) audioStatus = asrStatus;
        router.Evaluate(true, false, new(null), audioStatus.Health, Stopwatch.GetTimestamp());
        SetAsrDesired(false); rendered = null;
    }
    private string SourceStatus(SourceRoutingDecision decision, AudioSourceStatus audioStatus) => decision.Source switch
    {
        SubtitleSourceKind.StructuredLyrics => session.Status,
        SubtitleSourceKind.LocalAsr => "Local Japanese ASR",
        _ when decision.ShouldRunAsr && audioStatus.Health == AudioSourceHealth.Running => "Listening with local Japanese ASR",
        _ when decision.ShouldRunAsr && audioStatus.Health == AudioSourceHealth.Starting => "Starting local Japanese ASR",
        _ when overlay.Preferences.InternetLyricsOnly && decision.Source == SubtitleSourceKind.None => "Internet lyrics only — no timed lyrics found",
        _ when audioStatus.Health is AudioSourceHealth.Unavailable or AudioSourceHealth.Faulted => audioStatus.Message,
        _ => session.Status
    };

    private void ScheduleSoonest(TimeSpan? first, TimeSpan? second, TimeSpan? third = null)
    {
        TimeSpan? delay = first is null ? second is null ? third : third is null || second <= third ? second : third : second is null ? third is null || first <= third ? first : third : third is null ? first <= second ? first : second : new[] { first.Value, second.Value, third.Value }.Min();
        if (delay is null) return;
        timer.Interval = delay < TimeSpan.FromMilliseconds(15) ? TimeSpan.FromMilliseconds(15) : delay.Value;
        timer.Start();
    }

    private void OnTranscript(TranscriptionSegment value)
    {
        long generation; lock (asrGate) generation = activeAsrGeneration;
        long media = Volatile.Read(ref mediaGeneration);
        _ = overlay.Dispatcher.BeginInvoke(new Action(() =>
        {
            lock (asrGate) { if (disposed || generation != asrGeneration) return; }
            if (overlay.Preferences.InternetLyricsOnly) return;
            if (media != Volatile.Read(ref mediaGeneration)) return;
            var completedAt = Stopwatch.GetTimestamp();
            var observed = audioClock.MapCaptureTime(value.Start + (value.End - value.Start) / 2);

            var latency = audioClock.EstimateInferenceLatency(value.End, completedAt);
            if (latency is not null && observed is not null) log($"ASR callback latency={latency.Value.TotalMilliseconds:F0}ms; capture center={observed.Value.TotalSeconds:F3}s");
            if (observed is not null && session.Snapshot is not null && session.Candidates.Count > 0)
            {
                var snapshot = session.Snapshot;
                double progress = snapshot.Track.Duration > TimeSpan.Zero ? observed.Value.TotalSeconds / snapshot.Track.Duration.TotalSeconds : 0;
                foreach (var candidate in session.Candidates) session.AcceptEvidence(candidate.Candidate.Key, value, observed.Value, progress);
            }
            if (latency is not null && session.Selected is { } selected && session.Assessments.TryGetValue(selected.Key, out var assessment)) probeScheduler.RecordInference(latency.Value, assessment.State);
            router.AcceptTranscript(value, Stopwatch.GetTimestamp(), !overlay.Preferences.InternetLyricsOnly); rendered = null; Refresh();
        }));
    }

    private void OnCaptureObserved(AudioCaptureObservation observation) => audioClock.ObserveCapture(observation);

    private void OnAsrStatus(AudioSourceStatus value)
    {
        long generation;
        lock (asrGate) { asrStatus = value; generation = asrGeneration; }
        _ = overlay.Dispatcher.BeginInvoke(new Action(() =>
        {
            lock (asrGate) { if (disposed || generation != asrGeneration) return; }
            if (value.Health is AudioSourceHealth.Faulted or AudioSourceHealth.Unavailable) router.ClearTranscript();
            rendered = null; Refresh();
        }));
    }

    private void SetAsrDesired(bool value, bool flushProbe = false)
    {
        lock (asrGate)
        {
            if (disposed) return;
            if (!value && flushProbe && asrDesired) flushProbeOnStop = true;
            if (asrDesired == value) return;
            asrDesired = value; long generation = ++asrGeneration;
            asrTransition = asrTransition.ContinueWith(_ => ApplyAsrTargetAsync(value, generation), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task ApplyAsrTargetAsync(bool target, long operationGeneration)
    {
        try
        {
            if (target)
            {
                lock (asrGate) activeAsrGeneration = operationGeneration;
                await asr.StartAsync(lifetime.Token).ConfigureAwait(false);
            }
            else { bool flush; lock (asrGate) { flush = flushProbeOnStop; flushProbeOnStop = false; if (flush) activeAsrGeneration = operationGeneration; } await asr.StopAsync(flush).ConfigureAwait(false); }
            lock (asrGate) { if (!target) activeAsrGeneration = 0; }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            log($"ASR transition failed: {ex}");
            lock (asrGate)
            {
                activeAsrGeneration = 0; asrStatus = new(AudioSourceHealth.Unavailable, "Local Japanese ASR unavailable", ex);
                if (asrGeneration == operationGeneration) { asrDesired = false; asrGeneration++; }
            }
            _ = overlay.Dispatcher.BeginInvoke(new Action(() => { if (!disposed) { router.ClearTranscript(); rendered = null; Refresh(); } }));
        }
    }

    private void RestartAsr()
    {
        SetAsrDesired(false);
        Task pending; lock (asrGate) pending = asrTransition;
        _ = pending.ContinueWith(completed => { _ = overlay.Dispatcher.BeginInvoke(new Action(() => { if (!disposed) { lock (asrGate) asrStatus = new(AudioSourceHealth.Stopped, "Local ASR stopped"); Refresh(); } })); }, TaskScheduler.Default);
    }

    private static string FindModelPath(string directory)
    {
        string installed = Path.Combine(directory, "models", "ggml-base.bin");
        if (File.Exists(installed)) return installed;
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            string development = Path.Combine(current.FullName, ".data", "models", "ggml-base.bin");
            if (File.Exists(development)) return development;
        }
        return installed;
    }

    public void SettingsChanged() { rendered = null; Refresh(); }
    public int ClearLyricsCache() => lyricsCache.Clear();
    public void ClearLearnedLyrics() => session.ClearLearnedDecisions();

    public void Dispose()
    {
        Task transition;
        lock (asrGate) { if (disposed) return; asrDesired = false; long generation = ++asrGeneration; asrTransition = asrTransition.ContinueWith(_ => ApplyAsrTargetAsync(false, generation), TaskScheduler.Default).Unwrap(); transition = asrTransition; }
        lifetime.Cancel();
        try { transition.Wait(TimeSpan.FromSeconds(4)); } catch (AggregateException ex) { log($"ASR shutdown transition: {ex.GetBaseException().Message}"); }
        asr.Transcript -= OnTranscript; asr.CaptureObserved -= OnCaptureObserved; asr.StatusChanged -= OnAsrStatus;
        try { asr.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(4)); } catch (AggregateException ex) { log($"ASR shutdown: {ex.GetBaseException().Message}"); }
        lock (asrGate) disposed = true;
        learning.Dispose(); timer.Stop(); metadata.Dispose(); session.Dispose(); http.Dispose(); lifetime.Dispose(); _ = Task.Run(readings.Dispose);
    }
}
