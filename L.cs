using System.Globalization;

// UI language: every text is written as a pair (Russian, English)
static class L
{
    // 0 — same as Windows, 1 — Russian, 2 — English
    public static int Setting;

    public static bool Ru
    {
        get { return Setting == 1 || (Setting == 0 && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"); }
    }

    public static string T(string ru, string en) { return Ru ? ru : en; }
}
