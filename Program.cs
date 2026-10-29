using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [DllImport("kernel32.dll")] static extern bool SetDefaultDllDirectories(uint flags);

    [STAThread]
    static void Main(string[] args)
    {
        // System DLLs only from System32. KeyFish is often run from Downloads, and a dwmapi.dll or uxtheme.dll that
        // happens to lie next to it would otherwise be loaded instead of Windows' own
        try { SetDefaultDllDirectories(0x800); } catch { }   // LOAD_LIBRARY_SEARCH_SYSTEM32

        // The helper process that watches for password fields (see PasswordGuard.cs)
        int parentPid = 0;
        bool isGuard = args.Length == 2 && args[0] == "--password-guard" && int.TryParse(args[1], out parentPid);
        if (isGuard)
        {
            CrashLog.Start(App.DataDir(), true);
            PasswordGuardProcess.Run(parentPid);
            return;
        }

        bool updated = Array.IndexOf(args, "--updated") >= 0;
        bool created;
        using (var mutex = new Mutex(true, "KeyFish_SingleInstance", out created))
        {
            if (!created)
            {
                // After an update the old copy is still closing, so wait for it instead of exiting.
                // If it hangs, it is closed: otherwise the user would be left with no KeyFish at all
                if (!updated) return;
                try
                {
                    if (!mutex.WaitOne(15000))
                    {
                        int self = Process.GetCurrentProcess().Id;
                        foreach (var p in Process.GetProcessesByName("KeyFish"))
                            try { if (p.Id != self) p.Kill(); } catch { }
                        if (!mutex.WaitOne(5000)) return;
                    }
                }
                catch (AbandonedMutexException) { }
            }
            CrashLog.Start(App.DataDir(), false);   // only the copy that runs: a second one has just quit above
            // Sharp text and controls on scaled screens instead of a stretched bitmap
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.Run(new App(updated));
        }
    }
}

