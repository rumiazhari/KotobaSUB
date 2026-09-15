using System.Text.Json;
using KotobaSUB.Core;
using KotobaSUB.Japanese;

if (args.Length >= 3 && args[0] == "import")
{
    JmdictImporter.Import(args[1], args[2], Console.WriteLine); return 0;
}
if (args.Length >= 3 && args[0] == "analyze")
{
    using var analyzer = new JapaneseAnnotator(new IpaTokenizer(), new JmdictDictionary(args[1]));
    var line = await analyzer.AnnotateAsync(new SubtitleLine(TimeSpan.Zero, TimeSpan.FromSeconds(5), string.Join(" ", args.Skip(2)), []), CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(line, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })); return 0;
}
Console.Error.WriteLine("Usage: KotobaSUB.Data import <JMdict_e.xml|gz> <output.sqlite> | analyze <dictionary.sqlite> <Japanese text>"); return 2;
