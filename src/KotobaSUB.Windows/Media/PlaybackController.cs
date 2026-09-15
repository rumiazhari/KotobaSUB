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
    private readonly SongOffsets offsets;
    private readonly DispatcherTimer timer;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SubtitleSourceRouter router = new();
    private readonly AudioTranscriptionSession asr;
    private readonly object asrGate = new();
    private Task asrTransition = Task.CompletedTask;
    private AudioSourceStatus asrStatus = new(AudioSourceHealth.Stopped, "Local ASR stopped");
    private bool asrDesired;
    private long asrGeneration;
    private long activeAsrGeneration;
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
        session = new LyricsSession(new LyricsResolver([new LrcLibSource(client), new NetEaseSource(client)], new LyricsCache(Path.Combine(directory, "lyrics-v1"), log), log, readings.Romanize), log);
        offsets = new(Path.Combine(directory, "song-offsets.json"), log);
        metadata = new(overlay.Dispatcher, log);
        metadata.Changed += OnMedia;
        session.Changed += Refresh;
        string modelPath = FindModelPath(directory);
        asr = new AudioTranscriptionSession(new WasapiLoopbackAudioSource(), new WhisperTranscriber(modelPath, log), log);
        asr.Transcript += OnTranscript;
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
        latest = snapshot;
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
        if (asrStatus.Health is AudioSourceHealth.Faulted or AudioSourceHealth.Unavailable) RestartAsr();
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
        var decision = router.Evaluate(false, session.Timeline is not null, structured, audioStatus.Health, now);
        SetAsrDesired(decision.ShouldRunAsr);
        status(SourceStatus(decision, audioStatus));
        if (decision.Frame != rendered) { learning.RenderFrame(decision.Frame); rendered = decision.Frame; }
        ScheduleSoonest(session.NextDelay(offset), decision.NextEvaluation);
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
        _ when audioStatus.Health is AudioSourceHealth.Unavailable or AudioSourceHealth.Faulted => audioStatus.Message,
        _ => session.Status
    };

    private void ScheduleSoonest(TimeSpan? first, TimeSpan? second)
    {
        TimeSpan? delay = first is null ? second : second is null || first <= second ? first : second;
        if (delay is null) return;
        timer.Interval = delay < TimeSpan.FromMilliseconds(15) ? TimeSpan.FromMilliseconds(15) : delay.Value;
        timer.Start();
    }

    private void OnTranscript(TranscriptionSegment value)
    {
        long generation; lock (asrGate) generation = activeAsrGeneration;
        _ = overlay.Dispatcher.BeginInvoke(new Action(() =>
        {
            lock (asrGate) { if (disposed || !asrDesired || generation != asrGeneration) return; }
            router.AcceptTranscript(value, Stopwatch.GetTimestamp()); rendered = null; Refresh();
        }));
    }

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

    private void SetAsrDesired(bool value)
    {
        lock (asrGate)
        {
            if (disposed || asrDesired == value) return;
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
            else await asr.StopAsync().ConfigureAwait(false);
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

    public void Dispose()
    {
        Task transition;
        lock (asrGate) { if (disposed) return; asrDesired = false; long generation = ++asrGeneration; asrTransition = asrTransition.ContinueWith(_ => ApplyAsrTargetAsync(false, generation), TaskScheduler.Default).Unwrap(); transition = asrTransition; }
        lifetime.Cancel();
        try { transition.Wait(TimeSpan.FromSeconds(4)); } catch (AggregateException ex) { log($"ASR shutdown transition: {ex.GetBaseException().Message}"); }
        asr.Transcript -= OnTranscript; asr.StatusChanged -= OnAsrStatus;
        try { asr.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(4)); } catch (AggregateException ex) { log($"ASR shutdown: {ex.GetBaseException().Message}"); }
        lock (asrGate) disposed = true;
        learning.Dispose(); timer.Stop(); metadata.Dispose(); session.Dispose(); http.Dispose(); lifetime.Dispose(); _ = Task.Run(readings.Dispose);
    }
}
