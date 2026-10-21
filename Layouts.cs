using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

// What every physical key types in one keyboard layout. Keys are scan codes, not virtual-key codes:
// the same physical key has different virtual-key codes in, say, German QWERTZ and US QWERTY.
public class LayoutTable
{
    // Shift states: 0 — none, 1 — Shift, 2 — AltGr (Ctrl+Alt), 3 — Shift+AltGr
    readonly char[,] chars = new char[256, 4];
    readonly bool[] capsLetter = new bool[256];          // CapsLock acts like Shift on this key
    readonly Dictionary<char, int> reverse = new Dictionary<char, int>();   // char → scan * 4 + state

    public IntPtr Hkl;
    public string Lang;      // language model code: en, ru, uk, be, de, pl, sr-latn, sr-cyrl; null if unsupported
    public bool Cyrillic;    // the letters of this layout are Cyrillic
    public string Name;

    public char CharFor(int scan, bool shift, bool caps, bool altGr)
    {
        if (scan <= 0 || scan > 255) return '\0';
        bool upper = shift ^ (caps && capsLetter[scan]);
        return chars[scan, (upper ? 1 : 0) + (altGr ? 2 : 0)];
    }

    // The key (and shift state) that types this character, for converting selected text
    public bool Find(char c, out int scan, out bool shift, out bool altGr)
    {
        int code;
        scan = 0; shift = altGr = false;
        if (!reverse.TryGetValue(c, out code)) return false;
        scan = code / 4; shift = (code & 1) != 0; altGr = (code & 2) != 0;
        return true;
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
        int code = scan * 4 + state;
        // Prefer the simplest way to type a character (no Shift, no AltGr)
        if (!reverse.ContainsKey(c) || code % 4 < reverse[c] % 4) reverse[c] = code;
    }

    void Finish()
    {
        int cyr = 0, lat = 0;
        for (int s = 0; s < 256; s++)
        {
            char c = chars[s, 0];
            if (c >= 'а' && c <= 'я' || c == 'і' || c == 'ў' || c == 'ј' || c == 'љ' || c == 'њ') cyr++;
            else if (c >= 'a' && c <= 'z') lat++;
        }
        Cyrillic = cyr > lat;
    }

    // The language model that fits a layout, from its language id and its alphabet
    static string LangFor(int langId, bool cyrillic)
    {
        switch (langId & 0x3FF)
        {
            case 0x09: return cyrillic ? null : "en";
            case 0x19: return cyrillic ? "ru" : null;
            case 0x22: return cyrillic ? "uk" : null;
            case 0x23: return cyrillic ? "be" : null;
            case 0x07: return cyrillic ? null : "de";
            case 0x15: return cyrillic ? null : "pl";
            case 0x1A: return cyrillic ? "sr-cyrl" : "sr-latn";   // Serbian, also Croatian and Bosnian Latin
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
            for (int s = 0; s < 4; s++)
            {
                Array.Clear(state, 0, 256);
                if ((s & 1) != 0) state[0x10] = state[0xA0] = 0x80;                        // Shift
                if ((s & 2) != 0) state[0x11] = state[0xA2] = state[0x12] = state[0xA5] = 0x80;   // AltGr
                buf.Clear();
                // Flag 4: don't touch the system keyboard state (dead keys stay intact)
                int n = ToUnicodeEx(vk, (uint)scan, state, buf, buf.Capacity, 4, hkl);
                if (n == 1) t.Set(scan, s, buf[0]);
            }
            // Does CapsLock act like Shift on this key? Compare unshifted output with CapsLock on
            Array.Clear(state, 0, 256);
            state[0x14] = 0x01;
            buf.Clear();
            if (ToUnicodeEx(vk, (uint)scan, state, buf, buf.Capacity, 4, hkl) == 1)
                t.capsLetter[scan] = buf[0] != t.chars[scan, 0] && buf[0] == t.chars[scan, 1];
        }
        t.Finish();
        int langId = (int)(hkl.ToInt64() & 0xFFFF);
        t.Lang = LangFor(langId, t.Cyrillic);
        try { t.Name = new CultureInfo(langId).Name; } catch { t.Name = langId.ToString("X4"); }
        return t;
    }

    // All layouts the user has installed in Windows
    public static List<LayoutTable> Installed()
    {
        var list = new List<LayoutTable>();
        int n = GetKeyboardLayoutList(0, null);
        var hkls = new IntPtr[n];
        GetKeyboardLayoutList(n, hkls);
        foreach (var h in hkls)
        {
            try { list.Add(FromHkl(h)); } catch { }
        }
        return list;
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
            if (!int.TryParse(parts[0], NumberStyles.HexNumber, null, out scan)) continue;
            t.capsLetter[scan] = parts[2] == "1" || parts[2] == "SGCap";
            for (int i = 0; i < columns.Count && 3 + i < parts.Length; i++)
            {
                // KLC states: 0 none, 1 Shift, 2 Ctrl, 6 Ctrl+Alt (AltGr), 7 Shift+Ctrl+Alt
                int s = columns[i] == 0 ? 0 : columns[i] == 1 ? 1 : columns[i] == 6 ? 2 : columns[i] == 7 ? 3 : -1;
                if (s < 0) continue;
                string v = parts[3 + i];
                if (v == "-1" || v.EndsWith("@") || v == "%%") continue;
                char c = v.Length == 1 ? v[0] : (char)Convert.ToInt32(v, 16);
                t.Set(scan, s, c);
            }
        }
        t.Finish();
        t.Lang = lang ?? LangFor(langId, t.Cyrillic);
        t.Name = Path.GetFileNameWithoutExtension(path);
        return t;
    }
}
