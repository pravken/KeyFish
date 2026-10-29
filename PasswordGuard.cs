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
// Low half: 1 if focus is in a password field. High half: Environment.TickCount taken right before the focus was read,
// never 0 — 0 means no check has been made yet.
// The guard also finds the text caret of browsers and Electron apps for the layout tag (accessibility objects, MSAA):
// an app's broken accessibility object took the whole process down the same way. Bytes 8–15: the window asked about,
// 16–19: the question's number; 20–23: the number answered, 24–39: the caret (left, top, width, height; height 0: none).
static class GuardNames
{
    public const int Size = 40;
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

    // The last verdict and when the focus was read for it (0: never)
    public void Read(out bool password, out int checkedAt)
    {
        // One 8-byte read is atomic in a 64-bit process; a 32-bit one reads it in two halves, so it reads until two
        // reads agree, and a verdict is never paired with another check's time
        long v = view.ReadInt64(0);
        if (IntPtr.Size == 4)
            for (long again = view.ReadInt64(0); again != v; again = view.ReadInt64(0)) v = again;
        password = (v & 1) != 0;
        checkedAt = (int)(v >> 32);
    }

    public bool Password { get { bool p; int t; Read(out p, out t); return p; } }

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
            bool p; int at;
            Read(out p, out at);
            if (at != 0 && at - asked >= 0) { unansweredSince = 0; return true; }
            return Environment.TickCount - asked < 3000;
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
        // question. The answer's number is written last, after the caret
        new Thread(() =>
        {
            while (true)
            {
                caretAsk.WaitOne();
                IntPtr hwnd = new IntPtr(view.ReadInt64(8));
                int question = view.ReadInt32(16);
                var ask = new Thread(() =>
                {
                    Rectangle r;
                    if (!LayoutBadge.AccessibleCaret(hwnd, out r)) r = Rectangle.Empty;
                    lock (view)
                    {
                        view.Write(24, r.X); view.Write(28, r.Y); view.Write(32, r.Width); view.Write(36, r.Height);
                        view.Write(20, question);
                    }
                    caretAnswer.Set();
                }) { IsBackground = true };
                ask.SetApartmentState(ApartmentState.MTA);
                ask.Start();
            }
        }) { IsBackground = true }.Start();

        // Subscribing to focus changes can take from seconds to over a minute with many heavy windows open
        // (browsers, Electron apps, editors): UI Automation visits all of them. Done on its own thread, so the checks
        // below start answering at once; until it's done, checks still come with every keystroke
        new Thread(() =>
        {
            try { System.Windows.Automation.Automation.AddAutomationFocusChangedEventHandler((s, e) => signal.Set()); }
            catch { }
        }) { IsBackground = true }.Start();

        bool passwordFocused = false;
        IntPtr passwordWindow = IntPtr.Zero;   // the window where the password field was found; the flag clears when it loses the foreground
        while (true)
        {
            // Check what has focus on focus changes and while typing. Events alone aren't enough: a browser reports
            // the password field late, so at event time the focus is still the browser window, not the field
            signal.WaitOne();
            // The time is taken before the focus is read: a check that started before a word may not vouch for it
            int stamp = Environment.TickCount | 1;
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
            view.Write(0, ((long)stamp << 32) | (password ? 1L : 0L));
            Thread.Sleep(100); // at most ten times a second; keystrokes in between merge into one check
        }
    }
}
