using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

// Every language KeyFish has a model for, on its standard Windows layout (tests\layouts\*.klc), checked offline:
//   1. layout tables: the frequent words of the language can be typed on its layout and read back the same
//      (dead keys, AltGr), also capitalised (Shift) and with CapsLock on;
//   2. every pair of a Latin and a non-Latin layout, both ways: a frequent word typed in the wrong layout is
//      fixed to the word itself (lower case, capitalised, with punctuation after it), a word typed right is left
//      alone, and nothing throws. The decision goes the way KeyFish makes it at the end of a word
//      (Detector.Skipped, Detector.Choose, Detector.NeedsDictionary, Detector.KeepPunctuation).
// Usage: LayoutMatrix.exe <data dir> <layouts dir> [words N] [show N] [pair <typed-in> <language>]
//   words N: how many of the most frequent words of each language are tried (2000)
//   show N:  print up to N examples of every kind of failure
//   pair:    only this pair, e.g. "pair ru fr" — French words typed in the Russian layout
// The exit code is the number of failed checks of part 1 and of limits of part 2 (0: all passed).
// Built like the other checks: csc /out:tests\bin\LayoutMatrix.exe /r:System.Core.dll tests\LayoutMatrix.cs Detector.cs Layouts.cs
static class LayoutMatrix
{
    static readonly Dictionary<string, string> Klc = new Dictionary<string, string>
    {
        { "en", "kbdus" }, { "de", "kbdgr" }, { "pl", "kbdpl1" }, { "sr-latn", "kbdycl" },
        { "fr", "kbdfr" }, { "es", "kbdsp" }, { "it", "kbdit" }, { "pt", "kbdpo" }, { "cs", "kbdcz" }, { "tr", "kbdtuq" },
        { "ru", "kbdru" }, { "uk", "kbdur1" }, { "be", "kbdblr" }, { "sr-cyrl", "kbdycc" },
        { "bg", "kbdbulg" }, { "mk", "kbdmac" }, { "el", "kbdhe" }, { "he", "kbdheb" }, { "ka", "kbdgeo" }
    };
    static readonly string[] Latin = { "en", "de", "pl", "sr-latn", "fr", "es", "it", "pt", "cs", "tr" };
    static readonly string[] Other = { "ru", "uk", "be", "sr-cyrl", "bg", "mk", "el", "he", "ka" };

    static Dictionary<string, LayoutTable> layouts;
    static Dictionary<string, LangModel> models;
    static Dictionary<string, List<string>> words;
    static int show, failures;

    static CultureInfo Culture(string lang) { return lang == "tr" ? new CultureInfo("tr-TR") : CultureInfo.InvariantCulture; }
    static string Upper(string w, string lang) { return w.ToUpper(Culture(lang)); }
    static string Capital(string w, string lang) { return Upper(w.Substring(0, 1), lang) + w.Substring(1); }

    static void Fail(string what) { failures++; Console.WriteLine("FAIL " + what); }

    class Examples
    {
        readonly List<string> list = new List<string>();
        public int Count, Min;
        public void Add(string s) { Count++; if (list.Count < Math.Max(show, Min)) list.Add(s); }
        public void Print(string title)
        {
            if (list.Count > 0) Console.WriteLine("      {0}: {1}", title, string.Join(", ", list));
        }
    }

