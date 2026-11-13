using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

// A physical key press: the scan code identifies the key itself, whatever the layout
public struct KeyStroke
{
    public int Scan; public bool Shift, Caps, AltGr;
    public KeyStroke(int scan, bool shift, bool caps, bool altGr) { Scan = scan; Shift = shift; Caps = caps; AltGr = altGr; }
}

// What every physical key types in one keyboard layout. Keys are scan codes, not virtual-key codes:
// the same physical key has different virtual-key codes in, say, German QWERTZ and US QWERTY.
public class LayoutTable
{
    // Shift states: 0 — none, 1 — Shift, 2 — AltGr (Ctrl+Alt), 3 — Shift+AltGr; the same four with CapsLock on are 4–7.
    // CapsLock has its own states because it is not always Shift: on the Czech digit row it types the capital letter
    // (š → Š, where Shift types 3), in Polish it also capitalises AltGr letters (Ą), in German it leaves AltGr alone (@)
    readonly char[,] chars = new char[256, 8];
    readonly char[,] dead = new char[256, 8];            // dead keys: an accent that goes onto the next letter (Greek ´, French ^)
    readonly Dictionary<char, int> reverse = new Dictionary<char, int>();       // char → scan * 4 + state
    readonly Dictionary<char, int> reverseDead = new Dictionary<char, int>();   // combining accent → dead key

    public IntPtr Hkl;
    public string Lang;      // language model code (en, ru, el…); null if KeyFish has no model for the layout
    public string Script;    // the alphabet of the layout's letters: Latin, Cyrillic, Greek, Hebrew, Georgian
    public string Name;

    public char CharFor(int scan, bool shift, bool caps, bool altGr)
    {
        if (scan <= 0 || scan > 255) return '\0';
        return chars[scan, State(scan, shift, caps, altGr)];
    }

    public char DeadFor(int scan, bool shift, bool caps, bool altGr)
    {
        if (scan <= 0 || scan > 255) return '\0';
        return dead[scan, State(scan, shift, caps, altGr)];
    }

    static int State(int scan, bool shift, bool caps, bool altGr)
    {
        return (shift ? 1 : 0) + (altGr ? 2 : 0) + (caps ? 4 : 0);
    }

    public bool IsDead(KeyStroke k) { return CharFor(k.Scan, k.Shift, k.Caps, k.AltGr) == '\0' && DeadFor(k.Scan, k.Shift, k.Caps, k.AltGr) != '\0'; }

    // The text these keys type in this layout. A dead key puts its accent on the next letter (΄ + ε → έ);
    // a key with nothing in this layout gives U+FFFD, which spoils the word
    public string Type(List<KeyStroke> keys)
    {
        var sb = new StringBuilder(keys.Count);
        char pending = '\0';
        foreach (var k in keys)
        {
            char c = CharFor(k.Scan, k.Shift, k.Caps, k.AltGr);
            if (c == '\0')
            {
                char d = DeadFor(k.Scan, k.Shift, k.Caps, k.AltGr);
                if (d == '\0') { sb.Append('�'); continue; }
                if (pending != '\0') sb.Append(pending);   // two dead keys in a row: Windows types the first one as is
                pending = d;
                continue;
            }
            if (pending != '\0')
            {
                char mark = Combining(pending);
                string composed = mark == '\0' ? null : (c.ToString() + mark).Normalize(NormalizationForm.FormC);
                // An accent that doesn't go onto this letter is typed separately, as Windows does
                if (composed != null && composed.Length == 1) sb.Append(composed);
                else sb.Append(pending).Append(c);
                pending = '\0';
            }
            else sb.Append(c);
        }
        if (pending != '\0') sb.Append(pending);
        return sb.ToString();
    }

