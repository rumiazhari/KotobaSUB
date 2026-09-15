using KotobaSUB.Core;

int failures = 0;
void Test(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
string directory = Path.Combine(Path.GetTempPath(), "KotobaSUB-tests-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try
{
    LyricsTests.Run(Test, directory);
    AudioTests.Run(Test);
    AudioMediaClockTests.Run(Test);
    RoutingTests.Run(Test);
    LyricVerificationTests.Run(Test);
    JapaneseTests.Run(Test, directory, args.SkipWhile(a => a != "--dictionary").Skip(1).FirstOrDefault());
    Test("safe settings limits", () => { var s = new OverlaySettings { Width = -1, FontSize = double.NaN, Top = double.PositiveInfinity }.Validate(); Equal(320d, s.Width); Equal(36d, s.FontSize); Equal(650d, s.Top); });
    Test("settings validate monitor placement", () => { var s = new OverlaySettings { MonitorDeviceName = "  \\.\\DISPLAY2  ", MonitorRelativeLeft = 3, MonitorRelativeTop = double.NaN }.Validate(); Equal("\\.\\DISPLAY2", s.MonitorDeviceName); Equal(1d, s.MonitorRelativeLeft); Equal(0d, s.MonitorRelativeTop); });
    Test("settings roundtrip and overwrite", () => { var store = new SettingsStore(Path.Combine(directory, "settings.json"), _ => { }); var s = new OverlaySettings { Left = -1000, Furigana = false, FontSize = 42 }; store.Save(s); Equal(s, store.Load()); store.Save(s with { Gloss = false }); Equal(false, store.Load().Gloss); });
    Test("corrupt settings report recovery", () => { string path = Path.Combine(directory, "bad.json"); File.WriteAllText(path, "{bad"); var reports = new List<string>(); Equal(new OverlaySettings(), new SettingsStore(path, reports.Add).Load()); Equal(1, reports.Count); });
    Test("future schema is rejected", () => { string path = Path.Combine(directory, "future.json"); File.WriteAllText(path, "{\"Version\":99}"); var reports = new List<string>(); new SettingsStore(path, reports.Add).Load(); Equal(1, reports.Count); });
    Test("log rotates at bounded size", () => { string logs = Path.Combine(directory, "logs"); Directory.CreateDirectory(logs); File.WriteAllText(Path.Combine(logs, "kotobasub.log"), new string('x', 1_048_577)); new LocalLog(logs).Write("test"); Equal(true, File.Exists(Path.Combine(logs, "kotobasub.log.1"))); Equal(true, new FileInfo(Path.Combine(logs, "kotobasub.log")).Length < 1000); });
    Test("preview retains Japanese surface", () => Equal(PreviewSubtitle.Line.OriginalText, string.Concat(PreviewSubtitle.Line.Tokens.Select(t => t.Surface))));
}
finally { Directory.Delete(directory, true); }
Console.WriteLine($"{failures} failure(s)"); return failures == 0 ? 0 : 1;
