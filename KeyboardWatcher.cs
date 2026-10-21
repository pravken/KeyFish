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
    // A physical key press: the scan code identifies the key itself, whatever the layout
    struct KeyStroke
    {
        public int Scan; public bool Shift, Caps, AltGr;
        public KeyStroke(int scan, bool shift, bool caps, bool altGr) { Scan = scan; Shift = shift; Caps = caps; AltGr = altGr; }
    }

    class Correction
    {
        public string Original, Replaced;
        public List<string> OriginalWords;
        public int LastWordLength;           // length of the last corrected word on screen
        public int Backspaced;               // how many Backspaces were pressed right after the correction
        public int TerminatorVk;
        public IntPtr OriginalLayout;        // the layout the user typed in, restored on undo
        public IntPtr Hwnd;
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

    // The layout of each alphabet the user typed in last (true — Cyrillic): breaks ties such as Russian vs Ukrainian
    readonly Dictionary<bool, IntPtr> lastLayoutByScript = new Dictionary<bool, IntPtr>();

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
    readonly List<int> heldDuringPending = new List<int>();
    readonly BlockingCollection<Job> jobs = new BlockingCollection<Job>();

    readonly string replacementsPath;
    Dictionary<string, string> replacements = new Dictionary<string, string>();
    DateTime replacementsStamp;

    readonly string exclusionsPath;
    HashSet<string> excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    DateTime exclusionsStamp, exclusionsChecked;
    readonly Dictionary<uint, string> processNames = new Dictionary<uint, string>();

    // Keep the delegates in fields, otherwise the GC collects them while the hook is still alive
    readonly W.HookProc keyboardProc, mouseProc;

    public KeyboardWatcher(string exclusionsFile, string ignoredFile, string replacementsFile, string learnedFile)
    {
        exclusionsPath = exclusionsFile;
        replacementsPath = replacementsFile;
        ignored = new WordList(ignoredFile);
        learned = new WordList(learnedFile);
        LoadReplacements();
        keyboardProc = KeyboardHook;
        mouseProc = MouseHook;

        try { guard = new PasswordGuardClient(); } catch { }
        new Thread(Worker) { IsBackground = true }.Start();
        new Thread(() =>
        {
            IntPtr mod = W.GetModuleHandle(null);
            W.SetWindowsHookEx(W.WH_KEYBOARD_LL, keyboardProc, mod, 0);
            W.SetWindowsHookEx(W.WH_MOUSE_LL, mouseProc, mod, 0);
            Application.Run();
        }) { IsBackground = true }.Start();
    }

    IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        int msg = wParam.ToInt32();
        if (code >= 0 && (msg == 0x0201 || msg == 0x0204 || msg == 0x0207)) // L/R/M button down
            lock (sync) { ResetAll(); last = null; }
        return W.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
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
                // While a correction is being typed, hold keystrokes back and replay them afterwards
                heldDuringPending.Add(vk);
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
            bool typesChar = layout != null && LayoutTable.IsTypingKey(scan) && layout.CharFor(scan, shift, caps, altGr) != '\0';
            if (!(altGr && typesChar) && (ctrl || Down(0x12) || Down(0x5B) || Down(0x5C))) { ResetAll(); return false; }

            if (hwnd != wordHwnd) { ResetAll(); wordHwnd = hwnd; context = null; }

            if (vk == 0x08)
            {
                if (word.Count > 0) word.RemoveAt(word.Count - 1);
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

    // Line format: "abbreviation = text"; \n is a line break, \t a tab, # a comment
    void LoadReplacements()
    {
        var map = new Dictionary<string, string>();
        try
        {
            if (File.Exists(replacementsPath))
            {
                replacementsStamp = File.GetLastWriteTimeUtc(replacementsPath);
                foreach (string line in File.ReadAllLines(replacementsPath, Encoding.UTF8))
                {
                    string s = line.Trim();
                    int eq = s.IndexOf('=');
                    if (s.StartsWith("#") || eq <= 0) continue;
                    string key = s.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = s.Substring(eq + 1).Trim().Replace("\\n", "\n").Replace("\\t", "\t");
                    if (key.Length > 0 && key.IndexOf(' ') < 0 && value.Length > 0) map[key] = value;
                }
            }
        }
        catch { }
        replacements = map;
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

    // The user fixed a word with Pause, so it gets corrected automatically next time
    // Selected text retyped in the layout of the other alphabet: the same keys, as if pressed there.
    // Among several layouts of that alphabet, the one whose dictionary knows most of the words wins,
    // ties go to the one used last. Returns null if no installed layout fits
    public string ConvertText(string text, IntPtr hwnd, out LayoutTable target)
    {
        target = null;
        int lat = 0, cyr = 0;
        foreach (char ch in text)
        {
            if (!char.IsLetter(ch)) continue;
            if (ch >= 'Ѐ' && ch <= 'ӿ') cyr++; else lat++;
        }
        bool fromCyrillic = cyr > lat;

        IntPtr lastSame, lastOther;
        lock (sync)
        {
            lastLayoutByScript.TryGetValue(fromCyrillic, out lastSame);
            lastLayoutByScript.TryGetValue(!fromCyrillic, out lastOther);
        }
        // The text was typed in the window's layout if that has the right alphabet, otherwise in the one used last
        LayoutTable source = Layouts.Get(W.LayoutHkl(hwnd));
        if (source == null || source.Cyrillic != fromCyrillic)
        {
            source = null;
            foreach (var t in Layouts.All)
                if (t.Cyrillic == fromCyrillic && (source == null || t.Hkl == lastSame)) source = t;
        }
        if (source == null) return null;

        string best = null;
        double bestScore = double.MinValue;
        foreach (var t in Layouts.All)
        {
            if (t.Cyrillic == fromCyrillic) continue;
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                int scan; bool shift, altGr;
                char r = source.Find(ch, out scan, out shift, out altGr) ? t.CharFor(scan, shift, false, altGr) : '\0';
                sb.Append(r == '\0' ? ch : r);
            }
            string s = sb.ToString();
            double score = t.Hkl == lastOther ? 0.5 : 0;
            var model = Detector.Loaded(t.Lang);
            if (model != null)
                foreach (string w in s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string core = Detector.Core(w);
                    if (!string.IsNullOrEmpty(core) && model.Count(core) > 0) score++;
                }
            if (score > bestScore) { bestScore = score; best = s; target = t; }
        }
        if (target != null) lock (sync) lastLayoutByScript[target.Cyrillic] = target.Hkl;
        return best;
    }

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
        if (wasPartial || word.Count >= 40 || passwordFocused || !FocusVerified || IsOwnWindow(hwnd) || IsExcluded(hwnd) || IsPasswordField(hwnd) ||
            (QuietFullscreen && IsFullscreen(hwnd))) { history.Clear(); return false; }

        var current = Layouts.Get(W.LayoutHkl(hwnd));
        var T = current == null ? null : Detector.Loaded(current.Lang);
        if (T == null) { history.Clear(); return false; }   // a layout KeyFish has no language model for

        string typed = Build(word, current);
        foreach (char c in typed)
            if (char.IsDigit(c) || c == '@' || c == '_') { history.Clear(); return false; }

        // The same keys in every installed layout of the other alphabet
        var others = new List<LayoutTable>();
        var alts = new List<string>();
        var altModels = new List<LangModel>();
        int preferred = -1;
        IntPtr lastOther;
        lastLayoutByScript.TryGetValue(!current.Cyrillic, out lastOther);
        foreach (var t in Layouts.All)
        {
            if (t.Cyrillic == current.Cyrillic) continue;
            var m = Detector.Loaded(t.Lang);
            if (m == null) continue;
            if (t.Hkl == lastOther) preferred = others.Count;
            others.Add(t);
            alts.Add(Build(word, t));
            altModels.Add(m);
        }

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
            if (via != null) { context = via.Lang; lastLayoutByScript[via.Cyrillic] = via.Hkl; }
            last = new Correction
            {
                Original = typed, Replaced = expansion, OriginalWords = new List<string>(),
                LastWordLength = expansion.Length, TerminatorVk = terminatorVk, OriginalLayout = current.Hkl, Hwnd = hwnd
            };
            pending = true;
            Interlocked.Increment(ref expansionCount);
            jobs.Add(new Job
            {
                Backspaces = word.Count, Text = expansion, TerminatorVk = terminatorVk,
                Layout = via != null ? via.Hkl : IntPtr.Zero, Hwnd = hwnd
            });
            return true;
        }

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
            lastLayoutByScript[target.Cyrillic] = target.Hkl;
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
        int backspaces = word.Count;
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

        last = new Correction
        {
            Original = original.ToString(), Replaced = replaced.ToString(), OriginalWords = originalWords,
            LastWordLength = fixedCase.Length, TerminatorVk = terminatorVk, OriginalLayout = current.Hkl, Hwnd = hwnd
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
                LastWordLength = text.Length, TerminatorVk = job.TerminatorVk, OriginalLayout = rec.Layout.Hkl, Hwnd = job.Hwnd
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
            int[] held;
            lock (sync)
            {
                // More jobs queued: keep holding keystrokes so they replay after those jobs, in typing order
                if (jobs.Count > 0) continue;
                pending = false;
                held = heldDuringPending.ToArray();
                heldDuringPending.Clear();
            }
            // Replay the keystrokes held back during typing, now as normal input
            if (held.Length > 0) W.SendKeys(held);
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
    static string Build(List<KeyStroke> keys, LayoutTable t)
    {
        var sb = new StringBuilder(keys.Count);
        foreach (var k in keys)
        {
            char c = t.CharFor(k.Scan, k.Shift, k.Caps, k.AltGr);
            sb.Append(c == '\0' ? '�' : c);   // a key with nothing in this layout spoils the word
        }
        return sb.ToString();
    }

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

    bool IsExcluded(IntPtr hwnd)
    {
        if (DateTime.UtcNow - exclusionsChecked > TimeSpan.FromSeconds(2))
        {
            exclusionsChecked = DateTime.UtcNow;
            try
            {
                var stamp = File.GetLastWriteTimeUtc(exclusionsPath);
                if (stamp != exclusionsStamp)
                {
                    exclusionsStamp = stamp;
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string line in File.ReadAllLines(exclusionsPath))
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

            try
            {
                var stamp = File.Exists(replacementsPath) ? File.GetLastWriteTimeUtc(replacementsPath) : DateTime.MinValue;
                if (stamp != replacementsStamp) LoadReplacements();
            }
            catch { }

            ignored.ReloadIfChanged();
            learned.ReloadIfChanged();
        }

        uint pid;
        W.GetWindowThreadProcessId(hwnd, out pid);
        string name;
        if (!processNames.TryGetValue(pid, out name))
        {
            try { name = Process.GetProcessById((int)pid).ProcessName; }
            catch { name = ""; }
            processNames[pid] = name;
        }
        return excluded.Contains(name);
    }

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

    // Held-back keystrokes are sent without the marker so the hook handles them as normal typing
    public static void SendKeys(int[] vks)
    {
        var arr = new INPUT[vks.Length * 2];
        for (int i = 0; i < vks.Length; i++)
        {
            arr[2 * i] = Key((ushort)vks[i], 0, 0);
            arr[2 * i + 1] = Key((ushort)vks[i], 0, 2);
            arr[2 * i].u.ki.dwExtraInfo = UIntPtr.Zero;
            arr[2 * i + 1].u.ki.dwExtraInfo = UIntPtr.Zero;
        }
        SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void SwitchLayout(IntPtr hwnd, IntPtr hkl)
    {
        if (hkl != IntPtr.Zero) PostMessage(hwnd, 0x0050, IntPtr.Zero, hkl); // WM_INPUTLANGCHANGEREQUEST
    }
}

// A word list in a text file next to the app: one word per line, case-insensitive.
// The file is re-read when it changes in Settings or in Notepad
class WordList
{
    readonly string path;
    readonly HashSet<string> words = new HashSet<string>();
    DateTime stamp;

    public WordList(string path)
    {
        this.path = path;
        ReloadIfChanged();
    }

    public bool Contains(string word) { return words.Contains(word.ToLowerInvariant()); }

    public void Add(string word)
    {
        string w = word.ToLowerInvariant();
        if (!words.Add(w)) return;
        try { File.AppendAllText(path, w + "\r\n", Encoding.UTF8); stamp = File.GetLastWriteTimeUtc(path); }
        catch { }
    }

    public void Remove(string word)
    {
        if (!words.Remove(word.ToLowerInvariant()) || !File.Exists(path)) return;
        try
        {
            var kept = new List<string>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                if (!string.Equals(line.Trim(), word, StringComparison.OrdinalIgnoreCase)) kept.Add(line);
            File.WriteAllLines(path, kept, Encoding.UTF8);
            stamp = File.GetLastWriteTimeUtc(path);
        }
        catch { }
    }

    public void ReloadIfChanged()
    {
        try
        {
            var current = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (current == stamp) return;
            stamp = current;
            words.Clear();
            if (File.Exists(path))
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string s = line.Trim();
                    if (s.Length > 0 && !s.StartsWith("#")) words.Add(s.ToLowerInvariant());
                }
        }
        catch { }
    }
}