    // The keys that type this text in this layout, including accented letters typed with a dead key; null if it can't be typed
    public List<KeyStroke> Keys(string text)
    {
        var keys = new List<KeyStroke>(text.Length);
        foreach (char c in text)
        {
            int scan; bool shift, altGr;
            if (Find(c, out scan, out shift, out altGr)) { keys.Add(new KeyStroke(scan, shift, false, altGr)); continue; }
            // έ = ε + combining acute: the dead key for the accent, then the letter
            string d = Decomposed(c);
            if (d == null || d.Length < 2 || d.Length > 3) return null;
            int code;
            char letter = d[0];
            if (!FindDead(d.Substring(1), out code))
            {
                // ǘ = ü + acute: a letter of the layout that has a mark of its own, under a dead key's accent.
                // Either of the two marks may be the letter's own where their order doesn't matter (ḉ is ç + acute
                // and ć + cedilla); where it does (ǻ is å + acute, but á + ring is another letter), only the first
                if (d.Length != 3) return null;
                bool found = false;
                for (int own = 1; own <= 2 && !found; own++)
                {
                    string marked = (d.Substring(0, 1) + d[own]).Normalize(NormalizationForm.FormC);
                    found = marked.Length == 1 && (marked + d[3 - own]).Normalize(NormalizationForm.FormC) == c.ToString() &&
                            reverseDead.TryGetValue(d[3 - own], out code) && Find(marked[0], out scan, out shift, out altGr);
                    if (found) letter = marked[0];
                }
                if (!found) return null;
            }
            if (!Find(letter, out scan, out shift, out altGr)) return null;
            keys.Add(new KeyStroke(code / 4, (code & 1) != 0, false, (code & 2) != 0));
            keys.Add(new KeyStroke(scan, shift, false, altGr));
        }
        return keys;
    }

    // The letter and its combining marks apart; null for what isn't a character: half of one outside the first 65536
    // (an emoji is two such halves), U+FFFE, U+FFFF. Normalize throws on those, and no key types them
    static string Decomposed(char c)
    {
        if (char.IsSurrogate(c) || c >= '\uFFFE') return null;
        try { return c.ToString().Normalize(NormalizationForm.FormD); }
        catch (ArgumentException) { return null; }
    }

    // The dead key whose accent is these combining marks: one mark, or two that one key gives (Greek ΅ is ¨ and ´: ΐ, ΰ)
    bool FindDead(string marks, out int code)
    {
        if (marks.Length == 1) return reverseDead.TryGetValue(marks[0], out code);
        foreach (var kv in reverseDead)
            if (kv.Key.ToString().Normalize(NormalizationForm.FormD) == marks) { code = kv.Value; return true; }
        code = 0;
        return false;
    }

    // The key (and shift state) that types this character directly
    public bool Find(char c, out int scan, out bool shift, out bool altGr)
    {
        int code;
        scan = 0; shift = altGr = false;
        if (!reverse.TryGetValue(c, out code)) return false;
        scan = code / 4; shift = (code & 1) != 0; altGr = (code & 2) != 0;
        return true;
    }

    // The combining mark for a spacing accent that dead keys type: ´ → U+0301 and so on
    static char Combining(char accent)
    {
        switch (accent)
        {
            case '´': case '΄': case '\'': return '́';   // acute, Greek tonos
            case '`': return '̀';
            case '^': case 'ˆ': return '̂';
            case '~': case '˜': return '̃';
            case '¨': return '̈';                             // diaeresis
            case '΅': return '̈́';                             // Greek dialytika with tonos
            case 'ˇ': return '̌';                             // caron: č, ě
            case '˘': return '̆';                             // breve
            case '°': case '˚': return '̊';              // ring: ů
            case '¸': return '̧';                             // cedilla: ç
            case '˛': return '̨';                             // ogonek: ą
            case '˝': return '̋';                             // double acute: ő
            case '˙': return '̇';                             // dot above: ż
            default: return '\0';
        }
    }

    // Keys of the main block that type parts of words: digit row, three letter rows and the extra key next to left Shift.
    // The numeric keypad is left out: it differs between layout data sources and never types words
    public static bool IsTypingKey(int scan)
    {
        return (scan >= 0x02 && scan <= 0x0D) || (scan >= 0x10 && scan <= 0x1B) || (scan >= 0x1E && scan <= 0x29) ||
               (scan >= 0x2B && scan <= 0x35) || scan == 0x56;
    }

