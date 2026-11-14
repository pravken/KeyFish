using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

// Unit tests of the logic that needs no keyboard or screen: the detector's rules, word handling, transliteration,
// case, word lists, replacements, layout colors, the hotkey rule, the settings archive (including damaged files),
// import from other programs and the merging of word lists. Compiled together with KeyFish's own sources:
//   csc /main:UnitTests tests\UnitTests.cs <all .cs of KeyFish>   then   UnitTests.exe <data dir>
// The exit code is the number of failures (0: all passed). Run on every push by .github\workflows\check.yml
static class UnitTests
{
    static int failed, passed;

    static void Is<T>(string name, T got, T want)
    {
        if (EqualityComparer<T>.Default.Equals(got, want)) { passed++; return; }
        failed++;
        Console.WriteLine("FAIL {0}: got [{1}], want [{2}]", name, got, want);
    }

    static void True(string name, bool ok) { Is(name, ok, true); }

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string data = args.Length > 0 ? args[0] : "data";
        Detector.Source = l => File.ReadAllLines(Path.Combine(data, l + ".txt"), Encoding.UTF8);

        Run("Detector", () => DetectorTests());
        if (Directory.Exists(Path.Combine("tests", "layouts"))) Run("Layouts", () => LayoutTests());
        Run("Transliterator", () => TranslitTests());
        Run("CaseConverter", () => CaseTests());
        Run("Same alphabet", () => SameScriptTests());
        Run("Greek", () => GreekTests());
        Run("Words and lists", () => WordTests());
        Run("Hotkeys", () => HotkeyTests());
        Run("Settings archive", () => ArchiveTests());
        Run("Import", () => ImportTests());
        Run("Looks", () => SkinTests());
        Run("Sounds", () => SoundTests());
        Run("Plain text", () => PlainTests());
        Run("Text actions", () => TextActionTests());
        Run("Calculator", () => CalcTests());
        Run("Windows", () => WindowTests());
        if (Directory.Exists("i18n")) Run("Translations", () => TranslationTests());
        if (Directory.Exists("i18n")) Run("Source encoding", () => EncodingTests());

