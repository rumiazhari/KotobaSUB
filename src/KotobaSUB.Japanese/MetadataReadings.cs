using System.Text;
using KotobaSUB.Core.Japanese;

namespace KotobaSUB.Japanese;

public sealed class MetadataReadings(Action<string>? log = null) : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, string?> cache = new();
    private IpaTokenizer? tokenizer;
    private bool disposed;
    private bool unavailable;
    public string? Romanize(string text)
    {
        lock (gate)
        {
            if (disposed || unavailable || text.Length > 512 || !JapaneseText.HasKanji(text)) return null;
            if (cache.TryGetValue(text, out var remembered)) return remembered;
            try
            {
                tokenizer ??= new IpaTokenizer();
                var result = new StringBuilder();
                foreach (var word in tokenizer.Tokenize(text))
                {
                    if (JapaneseText.HasKanji(word.Surface) && (word.Unknown || word.Reading is null)) return Remember(text, null);
                    string reading = word.PartOfSpeech == "助詞" ? word.Surface switch { "は" => "わ", "へ" => "え", "を" => "お", _ => word.Reading ?? word.Surface } : word.Reading ?? word.Surface;
                    result.Append(KanaRomanizer.Convert(reading));
                }
                return Remember(text, result.ToString());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                unavailable = true; log?.Invoke($"Metadata readings unavailable: {ex.Message}"); return null;
            }
        }
    }
    private string? Remember(string text, string? reading)
    {
        if (cache.Count >= 256) cache.Remove(cache.Keys.First());
        cache[text] = reading; return reading;
    }
    public void Dispose() { lock (gate) { disposed = true; tokenizer?.Dispose(); tokenizer = null; cache.Clear(); } }
}
