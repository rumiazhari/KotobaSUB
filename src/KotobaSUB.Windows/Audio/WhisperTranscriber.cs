using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using KotobaSUB.Core.Audio;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace KotobaSUB.Windows.Audio;

internal sealed class WhisperTranscriber : ITranscriptionProvider
{
    private static readonly object runtimeGate = new();
    private readonly string modelPath;
    private readonly Action<string> log;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Timer idleTimer;
    private WhisperFactory? factory;
    private WhisperProcessor? processor;
    private DateTimeOffset lastUse;
    private bool disposed;
    public WhisperTranscriber(string modelPath, Action<string> log)
    {
        this.modelPath = modelPath; this.log = log;
        idleTimer = new(_ => _ = ReleaseIfIdleAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }
    public async Task<IReadOnlyList<TranscriptionSegment>> TranscribeAsync(SpeechWindow window, CancellationToken cancellationToken)
    {
        if (window.Samples.Length < 1600) return [];
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            EnsureLoaded(); lastUse = DateTimeOffset.UtcNow;
            var result = new List<TranscriptionSegment>(); var elapsed = Stopwatch.StartNew();
            await foreach (SegmentData segment in processor!.ProcessAsync(window.Samples, cancellationToken).ConfigureAwait(false))
                result.Add(new(segment.Text, window.Start + segment.Start, window.Start + segment.End, segment.Probability, segment.NoSpeechProbability));
            log($"ASR inference {window.Samples.Length / 16000d:F2}s audio in {elapsed.Elapsed.TotalMilliseconds:F0}ms; segments={result.Count}; confidence={(result.Count == 0 ? 0 : result.Average(s => s.Probability)):F2}");
            lastUse = DateTimeOffset.UtcNow; return result;
        }
        finally { gate.Release(); }
    }
    private void EnsureLoaded()
    {
        if (processor is not null) return;
        if (!File.Exists(modelPath)) throw new FileNotFoundException("Whisper model is not installed. Run tools/setup-model.ps1.", modelPath);
        if (new FileInfo(modelPath).Length < 1_000_000) throw new InvalidDataException("Whisper model file is incomplete");
        lock (runtimeGate) RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
        factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = false });
        processor = factory.CreateBuilder().WithLanguage("ja").WithNoContext().WithProbabilities().WithThreads(Math.Clamp(Environment.ProcessorCount - 1, 1, 8)).Build();
        lastUse = DateTimeOffset.UtcNow;
        log($"Loaded local CPU Whisper model {Path.GetFileName(modelPath)}; runtime={WhisperFactory.GetRuntimeInfo()}");
    }
    private async Task ReleaseIfIdleAsync()
    {
        if (disposed || processor is null || DateTimeOffset.UtcNow - lastUse < TimeSpan.FromMinutes(2) || !await gate.WaitAsync(0).ConfigureAwait(false)) return;
        try { if (processor is not null && DateTimeOffset.UtcNow - lastUse >= TimeSpan.FromMinutes(2)) ReleaseModel(); }
        finally { gate.Release(); }
    }
    private void ReleaseModel()
    {
        processor?.Dispose(); processor = null; factory?.Dispose(); factory = null; log("Released idle Whisper model");
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; await idleTimer.DisposeAsync().ConfigureAwait(false);
        await gate.WaitAsync().ConfigureAwait(false); try { ReleaseModel(); } finally { gate.Release(); gate.Dispose(); }
    }
}
