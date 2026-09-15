namespace KotobaSUB.Core.Japanese;

public sealed record JapaneseWord(string Surface, string Lemma, string? Reading, string? LemmaReading, string PartOfSpeech, int Start, int Length, bool Unknown);
public sealed record DictionaryEntry(long Id, string Spelling, string Reading, IReadOnlyList<string> Glosses);
public interface IJapaneseTokenizer : IDisposable
{
    IReadOnlyList<JapaneseWord> Tokenize(string text);
}
public interface IDictionaryProvider : IDisposable
{
    string Version { get; }
    DictionaryEntry? Lookup(JapaneseWord word);
}
public interface ISubtitleAnnotator : IDisposable
{
    Task<SubtitleLine> AnnotateAsync(SubtitleLine line, CancellationToken cancellationToken);
}
public static class JapaneseText
{
    public static string Hiragana(string value) => string.Concat(value.Normalize(System.Text.NormalizationForm.FormKC).Select(c => c >= '\u30a1' && c <= '\u30f6' ? (char)(c - 0x60) : c));
    public static bool HasKanji(string value) => value.EnumerateRunes().Any(r => r.Value is >= 0x3400 and <= 0x9fff or >= 0x20000 and <= 0x323af or 0x3005);
}
