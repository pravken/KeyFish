using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Watches typing and fixes words typed in the wrong layout or with wrong case
class KeyboardWatcher
{
    class Correction
    {
        public string Original, Replaced;
        public List<string> OriginalWords;
        public int LastWordLength;           // length of the last corrected word on screen
        public int Backspaced;               // how many Backspaces were pressed right after the correction
        public int TerminatorVk;
        public IntPtr OriginalLayout;        // the layout the user typed in, restored on undo
        public IntPtr Hwnd;
        public RecentFix Recent;             // its entry in the tray menu list, dropped on undo
    }

    // Recent auto-fixes for the tray menu, newest first. Kept only in memory and only while the option is on
    public class RecentFix
    {
        public string From, To;
        public List<string> Words;           // what goes to "Never correct" if the user picks it
    }

    public volatile bool KeepRecent = true;
    readonly List<RecentFix> recent = new List<RecentFix>();

    public List<RecentFix> Recent { get { lock (sync) return new List<RecentFix>(recent); } }

    public void ClearRecent() { lock (sync) recent.Clear(); }

    // The user picked a fix in the tray menu: its words are never corrected again
    public void NeverCorrect(RecentFix fix)
    {
        lock (sync)
        {
            foreach (string w in fix.Words) AddIgnored(w);
            recent.Remove(fix);
        }
    }

    RecentFix Remember(string from, string to, List<string> words)
    {
        if (!KeepRecent) return null;
        var fix = new RecentFix { From = from, To = to, Words = new List<string>(words) };
        recent.Insert(0, fix);
        if (recent.Count > 10) recent.RemoveAt(10);
        return fix;
    }

    // Double Shift or Caps Lock: the same as the first hotkey (undo a fresh fix, or switch the last word).
    // Raised on the hook thread, so the handler must only pass it on
    public event Action QuickFix;
    public volatile int QuickKey;        // 0 — off, 1 — double Shift, 2 — Caps Lock
    bool shiftClean, capsSwallowed;      // Shift is down with no other key yet; Caps Lock down was swallowed
    int shiftDownAt, lastShiftTap;

    // Watches the quick key; true means the keystroke is swallowed
    bool OnQuickKey(int vk, bool down)
    {
        int mode = QuickKey;
        bool shift = vk == 0x10 || vk == 0xA0 || vk == 0xA1;
        if (mode == 1)
        {
            int now = Environment.TickCount;
            if (shift && down) { if (!shiftClean) { shiftClean = true; shiftDownAt = now; } }
            else if (shift)
            {
                // A short tap of Shift alone; two of them in a row fire
                if (shiftClean && now - shiftDownAt < 300)
                {
                    if (lastShiftTap != 0 && now - lastShiftTap < 400) { lastShiftTap = 0; Fire(); }
                    else lastShiftTap = now;
                }
                shiftClean = false;
            }
            else if (down) { shiftClean = false; lastShiftTap = 0; }
        }
        else if (mode == 2 && vk == 0x14)
        {
            // Caps Lock fixes the word; Shift+Caps Lock still turns capitals on and off
            if (down && !capsSwallowed && Down(0x10)) return false;
            if (down) { if (!capsSwallowed) { capsSwallowed = true; Fire(); } return true; }
            if (capsSwallowed) { capsSwallowed = false; return true; }
        }
        return false;
    }

    void Fire()
    {
        var handler = QuickFix;
        if (handler != null) handler();
    }

    // A recently typed word, kept so it can be corrected after the fact
    class WordRec
    {
        public List<KeyStroke> Keys;
        public string Shown;                 // how the word looks on screen now
        public LayoutTable Layout;           // the layout it was typed in
        public bool Changed;                 // KeyFish has already changed it
        public int TerminatorVk;
    }

    class Job
    {
        public int Backspaces;
        public string Text;
        public int TerminatorVk;
        public bool ToggleCaps;
        public IntPtr Layout;                // not zero: switch the window to this layout afterwards
        public IntPtr Hwnd;
        public WordRec TypoRec;              // not null: check the word for a typo (the space is already typed)
        public bool ReleaseCtrl;             // lift the held Ctrl while typing (undo via Ctrl+Z)
        public int CtrlComboVk;              // not 0: send Ctrl + this key (Ctrl is still held after an undo)
    }

    public volatile bool Enabled = true;
    public volatile bool UndoWithCtrlZ = true;
    volatile bool ctrlHeld;              // the physical Ctrl key is down
    volatile bool ctrlLifted;            // an undo lifted Ctrl logically while it is still physically held
    // Fixed mistakes (layout, case, typos) and expanded replacements, for statistics and the fish blink
    int correctionCount, expansionCount;
    public int CorrectionCount { get { return Volatile.Read(ref correctionCount); } }
    public int ExpansionCount { get { return Volatile.Read(ref expansionCount); } }
    volatile bool fixTypos;
    SpellService spell;
    readonly object spellSync = new object();

    public bool FixTypos
    {
        get { return fixTypos; }
        set
        {
            fixTypos = value;
            // Load the dictionaries up front so the first word doesn't wait
            if (value) ThreadPool.QueueUserWorkItem(_ => { try { CheckTypo("проверка", "ru"); } catch { } });
        }
    }

    // Windows spell checker language for each language model
    static readonly Dictionary<string, string> SpellTags = new Dictionary<string, string>
    {
        { "en", "en-US" }, { "ru", "ru-RU" }, { "uk", "uk-UA" }, { "be", "be-BY" }, { "de", "de-DE" }, { "pl", "pl-PL" },
        { "sr-latn", "sr-Latn-RS" }, { "sr-cyrl", "sr-Cyrl-RS" }
    };

