using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KotobaSUB.Core;
using KotobaSUB.Core.Japanese;

namespace KotobaSUB.Japanese;

public sealed class JapaneseAnnotator : ISubtitleAnnotator
{
    private readonly IJapaneseTokenizer tokenizer;
    private readonly IDictionaryProvider? dictionary;
    private readonly string? cacheDirectory;
    private readonly Action<string> log;
    private readonly Dictionary<string, LearningToken[]> memory = new();
    private readonly object gate = new();
    private bool disposed;
    private readonly string version;
    public bool HasDictionary => dictionary is not null;
    public JapaneseAnnotator(IJapaneseTokenizer tokenizer, IDictionaryProvider? dictionary, string? cacheDirectory = null, Action<string>? log = null)
    {
        this.tokenizer = tokenizer; this.dictionary = dictionary; this.cacheDirectory = cacheDirectory; this.log = log ?? (_ => { });
        version = "ipa-0.10.0-nmecab-0.10.2-annotation-1:" + (dictionary?.Version ?? "no-dictionary");
    }
    public Task<SubtitleLine> AnnotateAsync(SubtitleLine line, CancellationToken cancellationToken) => Task.Run(() =>
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this); cancellationToken.ThrowIfCancellationRequested();
            if (memory.TryGetValue(line.OriginalText, out var remembered)) return line with { Tokens = remembered };
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version + "\0" + line.OriginalText)));
            var cached = Load(key, line.OriginalText);
            if (cached is not null) { Remember(line.OriginalText, cached); return line with { Tokens = cached }; }
            var tokens = new List<LearningToken>();
            foreach (var word in tokenizer.Tokenize(line.OriginalText))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = dictionary?.Lookup(word);
                string[] glosses = entry?.Glosses.Take(1).Select(Concise).ToArray() ?? [];
                tokens.Add(new(word.Surface, word.Lemma, word.Reading, word.PartOfSpeech, glosses));
            }
            if (string.Concat(tokens.Select(t => t.Surface)) != line.OriginalText) throw new InvalidDataException("Annotation changed subtitle text");
            cancellationToken.ThrowIfCancellationRequested();
            var result = tokens.ToArray(); Remember(line.OriginalText, result); Save(key, result);
            return line with { Tokens = result };
        }
    }, cancellationToken);
    private static string Concise(string gloss)
    {
        string value = gloss.StartsWith("to ", StringComparison.Ordinal) ? gloss[3..] : gloss;
        if (value.Length <= 48) return value;
        int boundary = value.LastIndexOf(' ', 45); return value[..(boundary > 15 ? boundary : 45)] + "…";
    }
    private void Remember(string text, LearningToken[] tokens)
    {
        if (memory.Count >= 512) memory.Remove(memory.Keys.First()); memory[text] = tokens;
    }
    private LearningToken[]? Load(string key, string text)
    {
        if (cacheDirectory is null) return null;
        string path = Path.Combine(cacheDirectory, key + ".json"); if (!File.Exists(path)) return null;
        try
        {
            if (new FileInfo(path).Length > 1_000_000) return null;
            var tokens = JsonSerializer.Deserialize<LearningToken[]>(File.ReadAllText(path));
            return tokens is not null && tokens.All(t => t is not null && t.Glosses is not null) && string.Concat(tokens.Select(t => t.Surface)) == text ? tokens : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { log($"annotation cache read failed: {ex.Message}"); return null; }
    }
    private void Save(string key, LearningToken[] tokens)
    {
        if (cacheDirectory is null) return;
        try
        {
            Directory.CreateDirectory(cacheDirectory); string path = Path.Combine(cacheDirectory, key + ".json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(tokens)); File.Move(path + ".tmp", path, true);
            foreach (var old in new DirectoryInfo(cacheDirectory).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(256)) old.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log($"annotation cache write failed: {ex.Message}"); }
    }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; tokenizer.Dispose(); dictionary?.Dispose(); } }
}
