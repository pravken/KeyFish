using System.Collections.Generic;
using System.Text;

public static class Converter
{
    // The same keys in the US QWERTY and Russian ЙЦУКЕН layouts
    const string En = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./" +
                      "~QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?" +
                      "@#$^&";
    const string Ru = "ёйцукенгшщзхъфывапролджэячсмитьбю." +
                      "ЁЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ," +
                      "\"№;:?";

    static readonly Dictionary<char, char> EnToRu = new Dictionary<char, char>();
    static readonly Dictionary<char, char> RuToEn = new Dictionary<char, char>();

    static Converter()
    {
        for (int i = 0; i < En.Length; i++)
        {
            EnToRu[En[i]] = Ru[i];
            RuToEn[Ru[i]] = En[i];
        }
    }

    // The direction is chosen by which letters the text has more of
    public static bool IsMostlyRussian(string text)
    {
        int lat = 0, cyr = 0;
        foreach (char c in text)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) lat++;
            else if ((c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё') cyr++;
        }
        return cyr > lat;
    }

    public static string Convert(string text, bool toRussian)
    {
        var map = toRussian ? EnToRu : RuToEn;
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            char r;
            sb.Append(map.TryGetValue(c, out r) ? r : c);
        }
        return sb.ToString();
    }
}