    string CheckTypo(string word, string lang)
    {
        string tag;
        if (lang == null || !SpellTags.TryGetValue(lang, out tag)) return null;
        lock (spellSync)
        {
            if (spell == null) spell = new SpellService();
            var model = Detector.Loaded(lang);
            return spell.Fix(word, tag, w => model == null ? 0 : model.Count(w));
        }
    }

    // The installed keyboard layouts and what each key types in them
    public readonly LayoutSet Layouts = new LayoutSet();
    public bool Ready { get { return true; } }

    // Layouts the user typed in, most recent first: breaks ties such as Russian vs Ukrainian
    readonly List<IntPtr> recentLayouts = new List<IntPtr>();

    void UsedLayout(IntPtr hkl)
    {
        recentLayouts.Remove(hkl);
        recentLayouts.Insert(0, hkl);
        if (recentLayouts.Count > 8) recentLayouts.RemoveAt(8);
    }

    // Of these layouts, the one used most recently; -1 if none was used yet
    int MostRecent(IList<LayoutTable> layouts)
    {
        int best = -1, bestPos = int.MaxValue;
        for (int i = 0; i < layouts.Count; i++)
        {
            int pos = recentLayouts.IndexOf(layouts[i].Hkl);
            if (pos >= 0 && pos < bestPos) { bestPos = pos; best = i; }
        }
        return best;
    }

    readonly object sync = new object();
    readonly List<KeyStroke> word = new List<KeyStroke>();
    bool partial;                        // the word didn't start at a word start (Backspace went into earlier text)
    readonly List<WordRec> history = new List<WordRec>();
    IntPtr wordHwnd;
    string context;                      // language of the previous word, or null if unknown
    Correction last;                     // the last correction; Pause can undo it
    readonly WordList ignored;           // words whose correction was undone or erased
    readonly WordList learned;           // words the user fixed manually with Pause
    bool pending;                        // a correction is still being typed
    readonly List<HeldKey> heldDuringPending = new List<HeldKey>();
    readonly BlockingCollection<Job> jobs = new BlockingCollection<Job>();

    // The files below are re-read on the file thread (see FileQueue), never in the keyboard hook
    readonly string replacementsPath;
    volatile Dictionary<string, string> replacements = new Dictionary<string, string>();
    DateTime replacementsStamp;

    readonly string exclusionsPath;
    volatile HashSet<string> excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    DateTime exclusionsStamp;
    readonly System.Threading.Timer reloadTimer;

    // Keep the delegates in fields, otherwise the GC collects them while the hook is still alive
    readonly W.HookProc keyboardProc, mouseProc;
    IntPtr keyboardHook, mouseHook;
    volatile int lastHookEvent = Environment.TickCount;   // when either hook was last called
    int lastReinstall;

    public KeyboardWatcher(string exclusionsFile, string ignoredFile, string replacementsFile, string learnedFile)
    {
        exclusionsPath = exclusionsFile;
        replacementsPath = replacementsFile;
        diagnosePath = Path.Combine(Path.GetDirectoryName(exclusionsFile), "debug-log.txt");
        ignored = new WordList(ignoredFile);
        learned = new WordList(learnedFile);
        ReloadFiles();
        reloadTimer = new System.Threading.Timer(_ => FileQueue.Run(ReloadFiles), null, 2000, 2000);
        keyboardProc = KeyboardHook;
        mouseProc = MouseHook;

        try { guard = new PasswordGuardClient(); } catch { }
        new Thread(Worker) { IsBackground = true }.Start();
        new Thread(() =>
        {
            InstallHooks();
            var watchdog = new System.Windows.Forms.Timer { Interval = 2000 };
            watchdog.Tick += (s, e) => CheckHooks();
            watchdog.Start();
            Application.Run();
        }) { IsBackground = true }.Start();
    }

