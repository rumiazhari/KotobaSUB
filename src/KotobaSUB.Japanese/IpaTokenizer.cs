using KotobaSUB.Core.Japanese;
using NMeCab;
using NMeCab.Specialized;

namespace KotobaSUB.Japanese;

public sealed class IpaTokenizer : IJapaneseTokenizer
{
    private readonly MeCabIpaDicTagger tagger;
    private readonly Dictionary<string, string?> lemmaReadings = new();
    private readonly object gate = new();
    private bool disposed;
    public IpaTokenizer(string? dictionaryDirectory = null) => tagger = MeCabIpaDicTagger.Create(dictionaryDirectory ?? Path.Combine(AppContext.BaseDirectory, "IpaDic"));
    public IReadOnlyList<JapaneseWord> Tokenize(string text)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (text.Length > 10000) throw new ArgumentOutOfRangeException(nameof(text), "Subtitle exceeds tokenizer limit");
            var words = new List<JapaneseWord>(); int cursor = 0;
            foreach (var node in tagger.Parse(text))
            {
                if (node.Surface.Length == 0) continue;
                int start = text.IndexOf(node.Surface, cursor, StringComparison.Ordinal);
                if (start < 0) throw new InvalidDataException("Tokenizer changed the source surface");
                if (start > cursor) words.Add(new(text[cursor..start], text[cursor..start], null, null, "空白", cursor, start - cursor, false));
                string lemma = Present(node.OriginalForm) ?? node.Surface;
                string? reading = Present(node.Reading) is { } r ? JapaneseText.Hiragana(r) : null;
                string? lemmaReading = lemma == node.Surface ? reading : LemmaReading(lemma);
                var word = new JapaneseWord(node.Surface, lemma, reading, lemmaReading, node.PartsOfSpeech, start, node.Surface.Length, node.Stat == MeCabNodeStat.Unk);
                // Auxiliary endings stay attached to their lexical verb/adjective, while
                // intervening particles/whitespace prevent an accidental merge.
                if (word.PartOfSpeech == "助動詞" && words.LastOrDefault() is { PartOfSpeech: "動詞" or "形容詞" } previous && previous.Start + previous.Length == start)
                    words[^1] = previous with { Surface = previous.Surface + word.Surface, Reading = previous.Reading is null || reading is null ? null : previous.Reading + reading, Length = previous.Length + word.Length };
                else words.Add(word);
                cursor = start + node.Surface.Length;
            }
            if (cursor < text.Length) words.Add(new(text[cursor..], text[cursor..], null, null, "空白", cursor, text.Length - cursor, false));
            return words;
        }
    }
    private string? LemmaReading(string lemma)
    {
        if (lemmaReadings.TryGetValue(lemma, out var cached)) return cached;
        var nodes = tagger.Parse(lemma);
        string? reading = nodes.Length > 0 && nodes.All(n => Present(n.Reading) is not null) ? JapaneseText.Hiragana(string.Concat(nodes.Select(n => n.Reading))) : null;
        if (lemmaReadings.Count >= 2048) lemmaReadings.Clear();
        lemmaReadings[lemma] = reading; return reading;
    }
    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) || value == "*" ? null : value;
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; tagger.Dispose(); } }
}
