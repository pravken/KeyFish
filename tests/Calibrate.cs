using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// Measures the detector for every pair of a Latin and a Cyrillic layout:
// how many words typed in the wrong layout it catches, and how many correct words it would wrongly "fix".
// Usage: Calibrate.exe <data dir> <layouts dir> heldout|full
// heldout: models learn from every other word and are tested on the rest (words they have never seen);
// full: the real models, tested on the 5000 most frequent words (the dictionary decides most of them).
static class Calibrate
{
    static readonly Dictionary<string, string> Klc = new Dictionary<string, string>
    {
        { "en", "kbdus" }, { "de", "kbdgr" }, { "pl", "kbdpl1" }, { "sr-latn", "kbdycl" },
        { "fr", "kbdfr" }, { "es", "kbdsp" }, { "it", "kbdit" }, { "pt", "kbdpo" }, { "cs", "kbdcz" }, { "tr", "kbdtuq" },
        { "ru", "kbdru" }, { "uk", "kbdur1" }, { "be", "kbdblr" }, { "sr-cyrl", "kbdycc" },
        { "bg", "kbdbulg" }, { "mk", "kbdmac" }, { "el", "kbdhe" }, { "he", "kbdheb" }, { "ka", "kbdgeo" }
    };
    static readonly string[] Latin = { "en", "de", "pl", "sr-latn", "fr", "es", "it", "pt", "cs", "tr" };
    static readonly string[] Cyrillic = { "ru", "uk", "be", "sr-cyrl", "bg", "mk", "el", "he", "ka" };   // every other alphabet, in fact

    // The same keys pressed in another layout; null if a letter can't be typed in the source layout
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
        bool heldOut = args[2] == "heldout";
        var lines = Detector.Languages.ToDictionary(l => l, l => File.ReadAllLines(Path.Combine(dataDir, l + ".txt"), Encoding.UTF8));
        Detector.Source = l => heldOut ? lines[l].Where((s, i) => i % 2 == 0) : lines[l];
        var models = Detector.Languages.ToDictionary(l => l, l => Detector.Load(l));
        var layouts = Klc.ToDictionary(p => p.Key, p => LayoutTable.FromKlc(Path.Combine(layoutDir, p.Value + ".klc"), p.Key));

        Func<string, List<string>> testWords = l => (heldOut ? lines[l].Where((s, i) => i % 2 == 1) : lines[l].Take(5000))
            .Select(s => s.Split(' ')[0]).Where(w => w.Length >= 3).Take(4000).ToList();
        var tests = Detector.Languages.ToDictionary(l => l, testWords);

        // Debug mode: show the words of one pair that were missed (args: ... debug <typed-in layout> <intended language>)
        if (args.Length >= 6 && args[3] == "debug")
        {
            string typedIn = args[4], lang = args[5];
            int shown = 0;
            foreach (string w in tests[lang])
            {
                string typed = Retype(w, layouts[lang], layouts[typedIn]);
                if (typed == null || Detector.ShouldConvert(typed, w, models[typedIn], models[lang], lang)) continue;
                string t = Detector.Core(typed);
                Console.WriteLine("missed {0} → typed {1}  count in {2}: {3}, count in {4}: {5}, score {6:F2} vs {7:F2}", w, typed, typedIn,
                    t == null ? -1 : models[typedIn].Count(t), lang, models[lang].Count(w), t == null ? 0 : models[typedIn].Score(t), models[lang].Score(w));
                if (++shown == 25) break;
            }
            return;
        }

        Console.WriteLine(heldOut ? "HELD-OUT (words the models have not seen)" : "FULL MODELS (5000 most frequent words)");
        Console.WriteLine("{0,-8} {1,-8} | caught: {1} words typed in {0} layout, {0} words typed in {1} layout | wrongly fixed: correct {0}, correct {1}", "latin", "cyrillic");
        bool withContext = args.Length > 3 && args[3] == "context";
        bool all = args.Contains("all");
        if (withContext) Console.WriteLine("(with context: the previous word was in the language the user is writing in)");
        foreach (string L in Latin)
            foreach (string C in Cyrillic)
            {
                if (L == "sr-latn" && C == "sr-cyrl") continue;   // the same language in two alphabets
                if (L != "en" && C != "ru" && !all) continue;      // by default: every language against English or Russian
                Func<string, string, string, string, double> rate = (lang, typedIn, other, mode) =>
                {
                    string context = withContext ? lang : null;
                    int n = 0, hit = 0;
                    foreach (string w in tests[lang])
                    {
                        string typed, alt;
                        if (mode == "wrong") { typed = Retype(w, layouts[lang], layouts[typedIn]); alt = w; }
                        else { typed = w; alt = Retype(w, layouts[lang], layouts[other]); }
                        if (typed == null || alt == null) continue;
                        n++;
                        var T = models[mode == "wrong" ? typedIn : lang];
                        var A = models[mode == "wrong" ? lang : other];
                        if (Detector.ShouldConvert(typed, alt, T, A, context)) hit++;
                    }
                    return n == 0 ? double.NaN : hit / (double)n;
                };
                Console.WriteLine("{0,-8} {1,-8} | caught {2,6:P1} {3,6:P1} | wrongly fixed {4,6:P2} {5,6:P2}", L, C,
                    rate(C, L, null, "wrong"), rate(L, C, null, "wrong"), rate(L, null, C, "correct"), rate(C, null, L, "correct"));
            }
    }
}