    void InstallHooks()
    {
        if (keyboardHook != IntPtr.Zero) W.UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != IntPtr.Zero) W.UnhookWindowsHookEx(mouseHook);
        IntPtr mod = W.GetModuleHandle(null);
        keyboardHook = W.SetWindowsHookEx(W.WH_KEYBOARD_LL, keyboardProc, mod, 0);
        mouseHook = W.SetWindowsHookEx(W.WH_MOUSE_LL, mouseProc, mod, 0);
    }

    // Windows silently removes a low-level hook that once answered too slowly (LowLevelHooksTimeout), and KeyFish
    // would quietly stop working. Input the system registered that never reached the hooks means they are gone,
    // so they are installed again. A false alarm (touch input, say) only reinstalls hooks that still work
    void CheckHooks()
    {
        var info = new W.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(W.LASTINPUTINFO)) };
        if (!W.GetLastInputInfo(ref info)) return;
        int seen = (int)info.dwTime;
        bool lost = keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero || seen - lastHookEvent > 1000;
        if (!lost || Environment.TickCount - lastReinstall < 10000) return;
        lastReinstall = Environment.TickCount;
        lastHookEvent = seen;   // this input is accounted for: only newer missed input counts next time
        InstallHooks();
    }

    IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        lastHookEvent = Environment.TickCount;
        int msg = wParam.ToInt32();
        if (code >= 0 && (msg == 0x0201 || msg == 0x0204 || msg == 0x0207)) // L/R/M button down
        {
            lastShiftTap = 0;
            lock (sync) { ResetAll(); last = null; }
        }
        return W.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        lastHookEvent = Environment.TickCount;
        if (code >= 0)
        {
            var k = (W.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(W.KBDLLHOOKSTRUCT));
            int msg = wParam.ToInt32();
            bool down = msg == 0x0100 || msg == 0x0104; // WM_KEYDOWN, WM_SYSKEYDOWN
            // Track the physical Ctrl key: undo via Ctrl+Z releases it logically and must know whether to press it back
            // Scan code 0x21D is the fake left Ctrl Windows emits itself when the layout switches (as with AltGr);
            // it is not the user's key, so it must not count as releasing Ctrl
            if (k.dwExtraInfo != W.Marker && k.scanCode != 0x21D && (k.vkCode == 0x11 || k.vkCode == 0xA2 || k.vkCode == 0xA3))
            {
                ctrlHeld = down;
                if (!down) ctrlLifted = false;
            }
            if (k.dwExtraInfo != W.Marker && QuickKey != 0)
            {
                bool swallowQuick;
                try { swallowQuick = OnQuickKey((int)k.vkCode, down); }
                catch { swallowQuick = false; }
                if (swallowQuick) return (IntPtr)1;
            }
            if (down && k.dwExtraInfo != W.Marker)
            {
                bool swallow;
                try { swallow = OnKey((int)k.vkCode, (int)k.scanCode); }
                catch { swallow = false; }
                if (swallow) return (IntPtr)1;
            }
        }
        return W.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    static bool IsModifier(int vk)
    {
        return (vk >= 0x10 && vk <= 0x12) || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C || vk == 0x14;
    }

    static bool Down(int vk) { return (W.GetAsyncKeyState(vk) & 0x8000) != 0; }

    bool OnKey(int vk, int scan)
    {
        lock (sync)
        {
            if (vk == 0x13 || IsModifier(vk)) return false; // Pause is undo; modifiers don't reset the word

            // Undo via Ctrl+Z lifted Ctrl while the user still holds it: this key must arrive with Ctrl,
            // so it is swallowed and sent again together with Ctrl (holding Ctrl and pressing Z twice).
            // Jobs run in order, so even during the undo itself this lands right after it
            if (ctrlLifted && ctrlHeld)
            {
                ctrlLifted = false;
                pending = true;
                jobs.Add(new Job { CtrlComboVk = vk });
                return true;
            }

            if (pending)
            {
                // While a correction is being typed, hold keystrokes back and replay them afterwards,
                // with the modifiers held right now: by the replay the user may have let go of Shift
                heldDuringPending.Add(new HeldKey { Vk = vk, Shift = Down(0x10), Ctrl = Down(0x11), Alt = Down(0x12) });
                return true;
            }

            if (passwordFocused) RequestFocusCheck(); // re-check: focus may have left the password field
            if (!Enabled || !Ready || passwordFocused) { ResetAll(); last = null; return false; }

            bool ctrl = Down(0x11);
            if (vk == 0x08 && word.Count == 0 && last != null && !Down(0x12))
            {
                // The fresh correction is being erased: erasing the whole word means it shouldn't have been corrected
                last.Backspaced++;
                if (ctrl || last.Backspaced >= last.LastWordLength + 1) Reject();
                history.Clear();
                partial = true;
                return false;
            }

            // The first Ctrl+Z right after an auto-fix undoes the fix (like Pause) and is swallowed;
            // any later Ctrl+Z reaches the app and undoes text as usual
            if (vk == 0x5A && ctrl && UndoWithCtrlZ && last != null && last.Backspaced == 0 &&
                !Down(0x10) && !Down(0x12) && !Down(0x5B) && !Down(0x5C) && StartUndo(true))
            {
                ctrlLifted = true;
                return true;
            }
            last = null;

            IntPtr hwnd = W.GetForegroundWindow();
            IntPtr hkl = W.LayoutHkl(hwnd);
            var layout = Layouts.Get(hkl);
            // Input injected without a scan code (some tools, our tests): work it out from the virtual key
            if (scan == 0) scan = (int)W.MapVirtualKeyEx((uint)vk, 0, hkl);
            bool shift = Down(0x10), caps = (W.GetKeyState(0x14) & 1) != 0;

            // AltGr (right Alt, which Windows reports as Ctrl+Alt) types letters in many layouts: Polish ą, ł, ę…
            bool altGr = Down(0xA5) || (ctrl && Down(0x12));
            bool typesChar = layout != null && LayoutTable.IsTypingKey(scan) &&
                             (layout.CharFor(scan, shift, caps, altGr) != '\0' || layout.DeadFor(scan, shift, caps, altGr) != '\0');
            if (!(altGr && typesChar) && (ctrl || Down(0x12) || Down(0x5B) || Down(0x5C))) { ResetAll(); return false; }

            if (hwnd != wordHwnd) { ResetAll(); wordHwnd = hwnd; context = null; }

            if (vk == 0x08)
            {
                if (word.Count > 0)
                {
                    word.RemoveAt(word.Count - 1);
                    // An accented letter typed with a dead key is one character on screen: Backspace erases both keys
                    if (word.Count > 0 && layout != null && layout.IsDead(word[word.Count - 1])) word.RemoveAt(word.Count - 1);
                }
                else { history.Clear(); partial = true; }
                return false;
            }

            if (typesChar)
            {
                if (word.Count == 0) wordStartedAt = Environment.TickCount;
                if (word.Count < 40) word.Add(new KeyStroke(scan, shift, caps, altGr));
                RequestFocusCheck();
                return false;
            }

            if (vk == 0x20 || vk == 0x0D || vk == 0x09) // Space, Enter, Tab
            {
                bool swallow = OnWordEnd(vk, hwnd);
                word.Clear();
                return swallow;
            }

            ResetAll(); // arrows, Delete, Home and so on: the caret position is unknown
            return false;
        }
    }

    void ResetAll()
    {
        word.Clear();
        history.Clear();
        partial = false;
    }

    bool IsIgnored(string s) { return ignored.Contains(s.ToLowerInvariant()); }

    // Re-reads the files that changed in Settings or in Notepad. Runs on the file thread: disk access in the
    // keyboard hook could hold it past the system's timeout
    void ReloadFiles()
    {
        try
        {
            var stamp = File.Exists(replacementsPath) ? File.GetLastWriteTimeUtc(replacementsPath) : DateTime.MinValue;
            if (stamp != replacementsStamp)
            {
                replacementsStamp = stamp;
                var map = new Dictionary<string, string>();
                if (File.Exists(replacementsPath))
                    foreach (var kv in ReplacementFile.Parse(File.ReadAllText(replacementsPath, Encoding.UTF8)))
                        map[kv.Key.ToLowerInvariant()] = kv.Value;
                replacements = map;
            }
        }
        catch { }

        try
        {
            var stamp = File.Exists(exclusionsPath) ? File.GetLastWriteTimeUtc(exclusionsPath) : DateTime.MinValue;
            if (stamp != exclusionsStamp)
            {
                exclusionsStamp = stamp;
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(exclusionsPath))
                    foreach (string line in File.ReadAllLines(exclusionsPath, Encoding.UTF8))
                    {
                        string s = line.Trim();
                        if (s.Length == 0 || s.StartsWith("#")) continue;
                        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
                        set.Add(s);
                    }
                excluded = set;
            }
        }
        catch { }

        diagnose = File.Exists(diagnosePath);
        ignored.ReloadIfChanged();
        learned.ReloadIfChanged();
    }

    string FindReplacement(string token)
    {
        string value;
        if (!replacements.TryGetValue(token.ToLowerInvariant(), out value)) return null;
        // "Thx" → "Thank you": a capital letter at the start carries over to the text
        if (char.IsUpper(token[0]) && char.IsLower(value[0])) value = char.ToUpper(value[0]) + value.Substring(1);
        return value;
    }

    void AddIgnored(string s)
    {
        ignored.Add(s);
        learned.Remove(s);
    }

    // Selected text retyped in a layout of another alphabet: the same keys, as if pressed there.
    // Among several such layouts, the one whose dictionary knows most of the words wins,
    // ties go to the one used last. Returns null if no installed layout fits
    public string ConvertText(string text, IntPtr hwnd, out LayoutTable target)
    {
        target = null;
        var counts = new Dictionary<string, int>();
        foreach (char ch in text)
        {
            string sc = LayoutTable.ScriptOf(ch);
            if (sc == null) continue;
            int n; counts.TryGetValue(sc, out n); counts[sc] = n + 1;
        }
        string script = null;
        int most = 0;
        foreach (var kv in counts) if (kv.Value > most) { most = kv.Value; script = kv.Key; }
        if (script == null) return null;

        var all = new List<LayoutTable>(Layouts.All);
        // The text was typed in the window's layout if that has the right alphabet, otherwise in the one used last
        LayoutTable source = Layouts.Get(W.LayoutHkl(hwnd));
        if (source == null || source.Script != script)
        {
            var same = all.FindAll(t => t.Script == script);
            int i;
            lock (sync) i = MostRecent(same);
            source = i >= 0 ? same[i] : same.Count > 0 ? same[0] : null;
        }
        if (source == null) return null;

        var others = all.FindAll(t => t.Script != script && t.Script != null);
        int recent;
        lock (sync) recent = MostRecent(others);
        string best = null;
        double bestScore = double.MinValue;
        for (int i = 0; i < others.Count; i++)
        {
            var t = others[i];
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                var keys = source.Keys(ch.ToString());
                string r = keys == null ? null : t.Type(keys);
                sb.Append(r == null || r.IndexOf('\uFFFD') >= 0 ? ch.ToString() : r);
            }
            string s = sb.ToString();
            double score = i == recent ? 0.5 : 0;
            var model = Detector.Loaded(t.Lang);
            if (model != null)
                foreach (string w in s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string core = Detector.Core(w);
                    if (!string.IsNullOrEmpty(core) && model.Count(core) > 0) score++;
                }
            if (score > bestScore) { bestScore = score; best = s; target = t; }
        }
        if (target != null) lock (sync) UsedLayout(target.Hkl);
        return best;
    }

    // The user fixed a word with Pause, so it gets corrected automatically next time
    public void Learn(string typed, string result, string resultLang)
    {
        string w = typed.Trim();
        if (w.Length == 0 || w.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0 || w.Length > 40) return;
        // Learn only when the result is a real word: converting a correct word by accident teaches nothing
        string core = Detector.Core(result.Trim());
        var model = Detector.Loaded(resultLang);
        if (string.IsNullOrEmpty(core) || model == null || model.Count(core) == 0) return;
        lock (sync)
        {
            learned.Add(w);
            ignored.Remove(w);
        }
    }

    // The user erased a correction: remember the words and switch the layout back
    void Reject()
    {
        var c = last;
        last = null;
        foreach (string w in c.OriginalWords) AddIgnored(w);
        recent.Remove(c.Recent);
        W.SwitchLayout(c.Hwnd, c.OriginalLayout);
        var original = Layouts.Get(c.OriginalLayout);
        context = original == null ? null : original.Lang;
    }

    void PushHistory(WordRec rec)
    {
        history.Add(rec);
        if (history.Count > 5) history.RemoveAt(0);
    }

    // A confidently corrected word: long enough and in the dictionary
    static bool IsStrong(string text, LangModel model)
    {
        string core = Detector.Core(text);
        return core != null && core.Length >= 3 && model != null && model.Count(core) > 0;
    }

    bool OnWordEnd(int terminatorVk, IntPtr hwnd)
    {
        bool wasPartial = partial;
        partial = false;
        if (word.Count == 0) return false;
        string skip = wasPartial ? "edited mid-word" : word.Count >= 40 ? "too long" : passwordFocused ? "password field" :
                      !FocusVerified ? "focus not checked yet" : IsOwnWindow(hwnd) ? "KeyFish window" : IsExcluded(hwnd) ? "excluded app" :
                      IsPasswordField(hwnd) ? "password field (Win32)" : QuietFullscreen && IsFullscreen(hwnd) ? "full screen" : null;
        if (skip != null) { Diagnose(hwnd, skip); history.Clear(); return false; }

        var current = Layouts.Get(W.LayoutHkl(hwnd));
        var T = current == null ? null : Detector.Loaded(current.Lang);
        if (T == null) { Diagnose(hwnd, "no language model for the layout"); history.Clear(); return false; }

        string typed = Build(word, current);

        // The same keys in every installed layout of another alphabet
        var others = new List<LayoutTable>();
        var alts = new List<string>();
        var altModels = new List<LangModel>();
        foreach (var t in Layouts.All)
        {
            if (t.Script == current.Script) continue;
            var m = Detector.Loaded(t.Lang);
            if (m == null) continue;
            others.Add(t);
            alts.Add(Build(word, t));
            altModels.Add(m);
        }
        int preferred = MostRecent(others);

        var rec = new WordRec { Keys = new List<KeyStroke>(word), Shown = typed, Layout = current, TerminatorVk = terminatorVk };

        // Replacement: an abbreviation from replacements.txt, typed in any layout
        LayoutTable via = null;
        string expansion = FindReplacement(typed);
        for (int i = 0; expansion == null && i < others.Count; i++)
        {
            expansion = FindReplacement(alts[i]);
            if (expansion != null) via = others[i];
        }
        if (expansion != null)
        {
            rec.Shown = expansion;
            rec.Changed = true;
            PushHistory(rec);
            if (via != null) { context = via.Lang; UsedLayout(via.Hkl); }
            last = new Correction
            {
                Original = typed, Replaced = expansion, OriginalWords = new List<string>(),
                LastWordLength = expansion.Length, TerminatorVk = terminatorVk, OriginalLayout = current.Hkl, Hwnd = hwnd
            };
            pending = true;
            Interlocked.Increment(ref expansionCount);
            jobs.Add(new Job
            {
                Backspaces = typed.Length, Text = expansion, TerminatorVk = terminatorVk,
                Layout = via != null ? via.Hkl : IntPtr.Zero, Hwnd = hwnd
            });
            return true;
        }

        // Numbers, e-mails, identifiers aren't words to fix (an abbreviation like "addr1" was expanded above)
        foreach (char c in typed)
            if (char.IsDigit(c) || c == '@' || c == '_') { Diagnose(hwnd, "digits or symbols"); history.Clear(); return false; }

        // Which layout did the user mean? A word they taught KeyFish goes to their usual other layout
        int chosen = -1;
        if (!IsIgnored(typed) && others.Count > 0)
            chosen = learned.Contains(typed) ? (preferred >= 0 ? preferred : 0)
                                             : Detector.Choose(typed, T, alts, altModels, context, preferred);
        bool convert = chosen >= 0;
        var target = convert ? others[chosen] : current;
        var targetModel = convert ? altModels[chosen] : T;
        string result = convert ? alts[chosen] : typed;

        bool capsMistake;
        string fixedCase = FixCase(result, out capsMistake);
        if (!convert && IsIgnored(fixedCase)) fixedCase = result;

        string core = Detector.Core(result);
        if (convert || (!string.IsNullOrEmpty(core) && targetModel.Count(core) > 0))
        {
            context = target.Lang;
            UsedLayout(target.Hkl);
        }

        // A word was confidently corrected, so check the short words before it: "f vj;yj" → "а можно"
        var retro = new List<WordRec>();
        if (convert && IsStrong(result, targetModel))
        {
            for (int i = history.Count - 1; i >= 0 && retro.Count < 3; i--)
            {
                var p = history[i];
                if (p.TerminatorVk != 0x20 || p.Changed || p.Layout != current || IsIgnored(p.Shown)) break;
                if (!Detector.ShouldConvert(p.Shown, Build(p.Keys, target), T, targetModel, target.Lang)) break;
                retro.Insert(0, p);
            }
        }

        if (!convert && fixedCase == result)
        {
            Diagnose(hwnd, IsIgnored(typed) ? "in Never correct" : "looks right as typed");
            PushHistory(rec);
            // Typos are checked after the space without holding it; Enter may already have sent a message
            if (fixTypos && terminatorVk == 0x20 && TypoCandidate(typed))
            {
                pending = true;
                jobs.Add(new Job { TypoRec = rec, TerminatorVk = terminatorVk, Hwnd = hwnd });
            }
            return false;
        }

        var original = new StringBuilder();
        var replaced = new StringBuilder();
        var originalWords = new List<string>();
        int backspaces = typed.Length;   // characters on screen: a dead key and its letter are one
        foreach (var p in retro)
        {
            string pAlt = Build(p.Keys, target);
            original.Append(p.Shown).Append(' ');
            replaced.Append(pAlt).Append(' ');
            originalWords.Add(p.Shown);
            backspaces += p.Shown.Length + 1;
            p.Shown = pAlt;
            p.Changed = true;
        }
        original.Append(typed);
        replaced.Append(fixedCase);
        originalWords.Add(typed);
        rec.Shown = fixedCase;
        rec.Changed = true;
        PushHistory(rec);

        Diagnose(hwnd, convert ? "corrected: layout" : "corrected: case");
        last = new Correction
        {
            Original = original.ToString(), Replaced = replaced.ToString(), OriginalWords = originalWords,
            LastWordLength = fixedCase.Length, TerminatorVk = terminatorVk, OriginalLayout = current.Hkl, Hwnd = hwnd,
            Recent = Remember(original.ToString(), replaced.ToString(), originalWords)
        };
        pending = true;
        Interlocked.Increment(ref correctionCount);
        jobs.Add(new Job
        {
            Backspaces = backspaces,
            Text = replaced.ToString(),
            TerminatorVk = terminatorVk,
            ToggleCaps = capsMistake && (W.GetKeyState(0x14) & 1) != 0,
            Layout = convert ? target.Hkl : IntPtr.Zero,
            Hwnd = hwnd
        });
        return true;
    }

    // Undo the last correction (Pause right after it). false means there is nothing to undo
    public bool Undo() { return StartUndo(false); }

    // releaseCtrl: the undo comes from Ctrl+Z, so Ctrl is held and must be lifted while the text is retyped
    bool StartUndo(bool releaseCtrl)
    {
        Correction c;
        lock (sync)
        {
            c = last;
            if (c == null || pending || W.GetForegroundWindow() != c.Hwnd) return false;
            last = null;
            foreach (string w in c.OriginalWords) AddIgnored(w);
            recent.Remove(c.Recent);
            history.Clear();
            context = null;
            pending = true;
        }
        jobs.Add(new Job
        {
            Backspaces = c.Replaced.Length + 1,
            Text = c.Original,
            TerminatorVk = c.TerminatorVk,
            Layout = c.OriginalLayout,
            Hwnd = c.Hwnd,
            ReleaseCtrl = releaseCtrl
        });
        return true;
    }

    // Only lowercase words of 4+ letters: names, abbreviations and short words are left alone
    bool TypoCandidate(string typed)
    {
        if (IsIgnored(typed)) return false;
        string core = Detector.Core(typed);
        if (core == null || core.Length < 4 || core.IndexOf('-') >= 0) return false;
        foreach (char c in typed) if (char.IsUpper(c)) return false;
        return true;
    }

    void ProcessTypo(Job job)
    {
        var rec = job.TypoRec;
        string typed = rec.Shown;
        string core = Detector.Core(typed);
        int start = typed.IndexOf(core, StringComparison.Ordinal);
        string fixedWord = CheckTypo(core, rec.Layout.Lang);
        if (fixedWord == null || fixedWord == core || start < 0) return;

        string text = typed.Substring(0, start) + fixedWord + typed.Substring(start + core.Length);
        lock (sync)
        {
            if (W.GetForegroundWindow() != job.Hwnd) return;
            last = new Correction
            {
                Original = typed, Replaced = text, OriginalWords = new List<string> { typed },
                LastWordLength = text.Length, TerminatorVk = job.TerminatorVk, OriginalLayout = rec.Layout.Hkl, Hwnd = job.Hwnd,
                Recent = Remember(typed, text, new List<string> { typed })
            };
            rec.Shown = text;
            rec.Changed = true;
            Interlocked.Increment(ref correctionCount);
        }
        Execute(new Job { Backspaces = typed.Length + 1, Text = text, TerminatorVk = job.TerminatorVk, Hwnd = job.Hwnd });
    }

    void Worker()
    {
        foreach (var job in jobs.GetConsumingEnumerable())
        {
            try
            {
                if (job.TypoRec != null) ProcessTypo(job);
                else Execute(job);
            }
            catch { }
            HeldKey[] held;
            lock (sync)
            {
                // More jobs queued: keep holding keystrokes so they replay after those jobs, in typing order
                if (jobs.Count > 0) continue;
                pending = false;
                held = heldDuringPending.ToArray();
                heldDuringPending.Clear();
            }
            // Replay the keystrokes held back during typing, now as normal input
            if (held.Length > 0) W.SendHeld(held);
        }
    }

    void Execute(Job job)
    {
        var inputs = new List<W.INPUT>();
        if (job.CtrlComboVk != 0)
        {
            // Ctrl is physically down (it can't have been released: that would have cleared ctrlLifted first),
            // so pressing it logically again can't leave it stuck; the physical release will lift it
            W.AddKeyDown(inputs, 0xA2);
            W.AddKey(inputs, job.CtrlComboVk);
            W.Send(inputs);
            return;
        }
        // With Ctrl held, Backspace would erase whole words: lift Ctrl first
        if (job.ReleaseCtrl) { W.AddKeyUp(inputs, 0xA2); W.AddKeyUp(inputs, 0xA3); W.AddKeyUp(inputs, 0x11); }
        if (job.ToggleCaps) W.AddKey(inputs, 0x14);
        for (int i = 0; i < job.Backspaces; i++) W.AddKey(inputs, 0x08);
        foreach (char c in job.Text)
        {
            if (c == '\n') W.AddKey(inputs, 0x0D);       // a line break is sent as a real Enter
            else if (c == '\t') W.AddKey(inputs, 0x09);
            else if (c != '\r') W.AddUnicode(inputs, c);
        }
        W.AddKey(inputs, job.TerminatorVk);
        W.Send(inputs);
        if (job.Layout != IntPtr.Zero) W.SwitchLayout(job.Hwnd, job.Layout);
    }

    // The word these keys type in the given layout
    static string Build(List<KeyStroke> keys, LayoutTable t) { return t.Type(keys); }

    // пРИВЕТ → Привет (accidental CapsLock), ПРивет → Привет (Shift held too long)
    static string FixCase(string w, out bool capsMistake)
    {
        capsMistake = false;
        var idx = new List<int>();
        for (int i = 0; i < w.Length; i++) if (char.IsLetter(w[i])) idx.Add(i);
        if (idx.Count < 3) return w;

        var chars = w.ToCharArray();
        bool restUpper = true, restLower = true;
        for (int j = 1; j < idx.Count; j++)
        {
            if (!char.IsUpper(chars[idx[j]])) restUpper = false;
            if (j >= 2 && !char.IsLower(chars[idx[j]])) restLower = false;
        }

        if (char.IsLower(chars[idx[0]]) && restUpper)
        {
            foreach (int i in idx) chars[i] = char.IsUpper(chars[i]) ? char.ToLower(chars[i]) : char.ToUpper(chars[i]);
            capsMistake = true;
            return new string(chars);
        }
        if (idx.Count >= 4 && char.IsUpper(chars[idx[0]]) && char.IsUpper(chars[idx[1]]) && restLower)
        {
            chars[idx[1]] = char.ToLower(chars[idx[1]]);
            return new string(chars);
        }
        return w;
    }

    public volatile bool QuietFullscreen = true;

    // Focus is in a password field (any app, browsers included): then nothing is corrected or remembered.
    // The check itself runs in a separate guard process, see PasswordGuard.cs
    PasswordGuardClient guard;
    bool passwordFocused { get { return guard != null && guard.Password; } }
    volatile int wordStartedAt;

    // Focus was checked after this word started; otherwise we can't know it isn't a password
    bool FocusVerified { get { return guard != null && guard.CheckedAt - wordStartedAt >= 0; } }

    void RequestFocusCheck() { if (guard != null) guard.RequestCheck(); }

    // A full-screen game, presentation or video: typing there isn't text, so don't correct
    static bool IsFullscreen(IntPtr hwnd)
    {
        int state;
        // 2 — full-screen app, 3 — Direct3D game, 4 — presentation mode
        if (W.SHQueryUserNotificationState(out state) == 0 && (state == 2 || state == 3 || state == 4)) return true;

        string cls = W.ClassName(hwnd);
        if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
        W.RECT r;
        var mi = new W.MONITORINFO { cbSize = Marshal.SizeOf(typeof(W.MONITORINFO)) };
        if (!W.GetWindowRect(hwnd, out r) || !W.GetMonitorInfo(W.MonitorFromWindow(hwnd, 2), ref mi)) return false;
        // A normal maximized window stops at the taskbar; a full-screen one covers the whole monitor
        return r.left <= mi.rcMonitor.left && r.top <= mi.rcMonitor.top &&
               r.right >= mi.rcMonitor.right && r.bottom >= mi.rcMonitor.bottom;
    }

    static readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;

    // Nothing is corrected in KeyFish's own settings window
    static bool IsOwnWindow(IntPtr hwnd)
    {
        uint pid;
        W.GetWindowThreadProcessId(hwnd, out pid);
        return pid == ownPid;
    }

    // Diagnostics, off unless debug-log.txt exists next to the settings files: one line per word with the app, what KeyFish
    // decided and why, the word's length and how fresh the password check was. Never the word itself
    readonly string diagnosePath;
    volatile bool diagnose;

    void Diagnose(IntPtr hwnd, string what)
    {
        if (!diagnose) return;
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + W.ProcessName(hwnd) + "  " + what + "  (" + word.Count + " keys, password check " +
                      (guard == null ? "not running" : (guard.CheckedAt - wordStartedAt) + " ms after the word began") + ")\r\n";
        FileQueue.Run(() => File.AppendAllText(diagnosePath, line, Encoding.UTF8));
    }

    bool IsExcluded(IntPtr hwnd) { return excluded.Contains(W.ProcessName(hwnd)); }

    static bool IsPasswordField(IntPtr hwnd)
    {
        uint pid;
        uint tid = W.GetWindowThreadProcessId(hwnd, out pid);
        var info = new W.GUITHREADINFO();
        info.cbSize = Marshal.SizeOf(info);
        if (!W.GetGUIThreadInfo(tid, ref info) || info.hwndFocus == IntPtr.Zero) return false;
        return (W.GetWindowLong(info.hwndFocus, -16) & 0x20) != 0 && W.ClassName(info.hwndFocus) == "Edit"; // ES_PASSWORD
    }
}

