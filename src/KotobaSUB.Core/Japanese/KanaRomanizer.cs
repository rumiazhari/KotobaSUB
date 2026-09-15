using System.Text;

namespace KotobaSUB.Core.Japanese;

// Kana-only Hepburn conversion. Unknown characters (including kanji) are kept:
// this is a comparison alias, never a guessed kanji reading or translation.
public static class KanaRomanizer
{
    private static readonly Dictionary<char, string> Singles = BuildSingles();
    private static readonly Dictionary<string, string> Pairs = new()
    {
        ["きゃ"]="kya", ["きゅ"]="kyu", ["きょ"]="kyo", ["ぎゃ"]="gya", ["ぎゅ"]="gyu", ["ぎょ"]="gyo",
        ["しゃ"]="sha", ["しゅ"]="shu", ["しょ"]="sho", ["じゃ"]="ja", ["じゅ"]="ju", ["じょ"]="jo",
        ["ちゃ"]="cha", ["ちゅ"]="chu", ["ちょ"]="cho", ["にゃ"]="nya", ["にゅ"]="nyu", ["にょ"]="nyo",
        ["ひゃ"]="hya", ["ひゅ"]="hyu", ["ひょ"]="hyo", ["びゃ"]="bya", ["びゅ"]="byu", ["びょ"]="byo",
        ["ぴゃ"]="pya", ["ぴゅ"]="pyu", ["ぴょ"]="pyo", ["みゃ"]="mya", ["みゅ"]="myu", ["みょ"]="myo",
        ["りゃ"]="rya", ["りゅ"]="ryu", ["りょ"]="ryo", ["てぃ"]="ti", ["でぃ"]="di", ["とぅ"]="tu", ["どぅ"]="du",
        ["ふぁ"]="fa", ["ふぃ"]="fi", ["ふぇ"]="fe", ["ふぉ"]="fo", ["しぇ"]="she", ["ちぇ"]="che", ["じぇ"]="je",
        ["うぃ"]="wi", ["うぇ"]="we", ["うぉ"]="wo", ["ゔぁ"]="va", ["ゔぃ"]="vi", ["ゔぇ"]="ve", ["ゔぉ"]="vo"
    };
    private static Dictionary<char, string> BuildSingles()
    {
        string kana = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをんがぎぐげござじずぜぞだぢづでどばびぶべぼぱぴぷぺぽぁぃぅぇぉゃゅょゔゐゑ";
        string[] latin = "a i u e o ka ki ku ke ko sa shi su se so ta chi tsu te to na ni nu ne no ha hi fu he ho ma mi mu me mo ya yu yo ra ri ru re ro wa o n ga gi gu ge go za ji zu ze zo da ji zu de do ba bi bu be bo pa pi pu pe po a i u e o ya yu yo vu i e".Split(' ');
        return kana.Select((c, i) => (c, latin[i])).ToDictionary(x => x.c, x => x.Item2);
    }
    public static string Convert(string input)
    {
        string kana = string.Concat(input.Normalize(NormalizationForm.FormKC).Select(c => c >= '\u30a1' && c <= '\u30f6' ? (char)(c - 0x60) : c));
        var result = new StringBuilder();
        for (int i = 0; i < kana.Length; i++)
        {
            string Syllable(int at) => at + 1 < kana.Length && Pairs.TryGetValue(kana.Substring(at, 2), out var pair) ? pair : Singles.GetValueOrDefault(kana[at], kana[at].ToString());
            char c = kana[i];
            if (c == 'っ' && i + 1 < kana.Length)
            {
                string next = Syllable(i + 1);
                if (next.StartsWith("ch", StringComparison.Ordinal)) { result.Append('t'); continue; }
                if (next.Length > 0 && "bcdfghjklmpqrstvwxyz".Contains(next[0])) { result.Append(next[0]); continue; }
            }
            if (c == 'ー' && result.Length > 0 && "aeiou".Contains(result[^1])) { result.Append(result[^1]); continue; }
            string syllable = Syllable(i);
            if (c == 'ん' && i + 1 < kana.Length && "aeiouy".Contains(Syllable(i + 1)[0])) syllable = "n'";
            result.Append(syllable);
            if (i + 1 < kana.Length && Pairs.ContainsKey(kana.Substring(i, 2))) i++;
        }
        return result.ToString();
    }
}
