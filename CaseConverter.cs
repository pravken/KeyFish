using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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

// What the text actions menu does with the selection, apart from layouts, transliteration and formatting
public static class TextActions
{
    // A capital letter only where a sentence starts: at the beginning, after . ! ? and at the start of a line
    public static string Sentence(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool start = true;
        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                sb.Append(start ? char.ToUpper(c, CultureInfo.CurrentCulture) : char.ToLower(c, CultureInfo.CurrentCulture));
                start = false;
            }
            else
            {
                sb.Append(c);
                if (c == '.' || c == '!' || c == '?' || c == '…' || c == '\n') start = true;
                else if (char.IsDigit(c)) start = false;
            }
        }
        return sb.ToString();
    }

    // One space between words, none at the ends of lines, at most one empty line in a row, none at the start and end
    public static string Spaces(string text)
    {
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>();
        foreach (string line in lines)
        {
            string clean = System.Text.RegularExpressions.Regex.Replace(line, @"[ \t   　]+", " ").Trim();
            if (clean.Length == 0 && (kept.Count == 0 || kept[kept.Count - 1].Length == 0)) continue;
            kept.Add(clean);
        }
        while (kept.Count > 0 && kept[kept.Count - 1].Length == 0) kept.RemoveAt(kept.Count - 1);
        return string.Join(nl, kept);
    }

    // The lines in alphabetical order, numbers by their value (2 before 10); a line break at the very end stays there
    public static string SortLines(string text)
    {
        string nl, end;
        var lines = Lines(text, out nl, out end);
        var culture = StringComparer.Create(CultureInfo.CurrentCulture, true);
        var sorted = lines.Select((l, i) => new { l, i }).OrderBy(x => x.l, Comparer<string>.Create((a, b) => Natural(a, b, culture)))
                          .ThenBy(x => x.i).Select(x => x.l);
        return string.Join(nl, sorted) + end;
    }

    // Repeated lines removed, the first of each kept where it was
    public static string UniqueLines(string text)
    {
        string nl, end;
        var lines = Lines(text, out nl, out end);
        var seen = new HashSet<string>();
        return string.Join(nl, lines.Where(l => seen.Add(l))) + end;
    }

    static string[] Lines(string text, out string nl, out string end)
    {
        nl = text.Contains("\r\n") ? "\r\n" : "\n";
        end = text.EndsWith("\n") ? nl : "";
        string body = text.Replace("\r\n", "\n");
        if (end.Length > 0) body = body.Substring(0, body.Length - 1);
        return body.Split('\n');
    }

    // Runs of digits compared by their value, the rest as words are
    static int Natural(string a, string b, StringComparer words)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                string x = a.Substring(si, i - si).TrimStart('0'), y = b.Substring(sj, j - sj).TrimStart('0');
                int c = x.Length != y.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y);
                if (c != 0) return c;
            }
            else
            {
                int si = i, sj = j;
                while (i < a.Length && !char.IsDigit(a[i])) i++;
                while (j < b.Length && !char.IsDigit(b[j])) j++;
                int c = words.Compare(a.Substring(si, i - si), b.Substring(sj, j - sj));
                if (c != 0) return c;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    // Straight quotes made typographic: «ёлочки» and „лапки“ inside them in Cyrillic text, “ ” and ‘ ’ in other text.
    // A quote opens at the start, after a space or an opening bracket or quote, and closes elsewhere
    public static string Quotes(string text)
    {
        bool cyrillic = text.Any(c => LayoutTable.ScriptOf(c) == "Cyrillic");
        string open1 = cyrillic ? "«" : "“", close1 = cyrillic ? "»" : "”", open2 = cyrillic ? "„" : "‘", close2 = cyrillic ? "“" : "’";
        var sb = new StringBuilder(text.Length);
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '«') depth++;
            else if (c == '»') depth = Math.Max(0, depth - 1);
            if (c != '"') { sb.Append(c); continue; }
            char prev = i > 0 ? text[i - 1] : ' ';
            bool opens = char.IsWhiteSpace(prev) || "([{«„“—–-/".IndexOf(prev) >= 0;
            if (opens) { sb.Append(depth == 0 ? open1 : open2); depth++; }
            else { depth = Math.Max(0, depth - 1); sb.Append(depth == 0 ? close1 : close2); }
        }
        return sb.ToString();
    }

    // Spaces that a line must not break at made non-breaking: after a word of one or two letters (в, и, на, a, to),
    // before a dash, between a number and the word after it (10 кг, 2026 г.), after № and §
    public static string NoBreakSpaces(string text)
    {
        string s = Regex.Replace(text, @"(?m)(?<=(?:^|[\s(«„“""'])\p{L}{1,2}) (?=\S)", " ");
        s = Regex.Replace(s, @" (?=[—–] )", " ");
        s = Regex.Replace(s, @"(?<=\d) (?=\p{L})", " ");
        return Regex.Replace(s, @"(?<=[№§]) ?(?=\d)", " ");
    }

    // Ё where the word list says the word is written with it (еще → ещё, ее → её), the case of each letter kept.
    // A word that is another word without ё (все — всё, звезды — звёзды, черта — чёрта) is left as it is
    public static string Yo(string text)
    {
        var map = YoWords();
        string s = Regex.Replace(text, @"\b([Вв])се-таки\b", m => m.Groups[1].Value + "сё-таки");
        return Regex.Replace(s, @"\p{L}+", m =>
        {
            string w = m.Value, yo;
            if (w.IndexOf('е') < 0 && w.IndexOf('Е') < 0) return w;
            if (!map.TryGetValue(w.ToLower(CultureInfo.CurrentCulture), out yo)) return w;
            var sb = new StringBuilder(w);
            for (int i = 0; i < w.Length; i++)
                if (yo[i] == 'ё') sb[i] = w[i] == 'Е' ? 'Ё' : 'ё';
            return sb.ToString();
        });
    }

    static readonly HashSet<string> YoAmbiguous = new HashSet<string>
    {
        "все", "берет", "осел", "мел", "лен", "небо", "падеж", "заем", "сел", "села", "озера", "звезды", "слезы", "колеса", "ребра",
        "бедра", "сестры", "жены", "пчелы", "весла", "гнезда", "ведра", "стекла", "черта", "черту", "чем", "совершенно", "узнаем",
        // names in the word list's films and series
        "кен", "тернера", "кертиса", "сегуна", "хека", "хена", "ена", "енджу", "ета"
    };

    static Dictionary<string, string> yoWords;

    // The Russian word list's words with ё, by their spelling without it: those written with ё at least a fifth as
    // often as without it (ещё, её; not лёт for лет), and of three letters or more, but её
    static Dictionary<string, string> YoWords()
    {
        if (yoWords != null) return yoWords;
        var counts = new Dictionary<string, long>();
        foreach (string line in Detector.Source("ru"))
        {
            int sp = line.IndexOf(' ');
            string w = sp > 0 ? line.Substring(0, sp) : line.Trim();
            long n = 1;
            if (sp > 0) long.TryParse(line.Substring(sp + 1).Trim(), out n);
            if (w.Length > 0 && !counts.ContainsKey(w)) counts[w] = n;   // the main list first: the extended one has no counts
        }
        var map = new Dictionary<string, string>();
        foreach (var kv in counts)
        {
            if (kv.Key.IndexOf('ё') < 0 || (kv.Key.Length < 3 && kv.Key != "её")) continue;
            string plain = kv.Key.Replace('ё', 'е');
            long without;
            counts.TryGetValue(plain, out without);
            if (YoAmbiguous.Contains(plain) || kv.Value * 5 < without) continue;
            map[plain] = kv.Key;
        }
        return yoWords = map;
    }

    // A name for code: the words of the selection ("user name", "userName", "user-name") joined the way asked
    public enum Code { Snake, Camel, Pascal, Kebab, Constant }

    public static string CodeName(string text, Code style)
    {
        var words = Words(text);
        if (words.Count == 0) return text;
        var ti = CultureInfo.CurrentCulture.TextInfo;
        Func<string, string> cap = w => ti.ToUpper(w[0]) + w.Substring(1);
        switch (style)
        {
            case Code.Snake: return string.Join("_", words);
            case Code.Kebab: return string.Join("-", words);
            case Code.Constant: return ti.ToUpper(string.Join("_", words));
            case Code.Camel: return words[0] + string.Concat(words.Skip(1).Select(cap));
            default: return string.Concat(words.Select(cap));
        }
    }

    // Lowercase words: split at anything not a letter or a digit, and where a capital follows a small letter (userName)
    // or ends a run of capitals (HTMLParser: html, parser)
    static List<string> Words(string text)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        Action flush = () => { if (sb.Length > 0) { words.Add(sb.ToString().ToLower(CultureInfo.CurrentCulture)); sb.Clear(); } };
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!char.IsLetterOrDigit(c)) { flush(); continue; }
            if (sb.Length > 0 && char.IsUpper(c))
            {
                char prev = text[i - 1];
                bool nextLower = i + 1 < text.Length && char.IsLower(text[i + 1]);
                if (char.IsLower(prev) || char.IsDigit(prev) || (char.IsUpper(prev) && nextLower)) flush();
            }
            sb.Append(c);
        }
        flush();
        return words;
    }
}

