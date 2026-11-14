using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

// Random texts and keys against the logic that needs no keyboard or screen: nothing may throw, nothing may take
// longer than a second, and what must hold for any input holds (a word typed and read back, the case going round,
// a replacement written and read, a title rule against the regular expression it stands for…).
// The random numbers start from a fixed seed, so a run repeats itself. Nothing is typed, and the clipboard, the
// registry and files outside %TEMP% are left alone.
// Compiled together with KeyFish's own sources, like UnitTests:
//   csc /main:Fuzz tests\Fuzz.cs <all .cs of KeyFish>   then   Fuzz.exe <data dir> <layouts dir> [seed] [times]
//   times: how many times the usual amount of inputs (1; 0.2 for a quick look)
// The exit code is the number of different failures (0: none). A call that doesn't come back in 20 seconds ends the
// run at once, with the input printed
static class Fuzz
{
    const int SlowMs = 1000, HangMs = 20000;

    static Random rnd;
    static int seed = 20261113;
    static double times = 1;
    static readonly Dictionary<string, int> kinds = new Dictionary<string, int>();   // a failure → how many inputs showed it
    static readonly Stopwatch clock = Stopwatch.StartNew();
    static long calls, startedAt;      // startedAt: when the call under way began (ms, +1), 0 between calls
    static volatile string nowName, nowInput;

    static readonly Dictionary<string, string> Klc = new Dictionary<string, string>
    {
        { "en", "kbdus" }, { "de", "kbdgr" }, { "pl", "kbdpl1" }, { "sr-latn", "kbdycl" },
        { "fr", "kbdfr" }, { "es", "kbdsp" }, { "it", "kbdit" }, { "pt", "kbdpo" }, { "cs", "kbdcz" }, { "tr", "kbdtuq" },
        { "ru", "kbdru" }, { "uk", "kbdur1" }, { "be", "kbdblr" }, { "sr-cyrl", "kbdycc" },
        { "bg", "kbdbulg" }, { "mk", "kbdmac" }, { "el", "kbdhe" }, { "he", "kbdheb" }, { "ka", "kbdgeo" }
    };
    static readonly List<string> langs = new List<string>();
    static readonly Dictionary<string, LayoutTable> layouts = new Dictionary<string, LayoutTable>();
    static readonly Dictionary<string, LangModel> models = new Dictionary<string, LangModel>();
    static readonly Dictionary<string, List<string>> lexicon = new Dictionary<string, List<string>>();
    static readonly List<int> typingScans = new List<int>();
    static string tempDir;

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string data = args.Length > 0 ? args[0] : "data", layoutDir = args.Length > 1 ? args[1] : Path.Combine("tests", "layouts");
        if (args.Length > 2) seed = int.Parse(args[2]);
        if (args.Length > 3) times = double.Parse(args[3], CultureInfo.InvariantCulture);
        Detector.Source = l => File.ReadAllLines(Path.Combine(data, l + ".txt"), Encoding.UTF8);
        new Thread(Watch) { IsBackground = true }.Start();

        for (int s = 1; s < 256; s++) if (LayoutTable.IsTypingKey(s)) typingScans.Add(s);
        foreach (string lang in Detector.Languages)
        {
            langs.Add(lang);
            layouts[lang] = LayoutTable.FromKlc(Path.Combine(layoutDir, Klc[lang] + ".klc"), lang);
            models[lang] = Detector.Load(lang);
            var words = new List<string>();
            string[] lines = File.ReadAllLines(Path.Combine(data, lang + ".txt"), Encoding.UTF8);
            for (int i = 0; i < lines.Length; i += i < 3000 ? 1 : 37)
            {
                int sp = lines[i].IndexOf(' ');
                if (sp > 0) words.Add(lines[i].Substring(0, sp));
            }
            lexicon[lang] = words;
        }
        Detector.ReturnMemory();
        Console.WriteLine("seed {0}, {1} languages loaded in {2} ms", seed, langs.Count, clock.ElapsedMilliseconds);

        tempDir = Path.Combine(Path.GetTempPath(), "keyfish-fuzz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            Section("Detector", 1, FuzzDetector);
            Section("Detector, long words", 2, FuzzDetectorLong);
            Section("Layout tables", 3, FuzzLayouts);
            Section("Retype, FixCase", 4, FuzzRetype);
            Section("Case, sentences, spaces, plain text", 5, FuzzCase);
            Section("Transliteration", 6, FuzzTranslit);
            Section("Replacements, placeholders, word lists", 7, FuzzReplacements);
            Section("App rules", 8, FuzzAppRules);
            Section("Calculator, colors, sounds", 9, FuzzSmall);
            Section("Settings archive", 10, FuzzArchive);
            Section("Import", 11, FuzzImport);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }

