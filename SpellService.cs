using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

// The built-in Windows spell checker (Spell Checking API, Windows 8+)
public class SpellService
{
    readonly Dictionary<string, ISpellChecker> checkers = new Dictionary<string, ISpellChecker>();
    readonly ISpellCheckerFactory factory;

    public SpellService()
    {
        factory = (ISpellCheckerFactory)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")));
    }

    public bool IsSupported(string lang)
    {
        int ok;
        factory.IsSupported(lang, out ok);
        return ok != 0;
    }

    ISpellChecker Get(string lang)
    {
        ISpellChecker c;
        if (!checkers.TryGetValue(lang, out c))
        {
            c = IsSupported(lang) ? factory.CreateSpellChecker(lang) : null;
            checkers[lang] = c;
        }
        return c;
    }

    // A confident typo fix for a single word, or null.
    // frequency is the word's count in our dictionary (to choose between similar suggestions)
    public string Fix(string word, string lang, Func<string, long> frequency)
    {
        var checker = Get(lang);
        if (checker == null) return null;

        IEnumSpellingError errors = checker.Check(word);
        ISpellingError error;
        if (errors.Next(out error) != 0 || error == null) return null; // no mistakes
        if (error.StartIndex != 0 || error.Length != word.Length) return null;

        CorrectiveAction action = error.CorrectiveAction;
        if (action == CorrectiveAction.Replace)
        {
            string r = error.Replacement;
            return Distance(word.ToLowerInvariant(), r.ToLowerInvariant()) <= 2 ? r : null;
        }
        if (action != CorrectiveAction.GetSuggestions) return null;

        var close = new List<string>();
        IEnumString suggestions = checker.Suggest(word);
        var buf = new string[1];
        int seen = 0;
        while (seen++ < 10 && suggestions.Next(1, buf, IntPtr.Zero) == 0)
        {
            string s = buf[0];
            if (s.IndexOf(' ') >= 0 || s.IndexOf('-') >= 0) continue;
            if (Distance(word.ToLowerInvariant(), s.ToLowerInvariant()) == 1) close.Add(s);
        }
        if (close.Count == 0) return null;
        if (close.Count == 1) return close[0];

        // Several suggestions one edit away: take one only if it is clearly more frequent
        close.Sort((a, b) => frequency(b.ToLowerInvariant()).CompareTo(frequency(a.ToLowerInvariant())));
        long f1 = frequency(close[0].ToLowerInvariant()), f2 = frequency(close[1].ToLowerInvariant());
        return f1 > 0 && f1 >= f2 * 20 ? close[0] : null;
    }

    // Damerau–Levenshtein distance: insertion, deletion, substitution, transposition of adjacent letters
    public static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    enum CorrectiveAction { None = 0, GetSuggestions = 1, Replace = 2, Delete = 3 }

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellCheckerFactory
    {
        IEnumString SupportedLanguages { get; }
        void IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out int value);
        ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellChecker
    {
        string LanguageTag { [return: MarshalAs(UnmanagedType.LPWStr)] get; }
        IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);
        IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
        // The remaining interface methods aren't used, but their vtable slots must be declared
        void Add([MarshalAs(UnmanagedType.LPWStr)] string word);
        void Ignore([MarshalAs(UnmanagedType.LPWStr)] string word);
        void AutoCorrect([MarshalAs(UnmanagedType.LPWStr)] string from, [MarshalAs(UnmanagedType.LPWStr)] string to);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumSpellingError
    {
        [PreserveSig] int Next(out ISpellingError value);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellingError
    {
        uint StartIndex { get; }
        uint Length { get; }
        CorrectiveAction CorrectiveAction { get; }
        string Replacement { [return: MarshalAs(UnmanagedType.LPWStr)] get; }
    }
}
