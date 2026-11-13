using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;

// Language model: a frequency dictionary plus three-letter sequence statistics.
// Kept compact, as sorted arrays: a string and a hash-table entry per word would take several times the letters
// themselves (tens of megabytes with an extended dictionary)
public class LangModel
{
    // A dictionary without frequencies (every word has the same weight) treats each of its words as this common
    const long TypicalCount = 3000;

    // The words, sorted, one after another in one buffer: word i is text[starts[i] .. starts[i + 1])
    readonly char[] text;
    readonly int[] starts;
    readonly long[] counts;
    // Three- and two-letter sequences, each packed into a number (see Pack), sorted, with their weights
    readonly long[] triKeys, ctxKeys;
    readonly double[] triWeights, ctxWeights;
    readonly int alphabet;

    public string Lang;
    public bool HasFrequencies = true;
    public HashSet<string> Singles = new HashSet<string>();   // one-letter words of the language

    public LangModel(IEnumerable<string> lines, int alphabetSize)
    {
        alphabet = alphabetSize + 1;
        var words = new Dictionary<string, long>();
        var tri = new Dictionary<long, double>();
        var ctx = new Dictionary<long, double>();
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
                Add(tri, Pack(p, i - 2, 3), weight);
                Add(ctx, Pack(p, i - 2, 2), weight);
            }
            // Pairs for HasImpossible: one-letter entries ("ь", "ы" in Russian lists) are left out, they are not words
            if (w.Length >= 2)
                for (int i = 1; i < p.Length - 1; i++) pairs.Add(Pack(p, i, 2));
        }
        HasFrequencies = !allSame;

        var sorted = new string[words.Count];
        words.Keys.CopyTo(sorted, 0);
        Array.Sort(sorted, StringComparer.Ordinal);
        int length = 0;
        foreach (string w in sorted) length += w.Length;
        text = new char[length];
        starts = new int[sorted.Length + 1];
        counts = new long[sorted.Length];
        for (int i = 0, at = 0; i < sorted.Length; i++)
        {
            starts[i] = at;
            sorted[i].CopyTo(0, text, at, sorted[i].Length);
            at += sorted[i].Length;
            counts[i] = words[sorted[i]];
        }
        starts[sorted.Length] = length;
        foreach (string w in sorted) longest = Math.Max(longest, w.Length);
        ToArrays(tri, out triKeys, out triWeights);
        ToArrays(ctx, out ctxKeys, out ctxWeights);

        // The letters its words are made of, and how often a word must be to count as common: the 20,000th most frequent
        var seen = new HashSet<char>(text);
        seen.Remove('-');
        letters = new char[seen.Count];
        seen.CopyTo(letters);
        var byCount = (long[])counts.Clone();
        Array.Sort(byCount);
        common = byCount.Length == 0 ? long.MaxValue : byCount[Math.Max(0, byCount.Length - 20000)];
        Rare = byCount.Length <= 50000 ? 0 : byCount[byCount.Length - 50000];
    }

    // Rarer than the 50,000 most frequent words: past the built-in list, so only in the extended dictionary, whose
    // subtitles also hold words typed in the wrong layout ("yj" for "но"). 0 without an extended dictionary
    public readonly long Rare;
    public long Common { get { return common; } }

    readonly char[] letters;
    readonly long common;
    readonly int longest;   // the longest word's length
    readonly HashSet<long> pairs = new HashSet<long>();   // every pair of letters side by side (or "^" and a first letter) in a word of two letters or more

    // A word of the language is one letter away from this one (one letter changed, added, dropped, or two side by side
    // swapped): the word is likely that word with a typo. commonOnly: only a common word counts, and only in a
    // dictionary with frequencies
    public bool NearWord(string word, bool commonOnly)
    {
        // A word more than a letter longer than the longest one is near none. Looking anyway takes time that grows
        // with the square of the length: seconds for a "word" of 30,000 letters
        if (word.Length < 3 || word.Length > longest + 1 || (commonOnly && !HasFrequencies)) return false;
        long common = commonOnly ? this.common : 1;
        var chars = word.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char keep = chars[i];
            foreach (char c in letters)
            {
                if (c == keep) continue;
                chars[i] = c;
                if (Count(new string(chars)) >= common) { chars[i] = keep; return true; }
            }
            chars[i] = keep;
            if (i + 1 < chars.Length && chars[i] != chars[i + 1])
            {
                string swapped = word.Substring(0, i) + word[i + 1] + word[i] + word.Substring(i + 2);
                if (Count(swapped) >= common) return true;
            }
            if (Count(word.Remove(i, 1)) >= common) return true;
        }
        for (int i = 0; i <= word.Length; i++)
            foreach (char c in letters)
                if (Count(word.Insert(i, c.ToString())) >= common) return true;
        return false;
    }

    // Up to three characters in one number; the length goes first, so "ab" and "\0ab" differ
    static long Pack(string s, int start, int length)
    {
        long key = length;
        for (int i = start; i < start + length; i++) key = (key << 16) | s[i];
        return key;
    }

    static void Add(Dictionary<long, double> d, long k, double v)
    {
        double cur;
        d.TryGetValue(k, out cur);
        d[k] = cur + v;
    }

    static void ToArrays(Dictionary<long, double> d, out long[] keys, out double[] values)
    {
        keys = new long[d.Count];
        values = new double[d.Count];
        d.Keys.CopyTo(keys, 0);
        d.Values.CopyTo(values, 0);
        Array.Sort(keys, values);
    }

    static double Lookup(long[] keys, double[] values, long key)
    {
        int i = Array.BinarySearch(keys, key);
        return i >= 0 ? values[i] : 0;
    }

    // Ordinal comparison of a word with word i of the buffer
    int Compare(string word, int i)
    {
        int s = starts[i], n = starts[i + 1] - s;
        for (int k = 0; k < word.Length && k < n; k++)
        {
            int d = word[k] - text[s + k];
            if (d != 0) return d;
        }
        return word.Length - n;
    }

    public long Count(string word)
    {
        int lo = 0, hi = counts.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1, c = Compare(word, mid);
            if (c == 0) return HasFrequencies ? counts[mid] : TypicalCount;
            if (c < 0) hi = mid - 1; else lo = mid + 1;
        }
        // Greek is very often written without its accents (chats, and capitals, where they are left out anyway):
        // καλημερα is καλημέρα. Such a word counts as its accented form
        if (Lang == "el" && word.Length > 1 && IsBare(word))
        {
            long bare;
            if (Bares().TryGetValue(word, out bare)) return bare;
        }
        return 0;
    }

    // Made now, while loading, rather than on the first word in the keyboard hook: Greek's table of words without accents
    public void WarmUp() { if (Lang == "el") Bares(); }

    // Greek letters with tonos or dialytika
    const string Accented = "άέήίόύώΐΰϊϋ";

    static bool IsBare(string word)
    {
        foreach (char c in word) if (Accented.IndexOf(c) >= 0) return false;
        return true;
    }

    public static string Bare(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (char c in word.Normalize(NormalizationForm.FormD))
            if (c != '́' && c != '̈') sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // Each accented word under its form without accents (the most frequent of words sharing one), made the first time
    // it is needed: a few megabytes, and only for Greek
    Dictionary<string, long> bares;

    Dictionary<string, long> Bares()
    {
        var b = bares;
        if (b != null) return b;
        b = new Dictionary<string, long>(StringComparer.Ordinal);
        for (int i = 0; i < counts.Length; i++)
        {
            string w = new string(text, starts[i], starts[i + 1] - starts[i]);
            if (IsBare(w)) continue;
            string key = Bare(w);
            long have, count = HasFrequencies ? counts[i] : TypicalCount;
            if (!b.TryGetValue(key, out have) || have < count) b[key] = count;
        }
        return bares = b;
    }

    // The word has two letters side by side (or a first letter) that no word of the language has: "ъь", or "ь" at the
    // start in Russian. Such a word is not of this language, however natural the rest of it reads. The parts of a
    // hyphenated word are checked each on its own
    public bool HasImpossible(string word)
    {
        foreach (string part in word.Split('-'))
        {
            if (part.Length == 0) continue;
            string p = "^" + part;
            for (int i = 0; i + 1 < p.Length; i++)
                if (!pairs.Contains(Pack(p, i, 2))) return true;
        }
        return false;
    }

    // The first of the words that are not less than this prefix: words starting with it begin here, if there are any
    int LowerBound(string prefix)
    {
        int lo = 0, hi = counts.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (Compare(prefix, mid) > 0) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    bool StartsWith(int i, string prefix)
    {
        int s = starts[i];
        if (starts[i + 1] - s < prefix.Length) return false;
        for (int k = 0; k < prefix.Length; k++) if (text[s + k] != prefix[k]) return false;
        return true;
    }

    // Some word of the language starts with these letters
    public bool HasPrefix(string prefix)
    {
        int i = LowerBound(prefix);
        return i < counts.Length && StartsWith(i, prefix);
    }

    // A common word of the language starts with these letters (any word, in a dictionary without frequencies)
    public bool HasCommonPrefix(string prefix)
    {
        for (int i = LowerBound(prefix), seen = 0; i < counts.Length && seen < 5000 && StartsWith(i, prefix); i++, seen++)
            if (!HasFrequencies || counts[i] >= common) return true;
        return false;
    }

    // Average log probability per character: the higher, the more natural the word is for the language
    public double Score(string word)
    {
        string p = "^^" + word + "$";
        double sum = 0;
        for (int i = 2; i < p.Length; i++)
        {
            double t = Lookup(triKeys, triWeights, Pack(p, i - 2, 3));
            double c = Lookup(ctxKeys, ctxWeights, Pack(p, i - 2, 2));
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
        { "en", 26 }, { "ru", 33 }, { "uk", 33 }, { "be", 32 }, { "de", 30 }, { "pl", 32 }, { "sr-latn", 30 }, { "sr-cyrl", 30 },
        { "bg", 30 }, { "mk", 31 }, { "el", 34 }, { "he", 27 }, { "ka", 33 },
        { "fr", 39 }, { "es", 33 }, { "it", 36 }, { "pt", 39 }, { "cs", 41 }, { "tr", 29 }
    };

    static readonly Dictionary<string, string[]> SingleLetterWords = new Dictionary<string, string[]>
    {
        { "en", new[] { "a", "i" } },
        { "ru", new[] { "а", "в", "и", "к", "о", "с", "у", "я" } },
        { "uk", new[] { "а", "в", "з", "і", "й", "о", "у", "я" } },
        { "be", new[] { "а", "з", "і", "у", "ў", "я" } },
        { "pl", new[] { "a", "i", "o", "u", "w", "z" } },
        { "sr-latn", new[] { "a", "i", "u", "o", "s", "k" } },
        { "sr-cyrl", new[] { "а", "и", "у", "о", "с", "к" } },
        { "bg", new[] { "а", "в", "и", "с", "у", "я" } },
        { "mk", new[] { "а", "в", "е", "и", "о", "с" } },
        { "el", new[] { "ο", "η", "ή", "ω", "α" } },
        { "fr", new[] { "a", "à", "y" } },
        { "es", new[] { "a", "e", "o", "u", "y" } },
        { "it", new[] { "a", "e", "è", "i", "o" } },
        { "pt", new[] { "a", "e", "é", "o" } },
        { "cs", new[] { "a", "i", "k", "o", "s", "u", "v", "z" } },
        { "tr", new[] { "o" } }
    };

    // Languages whose standard Windows layout puts letters on the keys of similar-sounding Latin letters
    static readonly HashSet<string> Phonetic = new HashSet<string> { "sr-cyrl", "mk", "el" };

    public static IEnumerable<string> Languages { get { return Alphabets.Keys; } }

    static readonly Dictionary<string, LangModel> models = new Dictionary<string, LangModel>();
    static readonly object sync = new object();

    // Where word lists come from: embedded resources "<lang>.txt.gz", then the extended dictionary if the user installed
    // one (Extra); tests can swap in their own source
    public static Func<string, IEnumerable<string>> Source = lang => Concat(ReadResource(lang + ".txt.gz"), Extra(lang));
    public static Func<string, IEnumerable<string>> Extra = lang => new string[0];

    static IEnumerable<string> Concat(IEnumerable<string> a, IEnumerable<string> b)
    {
        foreach (string s in a) yield return s;
        foreach (string s in b) yield return s;
    }

    // Builds the model again (an extended dictionary was installed or removed) and puts it in place of the old one.
    // Only languages already in use; call it off the keyboard hook
    public static void Reload(string lang)
    {
        if (Loaded(lang) == null) return;
        var m = new LangModel(Source(lang), Alphabets[lang]) { Lang = lang };
        string[] singles;
        if (SingleLetterWords.TryGetValue(lang, out singles)) m.Singles = new HashSet<string>(singles);
        m.WarmUp();
        lock (sync) models[lang] = m;
    }

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
        // Greek's table of words without accents is made now, while loading, not on the first such word in the hook
        m.WarmUp();
        string[] singles;
        if (SingleLetterWords.TryGetValue(lang, out singles)) m.Singles = new HashSet<string>(singles);
        lock (sync) models[lang] = m;
        return m;
    }


    // Building a model reads every line of its word lists into temporary lists and tables, tens of megabytes with an
    // extended dictionary, while the model itself keeps a few. Collected and compacted at once, that memory goes back
    // to Windows instead of staying with KeyFish. A fraction of a second, so off the keyboard hook too
    public static void ReturnMemory()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
    }

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

    // The word without surrounding punctuation; null if it has non-letters inside (so it isn't a word).
    // lang: the word's language where lower case depends on it — Turkish capital I is ı, and İ is i; a Greek capital Σ
    // at the end of a word is ς (ΚΟΣΜΟΣ is κοσμος)
    public static string Core(string s, string lang = null)
    {
        bool turkish = lang == "tr";
        int a = 0, b = s.Length;
        while (a < b && LeadPunct.IndexOf(s[a]) >= 0) a++;
        while (b > a && TrailPunct.IndexOf(s[b - 1]) >= 0) b--;
        if (a == b) return "";
        var sb = new StringBuilder(b - a);
        for (int i = a; i < b; i++)
        {
            char c = s[i];
            if (turkish && c == 'I') sb.Append('ı');
            else if (turkish && c == '\u0130') sb.Append('i');
            else if (c == 'Σ' && lang == "el" && (i == b - 1 || s[i + 1] == '-')) sb.Append('ς');
            else if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c == '-' && i > a && i < b - 1) sb.Append(c);
            else return null;
        }
        return sb.ToString();
    }

    // Links, paths and code are never changed: "://", "www." or a symbol inside the word that no layout makes a letter.
    // Symbols at the end don't count: "ghbdtn/" is "привет." typed in the wrong layout
    public static bool LooksLikeCode(string typed)
    {
        if (typed.IndexOf("://", StringComparison.Ordinal) >= 0 || typed.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return true;
        string inner = typed.Length > 2 ? typed.Substring(1, typed.Length - 2) : "";
        return inner.IndexOfAny(CodeSymbols.ToCharArray()) >= 0;
    }

    // Numbers, e-mails, identifiers, links and code aren't words to fix: a digit, "@", "_" or what LooksLikeCode finds.
    // But a key that types a digit or a symbol in one layout types a letter in another: French é è ç à and Czech
    // ě š č ř ž ý á í é are on the digit row (and digits there need Shift), Bulgarian б, Hebrew ץ, Serbian ž and
    // Portuguese ç are on the keys of / . \ and ;, Shift+/ is "_" in German and a comma in Russian, and the Bulgarian
    // question mark is on Shift+2. So such a word is still looked at when the other layout (alt, its model A) reads
    // the same keys as a word of its dictionary, three letters or more and nothing but letters with punctuation around
    // them — unless what was typed reads well as it is: a number alone ("3959" is not "šíří"), words of the typed
    // language around a symbol ("ne/to", "we\no") or one with a number before or after it ("тут9", "5го").
    // alt null: the plain rule
    public static bool Skipped(string typed, string alt, LangModel T, LangModel A)
    {
        bool odd = LooksLikeCode(typed), digit = false, letter = false;
        for (int i = 0; i < typed.Length; i++)
        {
            char c = typed[i];
            if (char.IsDigit(c)) odd = digit = true;
            else if (c == '_' || c == '@') odd = true;
            else if (char.IsLetter(c)) letter = true;
            if (c == '@' && i > 0 && i < typed.Length - 1) return true;   // an e-mail address
        }
        if (!odd) return false;
        if (alt == null || T == null || A == null || !letter) return true;
        if (typed.IndexOf("://", StringComparison.Ordinal) >= 0 || typed.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return true;
        string a = Core(alt.Replace("'", "").Replace("\u2019", ""), A.Lang);
        if (a == null || a.Length < 3 || !IsKnown(alt, A)) return true;
        // A capital after a small letter is no word ("неШто" from "ne/to")
        for (int i = 1; i < alt.Length; i++) if (char.IsLower(alt[i - 1]) && char.IsUpper(alt[i])) return true;

        // The pieces between the digits and symbols: if every one is a word of the typed language, the word was typed
        // as meant; so was a number with one or two letters before or after it ("5го", "20мм"). Letters between two
        // numbers ("2е2", "été") are not written so
        int numbers = 0, pieces = 0, scraps = 0;
        for (int i = 0; i < typed.Length; )
        {
            int j = i + 1;
            if (char.IsDigit(typed[i]))
            {
                while (j < typed.Length && char.IsDigit(typed[j])) j++;
                numbers++;
            }
            else if (!IsSymbol(typed[i]))
            {
                while (j < typed.Length && !char.IsDigit(typed[j]) && !IsSymbol(typed[j])) j++;
                string piece = Core(typed.Substring(i, j - i), T.Lang);
                if (piece == null) return false;
                if (piece.Length > 0)   // punctuation alone is no piece
                {
                    bool word = piece.Length == 1 ? T.Singles.Contains(piece) : T.Count(piece) > 0;
                    if (!word && (!digit || piece.Length > 2 || pieces > 0)) return false;
                    if (!word) scraps++;
                    pieces++;
                }
            }
            i = j;
        }
        if (scraps > 0 && pieces > 1) return false;
        return numbers < 2 || pieces != 1;
    }

    const string CodeSymbols = "/\\=|$%&*+";
    static bool IsSymbol(char c) { return c == '_' || c == '@' || CodeSymbols.IndexOf(c) >= 0; }

    // "ghbdtn," → "приветб": the key after the word was meant as the comma it typed, not as a letter of the other
    // layout. Decided by the dictionary: the word with that letter is unknown, without it known ("нож" from "yj;" stays)
    public static string KeepPunctuation(string result, LangModel model, string lang, char typedLast)
    {
        if (result.Length < 3 || !char.IsLetter(result[result.Length - 1]) || char.IsLetterOrDigit(typedLast) || char.IsWhiteSpace(typedLast) || model == null)
            return result;
        // A capital right after a small letter at the very end ("чтоБ" from "xnj;" typed in the German layout, where
        // ";" is Shift+,): no word ends so, it is the mark that was typed
        if (char.IsUpper(result[result.Length - 1]) && char.IsLower(result[result.Length - 2])) return result.Substring(0, result.Length - 1) + typedLast;
        string full = Core(result, lang), head = Core(result.Substring(0, result.Length - 1), lang);
        if (string.IsNullOrEmpty(full) || string.IsNullOrEmpty(head) || head.Length < 2 || model.Count(full) > 0 || model.Count(head) == 0) return result;
        return result.Substring(0, result.Length - 1) + typedLast;
    }

    // A dot between letters (file.txt, obj.method) or a capital after a small letter (camelCase) may be code;
    // such a word is changed only into a dictionary word, never on letter statistics alone
    public static bool NeedsDictionary(string typed)
    {
        for (int i = 1; i + 1 < typed.Length; i++)
            if (typed[i] == '.' && char.IsLetter(typed[i - 1]) && char.IsLetter(typed[i + 1])) return true;
        for (int i = 1; i < typed.Length; i++)
            if (char.IsLower(typed[i - 1]) && char.IsUpper(typed[i])) return true;
        return false;
    }

    // The typed word without the characters that are punctuation around the other layout's word. The same keys can be
    // a comma in one layout and a letter in the other ("a," is "фб"): the letter then isn't part of the word
    static string TypedCore(string typed, string alt, string lang)
    {
        if (typed.Length != alt.Length) return Core(typed, lang);
        int a = 0, b = alt.Length;
        while (a < b && LeadPunct.IndexOf(alt[a]) >= 0) a++;
        while (b > a && TrailPunct.IndexOf(alt[b - 1]) >= 0) b--;
        return a == b ? Core(typed, lang) : Core(typed.Substring(a, b - a), lang);
    }

    static bool HasInnerApostrophe(string s)
    {
        for (int i = 1; i + 1 < s.Length; i++)
            if ((s[i] == '\'' || s[i] == '\u2019') && char.IsLetter(s[i - 1]) && char.IsLetter(s[i + 1])) return true;
        return false;
    }

    // How frequent a word is, one written with a hyphen too. That one is in the lists as its parts ("peut-être" as
    // peut and être, "кое-как" as кое and как): it is a word if every part is a common word of two letters or more
    // (the lists hold scraps too), as frequent as the rarest of them. Only for telling that keys which type digits or
    // symbols in one layout make a word in another: two words with a hyphen between prove little ("все-ех" is "ließen")
    static long CountHyphened(LangModel m, string word)
    {
        long count = m.Count(word);
        if (count > 0 || word.IndexOf('-') < 0) return count;
        count = long.MaxValue;
        foreach (string part in word.Split('-'))
        {
            long c = part.Length < 2 ? 0 : m.Count(part);
            if (c < m.Common) return 0;
            count = Math.Min(count, c);
        }
        return count;
    }

    // What stands before the apostrophe in French and Italian (l'homme, qu'il, dell'anno) and after it in English
    // (isn't, we're); in Ukrainian and Belarusian, the letters that follow it (п'ять, сям'я)
    static readonly Dictionary<string, string[]> Elided = new Dictionary<string, string[]>
    {
        { "fr", new[] { "l", "d", "j", "n", "s", "m", "t", "c", "qu", "jusqu", "lorsqu", "puisqu", "quelqu", "aujourd" } },
        { "it", new[] { "l", "un", "dell", "all", "dall", "nell", "sull", "quell", "quest", "c", "d", "m", "t", "s", "v", "n", "com", "dov", "bell", "sant", "quant", "tutt", "nient", "senz", "anch", "cos" } },
        { "en", new[] { "t", "s", "m", "re", "ve", "ll", "d" } },
        { "uk", new[] { "я", "ю", "є", "ї" } },
        { "be", new[] { "я", "ю", "е", "ё", "і" } }
    };

    // The letters of a word and how frequent it is; null if it isn't a word. Like Core and Count, but for a word with an
    // apostrophe inside too. The lists hold such words without the apostrophe (English "dont") or as the parts around
    // it: French "l'homme" as l and homme, Ukrainian "п'ять" as п and ять, English "isn't" as isn and t. Only where the
    // language writes an apostrophe: after l, d, qu… in French and Italian, before t, s, re… in English, before я ю є ї
    // in Ukrainian (я ю е ё і in Belarusian); there a word whose parts the list doesn't have is still a word (frequency 0)
    // written: the text as it stands on screen, not another reading of its keys — then only by those rules
    // ("ve'e", the keys of "веќе", is not "vee")
    public static string Letters(string text, LangModel m, out long count, bool written = false)
    {
        count = 0;
        string core = Core(text, m.Lang);
        if (core != null)
        {
            if (core.Length > 0) count = m.Count(core);
            return core;
        }
        if (!HasInnerApostrophe(text)) return null;
        string plain = text.Replace('\u2019', '\'');
        string joined = Core(plain.Replace("'", ""), m.Lang);
        if (string.IsNullOrEmpty(joined)) return null;
        count = written ? 0 : m.Count(joined);
        if (count > 0) return joined;
        string[] rule, parts = plain.Split('\'');
        if (parts.Length != 2 || !Elided.TryGetValue(m.Lang, out rule)) return null;
        string head = Core(parts[0], m.Lang), tail = Core(parts[1], m.Lang);
        if (string.IsNullOrEmpty(head) || string.IsNullOrEmpty(tail)) return null;
        bool slavic = m.Lang == "uk" || m.Lang == "be", english = m.Lang == "en";
        if (Array.IndexOf(rule, slavic ? tail.Substring(0, 1) : english ? tail : head) < 0) return null;
        // The part that is a word by itself: all of an English one but the ending, a French or Italian one after the
        // article; a Ukrainian one is cut in two scraps, both in the list or neither
        if (slavic) count = Math.Min(m.Count(head), m.Count(tail));
        else
        {
            string word = english ? head : tail;
            count = word.Length < 2 && !m.Singles.Contains(word) ? 0 : m.Count(word);
            if (count == 0) return null;
        }
        if (written && count == 0) return null;
        return joined;
    }

    // A word of the dictionary, with punctuation around it or not
    public static bool IsKnown(string text, LangModel m)
    {
        long count;
        string letters = Letters(text, m, out count);
        return !string.IsNullOrEmpty(letters) && (count > 0 || CountHyphened(m, letters) > 0);
    }

    // Two to five letters, all capitals: an abbreviation, not a word typed in the wrong layout
    public static bool IsAbbreviation(string typed)
    {
        string core = Core(typed);
        if (core == null || core.Length < 2 || core.Length > 5) return false;
        int letters = 0;
        foreach (char c in typed)
            if (char.IsLetter(c)) { if (!char.IsUpper(c)) return false; letters++; }
        return letters >= 2;
    }

    // typed is the word as typed in a layout of language T; alt is the same keys in a layout of language A.
    // context: language of the previous word, or null if unknown
    public static bool ShouldConvert(string typed, string alt, LangModel T, LangModel A, string context = null)
    {
        long ca, ct = -1;
        if (typed.Length == 0) return false;   // nothing typed (the first letter is looked at below)
        string t = TypedCore(typed, alt, T.Lang), a = Letters(alt, A, out ca);
        if (string.IsNullOrEmpty(a)) return false;
        // What was typed has an apostrophe inside and is a word with it (don't, l'homme, п'ять)
        if (t == null)
        {
            string letters = Letters(typed, T, out ct, true);
            if (letters != null && ct > 0) t = letters; else ct = -1;
        }
        // A known abbreviation in capitals (VS, PDF) is left as typed, even if the other layout makes a word of it
        // (МЫ); one that means nothing as typed (ФЗШ) is still fixed (API). Pause converts either
        if (IsAbbreviation(typed) && !string.IsNullOrEmpty(t) && T.Count(t) > 0) return false;
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

        if (ct < 0) ct = string.IsNullOrEmpty(t) ? 0 : T.Count(t);

        // Letters the other layout turns into punctuation were cut off the typed word ("a," is "фб"). With them it may
        // be a word of its own language: "всю" is not "вс" and a dot ("dc."), Hebrew "קצת" is not "קצ" and a comma
        // ("em,"). Such a word is changed only for a word as much more frequent as any word is for another
        string whole = t == null ? null : Core(typed, T.Lang);
        long cw = string.IsNullOrEmpty(whole) || whole == t ? 0 : T.Count(whole);
        if (cw > 0 && !(ctxAlt ? ca > cw : (!ctxSame && ca > cw * FrequencyRatio))) return false;

        // Serbian Cyrillic, Macedonian and Greek layouts are phonetic: a word typed in the wrong layout turns into
        // a transliteration (hello → хелло, ηελλο) that reads as plausible, so letter statistics can't tell — only the dictionary decides
        bool strict = Phonetic.Contains(T.Lang) || Phonetic.Contains(A.Lang);

        // What was typed isn't a word at all (symbols inside), but the other layout gives a word
        if (t == null) return ca > 0 || CountHyphened(A, a) > 0 || (!strict && A.Score(a) > -3.2);

        if (ct > 0 && ca > 0)
        {
            // In a phonetic layout short words of both languages are everywhere (Greek το, με, αν; English to, me, an):
            // after a word in the other language such a word is the other one only if it is far more frequent there,
            // or a bilingual writer's "send το email" would lose its Greek
            long ratioAlt = strict ? 20 : 1;
            if (ctxAlt ? ca > ct * ratioAlt : (!ctxSame && ca > ct * FrequencyRatio)) return true;
            // A word known only from the extended dictionary against a common word of the other language: such a
            // "word" is most often that word typed in the wrong layout. Right after a word in the typed language, only
            // if the other one is a thousand times as frequent
            // ("шею" is not "ше" + a dot: letters the other layout turns into punctuation must not be cut off for it)
            return ct < T.Rare && ca >= A.Common && ca > ct * (ctxSame ? 1000 : 20) && t == Core(typed, T.Lang);
        }
        if (ct > 0) return false;
        // In strict mode an unknown typed word must also look clearly less natural than the alternative:
        // a rare real word can transliterate into a dictionary word of the other language
        if (ca > 0)
        {
            if (!strict) return !ctxSame || A.Score(a) > T.Score(t) + 1;
            if (!ctxSame) return A.Score(a) > T.Score(t) + (ctxAlt ? 0 : 0.5);
            // Greek and a Latin-script language only: Serbian Cyrillic and Macedonian take English words as their own
            // (лорд, топ, фронт), and a real word their lists lack would turn into English there
            if (T.Lang != "el" && A.Lang != "el") return false;
            string latin = T.Lang == "el" ? a : t;
            foreach (char c in latin) if (c > (char)0x24F) return false;
            // Right after a word in the typed language: an English word dropped into Greek text with the Greek layout
            // still on ("τιμε" for "time"), or a Greek word typed without accents right after English text with the
            // English layout still on ("kalhmera"). Taken only for a common word of three letters or more, when what was typed
            // isn't a slip for a common word of its own language and reads clearly less naturally
            // (the very commonest words — you, the, and, this — whatever Greek word they are a letter from: a slip that
            // happens to spell exactly one of them is far rarer than the English word itself)
            return a.Length >= 3 && ca >= A.Common && (ca >= A.Common * 50 || !T.NearWord(t, true)) && A.Score(a) > T.Score(t) + 0.5;
        }

        // Neither is a known word. A pair of letters the typed word's language never has, while the other layout's word
        // has none such, settles it whichever way, short words too (xneur's rule); the other way round, the other
        // layout's word can't be a word at all
        bool impossibleT = T.HasImpossible(t), impossibleA = A.HasImpossible(a);
        if (impossibleA && !impossibleT) return false;
        // A word written with a hyphen, each part a common word of the other language (кое-как, из-под): the letter
        // statistics know nothing of hyphens, so the parts are compared, the least natural of them with what was typed
        if (!impossibleA && CountHyphened(A, a) > 0)
        {
            if (impossibleT) return true;
            double least = double.MaxValue;
            foreach (string part in a.Split('-')) least = Math.Min(least, A.Score(part));
            if (!ctxSame && least - T.Score(t) > Threshold) return true;
        }
        // A typo makes such pairs too (привте): a typed word one letter from a word of its own language is that word
        // mistyped, not another layout
        // Three letters are often an abbreviation (мфц): only if a common word of the other language starts so, and not
        // right after a word in the typed language
        int nearT = -1;   // T.NearWord(t, false), worked out once: 1 or 0
        if (impossibleT && !impossibleA && !strict && (a.Length >= 4 || (a.Length == 3 && !ctxSame && A.HasCommonPrefix(a)))
            && (nearT = T.NearWord(t, false) ? 1 : 0) == 0)
            return A.Score(a) - T.Score(t) > (ctxSame ? 1.5 : 0.75);
        // Typed in the wrong layout and with a typo too: the other layout's word is one letter from a common word,
        // the typed one is near no word of its language at all, not even a rare one, and the letters agree. The layout is fixed, the typo stays
        if (!strict && !impossibleA && a.Length >= 4 && !ctxSame && A.Score(a) > T.Score(t) && A.NearWord(a, true)
            && (nearT >= 0 ? nearT == 0 : !T.NearWord(t, false)))
            return true;
        // Otherwise letter statistics decide, but not for words this short (wfh is not "царб")
        if (a.Length < 4 || strict) return false;
        double th = ctxAlt ? Threshold - 0.5 : ctxSame ? Threshold + 0.5 : Threshold;
        return A.Score(a) - T.Score(t) > th;
    }

    // The beginning of a word still being typed (four letters or more): is it so clearly in the wrong layout that the
    // layout is switched now, before the rest is typed in it too? Only on strong evidence: no word of the typed
    // language starts so, a common word of the other one starts with the same keys there, and the typed letters have
    // a pair their language never has or read far less naturally (right after a word in the typed language, far less
    // still). Never for phonetic layouts. The whole word is checked again when it ends
    public static bool ShouldSwitchEarly(string typed, string alt, LangModel T, LangModel A, string context)
    {
        string t = Core(typed, T.Lang), a = Core(alt, A.Lang);
        if (string.IsNullOrEmpty(t) || string.IsNullOrEmpty(a) || t.Length < 4 || t.Length != a.Length) return false;
        if (Phonetic.Contains(T.Lang) || Phonetic.Contains(A.Lang)) return false;
        // Capitals after the first letter: an abbreviation or code, not a word
        for (int i = 1; i < typed.Length; i++) if (char.IsUpper(typed[i])) return false;
        if (T.HasPrefix(t) || !A.HasCommonPrefix(a) || A.HasImpossible(a)) return false;
        // Right after a word in the typed language the letters must read twice as unnaturally
        return T.HasImpossible(t) || A.Score(a) - T.Score(t) > (context == T.Lang ? 2 * Threshold : Threshold);
    }

    // typed was typed in a layout of language T, alt is the same keys in another layout of the SAME alphabet: German
    // typed on the English layout ("t;ten" for "töten", "partz" for "party"), Ukrainian on the Russian one ("ныяк" for
    // "ніяк"). Such layouts share most keys, so letter statistics say nothing: only the dictionaries decide. Fixed only
    // when what was typed is no word of its language, not even one letter from one (a typo stays a typo), and the other
    // layout gives a common word of three letters or more
    public static bool ShouldConvertSameScript(string typed, string alt, LangModel T, LangModel A, string context)
    {
        if (T == null || A == null || string.IsNullOrEmpty(typed) || string.IsNullOrEmpty(alt)) return false;
        // A word list without frequencies (Belarusian) makes every word of it "common": nothing tells a real slip from
        // a word of the typed language that the list happens not to have ("плитка" isn't "плітка")
        if (!A.HasFrequencies) return false;
        // Numbers, words with a number ("333", "3ok") and words after a symbol ("+yes") are what they are: the digit
        // keys are letters in Czech or French, but nobody typing 333 meant "ššš". Digits are let through only as
        // Skipped lets them through for other alphabets: when the other layout makes a dictionary word of the keys and
        // what was typed doesn't read as a number with letters ("p5ij9t" is "přijít", "3ok" stays)
        if (IsSymbol(typed[0])) return false;
        // Marks around the word ("über" typed as "[ber" keeps its letter; "can'" is can and a quote, not "cani"): what is
        // inside them decides — a word of the typed language, or less than two letters, stays
        int x = 0, y = typed.Length;
        while (x < y && !char.IsLetterOrDigit(typed[x])) x++;
        while (y > x && !char.IsLetterOrDigit(typed[y - 1])) y--;
        if (x > 0 || y < typed.Length)
        {
            string inner = Core(typed.Substring(x, y - x), T.Lang);
            if (inner != null && (inner.Length < 2 || T.Count(inner) > 0)) return false;
        }
        if (Skipped(typed, alt, T, A)) return false;
        string t = Core(typed, T.Lang), a = Core(alt, A.Lang);
        if (string.IsNullOrEmpty(a) || a.Length < 3 || a == t) return false;
        // A letter key that is punctuation in the other layout ("esperem" on the French layout is "espere;") isn't that
        // word followed by a mark: marks count only where the same mark was typed
        for (int i = 0; i < alt.Length; i++)
            if (!char.IsLetter(alt[i]) && (i >= typed.Length || alt.Length != typed.Length || typed[i] != alt[i])) return false;
        if (IsAbbreviation(typed) || IsAbbreviation(alt)) return false;
        // One letter from a word of the typed language is a typo of it, unless the words before were in the other
        // language: after "do" in English, "zou" is "you" typed on the German layout, not a slip for "zu"
        // (a list without frequencies, Belarusian, has no "common" words: any word of it counts)
        if (!string.IsNullOrEmpty(t) && (T.Count(t) > 0 || (context != A.Lang && T.NearWord(t, T.HasFrequencies)))) return false;
        long ca = A.Count(a);
        // Right after a word in the typed language a slip of the layout is less likely: a more frequent word is needed
        return ca >= A.Common * (context == T.Lang ? 4 : 1);
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
            long known;
            string core = Letters(alts[i], A, out known);
            double score = (string.IsNullOrEmpty(core) ? -10 : A.Score(core))
                         + (known > 0 ? 2 : 0)
                         + (context == A.Lang ? 1.5 : 0)
                         + (i == preferred ? 1 : 0);
            if (score > bestScore) { bestScore = score; best = i; }
        }
        // Layouts of related languages type most words the same (Russian and Belarusian "добрая"): such a word
        // says nothing about the language, so stay with the layout the user was typing in
        if (best >= 0 && preferred >= 0 && preferred != best && alts[preferred] == alts[best]) return preferred;
        return best;
    }
}
