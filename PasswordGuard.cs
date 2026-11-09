using System;
using System.Diagnostics;
using System.Drawing;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Windows.Forms;

// The password-field check runs in a separate KeyFish process ("KeyFish.exe --password-guard <pid>").
// UI Automation can crash the whole process on some windows (a .NET runtime failure that can't be caught);
// isolated, such a crash only restarts the guard while KeyFish keeps running.
// Shared memory: one 64-bit value, written and read in one go so a verdict is never paired with another check's time.
// Low half: 1 if focus is in a password field, 2 if the focus couldn't be read (the app didn't answer or failed): then
// nothing is vouched for. High half: Environment.TickCount taken right before the focus was read, never 0 — 0 means
// no check has been made yet.
// The guard also finds the text caret of browsers and Electron apps for the layout tag (accessibility objects, MSAA):
// an app's broken accessibility object took the whole process down the same way. Bytes 8–15: the window asked about,
// 16–19: the question's number; 20–23: the number answered, 24–39: the caret (left, top, width, height; height 0: none).
// Bytes 40–55, for the diagnostics log: checks made, focus changes seen, microseconds spent reading the focus.
static class GuardNames
{
    public const int Size = 56;
    public static string Memory(int pid) { return "Local\\KeyFish_Guard_" + pid; }
    public static string Signal(int pid) { return "Local\\KeyFish_GuardCheck_" + pid; }
    public static string CaretAsk(int pid) { return "Local\\KeyFish_GuardCaret_" + pid; }
    public static string CaretAnswer(int pid) { return "Local\\KeyFish_GuardCaretDone_" + pid; }
}

// Main process side: starts the guard, restarts it if it dies, and reads its verdict
class PasswordGuardClient
{
    readonly int pid = Process.GetCurrentProcess().Id;
    readonly MemoryMappedFile memory;
    readonly MemoryMappedViewAccessor view;
    readonly EventWaitHandle signal, caretAsk, caretAnswer;
    readonly object sync = new object(), caretSync = new object();
    int caretQuestion;

    public static PasswordGuardClient Current;   // the one KeyFish runs, for the layout tag's caret
    Process guard;
    DateTime guardStarted;
    DateTime windowStart = DateTime.UtcNow;
    int restartsInWindow;

    public PasswordGuardClient()
    {
        memory = MemoryMappedFile.CreateNew(GuardNames.Memory(pid), GuardNames.Size);
        view = memory.CreateViewAccessor(0, GuardNames.Size);
        signal = new EventWaitHandle(false, EventResetMode.AutoReset, GuardNames.Signal(pid));
        caretAsk = new EventWaitHandle(false, EventResetMode.AutoReset, GuardNames.CaretAsk(pid));
        caretAnswer = new EventWaitHandle(false, EventResetMode.AutoReset, GuardNames.CaretAnswer(pid));
        Start();
        Current = this;
    }

    // The text caret of a window, on screen, as its accessibility object reports it; asked of the guard, so a broken
    // object can only take the guard down. False with no answer in time: the app is slow, or the guard is busy or
    // restarting (the tag then goes next to the mouse pointer)
    public bool Caret(IntPtr hwnd, int timeoutMs, out Rectangle caret)
    {
        caret = Rectangle.Empty;
        int question;
        lock (caretSync)
        {
            question = ++caretQuestion;
            view.Write(8, hwnd.ToInt64());
            view.Write(16, question);
        }
        caretAnswer.Reset();
        caretAsk.Set();
        int until = Environment.TickCount + timeoutMs;
        while (true)
        {
            int left = until - Environment.TickCount;
            if (left <= 0 || !caretAnswer.WaitOne(left)) return false;
            if (view.ReadInt32(20) != question) continue;   // a late answer to an earlier question
            int x = view.ReadInt32(24), y = view.ReadInt32(28), w = view.ReadInt32(32), h = view.ReadInt32(36);
            if (h <= 0) return false;
            caret = new Rectangle(x, y, w, h);
            return true;
        }
    }

    // The last verdict and when the focus was read for it (0: never). A check that couldn't read the focus comes with
    // the time 0 too: it vouches for nothing, the same as no check at all
    public void Read(out bool password, out int checkedAt)
    {
        bool unsure;
        Read(out password, out unsure, out checkedAt);
        if (unsure) checkedAt = 0;
    }

    void Read(out bool password, out bool unsure, out int checkedAt)
    {
        // One 8-byte read is atomic in a 64-bit process; a 32-bit one reads it in two halves, so it reads until two
        // reads agree, and a verdict is never paired with another check's time
        long v = view.ReadInt64(0);
        if (IntPtr.Size == 4)
            for (long again = view.ReadInt64(0); again != v; again = view.ReadInt64(0)) v = again;
        password = (v & 1) != 0;
        unsure = (v & 2) != 0;
        checkedAt = (int)(v >> 32);
    }

