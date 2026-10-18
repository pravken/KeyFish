using System.Collections.Generic;
using System.Text;

// Cyrillic ⇄ Latin transliteration by familiar rules (like in passports, ё → yo)
public static class Transliterator
{
    static readonly Dictionary<char, string> ToLat = new Dictionary<char, string>
    {
        { 'а', "a" }, { 'б', "b" }, { 'в', "v" }, { 'г', "g" }, { 'д', "d" }, { 'е', "e" }, { 'ё', "yo" },
        { 'ж', "zh" }, { 'з', "z" }, { 'и', "i" }, { 'й', "y" }, { 'к', "k" }, { 'л', "l" }, { 'м', "m" },
        { 'н', "n" }, { 'о', "o" }, { 'п', "p" }, { 'р', "r" }, { 'с', "s" }, { 'т', "t" }, { 'у', "u" },
        { 'ф', "f" }, { 'х', "kh" }, { 'ц', "ts" }, { 'ч', "ch" }, { 'ш', "sh" }, { 'щ', "shch" },
        { 'ъ', "" }, { 'ы', "y" }, { 'ь', "" }, { 'э', "e" }, { 'ю', "yu" }, { 'я', "ya" }
    };

    // Longer combinations first, then single letters
    static readonly string[][] ToCyr =
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

    const string Vowels = "aeiouаеёиоуыэюя";

    public static string Convert(string text)
    {
        return Converter.IsMostlyRussian(text) ? ToLatin(text) : ToCyrillic(text);
    }

    public static string ToLatin(string text)
    {
        var sb = new StringBuilder(text.Length * 2);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            string lat;
            if (!ToLat.TryGetValue(char.ToLowerInvariant(c), out lat)) { sb.Append(c); continue; }
            if (char.IsUpper(c) && lat.Length > 0)
                lat = WordIsUpper(text, i) ? lat.ToUpperInvariant() : char.ToUpperInvariant(lat[0]) + lat.Substring(1);
            sb.Append(lat);
        }
        return sb.ToString();
    }

    public static string ToCyrillic(string text)
    {
        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z')) { sb.Append(c); i++; continue; }

            string rest = text.Substring(i, System.Math.Min(4, text.Length - i)).ToLowerInvariant();
            string cyr = null; int len = 1;
            foreach (var pair in ToCyr)
                if (rest.StartsWith(pair[0])) { cyr = pair[1]; len = pair[0].Length; break; }

            bool wordStart = i == 0 || !char.IsLetter(text[i - 1]);
            char prev = i > 0 ? char.ToLowerInvariant(text[i - 1]) : ' ';
            if (len == 1 && rest[0] == 'e' && wordStart) cyr = "э";                            // eto → это (a word-initial e becomes э)
            if (len == 1 && rest[0] == 'y' && Vowels.IndexOf(prev) >= 0) cyr = "й";          // Aleksey → Алексей (y after a vowel becomes й)
            if (len == 2 && rest.StartsWith("ye") && !wordStart && Vowels.IndexOf(prev) < 0) cyr = "ые"; // nye → ные (ye after a consonant becomes ые)

            if (char.IsUpper(c))
                cyr = WordIsUpper(text, i) ? cyr.ToUpperInvariant() : char.ToUpperInvariant(cyr[0]) + cyr.Substring(1);
            sb.Append(cyr);
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
