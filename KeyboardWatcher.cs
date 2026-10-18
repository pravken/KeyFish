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
    struct KeyStroke
    {
        public int Vk; public bool Shift, Caps;
        public KeyStroke(int vk, bool shift, bool caps) { Vk = vk; Shift = shift; Caps = caps; }
    }

    class Correction
    {
        public string Original, Replaced;
        public List<string> OriginalWords;
        public int LastWordLength;           // length of the last corrected word on screen
        public int Backspaced;               // how many Backspaces were pressed right after the correction
        public int TerminatorVk;
        public bool OriginalRu;
        public IntPtr Hwnd;
    }

    // A recently typed word, kept so it can be corrected after the fact
    class WordRec
    {
        public List<KeyStroke> Keys;
        public string Shown;                 // how the word looks on screen now
        public bool TypedRu;
        public bool Changed;                 // KeyFish has already changed it
        public int TerminatorVk;
    }

    class Job
    {
        public int Backspaces;
        public string Text;
        public int TerminatorVk;
        public bool ToggleCaps;
        public string Layout;
        public IntPtr Hwnd;
        public WordRec TypoRec;              // not null: check the word for a typo (the space is already typed)
    }

    public volatile bool Enabled = true;
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
            if (value) ThreadPool.QueueUserWorkItem(_ => { try { CheckTypo("проверка", true); } catch { } });
        }
    }

    string CheckTypo(string word, bool ru)
    {
        lock (spellSync)
        {
            if (spell == null) spell = new SpellService();
            var model = ru ? Detector.Ru : Detector.En;
            return spell.Fix(word, ru ? "ru-RU" : "en-US", w => model == null ? 0 : model.Count(w));
        }
    }
    public bool Ready { get { return Detector.Ru != null; } }

    readonly object sync = new object();
    readonly List<KeyStroke> word = new List<KeyStroke>();
    bool partial;                        // the word didn't start at a word start (Backspace went into earlier text)
    readonly List<WordRec> history = new List<WordRec>();
    IntPtr wordHwnd;
    bool? context;                       // language of the previous word: true — Russian
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

        new Thread(() => Detector.LoadEmbedded()) { IsBackground = true }.Start();
        StartPasswordGuard();
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
            if (down && k.dwExtraInfo != W.Marker)
            {
                bool swallow;
                try { swallow = OnKey((int)k.vkCode); }
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

    bool OnKey(int vk)
    {
        lock (sync)
        {
            if (vk == 0x13 || IsModifier(vk)) return false; // Pause is undo; modifiers don't reset the word

            if (pending)
            {
                // While a correction is being typed, hold keystrokes back and replay them afterwards
                heldDuringPending.Add(vk);
                return true;
            }

            if (passwordFocused) guardSignal.Set(); // re-check: focus may have left the password field
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
            last = null;

            if (ctrl || Down(0x12) || Down(0x5B) || Down(0x5C)) { ResetAll(); return false; }

            IntPtr hwnd = W.GetForegroundWindow();
            if (hwnd != wordHwnd) { ResetAll(); wordHwnd = hwnd; context = null; }

            if (vk == 0x08)
            {
                if (word.Count > 0) word.RemoveAt(word.Count - 1);
                else { history.Clear(); partial = true; }
                return false;
            }

            if (KeyMap.IsCharKey(vk))
            {
                if (word.Count == 0) wordStartedAt = Environment.TickCount;
                if (word.Count < 40) word.Add(new KeyStroke(vk, Down(0x10), (W.GetKeyState(0x14) & 1) != 0));
                guardSignal.Set();
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
    public void Learn(string typed, string result, bool resultRu)
    {
        string w = typed.Trim();
        if (w.Length == 0 || w.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0 || w.Length > 40 || !Ready) return;
        // Learn only when the result is a real word: converting a correct word by accident teaches nothing
        string core = Detector.Core(result.Trim());
        if (string.IsNullOrEmpty(core) || (resultRu ? Detector.Ru : Detector.En).Count(core) == 0) return;
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
        W.SwitchLayout(c.Hwnd, c.OriginalRu ? "00000419" : "00000409");
        context = c.OriginalRu;
    }

    void PushHistory(WordRec rec)
    {
        history.Add(rec);
        if (history.Count > 5) history.RemoveAt(0);
    }

    // A confidently corrected word: long enough and in the dictionary
    static bool IsStrong(string text, bool ru)
    {
        string core = Detector.Core(text);
        return core != null && core.Length >= 3 && (ru ? Detector.Ru : Detector.En).Count(core) > 0;
    }

    bool OnWordEnd(int terminatorVk, IntPtr hwnd)
    {
        bool wasPartial = partial;
        partial = false;
        if (word.Count == 0) return false;
        if (wasPartial || word.Count >= 40 || passwordFocused || !FocusVerified || IsOwnWindow(hwnd) || IsExcluded(hwnd) || IsPasswordField(hwnd) ||
            (QuietFullscreen && IsFullscreen(hwnd))) { history.Clear(); return false; }

        int lang = W.LayoutLanguage(hwnd);
        if (lang != 0x19 && lang != 0x09) { history.Clear(); return false; }
        bool typedRu = lang == 0x19;

        string typed = Build(word, typedRu), alt = Build(word, !typedRu);
        foreach (char c in typed)
            if (char.IsDigit(c) || c == '@' || c == '_') { history.Clear(); return false; }

        var rec = new WordRec { Keys = new List<KeyStroke>(word), Shown = typed, TypedRu = typedRu, TerminatorVk = terminatorVk };

        // Replacement: an abbreviation from replacements.txt, typed in either layout
        bool viaAlt = false;
        string expansion = FindReplacement(typed);
        if (expansion == null) { expansion = FindReplacement(alt); viaAlt = expansion != null; }
        if (expansion != null)
        {
            rec.Shown = expansion;
            rec.Changed = true;
            PushHistory(rec);
            if (viaAlt) context = !typedRu;
            last = new Correction
            {
                Original = typed, Replaced = expansion, OriginalWords = new List<string>(),
                LastWordLength = expansion.Length, TerminatorVk = terminatorVk, OriginalRu = typedRu, Hwnd = hwnd
            };
            pending = true;
            Interlocked.Increment(ref expansionCount);
            jobs.Add(new Job
            {
                Backspaces = word.Count, Text = expansion, TerminatorVk = terminatorVk,
                Layout = viaAlt ? (typedRu ? "00000409" : "00000419") : null, Hwnd = hwnd
            });
            return true;
        }

        bool convert = !IsIgnored(typed) && (learned.Contains(typed) || Detector.ShouldConvert(typed, alt, typedRu, context));
        string result = convert ? alt : typed;
        bool resultRu = convert ? !typedRu : typedRu;

        bool capsMistake;
        string fixedCase = FixCase(result, out capsMistake);
        if (!convert && IsIgnored(fixedCase)) fixedCase = result;

        string core = Detector.Core(result);
        if (convert || (!string.IsNullOrEmpty(core) && (resultRu ? Detector.Ru : Detector.En).Count(core) > 0))
            context = resultRu;

        // A word was confidently corrected, so check the short words before it: "f vj;yj" → "а можно"
        var retro = new List<WordRec>();
        if (convert && IsStrong(alt, resultRu))
        {
            for (int i = history.Count - 1; i >= 0 && retro.Count < 3; i--)
            {
                var p = history[i];
                if (p.TerminatorVk != 0x20 || p.Changed || p.TypedRu != typedRu || IsIgnored(p.Shown)) break;
                if (!Detector.ShouldConvert(p.Shown, Build(p.Keys, !typedRu), typedRu, resultRu)) break;
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
            string pAlt = Build(p.Keys, !typedRu);
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
            LastWordLength = fixedCase.Length, TerminatorVk = terminatorVk, OriginalRu = typedRu, Hwnd = hwnd
        };
        pending = true;
        Interlocked.Increment(ref correctionCount);
        jobs.Add(new Job
        {
            Backspaces = backspaces,
            Text = replaced.ToString(),
            TerminatorVk = terminatorVk,
            ToggleCaps = capsMistake && (W.GetKeyState(0x14) & 1) != 0,
            Layout = convert ? (resultRu ? "00000419" : "00000409") : null,
            Hwnd = hwnd
        });
        return true;
    }

    // Undo the last correction (Pause right after it). false means there is nothing to undo
    public bool Undo()
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
            Layout = c.OriginalRu ? "00000419" : "00000409",
            Hwnd = c.Hwnd
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
        string fixedWord = CheckTypo(core, rec.TypedRu);
        if (fixedWord == null || fixedWord == core || start < 0) return;

        string text = typed.Substring(0, start) + fixedWord + typed.Substring(start + core.Length);
        lock (sync)
        {
            if (W.GetForegroundWindow() != job.Hwnd) return;
            last = new Correction
            {
                Original = typed, Replaced = text, OriginalWords = new List<string> { typed },
                LastWordLength = text.Length, TerminatorVk = job.TerminatorVk, OriginalRu = rec.TypedRu, Hwnd = job.Hwnd
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
                pending = false;
                held = heldDuringPending.ToArray();
                heldDuringPending.Clear();
            }
            // Replay the keystrokes held back during typing, now as normal input
            if (held.Length > 0) W.SendKeys(held);
        }
    }

    static void Execute(Job job)
    {
        var inputs = new List<W.INPUT>();
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
        if (job.Layout != null) W.SwitchLayout(job.Hwnd, job.Layout);
    }

    static string Build(List<KeyStroke> keys, bool ru)
    {
        var sb = new StringBuilder(keys.Count);
        foreach (var k in keys) sb.Append(KeyMap.CharFor(k.Vk, k.Shift, k.Caps, ru));
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

    // Focus is in a password field (any app, browsers included): then nothing is corrected or remembered
    volatile bool passwordFocused;

    readonly AutoResetEvent guardSignal = new AutoResetEvent(false);
    volatile int focusCheckedAt, wordStartedAt;
    IntPtr passwordWindow;               // the window where the password field was found; the flag clears when it loses the foreground

    // Focus was checked after this word started; otherwise we can't know it isn't a password
    bool FocusVerified { get { return focusCheckedAt - wordStartedAt >= 0; } }

    // Check what has focus on focus changes and while typing. Events alone aren't enough: a browser reports
    // the password field late, so at event time the focus is still the browser window, not the field
    void StartPasswordGuard()
    {
        var t = new Thread(() =>
        {
            try { System.Windows.Automation.Automation.AddAutomationFocusChangedEventHandler((s, e) => guardSignal.Set()); }
            catch { }
            while (true)
            {
                guardSignal.WaitOne();
                IntPtr foreground = W.GetForegroundWindow();
                bool password = passwordFocused && foreground == passwordWindow;
                try
                {
                    var element = System.Windows.Automation.AutomationElement.FocusedElement;
                    if (element != null)
                    {
                        var info = element.Current;
                        var type = info.ControlType;
                        // Only a text field can change the decision. Browser popups ("suggest a strong password",
                        // autofill) take focus for a moment, and at first the browser reports the whole page
                        // (Document) as focused; neither means the password field was left
                        if (info.IsPassword) password = true;
                        else if (type == System.Windows.Automation.ControlType.Edit ||
                                 type == System.Windows.Automation.ControlType.ComboBox) password = false;
                    }
                }
                catch { }
                if (password) passwordWindow = foreground;
                passwordFocused = password;
                focusCheckedAt = Environment.TickCount;
                if (password) lock (sync) { ResetAll(); last = null; }
                Thread.Sleep(100); // at most ten times a second; keystrokes in between merge into one check
            }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
    }

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

// What a key types in the US and ЙЦУКЕН layouts
static class KeyMap
{
    const string Digits = "1234567890", EnShiftDigits = "!@#$%^&*()", RuShiftDigits = "!\"№;%:?*()";
    static readonly Dictionary<int, string> EnOem = new Dictionary<int, string>
    {
        { 0xBA, ";:" }, { 0xBB, "=+" }, { 0xBC, ",<" }, { 0xBD, "-_" }, { 0xBE, ".>" }, { 0xBF, "/?" },
        { 0xC0, "`~" }, { 0xDB, "[{" }, { 0xDC, "\\|" }, { 0xDD, "]}" }, { 0xDE, "'\"" }
    };
    static readonly Dictionary<int, string> RuOem = new Dictionary<int, string>
    {
        { 0xBA, "жЖ" }, { 0xBB, "=+" }, { 0xBC, "бБ" }, { 0xBD, "-_" }, { 0xBE, "юЮ" }, { 0xBF, ".," },
        { 0xC0, "ёЁ" }, { 0xDB, "хХ" }, { 0xDC, "\\/" }, { 0xDD, "ъЪ" }, { 0xDE, "эЭ" }
    };

    public static bool IsCharKey(int vk)
    {
        return (vk >= 0x41 && vk <= 0x5A) || (vk >= 0x30 && vk <= 0x39) || EnOem.ContainsKey(vk);
    }

    public static char CharFor(int vk, bool shift, bool caps, bool ru)
    {
        if (vk >= 0x30 && vk <= 0x39)
        {
            int i = Digits.IndexOf((char)vk);
            return shift ? (ru ? RuShiftDigits : EnShiftDigits)[i] : Digits[i];
        }
        if (vk >= 0x41 && vk <= 0x5A)
        {
            char en = (char)(vk + 32);
            char c = ru ? Converter.Convert(en.ToString(), true)[0] : en;
            return shift ^ caps ? char.ToUpper(c) : c;
        }
        string pair = (ru ? RuOem : EnOem)[vk];
        bool letter = char.IsLetter(pair[0]);
        return pair[(letter ? shift ^ caps : shift) ? 1 : 0];
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
    [DllImport("user32.dll")] static extern IntPtr LoadKeyboardLayout(string klid, uint flags);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static int LayoutLanguage(IntPtr hwnd)
    {
        uint pid;
        uint tid = GetWindowThreadProcessId(hwnd, out pid);
        return (int)(GetKeyboardLayout(tid).ToInt64() & 0x3FF); // primary language: 0x09 — English, 0x19 — Russian
    }

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

    public static void SwitchLayout(IntPtr hwnd, string klid)
    {
        IntPtr hkl = LoadKeyboardLayout(klid, 0);
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