        Console.WriteLine("{0} passed, {1} failed", passed, failed);
        return failed;
    }

    // UTF-8 read as ANSI and saved again turns each Cyrillic letter into a Latin-1 pair starting with D0 or D1: the sources
    // and translations must not have it
    // A D0/D1 byte read as Latin-1, then the second byte of the letter read as Windows-1252
    static readonly string Moji = "[" + (char)0xD0 + (char)0xD1 + "][" + (char)0x80 + "-" + (char)0xBF + new string(new[] { (char)0x152, (char)0x153, (char)0x160, (char)0x161, (char)0x178, (char)0x17D, (char)0x17E, (char)0x192, (char)0x2C6, (char)0x2DC, (char)0x20AC, (char)0x2122 }) + (char)0x2013 + "-" + (char)0x203A + "]";

    static void EncodingTests()
    {
        var files = Directory.GetFiles(".", "*.cs").Concat(Directory.GetFiles("i18n", "*.txt")).Concat(Directory.GetFiles("tests", "*.cs"));
        foreach (string f in files)
        {
            string text = File.ReadAllText(f, Encoding.UTF8);
            bool bad = System.Text.RegularExpressions.Regex.IsMatch(text, Moji);
            True("no double-encoded text in " + Path.GetFileName(f), !bad);
        }
    }

    static void Run(string group, Action tests)
    {
        try { tests(); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL {0}: {1}", group, ex); }
    }

    static void DetectorTests()
    {
        var en = Detector.Load("en");
        var ru = Detector.Load("ru");
        True("ghbdtn is привет", Detector.ShouldConvert("ghbdtn", "привет", en, ru));
        True("hello stays", !Detector.ShouldConvert("hello", "руддщ", en, ru));
        True("привет stays", !Detector.ShouldConvert("привет", "ghbdtn", ru, en));
        True("don't typed in Russian", Detector.ShouldConvert("вщтэе", "don't", ru, en));
        True("a known abbreviation stays", !Detector.ShouldConvert("VS", "МЫ", en, ru));
        Is("Core strips punctuation", Detector.Core("«Привет!»"), "привет");
        Is("Core: an apostrophe inside isn't a word", Detector.Core("don't"), (string)null);
        Is("Core: Turkish I", Detector.Core("IŞIK", "tr"), "ışık");
        Is("Core: hyphen inside", Detector.Core("кое-как"), "кое-как");
        True("a link is code", Detector.LooksLikeCode("http://example.com"));
        True("a path is code", Detector.LooksLikeCode("C:\\new\\file"));
        True("a word isn't code", !Detector.LooksLikeCode("ghbdtn"));
        True("ghbdtn/ isn't code (привет.)", !Detector.LooksLikeCode("ghbdtn/"));
        True("file.txt needs the dictionary", Detector.NeedsDictionary("file.txt"));
        True("camelCase needs the dictionary", Detector.NeedsDictionary("camelCase"));
        True("a word doesn't", !Detector.NeedsDictionary("word"));
        True("PDF is an abbreviation", Detector.IsAbbreviation("PDF"));
        True("Pdf isn't", !Detector.IsAbbreviation("Pdf"));
        True("a long word in capitals isn't", !Detector.IsAbbreviation("ПРИВЕТ"));
        Is("Choose picks Russian", Detector.Choose("ghbdtn", en, new[] { "привет" }, new[] { ru }, null, -1), 0);

        // Found by tests\Fuzz.cs
        Is("nothing typed: no fix, and no error", Detector.ShouldConvert("", "я", en, ru), false);
        True("a word one letter from another is near it", en.NearWord("helllo", false));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string endless = new StringBuilder().Insert(0, "picked", 5000).ToString();
        True("a word longer than any in the list is near none", !en.NearWord(endless, false) && !en.NearWord(endless, true));
        Detector.ShouldConvert(endless, new StringBuilder().Insert(0, "зшслув", 5000).ToString(), en, ru);
        True("and that is seen at once (" + clock.ElapsedMilliseconds + " ms)", clock.ElapsedMilliseconds < 1000);
    }

    // Layouts other than English and Russian, read from tests\layouts\*.klc: CapsLock, dead keys, and words whose
    // keys type digits or symbols in the layout they were typed in
    static void LayoutTests()
    {
        Func<string, string, LayoutTable> klc = (file, lang) => LayoutTable.FromKlc(Path.Combine("tests", "layouts", file + ".klc"), lang);
        LayoutTable us = klc("kbdus", "en"), ruKeys = klc("kbdru", "ru"), cz = klc("kbdcz", "cs"), pl = klc("kbdpl1", "pl"),
                    de = klc("kbdgr", "de"), fr = klc("kbdfr", "fr"), el = klc("kbdhe", "el");

        // CapsLock is not always Shift
        Is("cs: CapsLock on the digit row types the capital", cz.CharFor(0x04, false, true, false), 'Š');
        Is("cs: CapsLock with Shift there types the digit", cz.CharFor(0x04, true, true, false), '3');
        Is("cs: ŠKODA typed with CapsLock on", cz.Type(cz.Keys("škoda").ConvertAll(k => new KeyStroke(k.Scan, k.Shift, true, k.AltGr))), "ŠKODA");
        Is("pl: CapsLock capitalises a letter", pl.CharFor(0x1E, false, true, false), 'A');
        Is("pl: CapsLock capitalises an AltGr letter", pl.CharFor(0x1E, false, true, true), 'Ą');
        Is("de: CapsLock leaves AltGr alone", de.CharFor(0x10, false, true, true), '@');
        Is("fr: CapsLock types digits", fr.CharFor(0x03, false, true, false), '2');
        Is("ru: CapsLock with Shift types the small letter", ruKeys.CharFor(0x10, true, true, false), 'й');

        // Dead keys
        Is("fr: ê is two keys", fr.Keys("être").Count, 5);
        Is("fr: dead key round trip", fr.Type(fr.Keys("être")), "être");
        Is("el: tonos round trip", el.Type(el.Keys("Καλημέρα")), "Καλημέρα");
        Is("Retype puts the accent on the next letter (el)", KeyboardWatcher.Retype("kalhm;era kosme", us, el), "καλημέρα κοσμε");
        Is("Retype puts the accent on the next letter (fr)", KeyboardWatcher.Retype("жхужу", ruKeys, fr), "même");
        Is("Retype leaves what has no key", KeyboardWatcher.Retype("ghbdtn → vbh", us, ruKeys), "привет → мир");

        // Found by tests\Fuzz.cs: an emoji is two halves in a string, and Normalize throws on a half
        Is("Retype leaves an emoji", KeyboardWatcher.Retype("ghbdtn 😀 vbh", us, ruKeys), "привет 😀 мир");
        Is("Keys: an emoji has none", us.Keys("a😀"), (List<KeyStroke>)null);
        Is("Keys: nor has what isn't a character", us.Keys("a" + (char)0xFFFF + (char)0xFDD0 + (char)0xD800), (List<KeyStroke>)null);
        Is("Retype leaves what isn't a character", KeyboardWatcher.Retype("vbh" + (char)0xFFFE, us, ruKeys), "мир" + (char)0xFFFE);
        // A letter under an accent of two marks from one dead key (Greek ΅), and a letter with its own mark under a dead key
        Is("el: ΐ is ΅ and ι", el.Keys("ΐ") == null ? -1 : el.Keys("ΐ").Count, 2);
        Is("el: Μαΐου round trip", el.Type(el.Keys("Μαΐου") ?? new List<KeyStroke>()), "Μαΐου");
        Is("el: Μαΐου into English keys and back", KeyboardWatcher.Retype(KeyboardWatcher.Retype("Μαΐου", el, us), us, el), "Μαΐου");
        Is("de: ǘ is ´ and ü", de.Type(de.Keys("ǘ") ?? new List<KeyStroke>()), "ǘ");
        Is("cs: ǻ is not ° and á (that is another letter)", cz.Keys("ǻ"), (List<KeyStroke>)null);

        LangModel en = Detector.Load("en"), ru = Detector.Load("ru"), french = Detector.Load("fr"), czech = Detector.Load("cs"),
                  german = Detector.Load("de"), bg = Detector.Load("bg"), uk = Detector.Load("uk"), greek = Detector.Load("el");
        // Digits and symbols where the other layout has letters
        True("plain rule: a digit", Detector.Skipped("abc1", null, null, null));
        True("plain rule: a word", !Detector.Skipped("abc", null, null, null));
        True("été typed in Russian is digits around a letter", !Detector.Skipped("2е2", "été", ru, french));
        True("était typed in Russian", !Detector.Skipped("2ефше", "était", ru, french));
        True("j'ai typed in Russian", !Detector.Skipped("о4фш", "j'ai", ru, french));
        True("dobře typed in Russian", !Detector.Skipped("вщи5у", "dobře", ru, czech));
        True("a number is never a word", Detector.Skipped("3959", "šíří", ru, czech));
        True("a word with a number after it stays", Detector.Skipped("тут9", "není", ru, czech));
        True("что, typed in German ends in _", !Detector.Skipped("xnj_", "что,", german, ru));
        True("трябва typed in English has a slash", !Detector.Skipped("j,s/ld", "трябва", en, bg));
        True("words around a slash stay", Detector.Skipped("he/she", "ру.ыру", en, ru));
        True("an e-mail address", Detector.Skipped("xnj@xnj", "что\"что", en, ru));
        True("a link", Detector.Skipped("http://ghbdtn", "реез://привет", en, ru));
        True("été is fixed", Detector.ShouldConvert("2е2", "été", ru, french));
        True("j'ai is fixed", Detector.ShouldConvert("о4фш", "j'ai", ru, french));

        // Apostrophes and hyphens
        True("здоров'я typed in English", Detector.ShouldConvert("pljhjd`z", "здоров'я", en, uk));
        True("здоров'я typed right stays", !Detector.ShouldConvert("здоров'я", "pljhjd`z", uk, en));
        True("I'm stays (Greek)", !Detector.ShouldConvert("I'm", "Ι'μ", en, greek));
        True("кое-как typed in English", Detector.ShouldConvert("rjt-rfr", "кое-как", en, ru));
        True("a known word in two parts", Detector.IsKnown("peut-être,", french));
        True("scraps with a hyphen aren't a word", !Detector.IsKnown("ě-da", czech));

        // Case that depends on the language
        Is("Core: Greek capital sigma at the end", Detector.Core("ΚΟΣΜΟΣ", "el"), "κοσμος");
        Is("Core: Greek sigma inside stays", Detector.Core("ΣΟΣ-ΣΟΣ", "el"), "σος-σος");
        True("ΚΟΣΜΟΣ typed in English", Detector.ShouldConvert("KOSMOS", "ΚΟΣΜΟΣ", en, greek));
        var fixCase = typeof(KeyboardWatcher).GetMethod("FixCase", BindingFlags.NonPublic | BindingFlags.Static);
        Func<string, string, string> fix = (w, lang) => (string)fixCase.Invoke(null, new object[] { w, false, lang });
        Is("FixCase", fix("пРИВЕТ", "ru"), "Привет");
        Is("FixCase: Turkish i", fix("iSTANBUL", "tr"), "İstanbul");
        Is("FixCase: Turkish I", fix("kIZIL", "tr"), "Kızıl");
        Is("FixCase: not Turkish", fix("iNSIDE", "en"), "Inside");
        Is("FixCase: Greek final sigma", fix("κΟΣΜΟΣ", "el"), "Κοσμος");

        // Letters where the other layout has punctuation
        True("всю is not вс and a dot", !Detector.ShouldConvert("всю", "dc.", uk, en));
        True("a, typed in Ukrainian", Detector.ShouldConvert("фб", "a,", uk, en));
        Is("что; typed in German: Shift+, is Б", Detector.KeepPunctuation("чтоБ", ru, "ru", ';'), "что;");
        Is("привет, typed in English", Detector.KeepPunctuation("приветб", ru, "ru", ','), "привет,");
        Is("нож stays", Detector.KeepPunctuation("нож", ru, "ru", ';'), "нож");
    }

    static void TranslitTests()
    {
        Is("ru to Latin", Transliterator.Convert("Привет, Алексей"), "Privet, Aleksey");
        Is("Latin to ru", Transliterator.Convert("privet eto Aleksey"), "привет это Алексей");
        Is("capitals", Transliterator.Convert("ЖУК"), "ZHUK");
        Is("uk", Transliterator.Convert("Григорій Їжак"), "Hryhorii Yizhak");
        Is("uk without its own letters", Transliterator.Convert("Знешкодження", "uk"), "Zneshkodzhennia");
        Is("sr", Transliterator.Convert("Љубица Ђорђевић"), "Ljubica Đorđević");
        Is("el", Transliterator.Convert("Καλημέρα κόσμε"), "Kalimera kosme");
        Is("to sr", Transliterator.Convert("Ljubica Đorđević", "sr-cyrl"), "Љубица Ђорђевић");
        Is("to el, final sigma", Transliterator.Convert("kalimera kosmos", "el"), "καλιμερα κοσμος");
        Is("to uk", Transliterator.Convert("pryvit", "uk"), "привіт");
        Is("ru words with a uk layout keep ы, э", Transliterator.Convert("это мы", "uk"), "eto my");
        Is("ru words with a be layout keep и, щ", Transliterator.Convert("ищи", "be"), "ishchi");
        Is("el: ου is ou", Transliterator.Convert("ουρανός"), "ouranos");
        Is("el: accented letters count as Greek", Transliterator.Convert("ήλιος ab"), "ilios ab");
        Is("one capital letter among capitals", Transliterator.Convert("Я ЛЮБЛЮ"), "YA LYUBLYU");
        Is("one capital letter before small ones", Transliterator.Convert("Я люблю"), "Ya lyublyu");
        // Found by tests\Fuzz.cs: the case of a word was counted again for each of its capitals
        Is("capitals word by word", Transliterator.Convert("ЖуК ЖУК Жук жуК"), "ZhuK ZHUK Zhuk zhuK");
        Is("capitals word by word, from Latin", Transliterator.Convert("ZHuk ZHUK Zhuk"), "Жук ЖУК Жук");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string letters = Transliterator.Convert(new StringBuilder().Insert(0, "жУ", 50000).ToString());
        True("100,000 letters in a row", letters.StartsWith("zhUzhU") && letters.Length == 150000);
        Transliterator.Convert(new StringBuilder().Insert(0, "aB", 50000).ToString());
        True("take no time (" + clock.ElapsedMilliseconds + " ms)", clock.ElapsedMilliseconds < 1000);
    }

    // Layouts of the same alphabet: the dictionaries alone decide
    static void SameScriptTests()
    {
        var en = Detector.Load("en"); var de = Detector.Load("de"); var fr = Detector.Load("fr");
        var ru = Detector.Load("ru"); var uk = Detector.Load("uk");
        True("German on the English layout", Detector.ShouldConvertSameScript("t;ten", "töten", en, de, null));
        True("Ukrainian on the Russian layout", Detector.ShouldConvertSameScript("ныяк", "ніяк", ru, uk, null));
        True("an English word stays", !Detector.ShouldConvertSameScript("world", "world", en, de, null) && !Detector.ShouldConvertSameScript("lazy", "layz", en, de, null));
        True("a Russian word stays", !Detector.ShouldConvertSameScript("мы", "мі", ru, uk, null) && !Detector.ShouldConvertSameScript("дым", "дим", ru, uk, null));
        True("a letter key read as a mark isn't a mark", !Detector.ShouldConvertSameScript("esperem", "espere;", en, fr, null));
        True("a typo of the typed language stays a typo", !Detector.ShouldConvertSameScript("helo", "helo", en, de, null));
    }

    // Greek: an English user's other alphabet in Cyprus and Greece
    static void GreekTests()
    {
        var en = Detector.Load("en"); var el = Detector.Load("el");
        True("Greek without accents is a Greek word", el.Count("καλημερα") > 0 && el.Count("καλημέρα") > 0);
        True("capitals without accents too", el.Count(Detector.Core("ΚΑΛΗΜΕΡΑ", "el")) > 0);
        True("Greek without accents typed on the English layout is fixed", Detector.ShouldConvert("kalhmera", "καλημερα", en, el, null));
        True("an English word in Greek text with the Greek layout on is fixed", Detector.ShouldConvert("φινε", "fine", el, en, "el") && Detector.ShouldConvert("λατερ", "later", el, en, "el"));
        True("one of the commonest, a letter from a Greek word", Detector.ShouldConvert("τηε", "the", el, en, "el"));
        True("a short Greek word after English stays", !Detector.ShouldConvert("το", "to", el, en, "en") && !Detector.ShouldConvert("με", "me", el, en, "en"));
        True("a Greek word without accents typed right stays", !Detector.ShouldConvert("καλημερα", "kalhmera", el, en, "el"));
    }

    static void CaseTests()
    {
        Is("aa -> Aa (each word)", CaseConverter.Next("hello world"), "Hello World");
        string once = CaseConverter.Next("hello"), twice = CaseConverter.Next(once), thrice = CaseConverter.Next(twice);
        True("the case cycles back", thrice == "hello" || CaseConverter.Next(thrice) == "hello");
        Is("2nd goes on to capitals", CaseConverter.Next("2nd"), "2ND");
        Is("2ND back to small", CaseConverter.Next("2ND"), "2nd");
        Is("Greek final sigma", CaseConverter.Next("ΟΔΟΣ ΚΑΙ"), "οδος και");
        Is("Greek final sigma in title case", CaseConverter.ToTitle("οδοσ"), "Οδος");
        Is("a stress mark doesn't start a word", CaseConverter.ToTitle("за́мок"), "За́мок");
        Is("sentence: a dot inside a word", TextActions.Sentence("open file.txt now. then go"), "Open file.txt now. Then go");
        Is("sentence: e.g. and 3.5", TextActions.Sentence("version 3.5 is out"), "Version 3.5 is out");
        True("guard clock: a check after the word", GuardClock.After(1000, 990));
        True("guard clock: one in the same millisecond", GuardClock.After(1000, 1000));
        True("guard clock: not one before", !GuardClock.After(990, 1000));
        True("guard clock: not one from ages ago", !GuardClock.After(int.MinValue + 5, 10));
        True("guard clock: across the wrap", GuardClock.After(int.MinValue + 5, int.MaxValue - 5));
    }

    static void WordTests()
    {
        Is("Normalize strips punctuation and case", WordList.Normalize(" «Солвере,» "), "солвере");
        Is("Normalize: punctuation only", WordList.Normalize("..."), "");
        var r = ReplacementFile.Parse("спс = спасибо\n# a comment\nсув = С уважением,\\nИмя\n");
        Is("replacements parsed", r.Count, 2);
        True("\\n is a line break", r.Count == 2 && r[1].Value.Contains("\n"));
        var colors = LayoutColors.Parse("EN=#2870DC;RU=#D83B3B;bad;XX=#zz");
        Is("layout colors parsed", colors.Count, 2);
        Is("layout colors round trip", LayoutColors.Parse(LayoutColors.Format(colors))["RU"].ToArgb(), colors["RU"].ToArgb());
        // App rules. Found by tests\Fuzz.cs: a title rule was a regular expression, and one with several * took 12 seconds
        // on a title of sixty letters that didn't fit
        var rules = AppRules.Parse(new[] { "# comment", "code = hotkeys", "Code.exe", "notepad = hotkeys", "title: KeePass = hotkeys", "title: * - ssh",
                                           "title: *a*a*a*a*a*a*a*a*b", "title: (x+)+$", "title: v?.txt = hotkeys", "title: *" });
        Is("an app: off beats hotkeys only", rules.Mode("CODE", "anything"), AppMode.Off);
        Is("an app: hotkeys only", rules.Mode("Notepad", "a.txt"), AppMode.HotkeysOnly);
        Is("no rule", rules.Mode("word", "ssh - letter.docx"), AppMode.Full);
        Is("no rule, no title", rules.Mode("word", ""), AppMode.Full);
        Is("a title that contains the text, in any case", rules.Mode("word", "db.kdbx - KEEPASS 2"), AppMode.HotkeysOnly);
        Is("a title that fits the pattern", rules.Mode("notepad", "me@host - SSH"), AppMode.Off);
        Is("? is one character", rules.Mode("word", "V1.TXT"), AppMode.HotkeysOnly);
        Is("? is not two", rules.Mode("word", "v10.txt"), AppMode.Full);
        Is("a pattern is text, not a regular expression", rules.Mode("word", "is (X+)+$ safe"), AppMode.Off);
        Is("and doesn't work as one", rules.Mode("word", "xxxx"), AppMode.Full);
        Is("* alone is no rule", rules.Mode("word", "any title"), AppMode.Full);
        True("Fits: empty pattern, empty text", AppRules.Fits("", "") && AppRules.Fits("*", "") && !AppRules.Fits("?", "") && !AppRules.Fits("", "a"));
        True("Fits: * in the middle", AppRules.Fits("a*c", "abbbc") && AppRules.Fits("a*c", "ac") && !AppRules.Fits("a*c", "abcd"));
        True("Fits: going back to the last *", AppRules.Fits("*ab*abc", "abababc") && !AppRules.Fits("*ab*abc", "ababab"));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string title = new string('a', 20000);
        Is("many stars, a long title that doesn't fit", rules.Mode("word", title), AppMode.Full);
        Is("and one that does", rules.Mode("word", title + "B"), AppMode.Off);
        True("answered at once (" + clock.ElapsedMilliseconds + " ms)", clock.ElapsedMilliseconds < 1000);

        var merge = typeof(App).GetMethod("MergeWords", BindingFlags.NonPublic | BindingFlags.Static);
        string merged = (string)merge.Invoke(null, new object[] { "a\nb\n", "a\nb\n", "a\nb\nc\n" });
        True("a word learned meanwhile is kept", merged.Contains("c"));
        merged = (string)merge.Invoke(null, new object[] { "a\n", "a\nb\n", "a\nb\n" });
        True("a word removed in Settings stays removed", !merged.Contains("b"));
        merged = (string)merge.Invoke(null, new object[] { "a\nb\n", "a\nb\n", "a\n" });
        True("a word KeyFish removed meanwhile stays removed", !merged.Contains("b"));
    }

    static void HotkeyTests()
    {
        True("Enter alone isn't a hotkey", !HotkeyBox.Allowed(Keys.Return));
        True("a letter alone isn't", !HotkeyBox.Allowed(Keys.A));
        True("Shift+letter isn't", !HotkeyBox.Allowed(Keys.Shift | Keys.A));
        True("an arrow isn't", !HotkeyBox.Allowed(Keys.Left));
        True("Pause is", HotkeyBox.Allowed(Keys.Pause));
        True("Shift+F3 is", HotkeyBox.Allowed(Keys.Shift | Keys.F3));
        True("Ctrl+Shift+Space is", HotkeyBox.Allowed(Keys.Control | Keys.Shift | Keys.Space));
        True("Alt+Pause is", HotkeyBox.Allowed(Keys.Alt | Keys.Pause));
    }

    static void ArchiveTests()
    {
        string dir = Path.Combine(Path.GetTempPath(), "keyfish-unit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string zip = Path.Combine(dir, "s.zip");
            var files = new Dictionary<string, string> { { "replacements.txt", "спс = спасибо\r\n" }, { "ignored-words.txt", "lol\r\n" }, { "evil.txt", "x" } };
            var values = new Dictionary<string, int> { { "AutoCorrect", 1 }, { "Hotkey1", (int)Keys.Pause }, { "Nonsense", 5 } };
            SettingsArchive.Save(zip, files, values);
            Dictionary<string, string> f2; Dictionary<string, int> v2;
            True("an archive loads", SettingsArchive.Load(zip, out f2, out v2));
            Is("the replacements come back", f2.ContainsKey("replacements.txt") ? f2["replacements.txt"] : null, "спс = спасибо\r\n");
            True("unknown files are left out", !f2.ContainsKey("evil.txt"));
            Is("known values come back", v2.ContainsKey("AutoCorrect") ? v2["AutoCorrect"] : -1, 1);
            True("unknown values are left out", !v2.ContainsKey("Nonsense"));

            string junk = Path.Combine(dir, "junk.zip");
            File.WriteAllBytes(junk, Encoding.ASCII.GetBytes("not a zip at all"));
            True("junk isn't an archive", !SettingsArchive.Load(junk, out f2, out v2));

            // Damaged copies of a real archive: loading may fail, but must never throw
            byte[] good = File.ReadAllBytes(zip);
            var rnd = new Random(7);
            int threw = 0;
            for (int i = 0; i < 300; i++)
            {
                var bad = (byte[])good.Clone();
                for (int k = 0; k < 1 + rnd.Next(8); k++) bad[rnd.Next(bad.Length)] = (byte)rnd.Next(256);
                string path = Path.Combine(dir, "bad.zip");
                File.WriteAllBytes(path, bad);
                try { SettingsArchive.Load(path, out f2, out v2); } catch { threw++; }
            }
            Is("damaged archives don't throw", threw, 0);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static void ImportTests()
    {
        string dir = Path.Combine(Path.GetTempPath(), "keyfish-unit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string ahk = Path.Combine(dir, "a.ahk");
            File.WriteAllText(ahk, "; comment\r\n::brb::be right back\r\n:*:sig::Best regards\r\n", Encoding.UTF8);
            var a = Importer.Replacements(ahk);
            Is("AutoHotkey hotstrings", a.Count, 2);
            True("brb expands", a.Any(kv => kv.Key == "brb" && kv.Value == "be right back"));

            string yml = Path.Combine(dir, "e.yml");
            File.WriteAllText(yml, "matches:\n  - trigger: \":thx\"\n    replace: \"thank you\"\n", Encoding.UTF8);
            var e = Importer.Replacements(yml);
            True("Espanso", e.Any(kv => kv.Value == "thank you"));

            string csv = Path.Combine(dir, "c.csv");
            File.WriteAllText(csv, "спс;спасибо\r\nпжл;пожалуйста\r\n", Encoding.UTF8);
            Is("CSV", Importer.Replacements(csv).Count, 2);

            string words = Path.Combine(dir, "w.txt");
            File.WriteAllText(words, "# list\r\nlol\r\nкек\r\n\r\n", Encoding.UTF8);
            Is("a word list", Importer.Words(words).Count, 2);

            // Found by tests\Fuzz.cs: each new word was looked up among all those before it, minutes for a spelling dictionary
            var big = new StringBuilder();
            for (int i = 0; i < 100000; i++) big.Append("слово").Append(i).Append(i % 2 == 0 ? "/AB\r\n" : "\r\n");
            big.Append("СЛОВО7\r\nслово8\r\n");
            string dic = Path.Combine(dir, "big.dic");
            File.WriteAllText(dic, big.ToString(), Encoding.UTF8);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Is("a spelling dictionary of 100,000 words, repeats left out", Importer.Words(dic).Count, 100000);
            big.Clear();
            for (int i = 0; i < 100000; i++) big.Append("k").Append(i).Append(" = text ").Append(i).Append("\r\n");
            big.Append("K7 = again\r\n");
            string many = Path.Combine(dir, "many.txt");
            File.WriteAllText(many, big.ToString(), Encoding.UTF8);
            var all = Importer.Replacements(many);
            Is("100,000 replacements, repeats left out", all.Count, 100000);
            Is("the first of the repeats is kept", all[7].Value, "text 7");
            True("imported at once (" + clock.ElapsedMilliseconds + " ms)", clock.ElapsedMilliseconds < 3000);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // Every English text of the interface (the second string of L.T and L.F, and the looks' names) is in each
    // i18n\*.txt: otherwise that language shows it in English. Lines written into files ("# …") stay English
    static void TranslationTests()
    {
        const string lit = "\"((?:[^\"\\\\]|\\\\.)*)\"";
        var call = new System.Text.RegularExpressions.Regex(@"L\.[TF]\(\s*" + lit + @"\s*,\s*" + lit);
        var skin = new System.Text.RegularExpressions.Regex(@"(?:NameEn|SeasonEn|ShortEn) = " + lit);
        var keys = new List<string>();
        foreach (string file in Directory.GetFiles(".", "*.cs"))
        {
            string src = string.Join("\n", File.ReadAllLines(file, Encoding.UTF8).Where(l => !l.TrimStart().StartsWith("//")));
            foreach (System.Text.RegularExpressions.Match m in call.Matches(src)) keys.Add(m.Groups[2].Value);
            foreach (System.Text.RegularExpressions.Match m in skin.Matches(src)) keys.Add(m.Groups[1].Value);
        }
        keys = keys.Select(k => k.Replace("\\\"", "\"").Replace("\\\\", "\\")).Where(k => !k.StartsWith("#")).Distinct().ToList();
        True("texts found", keys.Count > 100);
        foreach (string path in Directory.GetFiles("i18n", "*.txt"))
        {
            var have = new HashSet<string>(File.ReadAllLines(path, Encoding.UTF8).Where(l => !l.StartsWith("#") && l.Contains('\t')).Select(l => l.Split('\t')[0]));
            var missing = keys.Where(k => !have.Contains(k)).ToList();
            Is(Path.GetFileName(path) + " missing " + string.Join(" | ", missing.Take(5)), missing.Count, 0);
        }
    }

    static void TextActionTests()
    {
        var was = System.Threading.Thread.CurrentThread.CurrentCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
        try
        {
            Is("sentence", TextActions.Sentence("пРИВЕТ. как ДЕЛА? хорошо"), "Привет. Как дела? Хорошо");
            Is("sentence: lines", TextActions.Sentence("один\nдва"), "Один\nДва");
            Is("spaces", TextActions.Spaces("  a   b \t c  \n\n\n\nd  \n\n"), "a b c\n\nd");
            Is("spaces keep CRLF", TextActions.Spaces("a  b\r\nc"), "a b\r\nc");
            Is("sort", TextActions.SortLines("в\nа\nБ\n"), "а\nБ\nв\n");
            Is("sort numbers by value", TextActions.SortLines("item 10\r\nitem 2\r\nitem 1"), "item 1\r\nitem 2\r\nitem 10");
            Is("unique", TextActions.UniqueLines("a\nb\na\nc\nb"), "a\nb\nc");
            Is("snake", TextActions.CodeName("User Name", TextActions.Code.Snake), "user_name");
            Is("camel", TextActions.CodeName("user_name id", TextActions.Code.Camel), "userNameId");
            Is("pascal from camel", TextActions.CodeName("userName", TextActions.Code.Pascal), "UserName");
            Is("kebab from acronym", TextActions.CodeName("HTMLParser", TextActions.Code.Kebab), "html-parser");
            Is("constant", TextActions.CodeName("max-size2", TextActions.Code.Constant), "MAX_SIZE2");
            Is("quotes, Cyrillic", TextActions.Quotes("он сказал \"привет\""), "он сказал «привет»");
            Is("quotes inside quotes", TextActions.Quotes("\"фильм \"Брат\" вышел\""), "«фильм „Брат“ вышел»");
            Is("quotes, Latin", TextActions.Quotes("say \"hi\" (\"yes\")"), "say “hi” (“yes”)");
            Is("no-break: short words", TextActions.NoBreakSpaces("я в доме и на улице"), "я в доме и на улице");
            Is("no-break: dash and numbers", TextActions.NoBreakSpaces("Москва — 10 кг, № 5"), "Москва — 10 кг, № 5");
            Is("yo", TextActions.Yo("Еще ее ЕЩЕ"), "Ещё её ЕЩЁ");
            Is("yo: ambiguous words stay", TextActions.Yo("все звезды черта"), "все звезды черта");
            Is("yo: все-таки", TextActions.Yo("Все-таки"), "Всё-таки");
            Is("yo: other words stay", TextActions.Yo("лет дом hello"), "лет дом hello");
        }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture = was; }
    }

    static void CalcTests()
    {
        var was = System.Threading.Thread.CurrentThread.CurrentCulture;
        Func<string, string> calc = e => { var v = Calc.Evaluate(e); return v.HasValue ? Calc.Format(v.Value, e) : null; };
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
            Is("product with a decimal comma", calc("1250*12*0,87"), "13050");
            Is("precedence and brackets", calc("2+3*(4-1)"), "11");
            Is("decimal comma kept", calc("1/4"), "0,25");
            Is("thousands with spaces", calc("1 234,5 × 2"), "2469");
            Is("percent added", calc("200+15%"), "230");
            Is("percent taken", calc("200 − 10%"), "180");
            Is("percent of", calc("200*15%"), "30");
            Is("power", calc("2^10"), "1024");
            Is("trailing equals", calc("7÷2 ="), "3,5");
            Is("float noise rounded", calc("0.1+0.2"), "0.3");
            Is("negative", calc("3-5"), "-2");
            Is("a number alone isn't", calc("42"), null);
            Is("a negative number alone isn't", calc("-5"), null);
            Is("a date isn't", calc("12.03.2026"), null);
            Is("a time isn't", calc("12:30"), null);
            Is("words aren't", calc("2 + два"), null);
            Is("division by zero", calc("1/0"), null);
            Is("unclosed bracket", calc("(1+2"), null);
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Is("thousands comma", calc("1,234*2"), "2468");
            Is("decimal point", calc("1.5*3"), "4.5");
        }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture = was; }
    }

    static void PlainTests()
    {
        Is("math bold", PlainText.Clean("\U0001D41B\U0001D428\U0001D425\U0001D41D"), "bold");
        Is("math italic sans", PlainText.Clean("\U0001D62A\U0001D635\U0001D622\U0001D62D"), "ital");
        Is("full width", PlainText.Clean("\uFF46\uFF55\uFF4C\uFF4C\u3000\uFF11"), "full 1");
        Is("circled", PlainText.Clean("\u24B6\u24D1"), "Ab");
        Is("strike-through", PlainText.Clean("s\u0336t\u0336"), "st");
        Is("invisible", PlainText.Clean("a\u200Bb\u00ADc\uFEFF"), "abc");
        Is("Cyrillic and emoji stay", PlainText.Clean("привет 👨\u200D👩 é"), "привет 👨\u200D👩 é");
        Is("ordinary text unchanged", PlainText.Clean("Hello, мир! 1+1=2…"), "Hello, мир! 1+1=2…");
    }

    static void SoundTests()
    {
        var m = Sounds.Parse("fix = drop\nundo = file:my sound.wav\ntypo = file:..\\evil.wav\npause = nothing\nbogus = bloop\n");
        Is("known sounds are kept", m.Count, 2);
        Is("a built-in one", m["fix"], "drop");
        Is("a file by name", m["undo"], "file:my sound.wav");
        True("a path out of the folder is dropped", !m.ContainsKey("typo"));
        Is("written and read back", Sounds.Format(Sounds.Parse(Sounds.Format(m))), Sounds.Format(m));
        // Found by tests\Fuzz.cs: Path.GetFileName throws on these, and with it the start of KeyFish and the import of settings
        var odd = Sounds.Parse("fix = file:a|b.wav\nundo = file:<\"\x01>.wav\ntypo = file:a*.wav\npause = file:C:evil.wav\nhotkey = file:good one.wav\n");
        Is("names no file can have are dropped", odd.Count, 1);
        Is("the others stay", odd.ContainsKey("hotkey") ? odd["hotkey"] : null, "file:good one.wav");
        Is("default for a fix", Sounds.Default(Sounds.Fix), Sounds.Look);
        Is("default for an undo", Sounds.Default(Sounds.Undo), Sounds.None);
        foreach (string s in Sounds.Builtin)
        {
            byte[] w = Sounds.Make(s);
            True(s + " is a WAV", w != null && w.Length > 44 && Encoding.ASCII.GetString(w, 0, 4) == "RIFF");
            int peak = 0;
            for (int i = 44; i + 1 < w.Length; i += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(w, i)));
            True(s + " is soft (no clipping)", peak > 1000 && peak < 16000);
            True(s + " has a name", Sounds.SoundName(s) != s);
        }
        foreach (var skin in Skin.All) True(skin.Name + " sound is built in", Array.IndexOf(Sounds.Builtin, skin.Sound) >= 0);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    // A message with buttons of its own (the crash report offer) closes on each of them and tells which; the hidden
    // window agrees when Windows asks KeyFish to close (the Store updating it) and closes it once
    static void WindowTests()
    {
        var onClick = typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance);
        for (int pick = 0; pick < 3; pick++)
        {
            string error = null;
            var timer = new Timer { Interval = 50 };
            int target = pick;
            timer.Tick += (s, e) =>
            {
                var box = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == "ThemedMessage");
                if (box == null) return;
                timer.Stop();
                var button = box.Controls.Cast<Control>().FirstOrDefault(c => c.Text == "b" + target);
                try { onClick.Invoke(button, new object[] { EventArgs.Empty }); }
                catch (TargetInvocationException ex) { error = ex.InnerException.GetType().Name; }
                if (box.Visible) box.Close();
            };
            timer.Start();
            int got = ThemedMessage.Choose("text", "KeyFish", MessageBoxIcon.Warning, "b0", "b1", "b2");
            timer.Dispose();
            Is("own button " + pick + ": no error", error, null);
            Is("own button " + pick + ": chosen", got, pick);
        }

        int closed = 0;
        var window = new EndSessionWindow(() => closed++);
        var closeApp = (IntPtr)1;   // ENDSESSION_CLOSEAPP, as the Restart Manager sends it
        Is("close request: agreed", SendMessage(window.Handle, 0x0011, IntPtr.Zero, closeApp), (IntPtr)1);
        Is("not closed by the question alone", closed, 0);
        SendMessage(window.Handle, 0x0016, IntPtr.Zero, closeApp);
        Is("not closed when the request is called off", closed, 0);
        SendMessage(window.Handle, 0x0016, (IntPtr)1, closeApp);
        Is("closed on the request", closed, 1);
        SendMessage(window.Handle, 0x0016, (IntPtr)1, closeApp);
        Is("closed once", closed, 1);
        window.DestroyHandle();
    }

    static void SkinTests()
    {
        var halloween = new DateTime(2026, 10, 31);
        var summer = new DateTime(2026, 7, 1);
        Is("Halloween comes by itself", Skin.For(0, true, halloween, false), Skin.Halloween);
        Is("not when switched off", Skin.For(0, false, halloween, false), Skin.Classic);
        Is("and leaves after", Skin.For(0, true, summer, false), Skin.Classic);
        Is("chosen, it stays all year", Skin.For(1, true, summer, false), Skin.Halloween);
        Is("none in high contrast", Skin.For(1, true, halloween, true), Skin.Classic);
        Is("another one chosen on its days wins", Skin.For(2, true, halloween, false, new DateTime(2026, 10, 29)), Skin.Custom);
        Is("Classic chosen on its days too", Skin.For(0, true, halloween, false, new DateTime(2026, 10, 25)), Skin.Classic);
        Is("but it comes again next year", Skin.For(0, true, halloween.AddYears(1), false, new DateTime(2026, 10, 29)), Skin.Halloween);
        Is("declined before its days count for nothing", Skin.For(0, true, halloween, false, new DateTime(2026, 10, 1)), Skin.Halloween);
        // Settings show the look in use (Halloween, with Custom saved); Classic is picked there and saved
        var shown = Skin.For(2, true, halloween, false);
        Is("Settings show the holiday's look", shown, Skin.Halloween);
        Is("Classic picked: it applies", Skin.For(0, true, halloween, false, Skin.Declines(0, halloween)), Skin.Classic);
        Is("Custom picked: it applies", Skin.For(2, true, halloween, false, Skin.Declines(2, halloween)), Skin.Custom);
        Is("Halloween picked: nothing declined", Skin.Declines(1, halloween), DateTime.MinValue);
        Is("picked outside holidays: nothing declined", Skin.Declines(0, summer), DateTime.MinValue);
        Is("an unknown number is Classic", Skin.Find(99), Skin.Classic);
        // Layout colors: blue for the first Latin layout, orange for the first of another alphabet, then each its own
        var layouts = new[] { new LayoutTable { Name = "en-US", Script = "Latin" }, new LayoutTable { Name = "ru-RU", Script = "Cyrillic" },
                              new LayoutTable { Name = "uk-UA", Script = "Cyrillic" }, new LayoutTable { Name = "de-DE", Script = "Latin" } };
        var none = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        Is("first Latin is blue", LayoutColors.For("EN", false, layouts, none), LetterIcon.Latin);
        Is("first Cyrillic is orange", LayoutColors.For("RU", true, layouts, none), LetterIcon.Other);
        var uk = LayoutColors.For("UK", true, layouts, none);
        var de = LayoutColors.For("DE", false, layouts, none);
        True("the third has its own color", uk != LetterIcon.Latin && uk != LetterIcon.Other);
        True("the fourth has another one", de != uk && de != LetterIcon.Latin && de != LetterIcon.Other);
        var chosen = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase) { { "RU", uk } };
        Is("a chosen color stays", LayoutColors.For("RU", true, layouts, chosen), uk);
        True("and no other layout gets it", LayoutColors.For("UK", true, layouts, chosen) != uk);
        Is("the days: Oct 24", Skin.Halloween.Season(new DateTime(2026, 10, 24)), true);
        Is("the days: Nov 3", Skin.Halloween.Season(new DateTime(2026, 11, 3)), false);
        True("numbers are unique", Skin.All.Select(k => k.Number).Distinct().Count() == Skin.All.Length);
        var custom = Skin.Make(Color.FromArgb(200, 30, 60), Color.FromArgb(20, 160, 90));
        Is("custom accent", custom.Accent.ToArgb(), Color.FromArgb(200, 30, 60).ToArgb());
        Is("the custom fish in the tray takes the accent", custom.Latin.ToArgb(), Color.FromArgb(200, 30, 60).ToArgb());
        True("custom dark leans to the window color", custom.Dark.Back.G > Skin.Classic.Dark.Back.G && custom.Dark.Back.R <= Skin.Classic.Dark.Back.R + 2);
        Is("no colors chosen: the defaults", Skin.Make(Color.Empty, Color.Empty).Accent.ToArgb(), Skin.DefaultAccent.ToArgb());
        Is("the custom look by number", Skin.Find(2), Skin.Custom);
    }
}
