using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace KotobaSUB.Core;

public sealed record MediaTrack(string AppId, string Title, string Artist, string Album, TimeSpan Duration)
{
    // Duration can arrive after the title. It is checked on cache reuse, not used as session identity.
    public string Identity => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\0", AppId, Title, Artist, Album))));
}
public sealed record MediaSnapshot(MediaTrack Track, TimeSpan Position, bool Playing, double Rate, long ObservedAt)
{
    public TimeSpan PositionAt(long timestamp)
    {
        double elapsed = Playing ? Math.Max(0, (timestamp - ObservedAt) / (double)Stopwatch.Frequency) * Math.Clamp(Rate, 0, 8) : 0;
        double seconds = Math.Max(0, Position.TotalSeconds + elapsed);
        if (Track.Duration > TimeSpan.Zero) seconds = Math.Min(seconds, Track.Duration.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}
public interface IMediaMetadataProvider : IDisposable
{
    event Action<MediaSnapshot?>? Changed;
    Task StartAsync(CancellationToken cancellationToken);
}
public sealed record SubtitleFrame(SubtitleLine? Current, SubtitleLine? Previous = null, SubtitleLine? Next = null);
