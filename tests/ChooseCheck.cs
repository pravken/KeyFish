using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// Several Cyrillic layouts installed at once (Russian, Ukrainian, Belarusian): a word typed in the English layout
// must go to the language it was written in. Runs sentences word by word, like KeyFish does, with context.
// Usage: ChooseCheck.exe <data dir> <layouts dir>
static class ChooseCheck
{
    static string Retype(string word, LayoutTable from, LayoutTable to)
    {
        var keys = from.Keys(word);
        if (keys == null) return null;
        string r = to.Type(keys);
        return r.IndexOf('\uFFFD') >= 0 ? null : r;
    }

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string dataDir = args[0], layoutDir = args[1];
        Detector.Source = l => File.ReadAllLines(Path.Combine(dataDir, l + ".txt"), Encoding.UTF8);
        var en = LayoutTable.FromKlc(Path.Combine(layoutDir, "kbdus.klc"), "en");
        var cyr = new[] { LayoutTable.FromKlc(Path.Combine(layoutDir, "kbdru.klc"), "ru"),
                          LayoutTable.FromKlc(Path.Combine(layoutDir, "kbdur1.klc"), "uk"),
                          LayoutTable.FromKlc(Path.Combine(layoutDir, "kbdblr.klc"), "be") };
        var T = Detector.Load("en");
        var models = cyr.Select(t => Detector.Load(t.Lang)).ToList();

        var sentences = new Dictionary<string, string[]>
        {
            { "ru", new[] { "привет как у тебя дела сегодня", "мы поедем завтра на дачу к родителям", "это очень хорошая новость для всех нас" } },
            { "uk", new[] { "привіт як у тебе справи сьогодні", "ми поїдемо завтра до батьків на дачу", "це дуже гарна новина для всіх нас" } },
            { "be", new[] { "прывітанне як у цябе справы сёння", "мы паедзем заўтра да бацькоў", "гэта вельмі добрая навіна для ўсіх нас" } }
        };

        int total = 0, right = 0;
        foreach (var kv in sentences)
        {
            var source = cyr.First(t => t.Lang == kv.Key);
            foreach (string s in kv.Value)
            {
                string context = null;
                int last = -1;
                var outWords = new List<string>();
                foreach (string w in s.Split(' '))
                {
                    string typed = Retype(w, source, en);
                    if (typed == null) { outWords.Add("<" + w + ">"); continue; }
                    // What KeyFish sees: the keys, and every Cyrillic layout's reading of them
                    var alts = cyr.Select(t => Retype(typed, en, t)).ToList();
                    int i = Detector.Choose(typed, T, alts, models, context, last);
                    if (i >= 0) { context = cyr[i].Lang; last = i; }
                    outWords.Add(i >= 0 ? alts[i] + "/" + cyr[i].Lang : typed + "/-");
                    if (w.Length > 1) { total++; if (i >= 0 && cyr[i].Lang == kv.Key) right++; }
                }
                Console.WriteLine("{0}: {1}", kv.Key, string.Join(" ", outWords));
            }
        }
        Console.WriteLine("right layout: {0}/{1}", right, total);
    }
}
