namespace KotobaSUB.Core.Audio;

public enum AudioSourceHealth { Stopped, Starting, Running, Unavailable, Faulted }
public sealed record AudioSourceStatus(AudioSourceHealth Health, string Message, Exception? Error = null);
public sealed record AudioBlock(float[] Samples, int SampleRate, long FirstSample = -1);
public sealed record SpeechWindow(float[] Samples, long FirstSample, bool FinalAfterSilence)
{
    public TimeSpan Start => TimeSpan.FromSeconds(FirstSample / 16000d);
    public TimeSpan End => TimeSpan.FromSeconds((FirstSample + Samples.LongLength) / 16000d);
}
public sealed record TranscriptionSegment(string Text, TimeSpan Start, TimeSpan End, float Probability, float NoSpeechProbability, bool Final = false);

public interface IAudioSource : IAsyncDisposable
{
    AudioSourceStatus Status { get; }
    long DroppedBlocks { get; }
    event Action<AudioSourceStatus>? StatusChanged;
    Task StartAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<AudioBlock> ReadAllAsync(CancellationToken cancellationToken);
    Task StopAsync();
}

public interface ITranscriptionProvider : IAsyncDisposable
{
    Task<IReadOnlyList<TranscriptionSegment>> TranscribeAsync(SpeechWindow window, CancellationToken cancellationToken);
}
