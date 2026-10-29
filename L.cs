using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// UI language. Every text in the code is a pair (Russian, English); other languages are tables
// in i18n/<code>.txt (embedded as i18n-<code>.txt): "English text<TAB>translation" per line, \n for a line break.
// A text missing from a table is shown in English
static class L
{
    // Setting: 0 — same as Windows, then the languages in Codes order (1 — Russian, 2 — English, as before 2.0)
    public static readonly string[] Codes = { "ru", "en", "uk", "be", "de", "pl", "sr", "fr", "es" };
    static readonly string[] Names = { "Русский", "English", "Українська", "Беларуская", "Deutsch", "Polski", "Српски", "Français", "Español" };

    static int setting;
    static Dictionary<string, string> table;

    public static int Setting
    {
        get { return setting; }
        set { setting = value; table = Load(Code); }
    }

    public static string[] Choices
    {
        get
        {
            var list = new List<string> { T("Язык системы", "System language") };
            list.AddRange(Names);
            return list.ToArray();
        }
    }

    // The language in use: the chosen one, or Windows' own if KeyFish has it, otherwise English
    public static string Code
    {
        get
        {
            if (setting > 0 && setting <= Codes.Length) return Codes[setting - 1];
            string win = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Array.IndexOf(Codes, win) >= 0 ? win : "en";
        }
    }

    public static bool Ru { get { return Code == "ru"; } }

    // Number and date formats of the interface language; made once per language (the tray asks several times a second)
    static CultureInfo culture;
    static string cultureCode;
    public static CultureInfo Culture
    {
        get
        {
            string code = Code;
            if (culture == null || code != cultureCode)
            {
                try { culture = new CultureInfo(code == "sr" ? "sr-Cyrl-RS" : code); } catch { culture = CultureInfo.InvariantCulture; }
                cultureCode = code;
            }
            return culture;
        }
    }

    public static string T(string ru, string en)
    {
        if (Ru) return ru;
        string t;
        return table != null && table.TryGetValue(en, out t) ? t : en;
    }

    // A text with values in it: {0}, {1}…
    public static string F(string ru, string en, params object[] args) { return string.Format(T(ru, en), args); }

    static Dictionary<string, string> Load(string code)
    {
        if (code == "ru" || code == "en") return null;
        var map = new Dictionary<string, string>();
        try
        {
            using (var s = typeof(L).Assembly.GetManifestResourceStream("i18n-" + code + ".txt"))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        int tab = line.IndexOf('\t');
                        if (tab <= 0 || line.StartsWith("#")) continue;
                        map[Unescape(line.Substring(0, tab))] = Unescape(line.Substring(tab + 1));
                    }
                }
            }
        }
        catch { }
        return map;
    }

    static string Unescape(string s) { return s.Replace("\\n", "\n").Replace("\\r", "\r"); }
}