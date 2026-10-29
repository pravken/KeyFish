using System.Collections.Generic;
using System.Text;

// Transliteration between a Latin and a non-Latin alphabet by each language's familiar rules: Russian (like in
// passports, ё → yo), Ukrainian and Belarusian (their national systems, г → h), Serbian (its own Latin alphabet,
// one letter for one) and Greek (ELOT 743). To Latin, the language is told by the text's own letters; from Latin,
// it is the language of the user's other layout
public static class Transliterator
{
    static readonly Dictionary<char, string> RuToLat = new Dictionary<char, string>
    {
        { 'а', "a" }, { 'б', "b" }, { 'в', "v" }, { 'г', "g" }, { 'д', "d" }, { 'е', "e" }, { 'ё', "yo" },
        { 'ж', "zh" }, { 'з', "z" }, { 'и', "i" }, { 'й', "y" }, { 'к', "k" }, { 'л', "l" }, { 'м', "m" },
        { 'н', "n" }, { 'о', "o" }, { 'п', "p" }, { 'р', "r" }, { 'с', "s" }, { 'т', "t" }, { 'у', "u" },
        { 'ф', "f" }, { 'х', "kh" }, { 'ц', "ts" }, { 'ч', "ch" }, { 'ш', "sh" }, { 'щ', "shch" },
        { 'ъ', "" }, { 'ы', "y" }, { 'ь', "" }, { 'э', "e" }, { 'ю', "yu" }, { 'я', "ya" }
    };

    static readonly Dictionary<char, string> UkToLat = new Dictionary<char, string>
    {
        { 'а', "a" }, { 'б', "b" }, { 'в', "v" }, { 'г', "h" }, { 'ґ', "g" }, { 'д', "d" }, { 'е', "e" }, { 'є', "ie" },
        { 'ж', "zh" }, { 'з', "z" }, { 'и', "y" }, { 'і', "i" }, { 'ї', "i" }, { 'й', "i" }, { 'к', "k" }, { 'л', "l" },
        { 'м', "m" }, { 'н', "n" }, { 'о', "o" }, { 'п', "p" }, { 'р', "r" }, { 'с', "s" }, { 'т', "t" }, { 'у', "u" },
        { 'ф', "f" }, { 'х', "kh" }, { 'ц', "ts" }, { 'ч', "ch" }, { 'ш', "sh" }, { 'щ', "shch" }, { 'ь', "" },
        { 'ю', "iu" }, { 'я', "ia" }, { '\'', "" }, { '’', "" }
    };

    // At the start of a word Ukrainian є ї й ю я are written ye yi y yu ya
    static readonly Dictionary<char, string> UkWordStart = new Dictionary<char, string>
    {
        { 'є', "ye" }, { 'ї', "yi" }, { 'й', "y" }, { 'ю', "yu" }, { 'я', "ya" }
    };

    static readonly Dictionary<char, string> BeToLat = new Dictionary<char, string>
    {
        { 'а', "a" }, { 'б', "b" }, { 'в', "v" }, { 'г', "h" }, { 'д', "d" }, { 'е', "ie" }, { 'ё', "io" },
        { 'ж', "zh" }, { 'з', "z" }, { 'і', "i" }, { 'й', "j" }, { 'к', "k" }, { 'л', "l" }, { 'м', "m" },
        { 'н', "n" }, { 'о', "o" }, { 'п', "p" }, { 'р', "r" }, { 'с', "s" }, { 'т', "t" }, { 'у', "u" },
        { 'ў', "w" }, { 'ф', "f" }, { 'х', "ch" }, { 'ц', "c" }, { 'ч', "ch" }, { 'ш', "sh" }, { 'ы', "y" },
        { 'ь', "" }, { 'э', "e" }, { 'ю', "iu" }, { 'я', "ia" }, { '\'', "" }, { '’', "" }
    };

