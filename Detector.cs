using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;

// Language model: a frequency dictionary plus three-letter sequence statistics
public class LangModel
{
    // A dictionary without frequencies (every word has the same weight) treats each of its words as this common
    const long TypicalCount = 3000;

    readonly Dictionary<string, long> words = new Dictionary<string, long>();
    readonly Dictionary<string, double> tri = new Dictionary<string, double>();
    readonly Dictionary<string, double> ctx = new Dictionary<string, double>();
    readonly int alphabet;

    public string Lang;
    public bool HasFrequencies = true;
    public HashSet<string> Singles = new HashSet<string>();   // one-letter words of the language

    public LangModel(IEnumerable<string> lines, int alphabetSize)
    {
        alphabet = alphabetSize + 1;
        long first = -1;
        bool allSame = true;
        foreach (string line in lines)
        {
            int sp = line.IndexOf(' ');
            if (sp <= 0) continue;
            string w = line.Substring(0, sp);
            long count;
            if (!long.TryParse(line.Substring(sp + 1), out count)) continue;
            words[w] = count;
            if (first < 0) first = count; else if (count != first) allSame = false;

            double weight = Math.Log(count + 1);
            string p = "^^" + w + "$";
            for (int i = 2; i < p.Length; i++)
            {
                Add(tri, p.Substring(i - 2, 3), weight);
                Add(ctx, p.Substring(i - 2, 2), weight);
            }
        }
        HasFrequencies = !allSame;
    }

    static void Add(Dictionary<string, double> d, string k, double v)
    {
        double cur;
        d.TryGetValue(k, out cur);
        d[k] = cur + v;
    }

    public long Count(string word)
    {
        long c;
        if (!words.TryGetValue(word, out c)) return 0;
        return HasFrequencies ? c : TypicalCount;
    }

    // Average log probability per character: the higher, the more natural the word is for the language
    public double Score(string word)
    {
        string p = "^^" + word + "$";
        double sum = 0;
        for (int i = 2; i < p.Length; i++)
        {
            double t, c;
            tri.TryGetValue(p.Substring(i - 2, 3), out t);
            ctx.TryGetValue(p.Substring(i - 2, 2), out c);
            sum += Math.Log((t + 0.1) / (c + 0.1 * alphabet));
        }
        return sum / (p.Length - 2);
    }
}

public static class Detector
{
    // Likelihood difference threshold for words that are not in the dictionaries
    public static double Threshold = 1.5;
    // How many times more frequent the other-layout word must be than the typed one (without context)
    public static double FrequencyRatio = 200;

    static readonly Dictionary<string, int> Alphabets = new Dictionary<string, int>
    {
        { "en", 26 }, { "ru", 33 }, { "uk", 33 }, { "be", 32 }, { "de", 30 }, { "pl", 32 }, { "sr-latn", 30 }, { "sr-cyrl", 30 }
    };

    static readonly Dictionary<string, string[]> SingleLetterWords = new Dictionary<string, string[]>
    {
        { "en", new[] { "a", "i" } },
        { "ru", new[] { "а", "в", "и", "к", "о", "с", "у", "я" } },
        { "uk", new[] { "а", "в", "з", "і", "й", "о", "у", "я" } },
        { "be", new[] { "а", "з", "і", "у", "ў", "я" } },
        { "pl", new[] { "a", "i", "o", "u", "w", "z" } },
        { "sr-latn", new[] { "a", "i", "u", "o", "s", "k" } },
        { "sr-cyrl", new[] { "а", "и", "у", "о", "с", "к" } }
    };

    public static IEnumerable<string> Languages { get { return Alphabets.Keys; } }

    static readonly Dictionary<string, LangModel> models = new Dictionary<string, LangModel>();
    static readonly object sync = new object();

    // Where word lists come from: embedded resources "<lang>.txt.gz"; tests can swap in their own source
    public static Func<string, IEnumerable<string>> Source = lang => ReadResource(lang + ".txt.gz");

    // A model that is already loaded, or null. Never loads: safe to call from the keyboard hook
    public static LangModel Loaded(string lang)
    {
        if (lang == null) return null;
        lock (sync)
        {
            LangModel m;
            return models.TryGetValue(lang, out m) ? m : null;
        }
    }

    // Loads a model if needed (takes a fraction of a second, so call it off the keyboard hook)
    public static LangModel Load(string lang)
    {
        if (lang == null || !Alphabets.ContainsKey(lang)) return null;
        var m = Loaded(lang);
        if (m != null) return m;
        m = new LangModel(Source(lang), Alphabets[lang]) { Lang = lang };
        string[] singles;
        if (SingleLetterWords.TryGetValue(lang, out singles)) m.Singles = new HashSet<string>(singles);
        lock (sync) models[lang] = m;
        return m;
    }

