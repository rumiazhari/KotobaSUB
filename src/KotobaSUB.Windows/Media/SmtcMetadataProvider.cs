using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Media.Control;

namespace KotobaSUB.Windows.Media;

internal sealed class SmtcMetadataProvider(Dispatcher dispatcher, Action<string> log) : IMediaMetadataProvider
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private CancellationTokenSource? metadataRead;
    private MediaTrack? track;
    private MediaSnapshot? last;
    private DateTimeOffset lastTimelineUpdate;
    private long generation;
    private bool disposed;
    public event Action<MediaSnapshot?>? Changed;
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken);
        if (disposed) return;
        manager.CurrentSessionChanged += CurrentChanged;
        manager.SessionsChanged += SessionsChanged;
        RefreshSession();
    }
    private void Post(Action action)
    {
        if (!disposed && !dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(new Action(() => { if (!disposed) action(); }));
    }
    private void CurrentChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => Post(RefreshSession);
    private void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => Post(RefreshSession);
    private void MediaChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => Post(() => { if (sender == session) ReadMetadata(); });
    private void TimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => Post(() => { if (sender == session) PublishState(); });
    private void PlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => Post(() => { if (sender == session) PublishState(); });
    private void Detach()
    {
        if (session is null) return;
        session.MediaPropertiesChanged -= MediaChanged;
        session.TimelinePropertiesChanged -= TimelineChanged;
        session.PlaybackInfoChanged -= PlaybackChanged;
    }
    private void RefreshSession()
    {
        try
        {
            var next = manager?.GetCurrentSession();
            if (next == session) return;
            Detach(); session = next; track = null; last = null;
            generation++; metadataRead?.Cancel();
            Changed?.Invoke(null);
            if (session is null) { log("SMTC: no active session"); return; }
            session.MediaPropertiesChanged += MediaChanged;
            session.TimelinePropertiesChanged += TimelineChanged;
            session.PlaybackInfoChanged += PlaybackChanged;
            ReadMetadata();
        }
        catch (Exception ex) { log($"SMTC session read failed: {ex.Message}"); Changed?.Invoke(null); }
    }
    private void ReadMetadata()
    {
        metadataRead?.Cancel();
        var cancellation = new CancellationTokenSource(); metadataRead = cancellation;
        long expected = ++generation;
        track = null; last = null; Changed?.Invoke(null);
        _ = ReadMetadataAsync(session!, expected, cancellation);
    }
    private async Task ReadMetadataAsync(GlobalSystemMediaTransportControlsSession source, long expected, CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.CancelAfter(TimeSpan.FromSeconds(5));
            var properties = await source.TryGetMediaPropertiesAsync().AsTask(cancellation.Token);
            if (disposed || expected != generation || source != session) return;
            var timing = source.GetTimelineProperties();
            track = new(source.SourceAppUserModelId, properties.Title, properties.Artist, properties.AlbumTitle, timing.EndTime - timing.StartTime);
            log($"SMTC: app={track.AppId}; title={track.Title}; artist={track.Artist}; duration={track.Duration.TotalSeconds:F1}");
            PublishState();
        }
        catch (OperationCanceledException) { if (!disposed && expected == generation) log("SMTC metadata read cancelled or timed out"); }
        catch (Exception ex) { log($"SMTC metadata failed: {ex.Message}"); if (!disposed && expected == generation) Changed?.Invoke(null); }
        finally { if (metadataRead == cancellation) metadataRead = null; cancellation.Dispose(); }
    }
    private void PublishState()
    {
        if (track is null || session is null) return;
        try
        {
            var timing = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            bool usable = playing || playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
            if (!usable) { last = null; Changed?.Invoke(null); return; }
            double rate = playback.PlaybackRate ?? 1;
            if (!double.IsFinite(rate) || rate <= 0) rate = 1;
            long now = Stopwatch.GetTimestamp();
            TimeSpan position;
            if (last is not null && timing.LastUpdatedTime == lastTimelineUpdate)
                position = last.PositionAt(now); // pause/resume must not extrapolate the old UTC anchor again
            else
            {
                position = timing.Position - timing.StartTime;
                if (playing)
                {
                    double age = (DateTimeOffset.UtcNow - timing.LastUpdatedTime).TotalSeconds;
                    if (age >= 0 && age < 21600) position += TimeSpan.FromSeconds(age * rate);
                }
            }
            track = track with { Duration = timing.EndTime > timing.StartTime ? timing.EndTime - timing.StartTime : TimeSpan.Zero };
            lastTimelineUpdate = timing.LastUpdatedTime;
            last = new MediaSnapshot(track, position < TimeSpan.Zero ? TimeSpan.Zero : position, playing, rate, now);
            Changed?.Invoke(last);
        }
        catch (Exception ex) { log($"SMTC timeline failed: {ex.Message}"); last = null; Changed?.Invoke(null); }
    }
    public void Dispose()
    {
        disposed = true; generation++; metadataRead?.Cancel();
        Detach();
        if (manager is not null) { manager.CurrentSessionChanged -= CurrentChanged; manager.SessionsChanged -= SessionsChanged; }
    }
}
