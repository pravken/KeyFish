using System.Globalization;
using System.Text;

public static class CaseConverter
{
    // Cycle like in Word: lowercase → Title Case → UPPERCASE → lowercase
    public static string Next(string text)
    {
        if (!HasLetters(text)) return text;
        string lower = text.ToLower(CultureInfo.CurrentCulture);
        string upper = text.ToUpper(CultureInfo.CurrentCulture);
        string title = ToTitle(text);

        if (text == lower) return title;
        if (text == upper) return lower;
        if (text == title) return upper;
        return lower;
    }

    public static string ToTitle(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool wordStart = true;
        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                sb.Append(wordStart ? char.ToUpper(c, CultureInfo.CurrentCulture) : char.ToLower(c, CultureInfo.CurrentCulture));
                wordStart = false;
            }
            else
            {
                sb.Append(c);
                // Digits and apostrophes belong to the word: 2nd, don't
                if (char.IsDigit(c)) wordStart = false;
                else if (c != '\'' && c != '’') wordStart = true;
            }
        }
        return sb.ToString();
    }

    static bool HasLetters(string text)
    {
        foreach (char c in text) if (char.IsLetter(c)) return true;
        return false;
    }
}

// Text without its formatting: pasted back as plain text it loses bold, italics, colors, links and fonts. On top of
// that, letters "styled" with Unicode (𝐛𝐨𝐥𝐝, 𝘪𝘵𝘢𝘭𝘪𝘤, ｆｕｌｌ, Ⓐ) become ordinary ones, the marks that strike
// through or underline letters (s̶t̶r̶i̶k̶e̶) go, and so do invisible characters (zero-width spaces, soft hyphens)
public static class PlainText
{
    public static string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            // Mathematical letters and digits (U+1D400–U+1D7FF) are pairs of UTF-16 units
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                int cp = char.ConvertToUtf32(c, text[i + 1]);
                string pair = text.Substring(i, 2);
                sb.Append(cp >= 0x1D400 && cp <= 0x1D7FF ? pair.Normalize(NormalizationForm.FormKC) : pair);
                i++;
                continue;
            }
            if ((c >= '\uFF01' && c <= '\uFF5E') || (c >= '\u24B6' && c <= '\u24E9')) { sb.Append(c.ToString().Normalize(NormalizationForm.FormKC)); continue; }
            if (c == '\u3000') { sb.Append(' '); continue; }   // a full-width space
            if (c == '\u200B' || c == '\u2060' || c == '\uFEFF' || c == '\u00AD') continue;
            if (c >= '\u0332' && c <= '\u0338' && c != '\u0333' && c != '\u0334') continue;   // strike-through and underline marks
            sb.Append(c);
        }
        return sb.ToString();
    }
}