// A keystroke held back while a correction was typed, with the modifiers that were down
struct HeldKey { public int Vk; public bool Shift, Ctrl, Alt; }

static class W
{
    public static readonly UIntPtr Marker = (UIntPtr)0x4C464958; // marks our own input so the hook skips it
    public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;

    public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO { public uint cbSize, dwTime; }

    // The window's process name as Task Manager shows it, without .exe; "" if unknown. Asked each time instead of
    // cached by process id: ids are reused, and a stale name could treat a terminal as some excluded app or back
    public static string ProcessName(IntPtr hwnd)
    {
        uint pid;
        GetWindowThreadProcessId(hwnd, out pid);
        IntPtr h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? Path.GetFileNameWithoutExtension(sb.ToString()) : "";
        }
        finally { CloseHandle(h); }
    }
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr hkl);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    // The keyboard layout of the window's thread (HKL)
    public static IntPtr LayoutHkl(IntPtr hwnd)
    {
        uint pid;
        return GetKeyboardLayout(GetWindowThreadProcessId(hwnd, out pid));
    }

    // Primary language of the window's layout: 0x09 — English, 0x19 — Russian, …
    public static int LayoutLanguage(IntPtr hwnd) { return (int)(LayoutHkl(hwnd).ToInt64() & 0x3FF); }

    public static string ClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(64);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    static INPUT Key(ushort vk, ushort scan, uint flags)
    {
        var i = new INPUT { type = 1 };
        i.u.ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags, dwExtraInfo = Marker };
        return i;
    }

    public static void AddKeyDown(List<INPUT> list, int vk) { list.Add(Key((ushort)vk, 0, 0)); }
    public static void AddKeyUp(List<INPUT> list, int vk) { list.Add(Key((ushort)vk, 0, 2)); }

    public static void AddKey(List<INPUT> list, int vk)
    {
        list.Add(Key((ushort)vk, 0, 0));
        list.Add(Key((ushort)vk, 0, 2));
    }

    public static void AddUnicode(List<INPUT> list, char c)
    {
        list.Add(Key(0, c, 4));     // KEYEVENTF_UNICODE
        list.Add(Key(0, c, 4 | 2));
    }

    public static void Send(List<INPUT> list)
    {
        var arr = list.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
    }

    // Held-back keystrokes are sent without the marker so the hook handles them as normal typing.
    // Each goes with the Shift, Ctrl and Alt it was pressed with: where the keyboard differs now, those modifiers
    // are pressed or released around the key (with the marker, so the hook doesn't count them) and put back after
    public static void SendHeld(IList<HeldKey> keys)
    {
        var list = new List<INPUT>();
        foreach (var k in keys)
        {
            var undo = new List<INPUT>();
            Modifier(list, undo, k.Shift, 0xA0, 0xA1);
            Modifier(list, undo, k.Ctrl, 0xA2, 0xA3);
            Modifier(list, undo, k.Alt, 0xA4, 0xA5);
            var down = Key((ushort)k.Vk, 0, 0);
            var up = Key((ushort)k.Vk, 0, 2);
            down.u.ki.dwExtraInfo = up.u.ki.dwExtraInfo = UIntPtr.Zero;
            list.Add(down);
            list.Add(up);
            undo.Reverse();
            list.AddRange(undo);
        }
        var arr = list.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
    }

    // Makes a modifier (its left and right keys) match what a held key needs; undo gets the inputs that restore it
    static void Modifier(List<INPUT> list, List<INPUT> undo, bool want, int left, int right)
    {
        bool l = (GetAsyncKeyState(left) & 0x8000) != 0, r = (GetAsyncKeyState(right) & 0x8000) != 0;
        if (want == (l || r)) return;
        if (want) { list.Add(Key((ushort)left, 0, 0)); undo.Add(Key((ushort)left, 0, 2)); return; }
        if (l) { list.Add(Key((ushort)left, 0, 2)); undo.Add(Key((ushort)left, 0, 0)); }
        if (r) { list.Add(Key((ushort)right, 0, 2)); undo.Add(Key((ushort)right, 0, 0)); }
    }

    public static void SwitchLayout(IntPtr hwnd, IntPtr hkl)
    {
        if (hkl != IntPtr.Zero) PostMessage(hwnd, 0x0050, IntPtr.Zero, hkl); // WM_INPUTLANGCHANGEREQUEST
    }
}

