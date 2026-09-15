using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using KotobaSUB.Core.Audio;

namespace KotobaSUB.Core.Audio;

public sealed class AudioTranscriptionSession : IAsyncDisposable
{
    private readonly IAudioSource source;
    private readonly ITranscriptionProvider transcriber;
    private readonly SpeechActivityGate activity = new();
    private readonly StableTranscript stable = new();
    private readonly Action<string> log;
    private readonly TimeSpan inactivityFlush;
    private CancellationTokenSource? lifetime;
    private Task? worker;
    private bool disposed;
    public event Action<TranscriptionSegment>? Transcript;
    public event Action<AudioCaptureObservation>? CaptureObserved;
    public event Action<AudioSourceStatus>? StatusChanged;
    public AudioSourceStatus Status { get; private set; } = new(AudioSourceHealth.Stopped, "Local ASR stopped");
    public AudioTranscriptionSession(IAudioSource source, ITranscriptionProvider transcriber, Action<string> log, TimeSpan? inactivityFlush = null)
    {
        this.source = source; this.transcriber = transcriber; this.log = log;
        this.inactivityFlush = inactivityFlush ?? TimeSpan.FromMilliseconds(650);
        if (this.inactivityFlush <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(inactivityFlush));
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
        IAsyncEnumerator<AudioBlock> blocks = source.ReadAllAsync(token).GetAsyncEnumerator(token);
        Task<bool>? pendingRead = null;
        long previousEnd = -1;
        try
        {
            while (true)
            {
                pendingRead ??= blocks.MoveNextAsync().AsTask();
                Task delay = Task.Delay(inactivityFlush, token);
                Task completed = await Task.WhenAny(pendingRead, delay).ConfigureAwait(false);
                if (completed == delay)
                {
                    await delay.ConfigureAwait(false);
                    if (activity.Flush() is { } idleWindow) await ProcessWindowAsync(idleWindow, token).ConfigureAwait(false);
                    continue;
                }
                if (!await pendingRead.ConfigureAwait(false))
                {
                    pendingRead = null;
                    if (activity.Flush() is { } finalWindow) await ProcessWindowAsync(finalWindow, token).ConfigureAwait(false);
                    break;
                }
                AudioBlock block = blocks.Current; pendingRead = null;
                long firstSample = block.FirstSample >= 0 ? block.FirstSample : previousEnd >= 0 ? previousEnd : 0;
                bool discontinuity = previousEnd >= 0 && firstSample != previousEnd;
                CaptureObserved?.Invoke(new(firstSample, firstSample + block.Samples.LongLength, Stopwatch.GetTimestamp(), discontinuity));
                previousEnd = firstSample + block.Samples.LongLength;
                foreach (var window in activity.Push(block)) await ProcessWindowAsync(window, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { log($"ASR pipeline failed: {ex}"); SetStatus(new(AudioSourceHealth.Faulted, "Local transcription failed", ex)); }
        finally
        {
            if (pendingRead is not null)
            {
                try { await pendingRead.ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            }
            await blocks.DisposeAsync().ConfigureAwait(false);
        }
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
            Transcript?.Invoke(new(text, window.Start, window.End, confidence, segments.Count == 0 ? 1 : segments.Max(s => s.NoSpeechProbability), window.FinalAfterSilence));
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
