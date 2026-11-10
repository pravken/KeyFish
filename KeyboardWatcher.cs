using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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
        public string Promote;               // fixed because it was fixed by hand once: kept, it goes to Always correct
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

    // Why words were left alone, for "Why wasn't it corrected?" in the tray menu: the last five of the last
    // 15 minutes, in memory only. Ordinary words typed right aren't kept, and a word is kept only where it helps put
    // things right (it is in Never correct, or in another layout it reads as a real word); never in a password field
    public class Verdict { public DateTime At; public string App, Word, Reason; }
    readonly List<Verdict> verdicts = new List<Verdict>();
    public List<Verdict> Verdicts
    {
        get
        {
            lock (verdicts)
            {
                verdicts.RemoveAll(v => DateTime.Now - v.At > TimeSpan.FromMinutes(15));
                return new List<Verdict>(verdicts);
            }
        }
    }

    void Explain(IntPtr hwnd, string reason, string word = null)
    {
        string app = W.ProcessName(hwnd);
        lock (verdicts)
        {
            // Every word in an excluded app or a game has the same reason: one line for them, the time moving on
            var top = verdicts.Count > 0 ? verdicts[0] : null;
            if (top != null && top.App == app && top.Reason == reason && top.Word == word) { top.At = DateTime.Now; return; }
            verdicts.Insert(0, new Verdict { At = DateTime.Now, App = app, Word = word, Reason = reason });
            if (verdicts.Count > 5) verdicts.RemoveAt(5);
        }
    }

    // Everything KeyFish remembers about recent typing, gone at once (the session was locked)
    public void ClearMemory()
    {
        lock (sync) { ResetAll(); last = null; erasedKeys = null; eraseSnapshot = null; eraseRec = null; recent.Clear(); }
        lock (verdicts) verdicts.Clear();
    }

    // From "Why wasn't it corrected?": take a word out of Never correct, or teach KeyFish to correct it
    public void Unignore(string word) { lock (sync) ignored.Remove(word); }
    public void LearnWord(string word) { lock (sync) { learned.Add(word); learnedOnce.Remove(WordList.Normalize(word)); ignored.Remove(word); } }
    public void Unlearn(string word) { lock (sync) { learned.Remove(word.Trim()); learnedOnce.Remove(WordList.Normalize(word)); } }

    // Words fixed by hand once in this session (with Pause, or erased and typed again in the other layout): corrected
    // for the rest of it, and written to Always correct only when fixed by hand a second time or when the next
    // automatic fix of it is kept, so that one fix made by accident doesn't stay for good (as xneur does).
    // Kept as WordList.Normalize gives them; used under sync
    readonly HashSet<string> learnedOnce = new HashSet<string>();

    void LearnFromFix(string word)
    {
        string w = WordList.Normalize(word);
        if (w.Length == 0) return;
        ignored.Remove(word);
        if (learned.Contains(word)) return;
        if (learnedOnce.Remove(w)) learned.Add(word);
        else learnedOnce.Add(w);
    }

    bool IsLearned(string word) { return learned.Contains(word) || learnedOnce.Contains(WordList.Normalize(word)); }

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
        public List<LayoutTable> Layouts;    // per key, when the layout was switched mid-word
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
        public int Serial;                   // keySerial when the typo job was made
        public bool ReleaseCtrl;             // lift the held Ctrl while typing (undo via Ctrl+Z)
        public int CtrlComboVk;              // not 0: send Ctrl + this key (Ctrl is still held after an undo)
        public int VerifyFrom;               // not 0: typed only once a check of the focus made after this time finds no password field
        public bool Expansion;               // a replacement, not a fix: what is counted back if it isn't typed
        public bool Placeholders;            // a replacement with {date}, {time}, {clipboard} or {cursor}: filled in when typed
        public int CaretBack;                // Left presses after typing: the caret goes where {cursor} was
    }

    public volatile bool Enabled = true;
    public volatile bool UndoWithCtrlZ = true;
    volatile bool ctrlHeld;              // the physical Ctrl key is down
    volatile bool ctrlLifted;            // an undo lifted Ctrl logically while it is still physically held
    // Fixed mistakes (layout, case, typos) and expanded replacements, for statistics and the fish blink
    int correctionCount, expansionCount;
    public int CorrectionCount { get { return Volatile.Read(ref correctionCount); } }
    public int ExpansionCount { get { return Volatile.Read(ref expansionCount); } }
    // Of the corrections, typos fixed; and fixes taken back (Pause, Ctrl+Z): which sound to play
    public int TypoCount { get { return Volatile.Read(ref typoCount); } }
    public int UndoCount { get { return Volatile.Read(ref undoCount); } }
    int typoCount, undoCount;
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
            // Windows loads a spell checker on first use: do that now for each installed language, not on the first word
            if (value) ThreadPool.QueueUserWorkItem(_ =>
            {
                foreach (var t in Layouts.All)
                    try { CheckTypo("test", t.Lang); } catch { }
            });
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

    // Typos in a text, fixed the way they are while typing: a word that none of the word lists of its alphabet's
    // installed languages knows, and that the Windows spell checker of one of them fixes with confidence. The case kept
    public string FixTyposIn(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text, @"\p{L}{3,}", m =>
        {
            string word = m.Value, lower = word.ToLowerInvariant();
            string script = LayoutTable.ScriptOf(word[0]);
            var langs = new List<string>();
            foreach (var t in Layouts.All)
                if (t.Script == script && t.Lang != null && !langs.Contains(t.Lang)) langs.Add(t.Lang);
            foreach (string lang in langs)
            {
                var model = Detector.Load(lang);
                if (model != null && model.Count(lower) > 0) return word;
            }
            foreach (string lang in langs)
            {
                string fix = CheckTypo(word, lang);
                if (fix == null || fix.Length == 0) continue;
                if (word.ToUpperInvariant() == word && word.Length > 1) return fix.ToUpperInvariant();
                return char.IsUpper(word[0]) ? char.ToUpperInvariant(fix[0]) + fix.Substring(1) : fix;
            }
            return word;
        });
    }

    // The installed keyboard layouts and what each key types in them
    public readonly LayoutSet Layouts = new LayoutSet();

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
    readonly List<LayoutTable> wordLayouts = new List<LayoutTable>();
    bool overflow;                       // the word had more than 40 keys: not all of them are in `word`   // the layout each key of the word was typed in

    // A word the user erased whole, kept for a while: typed again with the same keys in a layout of another alphabet,
    // it is a manual layout fix, and KeyFish learns it like a fix made with Pause
    List<KeyStroke> erasedKeys, eraseSnapshot;
    List<LayoutTable> erasedLayouts, eraseSnapshotLayouts;
    int erasedAt, eraseCount;
    WordRec eraseRec;
    bool partial;                        // the word didn't start at a word start (Backspace went into earlier text)
    readonly List<WordRec> history = new List<WordRec>();
    IntPtr wordHwnd;
    string context;                      // language of the previous word, or null if unknown
    Correction last;                     // the last correction; Pause can undo it
    readonly WordList ignored;           // words whose correction was undone or erased
    readonly WordList learned;           // words the user fixed manually with Pause
    bool pending;                        // a correction is still being typed
    int pendingSince;
    int keySerial;                       // counts keys typed; a typo check is applied only if nothing was typed since

    void SetPending() { pending = true; pendingSince = Environment.TickCount; noJobs = false; }
    readonly List<HeldKey> heldDuringPending = new List<HeldKey>();
    // Held keys are replayed while pending stays on: a key pressed meanwhile would otherwise get in ahead of them
    // ("привте"). pending ends once the hook has seen the last of them; keys pressed meanwhile are held and replayed
    // next. All under sync
    int replayLeft;                      // replayed key presses the hook hasn't seen yet
    int replayInsertAt;                  // replayed keys held again (a correction came first) go here, before newer ones
    bool noJobs = true;                  // no correction is queued or being typed

    // Replays what was held, or ends pending if nothing was
    void StartReplay()
    {
        if (heldDuringPending.Count == 0) { pending = false; replayLeft = 0; return; }
        var keys = heldDuringPending.ToArray();
        heldDuringPending.Clear();
        pending = true;
        pendingSince = Environment.TickCount;
        replayLeft = keys.Length;
        replayInsertAt = 0;
        ThreadPool.QueueUserWorkItem(_ => { try { W.SendHeld(keys); } catch { } });
    }
    readonly BlockingCollection<Job> jobs = new BlockingCollection<Job>();

    // The files below are re-read on the file thread (see FileQueue), never in the keyboard hook
    readonly string replacementsPath;
    volatile Dictionary<string, string> replacements = new Dictionary<string, string>();
    DateTime replacementsStamp;

    readonly string exclusionsPath;
    volatile AppRules excluded = new AppRules();
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
        noAutoPath = Path.Combine(Path.GetDirectoryName(exclusionsFile), "no-auto-layouts.txt");
        ignored = new WordList(ignoredFile);
        learned = new WordList(learnedFile);
        ReloadFiles();
        reloadTimer = new System.Threading.Timer(_ => FileQueue.Run(ReloadFiles), null, 2000, 2000);
        keyboardProc = KeyboardHook;
        mouseProc = MouseHook;

        try { guard = new PasswordGuardClient(); }
        catch (Exception ex) { CrashLog.Write("the password-field check couldn't start", ex); }   // nothing is corrected then, and the fish is grey
        new Thread(Worker) { IsBackground = true }.Start();
        // Above the rest of KeyFish: Windows drops a low-level hook that answers too slowly, and a busy computer is
        // when that happens
        new Thread(() =>
        {
            InstallHooks();
            var watchdog = new System.Windows.Forms.Timer { Interval = 2000 };
            // An error must not leave this thread: WinForms would show its error window here, and the hooks would stop
            watchdog.Tick += (s, e) => { try { CheckHooks(); } catch (Exception ex) { CrashLog.Write("error", ex); } };
            watchdog.Start();
            Application.Run();
        }) { IsBackground = true, Priority = ThreadPriority.Highest }.Start();
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
        if (diagnose) perf.Tick(this);
        bool lost = keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero || seen - lastHookEvent > 1000;
        if (!lost || Environment.TickCount - lastReinstall < 10000) return;
        lastReinstall = Environment.TickCount;
        lastHookEvent = seen;   // this input is accounted for: only newer missed input counts next time
        InstallHooks();
    }

    // Called on the hook thread for every key and mouse click: KeyFish's tray icon wakes up to look at the layout
    // (it is switched with keys, or by clicking the taskbar). Must be quick and must not throw
    public Action Activity;

    void Notify()
    {
        var a = Activity;
        if (a != null) try { a(); } catch { }
    }

    IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        lastHookEvent = Environment.TickCount;
        int msg = wParam.ToInt32();
        if (code >= 0 && (msg == 0x0201 || msg == 0x0204 || msg == 0x0207)) // L/R/M button down
        {
            Notify();
            lastShiftTap = 0;
            // A click may move the caret, so what is being typed starts over. A click in another window (the fix's
            // notification, the taskbar) leaves the caret where it was: the last fix can still be taken back there
            var at = new W.PT { X = Marshal.ReadInt32(lParam, 0), Y = Marshal.ReadInt32(lParam, 4) };
            IntPtr clicked = W.GetAncestor(W.WindowFromPoint(at), 2);   // GA_ROOT
            lock (sync)
            {
                ResetAll();
                erasedKeys = null;
                if (last != null && clicked == last.Hwnd) last = null;
            }
        }
        return W.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        lastHookEvent = Environment.TickCount;
        if (code >= 0)
        {
            bool timed = diagnose;
            long started = timed ? Stopwatch.GetTimestamp() : 0;
            bool swallow = OnHookKey(wParam, lParam);
            if (timed) perf.Hook(Stopwatch.GetTimestamp() - started);
            if (swallow) return (IntPtr)1;
        }
        return W.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    // True: the key is swallowed
    bool OnHookKey(IntPtr wParam, IntPtr lParam)
    {
        {
            Notify();
            // The fields are read in place: copying the structure would allocate on every key, down and up
            var k = new W.KBDLLHOOKSTRUCT
            {
                vkCode = (uint)Marshal.ReadInt32(lParam, 0),
                scanCode = (uint)Marshal.ReadInt32(lParam, 4),
                flags = (uint)Marshal.ReadInt32(lParam, 8),
                dwExtraInfo = IntPtr.Size == 8 ? new UIntPtr((ulong)Marshal.ReadInt64(lParam, 16)) : new UIntPtr((uint)Marshal.ReadInt32(lParam, 16))
            };
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
                if (swallowQuick) return true;
            }
            if (down && k.dwExtraInfo != W.Marker)
            {
                bool swallow;
                try { swallow = OnKey((int)k.vkCode, (int)k.scanCode, (k.flags & 1) != 0, k.dwExtraInfo == W.ReplayMarker); }   // LLKHF_EXTENDED
                catch { swallow = false; }
                if (swallow) return true;
            }
        }
        return false;
    }

    static bool IsModifier(int vk)
    {
        return (vk >= 0x10 && vk <= 0x12) || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C || vk == 0x14;
    }

    static bool Down(int vk) { return (W.GetAsyncKeyState(vk) & 0x8000) != 0; }

    // extended: an extended key (the arrows and Insert/Delete/Home/End beside the letters, not on the numpad)
    // replay: a key held back during a correction, replayed now
    bool OnKey(int vk, int scan, bool extended = false, bool replay = false)
    {
        lock (sync)
        {
            if (!replay || replayLeft == 0 || vk == 0x13 || IsModifier(vk)) return OnKeyCore(vk, scan, extended, false);
            replayLeft--;
            bool swallow;
            if (noJobs) swallow = OnKeyCore(vk, scan, extended, true);
            else
            {
                // A correction was queued after it was held (a replayed space ended a word): back in the queue, in its place
                keySerial++;
                heldDuringPending.Insert(Math.Min(replayInsertAt++, heldDuringPending.Count), Held(vk, scan, extended));
                swallow = true;
            }
            if (replayLeft == 0 && noJobs) StartReplay();
            return swallow;
        }
    }

    HeldKey Held(int vk, int scan, bool extended)
    {
        return new HeldKey { Vk = vk, Scan = scan, Extended = extended, Shift = Down(0x10), Ctrl = Down(0x11), Alt = Down(0x12), Win = Down(0x5B) || Down(0x5C) };
    }

    bool OnKeyCore(int vk, int scan, bool extended, bool replayed)
    {
        lock (sync)
        {
            if (vk == 0x13 || IsModifier(vk)) return false; // Pause is undo; modifiers don't reset the word
            keySerial++;

            // A correction that hasn't finished in 3 seconds is stuck (its thread failed): stop holding keystrokes,
            // give back the ones held so far, rather than swallow all typing until KeyFish is closed
            if (pending && Environment.TickCount - pendingSince > 3000)
            {
                pending = false;
                replayLeft = 0;
                noJobs = true;
                var stuck = heldDuringPending.ToArray();
                heldDuringPending.Clear();
                if (stuck.Length > 0) ThreadPool.QueueUserWorkItem(_ => { try { W.SendHeld(stuck); } catch { } });
            }

            // Undo via Ctrl+Z lifted Ctrl while the user still holds it: this key must arrive with Ctrl,
            // so it is swallowed and sent again together with Ctrl (holding Ctrl and pressing Z twice).
            // Jobs run in order, so even during the undo itself this lands right after it
            if (ctrlLifted && ctrlHeld)
            {
                ctrlLifted = false;
                SetPending();
                jobs.Add(new Job { CtrlComboVk = vk });
                return true;
            }

            if (pending && !replayed)
            {
                // While a correction is being typed, hold keystrokes back and replay them afterwards,
                // with the modifiers held right now: by the replay the user may have let go of Shift
                heldDuringPending.Add(Held(vk, scan, extended));
                return true;
            }

            if (passwordFocused) RequestFocusCheck(); // re-check: focus may have left the password field
            if (!Enabled || passwordFocused) { ResetAll(); last = null; return false; }

            bool ctrl = Down(0x11);
            if (vk == 0x08 && last != null && last.TerminatorVk == 0x0D) last = null;
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
            // Typing goes on after a fix: it is kept
            if (last != null && last.Promote != null) LearnFromFix(last.Promote);
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
            // Win+Space switches the layout (Windows 10 and 11): the word goes on, like after Alt+Shift
            if (vk == 0x20 && (Down(0x5B) || Down(0x5C)) && !ctrl && !Down(0x12)) return false;
            if (!(altGr && typesChar) && (ctrl || Down(0x12) || Down(0x5B) || Down(0x5C))) { ResetAll(); return false; }

            if (hwnd != wordHwnd) { ResetAll(); wordHwnd = hwnd; context = null; erasedKeys = null; }

            if (vk == 0x08)
            {
                if (word.Count > 0)
                {
                    // The whole word may be on its way out: remember it as it was
                    if (eraseSnapshot == null) { eraseSnapshot = new List<KeyStroke>(word); eraseSnapshotLayouts = new List<LayoutTable>(wordLayouts); }
                    RemoveLastKey();
                    if (word.Count == 0 && switchedEarly) EarlyErased(hwnd);
                    else if (word.Count == 0) Erased(eraseSnapshot, eraseSnapshotLayouts);
                }
                else
                {
                    // A finished word and the space after it, erased letter by letter
                    var rec = history.Count > 0 ? history[history.Count - 1] : null;
                    if (eraseRec == null && rec != null && !rec.Changed) { eraseRec = rec; eraseCount = 0; }
                    history.Clear();
                    if (eraseRec != null && ++eraseCount == eraseRec.Shown.Length + 1)
                    {
                        Erased(eraseRec.Keys, eraseRec.Layouts);
                        eraseRec = null;
                        partial = false;   // the caret is back where that word began
                        return false;
                    }
                    if (eraseRec == null || eraseCount > eraseRec.Shown.Length + 1) erasedKeys = null;
                    partial = true;
                }
                return false;
            }

            if (typesChar)
            {
                if (word.Count == 0) wordStartedAt = Environment.TickCount;
                else eraseSnapshot = null;   // a letter in the middle of erasing: the word is being edited, not erased
                eraseRec = null;
                if (word.Count < 40) { word.Add(new KeyStroke(scan, shift, caps, altGr)); wordLayouts.Add(layout); }
                else overflow = true;
                RequestWordCheck();
                // From the 4th to the 6th letter: a beginning plainly in the wrong layout is fixed now. The key is
                // swallowed and typed with the rest, in the right order
                if (SwitchWhileTyping && !switchedEarly && word.Count >= 4 && word.Count <= (earlyWaited ? 8 : 6) && SwitchEarly(hwnd, layout)) return true;
                return false;
            }

            if (vk == 0x20 || vk == 0x0D || vk == 0x09) // Space, Enter, Tab
            {
                bool swallow = OnWordEnd(vk, hwnd);
                ClearWord();
                erasedKeys = null;
                return swallow;
            }

            ResetAll(); // arrows, Delete, Home and so on: the caret position is unknown
            return false;
        }
    }

    void ResetAll()
    {
        ClearWord();
        history.Clear();
        partial = false;
        eraseRec = null;
        // erasedKeys stays: switching the layout with Win+Space passes through here
    }

    void ClearWord()
    {
        word.Clear();
        overflow = false;
        wordLayouts.Clear();
        eraseSnapshot = null;
        switchedEarly = false;
        earlyWaited = false;
    }

    // Switching the layout while a word is typed (Settings → Correction): its beginning is retyped in the other layout,
    // which stays on for the rest of it. The word as a whole is checked when it ends, as any other: a beginning switched
    // by mistake is switched back then. Erased right away, it is switched back and not switched again this session
    public volatile bool SwitchWhileTyping = true;
    bool switchedEarly;   // done once per word
    LayoutTable earlyFrom;   // the layout the word was begun in
    int earlyLength;   // how many keys it was switched after
    bool earlyWaited;   // the password check hadn't answered yet: tried up to the 8th letter
    readonly List<string> rejectedEarly = new List<string>();   // beginnings switched and erased at once, lower case

    // true: the key just added is swallowed, the job types it
    bool SwitchEarly(IntPtr hwnd, LayoutTable current)
    {
        if (partial || overflow || current == null || Layouts.All.Count < 2) return false;
        foreach (var l in wordLayouts) if (l != current) return false;   // already switched by hand in this word
        var T = Detector.Loaded(current.Lang);
        if (T == null || guard == null) return false;
        bool password; int checkedAt;
        guard.Read(out password, out checkedAt);
        if (password) return false;
        if (checkedAt == 0 || checkedAt - wordStartedAt < 0) { earlyWaited = true; return false; }
        var lastKey = word[word.Count - 1];
        if (current.IsDead(lastKey)) return false;
        string typed = Shown(word, wordLayouts, current);
        if (Detector.LooksLikeCode(typed) || ignored.AnyStartsWith(typed)) return false;   // a word in Never correct may be on its way
        foreach (char c in typed) if (char.IsDigit(c) || c == '@' || c == '_') return false;
        string lower = typed.ToLowerInvariant();
        foreach (string r in rejectedEarly) if (lower.StartsWith(r) || r.StartsWith(lower)) return false;
        // The model first: the window checks cost system calls, and most beginnings go no further
        var fits = new List<LayoutTable>();
        foreach (var t in Layouts.All)
        {
            if (t.Script == current.Script || t.IsDead(lastKey) || !AutoTarget(t)) continue;
            var A = Detector.Loaded(t.Lang);
            if (A != null && Detector.ShouldSwitchEarly(typed, Build(word, t), T, A, context)) fits.Add(t);
        }
        if (fits.Count == 0) return false;
        if (IsOwnWindow(hwnd) || IsExcluded(hwnd) || W.HasMoreRights(hwnd) || IsPasswordField(hwnd) || (QuietFullscreen && IsFullscreen(hwnd))) return false;
        // Russian or Ukrainian: the one used last, as at the end of a word
        var to = fits[Math.Max(0, MostRecent(fits))];
        string alt = Build(word, to);
        int onScreen = Shown(word.GetRange(0, word.Count - 1), wordLayouts.GetRange(0, word.Count - 1), current).Length;   // the swallowed key isn't there
        switchedEarly = true;
        earlyFrom = current;
        earlyLength = word.Count;
        for (int i = 0; i < wordLayouts.Count; i++) wordLayouts[i] = to;   // the screen now shows the word in it
        UsedLayout(to.Hkl);
        Diagnose(hwnd, "switched while typing");
        SetPending();
        jobs.Add(new Job { Backspaces = onScreen, Text = alt, Layout = to.Hkl, Hwnd = hwnd });
        return true;
    }

    // A beginning switched while typing was erased at once: it was not meant. The layout goes back
    void EarlyErased(IntPtr hwnd)
    {
        switchedEarly = false;
        if (eraseSnapshot != null && earlyFrom != null)
        {
            string begun = Build(eraseSnapshot.GetRange(0, Math.Min(earlyLength, eraseSnapshot.Count)), earlyFrom).ToLowerInvariant();
            if (begun.Length >= 4 && !rejectedEarly.Contains(begun)) rejectedEarly.Add(begun);
            if (rejectedEarly.Count > 100) rejectedEarly.RemoveAt(0);
            Diagnose(hwnd, "switch while typing erased: switched back");
            SetPending();
            jobs.Add(new Job { Text = "", Layout = earlyFrom.Hkl, Hwnd = hwnd });
        }
        eraseSnapshot = null;
    }

    void RemoveLastKey()
    {
        word.RemoveAt(word.Count - 1);
        wordLayouts.RemoveAt(wordLayouts.Count - 1);
        // An accented letter typed with a dead key is one character on screen: Backspace erases both keys
        if (word.Count > 0 && wordLayouts[word.Count - 1] != null && wordLayouts[word.Count - 1].IsDead(word[word.Count - 1])) RemoveLastKey();
    }

    void Erased(List<KeyStroke> keys, List<LayoutTable> layouts)
    {
        erasedKeys = new List<KeyStroke>(keys);
        erasedLayouts = layouts == null ? null : new List<LayoutTable>(layouts);
        erasedAt = Environment.TickCount;
        eraseSnapshot = null;
    }

    // The word as it is on screen: each key in the layout it was typed in
    static string Shown(List<KeyStroke> keys, List<LayoutTable> layouts, LayoutTable fallback)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < keys.Count)
        {
            var t = (layouts != null && i < layouts.Count ? layouts[i] : null) ?? fallback;
            int j = i;
            while (j < keys.Count && ((layouts != null && j < layouts.Count ? layouts[j] : null) ?? fallback) == t) j++;
            sb.Append(t.Type(keys.GetRange(i, j - i)));
            i = j;
        }
        return sb.ToString();
    }

    static bool SameKeys(List<KeyStroke> a, List<KeyStroke> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i].Scan != b[i].Scan) return false;
        return true;
    }

    bool IsIgnored(string s) { return ignored.Contains(s.ToLowerInvariant()); }

    // Settings were just saved: read the files again now, not on the next tick
    public void ReloadSoon() { FileQueue.Run(ReloadFiles); }

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
                excluded = AppRules.Parse(File.Exists(exclusionsPath) ? File.ReadAllLines(exclusionsPath, Encoding.UTF8) : new string[0]);
            }
            stamp = File.Exists(noAutoPath) ? File.GetLastWriteTimeUtc(noAutoPath) : DateTime.MinValue;
            if (stamp != noAutoStamp)
            {
                noAutoStamp = stamp;
                var ids = new HashSet<long>();
                if (File.Exists(noAutoPath))
                    foreach (string line in File.ReadAllLines(noAutoPath, Encoding.UTF8))
                    {
                        long id;
                        if (long.TryParse(line.Trim(), NumberStyles.HexNumber, null, out id)) ids.Add(id);
                    }
                NoAutoLayouts = ids;
            }
        }
        catch { }

        diagnose = File.Exists(diagnosePath);
        ignored.ReloadIfChanged();
        learned.ReloadIfChanged();
    }

    public bool Typography;   // optional: a dash between words, an ellipsis, © ® ™

    // The typographic mark for a token typed on its own before a space, or null: "-" or "--" between words is a dash
    // (not at the start of a line, where it begins a list), (c) (r) (tm) are © ® ™
    string Typographic(string token, int terminatorVk)
    {
        if (!Typography || terminatorVk != 0x20) return null;
        if (token == "-" || token == "--")
            return history.Count > 0 && history[history.Count - 1].TerminatorVk == 0x20 ? "\u2014" : null;
        switch (token.ToLowerInvariant())
        {
            case "(c)": case "(с)": return "\u00A9";
            case "(r)": return "\u00AE";
            case "(tm)": return "\u2122";
        }
        return null;
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
        learnedOnce.Remove(WordList.Normalize(s));
    }

    // Selected text retyped in a layout of another alphabet: the same keys, as if pressed there.
    // Among several such layouts, the one whose dictionary knows most of the words wins,
    // ties go to the one used last. Returns null if no installed layout fits
    public string ConvertText(string text, IntPtr hwnd, out LayoutTable target)
    {
        LayoutTable source;
        return ConvertText(text, hwnd, out target, out source);
    }

    // source: the layout the text was typed in, as KeyFish figured it out
    public string ConvertText(string text, IntPtr hwnd, out LayoutTable target, out LayoutTable source)
    {
        target = null;
        source = null;
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
        source = Layouts.Get(W.LayoutHkl(hwnd));
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
            string s = Retype(text, source, t);
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

    // The text as the same keys type it in another layout; characters that layout can't make stay as they are
    public static string Retype(string text, LayoutTable from, LayoutTable to)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            var keys = from.Keys(ch.ToString());
            string r = keys == null ? null : to.Type(keys);
            sb.Append(r == null || r.IndexOf('\uFFFD') >= 0 ? ch.ToString() : r);
        }
        return sb.ToString();
    }

    public List<LayoutTable> AllLayouts { get { return new List<LayoutTable>(Layouts.All); } }

    // The user fixed a word with Pause, so it gets corrected automatically next time
    public void Learn(string typed, string result, string resultLang)
    {
        string w = typed.Trim();
        if (w.Length == 0 || w.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0 || w.Length > 40) return;
        // Learn only when the result is a real word: converting a correct word by accident teaches nothing
        string core = Detector.Core(result.Trim());
        var model = Detector.Loaded(resultLang);
        if (string.IsNullOrEmpty(core) || model == null || model.Count(core) == 0) return;
        lock (sync) LearnFromFix(w);
    }

    // The user erased a correction: remember the words and switch the layout back. Erasing only the last word rejects
    // only that one: short words fixed before it along with it ("а" in "а можно") stay on screen and aren't learned
    void Reject()
    {
        var c = last;
        last = null;
        bool all = c.Backspaced >= c.Replaced.Length + 1;
        for (int i = all ? 0 : Math.Max(0, c.OriginalWords.Count - 1); i < c.OriginalWords.Count; i++) AddIgnored(c.OriginalWords[i]);
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

    // "ghbdtn," → "приветб": the key after the word was meant as the comma it typed, not as a letter of the other
    // layout. Decided by the dictionary: the word with that letter is unknown, without it known ("нож" from "yj;" stays)
    static string KeepPunctuation(string result, LangModel model, string lang, char typedLast)
    {
        if (result.Length < 3 || !char.IsLetter(result[result.Length - 1]) || char.IsLetterOrDigit(typedLast) || char.IsWhiteSpace(typedLast) || model == null)
            return result;
        string full = Detector.Core(result, lang), head = Detector.Core(result.Substring(0, result.Length - 1), lang);
        if (string.IsNullOrEmpty(full) || string.IsNullOrEmpty(head) || head.Length < 2 || model.Count(full) > 0 || model.Count(head) == 0) return result;
        return result.Substring(0, result.Length - 1) + typedLast;
    }

    // A confidently corrected word: long enough and in the dictionary
    static bool IsStrong(string text, LangModel model)
    {
        string core = Detector.Core(text);
        return core != null && core.Length >= 3 && model != null && model.Count(core) > 0;
    }

    // The word's time when the focus hasn't been checked since it began: its fix waits for the check (Job.VerifyFrom)
    int verifyFrom;

    bool OnWordEnd(int terminatorVk, IntPtr hwnd)
    {
        bool swallow = OnWordEndCore(terminatorVk, hwnd);
        // Left as typed before the check came: nothing of it is kept, as it may yet turn out to be a password
        if (!swallow && verifyFrom != 0) history.Clear();
        verifyFrom = 0;
        return swallow;
    }

    bool OnWordEndCore(int terminatorVk, IntPtr hwnd)
    {
        bool wasPartial = partial;
        partial = false;
        if (word.Count == 0) return false;
        // The verdict and its time come in one read; a check counts only if it read the focus after this word began
        bool password = false, verified = false;
        if (guard != null)
        {
            int checkedAt;
            guard.Read(out password, out checkedAt);
            verified = checkedAt != 0 && checkedAt - wordStartedAt >= 0;
        }
        string skip = wasPartial ? "edited mid-word" : word.Count >= 40 || overflow ? "too long" : password ? "password field" :
                      !verified && (guard == null || !guard.Answering) ? "focus not checked yet" : IsOwnWindow(hwnd) ? "KeyFish window" : IsExcluded(hwnd) ? "excluded app" :
                      W.HasMoreRights(hwnd) ? "app run as administrator" :
                      IsPasswordField(hwnd) ? "password field (Win32)" : QuietFullscreen && IsFullscreen(hwnd) ? "full screen" : null;
        if (skip != null)
        {
            Diagnose(hwnd, skip);
            // Editing in the middle of a word or a very long one isn't a question anyone asks; the rest are kept, wordless
            if (!wasPartial && skip != "too long" && skip != "KeyFish window")
                Explain(hwnd, password || skip.StartsWith("password") ? "password" : skip == "focus not checked yet" ? "not-checked" :
                              skip == "excluded app" ? "excluded" : skip == "app run as administrator" ? "admin" : "fullscreen");
            history.Clear();
            return false;
        }
        // Not checked yet (under a heavy load a check can take a second): the word is decided now, and a fix of it is
        // held, with the keys typed after it, until the check comes. Without a fix nothing waits
        verifyFrom = verified ? 0 : wordStartedAt | 1;

        var current = Layouts.Get(W.LayoutHkl(hwnd));
        var T = current == null ? null : Detector.Loaded(current.Lang);
        if (T == null) { Diagnose(hwnd, "no language model for the layout"); Explain(hwnd, "no-model"); history.Clear(); return false; }

        string typed = Build(word, current);

        // The layout was switched in the middle of the word ("ghиветик"): the keys were meant for the layout now on
        bool mixed = false;
        foreach (var t in wordLayouts) if (t != null && t.Script != current.Script) mixed = true;
        // Switched while typing, but the app kept the old layout: the rest came out in it. The whole word goes to the
        // layout it was switched to
        if (mixed && switchedEarly && current == earlyFrom && wordLayouts[0] != null)
        {
            var to = wordLayouts[0];
            string shown = Shown(word, wordLayouts, current), meant = Build(word, to);
            Diagnose(hwnd, "corrected: layout, the switch while typing didn't take");
            PushHistory(new WordRec { Keys = new List<KeyStroke>(word), Layouts = word.ConvertAll(k => to), Shown = meant, Layout = to, Changed = true, TerminatorVk = terminatorVk });
            string begun = Build(word, earlyFrom);
            var begunWords = new List<string> { begun };
            last = new Correction
            {
                Original = begun, Replaced = meant, OriginalWords = begunWords, LastWordLength = meant.Length, TerminatorVk = terminatorVk,
                OriginalLayout = earlyFrom.Hkl, Hwnd = hwnd, Recent = Remember(begun, meant, begunWords)
            };
            SetPending();
            Interlocked.Increment(ref correctionCount);
            jobs.Add(new Job { Backspaces = shown.Length, Text = meant, TerminatorVk = terminatorVk, Layout = to.Hkl, Hwnd = hwnd, VerifyFrom = verifyFrom });
            return true;
        }
        if (mixed)
        {
            string shown = Shown(word, wordLayouts, current);
            string fixedCore = Detector.Core(typed);
            if (IsIgnored(shown) || string.IsNullOrEmpty(fixedCore) || T.Count(fixedCore) == 0)
            {
                Diagnose(hwnd, "layout switched mid-word, the result isn't a known word");
                history.Clear();
                return false;
            }
            Diagnose(hwnd, "corrected: layout switched mid-word");
            PushHistory(new WordRec { Keys = new List<KeyStroke>(word), Layouts = new List<LayoutTable>(wordLayouts), Shown = typed, Layout = current, Changed = true, TerminatorVk = terminatorVk });
            var shownWords = new List<string> { shown };
            last = new Correction
            {
                Original = shown, Replaced = typed, OriginalWords = shownWords, LastWordLength = typed.Length, TerminatorVk = terminatorVk,
                OriginalLayout = current.Hkl, Hwnd = hwnd, Recent = Remember(shown, typed, shownWords)
            };
            SetPending();
            Interlocked.Increment(ref correctionCount);
            jobs.Add(new Job { Backspaces = shown.Length, Text = typed, TerminatorVk = terminatorVk, Hwnd = hwnd, VerifyFrom = verifyFrom });
            return true;
        }

        // The user erased a word KeyFish had left alone, switched to a layout of another alphabet and typed the same keys:
        // a fix by hand. Next time KeyFish makes it by itself, as after a fix with Pause
        if (verifyFrom == 0 && erasedKeys != null && Environment.TickCount - erasedAt < 30000 && word.Count >= 2 && SameKeys(erasedKeys, word) &&
            erasedLayouts != null && erasedLayouts.Count > 0 && erasedLayouts[0] != null && erasedLayouts[0].Script != current.Script &&
            erasedLayouts.TrueForAll(t => t == erasedLayouts[0]))
        {
            var before = erasedLayouts[0];
            string wrong = Build(erasedKeys, before), right = Detector.Core(typed), wrongCore = Detector.Core(wrong);
            var beforeModel = Detector.Loaded(before.Lang);
            bool wrongIsWord = !string.IsNullOrEmpty(wrongCore) && beforeModel != null && beforeModel.Count(wrongCore) > 0;
            if (!wrongIsWord && !string.IsNullOrEmpty(right) && T.Count(right) > 0 && wrong.Length <= 40)
            {
                LearnFromFix(wrong);
                Diagnose(hwnd, "learned from a word typed again in another layout");
            }
        }
        erasedKeys = null;

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

        var rec = new WordRec { Keys = new List<KeyStroke>(word), Layouts = new List<LayoutTable>(wordLayouts), Shown = typed, Layout = current, TerminatorVk = terminatorVk };

        // Replacement: an abbreviation from replacements.txt, typed in any layout
        LayoutTable via = null;
        string expansion = FindReplacement(typed) ?? Typographic(typed, terminatorVk);
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
            SetPending();
            Interlocked.Increment(ref expansionCount);
            jobs.Add(new Job
            {
                Backspaces = typed.Length, Text = expansion, TerminatorVk = terminatorVk,
                Layout = via != null ? via.Hkl : IntPtr.Zero, Hwnd = hwnd, VerifyFrom = verifyFrom, Expansion = true,
                Placeholders = Placeholder.Has(expansion)
            });
            return true;
        }

        // Numbers, e-mails, identifiers aren't words to fix (an abbreviation like "addr1" was expanded above)
        foreach (char c in typed)
            if (char.IsDigit(c) || c == '@' || c == '_') { Diagnose(hwnd, "digits or symbols"); Explain(hwnd, "symbols"); history.Clear(); return false; }
        if (Detector.LooksLikeCode(typed)) { Diagnose(hwnd, "link, path or code"); Explain(hwnd, "code"); history.Clear(); return false; }

        // Which layout did the user mean? A word they taught KeyFish goes to their usual other layout
        int chosen = -1;
        // A layout KeyFish doesn't switch to by itself takes no part in the choice
        var choiceModels = new List<LangModel>(altModels);
        for (int i = 0; i < others.Count; i++) if (!AutoTarget(others[i])) choiceModels[i] = null;
        int autoPreferred = preferred >= 0 && choiceModels[preferred] != null ? preferred : -1;
        if (!IsIgnored(typed) && others.Count > 0)
            chosen = IsLearned(typed) ? (autoPreferred >= 0 ? autoPreferred : choiceModels.FindIndex(m => m != null))
                                             : Detector.Choose(typed, T, alts, choiceModels, context, autoPreferred);
        if (chosen >= 0 && Detector.NeedsDictionary(typed))
        {
            string altCore = Detector.Core(alts[chosen]);
            if (string.IsNullOrEmpty(altCore) || altModels[chosen].Count(altCore) == 0) { Diagnose(hwnd, "may be code, and the other layout gives no known word"); chosen = -1; }
        }
        bool convert = chosen >= 0;
        string promote = convert && !learned.Contains(typed) && learnedOnce.Contains(WordList.Normalize(typed)) ? typed : null;
        var target = convert ? others[chosen] : current;
        var targetModel = convert ? altModels[chosen] : T;
        string result = convert ? alts[chosen] : typed;
        if (convert) result = KeepPunctuation(result, targetModel, target.Lang, typed[typed.Length - 1]);

        bool capsMistake;
        string fixedCase = FixCase(result, out capsMistake);
        if (!convert && IsIgnored(fixedCase)) fixedCase = result;
        // Three dots at the end of the word become an ellipsis; alone that's typography, not a caught mistake
        bool onlyTypography = false;
        if (Typography && terminatorVk == 0x20 && fixedCase.EndsWith("...") && !fixedCase.EndsWith("....") && fixedCase.Length > 3)
        {
            onlyTypography = !convert && fixedCase == result;
            fixedCase = fixedCase.Substring(0, fixedCase.Length - 3) + "\u2026";
        }

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

        if (!convert && fixedCase == result && switchedEarly && earlyFrom != null)
        {
            // Switched while typing, and right as a whole: undone like any fix (Pause, Ctrl+Z, erasing it), which also
            // puts it in Never correct. Short words before it typed in the old layout are fixed now, as after any fix
            string begun = Build(word, earlyFrom);
            string final = KeepPunctuation(typed, T, current.Lang, begun[begun.Length - 1]);
            var fromModel = Detector.Loaded(earlyFrom.Lang);
            var earlier = new List<WordRec>();
            if (fromModel != null && IsStrong(final, T))
                for (int i = history.Count - 1; i >= 0 && earlier.Count < 3; i--)
                {
                    var p = history[i];
                    if (p.TerminatorVk != 0x20 || p.Changed || p.Layout != earlyFrom || IsIgnored(p.Shown)) break;
                    if (!Detector.ShouldConvert(p.Shown, Build(p.Keys, current), fromModel, T, current.Lang)) break;
                    earlier.Insert(0, p);
                }
            var from = new StringBuilder();
            var to = new StringBuilder();
            var words = new List<string>();
            int erase = 0;
            foreach (var p in earlier)
            {
                string pAlt = Build(p.Keys, current);
                from.Append(p.Shown).Append(' ');
                to.Append(pAlt).Append(' ');
                words.Add(p.Shown);
                erase += p.Shown.Length + 1;
                p.Shown = pAlt;
                p.Changed = true;
            }
            from.Append(begun);
            to.Append(final);
            words.Add(begun);
            rec.Shown = final;
            rec.Changed = true;
            PushHistory(rec);
            context = current.Lang;
            Diagnose(hwnd, "corrected: layout, while typing");
            last = new Correction
            {
                Original = from.ToString(), Replaced = to.ToString(), OriginalWords = words, LastWordLength = final.Length, TerminatorVk = terminatorVk,
                OriginalLayout = earlyFrom.Hkl, Hwnd = hwnd, Recent = Remember(from.ToString(), to.ToString(), words)
            };
            Interlocked.Increment(ref correctionCount);
            if (earlier.Count == 0 && final == typed) return false;
            SetPending();
            // The word itself is on screen already; the words before it (or its last character) are retyped with it
            jobs.Add(new Job { Backspaces = erase + typed.Length, Text = to.ToString(), TerminatorVk = terminatorVk, Hwnd = hwnd, VerifyFrom = verifyFrom });
            return true;
        }

        if (!convert && fixedCase == result)
        {
            Diagnose(hwnd, IsIgnored(typed) ? "in Never correct" : "looks right as typed");
            string typedCore = Detector.Core(typed, current.Lang);
            if (verifyFrom != 0) { }   // not checked yet: no word goes into the explanations
            else if (IsIgnored(typed)) Explain(hwnd, "ignored", typed);
            else if (!string.IsNullOrEmpty(typedCore) && T.Count(typedCore) == 0 && others.Count > 0)
            {
                // Not a word KeyFish knows. The word itself is kept only when another layout makes a real word of it:
                // otherwise it may as well be a password typed where KeyFish can't tell
                bool readsAsWord = false;
                for (int i = 0; i < alts.Count && !readsAsWord; i++)
                {
                    string altCore = Detector.Core(alts[i], others[i].Lang);
                    readsAsWord = !string.IsNullOrEmpty(altCore) && altModels[i].Count(altCore) > 0;
                }
                if (readsAsWord) Explain(hwnd, "unsure", typed);
                else Explain(hwnd, "unknown");
            }
            PushHistory(rec);
            // Typos are checked after the space without holding it; Enter may already have sent a message
            // The spell check can take a while (Windows loads the dictionary on first use), so typing isn't held
            // for it; the fix is applied only if nothing was typed after the space
            if (fixTypos && verifyFrom == 0 && terminatorVk == 0x20 && TypoCandidate(typed))
                jobs.Add(new Job { TypoRec = rec, TerminatorVk = terminatorVk, Hwnd = hwnd, Serial = keySerial });
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

        Diagnose(hwnd, convert ? "corrected: layout" : onlyTypography ? "typography" : "corrected: case");
        last = new Correction
        {
            // Undoing an ellipsis doesn't teach KeyFish to leave the word alone
            Original = original.ToString(), Replaced = replaced.ToString(), OriginalWords = onlyTypography ? new List<string>() : originalWords,
            LastWordLength = fixedCase.Length, TerminatorVk = terminatorVk, OriginalLayout = current.Hkl, Hwnd = hwnd,
            Recent = onlyTypography ? null : Remember(original.ToString(), replaced.ToString(), originalWords), Promote = promote
        };
        SetPending();
        if (onlyTypography) Interlocked.Increment(ref expansionCount);
        else Interlocked.Increment(ref correctionCount);
        jobs.Add(new Job
        {
            Backspaces = backspaces,
            Text = replaced.ToString(),
            TerminatorVk = terminatorVk,
            ToggleCaps = capsMistake && (W.GetKeyState(0x14) & 1) != 0,
            Layout = convert ? target.Hkl : IntPtr.Zero,
            Hwnd = hwnd,
            VerifyFrom = verifyFrom,
            Expansion = onlyTypography
        });
        return true;
    }

    // Undo the last correction (Pause right after it). false means there is nothing to undo
    // A fix that is still being typed (double Shift right after the space) is waited for, up to half a second
    public bool Undo()
    {
        for (int i = 0; i < 50 && pending && last != null; i++) Thread.Sleep(10);
        return StartUndo(false);
    }

    // releaseCtrl: the undo comes from Ctrl+Z, so Ctrl is held and must be lifted while the text is retyped
    bool StartUndo(bool releaseCtrl)
    {
        Correction c;
        lock (sync)
        {
            c = last;
            if (c == null || pending || W.GetForegroundWindow() != c.Hwnd) return false;
            last = null;
            Interlocked.Increment(ref undoCount);
            foreach (string w in c.OriginalWords) AddIgnored(w);
            recent.Remove(c.Recent);
            history.Clear();
            context = null;
            SetPending();
        }
        jobs.Add(new Job
        {
            Backspaces = c.Replaced.Length + 1,
            Text = c.Original,
            // After Enter the message may be sent already: the original is put back in the input box, not sent again
            TerminatorVk = c.TerminatorVk == 0x0D ? 0 : c.TerminatorVk,
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
            if (W.GetForegroundWindow() != job.Hwnd || keySerial != job.Serial || pending) return;
            SetPending();   // hold what's typed from now on, while the fix is being typed
            last = new Correction
            {
                Original = typed, Replaced = text, OriginalWords = new List<string> { typed },
                LastWordLength = text.Length, TerminatorVk = job.TerminatorVk, OriginalLayout = rec.Layout.Hkl, Hwnd = job.Hwnd,
                Recent = Remember(typed, text, new List<string> { typed })
            };
            rec.Shown = text;
            rec.Changed = true;
            Interlocked.Increment(ref typoCount);
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
                else if (job.VerifyFrom != 0 && !AwaitCheck(job.VerifyFrom)) Withdraw(job);
                else
                {
                    if (job.Placeholders) FillPlaceholders(job);
                    Execute(job);
                }
            }
            catch { }
            lock (sync)
            {
                // More jobs queued: keep holding keystrokes so they replay after those jobs, in typing order
                if (jobs.Count > 0) continue;
                noJobs = true;
                // Replay the keystrokes held back during typing, now as normal input; if some replayed ones are still on
                // their way, the last of them starts it
                if (replayLeft == 0) StartReplay();
            }
        }
    }

    // {date} and the rest are filled in on this thread, not on the hook's: the clipboard can take a while to answer.
    // The fix as remembered for undo becomes what is typed; with {cursor} the caret moves back, so there is no undo
    void FillPlaceholders(Job job)
    {
        int back;
        string text = Placeholder.Fill(job.Text, out back);
        lock (sync)
        {
            if (last != null && last.Replaced == job.Text) { last.Replaced = text; last.LastWordLength = text.Length; }
            if (history.Count > 0 && history[history.Count - 1].Shown == job.Text) history[history.Count - 1].Shown = text;
            if (back > 0) { last = null; history.Clear(); }
        }
        job.Text = text;
        job.CaretBack = back;
    }

    // Waits up to 1.5 seconds for a check of the focus made after the given time; true if it found no password field
    bool AwaitCheck(int from)
    {
        if (guard == null) return false;
        for (int i = 0; i < 150; i++)
        {
            bool password; int at;
            guard.Read(out password, out at);
            if (at != 0 && at - from >= 0) return !password;
            if (i % 20 == 0) guard.RequestCheck();
            Thread.Sleep(10);
        }
        return false;
    }

    // A fix that waited for the check of the focus and didn't get a clear answer: not typed, and nothing of the word is
    // kept. Only the key that ended it, held back for the fix, goes to the app
    void Withdraw(Job job)
    {
        lock (sync)
        {
            history.Clear();
            if (last != null) { recent.Remove(last.Recent); last = null; }
            if (job.Expansion) Interlocked.Decrement(ref expansionCount);
            else Interlocked.Decrement(ref correctionCount);
        }
        Diagnose(job.Hwnd, "fix withdrawn: the focus wasn't checked in time");
        Explain(job.Hwnd, "not-checked");
        Execute(new Job { Text = "", TerminatorVk = job.TerminatorVk, Hwnd = job.Hwnd });
    }

    void Execute(Job job)
    {
        var inputs = new List<W.INPUT>();
        if (job.CtrlComboVk != 0)
        {
            // Ctrl is physically down (it can't have been released: that would have cleared ctrlLifted first),
            // so pressing it logically again can't leave it stuck; the physical release will lift it
            W.AddKeyDown(inputs, (W.GetAsyncKeyState(0xA3) & 0x8000) != 0 ? 0xA3 : 0xA2);   // the side the user holds
            W.AddKey(inputs, job.CtrlComboVk);
            W.Send(inputs);
            return;
        }
        // The user switched windows in the meantime: typing there would erase and write into the wrong text
        if (job.Hwnd != IntPtr.Zero && W.GetForegroundWindow() != job.Hwnd) return;
        // With Ctrl held, Backspace would erase whole words: lift Ctrl first
        if (job.ReleaseCtrl) { W.AddKeyUp(inputs, 0xA2); W.AddKeyUp(inputs, 0xA3); W.AddKeyUp(inputs, 0x11); }
        if (job.ToggleCaps) W.AddKey(inputs, 0x14);
        for (int i = 0; i < job.Backspaces; i++) W.AddKey(inputs, 0x08);
        foreach (char c in job.Text)
        {
            // A line break as Shift+Enter: a new line in chats too, where a plain Enter would send the message half-written
            if (c == '\n') { W.AddKeyDown(inputs, 0xA0); W.AddKey(inputs, 0x0D); W.AddKeyUp(inputs, 0xA0); }
            else if (c == '\t') W.AddKey(inputs, 0x09);
            else if (c != '\r') W.AddUnicode(inputs, c);
        }
        if (job.TerminatorVk != 0) W.AddKey(inputs, job.TerminatorVk);
        for (int i = 0; i < job.CaretBack + (job.CaretBack > 0 && job.TerminatorVk != 0 ? 1 : 0); i++) W.AddKey(inputs, 0x25, true);   // Left
        if (inputs.Count > 0) W.Send(inputs);
        if (job.Layout != IntPtr.Zero)
        {
            W.SwitchLayout(job.Hwnd, job.Layout);
            // Keys held meanwhile replay after this: in the old layout they would type the wrong letters. The switch
            // usually lands within a few milliseconds; the fallback in SwitchLayout comes after 100
            for (int i = 0; i < 40 && W.LayoutHkl(job.Hwnd) != job.Layout; i++) Thread.Sleep(5);
        }
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

    // Layouts KeyFish never switches to by itself (no-auto-layouts.txt): a rarely used third layout that took ordinary
    // words for its own. A fix by hand (Pause, hotkeys) still goes there
    volatile HashSet<long> NoAutoLayouts = new HashSet<long>();
    readonly string noAutoPath;
    DateTime noAutoStamp;
    bool AutoTarget(LayoutTable t) { return !NoAutoLayouts.Contains(t.Hkl.ToInt64()); }

    // Focus is in a password field (any app, browsers included): then nothing is corrected or remembered.
    // The check itself runs in a separate guard process, see PasswordGuard.cs
    PasswordGuardClient guard;
    bool passwordFocused { get { return guard != null && guard.Password; } }
    volatile int wordStartedAt;

    // Focus was checked after this word started; otherwise we can't know it isn't a password
    // The password guard answers checks; while it doesn't, nothing is corrected (shown by the tray icon)
    public bool GuardAnswering { get { return guard != null && guard.Answering; } }   // no check at all is never "answering"

    void RequestFocusCheck() { if (guard != null) guard.RequestCheck(); }

    int lastWordCheck;

    // A letter asks for a check of the focus until one made after the word began has found the text field itself;
    // after that only every 300 ms. Asked for on every letter, the checks queued behind each other, ten a second, and
    // under load the last letter's check ended long after the word did
    void RequestWordCheck()
    {
        if (guard == null) return;
        bool password, definite; int at;
        guard.Read(out password, out at, out definite);
        int now = Environment.TickCount;
        if (definite && at != 0 && at - wordStartedAt >= 0 && now - lastWordCheck < 300) return;
        lastWordCheck = now;
        guard.RequestCheck();
    }

    // A full-screen game, presentation or video: typing there isn't text, so don't correct
    static bool IsFullscreen(IntPtr hwnd)
    {
        int state;
        // 2 — full-screen app, 3 — Direct3D game, 4 — presentation mode
        if (W.SHQueryUserNotificationState(out state) == 0 && (state == 2 || state == 3 || state == 4)) return true;

        string cls = W.ClassName(hwnd);
        if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
        // A maximized window is an ordinary one, even where it covers the whole monitor (an auto-hidden taskbar,
        // a second monitor without one); games and videos go full screen without being maximized
        if (W.IsZoomed(W.GetAncestor(hwnd, 2))) return false;   // GA_ROOT
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
        string check = "not running";
        if (guard != null)
        {
            bool p; int at;
            guard.Read(out p, out at);
            check = at == 0 ? "not answered yet" : (at - wordStartedAt) + " ms after the word began";
        }
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + W.ProcessName(hwnd) + "  " + what + "  (" + word.Count + " keys, password check " + check + ")\r\n";
        FileQueue.Run(() => File.AppendAllText(diagnosePath, line, Encoding.UTF8));
    }

    // Diagnostics too: every half minute while keys come, one line on how long the keyboard hook took per key (each
    // millisecond there is added to every key in every app), the memory KeyFish holds and its processor time
    readonly HookPerf perf = new HookPerf();
    public static DateTime ReadyAt;   // the tray icon is up

    void WriteDiagnostics(string line)
    {
        line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line + "\r\n";
        FileQueue.Run(() => File.AppendAllText(diagnosePath, line, Encoding.UTF8));
    }

    // Used on the hook thread alone (the hook and the watchdog timer): no locks
    class HookPerf
    {
        static readonly double TicksPerMs = Stopwatch.Frequency / 1000.0;
        static readonly double[] Limits = { 0.1, 0.5, 1, 2, 5, 20 };
        readonly int[] counts = new int[Limits.Length + 1];
        int keys, ticks;
        double totalMs, maxMs;
        bool startWritten;

        public void Hook(long elapsed)
        {
            double ms = elapsed / TicksPerMs;
            int i = 0;
            while (i < Limits.Length && ms >= Limits[i]) i++;
            counts[i]++;
            keys++;
            totalMs += ms;
            if (ms > maxMs) maxMs = ms;
        }

        // Every 2 seconds
        public void Tick(KeyboardWatcher w)
        {
            if (!startWritten && ReadyAt != DateTime.MinValue && LayoutSet.ModelsLoaded != DateTime.MinValue)
            {
                startWritten = true;
                FileQueue.Run(() =>
                {
                    DateTime launched = Process.GetCurrentProcess().StartTime;
                    w.WriteDiagnostics("start: tray icon " + (int)(ReadyAt - launched).TotalMilliseconds + " ms after launch, language models (" +
                                       LayoutSet.ModelsLoadedLangs + ") " + (int)(LayoutSet.ModelsLoaded - launched).TotalMilliseconds + " ms");
                });
            }
            if (++ticks < 15) return;
            ticks = 0;
            if (keys == 0) return;
            var line = new StringBuilder();
            line.AppendFormat(CultureInfo.InvariantCulture, "perf: {0} keys, hook {1:0.000} ms on average, {2:0.0} ms at most;", keys, totalMs / keys, maxMs);
            for (int i = 0; i < counts.Length; i++)
                line.AppendFormat(CultureInfo.InvariantCulture, " {0}{1} ms: {2};", i == counts.Length - 1 ? "over " : "under ",
                                  i == counts.Length - 1 ? Limits[i - 1] : Limits[i], counts[i]);
            // Process figures take a snapshot of all processes in Windows, milliseconds the hook thread can't spare
            FileQueue.Run(() =>
            {
                var p = Process.GetCurrentProcess();
                line.AppendFormat(CultureInfo.InvariantCulture, " managed {0:0.0} MB, private {1:0.0} MB, processor {2:0.0} s",
                                  GC.GetTotalMemory(false) / 1048576.0, p.PrivateMemorySize64 / 1048576.0, p.TotalProcessorTime.TotalSeconds);
                if (w.guard != null) line.Append("; password check: " + w.guard.Usage());
                w.WriteDiagnostics(line.ToString());
            });
            Array.Clear(counts, 0, counts.Length);
            keys = 0;
            totalMs = maxMs = 0;
        }
    }

    // No automatic fixes here: the app is off or set to hotkeys only
    bool IsExcluded(IntPtr hwnd) { return excluded.Mode(hwnd) != AppMode.Full; }

    // Hotkeys on text work through Ctrl+C and Ctrl+V. Not in an excluded app (in a terminal Ctrl+C stops the running
    // command) and never in a password field: a fresh check of the focus is asked for and waited for, up to half a second
    public bool HotkeyAllowed(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || IsOwnWindow(hwnd) || excluded.Mode(hwnd) == AppMode.Off || IsPasswordField(hwnd) || guard == null) return false;
        int asked = Environment.TickCount;
        guard.RequestCheck();
        for (int i = 0; i < 50; i++)
        {
            bool password; int at;
            guard.Read(out password, out at);
            if (at != 0 && at - asked >= 0) return !password;
            Thread.Sleep(10);
        }
        return false;
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

// A keystroke held back while a correction was typed, with the modifiers that were down
// A key held back while a correction is typed, replayed as it came: with its scan code and extended flag, so an arrow
// stays an arrow for apps that read those (games, remote desktops), not a numpad key
struct HeldKey { public int Vk, Scan; public bool Extended, Shift, Ctrl, Alt, Win; }

public enum AppMode { Full, HotkeysOnly, Off }

// Values filled into a replacement when it is typed: {date}, {time}, {clipboard} (its text, up to 2000 characters), and
// {cursor}, where the caret is left
static class Placeholder
{
    static readonly string[] Names = { "{date}", "{time}", "{clipboard}", "{cursor}" };

    public static bool Has(string text)
    {
        foreach (string n in Names) if (text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    // back: how many characters after {cursor} (the first one; the others are dropped)
    public static string Fill(string text, out int back)
    {
        // Split at {cursor} first: the clipboard may hold "{cursor}" too, and that one is just text
        string before = text, after = null;
        int at = text.IndexOf("{cursor}", StringComparison.OrdinalIgnoreCase);
        if (at >= 0) { before = text.Substring(0, at); after = Replace(text.Substring(at + 8), "{cursor}", () => ""); }
        string clip = null;
        Func<string> clipboard = () => clip ?? (clip = ClipboardText() ?? "");
        string head = Fill(before, clipboard), tail = after == null ? "" : Fill(after, clipboard);
        // Characters after the caret, as typed: a line break is one (Shift+Enter), "\r" is never typed
        back = after == null ? 0 : tail.Replace("\r", "").Length;
        return head + tail;
    }

    static string Fill(string s, Func<string> clipboard)
    {
        var now = DateTime.Now;
        s = Replace(s, "{date}", () => now.ToString("d", CultureInfo.CurrentCulture));
        s = Replace(s, "{time}", () => now.ToString("t", CultureInfo.CurrentCulture));
        return Replace(s, "{clipboard}", clipboard);
    }

    static string Replace(string s, string name, Func<string> value)
    {
        int at = s.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return s;
        string v = value() ?? "";
        var sb = new StringBuilder();
        int from = 0;
        for (; at >= 0; at = s.IndexOf(name, from, StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(s, from, at - from).Append(v);
            from = at + name.Length;
        }
        return sb.Append(s, from, s.Length - from).ToString();
    }

    // The clipboard is read on a thread of its own (it must be single-threaded) and given half a second: an app that
    // holds the clipboard open can't hold the typing up
    static string ClipboardText()
    {
        string text = null;
        var t = new Thread(() => { try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { } }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        if (!t.Join(500)) return "";
        if (text != null && text.Length > 2000) text = text.Substring(0, 2000);
        return text;
    }
}

// exclusions.txt: what KeyFish does in an app. One rule a line:
//   code                    off: no automatic fixes, no hotkeys on text (in a terminal Ctrl+C stops the running command)
//   code = hotkeys          hotkeys only: nothing is fixed by itself, the hotkeys work
//   title: KeePass          by the window title: the title contains the text; * stands for any characters
//   title: * - ssh = hotkeys
// A title is read only to be matched, when a word ends or a hotkey is pressed, and is kept nowhere
public class AppRules
{
    public const string TitlePrefix = "title:";
    readonly Dictionary<string, AppMode> apps = new Dictionary<string, AppMode>(StringComparer.OrdinalIgnoreCase);
    readonly List<KeyValuePair<Regex, AppMode>> titles = new List<KeyValuePair<Regex, AppMode>>();

    // "name = hotkeys" → name, true; "name" → name, false
    public static string Split(string line, out bool hotkeysOnly)
    {
        string s = line.Trim();
        hotkeysOnly = false;
        int eq = s.LastIndexOf('=');
        if (eq > 0 && s.Substring(eq + 1).Trim().Equals("hotkeys", StringComparison.OrdinalIgnoreCase))
        {
            hotkeysOnly = true;
            s = s.Substring(0, eq).Trim();
        }
        return s;
    }

    public static bool IsTitleRule(string rule) { return rule.StartsWith(TitlePrefix, StringComparison.OrdinalIgnoreCase); }

    public static AppRules Parse(IEnumerable<string> lines)
    {
        var r = new AppRules();
        foreach (string line in lines)
        {
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
            bool hotkeys;
            string s = Split(line, out hotkeys);
            var mode = hotkeys ? AppMode.HotkeysOnly : AppMode.Off;
            if (IsTitleRule(s))
            {
                string pattern = s.Substring(TitlePrefix.Length).Trim();
                if (pattern.Trim('*').Length == 0) continue;   // "*" alone would turn KeyFish off everywhere
                string re = Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".");
                r.titles.Add(new KeyValuePair<Regex, AppMode>(new Regex(pattern.Contains("*") || pattern.Contains("?") ? "^" + re + "$" : re,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline), mode));
                continue;
            }
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            AppMode was;
            if (s.Length > 0 && (!r.apps.TryGetValue(s, out was) || was < mode)) r.apps[s] = mode;
        }
        return r;
    }

    // The strictest rule that fits the window: off over hotkeys only over full
    public AppMode Mode(IntPtr hwnd)
    {
        AppMode mode;
        if (!apps.TryGetValue(W.ProcessName(hwnd), out mode)) mode = AppMode.Full;
        if (mode == AppMode.Off || titles.Count == 0) return mode;
        string title = W.WindowTitle(hwnd);
        if (title.Length == 0) return mode;
        foreach (var t in titles)
            if (t.Value > mode && t.Key.IsMatch(title)) mode = t.Value;
        return mode;
    }
}

static class W
{
    public static readonly UIntPtr Marker = (UIntPtr)0x4C464958; // marks our own input so the hook skips it
    public static readonly UIntPtr ReplayMarker = (UIntPtr)0x4C465250; // a held key replayed: the hook handles it as typed
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
        string name = ImageName(hwnd);
        // Store apps (Mail, Calculator, WhatsApp…) live inside a frame window owned by ApplicationFrameHost;
        // the app itself is the process of the CoreWindow in that frame
        if (string.Equals(name, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
        {
            IntPtr core = FindWindowEx(hwnd, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
            if (core != IntPtr.Zero) name = ImageName(core);
        }
        return name;
    }

    // Top-level windows of a class, e.g. every Store app frame ("ApplicationFrameWindow")
    public static List<IntPtr> TopWindows(string cls)
    {
        var list = new List<IntPtr>();
        for (IntPtr h = FindWindowEx(IntPtr.Zero, IntPtr.Zero, cls, null); h != IntPtr.Zero && list.Count < 256; h = FindWindowEx(IntPtr.Zero, h, cls, null))
            list.Add(h);
        return list;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct PT { public int X, Y; }
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(PT point);

    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr info, int length, out int returned);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();

    static int ownIntegrity = -1;

    // The window belongs to an app with more rights than KeyFish (run as administrator, or a system window such as
    // Task Manager): Windows doesn't let a program with fewer rights see the keys typed there or type into it.
    // True also when the app's rights can't be read at all — that happens to exactly such apps
    public static bool HasMoreRights(IntPtr hwnd)
    {
        if (ownIntegrity < 0) ownIntegrity = Math.Max(0, Integrity(GetCurrentProcess()));
        uint pid;
        GetWindowThreadProcessId(hwnd, out pid);
        if (pid == 0) return false;
        IntPtr h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return true;
        try { int level = Integrity(h); return level < 0 || level > ownIntegrity; }
        finally { CloseHandle(h); }
    }

    // The process's integrity level (0x2000 a normal app, 0x3000 run as administrator, 0x4000 system); -1 if unreadable
    static int Integrity(IntPtr process)
    {
        IntPtr token;
        if (!OpenProcessToken(process, 0x0008, out token)) return -1;   // TOKEN_QUERY
        IntPtr buffer = IntPtr.Zero;
        try
        {
            int size;
            GetTokenInformation(token, 25, IntPtr.Zero, 0, out size);   // TokenIntegrityLevel
            if (size <= 0) return -1;
            buffer = Marshal.AllocHGlobal(size);
            if (!GetTokenInformation(token, 25, buffer, size, out size)) return -1;
            IntPtr sid = Marshal.ReadIntPtr(buffer);   // TOKEN_MANDATORY_LABEL.Label.Sid
            int count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return count == 0 ? -1 : Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            CloseHandle(token);
        }
    }

    static string ImageName(IntPtr hwnd)
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int InternalGetWindowText(IntPtr hwnd, StringBuilder sb, int max);

    // The title of the window's top-level window. Read from Windows' own copy: no message goes to the app, so a hung
    // app can't hold KeyFish up
    public static string WindowTitle(IntPtr hwnd)
    {
        IntPtr top = GetAncestor(hwnd, 2);   // GA_ROOT
        var sb = new StringBuilder(512);
        InternalGetWindowText(top != IntPtr.Zero ? top : hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr hkl);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("imm32.dll")] static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hwnd);

    // The window with the keyboard focus in this window's thread: a control in a dialog, the app's own window inside a
    // Store app's frame (its input is joined to the frame's); the window itself when none has it
    public static IntPtr FocusOf(IntPtr hwnd)
    {
        uint pid;
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
        return GetGUIThreadInfo(GetWindowThreadProcessId(hwnd, out pid), ref info) && info.hwndFocus != IntPtr.Zero ? info.hwndFocus : hwnd;
    }

    // The keyboard layout (HKL) typing goes to in this window: that of the thread whose window has the focus
    public static IntPtr LayoutHkl(IntPtr hwnd)
    {
        uint pid;
        return GetKeyboardLayout(GetWindowThreadProcessId(FocusOf(hwnd), out pid));
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

    public static void AddKeyDown(List<INPUT> list, int vk) { list.Add(Key((ushort)vk, 0, 0)); }
    public static void AddKeyUp(List<INPUT> list, int vk) { list.Add(Key((ushort)vk, 0, 2)); }

    public static void AddKey(List<INPUT> list, int vk)
    {
        list.Add(Key((ushort)vk, 0, 0));
        list.Add(Key((ushort)vk, 0, 2));
    }

    // An arrow or another key beside the letters: marked extended, or with Num Lock on it would be the numpad key
    public static void AddKey(List<INPUT> list, int vk, bool extended)
    {
        uint e = extended ? 1u : 0u;
        list.Add(Key((ushort)vk, 0, e));
        list.Add(Key((ushort)vk, 0, e | 2));
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
            Modifier(list, undo, k.Win, 0x5B, 0x5C);
            uint ext = k.Extended ? 1u : 0;   // KEYEVENTF_EXTENDEDKEY
            var down = Key((ushort)k.Vk, (ushort)k.Scan, ext);
            var up = Key((ushort)k.Vk, (ushort)k.Scan, ext | 2);
            down.u.ki.dwExtraInfo = up.u.ki.dwExtraInfo = ReplayMarker;
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

    // Asks the app to switch the layout (WM_INPUTLANGCHANGEREQUEST), through the IME window of the focused control's
    // thread: it takes the request in dialogs, consoles and Store apps alike, where the top window may ignore it. If the
    // layout hasn't changed a tenth of a second later, the window itself is asked as well. The check waits on the thread
    // pool: callers, the keyboard hook among them, never wait
    // The second request is only made for the latest switch, in the same window, and while nobody else changed the layout
    static int switchSerial;

    public static void SwitchLayout(IntPtr hwnd, IntPtr hkl)
    {
        if (hkl == IntPtr.Zero) return;
        IntPtr before = LayoutHkl(hwnd);
        if (before == hkl) return;
        int my = Interlocked.Increment(ref switchSerial);
        IntPtr ime = ImmGetDefaultIMEWnd(FocusOf(hwnd));
        if (ime == IntPtr.Zero || !PostMessage(ime, 0x0050, IntPtr.Zero, hkl))
        {
            PostMessage(hwnd, 0x0050, IntPtr.Zero, hkl);
            return;
        }
        ThreadPool.QueueUserWorkItem(_ =>
        {
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(10);
                IntPtr now = LayoutHkl(hwnd);
                if (now == hkl) return;
                if (now != before) return;   // the user switched to another one meanwhile
            }
            if (switchSerial != my || GetForegroundWindow() != hwnd) return;
            PostMessage(hwnd, 0x0050, IntPtr.Zero, hkl);
        });
    }
}

// A word list in a text file next to the app: one word per line, case-insensitive, without the punctuation around it
// ("солвере," and "солвере" are one word).
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

    // The word as the list keeps it: lower case, without punctuation, quotes or brackets at its ends; "" if nothing is left
    public static string Normalize(string word)
    {
        string w = (word ?? "").Trim();
        int start = 0, end = w.Length;
        while (start < end && !char.IsLetterOrDigit(w[start])) start++;
        while (end > start && !char.IsLetterOrDigit(w[end - 1])) end--;
        return w.Substring(start, end - start).ToLowerInvariant();
    }

    public bool Contains(string word) { string w = Normalize(word); lock (gate) return w.Length > 0 && words.Contains(w); }

    // A word of the list begins with these letters (the beginning of a word still being typed)
    public bool AnyStartsWith(string prefix)
    {
        string p = Normalize(prefix);
        if (p.Length == 0) return false;
        lock (gate) foreach (string w in words) if (w.StartsWith(p, StringComparison.Ordinal)) return true;
        return false;
    }

    public void Add(string word)
    {
        string w = Normalize(word);
        if (w.Length == 0) return;
        lock (gate) if (!words.Add(w)) return;
        FileQueue.Run(() => { bool inSync = InSync(); File.AppendAllText(path, w + "\r\n", Encoding.UTF8); Written(inSync); });
    }

    public void Remove(string word)
    {
        string w = Normalize(word);
        lock (gate) if (!words.Remove(w)) return;
        FileQueue.Run(() =>
        {
            if (!File.Exists(path)) return;
            var kept = new List<string>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                if (Normalize(line) != w) kept.Add(line);
            bool inSync = InSync();
            File.WriteAllLines(path, kept, Encoding.UTF8);
            Written(inSync);
        });
    }

    // The file is as KeyFish last read or wrote it; if Settings or Notepad changed it meanwhile, it isn't
    bool InSync()
    {
        try { lock (gate) return File.Exists(path) ? File.GetLastWriteTimeUtc(path) == stamp : stamp == DateTime.MinValue; }
        catch { return false; }
    }

    // After KeyFish's own write: remember the new time only if nothing else changed the file before it. Otherwise the
    // other change (words deleted in Settings, say) would look already read, and the next reload would skip it
    void Written(bool inSync)
    {
        if (inSync) lock (gate) stamp = File.GetLastWriteTimeUtc(path);
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
                    if (line.Trim().StartsWith("#")) continue;
                    string s = Normalize(line);
                    if (s.Length > 0) set.Add(s);
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
