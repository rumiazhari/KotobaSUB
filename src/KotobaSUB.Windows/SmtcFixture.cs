using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace KotobaSUB.Windows;

// A generated, muted WAV is used only by --session-fixture. It exercises the
// operating system's real media-session events without controlling other apps.
internal sealed class SmtcFixture : IDisposable
{
    private readonly global::Windows.Media.Playback.MediaPlayer player = new();
    private readonly string path;
    public const string Title = "KotobaSUB SMTC 検証";
    public SmtcFixture(string directory)
    {
        path = Path.Combine(directory, "silence.wav");
        using var writer = new BinaryWriter(File.Create(path));
        int bytes = 16000 * 2 * 30;
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes); writer.Write(new byte[bytes]);
    }
    public async Task StartAsync(CancellationToken token)
    {
        player.IsMuted = true; player.CommandManager.IsEnabled = false;
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.MediaOpened += (_, _) => opened.TrySetResult();
        player.MediaFailed += (_, e) => opened.TrySetException(new InvalidOperationException(e.ErrorMessage));
        player.Source = MediaSource.CreateFromUri(new Uri(path));
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(8), token);
        var controls = player.SystemMediaTransportControls;
        controls.IsEnabled = true; controls.IsPlayEnabled = true; controls.IsPauseEnabled = true;
        controls.DisplayUpdater.Type = MediaPlaybackType.Music;
        controls.DisplayUpdater.MusicProperties.Title = Title;
        controls.DisplayUpdater.MusicProperties.Artist = "KotobaSUB test fixture";
        controls.DisplayUpdater.Update();
        player.Play(); SetState(TimeSpan.Zero, true);
    }
    public void SetState(TimeSpan position, bool playing)
    {
        player.PlaybackSession.Position = position;
        if (playing) player.Play(); else player.Pause();
        player.SystemMediaTransportControls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        { StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(30), MinSeekTime = TimeSpan.Zero, MaxSeekTime = TimeSpan.FromSeconds(30), Position = position });
        player.SystemMediaTransportControls.PlaybackStatus = playing ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
    }
    public void Dispose() { player.SystemMediaTransportControls.IsEnabled = false; player.Dispose(); }
}