    // What KeyFish makes of these keys pressed in layout `cur` when the only other layout is `oth`:
    // the other layout's word, or null if the word is left as typed
    static string Decide(List<KeyStroke> keys, string cur, string oth, string context)
    {
        string typed = layouts[cur].Type(keys), alt = layouts[oth].Type(keys);
        var A = models[oth];
        if (Detector.Skipped(typed, alt, models[cur], A)) return null;
        int chosen = Detector.Choose(typed, models[cur], new List<string> { alt }, new List<LangModel> { A }, context, -1);
        if (chosen >= 0 && Detector.NeedsDictionary(typed) && !Detector.IsKnown(alt, A)) chosen = -1;
        if (chosen < 0) return null;
        return Detector.KeepPunctuation(alt, A, oth, typed[typed.Length - 1]);
    }

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string dataDir = args[0], layoutDir = args[1];
        int count = 2000;
        string onlyTyped = null, onlyLang = null;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "words") count = int.Parse(args[++i]);
            else if (args[i] == "show") show = int.Parse(args[++i]);
            else if (args[i] == "pair") { onlyTyped = args[++i]; onlyLang = args[++i]; }
        }
        var lines = Detector.Languages.ToDictionary(l => l, l => File.ReadAllLines(Path.Combine(dataDir, l + ".txt"), Encoding.UTF8));
        Detector.Source = l => lines[l];
        models = Detector.Languages.ToDictionary(l => l, l => Detector.Load(l));
        layouts = Klc.ToDictionary(p => p.Key, p => LayoutTable.FromKlc(Path.Combine(layoutDir, p.Value + ".klc"), p.Key));
        // The most frequent words; a list without frequencies (be) is alphabetical, so every n-th word is taken
        words = new Dictionary<string, List<string>>();
        foreach (string l in Detector.Languages)
        {
            var all = lines[l].Select(s => s.Split(' ')[0]).Where(w => w.Length >= 2).ToList();
            int step = models[l].HasFrequencies ? 1 : Math.Max(1, all.Count / count);
            words[l] = all.Where((w, i) => i % step == 0).Take(count).ToList();
        }

        if (onlyTyped == null) Tables();
        Pairs(onlyTyped, onlyLang);
        if (onlyTyped == null) { Joined(); NotWords(); }
        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "LayoutMatrix: all checks passed" : "LayoutMatrix: " + failures + " checks FAILED");
        Environment.Exit(failures);
    }

    // ---- Part 1: the layout tables ----

    static void Tables()
    {
        Console.WriteLine("LAYOUT TABLES: the {0} most frequent words of each language on its own layout", words["en"].Count);
        Console.WriteLine("{0,-8} {1,-8} | can't be typed | read back wrong | capitalised wrong | no capital | CapsLock differs", "lang", "layout");
        foreach (string l in Latin.Concat(Other))
        {
            var t = layouts[l];
            var missing = new Dictionary<char, int>();
            Examples untypable = new Examples(), back = new Examples(), capital = new Examples(), noCapital = new Examples(), caps = new Examples();
            foreach (string w in words[l])
            {
                var keys = t.Keys(w);
                if (keys == null)
                {
                    untypable.Add(w);
                    foreach (char c in w) if (t.Keys(c.ToString()) == null) { int n; missing.TryGetValue(c, out n); missing[c] = n + 1; }
                    continue;
                }
                string again = t.Type(keys);
                if (again != w) { back.Add(w + "→" + again); continue; }

                // Capitalised: typed with Shift on the first letter
                string cap = Capital(w, l);
                if (cap != w)
                {
                    // No key for a capital is how some layouts are (French É); a capital that reads back wrong is a mistake
                    var capKeys = t.Keys(cap);
                    if (capKeys == null) noCapital.Add(cap);
                    else if (t.Type(capKeys) != cap) capital.Add(cap + "→" + t.Type(capKeys));
                }
                // CapsLock on: the same keys type the word in capitals — on most layouts; the others are listed to be
                // looked at (German ß, Italian è, French é and Hebrew letters have no capitals under CapsLock in Windows either)
                var locked = keys.ConvertAll(k => new KeyStroke(k.Scan, k.Shift, true, k.AltGr));
                string up = t.Type(locked);
                if (up != Upper(w, l)) caps.Add(w + "→" + up);
                // CapsLock and Shift together on the first letter: "пРИВЕТ", never a character lost
                if (up.Length != w.Length || up.IndexOf('�') >= 0) back.Add(w + "→" + up + " (CapsLock)");
            }
            Console.WriteLine("{0,-8} {1,-8} | {2,14} | {3,15} | {4,17} | {5,10} | {6,14}", l, t.Name, untypable.Count, back.Count, capital.Count, noCapital.Count, caps.Count);
            if (missing.Count > 0)
                Console.WriteLine("      not on the layout: {0}", string.Join(" ", missing.OrderByDescending(p => p.Value).Select(p => p.Key + "×" + p.Value)));
            untypable.Print("can't be typed"); back.Print("read back wrong"); capital.Print("capitalised wrong"); noCapital.Print("no capital"); caps.Print("CapsLock differs");
            // A word list may hold a few foreign words; more than 2% means letters of the language are missing from the table
            if (untypable.Count > words[l].Count / 50) Fail(l + ": " + untypable.Count + " frequent words can't be typed on " + t.Name);
            if (back.Count > 0) Fail(l + ": " + back.Count + " words read back wrong");
            if (capital.Count > 0) Fail(l + ": " + capital.Count + " capitalised words read back wrong");
        }
        Console.WriteLine();
    }

    // ---- Part 2: pairs of layouts ----

    class Tally
    {
        public int N, Unpressable, Fixed, Missed, Garbled, FalseFix, Threw;
        public Examples Missing = new Examples(), Wrong = new Examples(), False = new Examples(), Errors = new Examples();
    }

    // Words of `lang` typed in the layout of `typedIn`: `shape` makes the text the user meant from the word
    static Tally Run(string lang, string typedIn, Func<string, string> shape, bool rightToo)
    {
        var tally = new Tally();
        LayoutTable own = layouts[lang], wrong = layouts[typedIn];
        foreach (string w0 in words[lang])
        {
            string w = shape(w0);
            if (w == null) continue;
            var keys = own.Keys(w);
            if (keys == null) continue;                                   // counted in part 1
            if (own.Type(keys) != w) continue;
            tally.N++;
            string typed = wrong.Type(keys);
            // A key that types nothing in the wrong layout: KeyFish sees the word broken in two there
            if (typed.IndexOf('�') >= 0) { tally.Unpressable++; continue; }
            try
            {
                string got = Decide(keys, typedIn, lang, null);
                if (got == w) tally.Fixed++;
                else if (got == null) { tally.Missed++; tally.Missing.Add(typed + "(" + w + ")"); }
                else { tally.Garbled++; tally.Wrong.Add(typed + "→" + got + "(" + w + ")"); }
                if (rightToo)
                {
                    string spoiled = Decide(keys, lang, typedIn, null);
                    if (spoiled != null) { tally.FalseFix++; tally.False.Add(w + "→" + spoiled); }
                }
            }
            catch (Exception ex) { tally.Threw++; tally.Errors.Add(w + ": " + ex.GetType().Name + " " + ex.Message); }
        }
        return tally;
    }

    static void Pairs(string onlyTyped, string onlyLang)
    {
        Console.WriteLine("PAIRS: words of <language> typed in the layout of <typed in>");
        Console.WriteLine("  fixed / missed / wrong result, % of the words; no keys: a key of the word types nothing in that layout;");
        Console.WriteLine("  false: the word typed right and changed; Cap: capitalised; punct: with , . ; ? ! : after it, typed as the");
        Console.WriteLine("  language's own key for it; keep: with the , . ; key of the typed-in layout, where that key is a letter of the language");
        Console.WriteLine("{0,-8} {1,-8} | {2,6} {3,6} {4,6} {5,7} {6,6} | {7,9} {8,9} | {9,11} {10,11} | {11,10}", "typed in", "language",
            "fixed", "missed", "wrong", "no keys", "false", "Cap fixed", "Cap wrong", "punct fixed", "punct wrong", "keep wrong");
        var totals = new Tally();
        var capitals = new Dictionary<string, Tally>();
        foreach (string L in Latin)
            foreach (string C in Other)
            {
                if (L == "sr-latn" && C == "sr-cyrl") continue;   // the same language in two alphabets
                foreach (bool latinWord in new[] { false, true })
                {
                    string lang = latinWord ? L : C, typedIn = latinWord ? C : L;
                    if (onlyTyped != null && (typedIn != onlyTyped || lang != onlyLang)) continue;
                    var plain = Run(lang, typedIn, w => w, true);
                    var cap = Run(lang, typedIn, w => Capital(w, lang) == w ? null : Capital(w, lang), false);
                    // All in capitals, six letters or more (shorter ones may be abbreviations)
                    var upper = Run(lang, typedIn, w => w.Length < 6 || Upper(w, lang) == w ? null : Upper(w, lang), false);
                    Tally sum;
                    if (!capitals.TryGetValue(lang, out sum)) capitals[lang] = sum = new Tally();
                    sum.N += upper.N; sum.Fixed += upper.Fixed; sum.Garbled += upper.Garbled; sum.Threw += upper.Threw;
                    upper.Wrong.Print("CAPITALS wrong"); upper.Errors.Print("EXCEPTION");
                    totals.Garbled += upper.Garbled; totals.Threw += upper.Threw;

                    // Punctuation right after the word, as the user meant it: the key that types it in the word's layout
                    int at = 0;
                    const string marks = ",.;?!:";
                    var punct = Run(lang, typedIn, w => w.Length < 3 ? null : w + marks[at++ % marks.Length], false);

                    // The user saw the typed-in layout and pressed its key for the mark: where that key is a letter of
                    // the word's layout, the mark is kept unless the word with that letter is a word too
                    var keep = new Tally();
                    foreach (char mark in ",.;")
                    {
                        int scan; bool shift, altGr;
                        if (!layouts[typedIn].Find(mark, out scan, out shift, out altGr)) continue;
                        char letter = layouts[lang].CharFor(scan, shift, false, altGr);
                        if (!char.IsLetter(letter)) continue;
                        foreach (string w in words[lang])
                        {
                            if (w.Length < 3 || models[lang].Count(w) == 0 || models[lang].Count(w + letter) > 0) continue;
                            var keys = layouts[lang].Keys(w);
                            if (keys == null) continue;
                            keys.Add(new KeyStroke(scan, shift, false, altGr));
                            string typed = layouts[typedIn].Type(keys);
                            if (typed.IndexOf('�') >= 0) continue;
                            keep.N++;
                            try
                            {
                                string got = Decide(keys, typedIn, lang, null);
                                if (got == w + mark) keep.Fixed++;
                                else if (got == null) keep.Missed++;
                                else { keep.Garbled++; keep.Wrong.Add(typed + "→" + got); }
                            }
                            catch (Exception ex) { keep.Threw++; keep.Errors.Add(w + mark + ": " + ex.GetType().Name + " " + ex.Message); }
                        }
                    }

                    Console.WriteLine("{0,-8} {1,-8} | {2,6:P1} {3,6:P1} {4,6:P1} {5,7:P1} {6,6:P2} | {7,9:P1} {8,9:P1} | {9,11:P1} {10,11:P1} | {11,10}",
                        typedIn, lang, Share(plain.Fixed, plain.N), Share(plain.Missed, plain.N), Share(plain.Garbled, plain.N), Share(plain.Unpressable, plain.N),
                        Share(plain.FalseFix, plain.N), Share(cap.Fixed, cap.N), Share(cap.Garbled, cap.N), Share(punct.Fixed, punct.N), Share(punct.Garbled, punct.N),
                        keep.N == 0 ? "-" : keep.Garbled + "/" + keep.N);
                    plain.Missing.Print("missed"); plain.Wrong.Print("wrong result"); plain.False.Print("false");
                    cap.Missing.Print("Cap missed"); cap.Wrong.Print("Cap wrong"); punct.Missing.Print("punct missed"); punct.Wrong.Print("punct wrong"); keep.Wrong.Print("keep wrong");
                    foreach (var t in new[] { plain, cap, punct, keep })
                    {
                        t.Errors.Print("EXCEPTION");
                        totals.Threw += t.Threw;
                    }
                    totals.Garbled += plain.Garbled + cap.Garbled;
                    totals.N += plain.N; totals.Fixed += plain.Fixed; totals.FalseFix += plain.FalseFix; totals.Unpressable += plain.Unpressable;
                }
            }
        Console.WriteLine();
        Console.WriteLine("IN CAPITALS (words of six letters or more, typed in every layout of another alphabet), fixed:");
        Console.WriteLine("  " + string.Join("  ", capitals.Where(p => p.Value.N > 0).Select(p => string.Format("{0} {1:P1}", p.Key, Share(p.Value.Fixed, p.Value.N)))));
        Console.WriteLine("all pairs: {0} words, fixed {1:P2}, a key types nothing {2:P2}, false {3:P3}, wrong result {4}, exceptions {5}",
            totals.N, Share(totals.Fixed, totals.N), Share(totals.Unpressable, totals.N), Share(totals.FalseFix, totals.N), totals.Garbled, totals.Threw);
        if (totals.Threw > 0) Fail(totals.Threw + " exceptions");
        // A word fixed must come out as the word itself, capitals included
        if (totals.Garbled > 0) Fail(totals.Garbled + " words came out wrong");
    }

    // ---- Part 3: words with an apostrophe or a hyphen ----

    static readonly Dictionary<string, string> JoinedWords = new Dictionary<string, string>
    {
        { "uk", "п'ять м'яч ім'я сім'я здоров'я комп'ютер об'єкт пам'ять зв'язок обов'язково м'ясо п'ятниця з'явився дев'ять м'який під'їзд по-перше будь-який" },
        { "be", "сям'я аб'ява з'ява надвор'е аб'яднаць п'яны вераб'і па-першае" },
        { "fr", "l'homme j'ai c'est qu'il d'accord aujourd'hui n'est s'il l'amour d'abord peut-être celui-ci" },
        { "it", "l'amore dell'anno un'altra all'inizio c'era d'accordo nell'acqua" },
        { "en", "don't I'm it's can't we're you'll that's isn't they've he'd well-known" },
        { "ru", "кое-как что-то кто-нибудь по-русски как-то из-под" }
    };

    // The dictionaries hold such words in parts, so they are a case of their own: typed in the wrong layout they are
    // fixed, apostrophe and hyphen in place; typed right they stay
    static void Joined()
    {
        Console.WriteLine();
        Console.WriteLine("APOSTROPHES AND HYPHENS: words typed in every layout of another alphabet");
        int falseFixes = 0, wrong = 0;
        foreach (var kv in JoinedWords)
        {
            string lang = kv.Key;
            int n = 0, hit = 0;
            Examples missed = new Examples { Min = 12 }, bad = new Examples { Min = 12 };
            foreach (string other in Latin.Concat(Other))
            {
                if (layouts[lang].Script == layouts[other].Script) continue;
                foreach (string w in kv.Value.Split(' '))
                {
                    var keys = layouts[lang].Keys(w);
                    if (keys == null) { Fail(lang + ": " + w + " can't be typed on " + layouts[lang].Name); continue; }
                    string typed = layouts[other].Type(keys);
                    if (typed.IndexOf('\uFFFD') >= 0) continue;
                    n++;
                    string got = Decide(keys, other, lang, null);
                    if (got == w) hit++;
                    else if (got == null) missed.Add(other + " " + typed + "(" + w + ")");
                    else { wrong++; bad.Add(other + " " + typed + "→" + got + "(" + w + ")"); }
                    string spoiled = Decide(keys, lang, other, null);
                    if (spoiled != null) { falseFixes++; bad.Add(w + "→" + spoiled + " (" + other + ")"); }
                }
            }
            Console.WriteLine("  {0,-8} fixed {1,6:P1} of {2}", lang, Share(hit, n), n);
            missed.Print("missed"); bad.Print("WRONG");
            // The limit is the current result: Italian elisions with a short second part are the ones missed
            if (hit < n * 0.7) Fail(lang + ": only " + hit + " of " + n + " words with an apostrophe or a hyphen fixed");
        }
        if (falseFixes > 0) Fail(falseFixes + " words with an apostrophe or a hyphen changed though typed right");
        if (wrong > 0) Fail(wrong + " words with an apostrophe or a hyphen came out wrong");
    }

    // ---- Part 4: what is not a word stays ----

    // Numbers and short words with digits or symbols, typed right in every layout, with every layout of another
    // alphabet as the other one: none may be changed, though the same keys can type letters there (French and Czech
    // letters on the digit row, Bulgarian and Hebrew letters on the keys of / and .)
    static void NotWords()
    {
        Console.WriteLine();
        Console.WriteLine("NOT WORDS: numbers, and the 40 most frequent words of each language joined with digits and symbols, typed right");
        int tried = 0, changed = 0;
        var examples = new Examples { Min = 40 };
        var numbers = new List<string>();
        for (int i = 0; i < 3000; i++) numbers.Add(i.ToString());
        foreach (string n in new[] { "1990", "2024", "2025", "10000", "100000", "3959", "12345", "0000", "1,5", "2.5", "10:30", "1/2", "01.02.2024", "+7", "50%", "$100", "#1", "2+2=4", "3-4", "1-й" })
            numbers.Add(n);
        foreach (string own in Latin.Concat(Other))
            foreach (string other in Latin.Concat(Other))
            {
                if (layouts[own].Script == layouts[other].Script) continue;
                var tokens = new List<string>(numbers);
                var top = words[own].Take(40).ToList();
                foreach (string w in words[own].Where(x => x.Length <= 3).Take(150))
                    foreach (string d in new[] { "1", "2", "3", "5", "7", "9", "10", "20", "90", "100" }) { tokens.Add(d + w); tokens.Add(w + d); tokens.Add(d + "-" + w); }
                foreach (string a in top)
                    foreach (string b in top)
                        foreach (char sep in "/\\+=_&*")
                            tokens.Add(a + sep + b);
                foreach (string token in tokens)
                {
                    var keys = layouts[own].Keys(token);
                    if (keys == null || layouts[own].Type(keys) != token || layouts[other].Type(keys).IndexOf('�') >= 0) continue;
                    tried++;
                    string got = Decide(keys, own, other, null);
                    if (got != null) { changed++; examples.Add(own + ">" + other + " " + token + "→" + got); }
                }
            }
        Console.WriteLine("  {0} tried, {1} changed", tried, changed);
        examples.Print("changed");
        if (changed > 0) Fail(changed + " numbers or tokens with symbols were changed");
    }

    static double Share(int a, int n) { return n == 0 ? 0 : a / (double)n; }
}