    static readonly Dictionary<char, string> SrToLat = new Dictionary<char, string>
    {
        { 'а', "a" }, { 'б', "b" }, { 'в', "v" }, { 'г', "g" }, { 'д', "d" }, { 'ђ', "đ" }, { 'е', "e" }, { 'ж', "ž" },
        { 'з', "z" }, { 'и', "i" }, { 'ј', "j" }, { 'к', "k" }, { 'л', "l" }, { 'љ', "lj" }, { 'м', "m" }, { 'н', "n" },
        { 'њ', "nj" }, { 'о', "o" }, { 'п', "p" }, { 'р', "r" }, { 'с', "s" }, { 'т', "t" }, { 'ћ', "ć" }, { 'у', "u" },
        { 'ф', "f" }, { 'х', "h" }, { 'ц', "c" }, { 'ч', "č" }, { 'џ', "dž" }, { 'ш', "š" }
    };

    static readonly Dictionary<char, string> ElToLat = new Dictionary<char, string>
    {
        { 'α', "a" }, { 'ά', "a" }, { 'β', "v" }, { 'γ', "g" }, { 'δ', "d" }, { 'ε', "e" }, { 'έ', "e" }, { 'ζ', "z" },
        { 'η', "i" }, { 'ή', "i" }, { 'θ', "th" }, { 'ι', "i" }, { 'ί', "i" }, { 'ϊ', "i" }, { 'ΐ', "i" }, { 'κ', "k" },
        { 'λ', "l" }, { 'μ', "m" }, { 'ν', "n" }, { 'ξ', "x" }, { 'ο', "o" }, { 'ό', "o" }, { 'π', "p" }, { 'ρ', "r" },
        { 'σ', "s" }, { 'ς', "s" }, { 'τ', "t" }, { 'υ', "y" }, { 'ύ', "y" }, { 'ϋ', "y" }, { 'ΰ', "y" }, { 'φ', "f" },
        { 'χ', "ch" }, { 'ψ', "ps" }, { 'ω', "o" }, { 'ώ', "o" }
    };

    // From Latin: longer combinations first, then single letters
    static readonly string[][] ToRu =
    {
        new[] { "shch", "щ" }, new[] { "sch", "щ" }, new[] { "zh", "ж" }, new[] { "kh", "х" }, new[] { "ts", "ц" },
        new[] { "ch", "ч" }, new[] { "sh", "ш" }, new[] { "yo", "ё" }, new[] { "yu", "ю" }, new[] { "ya", "я" },
        new[] { "ye", "е" }, new[] { "a", "а" }, new[] { "b", "б" }, new[] { "c", "к" }, new[] { "d", "д" },
        new[] { "e", "е" }, new[] { "f", "ф" }, new[] { "g", "г" }, new[] { "h", "х" }, new[] { "i", "и" },
        new[] { "j", "й" }, new[] { "k", "к" }, new[] { "l", "л" }, new[] { "m", "м" }, new[] { "n", "н" },
        new[] { "o", "о" }, new[] { "p", "п" }, new[] { "q", "к" }, new[] { "r", "р" }, new[] { "s", "с" },
        new[] { "t", "т" }, new[] { "u", "у" }, new[] { "v", "в" }, new[] { "w", "в" }, new[] { "x", "кс" },
        new[] { "y", "ы" }, new[] { "z", "з" }
    };

    static readonly string[][] ToUk =
    {
        new[] { "shch", "щ" }, new[] { "zh", "ж" }, new[] { "kh", "х" }, new[] { "ts", "ц" }, new[] { "ch", "ч" },
        new[] { "sh", "ш" }, new[] { "ye", "є" }, new[] { "yi", "ї" }, new[] { "yu", "ю" }, new[] { "ya", "я" },
        new[] { "ie", "є" }, new[] { "iu", "ю" }, new[] { "ia", "я" },
        new[] { "a", "а" }, new[] { "b", "б" }, new[] { "c", "ц" }, new[] { "d", "д" }, new[] { "e", "е" },
        new[] { "f", "ф" }, new[] { "g", "ґ" }, new[] { "h", "г" }, new[] { "i", "і" }, new[] { "j", "й" },
        new[] { "k", "к" }, new[] { "l", "л" }, new[] { "m", "м" }, new[] { "n", "н" }, new[] { "o", "о" },
        new[] { "p", "п" }, new[] { "q", "к" }, new[] { "r", "р" }, new[] { "s", "с" }, new[] { "t", "т" },
        new[] { "u", "у" }, new[] { "v", "в" }, new[] { "w", "в" }, new[] { "x", "кс" }, new[] { "y", "и" }, new[] { "z", "з" }
    };

