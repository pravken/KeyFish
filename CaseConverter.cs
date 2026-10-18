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
