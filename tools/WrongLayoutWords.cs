using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// Finds the words of an extended dictionary that are, in fact, a common word of another language typed in the wrong
// layout: subtitles have "yj" (but for "но"), "ещ" (to), "ше" (it). Kept, they make KeyFish take such a slip for a word
// of the language and leave it as it is.
// A word goes when the same keys in the layout of a language with another alphabet type a word of that language's
// built-in list at least a thousand times as frequent, as KeyFish itself decides right after a word of the language
// (a count below 20, in a small corpus, is taken as 20). Below that, real words start to go too ("irk": "син" in
// Bulgarian, 510 times as frequent). Only the English and German layouts are checked against the other alphabets: that is where the
// slips in the subtitles come from, and with every Latin layout real words go ("руду": Czech "hele", "лещ": Polish "kto")
// Usage: WrongLayoutWords.exe <data dir> <layouts dir> <language> <words file> <output file>
// The words file has "word count" lines; the output gets "word count -> language:word count" for every word to drop
static class WrongLayoutWords
{
    const int Ratio = 1000;

    // The standard Windows layout of each language (tests/layouts)
    static readonly Dictionary<string, string> Klc = new Dictionary<string, string>
    {
        { "en", "kbdus" }, { "de", "kbdgr" }, { "pl", "kbdpl1" }, { "sr-latn", "kbdycl" },
        { "fr", "kbdfr" }, { "es", "kbdsp" }, { "it", "kbdit" }, { "pt", "kbdpo" }, { "cs", "kbdcz" }, { "tr", "kbdtuq" },
        { "ru", "kbdru" }, { "uk", "kbdur1" }, { "be", "kbdblr" }, { "sr-cyrl", "kbdycc" },
        { "bg", "kbdbulg" }, { "mk", "kbdmac" }, { "el", "kbdhe" }, { "he", "kbdheb" }, { "ka", "kbdgeo" }
    };
    static readonly HashSet<string> Latin = new HashSet<string> { "en", "de", "pl", "sr-latn", "fr", "es", "it", "pt", "cs", "tr" };
    static readonly HashSet<string> Checked = new HashSet<string> { "en", "de" };
    // Layouts whose keys follow the Latin letters by sound: "samo" in Latin letters is Serbian, "ena" Greek, written so
    // on purpose, not a slip in the Serbian or Greek layout
    static readonly HashSet<string> Phonetic = new HashSet<string> { "sr-cyrl", "mk", "el" };

    static Dictionary<string, long> Counts(string dataDir, string lang)
    {
        return File.ReadLines(Path.Combine(dataDir, lang + ".txt"), Encoding.UTF8).Select(s => s.Split(' '))
            .Where(p => p.Length == 2).GroupBy(p => p[0]).ToDictionary(g => g.Key, g => long.Parse(g.First()[1]));
    }

    static int Main(string[] args)
    {
        string dataDir = args[0], layoutDir = args[1], lang = args[2];
        var others = !Klc.ContainsKey(lang) || (Latin.Contains(lang) && !Checked.Contains(lang)) ? new string[0] :
            Klc.Keys.Where(l => Latin.Contains(l) != Latin.Contains(lang) && (Latin.Contains(l) ? Checked.Contains(l) : !Phonetic.Contains(l))).ToArray();
        if (others.Length == 0) { File.WriteAllText(args[4], ""); return 0; }
        var from = LayoutTable.FromKlc(Path.Combine(layoutDir, Klc[lang] + ".klc"), lang);
        var targets = others.Where(l => File.Exists(Path.Combine(dataDir, l + ".txt"))).Select(l =>
        {
            return new { Lang = l, Layout = LayoutTable.FromKlc(Path.Combine(layoutDir, Klc[l] + ".klc"), l), Counts = Counts(dataDir, l) };
        }).ToList();

        var output = new List<string>();
        foreach (string line in File.ReadLines(args[3], Encoding.UTF8))
        {
            var p = line.Split(' ');
            long count;
            if (p.Length != 2 || !long.TryParse(p[1], out count)) continue;
            var keys = from.Keys(p[0]);
            if (keys == null) continue;
            foreach (var o in targets)
            {
                string typed = o.Layout.Type(keys);
                long other;
                if (typed.IndexOf('\uFFFD') < 0 && o.Counts.TryGetValue(typed, out other) && other >= Math.Max(count, 20) * Ratio)
                {
                    output.Add(line + " -> " + o.Lang + ":" + typed + " " + other);
                    break;
                }
            }
        }
        File.WriteAllLines(args[4], output, new UTF8Encoding(false));
        return 0;
    }
}
