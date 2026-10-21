using System.Globalization;

// UI language: every text is written as a pair (Russian, English)
static class L
{
    // 0 — same as Windows, then the languages in Choices order
    public static int Setting;

    public static string[] Choices { get { return new[] { T("Как в Windows", "Same as Windows"), "Русский", "English" }; } }

    public static bool Ru
    {
        get { return Setting == 1 || (Setting == 0 && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"); }
    }

    public static string T(string ru, string en) { return Ru ? ru : en; }
}