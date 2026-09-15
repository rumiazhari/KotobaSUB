using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using KotobaSUB.Core.Japanese;

namespace KotobaSUB.Japanese;

public static class JmdictImporter
{
    public const int Schema = 1;
    public static int Import(string source, string destination, Action<string>? progress = null, CancellationToken token = default)
    {
        source = Path.GetFullPath(source); destination = Path.GetFullPath(destination);
        if (source == destination) throw new ArgumentException("Source and destination must differ");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        int count;
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
            {
                connection.Open();
                Execute(connection, """
                    PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;
                    CREATE TABLE metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                    CREATE TABLE forms(entry_id INTEGER NOT NULL, spelling TEXT NOT NULL, reading TEXT NOT NULL, common INTEGER NOT NULL, PRIMARY KEY(entry_id, spelling, reading));
                    CREATE TABLE senses(entry_id INTEGER NOT NULL, ordinal INTEGER NOT NULL, glosses TEXT NOT NULL, pos TEXT NOT NULL, stagk TEXT NOT NULL, stagr TEXT NOT NULL, PRIMARY KEY(entry_id, ordinal));
                    """);
                using var transaction = connection.BeginTransaction();
                using var form = connection.CreateCommand(); form.Transaction = transaction;
                form.CommandText = "INSERT OR IGNORE INTO forms VALUES($id,$spelling,$reading,$common)";
                foreach (string p in new[] { "$id", "$spelling", "$reading", "$common" }) form.Parameters.Add(new SqliteParameter(p, "")); form.Prepare();
                using var sense = connection.CreateCommand(); sense.Transaction = transaction;
                sense.CommandText = "INSERT INTO senses VALUES($id,$ordinal,$glosses,$pos,$stagk,$stagr)";
                foreach (string p in new[] { "$id", "$ordinal", "$glosses", "$pos", "$stagk", "$stagr" }) sense.Parameters.Add(new SqliteParameter(p, "")); sense.Prepare();
                using var file = File.OpenRead(source);
                using Stream content = source.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(file, CompressionMode.Decompress) : file;
                using var xml = XmlReader.Create(content, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null, MaxCharactersFromEntities = 64_000_000, MaxCharactersInDocument = 512_000_000, IgnoreComments = true });
                count = 0;
                while (xml.Read())
                {
                    if (xml.NodeType != XmlNodeType.Element || xml.Name != "entry") continue;
                    token.ThrowIfCancellationRequested();
                    using var subtree = xml.ReadSubtree(); var entry = XElement.Load(subtree);
                    long id = (long?)entry.Element("ent_seq") ?? throw new InvalidDataException("JMdict entry missing ID");
                    var kanji = entry.Elements("k_ele").ToArray();
                    foreach (var re in entry.Elements("r_ele"))
                    {
                        string reading = JapaneseText.Hiragana((string?)re.Element("reb") ?? "");
                        if (reading.Length == 0) continue;
                        var restrictions = re.Elements("re_restr").Select(e => e.Value).ToHashSet();
                        bool readingCommon = re.Elements("re_pri").Any();
                        void InsertForm(string spelling, bool common)
                        {
                            form.Parameters["$id"].Value = id; form.Parameters["$spelling"].Value = spelling; form.Parameters["$reading"].Value = reading; form.Parameters["$common"].Value = common ? 1 : 0; form.ExecuteNonQuery();
                        }
                        // Kana lookup remains available even when written forms also exist.
                        InsertForm((string?)re.Element("reb") ?? reading, readingCommon);
                        if (re.Element("re_nokanji") is null)
                            foreach (var ke in kanji)
                            {
                                string spelling = (string?)ke.Element("keb") ?? "";
                                if (spelling.Length > 0 && (restrictions.Count == 0 || restrictions.Contains(spelling))) InsertForm(spelling, readingCommon || ke.Elements("ke_pri").Any());
                            }
                    }
                    string[] inheritedPos = []; int ordinal = 0;
                    foreach (var se in entry.Elements("sense"))
                    {
                        var pos = se.Elements("pos").Select(e => e.Value).ToArray(); if (pos.Length > 0) inheritedPos = pos;
                        var glosses = se.Elements("gloss").Where(g => (string?)g.Attribute(XNamespace.Xml + "lang") is null or "eng").Select(g => g.Value).Where(g => !string.IsNullOrWhiteSpace(g)).ToArray();
                        if (glosses.Length == 0) { ordinal++; continue; }
                        sense.Parameters["$id"].Value = id; sense.Parameters["$ordinal"].Value = ordinal++;
                        sense.Parameters["$glosses"].Value = JsonSerializer.Serialize(glosses); sense.Parameters["$pos"].Value = JsonSerializer.Serialize(inheritedPos);
                        sense.Parameters["$stagk"].Value = JsonSerializer.Serialize(se.Elements("stagk").Select(e => e.Value).ToArray());
                        sense.Parameters["$stagr"].Value = JsonSerializer.Serialize(se.Elements("stagr").Select(e => JapaneseText.Hiragana(e.Value)).ToArray());
                        sense.ExecuteNonQuery();
                    }
                    count++; if (count % 25000 == 0) progress?.Invoke($"Indexed {count} entries");
                }
                if (count == 0) throw new InvalidDataException("No JMdict entries found");
                transaction.Commit();
                Execute(connection, "CREATE INDEX forms_spelling_reading ON forms(spelling, reading, common DESC); PRAGMA user_version=1;");
                using var metadata = connection.CreateCommand();
                metadata.CommandText = "INSERT INTO metadata VALUES('schema',$schema),('source_sha256',$hash),('imported_utc',$date),('entries',$count),('license','CC-BY-SA-4.0; EDRDG'),('source','https://www.edrdg.org/pub/Nihongo/JMdict_e.gz')";
                metadata.Parameters.AddWithValue("$schema", Schema.ToString());
                using var hashSource = File.OpenRead(source); metadata.Parameters.AddWithValue("$hash", Convert.ToHexString(SHA256.HashData(hashSource)));
                metadata.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.ToString("O")); metadata.Parameters.AddWithValue("$count", count.ToString()); metadata.ExecuteNonQuery();
                using var integrity = connection.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check";
                if ((string?)integrity.ExecuteScalar() != "ok") throw new InvalidDataException("Dictionary index failed integrity check");
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, destination, true);
            progress?.Invoke($"Completed {count} entries"); return count;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Execute(SqliteConnection connection, string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
}
