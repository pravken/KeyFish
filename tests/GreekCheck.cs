using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// English and Greek, the pair of a Cypriot or Greek user: how many words typed in the wrong layout are fixed, and how
// many words typed right are changed, the way KeyFish decides (Detector.Choose with the previous word's language).
// Greek words as people write them: with accents (typed with the dead key on ;), capitalised, all capitals.
// Usage: GreekCheck.exe <data dir> <layouts dir> [max false %] [min caught % for Greek typed on English]
static class GreekCheck
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string data = args[0], layouts = args[1];
        double maxFalse = args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 100;
        double minCaught = args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0;
        Detector.Source = l => File.ReadAllLines(Path.Combine(data, l + ".txt"), Encoding.UTF8);
        var en = LayoutTable.FromKlc(Path.Combine(layouts, "kbdus.klc"), "en");
        var el = LayoutTable.FromKlc(Path.Combine(layouts, "kbdhe.klc"), "el");
        var E = Detector.Load("en"); var G = Detector.Load("el");
        int bad = 0;
        foreach (string ctx in new[] { null, "en", "el" })
        {
            // Greek typed with the English layout on (keys of the Greek layout)
            var r1 = Run(Top(data, "el", 20000), el, en, G, E, ctx);
            // English typed with the Greek layout on
            var r2 = Run(Top(data, "en", 20000), en, el, E, G, ctx);
            Console.WriteLine("context {0}: Greek on the English layout fixed {1}; English on the Greek layout fixed {2}; false: English typed right {3}, Greek typed right {4}",
                ctx ?? "none", r1.Item1, r2.Item1, r2.Item2, r1.Item2);
            if (ctx == null && (r1.Item3 > maxFalse || r2.Item3 > maxFalse)) bad++;
            if (ctx == null && r1.Item4 < minCaught) bad++;
        }
        // Greek as it is often written in chats: without accents, and in capitals (where accents are left out anyway)
        var bare = Top(data, "el", 20000).Select(Bare).Distinct().ToList();
        foreach (string ctx in new[] { null, "en", "el" })
        {
            var r = Run(bare, el, en, G, E, ctx);
            Console.WriteLine("context {0}: Greek without accents on the English layout fixed {1}; false (typed right) {2}", ctx ?? "none", r.Item1, r.Item2);
            // right after English text the commonest Greek words must still be caught (a live run lost them all once)
            if (r.Item4 < 50 || (ctx == null && r.Item3 > maxFalse)) bad++;
        }
        return bad;
    }

    // words of the language of `own`, typed right (own layout on) and wrong (the other layout on)
    static Tuple<string, string, double, double> Run(List<string> words, LayoutTable own, LayoutTable other, LangModel O, LangModel X, string ctx)
    {
        int wrongTried = 0, fixedRight = 0, rightTried = 0, falseFix = 0;
        var misses = new List<string>(); var falses = new List<string>();
        foreach (string w0 in words)
            foreach (string w in new[] { w0, Cap(w0), w0.Length > 1 ? w0.ToUpperInvariant() : null })
            {
                if (w == null) continue;
                var keys = own.Keys(w);
                if (keys == null) continue;
                string meant = own.Type(keys), typed = other.Type(keys);
                if (typed.IndexOf('�') >= 0 || meant != w) continue;
                // Typed wrong: the other layout was on
                if (!Skipped(typed))
                {
                    wrongTried++;
                    int c = Detector.Choose(typed, X, new[] { meant }, new[] { O }, ctx, -1);
                    if (c == 0) fixedRight++; else if (misses.Count < 12 && w == w0) misses.Add(typed + "→" + meant);
                }
                // Typed right: the own layout was on; the other layout must not be taken
                if (!Skipped(meant))
                {
                    rightTried++;
                    if (Detector.Choose(meant, O, new[] { typed }, new[] { X }, ctx == null ? null : (ctx == O.Lang ? O.Lang : X.Lang), -1) == 0) { falseFix++; if (falses.Count < 12) falses.Add(meant + "→" + typed); }
                }
            }
        double caught = wrongTried == 0 ? 0 : 100.0 * fixedRight / wrongTried, falsePct = rightTried == 0 ? 0 : 100.0 * falseFix / rightTried;
        if (misses.Count > 0) Console.WriteLine("   missed ({0}): {1}", O.Lang, string.Join(" ", misses));
        if (falses.Count > 0) Console.WriteLine("   false ({0} typed right): {1}", O.Lang, string.Join(" ", falses));
        return Tuple.Create(string.Format("{0:0.0}% of {1}", caught, wrongTried), string.Format("{0:0.000}% of {1}", falsePct, rightTried), falsePct, caught);
    }

    static bool Skipped(string typed)
    {
        foreach (char c in typed) if (char.IsDigit(c) || c == '@' || c == '_') return true;
        return Detector.LooksLikeCode(typed);
    }

    // Without tonos and dialytika
    static string Bare(string w)
    {
        var sb = new StringBuilder();
        foreach (char c in w.Normalize(NormalizationForm.FormD)) if (c != (char)0x301 && c != (char)0x308) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    static string Cap(string w) { return w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1); }

    static List<string> Top(string data, string lang, int n)
    {
        return File.ReadLines(Path.Combine(data, lang + ".txt"), Encoding.UTF8).Select(l => l.Split(' ', '\t')[0])
            .Where(w => w.Length >= 2 && w.All(char.IsLetter)).Take(n).ToList();
    }
}
