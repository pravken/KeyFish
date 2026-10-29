using System;
using System.Collections.Generic;
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
        Run("Transliterator", () => TranslitTests());
        Run("CaseConverter", () => CaseTests());
        Run("Words and lists", () => WordTests());
        Run("Hotkeys", () => HotkeyTests());
        Run("Settings archive", () => ArchiveTests());
        Run("Import", () => ImportTests());
        Run("Looks", () => SkinTests());

        Console.WriteLine("{0} passed, {1} failed", passed, failed);
        return failed;
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
    }

    static void CaseTests()
    {
        Is("aa -> Aa (each word)", CaseConverter.Next("hello world"), "Hello World");
        string once = CaseConverter.Next("hello"), twice = CaseConverter.Next(once), thrice = CaseConverter.Next(twice);
        True("the case cycles back", thrice == "hello" || CaseConverter.Next(thrice) == "hello");
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
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
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
        Is("an unknown number is Classic", Skin.Find(99), Skin.Classic);
        Is("the days: Oct 24", Skin.Halloween.Season(new DateTime(2026, 10, 24)), true);
        Is("the days: Nov 3", Skin.Halloween.Season(new DateTime(2026, 11, 3)), false);
        True("numbers are unique", Skin.All.Select(k => k.Number).Distinct().Count() == Skin.All.Length);
    }
}
