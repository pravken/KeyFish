using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

// Stress test for "KeyFish sometimes doesn't fire": types one long fixed script under different conditions (speed,
// jitter, key rollover, a busy window, CPU load, other apps) so the results can be compared with a calm run and
// every miss matched with KeyFish's debug-log.txt.
//   StressTyper.exe type <script file> <out file> [delay=60] [jitter=0] [hold=8] [rollover=1] [seed=1]
//                        [lag=<busy>/<period>] [target=self|notepad|edge] [url=<file url>] [wordpause=0]
//   StressTyper.exe burn <seconds> <threads>
//   StressTyper.exe report <ideal file> <baseline out> <debug-log> <report file> <name>=<out file> ...
// Word numbers are 1-based everywhere: the script's words, the lines of <out file>.times and #N in the report.
static class StressTyper
{
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    // The two pads make the size of the real union (which also holds MOUSEINPUT): 40 bytes on x64, 28 on x86
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public KEYBDINPUT ki; public uint pad1, pad2; }

    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll", EntryPoint = "VkKeyScanExW", CharSet = CharSet.Unicode)] static extern short VkKeyScanEx(char c, IntPtr hkl);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyEx(uint code, uint type, IntPtr hkl);
    [DllImport("user32.dll")] static extern IntPtr LoadKeyboardLayout(string klid, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

    const ushort Shift = 0x10, Ctrl = 0x11, Alt = 0x12;
    static readonly Encoding Utf8 = new UTF8Encoding(false);
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int n, IntPtr[] list);
    [DllImport("user32.dll")] static extern bool UnloadKeyboardLayout(IntPtr hkl);

    // The session's layout for a KLID ("00020422"): one already loaded with that language and, for a variant such as
    // 00020422, that device id; only failing that, LoadKeyboardLayout. A layout installed a moment ago wasn't always
    // handed back right by LoadKeyboardLayout
    static IntPtr FindLayout(string klid)
    {
        int id = int.Parse(klid, System.Globalization.NumberStyles.HexNumber);
        int lang = id & 0xFFFF;
        IntPtr any = IntPtr.Zero;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            foreach (IntPtr h in LoadedLayouts())
            {
                long v = h.ToInt64();
                if ((v & 0xFFFF) != lang) continue;
                if ((id >> 16) == 0 && ((v >> 16) & 0xFFFF) == lang) return h;   // the plain layout of the language
                if ((id >> 16) != 0 && ((v >> 28) & 0xF) == 0xF) return h;     // a variant layout of it
                any = h;
            }
            if (any != IntPtr.Zero) return any;
            IntPtr loaded = LoadKeyboardLayout(klid, 0);
            if (loaded != IntPtr.Zero && (loaded.ToInt64() & 0xFFFF) == lang) return loaded;
            Thread.Sleep(250);
        }
        return LoadKeyboardLayout(klid, 0);
    }

    static List<IntPtr> LoadedLayouts()
    {
        var a = new IntPtr[64];
        int n = GetKeyboardLayoutList(a.Length, a);
        var r = new List<IntPtr>();
        for (int i = 0; i < n; i++) r.Add(a[i]);
        return r;
    }

    [STAThread]
    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "";
        if (mode == "type" && args.Length >= 3)
        {
            // LoadKeyboardLayout leaves a layout in the session until sign-out: tests\stress-langs.ps1 unloads the ones
            // that aren't in the user's language list when it is through (not here: the language it installed for the
            // run is needed by the next run too)
            TypeMode(args);
            return 0;
        }
        if (mode == "burn" && args.Length >= 3) { Burn(double.Parse(args[1], Inv), int.Parse(args[2])); return 0; }
        if (mode == "report" && args.Length >= 5)
        {
            // The calling script reads the report, so a failure goes into it too
            try { Report(args); } catch (Exception ex) { File.WriteAllText(args[4], "REPORT FAILED: " + ex, Encoding.UTF8); }
            return 0;
        }
        return 2;
    }

    // ------------------------------------------------------------------ type

    // Scan: the physical key (0 for the modifiers, sent by their virtual key)
    class Ev { public double T; public ushort Vk, Scan; public bool Up, First, Space; public int Word, Seq; }
    static string startLayout = "00000409";   // layout= : the layout the window starts in
    const ushort LCtrl = 0xA2, RAlt = 0xA5;   // AltGr: Windows takes Ctrl+Alt for it

    class Run
    {
        public string OutPath, Options, Skipped = "";
        public List<Ev> Events;
        public int Words;
        public DateTime[] First, Last;   // per word, [0] unused
        public bool[] Spaced;
    }

    static readonly HashSet<ushort> held = new HashSet<ushort>();   // keys this tool holds down right now

    // Physical keys (VK + scan code, no KEYEVENTF_UNICODE): the window's layout decides the character, as with a
    // real keyboard, so after KeyFish switches to Russian the same keys give Cyrillic
    static void Send(ushort vk, bool up)
    {
        var i = new INPUT[1];
        i[0].type = 1;
        i[0].ki.wVk = vk;
        i[0].ki.wScan = (ushort)MapVirtualKey(vk, 0);
        i[0].ki.dwFlags = (up ? 2u : 0u) | (vk == RAlt ? 1u : 0u);   // right Alt is an extended key
        SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
        if (up) held.Remove(vk); else held.Add(vk);
    }

    // A key sent as a real keyboard sends it: by its scan code alone (KEYEVENTF_SCANCODE), so Windows decides the
    // virtual key and the character from the window's layout. The same physical key is ö on a German layout and ; on an
    // English one, with different virtual keys
    static readonly Dictionary<ushort, ushort> heldScan = new Dictionary<ushort, ushort>();
    static void SendScan(ushort vk, ushort scan, bool up)
    {
        var i = new INPUT[1];
        i[0].type = 1;
        i[0].ki.wScan = scan;
        i[0].ki.dwFlags = 8u | (up ? 2u : 0u);
        SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
        if (up) { held.Remove(vk); heldScan.Remove(vk); } else { held.Add(vk); heldScan[vk] = scan; }
    }

    static void ReleaseAll()
    {
        foreach (ushort vk in new List<ushort>(held))
        {
            ushort scan;
            if (heldScan.TryGetValue(vk, out scan)) SendScan(vk, scan, true); else Send(vk, true);
        }
    }

    // Never type into someone else's window. A click elsewhere may take the focus for a moment and KeyFish gives it
    // back: wait two seconds for that before asking for it
    static bool EnsureFocus(IntPtr w)
    {
        if (GetForegroundWindow() == w) return true;
        // Nothing stays pressed in the other window; and Shift held together with the tap of Alt below would be the
        // system's layout hotkey
        bool shift = held.Contains(Shift);
        ReleaseAll();
        for (int i = 0; i < 100 && GetForegroundWindow() != w; i++) Thread.Sleep(20);
        if (GetForegroundWindow() != w) { SetForegroundWindow(w); Thread.Sleep(50); }
        // Windows may refuse the focus to a window that wasn't just clicked; a tap of Alt is the usual way through
        if (GetForegroundWindow() != w) { Send(Alt, false); Send(Alt, true); SetForegroundWindow(w); Thread.Sleep(50); }
        if (GetForegroundWindow() != w) return false;
        if (shift) Send(Shift, false);   // the capital that was under way stays a capital
        return true;
    }

    static bool Chord(IntPtr w, ushort key)   // Ctrl+key, only into the target
    {
        if (GetForegroundWindow() != w) return false;
        Send(Ctrl, false); Send(key, false); Send(key, true); Send(Ctrl, true);
        return true;
    }

    static void TypeMode(string[] args)
    {
        string outPath = args[2];
        try
        {
            string script = File.ReadAllText(args[1], Encoding.UTF8).Trim('\r', '\n', '﻿');
            double delay = 60, jitter = 0, hold = -1, wordPause = 0;
            int seed = 1, lagBusy = 0, lagPeriod = 0;
            bool rollover = false;
            string target = "self", url = "";
            for (int i = 3; i < args.Length; i++)
            {
                int eq = args[i].IndexOf('=');
                if (eq < 0) continue;
                string k = args[i].Substring(0, eq).ToLowerInvariant(), v = args[i].Substring(eq + 1);
                if (k == "delay") delay = double.Parse(v, Inv);
                else if (k == "jitter") jitter = double.Parse(v, Inv);
                else if (k == "hold") hold = double.Parse(v, Inv);
                else if (k == "wordpause") wordPause = double.Parse(v, Inv);
                else if (k == "seed") seed = int.Parse(v);
                else if (k == "rollover") rollover = v == "1";
                else if (k == "target") target = v.ToLowerInvariant();
                else if (k == "url") url = v;
                else if (k == "layout") startLayout = v;
                else if (k == "lag") { string[] p = v.Split('/'); lagBusy = int.Parse(p[0]); lagPeriod = int.Parse(p[1]); }
            }
            // Rollover: the key is still down when the next one goes down, as with fast typists
            if (hold < 0) hold = rollover ? 1.6 * delay : 8;
            if (hold < 1) hold = 1;

            var run = new Run { OutPath = outPath };
            run.Events = Build(script, delay, jitter, hold, wordPause, seed, run);
            run.First = new DateTime[run.Words + 1];
            run.Last = new DateTime[run.Words + 1];
            run.Spaced = new bool[run.Words + 1];
            run.Options = "delay=" + delay.ToString(Inv) + " jitter=" + jitter.ToString(Inv) + " hold=" + hold.ToString(Inv) +
                " rollover=" + (rollover ? "1" : "0") + " seed=" + seed + " lag=" + (lagBusy > 0 ? lagBusy + "/" + lagPeriod : "none") +
                " target=" + target + " wordpause=" + wordPause.ToString(Inv) + (url != "" ? " url=" + url : "") +
                " words=" + run.Words + (run.Skipped != "" ? " skipped-chars=" + run.Skipped : "");

            if (target == "self") RunSelf(run, lagBusy, lagPeriod); else RunExternal(run, target, url);
        }
        catch (Exception ex) { ReleaseAll(); File.WriteAllText(outPath, "ERROR " + ex.Message, Utf8); }
    }

    // The whole timeline is laid out first, so that the same seed gives the same rhythm in every run
    static List<Ev> Build(string script, double delay, double jitter, double hold, double wordPause, int seed, Run run)
    {
        var keys = new List<int[]>();   // vk, shift, is space, AltGr
        // A plain script is typed with the keys of the US layout. "#v2" scripts have lines "<KLID><TAB><text>": each
        // text with the keys it takes in that layout (German words with QWERTZ keys, Greek with the Greek ones...),
        // whatever layout the window has on. In them "\r" stands for Enter
        var parts = new List<KeyValuePair<string, string>>();
        if (script.StartsWith("#v2"))
            foreach (string line in script.Split('\n'))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0) parts.Add(new KeyValuePair<string, string>(line.Substring(0, tab), line.Substring(tab + 1).TrimEnd('\r').Replace("\r", "\r")));
            }
        else parts.Add(new KeyValuePair<string, string>("00000409", script));
        // Each layout loaded once: loading the same one again for every word made Windows hand back another one at times
        var hkls = new Dictionary<string, IntPtr>();
        foreach (var part in parts)
        {
            IntPtr hkl;
            if (!hkls.TryGetValue(part.Key, out hkl)) hkls[part.Key] = hkl = FindLayout(part.Key);
            foreach (char c in part.Value)
            {
                // Enter, space and Backspace are the same keys everywhere
                if (c == '\r' || c == ' ' || c == '\b') { keys.Add(new[] { c == '\r' ? 0x0D : c == ' ' ? 0x20 : 0x08, 0, c == '\b' ? 0 : 1, 0, c == '\r' ? 0x1C : c == ' ' ? 0x39 : 0x0E }); continue; }
                if (AddChar(keys, c, hkl)) continue;
                // An accented letter the layout has no key for: its dead key, then the letter (á = ´ + a)
                string d = c.ToString().Normalize(NormalizationForm.FormD);
                int before = keys.Count;
                bool typedIt = false;
                // The accent's dead key: the Latin one (´) or the Greek one (΄ tonos, ΅ dialytika with tonos)
                if (d.Length == 2)
                    foreach (char dead in new[] { DeadFor(d[1]), d[1] == (char)0x301 ? (char)0x384 : d[1] == (char)0x308 ? (char)0xA8 : '\0' })
                    {
                        if (dead == '\0') continue;
                        if (AddChar(keys, dead, hkl) && AddChar(keys, d[0], hkl)) { typedIt = true; break; }
                        keys.RemoveRange(before, keys.Count - before);
                    }
                if (typedIt) continue;
                keys.RemoveRange(before, keys.Count - before);
                run.Skipped += c;   // said in the .times file
            }
        }
        var rnd = new Random(seed);
        var down = new double[keys.Count];
        for (int i = 0; i < down.Length; i++)
            // At least 5 ms between presses keeps the order Shift-down, key-down, Shift-up, next key-down possible
            down[i] = i == 0 ? 10 : down[i - 1] + Math.Max(5, delay + (rnd.NextDouble() * 2 - 1) * jitter) + (keys[i - 1][2] == 1 ? wordPause : 0);

        var evs = new List<Ev>();
        var lastUp = new Dictionary<ushort, Ev>();
        int word = 0;
        bool inWord = false, shiftDown = false, altGrDown = false;
        for (int i = 0; i < keys.Count; i++)
        {
            ushort vk = (ushort)keys[i][0];
            bool sh = keys[i][1] == 1, space = keys[i][2] == 1, last = i + 1 == keys.Count, ag = keys[i][3] == 1;
            double dn = down[i], up = dn + hold;
            bool first = false, term = false;
            if (space) { term = inWord; inWord = false; }
            else if (!inWord) { inWord = true; first = true; word++; }

            if (sh && !shiftDown) { evs.Add(new Ev { T = dn - 2, Vk = Shift, Word = word, Seq = evs.Count }); shiftDown = true; }
            if (ag && !altGrDown)
            {
                evs.Add(new Ev { T = dn - 2, Vk = LCtrl, Word = word, Seq = evs.Count });
                evs.Add(new Ev { T = dn - 2, Vk = RAlt, Word = word, Seq = evs.Count });
                altGrDown = true;
            }
            // A physical key can't go down twice: with rollover a doubled letter lets go just before its second press
            Ev prev;
            if (lastUp.TryGetValue(vk, out prev) && prev.T > dn - 2) prev.T = dn - 2;
            ushort scan = (ushort)keys[i][4];
            evs.Add(new Ev { T = dn, Vk = vk, Scan = scan, Word = word, First = first, Space = term, Seq = evs.Count });
            var u = new Ev { T = up, Vk = vk, Scan = scan, Up = true, Word = word, Seq = evs.Count };
            evs.Add(u);
            lastUp[vk] = u;
            // Shift goes up before the next letter goes down, or rollover would give two capitals; it stays down
            // through a run of capitals, as a typist holds it
            if (shiftDown && (last || keys[i + 1][1] == 0))
            {
                evs.Add(new Ev { T = last ? up : Math.Min(up, down[i + 1] - 3), Vk = Shift, Up = true, Word = word, Seq = evs.Count });
                shiftDown = false;
            }
            if (altGrDown && (last || keys[i + 1][3] == 0))
            {
                double at = last ? up : Math.Min(up, down[i + 1] - 3);
                evs.Add(new Ev { T = at, Vk = RAlt, Up = true, Word = word, Seq = evs.Count });
                evs.Add(new Ev { T = at, Vk = LCtrl, Up = true, Word = word, Seq = evs.Count });
                altGrDown = false;
            }
        }
        run.Words = word;
        evs.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) : a.Seq.CompareTo(b.Seq));
        return evs;
    }

    // The key for a character in a layout, with Shift and AltGr; false if it has none, or it needs Ctrl or Alt alone
    static bool AddChar(List<int[]> keys, char c, IntPtr hkl)
    {
        short r = VkKeyScanEx(c, hkl);
        if (r == -1) return false;
        int mods = (r >> 8) & 7;
        if (mods != 0 && mods != 1 && mods != 6 && mods != 7) return false;
        keys.Add(new[] { r & 0xFF, mods & 1, 0, (mods & 6) == 6 ? 1 : 0, (int)MapVirtualKeyEx((uint)(r & 0xFF), 0, hkl) });
        return true;
    }

    // The spacing character of the dead key that puts this combining mark on a letter
    static char DeadFor(char mark)
    {
        switch (mark)
        {
            case '́': return '´';   // acute
            case '̀': return '`';
            case '̂': return '^';
            case '̃': return '~';
            case '̈': return '¨';   // diaeresis
            case '̌': return 'ˇ';   // caron
            case '̧': return '¸';   // cedilla
            case '̊': return '˚';   // ring
            case '̨': return '˛';   // ogonek
            case '̇': return '˙';   // dot above
            case '̋': return '˝';   // double acute
            case '̆': return '˘';   // breve
        }
        return '\0';
    }

    // Returns -1 when everything was typed, else the number of the last word that got a key before the focus was lost.
    // Runs off the UI thread, so a busy window (lag=) doesn't slow the typing down.
    static int Play(Run run, IntPtr w)
    {
        timeBeginPeriod(1);
        var sw = Stopwatch.StartNew();
        double late = 0;
        int word = 0;
        try
        {
            foreach (Ev ev in run.Events)
            {
                // Sleep for the bulk, spin for the last millisecond: Sleep alone is ±1 ms at best
                for (;;)
                {
                    double rem = ev.T + late - sw.Elapsed.TotalMilliseconds;
                    if (rem <= 0) break;
                    if (rem > 2) Thread.Sleep((int)(rem - 1.5)); else Thread.SpinWait(200);
                }
                double before = sw.Elapsed.TotalMilliseconds;
                if (!EnsureFocus(w)) return word;
                double lost = sw.Elapsed.TotalMilliseconds - before;
                if (lost > 5) late += lost;   // the rhythm resumes where it stopped instead of a burst to catch up
                if (ev.Up && !held.Contains(ev.Vk)) continue;   // already released when the focus went away
                if (ev.Scan != 0) SendScan(ev.Vk, ev.Scan, ev.Up); else Send(ev.Vk, ev.Up);
                if (ev.Up || ev.Vk == Shift || ev.Vk == LCtrl || ev.Vk == RAlt || ev.Word == 0) continue;
                DateTime now = DateTime.Now;
                word = ev.Word;
                if (ev.First) run.First[word] = now;
                if (!run.Spaced[word]) { run.Last[word] = now; run.Spaced[word] = ev.Space; }
            }
            return -1;
        }
        finally { ReleaseAll(); timeEndPeriod(1); }
    }

    static void Finish(Run run, int fail, string text)
    {
        File.WriteAllText(run.OutPath, fail >= 0 ? "NOFOCUS after word " + fail : text, Utf8);
        var sb = new StringBuilder("# options: " + run.Options + "\r\n");
        for (int i = 1; i <= run.Words; i++)
            if (run.First[i] != DateTime.MinValue)
                sb.Append(i).Append('\t').Append(run.First[i].ToString("HH:mm:ss.fff", Inv)).Append('\t').Append(run.Last[i].ToString("HH:mm:ss.fff", Inv)).Append("\r\n");
        File.WriteAllText(run.OutPath + ".times", sb.ToString(), Utf8);
    }

    static void RunSelf(Run run, int lagBusy, int lagPeriod)
    {
        var f = new Form { Width = 900, Height = 500, TopMost = true, Text = "KeyFish stress" };
        var tb = new TextBox { Multiline = true, Dock = DockStyle.Fill, WordWrap = true, Font = new System.Drawing.Font("Segoe UI", 12) };
        f.Controls.Add(tb);
        f.Shown += (o, e) =>
        {
            ActivateKeyboardLayout(LoadKeyboardLayout(startLayout, 0), 0);
            tb.Focus();
            if (lagBusy > 0 && lagPeriod > 0)
            {
                // A busy app: its UI thread doesn't pump messages for a while, again and again
                var t = new System.Windows.Forms.Timer { Interval = lagPeriod };
                t.Tick += (o2, e2) => Thread.Sleep(lagBusy);
                t.Start();
            }
            IntPtr h = f.Handle;
            var th = new Thread(() =>
            {
                try
                {
                    Thread.Sleep(1500);
                    int fail = Play(run, h);
                    if (fail < 0) Thread.Sleep(2500);   // KeyFish finishes its last fix
                    string text = "";
                    f.Invoke((Action)(() => { text = tb.Text; }));
                    Finish(run, fail, text);
                    f.Invoke((Action)(() => f.Close()));
                }
                catch (Exception ex) { try { File.WriteAllText(run.OutPath, "ERROR " + ex.Message, Utf8); } catch { } Environment.Exit(0); }
            });
            th.IsBackground = true;
            th.Start();
        };
        Application.Run(f);
    }

    static string Title(IntPtr h) { var sb = new StringBuilder(4096); GetWindowText(h, sb, 4096); return sb.ToString(); }

    static IntPtr WaitWindow(string marker)
    {
        IntPtr found = IntPtr.Zero;
        for (int i = 0; i < 80 && found == IntPtr.Zero; i++)
        {
            Thread.Sleep(250);
            EnumWindows((h, l) => { if (IsWindowVisible(h) && Title(h).Contains(marker)) { found = h; return false; } return true; }, IntPtr.Zero);
        }
        return found;
    }

    // "KFTX:text - [InPrivate] - Microsoft Edge" -> "text"
    static string CleanTitle(string t)
    {
        for (;;)
        {
            int i = t.LastIndexOf(" - ", StringComparison.Ordinal);
            if (i < 0) break;
            string tail = t.Substring(i + 3);
            if (!tail.Contains("InPrivate") && !tail.Contains("Microsoft")) break;
            t = t.Substring(0, i);
        }
        return t.StartsWith("KFTX:") ? t.Substring(5) : t.StartsWith("KFTX") ? t.Substring(4) : t;
    }

    static void RunExternal(Run run, string target, string url)
    {
        bool notepad = target == "notepad";
        string tmp = null;
        IntPtr w;
        if (notepad)
        {
            tmp = Path.Combine(Path.GetTempPath(), "kf-stress-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            File.WriteAllBytes(tmp, new byte[0]);
            Process.Start("notepad.exe", "\"" + tmp + "\"");
            w = WaitWindow(Path.GetFileName(tmp));
        }
        else if (target == "edge" && url != "")
        {
            Process.Start("msedge.exe", "--inprivate --new-window \"" + url + "\"");
            w = WaitWindow("KFTX");
        }
        else throw new ArgumentException("target must be self, notepad or edge (edge needs url=)");
        if (w == IntPtr.Zero) { File.WriteAllText(run.OutPath, "NOWINDOW", Utf8); return; }

        SetForegroundWindow(w);
        int fail = 0;
        string text = "";
        if (EnsureFocus(w))
        {
            // Always start in the same layout (English unless layout= says otherwise): a previous run may have left another on
            PostMessage(w, 0x0050, IntPtr.Zero, LoadKeyboardLayout(startLayout, 0));
            Thread.Sleep(1500);
            fail = Play(run, w);
        }
        if (fail < 0)
        {
            Thread.Sleep(2500);
            if (!notepad) text = CleanTitle(Title(w));
            else if (Chord(w, (ushort)'S')) { Thread.Sleep(1000); text = File.ReadAllText(tmp, Encoding.UTF8); }
            else fail = run.Words;   // the text is there but can't be saved without typing into another window
        }
        Finish(run, fail, text);
        Chord(w, (ushort)'W');   // close the tab; skipped when the window isn't in front
        if (notepad) { Thread.Sleep(500); try { File.Delete(tmp); } catch { } }
    }

    // ------------------------------------------------------------------ burn

    static void Burn(double seconds, int threads)
    {
        var sw = Stopwatch.StartNew();
        var list = new List<Thread>();
        for (int i = 0; i < threads; i++)
        {
            var t = new Thread(() =>
            {
                double x = 1;
                while (sw.Elapsed.TotalSeconds < seconds)
                    for (int k = 1; k < 20000; k++) x = Math.Sqrt(x * k + 1) + Math.Sin(x);
                GC.KeepAlive(x);
            });
            t.IsBackground = true;   // normal priority: the load competes with KeyFish as any busy program would
            t.Start();
            list.Add(t);
        }
        foreach (Thread t in list) t.Join();
    }

    // ------------------------------------------------------------------ report

    class Pair { public int R, G; public string Exp, Got; }   // R: index in the reference (for an insertion: the word before it); G: index in the run or -1
    class LogLine { public double T; public string Text; }

    static readonly Regex EventRx = new Regex(@"hooks reinstalled|slow hook|late hook|correction stuck|fix not typed|error in|word dropped|perf:");

    static string[] Tokens(string s) { return (s ?? "").Split(new[] { ' ', '\t', '\r', '\n', '﻿' }, StringSplitOptions.RemoveEmptyEntries); }

    static string ReadOrNull(string path) { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null; }

    static bool Ms(string s, out double ms)
    {
        TimeSpan ts;
        bool ok = TimeSpan.TryParseExact(s, @"hh\:mm\:ss\.fff", Inv, out ts);
        ms = ts.TotalMilliseconds;
        return ok;
    }

    // Word-level Levenshtein with the path: every reference word comes out once, as a match, a substitution or a deletion
    static List<Pair> Align(string[] r, string[] g)
    {
        int n = r.Length, m = g.Length;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
                d[i, j] = Math.Min(d[i - 1, j - 1] + (r[i - 1] == g[j - 1] ? 0 : 1), Math.Min(d[i - 1, j], d[i, j - 1]) + 1);
        var res = new List<Pair>();
        for (int i = n, j = m; i > 0 || j > 0;)
        {
            if (i > 0 && j > 0 && d[i, j] == d[i - 1, j - 1] + (r[i - 1] == g[j - 1] ? 0 : 1)) { i--; j--; res.Add(new Pair { R = i, G = j, Exp = r[i], Got = g[j] }); }
            else if (i > 0 && d[i, j] == d[i - 1, j] + 1) { i--; res.Add(new Pair { R = i, G = -1, Exp = r[i] }); }
            else { j--; res.Add(new Pair { R = Math.Max(0, i - 1), G = j, Got = g[j] }); }
        }
        res.Reverse();
        return res;
    }

    static List<LogLine> ParseLog(string path)
    {
        var res = new List<LogLine>();
        if (!File.Exists(path)) return res;
        // KeyFish may be writing to it right now
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var sr = new StreamReader(fs, Encoding.UTF8))
        {
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                double ms;
                if (line.Length < 13 || !Ms(line.Substring(0, 12), out ms)) continue;
                res.Add(new LogLine { T = ms, Text = line.Substring(12).Trim() });
            }
        }
        return res;
    }

    // word number -> { first key-down, terminating space } in ms of the day
    static Dictionary<int, double[]> Times(string path)
    {
        var res = new Dictionary<int, double[]>();
        if (!File.Exists(path)) return res;
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            string[] p = line.Split('\t');
            int idx; double a, b;
            if (p.Length >= 3 && int.TryParse(p[0], out idx) && Ms(p[1], out a) && Ms(p[2], out b)) res[idx] = new[] { a, b };
        }
        return res;
    }

    // What KeyFish said about a word: its line is written when the word ends, a late or retried fix up to 1.5 s after.
    // The window also catches the next few words' lines, so each one carries its distance from this word's space.
    static string LogFor(List<LogLine> log, Dictionary<int, double[]> times, int word, double pre)
    {
        double[] w;
        if (!times.TryGetValue(word, out w)) return "no timeline for this word";
        var sb = new StringBuilder();
        foreach (LogLine l in log)
        {
            if (l.T < w[0] - pre || l.T > w[1] + 1500 || l.Text.StartsWith("perf:")) continue;
            string t = Regex.Replace(Regex.Replace(l.Text, @"\s*\(\d+ keys?,.*$", ""), @"\s{2,}", " / ");
            if (sb.Length > 0) sb.Append(" ; ");
            sb.Append('[').Append((l.T - w[1]).ToString("+0;-0", Inv)).Append(" ms] ").Append(t);
        }
        return sb.Length > 0 ? sb.ToString() : "no log line";
    }

    static void Events(StringBuilder sb, string name, List<LogLine> log, Dictionary<int, double[]> times)
    {
        sb.Append(name).Append(": ");
        if (times.Count == 0) { sb.AppendLine("no timeline"); return; }
        double from = double.MaxValue, to = double.MinValue;
        foreach (double[] w in times.Values) { from = Math.Min(from, w[0]); to = Math.Max(to, w[1]); }
        var counts = new SortedDictionary<string, int>();
        var perf = new List<string>();
        foreach (LogLine l in log)
        {
            if (l.T < from || l.T > to + 3000) continue;
            Match m = EventRx.Match(l.Text);
            if (!m.Success) continue;
            string kind = m.Value.TrimEnd(':');
            int c;
            counts.TryGetValue(kind, out c);
            counts[kind] = c + 1;
            if (kind == "perf") perf.Add(l.Text);
        }
        if (counts.Count == 0) { sb.AppendLine("none"); return; }
        var parts = new List<string>();
        foreach (var kv in counts) parts.Add(kv.Key + " x" + kv.Value);
        sb.AppendLine(string.Join(", ", parts.ToArray()));
        foreach (string p in perf) sb.Append("    ").AppendLine(p);
    }

    static string Show(string s) { return s ?? "(nothing)"; }

    static void Report(string[] args)
    {
        string[] ideal = Tokens(ReadOrNull(args[1]));
        string baseRaw = ReadOrNull(args[2]);
        string[] baseTok = Tokens(baseRaw);
        List<LogLine> log = ParseLog(args[3]);
        Dictionary<int, double[]> baseTimes = Times(args[2] + ".times");
        var sb = new StringBuilder();
        var summary = new StringBuilder();
        var events = new StringBuilder();

        sb.AppendLine("KeyFish stress report  (#N = number of the word in the script, from 1; [+N ms] = after that word's space)");
        sb.AppendLine("debug-log lines read: " + log.Count + (File.Exists(args[3]) ? "" : "  (FILE MISSING)"));
        sb.AppendLine();
        sb.AppendLine("== Detector (baseline vs intended) ==");
        if (baseRaw == null) sb.AppendLine("baseline out file is missing: " + args[2]);
        else if (baseRaw.StartsWith("NOFOCUS") || baseRaw.StartsWith("NOWINDOW") || baseRaw.StartsWith("ERROR")) sb.AppendLine("baseline run failed: " + baseRaw.Trim());
        List<Pair> ab = Align(ideal, baseTok);
        // baseline word -> word of the script: the timelines are numbered by the script
        var toScript = new int[baseTok.Length];
        int wrong = 0;
        foreach (Pair p in ab) { if (p.G >= 0) toScript[p.G] = p.R; if (p.Exp != p.Got) wrong++; }
        sb.AppendLine(wrong + " of " + ideal.Length + " intended words came out differently in the baseline");
        foreach (Pair p in ab)
            if (p.Exp != p.Got)
                sb.AppendLine("#" + (p.R + 1) + "  " + Show(p.Exp) + " → " + Show(p.Got) + "  | log: " + LogFor(log, baseTimes, p.R + 1, 0));
        Events(events, "baseline", log, baseTimes);

        for (int a = 5; a < args.Length; a++)
        {
            int eq = args[a].IndexOf('=');
            if (eq < 1) continue;
            string name = args[a].Substring(0, eq), path = args[a].Substring(eq + 1);
            string raw = ReadOrNull(path);
            Dictionary<int, double[]> times = Times(path + ".times");
            sb.AppendLine();
            sb.AppendLine("== " + name + " ==");
            Events(events, name, log, times);
            if (raw == null)
            {
                sb.AppendLine(name + ": out file is missing: " + path);
                summary.AppendLine(name + ": 0/" + baseTok.Length + " (out file missing)");
                continue;
            }
            if (raw.StartsWith("NOFOCUS") || raw.StartsWith("NOWINDOW") || raw.StartsWith("ERROR"))
            {
                sb.AppendLine(name + ": run failed: " + raw.Trim());
                summary.AppendLine(name + ": 0/" + baseTok.Length + " (" + raw.Trim() + ")");
                continue;
            }
            List<Pair> al = Align(baseTok, Tokens(raw));
            int same = 0;
            foreach (Pair p in al) if (p.Exp == p.Got) same++;
            sb.AppendLine(name + ": " + same + " of " + baseTok.Length + " words as in the baseline");
            foreach (Pair p in al)
            {
                if (p.Exp == p.Got) continue;
                int word = (p.R < toScript.Length ? toScript[p.R] : p.R) + 1;
                sb.AppendLine("#" + word + "  " + Show(p.Exp) + " → " + Show(p.Got) + "  | log: " + LogFor(log, times, word, 50));
            }
            string pct = baseTok.Length > 0 ? (100.0 * same / baseTok.Length).ToString("0.0", Inv) + "%" : "n/a";
            summary.AppendLine(name + ": " + same + "/" + baseTok.Length + " (" + pct + ")");
        }

        sb.AppendLine();
        sb.AppendLine("== Events ==");
        sb.Append(events);
        sb.AppendLine();
        sb.AppendLine("== Summary ==");
        sb.Append(summary);
        // With a BOM: Windows PowerShell's Get-Content reads Cyrillic right without -Encoding
        File.WriteAllText(args[4], sb.ToString(), Encoding.UTF8);
    }
}
