using System.Threading;
using System.Threading.Tasks;
using KotobaSUB.Core.Audio;

namespace KotobaSUB.Core.Audio;

public sealed class AudioTranscriptionSession : IAsyncDisposable
{
    private readonly IAudioSource source;
    private readonly ITranscriptionProvider transcriber;
    private readonly SpeechActivityGate activity = new();
    private readonly StableTranscript stable = new();
    private readonly Action<string> log;
    private CancellationTokenSource? lifetime;
    private Task? worker;
    private bool disposed;
    public event Action<TranscriptionSegment>? Transcript;
    public event Action<AudioSourceStatus>? StatusChanged;
    public AudioSourceStatus Status { get; private set; } = new(AudioSourceHealth.Stopped, "Local ASR stopped");
    public AudioTranscriptionSession(IAudioSource source, ITranscriptionProvider transcriber, Action<string> log)
    {
        this.source = source; this.transcriber = transcriber; this.log = log;
        source.StatusChanged += OnSourceStatus;
    }
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this); if (lifetime is not null) return;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try { await source.StartAsync(lifetime.Token).ConfigureAwait(false); worker = ProcessAsync(lifetime.Token); }
        catch { lifetime.Dispose(); lifetime = null; throw; }
    }
    private async Task ProcessAsync(CancellationToken token)
    {
        try
        {
            await foreach (var block in source.ReadAllAsync(token).ConfigureAwait(false))
                foreach (var window in activity.Push(block)) await ProcessWindowAsync(window, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { log($"ASR pipeline failed: {ex}"); SetStatus(new(AudioSourceHealth.Faulted, "Local transcription failed", ex)); }
    }
    private async Task ProcessWindowAsync(SpeechWindow window, CancellationToken token)
    {
        IReadOnlyList<TranscriptionSegment> segments;
        try { segments = await transcriber.TranscribeAsync(window, token).ConfigureAwait(false); }
        catch (FileNotFoundException ex) { SetStatus(new(AudioSourceHealth.Unavailable, ex.Message, ex)); lifetime?.Cancel(); return; }
        string? text = stable.Submit(segments, window.FinalAfterSilence);
        if (text is not null)
        {
            float confidence = segments.Count == 0 ? 0 : segments.Average(s => s.Probability);
            Transcript?.Invoke(new(text, window.Start, window.End, confidence, segments.Count == 0 ? 1 : segments.Max(s => s.NoSpeechProbability)));
        }
        if (window.FinalAfterSilence) stable.SilenceReset();
    }
    private void OnSourceStatus(AudioSourceStatus value) => SetStatus(value);
    private void SetStatus(AudioSourceStatus value) { Status = value; StatusChanged?.Invoke(value); }
    public async Task StopAsync()
    {
        var cancellation = lifetime; if (cancellation is null) return;
        lifetime = null; cancellation.Cancel(); await source.StopAsync().ConfigureAwait(false);
        if (worker is not null) { try { await worker.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        worker = null; cancellation.Dispose(); activity.Reset(); stable.SilenceReset();
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; source.StatusChanged -= OnSourceStatus;
        await StopAsync().ConfigureAwait(false); await transcriber.DisposeAsync().ConfigureAwait(false); await source.DisposeAsync().ConfigureAwait(false);
    }
}