    public static void Set(LangModel m) { lock (sync) models[m.Lang] = m; }

    static IEnumerable<string> ReadResource(string name)
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (var gz = new GZipStream(s, CompressionMode.Decompress))
        using (var r = new StreamReader(gz, Encoding.UTF8))
        {
            string line;
            while ((line = r.ReadLine()) != null) yield return line;
        }
    }

    const string LeadPunct = "([{\"«„";
    const string TrailPunct = ".,!?:;)]}\"»“";

    // The word without surrounding punctuation; null if it has non-letters inside (so it isn't a word)
    public static string Core(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && LeadPunct.IndexOf(s[a]) >= 0) a++;
        while (b > a && TrailPunct.IndexOf(s[b - 1]) >= 0) b--;
        if (a == b) return "";
        var sb = new StringBuilder(b - a);
        for (int i = a; i < b; i++)
        {
            char c = s[i];
            if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c == '-' && i > a && i < b - 1) sb.Append(c);
            else return null;
        }
        return sb.ToString();
    }

    // typed is the word as typed in a layout of language T; alt is the same keys in a layout of language A.
    // context: language of the previous word, or null if unknown
    public static bool ShouldConvert(string typed, string alt, LangModel T, LangModel A, string context = null)
    {
        string t = Core(typed), a = Core(alt);
        if (string.IsNullOrEmpty(a)) return false;
        bool ctxAlt = context != null && context == A.Lang;
        bool ctxSame = context != null && context == T.Lang;

        // Serbian is written in both alphabets: a Latin word that is valid Serbian Latin is a choice, not a mistake
        if (A.Lang == "sr-cyrl" && !string.IsNullOrEmpty(t))
        {
            var latin = Loaded("sr-latn");
            if (latin != null && latin.Count(t) > 0) return false;
        }

        if (a.Length == 1)
        {
            if (t == null) return A.Singles.Contains(a);
            if (ctxSame || char.IsUpper(typed[0])) return false;
            return t.Length == 1 && !T.Singles.Contains(t) && A.Singles.Contains(a);
        }

        long ct = string.IsNullOrEmpty(t) ? 0 : T.Count(t);
        long ca = A.Count(a);

        // The Serbian Cyrillic layout is phonetic: a word typed in the wrong layout turns into a transliteration
        // (hello → хелло) that reads as plausible, so letter statistics can't tell — only the dictionary decides
        bool strict = T.Lang == "sr-cyrl" || A.Lang == "sr-cyrl";

        // What was typed isn't a word at all (symbols inside), but the other layout gives a word
        if (t == null) return ca > 0 || (!strict && A.Score(a) > -3.2);

        if (ct > 0 && ca > 0)
            return ctxAlt ? ca > ct : (!ctxSame && ca > ct * FrequencyRatio);
        if (ct > 0) return false;
        // In strict mode an unknown typed word must also look clearly less natural than the alternative:
        // a rare real word can transliterate into a dictionary word of the other language
        if (ca > 0) return strict ? A.Score(a) > T.Score(t) + (ctxAlt ? 0 : 0.5) && !ctxSame
                                  : !ctxSame || A.Score(a) > T.Score(t) + 1;

        if (a.Length < 3 || strict) return false;
        double th = ctxAlt ? Threshold - 0.5 : ctxSame ? Threshold + 0.5 : Threshold;
        return A.Score(a) - T.Score(t) > th;
    }

    // Among the other installed layouts, which one did the user mean? Returns its index or -1 to leave the word alone.
    // preferred: the layout of that alphabet the user used last (breaks ties such as Russian vs Ukrainian)
    public static int Choose(string typed, LangModel T, IList<string> alts, IList<LangModel> altModels, string context, int preferred)
    {
        int best = -1;
        double bestScore = double.MinValue;
        for (int i = 0; i < alts.Count; i++)
        {
            var A = altModels[i];
            if (A == null || !ShouldConvert(typed, alts[i], T, A, context)) continue;
            string core = Core(alts[i]);
            double score = (string.IsNullOrEmpty(core) ? -10 : A.Score(core))
                         + (core != null && A.Count(core) > 0 ? 2 : 0)
                         + (context == A.Lang ? 1.5 : 0)
                         + (i == preferred ? 1 : 0);
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return best;
    }
}