// The tray app. Its other parts: Tray.cs (the icon and menu), Hotkeys.cs (hotkeys and the clipboard), Updates.cs
partial class App : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "KeyFish";

    const string SettingsKey = @"Software\KeyFish";
    static string DefaultExclusions
    {
        get
        {
            return L.T("# Программы, где автоисправление при наборе выключено.\r\n" +
                       "# Имя процесса по одному в строке (как в Диспетчере задач, .exe можно не писать).\r\n",
                       "# Apps where auto-correction is off.\r\n" +
                       "# One process name per line, as in Task Manager (.exe is optional).\r\n") +
                   "Code\r\ndevenv\r\ncmd\r\npowershell\r\npwsh\r\nWindowsTerminal\r\nidea64\r\npycharm64\r\nrider64\r\n" +
                   "KeePass\r\nKeePassXC\r\n1Password\r\nmstsc\r\n" + string.Join("\r\n", AddedExclusions.Concat(AddedExclusions3)) + "\r\n";
        }
    }

    // Added in 2.0: terminals and SSH clients, where a password prompt is invisible to the password check;
    // virtual machines and remote desktops, which type with the layout of the other computer; password managers.
    // Existing exclusion lists get them once (see AddNewExclusions)
    static readonly string[] AddedExclusions =
    {
        "conhost", "OpenConsole", "wsl", "ssh", "putty", "kitty", "mintty", "wezterm-gui", "alacritty", "MobaXterm", "Termius",
        "Tabby", "Hyper", "vmware", "vmplayer", "VirtualBoxVM", "vmconnect", "AnyDesk", "TeamViewer", "rustdesk", "parsecd",
        "Bitwarden", "Enpass"
    };

    // Added in 2.1.5: remote desktop clients other than mstsc — the Windows App and the Remote Desktop app (msrdc),
    // connection managers and VNC viewers. What is typed there goes to another computer with its own layout
    static readonly string[] AddedExclusions3 =
    {
        "msrdc", "msrdcw", "Windows365", "RDCMan", "mRemoteNG", "RoyalTS", "RemoteDesktopManager", "RemoteDesktopManager64",
        "vncviewer", "tvnviewer"
    };
    const int ExclusionsVersion = 3;

    // Adds the new default exclusions to an older list, once: if the user removes one later, it stays removed
    void AddNewExclusions()
    {
        int version = GetSetting("ExclusionsVersion", 1);
        if (version >= ExclusionsVersion) return;
        try
        {
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(exclusionsPath, Encoding.UTF8))
            {
                string s = line.Trim();
                if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
                have.Add(s);
            }
            var add = new StringBuilder();
            // Each list once: a name the user removed after an earlier list was added stays removed
            var added = (version < 2 ? AddedExclusions : new string[0]).Concat(AddedExclusions3);
            foreach (string name in added) if (!have.Contains(name)) add.Append(name).Append("\r\n");
            string text = File.ReadAllText(exclusionsPath, Encoding.UTF8);
            if (add.Length > 0) File.WriteAllText(exclusionsPath, text + (text.Length == 0 || text.EndsWith("\n") ? "" : "\r\n") + add, Encoding.UTF8);
            SetSetting("ExclusionsVersion", ExclusionsVersion);
        }
        catch { }
    }

    // Settings files live next to the exe, so KeyFish stays portable — when that folder is the user's own: inside their
    // profile (Downloads, Desktop), already holding KeyFish's files, or marked with an empty portable.txt. A shared
    // folder (C:\Tools, a network drive) would mix several people's word lists, so then, and when the folder is
    // read-only (Program Files), they go to %APPDATA%\KeyFish
    public static string DataDir()
    {
        string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
        try
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            bool own = (profile.Length > 0 && exeDir.StartsWith(profile.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) ||
                       File.Exists(Path.Combine(exeDir, "portable.txt")) || File.Exists(Path.Combine(exeDir, "replacements.txt")) ||
                       File.Exists(Path.Combine(exeDir, "exclusions.txt"));
            if (own)
            {
                using (new FileStream(Path.Combine(exeDir, "~keyfish-write-test.tmp"), FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return exeDir;
            }
        }
        catch { }
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyFish");
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    static string DefaultReplacements
    {
        get
        {
            return L.T("# Автозамены: наберите сокращение и пробел — оно заменится на текст.\r\n" +
                       "# Формат: сокращение = замена. \\n внутри замены — перенос строки.\r\n" +
                       "# Уберите # в начале строки, чтобы включить пример.\r\n" +
                       "# спс = спасибо\r\n# пжл = пожалуйста\r\n# сув = С уважением,\\nВаше имя\r\n",
                       "# Replacements: type an abbreviation and a space to expand it.\r\n" +
                       "# Format: abbreviation = text. \\n inside the text is a line break.\r\n" +
                       "# Remove # at the start of a line to turn an example on.\r\n" +
                       "# thx = thank you\r\n# brb = be right back\r\n# br = Best regards,\\nYour name\r\n");
        }
    }

    readonly HotkeyWindow hotkeys = new HotkeyWindow();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly KeyboardWatcher watcher;
    bool busy;

    // The tray fish: blue for a Latin layout, orange for a Cyrillic one; it blinks after an auto-fix
    Icon iconEn, iconRu, blinkEn, blinkRu;
    bool showLayout, currentRu;
    string currentLayoutName = "";
    int seenCorrections, blinkTicks, seenTypos, seenUndos;
    readonly System.Windows.Forms.Timer trayTimer = new System.Windows.Forms.Timer();
    SynchronizationContext uiContext;
    volatile bool idle;
    volatile int lastActivity;
    Native.WinEventProc foregroundProc;   // kept referenced: Windows calls it for as long as the hook lives
    IntPtr foregroundHook;

    readonly int ownPid = Process.GetCurrentProcess().Id;

    IntPtr lastForeground;
    Dictionary<string, long> appLayouts = new Dictionary<string, long>();

    // A window of an app with its own layout (Settings → Apps) came to the front: switch to that layout.
    // Only on switching to the window, so the user can still change the layout by hand while in it
    void ApplyAppLayout(IntPtr hwnd, uint pid)
    {
        if (appLayouts.Count == 0) return;
        long hkl;
        if (!appLayouts.TryGetValue(W.ProcessName(hwnd), out hkl)) return;
        var target = new IntPtr(hkl);
        if (watcher.Layouts.Get(target) == null) target = SameLayout(hkl);
        if (target == IntPtr.Zero || W.LayoutHkl(hwnd) == target) return;
        W.SwitchLayout(hwnd, target);
    }

    // A layout saved for an app that isn't installed under that handle any more: custom layouts (made with MSKLC, an
    // IME) get a new upper half after a restart. The one installed layout of the same language and kind stands in
    IntPtr SameLayout(long hkl)
    {
        IntPtr found = IntPtr.Zero;
        foreach (var t in watcher.Layouts.All)
        {
            long h = t.Hkl.ToInt64();
            bool sameKind = ((h >> 28) & 0xF) == ((hkl >> 28) & 0xF);   // 0xF…: a custom layout
            if ((h & 0xFFFF) != (hkl & 0xFFFF) || !sameKind) continue;
            if (found != IntPtr.Zero) return IntPtr.Zero;   // two candidates: don't guess
            found = t.Hkl;
        }
        return found;
    }

    void LoadAppLayouts() { appLayouts = SettingsForm.ParseAppLayouts(ReadText(appLayoutsPath)); }

    IntPtr lastHkl;
    string currentCulture;   // the layout's culture name, "ru-RU", for the tray menu
    bool playSound, trayLetters;
    bool foregroundNoRights;   // the window in front runs as administrator: KeyFish can't work in it (the fish is grey)

    // Letter icons are made once per layout, color and state, and kept (a handful in all)
    readonly Dictionary<string, Icon> letterIcons = new Dictionary<string, Icon>();

    static string GetText(string name)
    {
        try { using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey)) return k == null ? null : k.GetValue(name) as string; }
        catch { return null; }
    }

    static void SetText(string name, string value)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey))
            if (!((value ?? "").Equals(k.GetValue(name) as string))) k.SetValue(name, value ?? "", RegistryValueKind.String);
    }

    LayoutBadge badge;
    bool showBadge;
    Icon iconOff;                        // grey fish: auto-correction is off or paused

    // Auto-correction as set by the user, and a pause from the tray menu on top of it
    bool autoCorrect;
    DateTime pausedUntil = DateTime.MinValue;

    void ApplyEnabled() { watcher.Enabled = autoCorrect && pausedUntil == DateTime.MinValue; }

    // Statistics: total mistakes fixed and the day counting started
    int statsBase, savedCorrections;

    // A value broken in regedit (text instead of a number) reads as the default instead of stopping KeyFish
    static int GetSetting(string name, int def)
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                return k == null ? def : Convert.ToInt32(k.GetValue(name, def));
        }
        catch { return def; }
    }

    // Settings apply at every change in the window: a value that is already there isn't written again
    static void SetSetting(string name, int value)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey))
        {
            object old = k.GetValue(name);
            if (old is int && (int)old == value) return;
            k.SetValue(name, value, RegistryValueKind.DWord);
        }
    }

    readonly string exclusionsPath, ignoredPath, replacementsPath, learnedPath, appLayoutsPath, backupDir;
    ToolStripMenuItem autoItem;
    SettingsForm settingsForm;

    // Before 1.6.0 the app was called LayoutFix: carry over its settings and autostart
    static void MigrateFromLayoutFix()
    {
        try
        {
            using (var old = Registry.CurrentUser.OpenSubKey(@"Software\LayoutFix"))
            using (var current = Registry.CurrentUser.OpenSubKey(SettingsKey))
            {
                if (old != null && current == null)
                    using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey))
                        foreach (string name in old.GetValueNames()) k.SetValue(name, old.GetValue(name), old.GetValueKind(name));
            }
            using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (run != null && run.GetValue("LayoutFix") != null)
                {
                    run.DeleteValue("LayoutFix", false);
                    run.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
                }
            }
        }
        catch { }
    }

    public App(bool updated)
    {
        // Work handed to this thread (double Shift from the keyboard hook, window changes, update checks) is posted
        // through the context caught here. Application.Run installs the window one only later, and before it
        // there may be none or a bare one (whatever ran first may have set it), which runs posts on the thread pool,
        // where the clipboard can't be read: so double Shift once found no word. The window one is installed now
        if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        MigrateFromLayoutFix();
        RepairAutostart();
        L.Setting = GetSetting("Language", 0);
        Theme.Mode = Math.Max(0, Math.Min(GetSetting("Theme", 0), 2));
        Theme.Chosen = GetSetting("Skin", 0);
        Theme.Holidays = GetSetting("Holiday", 1) != 0;
        DateTime.TryParseExact(GetSetting("HolidayDeclined", 0).ToString(), "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out Theme.Declined);
        Theme.SkinLogo = GetSetting("SkinLogo", 1) != 0;
        Theme.SkinSound = GetSetting("SkinSound", 1) != 0;
        Skin.SetCustom(Color.FromArgb(GetSetting("CustomAccent", 0)), Color.FromArgb(GetSetting("CustomFish", 0)));
        Theme.Refresh();
        statsBase = GetSetting("StatsTotal", 0);
        if (GetSetting("StatsSince", 0) == 0) SetSetting("StatsSince", int.Parse(DateTime.Today.ToString("yyyyMMdd")));
        // The day the count by day started (2.1.0): a week or a month before it isn't known
        if (GetSetting("StatsDaysSince", 0) == 0) SetSetting("StatsDaysSince", Daily().Count > 0 ? Daily().Keys.Min() : DayKey(DateTime.Today));
        string dir = DataDir();
        Dictionaries.Dir = Path.Combine(dir, "dictionaries");
        Detector.Extra = Dictionaries.Lines;
        exclusionsPath = Path.Combine(dir, "exclusions.txt");
        ignoredPath = Path.Combine(dir, "ignored-words.txt");
        learnedPath = Path.Combine(dir, "learned-words.txt");
        replacementsPath = Path.Combine(dir, "replacements.txt");
        appLayoutsPath = Path.Combine(dir, "app-layouts.txt");
        backupDir = Path.Combine(dir, "backups");
        try
        {
            if (!File.Exists(exclusionsPath))
            {
                File.WriteAllText(exclusionsPath, DefaultExclusions, Encoding.UTF8);
                SetSetting("ExclusionsVersion", ExclusionsVersion);
            }
            else AddNewExclusions();
            if (!File.Exists(replacementsPath)) File.WriteAllText(replacementsPath, DefaultReplacements, Encoding.UTF8);
        }
        catch { }

        watcher = new KeyboardWatcher(exclusionsPath, ignoredPath, replacementsPath, learnedPath)
        {
            FixTypos = GetSetting("Typos", 0) != 0,
            QuietFullscreen = GetSetting("QuietFullscreen", 1) != 0,
            UndoWithCtrlZ = GetSetting("UndoCtrlZ", 1) != 0,
            KeepRecent = GetSetting("RecentMenu", 1) != 0,
            Typography = GetSetting("Typography", 0) != 0,
            QuickKey = GetSetting("QuickKey", 0)
        };
        autoCorrect = GetSetting("AutoCorrect", 1) != 0;
        ApplyEnabled();
        showLayout = GetSetting("LayoutColor", 1) != 0;
        showBadge = GetSetting("LayoutBadge", 0) != 0;
        playSound = GetSetting("Sound", 0) != 0;
        trayLetters = GetSetting("TrayLetters", 0) != 0;
        layoutColors = LayoutColors.Parse(GetText("LayoutColors"));
        Sounds.Dir = Path.Combine(dir, "sounds");
        Sounds.Load(Sounds.Parse(GetText("Sounds")));
        // Double Shift or Caps Lock comes from the keyboard hook's thread: hand it to this one
        var ui = SynchronizationContext.Current;
        watcher.QuickFix += () => ui.Post(_ =>
        {
            if (settingsForm == null && W.GetForegroundWindow() != IntPtr.Zero) OnHotkey(HkLayout);
        }, null);
        LoadAppLayouts();

        var menu = BuildMenu();
        SystemEvents.UserPreferenceChanged += OnSystemPreference;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        iconEn = MakeIcon("fish.ico");
        iconRu = MakeIcon("fish-ru.ico");
        blinkEn = MakeIcon("fish-blink.ico");
        blinkRu = MakeIcon("fish-ru-blink.ico");
        iconOff = Grey(iconEn);
        iconPaused = Paused(iconEn);

        tray.Icon = iconEn;
        tray.Text = "KeyFish " + Application.ProductVersion;
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (s, e) => { if (!busy) OpenSettings(); };
        tray.Visible = true;

        // The tray icon follows the window in front and its layout. Windows says when another window comes to the front;
        // a layout change has no such signal, so it is looked for often only while keys are pressed or clicked (that's
        // how layouts are switched) and while the icon blinks, and once a second otherwise
        uiContext = SynchronizationContext.Current;
        // Called by Windows from outside any handler of KeyFish's: an error here must not reach it
        foregroundProc = (hook, ev, hwnd, idObject, idChild, thread, time) =>
        {
            lastActivity = Environment.TickCount;
            try { Wake(); } catch (Exception ex) { CrashLog.Write("error", ex); }
        };
        foregroundHook = Native.SetWinEventHook(3, 3, IntPtr.Zero, foregroundProc, 0, 0, 0);   // EVENT_SYSTEM_FOREGROUND, out of context
        watcher.Activity = () =>
        {
            lastActivity = Environment.TickCount;
            if (idle) { idle = false; var c = uiContext; if (c != null) c.Post(_ => Wake(), null); }
        };
        trayTimer.Interval = 150;
        trayTimer.Tick += (s, e) =>
        {
            UpdateTrayIcon();
            bool busyNow = blinkTicks > 0 || Environment.TickCount - lastActivity < 3000;
            trayTimer.Interval = busyNow ? 150 : 1000;
            idle = !busyNow;
        };
        trayTimer.Start();

        hotkeys.Pressed += OnHotkey;
        updates.Check = CheckNow;
        updates.Install = () => { if (updates.NewVersion) OfferUpdate(updates.Latest); };
        WarnHotkeys(RegisterHotkeys());

        // Updates: half a minute after start, then once a day
        tray.BalloonTipClicked += (s, e) => { if (pendingRelease != null) OfferUpdate(pendingRelease); };
        if (updated)
            tray.ShowBalloonTip(5000, "KeyFish " + Application.ProductVersion,
                L.T("Обновление установлено", "Update installed"), ToolTipIcon.Info);
        updateTimer.Interval = 30 * 1000;
        updateTimer.Tick += (s, e) =>
        {
            updateTimer.Interval = 24 * 60 * 60 * 1000;
            if (GetSetting("CheckUpdates", 1) != 0) CheckForUpdates(false);
        };
        updateTimer.Start();

        // A new user (no settings saved yet, nothing corrected yet) gets a short introduction once
        if (GetSetting("IntroShown", 0) == 0)
        {
            SetSetting("IntroShown", 1);
            if (statsBase == 0)
            {
                var intro = new System.Windows.Forms.Timer { Interval = 1500 };
                intro.Tick += (s, e) => { intro.Stop(); intro.Dispose(); using (var f = new IntroForm()) f.ShowDialog(); };
                intro.Start();
            }
        }

        // The last run ended in a crash: once Windows' record of it is read, offer to report it (a few seconds after
        // start, so the tray icon is there and KeyFish is running again before anything asks for attention)
        var crashOffer = new System.Windows.Forms.Timer { Interval = 8000 };
        crashOffer.Tick += (s, e) => { crashOffer.Stop(); crashOffer.Dispose(); if (CrashLog.LastCrash != null) OfferCrashReport(CrashLog.LastCrash); };
        crashOffer.Start();

        // The copy left by an update is removed after a minute of normal work, not at once: if the new version
        // failed to start, KeyFish.old.exe would still be there to go back to
        var cleanup = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
        cleanup.Tick += (s, e) => { cleanup.Stop(); cleanup.Dispose(); Updater.CleanUp(); };
        cleanup.Start();
    }

    // Nothing is sent by KeyFish itself: the report opens as a new GitHub issue for the user to read and submit,
    // or goes to the clipboard to send any other way
    void OfferCrashReport(string record)
    {
        string report = "KeyFish " + Application.ProductVersion + "\r\n\r\n" + record;
        int choice = ThemedMessage.Choose(
            L.T("Предыдущий сеанс KeyFish завершился с ошибкой. Рекомендуется отправить отчёт разработчику. Отчёт содержит только описание ошибки от Windows и не содержит набранного текста; перед отправкой его можно просмотреть на GitHub.",
                "The previous KeyFish session ended with an error. Sending a report to the developer is recommended. The report contains only Windows' description of the error and no typed text; it can be reviewed on GitHub before sending."),
            "KeyFish", MessageBoxIcon.Warning,
            L.T("Открыть на GitHub", "Open on GitHub"), L.T("Копировать", "Copy"), L.T("Не отправлять", "Don't send"));
        try
        {
            if (choice == 0) Process.Start(IssueUrl(report));
            else if (choice == 1) Clipboard.SetText(report);
        }
        catch { }
    }

    // A new issue with the report filled in; long reports are cut so the address stays within what browsers take
    static string IssueUrl(string report)
    {
        string title = Uri.EscapeDataString("Crash in KeyFish " + Application.ProductVersion);
        for (int keep = report.Length; ; keep = keep * 3 / 4)
        {
            string body = "What I was doing when it closed (which app, which key):\n\n\n```\n" +
                          (keep < report.Length ? report.Substring(0, keep) + "\n…" : report) + "\n```";
            string url = "https://github.com/" + Updater.Repo + "/issues/new?title=" + title + "&body=" + Uri.EscapeDataString(body);
            if (url.Length <= 7500 || keep < 200) return url;
        }
    }

    static string ReadText(string path) { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : ""; }

    static void WriteIfChanged(string path, string text)
    {
        if (!File.Exists(path) || ReadText(path) != text) File.WriteAllText(path, text, Encoding.UTF8);
    }

    void OpenSettings()
    {
        if (settingsForm != null) { settingsForm.Activate(); return; }
        FileQueue.Flush(2000);   // words KeyFish has just learned reach the files before Settings reads them
        string ignoredBefore = ReadText(ignoredPath), learnedBefore = ReadText(learnedPath);

        // The gallery marks the look in use, a holiday's own one on its days too: choosing another one is then seen
        int shownSkin = Theme.Contrast ? Theme.Chosen : Theme.Skin.Number;

        // Global hotkeys are off while the window is open, otherwise they couldn't be assigned
        hotkeys.UnregisterAll();
        try
        {
            // Apply keeps the window open, and a look applies as soon as it is picked
            settingsForm = NewSettingsForm(shownSkin, ignoredBefore, learnedBefore);
            // Settings apply at every change: the copy is made once, before the first of them, and again before an import
            bool backedUp = false;
            settingsForm.Apply = f =>
            {
                bool rebuild = ApplySettings(f, shownSkin, ignoredBefore, learnedBefore, !backedUp || f.Replacing);
                backedUp = true;
                // The next Apply starts from this one: the word lists as they were in the window, the look now in use
                ignoredBefore = f.Ignored;
                learnedBefore = f.Learned;
                shownSkin = Theme.Contrast ? Theme.Chosen : Theme.Skin.Number;
                return rebuild;
            };
            settingsForm.ApplyLook = f =>
            {
                if (!backedUp) { Backup(ignoredBefore, learnedBefore); backedUp = true; }
                bool rebuild = ApplyLook(f, shownSkin);
                shownSkin = Theme.Contrast ? Theme.Chosen : Theme.Skin.Number;
                return rebuild;
            };
            settingsForm.ShowDialog();
        }
        finally
        {
            if (settingsForm != null) settingsForm.Dispose();
            settingsForm = null;
            WarnHotkeys(RegisterHotkeys());
        }
    }

    SettingsForm NewSettingsForm(int shownSkin, string ignored, string learned)
    {
        var keys = new Keys[HotkeyDefaults.Length];
        for (int i = 0; i < keys.Length; i++) keys[i] = GetHotkey(i);
        var form = new SettingsForm
        {
            OpenAbout = owner => ShowAbout(owner), Updates = updates,
            BackupDir = backupDir, TrayLetters = trayLetters, LayoutColors = new Dictionary<string, Color>(layoutColors, StringComparer.OrdinalIgnoreCase), AutoCorrect = autoCorrect, LayoutBadge = showBadge, Sound = playSound, SoundMap = new Dictionary<string, string>(Sounds.Map), SoundsDir = Sounds.Dir, RecentMenu = watcher.KeepRecent, Typography = watcher.Typography, QuickKey = watcher.QuickKey, Typos = watcher.FixTypos, QuietFullscreen = watcher.QuietFullscreen, UndoCtrlZ = watcher.UndoWithCtrlZ, LayoutColor = showLayout, Autostart = IsAutostart(),
            CheckUpdates = GetSetting("CheckUpdates", 1) != 0, Hotkeys = keys, Language = L.Setting, ThemeMode = Theme.Mode, SkinNumber = shownSkin, Holiday = Theme.Holidays, SkinLogo = Theme.SkinLogo, SkinSound = Theme.SkinSound, CustomAccent = Skin.Custom.Accent, CustomFish = Skin.Custom.Latin,
            Replacements = ReadText(replacementsPath), Exclusions = ReadText(exclusionsPath), Ignored = ignored, Learned = learned,
            AppLayouts = ReadText(appLayoutsPath), InstalledLayouts = new List<LayoutTable>(watcher.Layouts.All)
        };
        form.Icon = iconEn;
        return form;
    }

    // Settings → Apply: KeyFish takes the window's values and keeps them. True if the window has to be opened again
    // to show them (another language, theme or look)
    bool ApplySettings(SettingsForm f, int shownSkin, string ignoredBefore, string learnedBefore, bool backup)
    {
        // What was there before the first change in the window goes to backups\, the newest ten are kept
        if (backup) Backup(ignoredBefore, learnedBefore);
        autoCorrect = f.AutoCorrect; ApplyEnabled(); SetSetting("AutoCorrect", f.AutoCorrect ? 1 : 0);
        showBadge = f.LayoutBadge; SetSetting("LayoutBadge", f.LayoutBadge ? 1 : 0);
        playSound = f.Sound; SetSetting("Sound", f.Sound ? 1 : 0);
        Sounds.Load(f.SoundMap); SetText("Sounds", Sounds.Format(f.SoundMap)); Sounds.Forget();
        trayLetters = f.TrayLetters; SetSetting("TrayLetters", f.TrayLetters ? 1 : 0);
        layoutColors = new Dictionary<string, Color>(f.LayoutColors, StringComparer.OrdinalIgnoreCase); SetText("LayoutColors", LayoutColors.Format(f.LayoutColors));
        watcher.KeepRecent = f.RecentMenu; SetSetting("RecentMenu", f.RecentMenu ? 1 : 0);
        watcher.Typography = f.Typography; SetSetting("Typography", f.Typography ? 1 : 0);
        if (!f.RecentMenu) watcher.ClearRecent();
        watcher.QuickKey = f.QuickKey; SetSetting("QuickKey", f.QuickKey);
        watcher.FixTypos = f.Typos; SetSetting("Typos", f.Typos ? 1 : 0);
        watcher.QuietFullscreen = f.QuietFullscreen; SetSetting("QuietFullscreen", f.QuietFullscreen ? 1 : 0);
        watcher.UndoWithCtrlZ = f.UndoCtrlZ; SetSetting("UndoCtrlZ", f.UndoCtrlZ ? 1 : 0);
        showLayout = f.LayoutColor; SetSetting("LayoutColor", f.LayoutColor ? 1 : 0);
        SetSetting("CheckUpdates", f.CheckUpdates ? 1 : 0);
        if (f.Autostart != IsAutostart()) SetAutostart(f.Autostart);
        for (int i = 0; i < f.Hotkeys.Length; i++) SetSetting("Hotkey" + (i + 1), (int)f.Hotkeys[i]);
        // Only what changed is written: settings apply at every change in the window
        WriteIfChanged(replacementsPath, f.Replacements);
        WriteIfChanged(exclusionsPath, f.Exclusions);
        // KeyFish keeps learning while the window is open: keep those changes along with the user's edits. A list
        // the user didn't touch stays as KeyFish keeps it
        if (f.Ignored != ignoredBefore || f.Learned != learnedBefore) FileQueue.Flush(2000);
        if (f.Ignored != ignoredBefore) WriteIfChanged(ignoredPath, MergeWords(f.Ignored, ignoredBefore, ReadText(ignoredPath)));
        if (f.Learned != learnedBefore) WriteIfChanged(learnedPath, MergeWords(f.Learned, learnedBefore, ReadText(learnedPath)));
        WriteIfChanged(appLayoutsPath, f.AppLayouts);
        LoadAppLayouts();
        watcher.ReloadSoon();
        bool themeChanged = ApplyLook(f, shownSkin);
        if (f.Language == L.Setting) return themeChanged;
        L.Setting = f.Language;
        SetSetting("Language", f.Language);
        RebuildMenu();
        return true;
    }

    // The look's part of Settings, applied as soon as it is picked there. True if anything about it changed
    bool ApplyLook(SettingsForm f, int shownSkin)
    {
        bool themeChanged = false;
        if (f.ThemeMode != Theme.Mode)
        {
            Theme.Mode = f.ThemeMode;
            SetSetting("Theme", f.ThemeMode);
            themeChanged = Theme.Refresh();
        }
        if (f.Holiday != Theme.Holidays || f.SkinNumber != shownSkin)
        {
            SetSetting("Holiday", f.Holiday ? 1 : 0);
            Theme.Holidays = f.Holiday;
            if (f.SkinNumber != shownSkin)
            {
                // Another look chosen on a holiday's days wins over the holiday's own one until they end
                SetSetting("Skin", f.SkinNumber);
                Theme.Chosen = f.SkinNumber;
                Theme.Declined = Skin.Declines(f.SkinNumber, DateTime.Today);
                SetSetting("HolidayDeclined", Theme.Declined == DateTime.MinValue ? 0 : int.Parse(Theme.Declined.ToString("yyyyMMdd")));
            }
            if (Theme.Refresh()) themeChanged = true;
        }
        Color accent = f.CustomAccent, fish = f.CustomFish;
        if (accent.ToArgb() != Skin.Custom.Accent.ToArgb() || fish.ToArgb() != Skin.Custom.Latin.ToArgb())
        {
            SetSetting("CustomAccent", accent.ToArgb());
            SetSetting("CustomFish", fish.ToArgb());
            Skin.SetCustom(accent, fish);
            Theme.Refresh();
            themeChanged = true;
        }
        bool fishOnly = false;
        if (f.SkinLogo != Theme.SkinLogo)
        {
            SetSetting("SkinLogo", f.SkinLogo ? 1 : 0);
            Theme.SkinLogo = f.SkinLogo;
            fishOnly = true;   // the fish only: the Settings window just redraws it
        }
        if (f.SkinSound != Theme.SkinSound)
        {
            SetSetting("SkinSound", f.SkinSound ? 1 : 0);
            Theme.SkinSound = f.SkinSound;
        }
        if (themeChanged || fishOnly) RebuildMenu();
        return themeChanged;
    }

    void Backup(string ignoredBefore, string learnedBefore)
    {
        try
        {
            Directory.CreateDirectory(backupDir);
            var files = new Dictionary<string, string>
            {
                { "replacements.txt", ReadText(replacementsPath) }, { "exclusions.txt", ReadText(exclusionsPath) },
                { "ignored-words.txt", ignoredBefore }, { "learned-words.txt", learnedBefore }, { "app-layouts.txt", ReadText(appLayoutsPath) },
                { "layout-colors.txt", GetText("LayoutColors") }, { "sounds.txt", GetText("Sounds") }
            };
            var values = new Dictionary<string, int>();
            using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                if (k != null)
                    foreach (string name in SettingsArchive.Values)
                    {
                        object v = k.GetValue(name);
                        if (v is int) values[name] = (int)v;
                    }
            SettingsArchive.Save(Path.Combine(backupDir, "settings-" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".zip"), files, values);
            var old = new DirectoryInfo(backupDir).GetFiles("settings-*.zip").OrderByDescending(x => x.Name).Skip(10);
            foreach (var x in old) x.Delete();
        }
        catch { }
    }

    // A word list as the user edited it in Settings, plus the words KeyFish added to the file meanwhile,
    // minus the ones it removed meanwhile (a word moved between "Never correct" and "Always correct")
    static string MergeWords(string edited, string before, string now)
    {
        var was = WordSet(before);
        var isNow = WordSet(now);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string w in Words(edited))
            if (!(was.Contains(w) && !isNow.Contains(w)) && seen.Add(w)) result.Add(w);
        foreach (string w in Words(now))
            if (!was.Contains(w) && seen.Add(w)) result.Add(w);
        return result.Count == 0 ? "" : string.Join("\r\n", result) + "\r\n";
    }

    static IEnumerable<string> Words(string text)
    {
        foreach (string line in (text ?? "").Split('\n'))
        {
            string s = line.Trim();
            if (s.Length > 0 && !s.StartsWith("#")) yield return s;
        }
    }

    static HashSet<string> WordSet(string text) { return new HashSet<string>(Words(text), StringComparer.OrdinalIgnoreCase); }

    // Path of the exe the autostart entry launches, or null if there is no entry
    static string AutostartTarget()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
        {
            var value = k == null ? null : k.GetValue(AppName) as string;
            return value == null ? null : value.Trim().Trim('"');
        }
    }

    // Autostart counts as on only if it launches an exe that exists
    static bool IsAutostart()
    {
        string target = AutostartTarget();
        return target != null && File.Exists(target);
    }

    // The entry points to an exe that is gone (moved or deleted copy): point it at this one instead,
    // otherwise autostart silently does nothing after a reboot
    static void RepairAutostart()
    {
        try
        {
            string target = AutostartTarget();
            if (target != null && !File.Exists(target)) SetAutostart(true);
        }
        catch { }
    }

    static void SetAutostart(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
            else k.DeleteValue(AppName, false);
        }
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.UserPreferenceChanged -= OnSystemPreference;   // a static event: it would keep KeyFish's objects alive
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SaveStats();
        FlushRestore();
        FileQueue.Flush(2000);
        tray.Visible = false;
        hotkeys.UnregisterAll();
        if (foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(foregroundHook);
        CrashLog.Stop();
        base.ExitThreadCore();
    }
}