// A word list in a text file next to the app: one word per line, case-insensitive.
// The file is re-read when it changes in Settings or in Notepad. The keyboard hook only touches the set in memory;
// the file is written on the file thread
class WordList
{
    readonly string path;
    readonly object gate = new object();
    HashSet<string> words = new HashSet<string>();
    DateTime stamp;

    public WordList(string path)
    {
        this.path = path;
        ReloadIfChanged();
    }

    public bool Contains(string word) { lock (gate) return words.Contains(word.ToLowerInvariant()); }

    public void Add(string word)
    {
        string w = word.ToLowerInvariant();
        lock (gate) if (!words.Add(w)) return;
        FileQueue.Run(() =>
        {
            File.AppendAllText(path, w + "\r\n", Encoding.UTF8);
            lock (gate) stamp = File.GetLastWriteTimeUtc(path);
        });
    }

    public void Remove(string word)
    {
        string w = word.ToLowerInvariant();
        lock (gate) if (!words.Remove(w)) return;
        FileQueue.Run(() =>
        {
            if (!File.Exists(path)) return;
            var kept = new List<string>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                if (!string.Equals(line.Trim(), w, StringComparison.OrdinalIgnoreCase)) kept.Add(line);
            File.WriteAllLines(path, kept, Encoding.UTF8);
            lock (gate) stamp = File.GetLastWriteTimeUtc(path);
        });
    }

