namespace KotobaSUB.Core;

public enum TimingPrecision { Segment, Line, Word }
public sealed record LearningToken(string Surface, string Lemma, string? Reading, string PartOfSpeech, IReadOnlyList<string> Glosses);
public sealed record SubtitleLine(TimeSpan Start, TimeSpan End, string OriginalText, IReadOnlyList<LearningToken> Tokens, string? Translation = null, TimingPrecision Precision = TimingPrecision.Line);
public interface ISubtitleRenderer { void Render(SubtitleLine? line); }

// Explicit preview fixture. This is neither recognition nor dictionary output.
public static class PreviewSubtitle
{
    public static SubtitleLine Line { get; } = new(TimeSpan.Zero, TimeSpan.FromSeconds(5), "私は明日学校に行きます", [
        new("私", "私", "わたし", "noun", ["I"]), new("は", "は", null, "particle", []),
        new("明日", "明日", "あした", "noun", ["tomorrow"]), new("学校", "学校", "がっこう", "noun", ["school"]),
        new("に", "に", null, "particle", []), new("行きます", "行く", "いきます", "verb", ["go"])
    ], "I will go to school tomorrow.");
}