    void Set(int scan, int state, char c)
    {
        if (c == '\0' || char.IsControl(c) || !IsTypingKey(scan)) return;
        chars[scan, state] = c;
        if (state >= 4) return;   // text is turned into keys without CapsLock
        int code = scan * 4 + state;
        // Prefer the simplest way to type a character (no Shift, no AltGr)
        if (!reverse.ContainsKey(c) || code % 4 < reverse[c] % 4) reverse[c] = code;
    }

    void SetDead(int scan, int state, char c)
    {
        if (c == '\0' || !IsTypingKey(scan)) return;
        dead[scan, state] = c;
        if (state >= 4) return;
        char mark = Combining(c);
        int code = scan * 4 + state;
        if (mark != '\0' && (!reverseDead.ContainsKey(mark) || code % 4 < reverseDead[mark] % 4)) reverseDead[mark] = code;
    }

    public static string ScriptOf(char c)
    {
        if (!char.IsLetter(c)) return null;
        if (c < 0x0250) return "Latin";
        if (c >= 0x0370 && c <= 0x03FF) return "Greek";
        if (c >= 0x0400 && c <= 0x04FF) return "Cyrillic";
        if (c >= 0x0590 && c <= 0x05FF) return "Hebrew";
        if (c >= 0x10A0 && c <= 0x10FF) return "Georgian";
        return "Other";
    }

    void Finish()
    {
        // The alphabet most unshifted letter keys type
        var counts = new Dictionary<string, int>();
        for (int s = 0; s < 256; s++)
        {
            string script = ScriptOf(chars[s, 0]);
            if (script == null) continue;
            int n; counts.TryGetValue(script, out n); counts[script] = n + 1;
        }
        int best = 0;
        foreach (var kv in counts) if (kv.Value > best) { best = kv.Value; Script = kv.Key; }
    }

    // The language model that fits a layout, from its language id and its alphabet
    static string LangFor(int langId, string script)
    {
        switch (langId & 0x3FF)
        {
            case 0x09: return script == "Latin" ? "en" : null;
            case 0x07: return script == "Latin" ? "de" : null;
            case 0x15: return script == "Latin" ? "pl" : null;
            case 0x0C: return script == "Latin" ? "fr" : null;
            case 0x0A: return script == "Latin" ? "es" : null;
            case 0x10: return script == "Latin" ? "it" : null;
            case 0x16: return script == "Latin" ? "pt" : null;
            case 0x05: return script == "Latin" ? "cs" : null;
            case 0x1F: return script == "Latin" ? "tr" : null;
            case 0x19: return script == "Cyrillic" ? "ru" : null;
            case 0x22: return script == "Cyrillic" ? "uk" : null;
            case 0x23: return script == "Cyrillic" ? "be" : null;
            case 0x02: return script == "Cyrillic" ? "bg" : null;
            case 0x2F: return script == "Cyrillic" ? "mk" : null;
            case 0x1A: return script == "Cyrillic" ? "sr-cyrl" : script == "Latin" ? "sr-latn" : null;   // Serbian, also Croatian and Bosnian Latin
            case 0x08: return script == "Greek" ? "el" : null;
            case 0x0D: return script == "Hebrew" ? "he" : null;
            case 0x37: return script == "Georgian" ? "ka" : null;
            default: return null;
        }
    }

    // ---- From an installed layout, through Windows ----