        Console.WriteLine("{0} calls in {1:0.0} s, {2} different failures", calls, clock.ElapsedMilliseconds / 1000.0, kinds.Count);
        foreach (var kv in kinds) Console.WriteLine("  {0} x {1}", kv.Value, kv.Key);
        return kinds.Count;
    }

    // ---------- The harness ----------

    static void Section(string name, int number, Action body)
    {
        rnd = new Random(seed + number);   // each part has its own numbers: changing one part leaves the others' inputs as they were
        long c0 = calls, t0 = clock.ElapsedMilliseconds;
        int k0 = kinds.Count;
        try { body(); }
        catch (Exception ex) { Fail(name + ": the fuzzer itself threw " + ex.GetType().Name, "", ex.ToString()); }
        Console.WriteLine("{0}: {1} calls, {2} ms{3}", name, calls - c0, clock.ElapsedMilliseconds - t0, kinds.Count > k0 ? ", " + (kinds.Count - k0) + " new failures" : "");
    }

    static int N(int usual) { return Math.Max(1, (int)(usual * times)); }

    static void Call(string name, string input, Action action)
    {
        calls++;
        nowName = name; nowInput = input;
        long t0 = clock.ElapsedMilliseconds;
        Interlocked.Exchange(ref startedAt, t0 + 1);
        try { action(); }
        catch (Exception ex)
        {
            var e = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            Fail(name + ": " + e.GetType().Name + " in " + Where(e), input, e.Message);
        }
        Interlocked.Exchange(ref startedAt, 0);
        long ms = clock.ElapsedMilliseconds - t0;
        if (ms > SlowMs) Fail(name + ": over a second", input, ms + " ms");
    }

    static string Where(Exception e)
    {
        var frame = new StackTrace(e).GetFrame(0);
        var m = frame == null ? null : frame.GetMethod();
        return m == null ? "?" : (m.DeclaringType == null ? "" : m.DeclaringType.Name + ".") + m.Name;
    }

    static void Check(string name, bool ok, string input, string detail = "")
    {
        if (!ok) Fail(name, input, detail);
    }

    static void Fail(string kind, string input, string detail)
    {
        int n;
        kinds.TryGetValue(kind, out n);
        kinds[kind] = n + 1;
        if (n >= 3) return;   // three examples of a failure are enough
        Console.WriteLine("FAIL {0}\n     input ({1} chars): {2}\n     {3}", kind, (input ?? "").Length, Show(input), Show(detail));
    }

    // Printable whatever is in it: everything outside ASCII as \uXXXX, a long text cut
    static string Show(string s)
    {
        if (s == null) return "(null)";
        var sb = new StringBuilder();
        foreach (char c in s.Length > 300 ? s.Substring(0, 300) : s)
            if (c >= ' ' && c < 127 && c != '\\') sb.Append(c); else sb.Append("\\u").Append(((int)c).ToString("X4"));
        return sb.Append(s.Length > 300 ? "…" : "").ToString();
    }

    static void Watch()
    {
        while (true)
        {
            Thread.Sleep(500);
            long began = Interlocked.Read(ref startedAt);
            if (began == 0 || clock.ElapsedMilliseconds - began < HangMs) continue;
            Console.WriteLine("HANG {0}: no answer in {1} s\n     input ({2} chars): {3}", nowName, HangMs / 1000, (nowInput ?? "").Length, Show(nowInput));
            Console.Out.Flush();
            Environment.Exit(kinds.Count + 1);
        }
    }

    // ---------- Inputs ----------

    const string Specials = "σςΣİıIißẞǅǆ'’`´^¨~΄΅-–—…«»„“”\"{}%_@/\\=|$&*+.,!?:;()[]#<>" +
                            "\u200B\u200D\u200E\u200F\u202E\u2060\uFEFF\u00AD\u3000\uFFFD\uFFFE\uFFFF\u2028\u2029\u0085\u0000\u0336\u0301\u0308\u0344";
    static readonly string[] Tokens =
    {
        "{cursor}", "{date}", "{time}", "{CURSOR}", "{Date}", "://", "www.", "title:", "Title: ", " = hotkeys", "=", "\\n", "\\t", "\\\\", "\\",
        "\r\n", "\n", ".exe", "*", "?", "(a+)+$", "- trigger: ", "replace: ", "::", ":*:", "#", "ου", "'", "-", "file:", "fix = ", ";", ",", "\t", "\"", "%"
    };
    const int Classes = 21;

    static void Append(StringBuilder sb, int cls)
    {
        switch (cls)
        {
            case 0: sb.Append((char)('a' + rnd.Next(26))); break;
            case 1: sb.Append((char)('A' + rnd.Next(26))); break;
            case 2: sb.Append((char)('0' + rnd.Next(10))); break;
            case 3: sb.Append((char)(0x21 + rnd.Next(0x5E))); break;
            case 4: sb.Append((char)(0x400 + rnd.Next(0x100))); break;      // Cyrillic
            case 5: sb.Append((char)(0x370 + rnd.Next(0x90))); break;       // Greek
            case 6: sb.Append((char)(0x590 + rnd.Next(0x70))); break;       // Hebrew: right to left
            case 7: sb.Append((char)(0x600 + rnd.Next(0x100))); break;      // Arabic
            case 8: sb.Append((char)(0x10A0 + rnd.Next(0x60))); break;      // Georgian
            case 9: sb.Append((char)(0xA0 + rnd.Next(0x1B0))); break;       // Latin with marks
            case 10: sb.Append((char)(0x300 + rnd.Next(0x70))); break;      // combining marks
            case 11: sb.Append((char)(rnd.Next(2) == 0 ? rnd.Next(0x20) : 0x7F + rnd.Next(0x21))); break;   // control characters
            case 12: sb.Append(Specials[rnd.Next(Specials.Length)]); break;
            case 13: sb.Append((char)(0xD800 + rnd.Next(0x400))); break;    // half a pair
            case 14: sb.Append((char)(0xDC00 + rnd.Next(0x400))); break;
            case 15:   // beyond the first 65536: emoji, mathematical letters, old scripts, rare Chinese, the very last one
                int[] from = { 0x1F300, 0x1D400, 0x10000, 0x20000, 0xE0000, 0x10FF00, 0x1F1E6 };
                int[] count = { 0x400, 0x400, 0x100, 0x1000, 0x80, 0x100, 26 };
                int r = rnd.Next(from.Length);
                sb.Append(char.ConvertFromUtf32(from[r] + rnd.Next(count[r])));
                break;
            case 16: sb.Append((char)(0x4E00 + rnd.Next(0x5200))); break;   // Chinese
            case 17: sb.Append((char)(rnd.Next(2) == 0 ? 0xFF01 + rnd.Next(0x5E) : 0x24B6 + rnd.Next(52))); break;   // full width, circled
            case 18: sb.Append((char)rnd.Next(0x10000)); break;
            case 19: sb.Append(" \t\r\n\u00A0\u2003\u3000\v\f "[rnd.Next(10)]); break;
            default: sb.Append(Tokens[rnd.Next(Tokens.Length)]); break;
        }
    }

    // Any text up to about maxLen characters: of every kind of character, of two or three kinds, or of one
    static string Any(int maxLen)
    {
        int len = rnd.Next(5) == 0 ? rnd.Next(4) : rnd.Next(maxLen + 1);
        var sb = new StringBuilder();
        int mode = rnd.Next(4);
        int[] few = { rnd.Next(Classes), rnd.Next(Classes), rnd.Next(Classes) };
        while (sb.Length < len) Append(sb, mode == 0 ? rnd.Next(Classes) : mode == 3 ? few[0] : few[rnd.Next(3)]);
        return sb.ToString();
    }

    // Text as people write it, with odd characters here and there: words of the languages, spaces, marks, line breaks
    static string Prose(int maxWords)
    {
        var sb = new StringBuilder();
        int n = rnd.Next(maxWords + 1);
        string lang = Pick(langs);
        for (int i = 0; i < n; i++)
        {
            if (rnd.Next(6) == 0) lang = Pick(langs);
            int k = rnd.Next(12);
            if (k < 8) sb.Append(rnd.Next(4) == 0 ? Mutate(Word(lang)) : Word(lang));
            else if (k < 10) sb.Append(Any(6));
            else sb.Append(rnd.Next(100000));
            string marks = " ,.!?…;:\n\t-—\"'()«»";
            sb.Append(marks[rnd.Next(rnd.Next(3) == 0 ? marks.Length : 1)]);
            if (rnd.Next(3) > 0) sb.Append(rnd.Next(12) == 0 ? "\r\n" : " ");
        }
        return sb.ToString();
    }

    static string Text(int maxLen) { return rnd.Next(2) == 0 ? Any(maxLen) : Prose(maxLen / 5); }

    static char One(string of) { return of[rnd.Next(of.Length)]; }
    static T Pick<T>(IList<T> list) { return list[rnd.Next(list.Count)]; }
    static string Word(string lang) { return Pick(lexicon[lang]); }

    static string Mutate(string word)
    {
        var sb = new StringBuilder(word);
        for (int n = rnd.Next(4); n > 0; n--)
        {
            int at = rnd.Next(sb.Length + 1);
            switch (rnd.Next(10))
            {
                case 0: sb.Insert(at, "'’`-."[rnd.Next(5)]); break;
                case 1: sb.Insert(at, (char)('0' + rnd.Next(10))); break;
                case 2: sb.Insert(at, "/\\=|$%&*+_@"[rnd.Next(11)]); break;
                case 3: if (sb.Length > 0) sb.Remove(rnd.Next(sb.Length), 1); break;
                case 4: if (sb.Length > 1) { int i = rnd.Next(sb.Length - 1); char c = sb[i]; sb[i] = sb[i + 1]; sb[i + 1] = c; } break;
                case 5: sb = new StringBuilder(sb.ToString().ToUpperInvariant()); break;
                case 6: if (sb.Length > 0) sb[0] = char.ToUpperInvariant(sb[0]); break;
                case 7: sb.Insert(0, "([{\"«„"[rnd.Next(6)]).Append(".,!?:;)]}\"»“"[rnd.Next(12)]); break;
                case 8: for (int i = 0; i < sb.Length; i++) sb[i] = rnd.Next(2) == 0 ? char.ToUpperInvariant(sb[i]) : char.ToLowerInvariant(sb[i]); break;
                default: var one = new StringBuilder(); Append(one, rnd.Next(Classes)); sb.Insert(at, one.ToString()); break;
            }
        }
        return sb.ToString();
    }

    static bool WellFormed(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i])) { if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) return false; i++; }
            else if (char.IsLowSurrogate(s[i]) || s[i] >= 0xFFFE) return false;
        }
        return true;
    }

    // The same letters however their accents are stored; text with what isn't a character (U+FDD0, U+10FFFF) as it is
    static string Nfc(string s)
    {
        try { return s.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return s; }
    }

    // ---------- Detector ----------

    static void FuzzDetector()
    {
        foreach (string T in langs)
            for (int i = N(4000); i > 0; i--)
            {
                string A = Pick(langs), typed, alt;
                int kind = rnd.Next(10);
                if (kind < 6)
                {
                    // The keys of a word of either language, with slips of the fingers, as both layouts type them
                    bool own = rnd.Next(3) == 0;
                    var keys = layouts[own ? T : A].Keys(Word(own ? T : A)) ?? new List<KeyStroke>();
                    MutateKeys(keys);
                    typed = layouts[T].Type(keys);
                    alt = layouts[A].Type(keys);
                }
                else if (kind < 8) { typed = Mutate(Word(T)); alt = rnd.Next(2) == 0 ? Mutate(Word(A)) : Any(12); }
                else { typed = Any(40); alt = rnd.Next(3) == 0 ? Mutate(typed) : Any(40); }
                DetectorCalls(typed, alt, T, A);
            }
    }

    static void MutateKeys(List<KeyStroke> keys)
    {
        for (int n = rnd.Next(4); n > 0; n--)
        {
            int at = rnd.Next(keys.Count + 1);
            var key = new KeyStroke(Pick(typingScans), rnd.Next(4) == 0, rnd.Next(8) == 0, rnd.Next(10) == 0);
            switch (rnd.Next(7))
            {
                case 0: keys.Insert(at, key); break;
                case 1: keys.Add(key); break;
                case 2: if (keys.Count > 0) keys.RemoveAt(rnd.Next(keys.Count)); break;
                case 3: for (int i = 0; i < keys.Count; i++) keys[i] = new KeyStroke(keys[i].Scan, keys[i].Shift, true, keys[i].AltGr); break;
                case 4: for (int i = 0; i < keys.Count; i++) keys[i] = new KeyStroke(keys[i].Scan, true, keys[i].Caps, keys[i].AltGr); break;
                case 5: if (keys.Count > 0) { int i = rnd.Next(keys.Count); keys[i] = new KeyStroke(keys[i].Scan, !keys[i].Shift, keys[i].Caps, keys[i].AltGr); } break;
                default: keys.Insert(at, new KeyStroke(rnd.Next(-2, 300), false, false, false)); break;   // a key that types nothing
            }
        }
    }

    static void DetectorCalls(string typed, string alt, string T, string A)
    {
        LangModel Tm = models[T], Am = models[A];
        string context = rnd.Next(4) == 0 ? T : rnd.Next(3) == 0 ? A : rnd.Next(4) == 0 ? "xx" : null;
        string input = typed + " | " + alt + " | " + T + " > " + A + ", after " + (context ?? "nothing");

        Call("Detector.Core", input, () =>
        {
            CheckCore(typed, null, input); CheckCore(typed, T, input); CheckCore(alt, A, input);
        });
        Call("Detector.LooksLikeCode, NeedsDictionary, IsAbbreviation", input, () =>
        {
            Detector.LooksLikeCode(typed); Detector.NeedsDictionary(typed); Detector.IsAbbreviation(typed);
            Detector.LooksLikeCode(alt); Detector.NeedsDictionary(alt); Detector.IsAbbreviation(alt);
        });
        Call("Detector.Skipped", input, () =>
        {
            bool plain = Detector.Skipped(typed, null, null, null), full = Detector.Skipped(typed, alt, Tm, Am);
            Check("Detector.Skipped: a word the plain rule lets through is skipped with the other layout known", plain || !full, input);
            Detector.Skipped(typed, alt, null, Am); Detector.Skipped(typed, alt, Tm, null);
        });
        Call("Detector.IsKnown, Letters", input, () =>
        {
            long count;
            Detector.IsKnown(typed, Tm); Detector.IsKnown(alt, Am);
            Detector.Letters(typed, Tm, out count, true);
            string letters = Detector.Letters(alt, Am, out count);
            Check("Detector.Letters: a count below zero", count >= 0, input);
            Check("Detector.Letters: a count without letters", letters != null || count == 0, input);
        });
        Call("Detector.ShouldConvert", input, () =>
        {
            bool yes = Detector.ShouldConvert(typed, alt, Tm, Am, context);
            Check("Detector.ShouldConvert: another answer the second time", yes == Detector.ShouldConvert(typed, alt, Tm, Am, context), input);
            Detector.ShouldConvert(alt, typed, Am, Tm, context);
        });
        Call("Detector.ShouldSwitchEarly", input, () => { Detector.ShouldSwitchEarly(typed, alt, Tm, Am, context); Detector.ShouldSwitchEarly(alt, typed, Am, Tm, context); });
        Call("Detector.Choose", input, () =>
        {
            var alts = new List<string> { alt };
            var altModels = new List<LangModel> { Am };
            for (int n = rnd.Next(4); n > 0; n--)
            {
                string other = Pick(langs);
                alts.Add(rnd.Next(3) == 0 ? alt : rnd.Next(2) == 0 ? Mutate(alt) : Word(other));
                altModels.Add(rnd.Next(5) == 0 ? null : models[other]);
            }
            // The layout used last is passed only if it takes part in the choice
            int preferred = rnd.Next(-1, alts.Count);
            if (preferred >= 0 && altModels[preferred] == null) preferred = -1;
            int chosen = Detector.Choose(typed, Tm, alts, altModels, context, preferred);
            Check("Detector.Choose: a layout that isn't there, or one without a model", chosen >= -1 && chosen < alts.Count && (chosen < 0 || altModels[chosen] != null), input);
            Check("Detector.Choose: another answer the second time", chosen == Detector.Choose(typed, Tm, alts, altModels, context, preferred), input);
        });
        Call("Detector.KeepPunctuation", input, () =>
        {
            char last = typed.Length > 0 && rnd.Next(4) > 0 ? typed[typed.Length - 1] : (char)rnd.Next(0x10000);
            string kept = Detector.KeepPunctuation(alt, Am, A, last);
            Check("Detector.KeepPunctuation: more than the last character changed",
                kept == alt || (alt.Length > 0 && kept == alt.Substring(0, alt.Length - 1) + last), input, kept);
            Detector.KeepPunctuation(alt, null, A, last);
        });
    }

    static void CheckCore(string s, string lang, string input)
    {
        string core = Detector.Core(s, lang);
        if (core == null) return;
        bool letters = true;
        foreach (char c in core) if (!char.IsLetter(c) && c != '-') letters = false;
        Check("Detector.Core: not letters and hyphens between them", letters && !core.StartsWith("-", StringComparison.Ordinal) && !core.EndsWith("-", StringComparison.Ordinal), input, core);
        Check("Detector.Core: the core of a core is another word", Detector.Core(core, lang) == core, input, core);
    }

    // Words far longer than anyone types: nothing here may grow faster than the word does
    static void FuzzDetectorLong()
    {
        foreach (int length in new[] { 300, 3000, 30000 })
            foreach (string T in langs)
            {
                string A = Pick(langs);
                for (int kind = 0; kind < 4; kind++)
                {
                    var keys = new List<KeyStroke>();
                    if (kind == 0) while (keys.Count < length) keys.Add(new KeyStroke(Pick(typingScans), false, false, false));
                    else
                    {
                        // The same word (or two with a hyphen) over and over: every pair of letters is one the language has
                        string word = Word(kind == 1 ? T : A) + (kind == 3 ? "-" + Word(A) + "-" : "");
                        var one = layouts[kind == 1 ? T : A].Keys(word);
                        if (one == null || one.Count == 0) continue;
                        while (keys.Count < length) keys.AddRange(one);
                    }
                    string typed = layouts[T].Type(keys), alt = layouts[A].Type(keys);
                    string input = typed + " | " + alt + " | " + T + " > " + A;
                    Call("Detector, a word of " + length + " keys", input, () =>
                    {
                        Detector.Core(typed, T); Detector.LooksLikeCode(typed); Detector.NeedsDictionary(typed); Detector.IsAbbreviation(typed);
                        Detector.Skipped(typed, alt, models[T], models[A]);
                        Detector.ShouldConvert(typed, alt, models[T], models[A], null);
                        Detector.ShouldConvert(alt, typed, models[A], models[T], null);
                        Detector.ShouldSwitchEarly(typed, alt, models[T], models[A], null);
                        Detector.Choose(typed, models[T], new[] { alt }, new[] { models[A] }, null, -1);
                        Detector.KeepPunctuation(alt, models[A], A, '.');
                        Detector.IsKnown(alt, models[A]);
                    });
                }
            }
    }

    // ---------- Layout tables ----------

    static void FuzzLayouts()
    {
        foreach (string lang in langs)
        {
            var t = layouts[lang];
            // What the layout types with one key, and its dead keys
            var direct = new List<char>();
            var valid = new List<KeyStroke>();
            var deadKeys = new List<KeyStroke>();
            foreach (int scan in typingScans)
                for (int s = 0; s < 8; s++)
                {
                    var k = new KeyStroke(scan, (s & 1) != 0, (s & 4) != 0, (s & 2) != 0);
                    char c = t.CharFor(k.Scan, k.Shift, k.Caps, k.AltGr);
                    if (c != '\0') { valid.Add(k); if (s < 4 && !direct.Contains(c)) direct.Add(c); }
                    else if (t.IsDead(k)) { valid.Add(k); if (s < 4) deadKeys.Add(k); }
                }

            // Text of the layout's own characters goes to keys and back unchanged
            for (int i = N(1000); i > 0; i--)
            {
                var sb = new StringBuilder();
                for (int n = rnd.Next(30); n > 0; n--) sb.Append(Pick(direct));
                string text = sb.ToString();
                Call("LayoutTable.Keys, Type (" + lang + ")", text, () =>
                {
                    var keys = t.Keys(text);
                    Check("LayoutTable.Keys: the layout's own characters can't be typed (" + lang + ")", keys != null && keys.Count == text.Length, text);
                    if (keys != null) Check("LayoutTable: text typed and read back differs (" + lang + ")", t.Type(keys) == text, text, t.Type(keys));
                });
            }

            // Every dead key before every character: what comes out goes to keys and back the same
            foreach (var d in deadKeys)
                foreach (char c in direct)
                {
                    int scan; bool shift, altGr;
                    t.Find(c, out scan, out shift, out altGr);
                    var two = new List<KeyStroke> { d, new KeyStroke(scan, shift, false, altGr) };
                    string text = null;
                    Call("LayoutTable.Type, a dead key (" + lang + ")", c.ToString(), () => text = t.Type(two));
                    if (text == null || text.Length != 1) continue;   // the accent doesn't go onto this letter
                    Call("LayoutTable.Keys, a letter from a dead key (" + lang + ")", text, () =>
                    {
                        var keys = t.Keys(text);
                        Check("LayoutTable.Keys: a letter typed with a dead key has no keys (" + lang + ")", keys != null, text);
                        if (keys != null) Check("LayoutTable: a letter typed with a dead key reads back another", t.Type(keys) == text, lang + " " + text, t.Type(keys));
                    });
                }

            // Any keys of the layout, with Shift, CapsLock and AltGr: the text, where it can be put into keys, reads back the same
            for (int i = N(1000); i > 0; i--)
            {
                var keys = new List<KeyStroke>();
                for (int n = rnd.Next(20); n > 0; n--) keys.Add(Pick(valid));
                string text = null;
                Call("LayoutTable.Type (" + lang + ")", "", () => text = t.Type(keys));
                if (text == null) continue;
                Call("LayoutTable.Keys of typed text (" + lang + ")", text, () =>
                {
                    Check("LayoutTable.Type: keys of the layout typed U+FFFD (" + lang + ")", text.IndexOf('\uFFFD') < 0, text);
                    var back = t.Keys(text);
                    if (back != null) Check("LayoutTable: typed text put into keys reads back differently (" + lang + ")", t.Type(back) == text, text, t.Type(back));
                });
            }

            // Any text at all: no keys, or keys that type the same text (the same letters, however the accents are stored)
            for (int i = N(1500); i > 0; i--)
            {
                string text = rnd.Next(3) == 0 ? Mutate(Word(lang)) : Any(30);
                Call("LayoutTable.Keys of any text", text + " | " + lang, () =>
                {
                    var keys = t.Keys(text);
                    if (keys == null || !WellFormed(text)) return;
                    string typed = t.Type(keys);
                    Check("LayoutTable: any text put into keys reads back differently (" + lang + ")", Nfc(typed) == Nfc(text), text, typed);
                });
            }

            // Keys no keyboard has
            for (int i = N(500); i > 0; i--)
            {
                var keys = new List<KeyStroke>();
                int[] odd = { 0, -1, 255, 256, 257, 1000, int.MaxValue, int.MinValue, 0x56, 0x39 };
                for (int n = rnd.Next(20); n > 0; n--)
                    keys.Add(new KeyStroke(rnd.Next(3) == 0 ? Pick(odd) : rnd.Next(-10, 300), rnd.Next(2) == 0, rnd.Next(2) == 0, rnd.Next(2) == 0));
                Call("LayoutTable.Type, CharFor, DeadFor, IsDead with keys no keyboard has (" + lang + ")", string.Join(" ", keys.Select(k => k.Scan)), () =>
                {
                    string text = t.Type(keys);
                    Check("LayoutTable.Type: more than two characters a key", text.Length <= keys.Count * 2, text);
                    foreach (var k in keys) { t.CharFor(k.Scan, k.Shift, k.Caps, k.AltGr); t.DeadFor(k.Scan, k.Shift, k.Caps, k.AltGr); t.IsDead(k); }
                });
            }
        }
    }

    static void FuzzRetype()
    {
        var fixCase = typeof(KeyboardWatcher).GetMethod("FixCase", BindingFlags.NonPublic | BindingFlags.Static);
        for (int i = N(20000); i > 0; i--)
        {
            string a = Pick(langs), b = Pick(langs);
            string text = rnd.Next(3) == 0 ? Prose(8) : rnd.Next(2) == 0 ? Mutate(Word(a)) : Any(60);
            Call("KeyboardWatcher.Retype", text + " | " + a + " > " + b, () =>
            {
                string there = KeyboardWatcher.Retype(text, layouts[a], layouts[b]);
                KeyboardWatcher.Retype(there, layouts[b], layouts[a]);
                if (!WellFormed(text)) return;
                string same = KeyboardWatcher.Retype(text, layouts[a], layouts[a]);
                Check("KeyboardWatcher.Retype: into the same layout the text changes", Nfc(same) == Nfc(text), text + " | " + a, same);
            });

            string word = rnd.Next(2) == 0 ? Mutate(Word(a)) : Any(20);
            string lang = rnd.Next(5) == 0 ? null : rnd.Next(4) == 0 ? "tr" : rnd.Next(4) == 0 ? "el" : a;
            Call("KeyboardWatcher.FixCase", word + " | " + lang, () =>
            {
                string once = (string)fixCase.Invoke(null, new object[] { word, false, lang });
                Check("KeyboardWatcher.FixCase: the length changed", once.Length == word.Length, word, once);
                string twice = (string)fixCase.Invoke(null, new object[] { once, false, lang });
                Check("KeyboardWatcher.FixCase: a fixed word is fixed again", twice == once, word + " | " + lang, once + " | " + twice);
            });
        }
        // A long text from the clipboard (ConvertClipboard takes up to 100,000 characters)
        for (int i = 0; i < 6; i++)
        {
            string a = Pick(langs), b = Pick(langs);
            var sb = new StringBuilder();
            while (sb.Length < 100000) sb.Append(i % 2 == 0 ? Prose(50) : Any(200));
            string text = sb.ToString();
            Call("KeyboardWatcher.Retype, 100,000 characters", text + " | " + a + " > " + b, () => KeyboardWatcher.Retype(text, layouts[a], layouts[b]));
        }
    }

    // ---------- Case, sentences, spaces, plain text ----------

    static void FuzzCase()
    {
        var was = Thread.CurrentThread.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "ru-RU", "tr-TR", "el-GR", "de-DE", "" })
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                for (int i = N(4000); i > 0; i--)
                {
                    string text = rnd.Next(3) == 0 ? Any(40) : rnd.Next(2) == 0 ? Prose(6) : Mutate(Word(Pick(langs)));
                    string input = text + " | culture " + culture;
                    Call("CaseConverter.Next", input, () =>
                    {
                        // The first presses may change letters for good (mixed case; ı and ϕ, which capitals don't give
                        // back as they were). After that the presses go round: three more at most bring the same text
                        string first = CaseConverter.Next(text), settled = first, s;
                        for (int k = 0; k < 4; k++) settled = CaseConverter.Next(settled);
                        s = settled;
                        bool round = false;
                        for (int k = 0; k < 3 && !round; k++) { s = CaseConverter.Next(s); round = s == settled; }
                        Check("CaseConverter.Next: the case doesn't come back within three presses", round, input, settled + " | " + s);
                        Check("CaseConverter.Next: the length changed", first.Length == text.Length, input, first);
                    });
                    Call("CaseConverter.ToTitle, FinalSigma", input, () => { CaseConverter.ToTitle(text); CaseConverter.FinalSigma(text); });
                    Call("TextActions.Sentence", input, () =>
                    {
                        string once = TextActions.Sentence(text);
                        Check("TextActions.Sentence: the length changed", once.Length == text.Length, input, once);
                        Check("TextActions.Sentence: done twice differs from once", TextActions.Sentence(once) == once, input, once);
                    });
                    Call("TextActions.Spaces", input, () =>
                    {
                        string once = TextActions.Spaces(text);
                        Check("TextActions.Spaces: done twice differs from once", TextActions.Spaces(once) == once, input, once);
                        if (!text.Contains("\r\n")) Check("TextActions.Spaces: the text grew", once.Length <= text.Length, input, once);
                    });
                    Call("PlainText.Clean", input, () =>
                    {
                        string once = PlainText.Clean(text);
                        if (WellFormed(text)) Check("PlainText.Clean: done twice differs from once", PlainText.Clean(once) == once, input, once);
                    });
                    Call("TextActions: lines, quotes, spaces that don't break, ё, code names", input, () =>
                    {
                        string unique = TextActions.UniqueLines(text);
                        Check("TextActions.UniqueLines: done twice differs from once", TextActions.UniqueLines(unique) == unique, input, unique);
                        string sorted = TextActions.SortLines(text);
                        if (!text.Contains("\r\n")) Check("TextActions.SortLines: the length changed", sorted.Length == text.Length, input, sorted);
                        string quotes = TextActions.Quotes(text);
                        Check("TextActions.Quotes: a straight quote is left", quotes.IndexOf('"') < 0 && quotes.Length == text.Length, input, quotes);
                        Check("TextActions.NoBreakSpaces: the length changed beyond spaces added after № and §",
                            TextActions.NoBreakSpaces(text).Length >= text.Length, input);
                        Check("TextActions.Yo: the length changed", TextActions.Yo(text).Length == text.Length, input);
                        foreach (TextActions.Code style in Enum.GetValues(typeof(TextActions.Code))) TextActions.CodeName(text, style);
                    });
                }
            }
            // Long texts
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
            string[] bricks = { "σ", "\u0301", "a\u0301σ ", " ", "\t ", "\n", "\r\n", "\"", ". ", "е", "ее ", "a ", "1 ", "№ ", "— ", "aB", "😀", "\uD83D" };
            foreach (string brick in bricks)
            {
                string text = new StringBuilder().Insert(0, brick, 200000 / brick.Length).ToString();
                LongText(text, "\"" + Show(brick) + "\" repeated");
            }
            for (int i = 0; i < 4; i++)
            {
                var sb = new StringBuilder();
                while (sb.Length < 200000) sb.Append(i % 2 == 0 ? Prose(50) : Any(300));
                LongText(sb.ToString(), i % 2 == 0 ? "prose" : "any characters");
            }
        }
        finally { Thread.CurrentThread.CurrentCulture = was; }
    }

    static void LongText(string text, string what)
    {
        string input = "200,000 characters: " + what;
        Call("CaseConverter.Next, long", input, () => CaseConverter.Next(CaseConverter.Next(text)));
        Call("TextActions.Sentence, long", input, () => TextActions.Sentence(text));
        Call("TextActions.Spaces, long", input, () => TextActions.Spaces(text));
        Call("PlainText.Clean, long", input, () => PlainText.Clean(text));
        Call("TextActions.SortLines, long", input, () => TextActions.SortLines(text));
        Call("TextActions.UniqueLines, long", input, () => TextActions.UniqueLines(text));
        Call("TextActions.Quotes, long", input, () => TextActions.Quotes(text));
        Call("TextActions.NoBreakSpaces, long", input, () => TextActions.NoBreakSpaces(text));
        Call("TextActions.Yo, long", input, () => TextActions.Yo(text));
        Call("TextActions.CodeName, long", input, () => TextActions.CodeName(text, TextActions.Code.Camel));
        Call("Transliterator.Convert, long", input, () => { Transliterator.Convert(text, "ru"); Transliterator.Convert(text, "el"); });
        Call("WordList.Normalize, long", input, () => WordList.Normalize(text));
        Call("ReplacementFile.Parse, long", input, () => ReplacementFile.Parse(text));
    }

    static void FuzzTranslit()
    {
        string[] to = { "ru", "uk", "be", "sr-cyrl", "sr-latn", "el", "en", null, "xx" };
        for (int i = N(25000); i > 0; i--)
        {
            string lang = Pick(to);
            string text = rnd.Next(3) == 0 ? Any(40) : rnd.Next(2) == 0 ? Prose(6) : Mutate(Word(Pick(langs)));
            string input = text + " | " + lang;
            Call("Transliterator.Convert", input, () =>
            {
                string there = Transliterator.Convert(text, lang);
                Check("Transliterator.Convert: far longer than the text", there.Length <= text.Length * 4, input, there);
                Transliterator.Convert(there, lang);
                Transliterator.Convert(text);
                foreach (string l in to) { Transliterator.ToLatin(text, l); Transliterator.FromLatin(text, l); }
                Transliterator.ToLatin(text); Transliterator.ToCyrillic(text);
            });
        }
    }

    // ---------- Replacements, placeholders, word lists ----------

    static void FuzzReplacements()
    {
        var fill = typeof(Placeholder).GetMethod("Fill", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string), typeof(Func<string>) }, null);
        var merge = typeof(App).GetMethod("MergeWords", BindingFlags.NonPublic | BindingFlags.Static);
        for (int i = N(25000); i > 0; i--)
        {
            string text = Text(60);
            Call("ReplacementFile.Parse", text, () =>
            {
                foreach (var kv in ReplacementFile.Parse(text))
                    Check("ReplacementFile.Parse: an empty or two-word abbreviation, or no text", kv.Key.Length > 0 && kv.Key.IndexOf(' ') < 0 && kv.Value.Length > 0, text, kv.Key);
            });

            // A replacement as Settings writes it comes back the same (spaces at its ends aside, which the file doesn't keep)
            string key = Any(12).Trim(), value = Text(40);
            if (key.Length > 0 && key.IndexOfAny(new[] { ' ', '=', '\n' }) < 0 && !key.StartsWith("#"))
                Call("ReplacementFile.Escape, Parse", key + " = " + value, () =>
                {
                    string line = key + " = " + ReplacementFile.Escape(value);
                    var back = ReplacementFile.Parse("# a comment\n" + line + "\r\n");
                    string want = TrimKept(value);
                    if (want.Length == 0) { Check("ReplacementFile: a replacement with no text is read", back.Count == 0, line); return; }
                    Check("ReplacementFile: a written replacement doesn't come back", back.Count == 1 && back[0].Key == key && back[0].Value == want,
                        line, back.Count == 1 ? back[0].Key + " = " + back[0].Value : back.Count + " read");
                });

            Call("WordList.Normalize", text, () =>
            {
                string once = WordList.Normalize(text);
                Check("WordList.Normalize: done twice differs from once", WordList.Normalize(once) == once, text, once);
                Check("WordList.Normalize: punctuation left at the ends", once.Length == 0 || (char.IsLetterOrDigit(once[0]) && char.IsLetterOrDigit(once[once.Length - 1])), text, once);
                WordList.Normalize(null);
            });

            // {clipboard} is left out of what goes to Fill itself: the clipboard isn't read here. The part that puts the
            // values in is called with a clipboard of its own
            string safe = text;
            for (int at; (at = safe.IndexOf("{clipboard}", StringComparison.OrdinalIgnoreCase)) >= 0; ) safe = safe.Remove(at, 1);
            Call("Placeholder.Fill", safe, () =>
            {
                int back;
                string filled = Placeholder.Fill(safe, out back);
                Check("Placeholder.Fill: the caret goes back further than the text", back >= 0 && back <= filled.Length, safe, back + " | " + filled);
                if (!Placeholder.Has(safe)) Check("Placeholder.Fill: text without placeholders changed", filled == safe && back == 0, safe, filled);
                if (safe.IndexOf("{cursor}", StringComparison.OrdinalIgnoreCase) < 0) Check("Placeholder.Fill: the caret moved without {cursor}", back == 0, safe);
            });
            string clip = rnd.Next(2) == 0 ? "{cursor}{date}{clipboard}" : Any(30);
            string withClip = rnd.Next(2) == 0 ? text + "{clipboard}" + Any(10) + "{Clipboard}" : text;
            Call("Placeholder.Fill with a clipboard", withClip + " | " + clip, () =>
            {
                string filled = (string)fill.Invoke(null, new object[] { withClip, (Func<string>)(() => clip) });
                if (withClip.IndexOf("{clipboard}", StringComparison.OrdinalIgnoreCase) >= 0 && clip.Length > 0)
                    Check("Placeholder.Fill: the clipboard's text isn't there as it was", filled.Contains(clip), withClip + " | " + clip, filled);
            });

            string before = Text(30), now = Text(30);
            Call("App.MergeWords", text + " | " + before + " | " + now, () => merge.Invoke(null, new object[] { text, before, now }));
        }
    }

    // What the file keeps of a replacement's text: spaces at the ends go, but not a tab or a line break (written as \t, \n)
    static string TrimKept(string value)
    {
        int a = 0, b = value.Length;
        while (a < b && char.IsWhiteSpace(value[a]) && value[a] != '\t' && value[a] != '\n') a++;
        while (b > a && char.IsWhiteSpace(value[b - 1]) && value[b - 1] != '\t' && value[b - 1] != '\n') b--;
        return value.Substring(a, b - a);
    }

    // ---------- App rules ----------

    static void FuzzAppRules()
    {
        for (int i = N(20000); i > 0; i--)
        {
            var lines = new List<string>();
            for (int n = rnd.Next(6); n > 0; n--)
            {
                string body = rnd.Next(3) == 0 ? Any(20) : rnd.Next(2) == 0 ? Glob(8) : Word(Pick(langs));
                lines.Add((rnd.Next(2) == 0 ? "title: " : rnd.Next(8) == 0 ? "TITLE:" : "") + body + (rnd.Next(3) == 0 ? " = hotkeys" : rnd.Next(10) == 0 ? "=" : ""));
            }
            string title = rnd.Next(3) == 0 ? Any(60) : rnd.Next(2) == 0 ? Subject(40) : Prose(6);
            string process = rnd.Next(3) == 0 ? Any(10) : Word("en");
            string input = string.Join("\n", lines) + " | " + process + " | " + title;
            Call("AppRules.Parse, Mode", input, () =>
            {
                var rules = AppRules.Parse(lines);
                rules.Mode(process, title); rules.Mode(null, null); rules.Mode("", "");
                bool hotkeys;
                foreach (string line in lines) { AppRules.Split(line, out hotkeys); AppRules.IsTitleRule(line); }
            });

            // A title rule against the regular expression that says what it means
            string pattern = rnd.Next(4) == 0 ? Any(10).Trim() : Glob(10);
            if (pattern.Trim('*').Length == 0 || pattern.IndexOf('\n') >= 0 || pattern.IndexOf('=') >= 0 || pattern.Trim() != pattern || pattern.StartsWith("#")) continue;
            string subject = Subject(14);
            Call("AppRules: a title rule", pattern + " | " + subject, () =>
            {
                bool whole = pattern.IndexOf('*') >= 0 || pattern.IndexOf('?') >= 0;
                string re = Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".");
                bool want = subject.Length > 0 && new Regex(whole ? @"\A" + re + @"\z" : re, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline,
                                         TimeSpan.FromSeconds(5)).IsMatch(subject);
                var mode = AppRules.Parse(new[] { "title: " + pattern + " = hotkeys" }).Mode("app", subject);
                Check("AppRules: a title rule and its regular expression disagree", (mode == AppMode.HotkeysOnly) == want, pattern + " | " + subject, mode.ToString());
            });
        }

        // The strictest rule wins, whatever the order
        var both = AppRules.Parse(new[] { "code = hotkeys", "CODE.EXE", "title: KeePass = hotkeys", "title: * - ssh", "notepad = hotkeys" });
        Check("AppRules: off beats hotkeys only", both.Mode("Code", "x") == AppMode.Off, "code");
        Check("AppRules: a title that contains the text", both.Mode("x", "My keepass 2") == AppMode.HotkeysOnly, "KeePass");
        Check("AppRules: a title that fits the pattern", both.Mode("notepad", "user@host - SSH") == AppMode.Off, "* - ssh");
        Check("AppRules: no rule", both.Mode("x", "ssh - x") == AppMode.Full, "none");

        // Patterns with many stars against long titles that almost fit: a regular expression went back for minutes here
        foreach (int stars in new[] { 3, 8, 20, 60 })
            foreach (int length in new[] { 100, 1000, 30000 })
            {
                string pattern = "title: " + new StringBuilder().Insert(0, "*a", stars) + "*b";
                string title = new string('a', length);
                Call("AppRules.Mode, " + stars + " stars and a title of " + length, pattern, () =>
                {
                    var rules = AppRules.Parse(new[] { pattern, "title: " + new StringBuilder().Insert(0, "?*", stars) + "c = hotkeys", "title: (a+)+$", "title: (a|aa)*b" });
                    Check("AppRules.Mode: a title that doesn't fit", rules.Mode("app", title) == AppMode.Full, pattern);
                    Check("AppRules.Mode: a title that fits", rules.Mode("app", title + "B") == AppMode.Off, pattern);
                    Check("AppRules.Mode: a pattern taken as text", rules.Mode("app", title + "(A+)+$") == AppMode.Off, pattern);
                });
            }
    }

    // A pattern of a few letters, * and ?, and a title of the same few letters: they fit often enough to tell
    static string Glob(int maxLen)
    {
        var sb = new StringBuilder();
        for (int n = rnd.Next(maxLen + 1); n > 0; n--) sb.Append(One("ab*?*aB.İiı(я"));
        return sb.ToString();
    }

    static string Subject(int maxLen)
    {
        var sb = new StringBuilder();
        for (int n = rnd.Next(maxLen + 1); n > 0; n--) sb.Append(One("aabbAB.İiIı(Яя\n*?"));
        return sb.ToString();
    }

    // ---------- Calculator, colors, sounds ----------

    static void FuzzSmall()
    {
        var scrub = typeof(CrashLog).GetMethod("Scrub", BindingFlags.NonPublic | BindingFlags.Static);
        var was = Thread.CurrentThread.CurrentCulture;
        try
        {
            for (int i = N(30000); i > 0; i--)
            {
                if (i % 500 == 0) Thread.CurrentThread.CurrentCulture = new CultureInfo(i % 1000 == 0 ? "ru-RU" : "en-US");
                var sb = new StringBuilder();
                for (int n = rnd.Next(rnd.Next(20) == 0 ? 600 : 30); n > 0; n--) sb.Append("0123456789  ..,,+-*/^()%%=×÷−–'e"[rnd.Next(32)]);
                string expression = rnd.Next(10) == 0 ? Any(20) : sb.ToString();
                Call("Calc.Evaluate, Format", expression, () =>
                {
                    double? v = Calc.Evaluate(expression);
                    if (v.HasValue) Check("Calc.Format: nothing", Calc.Format(v.Value, expression).Length > 0, expression);
                    Calc.Format(rnd.NextDouble() * Math.Pow(10, rnd.Next(-320, 310)) * (rnd.Next(2) == 0 ? -1 : 1), expression);
                });

                string text = Text(40);
                Call("LayoutColors.Parse, Format", text, () =>
                {
                    var colors = LayoutColors.Parse(text);
                    LayoutColors.Parse(null);
                    var back = LayoutColors.Parse(LayoutColors.Format(colors));
                    foreach (var kv in colors)
                        if (kv.Key.IndexOf(';') < 0 && kv.Key.IndexOf('=') < 0 && kv.Key.Length > 0)
                            Check("LayoutColors: a color written and read is another", back.ContainsKey(kv.Key) && back[kv.Key].ToArgb() == kv.Value.ToArgb(), text, kv.Key);
                });
                string sounds = (rnd.Next(2) == 0 ? Pick(Sounds.Events) + " = " + (rnd.Next(2) == 0 ? "file:" : "") : "") + text;
                Call("Sounds.Parse, Format", sounds, () =>
                {
                    var m = Sounds.Parse(sounds);
                    Sounds.Parse(null);
                    Check("Sounds: written and read back differs", Sounds.Format(Sounds.Parse(Sounds.Format(m))) == Sounds.Format(m), sounds);
                });
                string value = Any(8);
                Call("CrashLog.Scrub", text + " | " + value, () => scrub.Invoke(null, new object[] { text + value + text, value, "%NAME%" }));
                Call("HotkeyBox.Allowed", "", () => HotkeyBox.Allowed((Keys)rnd.Next(int.MinValue, int.MaxValue)));
            }
        }
        finally { Thread.CurrentThread.CurrentCulture = was; }
    }

    // ---------- The settings archive ----------

    static byte[] Zip(Action<ZipArchive> fill)
    {
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) fill(zip);
            return ms.ToArray();
        }
    }

    static void Entry(ZipArchive zip, string name, byte[] content)
    {
        using (var s = zip.CreateEntry(name, rnd.Next(3) == 0 ? CompressionLevel.NoCompression : CompressionLevel.Optimal).Open()) s.Write(content, 0, content.Length);
    }

    static byte[] RandomBytes(int max)
    {
        var b = new byte[rnd.Next(max + 1)];
        rnd.NextBytes(b);
        return b;
    }

    static void FuzzArchive()
    {
        string[] names = { "keyfish.txt", "settings.txt", "replacements.txt", "exclusions.txt", "ignored-words.txt", "learned-words.txt", "app-layouts.txt",
                           "no-auto-layouts.txt", "layout-colors.txt", "sounds.txt", "KEYFISH.TXT", "evil.txt", "../replacements.txt", "dir/", "", "settings.txt/x" };
        var seeds = new List<byte[]>();
        string saved = Path.Combine(tempDir, "saved.zip");
        SettingsArchive.Save(saved, new Dictionary<string, string> { { "replacements.txt", "спс = спасибо\r\n" }, { "sounds.txt", "fix = drop\r\n" } },
            new Dictionary<string, int> { { "AutoCorrect", 1 }, { "Hotkey1", 19 } });
        seeds.Add(File.ReadAllBytes(saved));
        for (int i = 0; i < 40; i++)
            seeds.Add(Zip(zip =>
            {
                if (rnd.Next(6) > 0) Entry(zip, "keyfish.txt", Encoding.UTF8.GetBytes("KeyFish settings"));
                for (int n = rnd.Next(8); n > 0; n--)
                {
                    string name = Pick(names);
                    byte[] content;
                    int kind = rnd.Next(6);
                    if (kind == 0) content = RandomBytes(2000);
                    else if (kind == 1) content = Encoding.UTF8.GetBytes(Text(300));
                    else if (kind == 2) content = Encoding.Unicode.GetBytes(Text(300));
                    else
                    {
                        var sb = new StringBuilder();
                        for (int k = rnd.Next(40); k > 0; k--)
                            sb.Append(rnd.Next(2) == 0 ? Pick(SettingsArchive.Values) : Any(8)).Append(rnd.Next(8) == 0 ? "" : "=")
                              .Append(rnd.Next(3) == 0 ? Any(6) : rnd.Next(2) == 0 ? rnd.Next(int.MinValue, int.MaxValue).ToString() : "99999999999999999999").Append(rnd.Next(2) == 0 ? "\r\n" : "\n");
                        content = Encoding.UTF8.GetBytes(sb.ToString());
                    }
                    try { Entry(zip, name, content); } catch (ArgumentException) { }
                }
            }));
        // Files that unpack to more than the archive takes: 6 MB (over the limit for one file) and 80 MB
        seeds.Add(Zip(zip => { Entry(zip, "keyfish.txt", new byte[1]); Entry(zip, "settings.txt", new byte[6 * 1024 * 1024]); }));
        seeds.Add(Zip(zip => { Entry(zip, "keyfish.txt", new byte[1]); Entry(zip, "replacements.txt", new byte[80 * 1024 * 1024]); Entry(zip, "sounds.txt", Encoding.UTF8.GetBytes("fix = file:a|b<>.wav\n")); }));
        seeds.Add(Zip(zip => { Entry(zip, "keyfish.txt", new byte[1]); Entry(zip, "settings.txt", Encoding.UTF8.GetBytes(new StringBuilder().Insert(0, "AutoCorrect=1\n", 300000).ToString())); }));

        string path = Path.Combine(tempDir, "fuzz.zip");
        int n0 = N(4000);
        for (int i = 0; i < n0; i++)
        {
            byte[] bytes;
            string what;
            if (i < seeds.Count) { bytes = seeds[i]; what = "archive " + i + " as built"; }
            else if (rnd.Next(12) == 0) { bytes = RandomBytes(rnd.Next(2) == 0 ? 64 : 5000); if (bytes.Length > 4 && rnd.Next(2) == 0) { bytes[0] = 0x50; bytes[1] = 0x4B; bytes[2] = 3; bytes[3] = 4; } what = "random bytes"; }
            else
            {
                int from = rnd.Next(Math.Min(seeds.Count, 41));   // the big ones are tried only as they are
                bytes = Damage(seeds[from]);
                what = "archive " + from + " damaged";
            }
            File.WriteAllBytes(path, bytes);
            Call("SettingsArchive.Load", what + ", seed " + seed + ", number " + i + ": " + Convert.ToBase64String(bytes, 0, Math.Min(bytes.Length, 150)), () =>
            {
                Dictionary<string, string> files; Dictionary<string, int> values;
                bool ok = SettingsArchive.Load(path, out files, out values);
                Check("SettingsArchive.Load: nothing to read into", files != null && values != null, what);
                foreach (string name in files.Keys) Check("SettingsArchive.Load: a file it doesn't know", Array.IndexOf(names, name) >= 2 && Array.IndexOf(names, name) <= 9, name);
                foreach (string name in values.Keys) Check("SettingsArchive.Load: a value it doesn't know", Array.IndexOf(SettingsArchive.Values, name) >= 0, name);
                foreach (string text in files.Values) Check("SettingsArchive.Load: a file over 5 MB", text.Length <= 5 * 1024 * 1024, what);
                if (!ok) return;
                // What Settings then does with the files read
                string s;
                if (files.TryGetValue("replacements.txt", out s)) ReplacementFile.Parse(s);
                if (files.TryGetValue("exclusions.txt", out s)) AppRules.Parse(s.Split('\n')).Mode("app", "a title");
                if (files.TryGetValue("layout-colors.txt", out s)) LayoutColors.Parse(s);
                if (files.TryGetValue("sounds.txt", out s)) Sounds.Parse(s);
            });
        }
        Call("SettingsArchive.Load: no such file", "", () =>
        {
            Dictionary<string, string> files; Dictionary<string, int> values;
            Check("SettingsArchive.Load: a file that isn't there loads", !SettingsArchive.Load(Path.Combine(tempDir, "none.zip"), out files, out values), "");
        });
    }

    static byte[] Damage(byte[] good)
    {
        var bytes = new List<byte>(good);
        for (int n = 1 + rnd.Next(4); n > 0 && bytes.Count > 0; n--)
        {
            int at = rnd.Next(bytes.Count);
            switch (rnd.Next(8))
            {
                case 0: bytes[at] = (byte)rnd.Next(256); break;
                case 1: bytes[at] ^= (byte)(1 << rnd.Next(8)); break;
                case 2: bytes.RemoveRange(at, bytes.Count - at); break;                                  // cut short
                case 3: bytes.InsertRange(at, RandomBytes(40)); break;
                case 4: bytes.RemoveRange(at, Math.Min(rnd.Next(40), bytes.Count - at)); break;
                case 5: int from = rnd.Next(bytes.Count); bytes.InsertRange(at, bytes.GetRange(from, Math.Min(30, bytes.Count - from))); break;   // a piece copied elsewhere
                default:
                    // A size, an offset or a count in the archive's own records, set to an extreme
                    var marks = new List<int>();
                    for (int i = 0; i + 3 < bytes.Count; i++) if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] <= 6) marks.Add(i);
                    if (marks.Count == 0) break;
                    int field = Pick(marks) + 4 + rnd.Next(44);
                    byte fill = (byte)(rnd.Next(3) == 0 ? 0 : rnd.Next(2) == 0 ? 0xFF : 0x7F);
                    for (int i = field; i < field + (rnd.Next(2) == 0 ? 2 : 4) && i < bytes.Count; i++) bytes[i] = fill;
                    break;
            }
        }
        return bytes.ToArray();
    }

    // ---------- Import from other programs' files ----------

    static void FuzzImport()
    {
        string[] extensions = { ".txt", ".csv", ".tsv", ".ahk", ".yml", ".yaml", ".dic", "" };
        int n0 = N(2500);
        for (int i = 0; i < n0; i++)
        {
            byte[] bytes;
            int kind = rnd.Next(8);
            if (kind == 0) bytes = RandomBytes(600);
            else
            {
                var sb = new StringBuilder();
                for (int n = rnd.Next(25); n > 0; n--)
                {
                    int k = rnd.Next(8);
                    if (k == 0) sb.Append("::").Append(Any(6)).Append("::").Append(Text(20));
                    else if (k == 1) sb.Append(":").Append(Any(3)).Append(":").Append(Any(6)).Append("::").Append(Any(10));
                    else if (k == 2) sb.Append("  - trigger: ").Append(rnd.Next(2) == 0 ? "\"" + Any(8) + "\"" : "'" + Any(8) + "'").Append("\n    replace: ").Append(rnd.Next(2) == 0 ? "\"" + Text(20) + "\"" : Text(20));
                    else if (k == 3) sb.Append(Any(8)).Append(One("\t;,,\"\"")).Append(Text(20));
                    else if (k == 4) sb.Append('"').Append(Any(8)).Append("\",\"").Append(Text(20)).Append(rnd.Next(2) == 0 ? "\"" : "");
                    else if (k == 5) sb.Append(Word(Pick(langs))).Append(rnd.Next(2) == 0 ? "/" + Any(4) : " = " + Text(20));
                    else sb.Append(Text(40));
                    sb.Append(rnd.Next(2) == 0 ? "\r\n" : "\n");
                }
                var encoding = kind == 1 ? Encoding.Unicode : kind == 2 ? Encoding.BigEndianUnicode : kind == 3 ? Encoding.GetEncoding(1251) : kind == 4 ? new UTF8Encoding(true) : (Encoding)new UTF8Encoding(false);
                bytes = encoding.GetPreamble().Concat(encoding.GetBytes(sb.ToString())).ToArray();
            }
            string path = Path.Combine(tempDir, "import" + Pick(extensions));
            File.WriteAllBytes(path, bytes);
            Call("Importer.Replacements, Words", "seed " + seed + ", number " + i + ", " + Path.GetExtension(path) + ": " + Convert.ToBase64String(bytes, 0, Math.Min(bytes.Length, 150)), () =>
            {
                foreach (var kv in Importer.Replacements(path))
                    Check("Importer.Replacements: an abbreviation the editor wouldn't take, or no text",
                        kv.Key.Length > 0 && kv.Key.Length <= 40 && kv.Key.IndexOfAny(new[] { ' ', '=', '\t' }) < 0 && !kv.Key.StartsWith("#") && kv.Value.Length > 0, kv.Key + " = " + kv.Value);
                var words = Importer.Words(path);
                foreach (string w in words) Check("Importer.Words: not a word", w.Length > 0 && w.Length <= 40 && w.IndexOf(' ') < 0, w);
                Check("Importer.Words: a word twice", new HashSet<string>(words, StringComparer.OrdinalIgnoreCase).Count == words.Count, "");
            });
        }

        // Real lists are long: a spelling dictionary has 150,000 words and more
        var big = new StringBuilder();
        for (int i = 0; i < 150000; i++) big.Append("слово").Append(i).Append(i % 3 == 0 ? "/AB" : "").Append("\r\n");
        string dic = Path.Combine(tempDir, "big.dic");
        File.WriteAllText(dic, big.ToString(), Encoding.UTF8);
        Call("Importer.Words, 150,000 words", "слово0/AB, слово1, слово2…", () => Check("Importer.Words: words lost", Importer.Words(dic).Count == 150000, ""));
        big.Clear();
        for (int i = 0; i < 150000; i++) big.Append("k").Append(i).Append(" = text ").Append(i).Append("\r\n");
        string txt = Path.Combine(tempDir, "big.txt");
        File.WriteAllText(txt, big.ToString(), Encoding.UTF8);
        Call("Importer.Replacements, 150,000 lines", "k0 = text 0, k1 = text 1…", () => Check("Importer.Replacements: lines lost", Importer.Replacements(txt).Count == 150000, ""));
        big.Clear();
        for (int i = 0; i < 100000; i++) big.Append("- trigger: \":k").Append(i).Append("\"\n  replace: \"text\"\n");
        string yml = Path.Combine(tempDir, "big.yml");
        File.WriteAllText(yml, big.ToString(), Encoding.UTF8);
        Call("Importer.Replacements, 100,000 Espanso matches", "- trigger: :k0…", () => Check("Importer.Replacements: matches lost", Importer.Replacements(yml).Count == 100000, ""));
        string colons = Path.Combine(tempDir, "colons.ahk");
        File.WriteAllText(colons, ":" + new string('a', 100000) + ":" + new string('b', 100000) + ":\n:x:" + new string(':', 200000) + "\n", Encoding.UTF8);
        Call("Importer.Replacements, a hotstring line of 200,000 characters", ":aaa…:bbb…:", () => Importer.Replacements(colons));
    }
}
