using KotobaSUB.Core;
using KotobaSUB.Core.Adapted.FlyingLyrics;
using KotobaSUB.Core.Lyrics;
using KotobaSUB.Core.Japanese;
using KotobaSUB.Japanese;
using System.Security.Cryptography;

internal static class JapaneseTests
{
    private sealed class ReadingSource : ILyricsSource
    {
        public string Name => "fixture";
        public List<string> Queries { get; } = new();
        public Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string query, CancellationToken token)
        {
            Queries.Add(query);
            IReadOnlyList<LyricsCandidate> result = query == "kashu yorunikakeru" ? [new(Name, "reading", "Yoru ni Kakeru", "kashu", TimeSpan.FromSeconds(200), "[00:01]夜")] : [];
            return Task.FromResult(result);
        }
        public Task<LyricsCandidate> FetchAsync(LyricsCandidate candidate, CancellationToken token) => Task.FromResult(candidate);
    }
    private static void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
    public static void Run(Action<string, Action> test, string directory, string? fullDictionary)
    {
        using var tokenizer = new IpaTokenizer();
        test("real metadata readings allow exact cross-script identity with hard gates", () =>
        {
            using var readings = new MetadataReadings();
            Check(readings.Romanize("夜に駆ける") == "yorunikakeru");
            Check(readings.Romanize("私は学校へ") == "watashiwagakkoue");
            Check(readings.Romanize("𠮷𠮷𠮷") is null);
            var track = new MediaTrack("fixture", "夜に駆ける", "歌手", "", TimeSpan.FromSeconds(200));
            bool Match(string title, string artist = "kashu", int seconds = 200) => MetadataMatching.Evaluate(track, title, artist, TimeSpan.FromSeconds(seconds), readings.Romanize).Accepted;
            Check(Match("Yoru ni Kakeru"));
            Check(!Match("Yoru ni Kakeru", "other") && !Match("Yoru ni Kakeru", seconds: 240));
            Check(!Match("Yoru ni Kakeru (Live)"));
            Check(!Match("Yoru ni Kakeru extra"));
            var homophone = track with { Title = "橋", Artist = "YOASOBI" };
            Check(!MetadataMatching.Evaluate(homophone, "箸", "YOASOBI", track.Duration, readings.Romanize).Accepted);
            Check(MetadataMatching.Evaluate(track with { Title = "Yoru ni Kakeru", Artist = "kashu" }, "夜に駆ける", "歌手", track.Duration, readings.Romanize).Accepted);
        });
        test("metadata resolver uses bounded local aliases and revalidates cache on worker", () =>
        {
            using var readings = new MetadataReadings();
            int caller = Environment.CurrentManagedThreadId;
            string? Alias(string text) { Check(Environment.CurrentManagedThreadId != caller, "analysis must leave calling thread"); return readings.Romanize(text); }
            var track = new MediaTrack("fixture", "夜に駆ける", "歌手", "", TimeSpan.FromSeconds(200));
            var source = new ReadingSource();
            var cache = new LyricsCache(Path.Combine(directory, "reading-lyrics"), _ => { });
            var resolver = new LyricsResolver([source], cache, _ => { }, Alias);
            var found = resolver.ResolveAsync(track, new HashSet<string>(), false, CancellationToken.None).GetAwaiter().GetResult();
            Check(found?.Id == "reading" && source.Queries.Count <= 3);
            Check(source.Queries[0] == "歌手 夜に駆ける" && source.Queries.Contains("kashu yorunikakeru"));
            int searches = source.Queries.Count;
            Check(resolver.ResolveAsync(track, new HashSet<string>(), false, CancellationToken.None).GetAwaiter().GetResult()?.Id == "reading");
            Check(source.Queries.Count == searches);
            Check(cache.Load(track) is null);
            Check(cache.Load(track with { Duration = TimeSpan.FromSeconds(250) }, readings.Romanize) is null);
        });
        test("real IPADIC preserves source and joins inflected auxiliary endings", () =>
        {
            var words = tokenizer.Tokenize("私は明日学校に行きます");
            Check(words.Count == 6); Check(string.Concat(words.Select(w => w.Surface)) == "私は明日学校に行きます");
            var verb = words[^1]; Check(verb.Surface == "行きます" && verb.Lemma == "行く" && verb.Reading == "いきます" && verb.LemmaReading == "いく");
            Check(words.Single(w => w.Surface == "学校").Reading == "がっこう");
            var past = tokenizer.Tokenize("食べました"); Check(past.Count == 1 && past[0].Lemma == "食べる" && past[0].Reading == "たべました");
        });
        test("IPADIC preserves whitespace punctuation and unknown surfaces", () =>
        {
            string text = "  私は\n未知XYZ🙂に 会います。";
            var words = tokenizer.Tokenize(text); Check(string.Concat(words.Select(w => w.Surface)) == text);
            Check(words.All(w => text.Substring(w.Start, w.Length) == w.Surface));
            Check(!JapaneseText.HasKanji("こんにちは") && !JapaneseText.HasKanji("コトハ") && JapaneseText.HasKanji("行きます"));
        });
        string xmlPath = Path.Combine(directory, "jmdict-fixture.xml"), dbPath = Path.Combine(directory, "dictionary.sqlite");
        File.WriteAllText(xmlPath, """
            <?xml version="1.0"?>
            <!DOCTYPE JMdict [<!ENTITY n "noun (common) (futsuumeishi)"><!ENTITY v "Godan verb - Iku/Yuku special class">]>
            <JMdict>
              <entry><ent_seq>1</ent_seq><k_ele><keb>生</keb></k_ele><k_ele><keb>生え</keb></k_ele>
                <r_ele><reb>なま</reb><re_restr>生</re_restr></r_ele><r_ele><reb>せい</reb><re_restr>生</re_restr></r_ele><r_ele><reb>はえ</reb><re_restr>生え</re_restr></r_ele>
                <sense><stagk>生</stagk><stagr>せい</stagr><pos>&n;</pos><gloss>life</gloss></sense>
                <sense><stagk>生</stagk><stagr>なま</stagr><gloss>raw</gloss></sense>
                <sense><stagk>生え</stagk><stagr>はえ</stagr><gloss>growth</gloss></sense>
              </entry>
              <entry><ent_seq>2</ent_seq><k_ele><keb>行く</keb><ke_pri>news1</ke_pri></k_ele><r_ele><reb>いく</reb></r_ele>
                <sense><pos>&v;</pos><gloss>to go</gloss><gloss>to move</gloss><gloss xml:lang="ger">gehen</gloss></sense>
              </entry>
              <entry><ent_seq>3</ent_seq><k_ele><keb>架空表記</keb></k_ele><r_ele><reb>かな</reb><re_nokanji/></r_ele><sense><pos>&n;</pos><gloss>kana</gloss></sense></entry>
            </JMdict>
            """);
        test("JMdict importer builds indexed reading and sense restrictions", () =>
        {
            Check(JmdictImporter.Import(xmlPath, dbPath) == 3);
            using var dictionary = new JmdictDictionary(dbPath);
            JapaneseWord Word(string surface, string reading) => new(surface, surface, reading, reading, "名詞", 0, surface.Length, false);
            Check(dictionary.Lookup(Word("生", "なま"))?.Glosses[0] == "raw");
            Check(dictionary.Lookup(Word("生", "せい"))?.Glosses[0] == "life");
            Check(dictionary.Lookup(Word("生え", "はえ"))?.Glosses[0] == "growth");
            Check(dictionary.Lookup(Word("生え", "なま")) is null);
            Check(dictionary.Lookup(Word("架空表記", "かな")) is null);
            Check(dictionary.Lookup(Word("かな", "かな"))?.Glosses[0] == "kana");
            var entry = dictionary.Lookup(new("行きます", "行く", "いきます", "いく", "動詞", 0, 4, false));
            Check(entry?.Glosses.SequenceEqual(new[] { "to go", "to move" }) == true);
        });
        test("failed or cancelled dictionary import preserves previous index", () =>
        {
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dbPath)));
            string broken = Path.Combine(directory, "broken.xml"); File.WriteAllText(broken, "<JMdict><entry>");
            try { JmdictImporter.Import(broken, dbPath); throw new Exception("Expected malformed XML failure"); } catch (System.Xml.XmlException) { }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { JmdictImporter.Import(xmlPath, dbPath, token: cancelled.Token); throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dbPath))) == hash);
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0);
        });
        test("annotation caches real analysis and preserves timing without translating", () =>
        {
            string cache = Path.Combine(directory, "annotations");
            var line = new SubtitleLine(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(7), "行きます", [], "supplied translation");
            using (var analyzer = new JapaneseAnnotator(new IpaTokenizer(), new JmdictDictionary(dbPath), cache))
            {
                var result = analyzer.AnnotateAsync(line, CancellationToken.None).GetAwaiter().GetResult();
                Check(result.Start == line.Start && result.End == line.End && result.Translation == line.Translation);
                Check(result.Tokens.Single().Glosses.SequenceEqual(new[] { "go" }));
                var cached = analyzer.AnnotateAsync(line with { Start = TimeSpan.FromSeconds(20) }, CancellationToken.None).GetAwaiter().GetResult();
                Check(cached.Start == TimeSpan.FromSeconds(20) && ReferenceEquals(result.Tokens, cached.Tokens));
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                try { analyzer.AnnotateAsync(line, cancelled.Token).GetAwaiter().GetResult(); throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
            }
            Check(Directory.GetFiles(cache, "*.json").Length == 1);
            using var loaded = new JapaneseAnnotator(new IpaTokenizer(), new JmdictDictionary(dbPath), cache);
            Check(loaded.AnnotateAsync(line, CancellationToken.None).GetAwaiter().GetResult().Tokens[0].Glosses[0] == "go");
        });
        test("missing lexical dictionary still produces real readings", () =>
        {
            using var analyzer = new JapaneseAnnotator(new IpaTokenizer(), null);
            var result = analyzer.AnnotateAsync(new(TimeSpan.Zero, TimeSpan.FromSeconds(5), "学校", []), CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Tokens[0].Reading == "がっこう" && result.Tokens[0].Glosses.Count == 0);
        });
        if (fullDictionary is not null)
            test("full JMdict and IPADIC produce requested learning example", () =>
            {
                using var analyzer = new JapaneseAnnotator(new IpaTokenizer(), new JmdictDictionary(fullDictionary));
                var result = analyzer.AnnotateAsync(new(TimeSpan.Zero, TimeSpan.FromSeconds(5), "私は明日学校に行きます", []), CancellationToken.None).GetAwaiter().GetResult();
                Check(result.Tokens.Count == 6); Check(result.Tokens[0].Glosses[0] == "I");
                Check(result.Tokens[2].Glosses[0] == "tomorrow" && result.Tokens[3].Glosses[0] == "school" && result.Tokens[5].Glosses[0] == "go");
                Check(result.Tokens[1].Glosses.Count == 0 && result.Tokens[4].Glosses.Count == 0);
            });
        else Console.WriteLine("SKIP full JMdict integration (pass --dictionary <path>)");
    }
}
