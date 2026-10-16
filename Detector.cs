using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

// Модель языка: частотный словарь + статистика сочетаний из трёх букв
public class LangModel
{
    readonly Dictionary<string, long> words = new Dictionary<string, long>();
    readonly Dictionary<string, double> tri = new Dictionary<string, double>();
    readonly Dictionary<string, double> ctx = new Dictionary<string, double>();
    readonly int alphabet;

    public LangModel(IEnumerable<string> lines, int alphabetSize)
    {
        alphabet = alphabetSize + 1;
        foreach (string line in lines)
        {
            int sp = line.IndexOf(' ');
            if (sp <= 0) continue;
            string w = line.Substring(0, sp);
            long count;
            if (!long.TryParse(line.Substring(sp + 1), out count)) continue;
            words[w] = count;

            double weight = Math.Log(count + 1);
            string p = "^^" + w + "$";
            for (int i = 2; i < p.Length; i++)
            {
                Add(tri, p.Substring(i - 2, 3), weight);
                Add(ctx, p.Substring(i - 2, 2), weight);
            }
        }
    }

    static void Add(Dictionary<string, double> d, string k, double v)
    {
        double cur;
        d.TryGetValue(k, out cur);
        d[k] = cur + v;
    }

    public long Count(string word)
    {
        long c;
        return words.TryGetValue(word, out c) ? c : 0;
    }

    // Средний логарифм вероятности на символ: чем выше, тем «естественнее» слово для языка
    public double Score(string word)
    {
        string p = "^^" + word + "$";
        double sum = 0;
        for (int i = 2; i < p.Length; i++)
        {
            double t, c;
            tri.TryGetValue(p.Substring(i - 2, 3), out t);
            ctx.TryGetValue(p.Substring(i - 2, 2), out c);
            sum += Math.Log((t + 0.1) / (c + 0.1 * alphabet));
        }
        return sum / (p.Length - 2);
    }
}

public static class Detector
{
    public static LangModel En, Ru;

    // Порог разницы правдоподобия для слов, которых нет в словарях
    public static double Threshold = 1.5;
    // Во сколько раз слово в другой раскладке должно быть частотнее набранного (без контекста)
    public static double FrequencyRatio = 200;

    static readonly HashSet<string> EnSingles = new HashSet<string> { "a", "i" };
    static readonly HashSet<string> RuSingles = new HashSet<string> { "а", "в", "и", "к", "о", "с", "у", "я" };

    public static void LoadEmbedded()
    {
        En = new LangModel(ReadResource("en.txt"), 26);
        Ru = new LangModel(ReadResource("ru.txt"), 33);
    }

    static IEnumerable<string> ReadResource(string name)
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (var r = new StreamReader(s, Encoding.UTF8))
        {
            string line;
            while ((line = r.ReadLine()) != null) yield return line;
        }
    }

    const string LeadPunct = "([{\"«";
    const string TrailPunct = ".,!?:;)]}\"»";

    // Слово без окружающей пунктуации; null, если внутри есть не-буквы (значит, это не слово)
    public static string Core(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && LeadPunct.IndexOf(s[a]) >= 0) a++;
        while (b > a && TrailPunct.IndexOf(s[b - 1]) >= 0) b--;
        if (a == b) return "";
        var sb = new StringBuilder(b - a);
        for (int i = a; i < b; i++)
        {
            char c = s[i];
            if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c == '-' && i > a && i < b - 1) sb.Append(c);
            else return null;
        }
        return sb.ToString();
    }

    // typed — слово как набрано, alt — те же клавиши в другой раскладке.
    // context: язык предыдущего слова (true — русский, false — английский, null — неизвестно)
    public static bool ShouldConvert(string typed, string alt, bool typedRu, bool? context = null)
    {
        LangModel T = typedRu ? Ru : En, A = typedRu ? En : Ru;
        string t = Core(typed), a = Core(alt);
        if (string.IsNullOrEmpty(a)) return false;
        bool ctxAlt = context.HasValue && context.Value != typedRu;
        bool ctxSame = context.HasValue && context.Value == typedRu;

        if (a.Length == 1)
        {
            if (t == null) return A == Ru ? RuSingles.Contains(a) : EnSingles.Contains(a);
            if (ctxSame || char.IsUpper(typed[0])) return false;
            var tSingles = typedRu ? RuSingles : EnSingles;
            var aSingles = typedRu ? EnSingles : RuSingles;
            return t.Length == 1 && !tSingles.Contains(t) && aSingles.Contains(a);
        }

        long ct = string.IsNullOrEmpty(t) ? 0 : T.Count(t);
        long ca = A.Count(a);

        // Набранное — вообще не слово (знаки внутри), а в другой раскладке слово есть
        if (t == null) return ca > 0 || A.Score(a) > -3.2;

        if (ct > 0 && ca > 0)
            return ctxAlt ? ca > ct : (!ctxSame && ca > ct * FrequencyRatio);
        if (ct > 0) return false;
        if (ca > 0) return !ctxSame || A.Score(a) > T.Score(t) + 1;

        if (a.Length < 3) return false;
        double th = ctxAlt ? Threshold - 0.5 : ctxSame ? Threshold + 0.5 : Threshold;
        return A.Score(a) - T.Score(t) > th;
    }
}