    [DllImport("user32.dll")] static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr hkl);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder buf, int size, uint flags, IntPtr hkl);
    [DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int count, IntPtr[] list);

    public static LayoutTable FromHkl(IntPtr hkl)
    {
        var t = new LayoutTable { Hkl = hkl };
        var state = new byte[256];
        var buf = new StringBuilder(8);
        for (int scan = 1; scan < 0x60; scan++)
        {
            uint vk = MapVirtualKeyEx((uint)scan, 3, hkl);   // MAPVK_VSC_TO_VK_EX
            if (vk == 0) continue;
            for (int s = 0; s < 8; s++)
            {
                Array.Clear(state, 0, 256);
                if ((s & 1) != 0) state[0x10] = state[0xA0] = 0x80;                        // Shift
                if ((s & 2) != 0) state[0x11] = state[0xA2] = state[0x12] = state[0xA5] = 0x80;   // AltGr
                if ((s & 4) != 0) state[0x14] = 0x01;                                      // CapsLock on
                buf.Clear();
                // Flag 4: don't touch the system keyboard state (dead keys stay intact)
                int n = ToUnicodeEx(vk, (uint)scan, state, buf, buf.Capacity, 4, hkl);
                if (n == 1) t.Set(scan, s, buf[0]);
                else if (n < 0 && buf.Length > 0) t.SetDead(scan, s, buf[0]);   // a dead key reports its accent
            }
        }
        t.Finish();
        int langId = (int)(hkl.ToInt64() & 0xFFFF);
        t.Lang = LangFor(langId, t.Script);
        try { t.Name = new CultureInfo(langId).Name; } catch { t.Name = langId.ToString("X4"); }
        return t;
    }

    // All layouts the user has installed in Windows
    public static List<LayoutTable> Installed()
    {
        var list = new List<LayoutTable>();
        foreach (var h in InstalledHkls())
        {
            try { list.Add(FromHkl(h)); } catch { }
        }
        return list;
    }

    public static IntPtr[] InstalledHkls()
    {
        int n = GetKeyboardLayoutList(0, null);
        var hkls = new IntPtr[n];
        GetKeyboardLayoutList(n, hkls);
        return hkls;
    }

    // ---- From a .klc file (Microsoft Keyboard Layout Creator format), for tests ----

    // lang overrides the language taken from the file (some published .klc files carry another locale)
    public static LayoutTable FromKlc(string path, string lang = null)
    {
        var t = new LayoutTable();
        string text = File.ReadAllText(path, Encoding.Unicode);
        var columns = new List<int>();   // KLC column order of shift states
        int langId = 0;
        string section = "";
        var capsFlags = new int[256];    // KLC "Cap" column: 1 — CapsLock is Shift, 4 — the same for the AltGr states
        var sgCaps = new bool[256];      // "SGCap": the next line holds what the key types with CapsLock on
        int sgScan = 0;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Split(new[] { "//" }, StringSplitOptions.None)[0].Trim();
            if (line.Length == 0 || line.StartsWith(";")) continue;
            var parts = Regex.Split(line, @"\s+");
            if (parts[0] == "LOCALEID") langId = Convert.ToInt32(parts[1].Trim('"'), 16) & 0xFFFF;
            if (parts[0] == "SHIFTSTATE" || parts[0] == "LAYOUT" || parts[0] == "DEADKEY" || parts[0] == "KEYNAME" ||
                parts[0] == "LIGATURE" || parts[0] == "DESCRIPTIONS" || parts[0] == "LANGUAGENAMES" || parts[0] == "ENDKBD")
            { section = parts[0]; continue; }
            if (section == "SHIFTSTATE")
            {
                int s; if (int.TryParse(parts[0], out s)) columns.Add(s);
                continue;
            }
            if (section != "LAYOUT" || parts.Length < 4) continue;
            int scan;
            // The line after a SGCap key ("-1 -1 0 ..."): its plain and Shift columns are the CapsLock states
            bool capsLine = parts[0] == "-1" && sgScan != 0;
            if (capsLine) { scan = sgScan; sgScan = 0; }
            else
            {
                if (!int.TryParse(parts[0], NumberStyles.HexNumber, null, out scan) || scan <= 0 || scan > 255) continue;
                sgCaps[scan] = parts[2] == "SGCap";
                sgScan = sgCaps[scan] ? scan : 0;
                if (!sgCaps[scan]) int.TryParse(parts[2], out capsFlags[scan]);
            }
            for (int i = 0; i < columns.Count && 3 + i < parts.Length; i++)
            {
                // KLC states: 0 none, 1 Shift, 2 Ctrl, 6 Ctrl+Alt (AltGr), 7 Shift+Ctrl+Alt
                int s = columns[i] == 0 ? 0 : columns[i] == 1 ? 1 : columns[i] == 6 ? 2 : columns[i] == 7 ? 3 : -1;
                if (s < 0 || (capsLine && s > 1)) continue;
                if (capsLine) s += 4;
                string v = parts[3 + i];
                if (v == "-1" || v == "%%") continue;
                bool isDead = v.EndsWith("@");
                v = v.TrimEnd('@');
                char c = v.Length == 1 ? v[0] : (char)Convert.ToInt32(v, 16);
                if (isDead) t.SetDead(scan, s, c); else t.Set(scan, s, c);
            }
        }
        // The CapsLock states of the other keys: Shift turned round where the key's flag says so, the same otherwise
        for (int scan = 1; scan < 256; scan++)
            for (int s = 4; s < 8; s++)
            {
                if (sgCaps[scan] && s < 6) continue;
                bool swap = (capsFlags[scan] & (s < 6 ? 1 : 4)) != 0;
                int from = (s - 4) ^ (swap ? 1 : 0);
                t.chars[scan, s] = t.chars[scan, from];
                t.dead[scan, s] = t.dead[scan, from];
            }
        t.Finish();
        t.Lang = lang ?? LangFor(langId, t.Script);
        t.Name = Path.GetFileNameWithoutExtension(path);
        return t;
    }
}