    // Called on the file thread (and once at start)
    public void ReloadIfChanged()
    {
        try
        {
            var current = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            lock (gate) if (current == stamp) return;
            var set = new HashSet<string>();
            if (File.Exists(path))
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string s = line.Trim();
                    if (s.Length > 0 && !s.StartsWith("#")) set.Add(s.ToLowerInvariant());
                }
            lock (gate) { words = set; stamp = current; }
        }
        catch { }
    }
}

// One background thread for writing and re-reading KeyFish's text files, in order. Disk access can take a while
// (an antivirus scanning the file, a sleeping disk), and the keyboard hook must answer within the system's timeout
static class FileQueue
{
    static readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();

    static FileQueue()
    {
        new Thread(() =>
        {
            foreach (var action in queue.GetConsumingEnumerable())
                try { action(); } catch { }
        }) { IsBackground = true, Name = "KeyFish files" }.Start();
    }

    public static void Run(Action action) { queue.Add(action); }

    // Waits until everything queued so far is written: before Settings reads the files and before exit
    public static void Flush(int timeoutMs)
    {
        using (var done = new ManualResetEvent(false))
        {
            queue.Add(() => done.Set());
            done.WaitOne(timeoutMs);
        }
    }
}

// replacements.txt: "abbreviation = text" per line, # starts a comment. Inside the text \n is a line break,
// \t a tab and \\ a backslash, so a path like C:\new survives saving and reading back
static class ReplacementFile
{
    public static List<KeyValuePair<string, string>> Parse(string text)
    {
        var list = new List<KeyValuePair<string, string>>();
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string s = raw.Trim();
            int eq = s.IndexOf('=');
            if (s.StartsWith("#") || eq <= 0) continue;
            string key = s.Substring(0, eq).Trim(), value = Unescape(s.Substring(eq + 1).Trim());
            if (key.Length > 0 && key.IndexOf(' ') < 0 && value.Length > 0) list.Add(new KeyValuePair<string, string>(key, value));
        }
        return list;
    }

    public static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");
    }

    static string Unescape(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                char next = value[i + 1];
                if (next == 'n') { sb.Append('\n'); i++; continue; }
                if (next == 't') { sb.Append('\t'); i++; continue; }
                if (next == '\\') { sb.Append('\\'); i++; continue; }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
