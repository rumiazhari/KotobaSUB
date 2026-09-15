using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using KotobaSUB.Core.Lyrics;
using KotobaSUB.Windows.Learning;

namespace KotobaSUB.Windows.Media;

internal sealed class PlaybackController : IDisposable
{
    private readonly OverlayWindow overlay;
    private readonly Action<string> status;
    private readonly Action<string> log;
    private readonly LearningRenderer learning;
    private readonly HttpClient http = new();
    private readonly SmtcMetadataProvider metadata;
    private readonly LyricsSession session;
    private readonly SongOffsets offsets;
    private readonly DispatcherTimer timer;
    private readonly CancellationTokenSource lifetime = new();
    private bool preview;
    private bool paused;
    private bool disposed;
    private MediaSnapshot? latest;
    private SubtitleFrame? rendered;
    public PlaybackController(OverlayWindow overlay, string directory, Action<string> status, Action<string> log)
    {
        this.overlay = overlay; this.status = status; this.log = log;
        learning = new LearningRenderer(overlay, Path.Combine(directory, "annotations"), log);
        var client = new LyricsHttpClient(http);
        session = new LyricsSession(new LyricsResolver([new LrcLibSource(client), new NetEaseSource(client)], new LyricsCache(Path.Combine(directory, "lyrics-v1"), log), log), log);
        offsets = new(Path.Combine(directory, "song-offsets.json"), log);
        metadata = new(overlay.Dispatcher, log);
        metadata.Changed += OnMedia;
        session.Changed += Refresh;
        timer = new DispatcherTimer(DispatcherPriority.Background, overlay.Dispatcher);
        timer.Tick += (_, _) => { timer.Stop(); Refresh(); };
    }
    public async Task StartAsync()
    {
        try { await metadata.StartAsync(lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { log($"SMTC startup failed: {ex}"); status("Media sessions unavailable"); }
    }
    private void OnMedia(MediaSnapshot? snapshot)
    {
        latest = snapshot;
        if (!preview && !paused) session.Update(snapshot);
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
    public void Retry(bool rejectCurrent) { if (!preview && !paused) session.Retry(rejectCurrent); }
    public void AdjustSync(double seconds)
    {
        if (session.Snapshot is not { } snapshot) return;
        offsets.Set(snapshot.Track.Identity, offsets.Get(snapshot.Track.Identity) + seconds); Refresh();
    }
    private void Refresh()
    {
        if (disposed) return;
        timer.Stop();
        if (preview) { learning.RenderSample(PreviewSubtitle.Line); status("Sample preview"); return; }
        if (paused) { learning.RenderSample(null); status("Paused"); return; }
        double offset = overlay.Preferences.GlobalOffsetSeconds + (session.Snapshot is { } snapshot ? offsets.Get(snapshot.Track.Identity) : 0);
        status(session.Status);
        var frame = session.Frame(offset);
        if (frame != rendered) { learning.RenderFrame(frame); rendered = frame; }
        if (session.NextDelay(offset) is { } delay) { timer.Interval = delay; timer.Start(); }
    }
    public void SettingsChanged() { rendered = null; Refresh(); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; learning.Dispose(); timer.Stop(); lifetime.Cancel(); metadata.Dispose(); session.Dispose(); http.Dispose(); lifetime.Dispose();
    }
}