// The layouts installed in Windows, kept up to date: a layout added or removed in Windows settings is picked up
// within a few seconds, and the language models those layouts need are loaded in the background
public class LayoutSet
{
    volatile Dictionary<long, LayoutTable> tables = new Dictionary<long, LayoutTable>();
    string signature = "";
    readonly System.Threading.Timer timer;

    public LayoutSet()
    {
        Refresh();
        timer = new System.Threading.Timer(_ => { try { Refresh(); } catch { } }, null, 5000, 5000);
    }

    public LayoutTable Get(IntPtr hkl)
    {
        LayoutTable t;
        if (tables.TryGetValue(hkl.ToInt64(), out t)) return t;
        // One that couldn't be read isn't tried again on every keystroke, only after the layout list changes
        if (unreadable.ContainsKey(hkl.ToInt64())) return null;
        // A layout we haven't seen yet (just added): read it now, it takes a moment
        try
        {
            t = LayoutTable.FromHkl(hkl);
            var copy = new Dictionary<long, LayoutTable>(tables);
            copy[hkl.ToInt64()] = t;
            tables = copy;
            LoadModels(new[] { t });
        }
        catch { t = null; unreadable[hkl.ToInt64()] = 0; }
        return t;
    }

    readonly System.Collections.Concurrent.ConcurrentDictionary<long, byte> unreadable = new System.Collections.Concurrent.ConcurrentDictionary<long, byte>();

    public ICollection<LayoutTable> All { get { return tables.Values; } }

    void Refresh()
    {
        var hkls = LayoutTable.InstalledHkls();
        string sig = string.Join(",", Array.ConvertAll(hkls, h => h.ToInt64().ToString("X")));
        if (sig == signature) return;
        signature = sig;
        unreadable.Clear();
        var fresh = new Dictionary<long, LayoutTable>();
        foreach (var h in hkls)
        {
            try { fresh[h.ToInt64()] = LayoutTable.FromHkl(h); } catch { }
        }
        tables = fresh;
        LoadModels(fresh.Values);
    }

    static void LoadModels(IEnumerable<LayoutTable> layouts)
    {
        var langs = new List<string>();
        foreach (var t in layouts)
        {
            if (t.Lang == null || langs.Contains(t.Lang)) continue;
            langs.Add(t.Lang);
            // Serbian Cyrillic needs the Latin model too: a valid Serbian Latin word is never "fixed"
            if (t.Lang == "sr-cyrl" && !langs.Contains("sr-latn")) langs.Add("sr-latn");
        }
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            foreach (string l in langs) { try { Detector.Load(l); } catch { } }
            Detector.ReturnMemory();
            ModelsLoaded = DateTime.Now;
            ModelsLoadedLangs = string.Join(" ", langs);
        });
    }

    // When the language models of the installed layouts were last ready, for the diagnostics log
    public static DateTime ModelsLoaded;
    public static string ModelsLoadedLangs = "";
}