    static readonly string[][] ToBe =
    {
        new[] { "zh", "ж" }, new[] { "ch", "ч" }, new[] { "sh", "ш" }, new[] { "ie", "е" }, new[] { "io", "ё" },
        new[] { "iu", "ю" }, new[] { "ia", "я" }, new[] { "ye", "е" }, new[] { "yu", "ю" }, new[] { "ya", "я" },
        new[] { "a", "а" }, new[] { "b", "б" }, new[] { "c", "ц" }, new[] { "d", "д" }, new[] { "e", "э" },
        new[] { "f", "ф" }, new[] { "g", "г" }, new[] { "h", "г" }, new[] { "i", "і" }, new[] { "j", "й" },
        new[] { "k", "к" }, new[] { "l", "л" }, new[] { "m", "м" }, new[] { "n", "н" }, new[] { "o", "о" },
        new[] { "p", "п" }, new[] { "q", "к" }, new[] { "r", "р" }, new[] { "s", "с" }, new[] { "t", "т" },
        new[] { "u", "у" }, new[] { "v", "в" }, new[] { "w", "ў" }, new[] { "x", "кс" }, new[] { "y", "ы" }, new[] { "z", "з" }
    };

    static readonly string[][] ToSr =
    {
        new[] { "dž", "џ" }, new[] { "lj", "љ" }, new[] { "nj", "њ" }, new[] { "dj", "ђ" },
        new[] { "a", "а" }, new[] { "b", "б" }, new[] { "c", "ц" }, new[] { "č", "ч" }, new[] { "ć", "ћ" },
        new[] { "d", "д" }, new[] { "đ", "ђ" }, new[] { "e", "е" }, new[] { "f", "ф" }, new[] { "g", "г" },
        new[] { "h", "х" }, new[] { "i", "и" }, new[] { "j", "ј" }, new[] { "k", "к" }, new[] { "l", "л" },
        new[] { "m", "м" }, new[] { "n", "н" }, new[] { "o", "о" }, new[] { "p", "п" }, new[] { "r", "р" },
        new[] { "s", "с" }, new[] { "š", "ш" }, new[] { "t", "т" }, new[] { "u", "у" }, new[] { "v", "в" },
        new[] { "z", "з" }, new[] { "ž", "ж" }, new[] { "q", "к" }, new[] { "w", "в" }, new[] { "x", "кс" }, new[] { "y", "ј" }
    };

    static readonly string[][] ToEl =
    {
        new[] { "th", "θ" }, new[] { "ch", "χ" }, new[] { "ps", "ψ" }, new[] { "ks", "ξ" },
        new[] { "a", "α" }, new[] { "b", "β" }, new[] { "c", "κ" }, new[] { "d", "δ" }, new[] { "e", "ε" },
        new[] { "f", "φ" }, new[] { "g", "γ" }, new[] { "h", "χ" }, new[] { "i", "ι" }, new[] { "j", "ι" },
        new[] { "k", "κ" }, new[] { "l", "λ" }, new[] { "m", "μ" }, new[] { "n", "ν" }, new[] { "o", "ο" },
        new[] { "p", "π" }, new[] { "q", "κ" }, new[] { "r", "ρ" }, new[] { "s", "σ" }, new[] { "t", "τ" },
        new[] { "u", "ου" }, new[] { "v", "β" }, new[] { "w", "ω" }, new[] { "x", "ξ" }, new[] { "y", "υ" }, new[] { "z", "ζ" }
    };

    const string Vowels = "aeiouаеёиоуыэюяіїєαεηιουω";

    // lang: the language of the user's other layout (ru, uk, be, sr-cyrl, el), for text going from Latin
    public static string Convert(string text) { return Convert(text, "ru"); }

    public static string Convert(string text, string lang)
    {
        string source = SourceLanguage(text);
        // Ukrainian and Belarusian words without their own letters (знешкодження) look Russian: the user's layout decides
        if (source == "ru" && (lang == "uk" || lang == "be")) source = lang;
        return source != null ? ToLatin(text, source) : FromLatin(text, lang);
    }

