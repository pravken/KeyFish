using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Windows.Forms;

// The password-field check runs in a separate KeyFish process ("KeyFish.exe --password-guard <pid>").
// UI Automation can crash the whole process on some windows (a .NET runtime failure that can't be caught);
// isolated, such a crash only restarts the guard while KeyFish keeps running.
// Shared memory: one 64-bit value, written and read in one go so a verdict is never paired with another check's time.
// Low half: 1 if focus is in a password field. High half: Environment.TickCount taken right before the focus was read,
// never 0 — 0 means no check has been made yet.
static class GuardNames
{
    public static string Memory(int pid) { return "Local\\KeyFish_Guard_" + pid; }
    public static string Signal(int pid) { return "Local\\KeyFish_GuardCheck_" + pid; }
}

// Main process side: starts the guard, restarts it if it dies, and reads its verdict
class PasswordGuardClient
{
    readonly int pid = Process.GetCurrentProcess().Id;
    readonly MemoryMappedFile memory;
    readonly MemoryMappedViewAccessor view;
    readonly EventWaitHandle signal;
    readonly object sync = new object();
    Process guard;
    DateTime guardStarted;
    DateTime windowStart = DateTime.UtcNow;
    int restartsInWindow;

    public PasswordGuardClient()
    {
        memory = MemoryMappedFile.CreateNew(GuardNames.Memory(pid), 8);
        view = memory.CreateViewAccessor(0, 8);
        signal = new EventWaitHandle(false, EventResetMode.AutoReset, GuardNames.Signal(pid));
        Start();
    }

    // The last verdict and when the focus was read for it (0: never)
    public void Read(out bool password, out int checkedAt)
    {
        long v = view.ReadInt64(0);
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
        EventWaitHandle signal;
        Process parent;
        try
        {
            view = MemoryMappedFile.OpenExisting(GuardNames.Memory(parentPid)).CreateViewAccessor(0, 8);
            signal = EventWaitHandle.OpenExisting(GuardNames.Signal(parentPid));
            parent = Process.GetProcessById(parentPid);
        }
        catch { return; }

        // Leave together with KeyFish
        new Thread(() => { parent.WaitForExit(); Environment.Exit(0); }) { IsBackground = true }.Start();

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