    public bool Password { get { bool p; int t; Read(out p, out t); return p; } }

    // The guard process's memory and processor time, for the diagnostics log
    public string Usage()
    {
        try
        {
            var g = guard;
            g.Refresh();
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                 "private {0:0.0} MB, processor {1:0.0} s, {2} checks taking {3:0.0} s, {4} focus changes",
                                 g.PrivateMemorySize64 / 1048576.0, g.TotalProcessorTime.TotalSeconds,
                                 view.ReadInt32(40), view.ReadInt64(48) / 1e6, view.ReadInt32(44));
        }
        catch { return "not running"; }
    }

    volatile int unansweredSince;   // when a check was asked for that hasn't been answered yet (0: none)

    // Ask the guard to look at the focus now; cheap enough to call on every keystroke
    public void RequestCheck()
    {
        if (unansweredSince == 0) unansweredSince = Environment.TickCount | 1;
        try { signal.Set(); } catch { }
    }

    // False while a check asked for has gone unanswered for seconds: the guard is starting, restarting or stuck,
    // and until it answers KeyFish corrects nothing. The tray shows that instead of looking as if all is fine
    public bool Answering
    {
        get
        {
            int asked = unansweredSince;
            if (asked == 0) return true;
            bool p, unsure; int at;
            Read(out p, out unsure, out at);   // a check that couldn't read the focus is an answer all the same
            if (at != 0 && at - asked >= 0) { unansweredSince = 0; return true; }
            int waiting = Environment.TickCount - asked;
            // A guard stuck in an app that never answers would hold every check up for good: it is restarted
            if (waiting > 30000) KillStuck();
            return waiting < 3000;
        }
    }

    int lastKill;

    void KillStuck()
    {
        lock (sync)
        {
            if (lastKill != 0 && Environment.TickCount - lastKill < 60000) return;
            lastKill = Environment.TickCount | 1;
            try { guard.Kill(); } catch { }   // Exited starts a new one
        }
    }

    void Start()
    {
        lock (sync)
        {
            try
            {
                var info = new ProcessStartInfo(Application.ExecutablePath, "--password-guard " + pid)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                guardStarted = DateTime.UtcNow;
                if (guard != null) guard.Dispose();   // the one that exited
                guard = Process.Start(info);
                guard.EnableRaisingEvents = true;
                guard.Exited += (s, e) => Restart();
            }
            catch { ScheduleStart(30000); }
        }
    }

    // While the guard is down no check is fresh, so KeyFish doesn't correct anything — the safe side
    void Restart()
    {
        // It crashed (it leaves with 0 only together with KeyFish): Windows' record of it goes to crash.log once written
        int code = 0;
        try { code = guard.ExitCode; } catch { }
        if (code != 0)
        {
            DateTime from = guardStarted;
            ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(5000); CrashLog.FromWindows(from, DateTime.UtcNow, "the password check (exit code 0x" + code.ToString("X") + ")"); });
        }
        lock (sync)
        {
            if (DateTime.UtcNow - windowStart > TimeSpan.FromMinutes(1)) { windowStart = DateTime.UtcNow; restartsInWindow = 0; }
            restartsInWindow++;
        }
        ScheduleStart(restartsInWindow > 5 ? 30000 : 2000);
    }

    void ScheduleStart(int delayMs)
    {
        System.Threading.Timer timer = null;
        timer = new System.Threading.Timer(_ => { timer.Dispose(); Start(); }, null, delayMs, Timeout.Infinite);
    }
}