// Arithmetic in a selection: "1250*12*0,87" gives 13050. Numbers with a point or a comma, spaces between thousands,
// + - * / × ÷ ^ and brackets; percents the way calculators take them: 200+15% is 230, 200*15% is 30.
// A trailing "=" is allowed. Anything else in the text, or no operator at all: not an expression
public static class Calc
{
    public static double? Evaluate(string text)
    {
        string s = Normalize(text);
        if (s == null) return null;
        try
        {
            int at = 0;
            bool percent, op = false;
            double v = Sum(s, ref at, out percent, ref op);
            if (at != s.Length || !op || percent || double.IsNaN(v) || double.IsInfinity(v)) return null;
            return v;
        }
        catch (FormatException) { return null; }
    }

    // The value as the expression wrote its numbers: a decimal comma stays a comma
    public static string Format(double value, string expression)
    {
        double rounded = Math.Round(value, Math.Max(0, Math.Min(15, 12 - (int)Math.Floor(Math.Log10(Math.Max(1, Math.Abs(value)))))));
        string s = rounded.ToString("0.###############", CultureInfo.InvariantCulture);
        if (s == "-0") s = "0";
        return s.Replace('.', DecimalComma(expression) ? ',' : '.');
    }

    // Whether the expression's decimal mark is a comma: "0,87" yes; "1,234" only where the system writes decimals with
    // a comma (elsewhere it's a thousands separator)
    static bool DecimalComma(string text)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\d\.\d")) return text.LastIndexOf(',') > text.LastIndexOf('.');
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\d,\d")) return !ThousandsComma(text);
        return CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == ",";
    }

    static bool ThousandsComma(string text)
    {
        return CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator != "," &&
               System.Text.RegularExpressions.Regex.IsMatch(text, @"^[^,]*(\d,\d{3}(?!\d)[^,]*)+$");
    }

    // Operators made plain, spaces taken out, numbers written with a point: null if anything else is in the text
    static string Normalize(string text)
    {
        if (text == null || text.Length > 500) return null;
        string t = text.Trim().TrimEnd('=').Trim();
        if (t.Length == 0) return null;
        bool comma = DecimalComma(t), thousands = !comma && ThousandsComma(t);
        var sb = new StringBuilder();
        foreach (char c in t)
        {
            if (char.IsWhiteSpace(c) || c == '\'') continue;   // spaces and apostrophes between thousands
            if (c >= '0' && c <= '9') sb.Append(c);
            else if (c == ',') { if (!thousands) sb.Append(comma ? '.' : ','); }
            else if (c == '.') { if (comma) return null; sb.Append('.'); }
            else if (c == '×' || c == '·' || c == '∙' || c == '*') sb.Append('*');
            else if (c == '÷' || c == '/') sb.Append('/');
            else if (c == '−' || c == '–' || c == '-') sb.Append('-');
            else if (c == '+' || c == '^' || c == '(' || c == ')' || c == '%') sb.Append(c);
            else return null;
        }
        string s = sb.ToString();
        return s.IndexOf(',') >= 0 ? null : s;
    }

    static double Sum(string s, ref int at, out bool percent, ref bool op)
    {
        double v = Product(s, ref at, out percent, ref op);
        while (at < s.Length && (s[at] == '+' || s[at] == '-'))
        {
            char sign = s[at++];
            op = true;
            bool p;
            double r = Product(s, ref at, out p, ref op);
            if (percent) { v /= 100; percent = false; }
            if (p) r = v * r / 100;   // 200+15%: fifteen percent of 200
            v = sign == '+' ? v + r : v - r;
        }
        return v;
    }

    static double Product(string s, ref int at, out bool percent, ref bool op)
    {
        double v = Power(s, ref at, out percent, ref op);
        while (at < s.Length && (s[at] == '*' || s[at] == '/'))
        {
            char sign = s[at++];
            op = true;
            bool p;
            double r = Power(s, ref at, out p, ref op);
            if (percent) { v /= 100; percent = false; }
            if (p) r /= 100;
            v = sign == '*' ? v * r : v / r;
        }
        return v;
    }

    static double Power(string s, ref int at, out bool percent, ref bool op)
    {
        double v = Unary(s, ref at, out percent, ref op);
        if (at < s.Length && s[at] == '^')
        {
            at++;
            op = true;
            bool p;
            double r = Power(s, ref at, out p, ref op);
            if (percent) { v /= 100; percent = false; }
            if (p) r /= 100;
            v = Math.Pow(v, r);
        }
        return v;
    }

    static double Unary(string s, ref int at, out bool percent, ref bool op)
    {
        if (at < s.Length && (s[at] == '-' || s[at] == '+'))
        {
            bool minus = s[at++] == '-';
            double v = Unary(s, ref at, out percent, ref op);
            return minus ? -v : v;
        }
        double value;
        if (at < s.Length && s[at] == '(')
        {
            at++;
            bool inner;
            value = Sum(s, ref at, out inner, ref op);
            if (inner) value /= 100;
            if (at >= s.Length || s[at] != ')') throw new FormatException();
            at++;
        }
        else
        {
            int start = at;
            while (at < s.Length && (char.IsDigit(s[at]) || s[at] == '.')) at++;
            if (at == start || !double.TryParse(s.Substring(start, at - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
                throw new FormatException();
        }
        percent = at < s.Length && s[at] == '%';
        if (percent) at++;
        return value;
    }
}
