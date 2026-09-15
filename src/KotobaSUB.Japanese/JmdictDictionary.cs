using System.Text.Json;
using KotobaSUB.Core.Japanese;
using Microsoft.Data.Sqlite;

namespace KotobaSUB.Japanese;

public sealed class JmdictDictionary : IDictionaryProvider
{
    private readonly SqliteConnection connection;
    private readonly object gate = new();
    public string Version { get; }
    public JmdictDictionary(string path)
    {
        connection = new(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        try
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT value FROM metadata WHERE key='schema'";
            if ((string?)command.ExecuteScalar() != JmdictImporter.Schema.ToString()) throw new InvalidDataException("Unsupported dictionary schema");
            command.CommandText = "SELECT value FROM metadata WHERE key='source_sha256'"; Version = (string?)command.ExecuteScalar() ?? throw new InvalidDataException("Missing dictionary version");
        }
        catch { connection.Dispose(); throw; }
    }
    public DictionaryEntry? Lookup(JapaneseWord word)
    {
        if (word.PartOfSpeech is "助詞" or "助動詞" or "記号" or "空白") return null;
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT f.entry_id,f.spelling,f.reading,s.glosses,s.pos,s.stagk,s.stagr
                FROM forms f JOIN senses s ON s.entry_id=f.entry_id
                WHERE f.spelling IN ($lemma,$surface) AND
                (($reading='' OR f.reading=$reading) AND f.spelling=$lemma OR ($surfaceReading='' OR f.reading=$surfaceReading) AND f.spelling=$surface)
                ORDER BY CASE WHEN f.spelling=$lemma THEN 0 ELSE 1 END,f.common DESC,s.ordinal,f.entry_id LIMIT 100
                """;
            command.Parameters.AddWithValue("$lemma", word.Lemma); command.Parameters.AddWithValue("$surface", word.Surface);
            command.Parameters.AddWithValue("$reading", word.LemmaReading ?? ""); command.Parameters.AddWithValue("$surfaceReading", word.Reading ?? "");
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string spelling = reader.GetString(1), reading = reader.GetString(2);
                string[] writtenRestrictions = Deserialize(reader.GetString(5)), readingRestrictions = Deserialize(reader.GetString(6));
                if (writtenRestrictions.Length > 0 && !writtenRestrictions.Contains(spelling)) continue;
                if (readingRestrictions.Length > 0 && !readingRestrictions.Contains(reading)) continue;
                string[] pos = Deserialize(reader.GetString(4));
                if (!Compatible(word.PartOfSpeech, pos)) continue;
                return new(reader.GetInt64(0), spelling, reading, Deserialize(reader.GetString(3)));
            }
            return null;
        }
    }
    private static string[] Deserialize(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];
    private static bool Compatible(string pos, string[] dictionaryPos)
    {
        if (dictionaryPos.Length == 0) return true;
        return pos switch
        {
            "動詞" => dictionaryPos.Any(p => p.Contains("verb", StringComparison.OrdinalIgnoreCase)),
            "形容詞" => dictionaryPos.Any(p => p.Contains("adjective", StringComparison.OrdinalIgnoreCase)),
            "名詞" => dictionaryPos.Any(p => p.Contains("noun", StringComparison.OrdinalIgnoreCase) || p.Contains("pronoun", StringComparison.OrdinalIgnoreCase)),
            _ => true
        };
    }
    public void Dispose() { lock (gate) connection.Dispose(); }
}