// Guard process side
static class PasswordGuardProcess
{
    public static void Run(int parentPid)
    {
        MemoryMappedViewAccessor view;
        EventWaitHandle signal, caretAsk, caretAnswer;
        Process parent;
        try
        {
            view = MemoryMappedFile.OpenExisting(GuardNames.Memory(parentPid)).CreateViewAccessor(0, GuardNames.Size);
            signal = EventWaitHandle.OpenExisting(GuardNames.Signal(parentPid));
            caretAsk = EventWaitHandle.OpenExisting(GuardNames.CaretAsk(parentPid));
            caretAnswer = EventWaitHandle.OpenExisting(GuardNames.CaretAnswer(parentPid));
            parent = Process.GetProcessById(parentPid);
        }
        catch { return; }

        // Leave together with KeyFish
        new Thread(() => { parent.WaitForExit(); Environment.Exit(0); }) { IsBackground = true }.Start();

        // Where's the caret: each question on a thread of its own, so an app that never answers holds up only its own
        // question; at most four at a time, so apps that never answer can't pile threads up. The answer's number is
        // written last, after the caret
        int asking = 0;
        new Thread(() =>
        {
            while (true)
            {
                caretAsk.WaitOne();
                IntPtr hwnd = new IntPtr(view.ReadInt64(8));
                int question = view.ReadInt32(16);
                if (Interlocked.Increment(ref asking) > 4) { Interlocked.Decrement(ref asking); continue; }   // no answer: the tag goes by the mouse
                var ask = new Thread(() =>
                {
                    try
                    {
                        Rectangle r;
                        if (!LayoutBadge.AccessibleCaret(hwnd, out r)) r = Rectangle.Empty;
                        lock (view)
                        {
                            view.Write(24, r.X); view.Write(28, r.Y); view.Write(32, r.Width); view.Write(36, r.Height);
                            view.Write(20, question);
                        }
                        caretAnswer.Set();
                    }
                    finally { Interlocked.Decrement(ref asking); }
                }) { IsBackground = true };
                ask.SetApartmentState(ApartmentState.MTA);
                ask.Start();
            }
        }) { IsBackground = true }.Start();

        // The focus is read on a thread of its own, in the multithreaded apartment UI Automation clients are meant to use
        var checks = new Thread(() => CheckFocus(view, signal)) { IsBackground = true };
        checks.SetApartmentState(ApartmentState.MTA);
        checks.Start();

        // Focus changes wake the check too: Windows' own focus and foreground events, taken out of context. UI Automation's
        // focus subscription did this at a high price: subscribing visited every window (seconds to over a minute with
        // browsers open), switched every browser and Electron app into full accessibility mode for as long as it ran,
        // and fetched an element on every focus change in any app. Words never depended on it: the first key of each
        // word asks for a check of its own. This thread only passes the events on
        Native.WinEventProc onFocus = (hook, ev, hwnd, idObject, idChild, thread, time) =>
        {
            view.Write(44, view.ReadInt32(44) + 1);
            signal.Set();
        };
        Native.SetWinEventHook(3, 3, IntPtr.Zero, onFocus, 0, 0, 2);             // EVENT_SYSTEM_FOREGROUND; out of context, not our own
        Native.SetWinEventHook(0x8005, 0x8005, IntPtr.Zero, onFocus, 0, 0, 2);   // EVENT_OBJECT_FOCUS
        Application.Run();
        GC.KeepAlive(onFocus);
    }

    static void CheckFocus(MemoryMappedViewAccessor view, EventWaitHandle signal)
    {
        // The two properties the check needs come together with the focused element, in one call to the app; read one
        // by one, each was another call (and a browser builds its accessibility answers anew for every one)
        var cache = new System.Windows.Automation.CacheRequest { AutomationElementMode = System.Windows.Automation.AutomationElementMode.None };
        cache.Add(System.Windows.Automation.AutomationElement.IsPasswordProperty);
        cache.Add(System.Windows.Automation.AutomationElement.ControlTypeProperty);

        bool passwordFocused = false;
        IntPtr passwordWindow = IntPtr.Zero;   // the window where the password field was found; the flag clears when it loses the foreground
        int count = 0;
        long micros = 0;
        while (true)
        {
            // Check what has focus on focus changes and while typing. Events alone aren't enough: a browser reports
            // the password field late, so at event time the focus is still the browser window, not the field
            signal.WaitOne();
            // The time is taken before the focus is read: a check that started before a word may not vouch for it
            int stamp = Environment.TickCount | 1;
            long started = Stopwatch.GetTimestamp();
            IntPtr foreground = W.GetForegroundWindow();
            bool password = passwordFocused && foreground == passwordWindow;
            // The focus couldn't be read (the app failed or didn't answer): this check vouches for nothing, so the
            // word isn't corrected, as if no check had been made. Never taken for "not a password"
            bool unsure = true;
            try
            {
                System.Windows.Automation.AutomationElement element;
                using (cache.Activate()) element = System.Windows.Automation.AutomationElement.FocusedElement;
                if (element != null)
                {
                    var info = element.Cached;
                    var type = info.ControlType;
                    // Only a text field can change the decision. Browser popups ("suggest a strong password",
                    // autofill) take focus for a moment, and at first the browser reports the whole page
                    // (Document) as focused; neither means the password field was left
                    if (info.IsPassword) password = true;
                    else if (type == System.Windows.Automation.ControlType.Edit ||
                             type == System.Windows.Automation.ControlType.ComboBox) password = false;
                    unsure = false;
                }
            }
            catch { }
            if (password) passwordWindow = foreground;
            passwordFocused = password;
            view.Write(0, ((long)stamp << 32) | (password ? 1L : 0L) | (unsure ? 2L : 0L));
            micros += (Stopwatch.GetTimestamp() - started) * 1000000 / Stopwatch.Frequency;
            view.Write(40, ++count);
            view.Write(48, micros);
            Thread.Sleep(100); // at most ten times a second; keystrokes in between merge into one check
        }
    }
}
