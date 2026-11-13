using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// Layouts of the same alphabet (English and German, Russian and Ukrainian…): how often Detector.ShouldConvertSameScript
// would fix a word typed right (a false fix) and how often it fixes a word typed in the other layout (caught).
// The 20000 most frequent words of each language, each typed in its own layout and in the other one.
// Usage: SameScriptCheck.exe <data dir> <layouts dir> [max false % per pair]   (exit code 1 past the limit)
static class SameScriptCheck
{
    static readonly string[][] Pairs =
    {
        new[] { "en", "kbdus", "de", "kbdgr" }, new[] { "en", "kbdus", "fr", "kbdfr" }, new[] { "en", "kbdus", "es", "kbdsp" },
        new[] { "en", "kbdus", "pl", "kbdpl1" }, new[] { "en", "kbdus", "cs", "kbdcz" }, new[] { "en", "kbdus", "it", "kbdit" },
        new[] { "ru", "kbdru", "uk", "kbdur1" }, new[] { "ru", "kbdru", "be", "kbdblr" }, new[] { "ru", "kbdru", "bg", "kbdbulg" },
        new[] { "uk", "kbdur1", "be", "kbdblr" }
    };

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string data = args[0], layouts = args[1];
        double limit = args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 100;
        Detector.Source = l => File.ReadAllLines(Path.Combine(data, l + ".txt"), Encoding.UTF8);
        int bad = 0;
        foreach (var p in Pairs)
            for (int dir = 0; dir < 2; dir++)
            {
                string tl = p[dir * 2], al = p[2 - dir * 2];
                var T = Detector.Load(tl); var A = Detector.Load(al);
                var tLayout = LayoutTable.FromKlc(Path.Combine(layouts, p[dir * 2 + 1] + ".klc"), tl);
                var aLayout = LayoutTable.FromKlc(Path.Combine(layouts, p[3 - dir * 2] + ".klc"), al);
                // False: words of T typed right in T's layout; the same keys in A's layout must not be taken
                int tried = 0, wrong = 0; var wrongs = new List<string>();
                foreach (string w in Top(data, tl))
                {
                    var keys = tLayout.Keys(w); if (keys == null) continue;
                    string typed = tLayout.Type(keys), alt = aLayout.Type(keys);
                    if (alt == typed || alt.IndexOf('�') >= 0) continue;
                    tried++;
                    if (Detector.ShouldConvertSameScript(typed, alt, T, A, null)) { wrong++; if (wrongs.Count < 12) wrongs.Add(typed + "→" + alt); }
                }
                // Caught: words of A typed with A's keys while T's layout is on
                int typedWrong = 0, caught = 0, caughtCtx = 0;
                foreach (string w in Top(data, al))
                {
                    var keys = aLayout.Keys(w); if (keys == null) continue;
                    string typed = tLayout.Type(keys), meant = aLayout.Type(keys);
                    if (typed == meant || typed.IndexOf('�') >= 0) continue;
                    typedWrong++;
                    if (Detector.ShouldConvertSameScript(typed, meant, T, A, null)) caught++;
                    if (Detector.ShouldConvertSameScript(typed, meant, T, A, al)) caughtCtx++;
                }
                double falsePct = tried == 0 ? 0 : 100.0 * wrong / tried;
                Console.WriteLine("{0} layout, {1} meant: false {2}/{3} ({4:0.000}%), caught {5}/{6} ({7:0.0}%), after a word in {1} {9:0.0}%  {8}",
                    tl, al, wrong, tried, falsePct, caught, typedWrong, typedWrong == 0 ? 0 : 100.0 * caught / typedWrong, string.Join(" ", wrongs),
                    typedWrong == 0 ? 0 : 100.0 * caughtCtx / typedWrong);
                if (falsePct > limit) bad++;
            }
        // Words the typed language doesn't know but that are typed right: words of a third language of the same alphabet
        // typed on that language's own layout keys as they are (a French name on the English layout, a Russian word
        // on the Ukrainian layout). None of them should be "fixed" into the other language
        var thirds = new[] { new[] { "en", "kbdus", "de", "kbdgr", "fr,es,it,pt,pl,cs,tr" }, new[] { "en", "kbdus", "fr", "kbdfr", "de,es,it,pt,pl,cs" },
                             new[] { "ru", "kbdru", "uk", "kbdur1", "be,bg,mk,sr-cyrl" }, new[] { "uk", "kbdur1", "ru", "kbdru", "be,bg,mk" },
                             new[] { "ru", "kbdru", "be", "kbdblr", "uk,bg" } };
        foreach (var p in thirds)
        {
            var T = Detector.Load(p[0]); var A = Detector.Load(p[2]);
            var tLayout = LayoutTable.FromKlc(Path.Combine(layouts, p[1] + ".klc"), p[0]);
            var aLayout = LayoutTable.FromKlc(Path.Combine(layouts, p[3] + ".klc"), p[2]);
            int tried = 0, wrong = 0, wrongCtx = 0; var wrongs = new List<string>();
            foreach (string third in p[4].Split(','))
                foreach (string w in Top(data, third))
                {
                    var keys = tLayout.Keys(w); if (keys == null) continue;   // only words the typed layout can type as they are
                    string typed = tLayout.Type(keys), alt = aLayout.Type(keys);
                    string core = Detector.Core(typed, p[0]);
                    if (alt == typed || string.IsNullOrEmpty(core) || T.Count(core) > 0) continue;
                    tried++;
                    if (Detector.ShouldConvertSameScript(typed, alt, T, A, null)) { wrong++; if (wrongs.Count < 15) wrongs.Add(typed + "→" + alt); }
                    if (Detector.ShouldConvertSameScript(typed, alt, T, A, p[2])) wrongCtx++;
                }
            Console.WriteLine("third languages on the {0} layout with {1} installed: false {2}/{3} ({4:0.00}%), after a word in {1} {6:0.00}%  {5}", p[0], p[2], wrong, tried, tried == 0 ? 0 : 100.0 * wrong / tried, string.Join(" ", wrongs), tried == 0 ? 0 : 100.0 * wrongCtx / tried);
            if (tried > 0 && 100.0 * wrongCtx / tried > limit * 10) bad++;
        }
        // Not words at all: numbers and words with digits or a symbol in front, typed on the typed layout's keys.
        // None of them may be "fixed" (the digit keys are letters in Czech and French: 333 is not ššš)
        foreach (var p in Pairs)
            for (int dir = 0; dir < 2; dir++)
            {
                string tl = p[dir * 2], al = p[2 - dir * 2];
                var T = Detector.Load(tl); var A = Detector.Load(al);
                var tLayout = LayoutTable.FromKlc(Path.Combine(layouts, p[dir * 2 + 1] + ".klc"), tl);
                var aLayout = LayoutTable.FromKlc(Path.Combine(layouts, p[3 - dir * 2] + ".klc"), al);
                var tokens = new List<string>();
                for (int n = 0; n < 10000; n++) tokens.Add(n.ToString());
                var top = Top(data, tl).Take(400).ToList();
                foreach (string w in top) { tokens.Add(w + "2"); tokens.Add("3" + w); tokens.Add(w + "7"); tokens.Add("+" + w); tokens.Add("=" + w); }
                int tried = 0, wrong = 0; var wrongs = new List<string>();
                foreach (string token in tokens)
                {
                    var keys = tLayout.Keys(token); if (keys == null) continue;
                    string typed = tLayout.Type(keys), alt = aLayout.Type(keys);
                    if (alt == typed || alt.IndexOf('�') >= 0) continue;
                    tried++;
                    if (Detector.ShouldConvertSameScript(typed, alt, T, A, null) || Detector.ShouldConvertSameScript(typed, alt, T, A, al))
                    { wrong++; if (wrongs.Count < 10) wrongs.Add(typed + "→" + alt); }
                }
                if (wrong > 0) { bad++; Console.WriteLine("NOT WORDS on the {0} layout, {1} installed: {2} of {3} changed  {4}", tl, al, wrong, tried, string.Join(" ", wrongs)); }
            }
        Console.WriteLine("not words: checked on every pair");
        return bad == 0 ? 0 : 1;
    }

    static IEnumerable<string> Top(string data, string lang)
    {
        return File.ReadLines(Path.Combine(data, lang + ".txt"), Encoding.UTF8).Select(l => l.Split(' ', '\t')[0]).Where(w => w.Length >= 2 && w.All(char.IsLetter)).Take(20000);
    }
}
