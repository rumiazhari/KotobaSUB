using System.Threading;
using System.Threading.Tasks;
using KotobaSUB.Core.Japanese;
using KotobaSUB.Japanese;

namespace KotobaSUB.Windows.Learning;

internal sealed class LearningRenderer : IDisposable
{
    private readonly OverlayWindow overlay;
    private readonly Action<string> log;
    private readonly Lazy<Task<JapaneseAnnotator>> analyzer;
    private CancellationTokenSource? request;
    private long generation;
    private bool disposed;
    private SubtitleFrame? source;
    private SubtitleFrame? displayed;
    internal Task Completion { get; private set; } = Task.CompletedTask;
    public LearningRenderer(OverlayWindow overlay, string cacheDirectory, Action<string> log)
    {
        this.overlay = overlay; this.log = log;
        analyzer = new(() => Task.Run(() =>
        {
            var tokenizer = new IpaTokenizer();
            IDictionaryProvider? dictionary = null;
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "jmdict.sqlite");
            try { dictionary = new JmdictDictionary(path); }
            catch (Exception ex) { log($"Offline gloss dictionary unavailable; readings remain enabled: {ex.Message}"); }
            return new JapaneseAnnotator(tokenizer, dictionary, cacheDirectory, log);
        }));
    }
    public void RenderFrame(SubtitleFrame frame)
    {
        if (disposed) return;
        if (source == frame && displayed is not null) { overlay.RenderFrame(displayed); return; }
        Cancel(); source = frame; displayed = frame; overlay.RenderFrame(frame);
        if (frame.Current is null) return;
        var cancellation = new CancellationTokenSource(); request = cancellation;
        Completion = CompleteAsync(frame, generation, cancellation);
    }
    private async Task CompleteAsync(SubtitleFrame frame, long expected, CancellationTokenSource cancellation)
    {
        try
        {
            var ready = await analyzer.Value.WaitAsync(cancellation.Token);
            var enriched = await ready.AnnotateAsync(frame.Current!, cancellation.Token);
            if (disposed || cancellation.IsCancellationRequested || expected != generation) return;
            displayed = frame with { Current = enriched }; overlay.RenderFrame(displayed);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { log($"Japanese annotation failed; retaining original text: {ex}"); }
        finally { if (request == cancellation) request = null; cancellation.Dispose(); }
    }
    public void RenderSample(SubtitleLine? line) { Cancel(); source = displayed = null; overlay.Render(line); }
    private void Cancel() { generation++; request?.Cancel(); request = null; }
    public void Dispose()
    {
        if (disposed) return; disposed = true; Cancel();
        if (analyzer.IsValueCreated)
            _ = Task.Run(async () => { try { (await analyzer.Value).Dispose(); } catch (Exception ex) { log($"Annotation shutdown: {ex.Message}"); } });
    }
}
