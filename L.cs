using System.Globalization;

// Язык интерфейса: каждый текст записан парой «по-русски, по-английски»
static class L
{
    // 0 — как в Windows, 1 — русский, 2 — английский
    public static int Setting;

    public static bool Ru
    {
        get { return Setting == 1 || (Setting == 0 && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"); }
    }

    public static string T(string ru, string en) { return Ru ? ru : en; }
}
