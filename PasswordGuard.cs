using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Windows.Forms;

// The password-field check runs in a separate KeyFish process ("KeyFish.exe --password-guard <pid>").
// UI Automation can crash the whole process on some windows (a .NET runtime failure that can't be caught);
// isolated, such a crash only restarts the guard while KeyFish keeps running.
// Shared memory: offset 0 — int, 1 if focus is in a password field; 4 — int, Environment.TickCount of the last check.
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
    DateTime windowStart = DateTime.UtcNow;
    int restartsInWindow;

    public PasswordGuardClient()
    {
        memory = MemoryMappedFile.CreateNew(GuardNames.Memory(pid), 8);
        view = memory.CreateViewAccessor(0, 8);
        signal = new EventWaitHandle(false, EventResetMode.AutoReset, GuardNames.Signal(pid));
        Start();
    }

    public bool Password { get { return view.ReadInt32(0) != 0; } }
    public int CheckedAt { get { return view.ReadInt32(4); } }

    // Ask the guard to look at the focus now; cheap enough to call on every keystroke
    public void RequestCheck()
    {
        try { signal.Set(); } catch { }
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

        try { System.Windows.Automation.Automation.AddAutomationFocusChangedEventHandler((s, e) => signal.Set()); }
        catch { }

        bool passwordFocused = false;
        IntPtr passwordWindow = IntPtr.Zero;   // the window where the password field was found; the flag clears when it loses the foreground
        while (true)
        {
            // Check what has focus on focus changes and while typing. Events alone aren't enough: a browser reports
            // the password field late, so at event time the focus is still the browser window, not the field
            signal.WaitOne();
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
            view.Write(0, password ? 1 : 0);
            Thread.MemoryBarrier();
            view.Write(4, Environment.TickCount);   // written last: marks the verdict above as fresh
            Thread.Sleep(100); // at most ten times a second; keystrokes in between merge into one check
        }
    }
}
