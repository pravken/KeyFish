using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// How often KeyFish gets it wrong, measured on the texts in tests\corpus. Every sentence is "typed" twice:
//   right  — each word in its own layout: KeyFish must leave it alone, any change is a false correction;
//   wrong  — each word in the other layout: KeyFish must give the original word back.
// The decision goes the way KeyFish makes it (digits and code skipped, Detector.Choose with the previous word's
// language as context), without the parts that need a live keyboard (learned words, held-back short words).
// Usage: LayoutBench.exe <data dir> <layouts dir> <corpus dir> [max false corrections] [min % fixed]   (exit code 1 past a limit)
static class LayoutBench
{
    class Side { public string Lang; public LayoutTable Layout; public LangModel Model; }

    static string Retype(string word, LayoutTable from, LayoutTable to)
    {
        var keys = from.Keys(word);
        if (keys == null) return null;
        string r = to.Type(keys);
        return r.IndexOf('�') >= 0 ? null : r;
    }

    static bool Skipped(string typed)
    {
        foreach (char c in typed) if (char.IsDigit(c) || c == '@' || c == '_') return true;
        return Detector.LooksLikeCode(typed);
    }

    // What KeyFish makes of a word typed in layout `current`: the other side's word, or null to leave it
    static string Decide(string typed, Side current, Side other, ref string context)
    {
        if (Skipped(typed)) return null;
        string alt = Retype(typed, current.Layout, other.Layout);
        var alts = new List<string> { alt };
        int chosen = alt == null ? -1 : Detector.Choose(typed, current.Model, alts, new List<LangModel> { other.Model }, context, -1);
        if (chosen >= 0 && Detector.NeedsDictionary(typed))
        {
            string core = Detector.Core(alt);
            if (string.IsNullOrEmpty(core) || other.Model.Count(core) == 0) chosen = -1;
        }
        string result = chosen >= 0 ? alt : typed;
        var target = chosen >= 0 ? other : current;
        string resultCore = Detector.Core(result);
        if (chosen >= 0 || (!string.IsNullOrEmpty(resultCore) && target.Model.Count(resultCore) > 0)) context = target.Lang;
        return chosen >= 0 ? alt : null;
    }

    static bool IsCyrillic(string w) { return w.Any(c => c >= 'Ѐ' && c <= 'ӿ'); }

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string dataDir = args[0], layoutDir = args[1], corpusDir = args[2];
        Detector.Source = l => File.ReadAllLines(Path.Combine(dataDir, l + ".txt"), Encoding.UTF8);
        var en = new Side { Lang = "en", Layout = LayoutTable.FromKlc(Path.Combine(layoutDir, "kbdus.klc"), "en") };
        var ru = new Side { Lang = "ru", Layout = LayoutTable.FromKlc(Path.Combine(layoutDir, "kbdru.klc"), "ru") };
        en.Model = Detector.Load("en");
        ru.Model = Detector.Load("ru");
        Detector.Load("sr-latn");

        int words = 0, falseFixes = 0, wrongTotal = 0, wrongFixed = 0, wrongMissed = 0, wrongGarbled = 0;
        var falseList = new List<string>();
        var missedList = new List<string>();
        foreach (string file in new[] { "ru.txt", "en.txt" })
            foreach (string line in File.ReadAllLines(Path.Combine(corpusDir, file), Encoding.UTF8))
            {
                if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
                var tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                // Typed right: nothing may change
                string context = null;
                foreach (string w in tokens)
                {
                    var own = IsCyrillic(w) ? ru : en;
                    var other = own == ru ? en : ru;
                    string got = Decide(w, own, other, ref context);
                    words++;
                    if (got != null) { falseFixes++; falseList.Add(w + " → " + got + "   (" + line.Trim() + ")"); }
                }

                // Typed in the wrong layout: the word must come back. As in KeyFish, a word confidently fixed also
                // fixes up to three short words left before it ("f vj;yj" → "а можно")
                context = null;
                var results = new List<string[]>();   // typed, expected, got
                foreach (string w in tokens)
                {
                    var own = IsCyrillic(w) ? ru : en;
                    var other = own == ru ? en : ru;
                    string typed = Retype(w, own.Layout, other.Layout);
                    if (typed == null || Skipped(w)) { results.Add(null); continue; }   // a number or code between words: nothing is fixed back across it
                    string got = Decide(typed, other, own, ref context);
                    string core = got == null ? null : Detector.Core(got);
                    if (got != null && core != null && core.Length >= 3 && own.Model.Count(core) > 0)
                        for (int i = results.Count - 1, n = 0; i >= 0 && n < 3; i--, n++)
                        {
                            var p = results[i];
                            if (p == null || p[2] != null) break;
                            string back = Retype(p[0], other.Layout, own.Layout);
                            if (back == null || !Detector.ShouldConvert(p[0], back, other.Model, own.Model, own.Lang)) break;
                            p[2] = back;
                        }
                    results.Add(new[] { typed, w, got });
                }
                foreach (var r in results.Where(x => x != null))
                {
                    wrongTotal++;
                    if (r[2] == r[1]) wrongFixed++;
                    else if (r[2] == null) { wrongMissed++; missedList.Add(r[0] + " (" + r[1] + ")"); }
                    else wrongGarbled++;
                }
            }

        Console.WriteLine("FALSE CORRECTIONS (typed right, KeyFish would change):");
        foreach (string s in falseList) Console.WriteLine("  " + s);
        Console.WriteLine();
        Console.WriteLine("MISSED (typed in the wrong layout, KeyFish would leave):");
        Console.WriteLine("  " + string.Join(", ", missedList));
        Console.WriteLine();
        Console.WriteLine("typed right:  {0} words, {1} false corrections ({2:0.00}%)", words, falseFixes, 100.0 * falseFixes / words);
        Console.WriteLine("typed wrong:  {0} words, fixed {1} ({2:0.0}%), missed {3}, wrong result {4}",
                          wrongTotal, wrongFixed, 100.0 * wrongFixed / wrongTotal, wrongMissed, wrongGarbled);
        // Optional limits for CI: at most this many false corrections, at least this share of wrong-layout words fixed
        int limit = args.Length > 3 ? int.Parse(args[3]) : int.MaxValue;
        double minFixed = args.Length > 4 ? double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 0;
        bool failed = falseFixes > limit || 100.0 * wrongFixed / wrongTotal < minFixed;
        if (failed) Console.WriteLine("FAILED: the limits are at most {0} false corrections and at least {1}% fixed", limit, minFixed);
        Environment.Exit(failed ? 1 : 0);
    }
}
