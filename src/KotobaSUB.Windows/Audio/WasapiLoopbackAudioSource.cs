using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using KotobaSUB.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace KotobaSUB.Windows.Audio;

internal sealed class WasapiLoopbackAudioSource : IAudioSource
{
    private const int BlockSamples = 320;
    private const int Capacity = 32;
    private readonly object gate = new();
    private readonly Queue<AudioBlock> queue = new();
    private readonly SemaphoreSlim available = new(0, Capacity);
    private readonly List<float> pending = new();
    private WasapiRecorder? recorder;
    private AudioSampleConverter? converter;
    private CancellationTokenSource? captureLifetime;
    private Task? captureTask;
    private long nextSample;
    private bool manualStop;
    private bool disposed;
    public AudioSourceStatus Status { get; private set; } = new(AudioSourceHealth.Stopped, "System audio stopped");
    public long DroppedBlocks { get; private set; }
    public event Action<AudioSourceStatus>? StatusChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (recorder is not null) return Task.CompletedTask;
            queue.Clear(); while (available.Wait(0)) { } DroppedBlocks = 0;
            SetStatus(new(AudioSourceHealth.Starting, "Opening default Windows output"));
            try
            {
                recorder = new WasapiRecorderBuilder().WithLoopbackCapture().WithBufferLength(100).Build();
                WaveFormat format = recorder.WaveFormat.AsStandardWaveFormat();
                PcmSampleEncoding encoding = format.Encoding switch
                {
                    WaveFormatEncoding.IeeeFloat when format.BitsPerSample == 32 => PcmSampleEncoding.Float32,
                    WaveFormatEncoding.Pcm when format.BitsPerSample == 16 => PcmSampleEncoding.Signed16,
                    WaveFormatEncoding.Pcm when format.BitsPerSample == 24 => PcmSampleEncoding.Signed24,
                    WaveFormatEncoding.Pcm when format.BitsPerSample == 32 => PcmSampleEncoding.Signed32,
                    _ => throw new NotSupportedException($"Unsupported loopback format {format.Encoding} {format.BitsPerSample}-bit")
                };
                converter = new(format.SampleRate, format.Channels, encoding);
                captureLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                manualStop = false; nextSample = 0; pending.Clear();
                captureTask = CaptureAsync(recorder, captureLifetime.Token);
                SetStatus(new(AudioSourceHealth.Running, $"System audio: {format.SampleRate} Hz, {format.Channels} ch, {format.BitsPerSample}-bit {format.Encoding}"));
            }
            catch (Exception ex)
            {
                CleanupCapture(); SetStatus(new(AudioSourceHealth.Unavailable, "Windows system audio is unavailable", ex)); throw;
            }
        }
        return Task.CompletedTask;
    }

    private async Task CaptureAsync(WasapiRecorder activeRecorder, CancellationToken token)
    {
        Exception? failure = null;
        try
        {
            await foreach (AudioBuffer buffer in activeRecorder.CaptureAsync(token).ConfigureAwait(false))
            {
                lock (gate)
                {
                    if (converter is null || recorder != activeRecorder) break;
                    ReadOnlySpan<byte> data = buffer.Data.Span;
                    byte[]? silence = null;
                    if ((buffer.Flags & AudioClientBufferFlags.Silent) != 0) { silence = new byte[data.Length]; data = silence; }
                    pending.AddRange(converter.Convert(data));
                    while (pending.Count >= BlockSamples)
                    {
                        var block = new float[BlockSamples]; pending.CopyTo(0, block, 0, BlockSamples); pending.RemoveRange(0, BlockSamples);
                        Enqueue(new(block, 16000, nextSample)); nextSample += BlockSamples;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { failure = ex; }
        finally
        {
            bool wakeReader = false;
            lock (gate)
            {
                if (recorder == activeRecorder)
                {
                    CleanupCapture();
                    SetStatus(failure is null || manualStop ? new(AudioSourceHealth.Stopped, "System audio stopped") : new(AudioSourceHealth.Faulted, "Windows audio device stopped", failure));
                    wakeReader = available.CurrentCount < Capacity;
                }
            }
            if (wakeReader) available.Release();
        }
    }
    private void Enqueue(AudioBlock block)
    {
        bool release = true;
        if (queue.Count >= Capacity) { queue.Dequeue(); DroppedBlocks++; release = false; }
        queue.Enqueue(block); if (release) available.Release();
    }
    public async IAsyncEnumerable<AudioBlock> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            bool ended;
            lock (gate) { ended = queue.Count == 0 && recorder is null; }
            if (ended) yield break;
            await available.WaitAsync(cancellationToken).ConfigureAwait(false);
            AudioBlock? block = null;
            lock (gate) { if (queue.Count > 0) block = queue.Dequeue(); }
            if (block is not null) yield return block;
        }
    }
    public async Task StopAsync()
    {
        Task? wait;
        lock (gate)
        {
            if (recorder is null) return;
            manualStop = true; captureLifetime?.Cancel(); wait = captureTask;
        }
        if (wait is not null) await wait.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
    }
    private void CleanupCapture()
    {
        recorder?.Dispose(); recorder = null; converter = null; pending.Clear();
        captureLifetime?.Dispose(); captureLifetime = null; captureTask = null;
    }
    private void SetStatus(AudioSourceStatus value) { Status = value; StatusChanged?.Invoke(value); }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        try { await StopAsync().ConfigureAwait(false); } catch (TimeoutException) { }
        lock (gate) { disposed = true; CleanupCapture(); queue.Clear(); }
        available.Dispose();
    }
}