    // The language of the non-Latin letters, by the letters only some alphabets have; null if Latin prevails
    static string SourceLanguage(string text)
    {
        int lat = 0, other = 0;
        bool uk = false, be = false, sr = false, el = false;
        foreach (char ch in text)
        {
            char c = char.ToLowerInvariant(ch);
            if (c >= 'a' && c <= 'z') { lat++; continue; }
            if (c >= 'α' && c <= 'ώ') { other++; el = true; continue; }
            if (c < 'а' || c > 'ӿ') continue;
            other++;
            if ("ґєї".IndexOf(c) >= 0) uk = true;
            else if (c == 'ў') be = true;
            else if ("ђјљњћџ".IndexOf(c) >= 0) sr = true;
        }
        if (other <= lat) return null;
        return el ? "el" : sr ? "sr-cyrl" : be ? "be" : uk ? "uk" : "ru";
    }

    public static string ToLatin(string text) { return ToLatin(text, "ru"); }

    public static string ToLatin(string text, string lang)
    {
        var table = lang == "uk" ? UkToLat : lang == "be" ? BeToLat : lang == "sr-cyrl" ? SrToLat : lang == "el" ? ElToLat : RuToLat;
        var sb = new StringBuilder(text.Length * 2);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char low = char.ToLowerInvariant(c);
            string lat;
            bool wordStart = i == 0 || !char.IsLetter(text[i - 1]);
            if (!(lang == "uk" && wordStart && UkWordStart.TryGetValue(low, out lat)) && !table.TryGetValue(low, out lat)) { sb.Append(c); continue; }
            if (char.IsUpper(c) && lat.Length > 0)
                lat = WordIsUpper(text, i) ? lat.ToUpperInvariant() : char.ToUpperInvariant(lat[0]) + lat.Substring(1);
            sb.Append(lat);
        }
        return sb.ToString();
    }

    public static string ToCyrillic(string text) { return FromLatin(text, "ru"); }

    public static string FromLatin(string text, string lang)
    {
        var table = lang == "uk" ? ToUk : lang == "be" ? ToBe : lang == "sr-cyrl" || lang == "sr-latn" ? ToSr : lang == "el" ? ToEl : ToRu;
        bool ru = table == ToRu, el = table == ToEl;
        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            char low = char.ToLowerInvariant(c);
            bool latin = (low >= 'a' && low <= 'z') || "čćđšž".IndexOf(low) >= 0;
            if (!latin) { sb.Append(c); i++; continue; }

            string rest = text.Substring(i, System.Math.Min(4, text.Length - i)).ToLowerInvariant();
            string res = null; int len = 1;
            foreach (var pair in table)
                if (rest.StartsWith(pair[0])) { res = pair[1]; len = pair[0].Length; break; }
            if (res == null) { sb.Append(c); i++; continue; }

            bool wordStart = i == 0 || !char.IsLetter(text[i - 1]);
            char prev = i > 0 ? char.ToLowerInvariant(text[i - 1]) : ' ';
            bool wordEnd = i + len >= text.Length || !char.IsLetter(text[i + len]);
            if (ru)
            {
                if (len == 1 && rest[0] == 'e' && wordStart) res = "э";                              // eto → это (a word-initial e becomes э)
                if (len == 1 && rest[0] == 'y' && Vowels.IndexOf(prev) >= 0) res = "й";            // Aleksey → Алексей (y after a vowel becomes й)
                if (len == 2 && rest.StartsWith("ye") && !wordStart && Vowels.IndexOf(prev) < 0) res = "ые"; // nye → ные
            }
            if (el && res == "σ" && wordEnd) res = "ς";                                              // final sigma

            if (char.IsUpper(c))
                res = WordIsUpper(text, i) ? res.ToUpperInvariant() : char.ToUpperInvariant(res[0]) + res.Substring(1);
            sb.Append(res);
            i += len;
        }
        return sb.ToString();
    }

    // The whole word is uppercase (ЖУК → ZHUK), not just capitalized (Жук → Zhuk)
    static bool WordIsUpper(string text, int i)
    {
        int letters = 0, upper = 0;
        for (int j = i; j >= 0 && char.IsLetter(text[j]); j--) { letters++; if (char.IsUpper(text[j])) upper++; }
        for (int j = i + 1; j < text.Length && char.IsLetter(text[j]); j++) { letters++; if (char.IsUpper(text[j])) upper++; }
        return letters > 1 && upper == letters;
    }
}
