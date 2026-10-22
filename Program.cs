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
        int parentPid;
        if (args.Length == 2 && args[0] == "--password-guard" && int.TryParse(args[1], out parentPid))
        {
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
            // Sharp text and controls on scaled screens instead of a stretched bitmap
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.Run(new App(updated));
        }
    }
}

class App : ApplicationContext
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
                   "KeePass\r\nKeePassXC\r\n1Password\r\nmstsc\r\n" + string.Join("\r\n", AddedExclusions) + "\r\n";
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
    const int ExclusionsVersion = 2;

    // Adds the new default exclusions to an older list, once: if the user removes one later, it stays removed
    void AddNewExclusions()
    {
        if (GetSetting("ExclusionsVersion", 1) >= ExclusionsVersion) return;
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
            foreach (string name in AddedExclusions) if (!have.Contains(name)) add.Append(name).Append("\r\n");
            string text = File.ReadAllText(exclusionsPath, Encoding.UTF8);
            if (add.Length > 0) File.WriteAllText(exclusionsPath, text + (text.Length == 0 || text.EndsWith("\n") ? "" : "\r\n") + add, Encoding.UTF8);
            SetSetting("ExclusionsVersion", ExclusionsVersion);
        }
        catch { }
    }

    // Settings files live next to the exe, so KeyFish stays portable. If that folder is read-only
    // (Program Files, say), they go to %APPDATA%\KeyFish instead of KeyFish failing to start
    static string DataDir()
    {
        string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
        try
        {
            using (new FileStream(Path.Combine(exeDir, "~keyfish-write-test.tmp"), FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return exeDir;
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
    int seenCorrections, blinkTicks;
    readonly System.Windows.Forms.Timer trayTimer = new System.Windows.Forms.Timer();
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
        if (W.LayoutHkl(hwnd) == target || watcher.Layouts.Get(target) == null) return;
        W.SwitchLayout(hwnd, target);
    }

    void LoadAppLayouts() { appLayouts = SettingsForm.ParseAppLayouts(ReadText(appLayoutsPath)); }

    void UpdateTrayIcon()
    {
        IntPtr fg = W.GetForegroundWindow();
        uint pid;
        W.GetWindowThreadProcessId(fg, out pid);
        string cls = W.ClassName(fg);
        // While the tray or menu is open, don't show the taskbar's layout; it isn't the one being typed in
        if (fg != IntPtr.Zero && pid != ownPid && cls != "Shell_TrayWnd" && cls != "NotifyIconOverflowWindow"
            && cls != "TopLevelWindowForOverflowXamlIsland")
        {
            // The tray icon shows the alphabet of the layout: Latin or another one
            bool sameWindow = fg == lastForeground;
            if (!sameWindow) { lastForeground = fg; ApplyAppLayout(fg, pid); }
            IntPtr hkl = W.LayoutHkl(fg);
            var layout = watcher.Layouts.Get(hkl);
            if (layout != null) { currentRu = layout.Script != null && layout.Script != "Latin"; currentLayoutName = (layout.Name ?? "").Split('-')[0].ToUpperInvariant(); }
            // The layout changed in the window being typed in (by the user or by an auto-fix): show it at the caret
            if (sameWindow && hkl != lastHkl && lastHkl != IntPtr.Zero && showBadge && layout != null && currentLayoutName.Length > 0)
            {
                if (badge == null) badge = new LayoutBadge();
                badge.Flash(currentLayoutName, !currentRu);
            }
            lastHkl = hkl;
        }

        // A pause from the tray menu is over
        if (pausedUntil != DateTime.MinValue && DateTime.Now >= pausedUntil) { pausedUntil = DateTime.MinValue; ApplyEnabled(); }

        int corrections = watcher.CorrectionCount, events = corrections + watcher.ExpansionCount;
        if (events != seenCorrections)
        {
            seenCorrections = events;
            blinkTicks = 4;
            if (playSound) Bubble.Play();   // a fix or a replacement, like the blink
            if (corrections != savedCorrections)
            {
                SetSetting("StatsTotal", statsBase + corrections);
                savedCorrections = corrections;
            }
        }

        bool ru = showLayout && currentRu;
        // The password-field check isn't answering (starting, restarting): KeyFish corrects nothing until it does,
        // so the fish is grey and says why, instead of looking as if all is well
        bool guardDown = watcher.Enabled && !watcher.GuardAnswering;
        Icon want = !watcher.Enabled || guardDown ? iconOff : blinkTicks > 0 && blinkTicks % 2 == 0 ? (ru ? blinkRu : blinkEn) : (ru ? iconRu : iconEn);
        if (blinkTicks > 0) blinkTicks--;
        if (tray.Icon != want) tray.Icon = want;

        string text = "KeyFish " + Application.ProductVersion + (showLayout && currentLayoutName.Length > 0 ? " — " + currentLayoutName : "") +
                      "\n" + (guardDown ? L.T("Проверка полей паролей запускается…", "The password-field check is starting…")
                               : pausedUntil != DateTime.MinValue ? PausedText : StatsLine(statsBase + corrections));
        if (text.Length > 63) text = text.Substring(0, 63);
        if (tray.Text != text) tray.Text = text;
    }

    IntPtr lastHkl;
    bool playSound;
    LayoutBadge badge;
    bool showBadge;
    Icon iconOff;                        // grey fish: auto-correction is off or paused

    // Auto-correction as set by the user, and a pause from the tray menu on top of it
    bool autoCorrect;
    DateTime pausedUntil = DateTime.MinValue;

    void ApplyEnabled() { watcher.Enabled = autoCorrect && pausedUntil == DateTime.MinValue; }

    string PausedText { get { return L.F("Пауза до {0}", "Paused until {0}", pausedUntil.ToString("t", L.Culture)); } }

    // Statistics: total mistakes fixed and the day counting started
    int statsBase, savedCorrections;

    void SaveStats()
    {
        int corrections = watcher.CorrectionCount;
        if (corrections != savedCorrections) { SetSetting("StatsTotal", statsBase + corrections); savedCorrections = corrections; }
    }

    static string StatsLine(int total)
    {
        return L.F("Поймано ошибок: {0}", "Mistakes caught: {0}", total.ToString("N0", L.Culture));
    }

    static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        if (m10 == 1 && m100 != 11) return one;
        if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
        return many;
    }

    // owner: the window it opens over (Settings), or none when opened from the tray
    void ShowAbout(IWin32Window owner = null)
    {
        string changes = "";
        using (var s = typeof(App).Assembly.GetManifestResourceStream(L.Ru ? "changelog-ru.txt" : "changelog-en.txt"))
            if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) changes = r.ReadToEnd();

        int total = statsBase + watcher.CorrectionCount;
        int since = GetSetting("StatsSince", 0);
        string sinceText = "";
        try { if (since > 0) sinceText = new DateTime(since / 10000, since / 100 % 100, since % 100).ToString("d", L.Culture); }
        catch { since = 0; }
        string count = total.ToString("N0", L.Culture);
        string stats;
        if (L.Ru) stats = "Рыбка поймала " + count + " " + Plural(total, "ошибку", "ошибки", "ошибок") + (since > 0 ? " с " + sinceText : "");
        else if (L.Code == "en") stats = "The fish has caught " + count + (total == 1 ? " mistake" : " mistakes") + (since > 0 ? " since " + sinceText : "");
        else stats = StatsLine(total) + (since > 0 ? L.F("", " (since {0})", sinceText) : "");   // no plural forms needed this way

        if (aboutForm != null) { aboutForm.Activate(); return; }
        using (aboutForm = new AboutForm(stats, changes, new List<LayoutTable>(watcher.Layouts.All), updates) { Icon = iconEn })
            aboutForm.ShowDialog(owner);
        aboutForm = null;
    }

    AboutForm aboutForm;

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

    static void SetSetting(string name, int value)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey)) k.SetValue(name, value, RegistryValueKind.DWord);
    }

    // Hotkeys: id = index + 1, stored in the registry as a Keys value (key + modifiers), 0 means off
    public static string[] HotkeyTitles
    {
        get
        {
            return new[]
            {
                L.T("Раскладка выделенного текста; сразу после автоисправления — отмена (как и Ctrl+Z)",
                    "Switch layout of selected text; right after an auto-fix — undo it (as does Ctrl+Z)"),
                L.T("Раскладка выделенного текста — запасное сочетание", "Switch layout of selected text — alternative keys"),
                L.T("Регистр выделенного текста (aa → Aa → AA)", "Change case of selected text (aa → Aa → AA)"),
                L.T("Транслитерация (привет ⇄ privet)", "Transliterate (привет ⇄ privet)"),
                L.T("Раскладка всей строки до курсора", "Switch layout of the whole line before the cursor")
            };
        }
    }
    public static readonly Keys[] HotkeyDefaults =
    {
        Keys.Pause, Keys.Control | Keys.Shift | Keys.Space, Keys.Shift | Keys.F3, Keys.Shift | Keys.Pause, Keys.Alt | Keys.Pause
    };
    const int HkLayout = 1, HkLayoutAlt = 2, HkCase = 3, HkTranslit = 4, HkLine = 5;

    static Keys GetHotkey(int i) { return (Keys)GetSetting("Hotkey" + (i + 1), (int)HotkeyDefaults[i]); }

    // Registers all hotkeys again; returns the ones taken by other apps
    List<string> RegisterHotkeys()
    {
        hotkeys.UnregisterAll();
        var failed = new List<string>();
        for (int i = 0; i < HotkeyDefaults.Length; i++)
        {
            Keys k = GetHotkey(i);
            if (k == Keys.None) continue;
            uint mods = ((k & Keys.Control) != 0 ? Native.MOD_CONTROL : 0) | ((k & Keys.Shift) != 0 ? Native.MOD_SHIFT : 0) |
                        ((k & Keys.Alt) != 0 ? Native.MOD_ALT : 0);
            if (!hotkeys.Register(i + 1, mods, k & Keys.KeyCode)) failed.Add(SettingsForm.Format(k));
        }
        return failed;
    }

    void WarnHotkeys(List<string> failed)
    {
        if (failed.Count > 0)
            tray.ShowBalloonTip(4000, "KeyFish", L.F("Эти сочетания заняты другой программой: {0}. Их можно поменять в настройках.",
                "These hotkeys are taken by another app: {0}. You can change them in Settings.", string.Join(", ", failed)), ToolTipIcon.Warning);
    }

    readonly string exclusionsPath, ignoredPath, replacementsPath, learnedPath, appLayoutsPath, backupDir;
    ToolStripMenuItem autoItem;
    SettingsForm settingsForm;

    // The menu is rebuilt when the language changes
    ContextMenuStrip BuildMenu()
    {
        autoItem = new ToolStripMenuItem(L.T("Автоисправление при наборе", "Auto-correct while typing")) { Checked = autoCorrect };
        autoItem.Click += (s, e) =>
        {
            autoCorrect = !autoCorrect;
            pausedUntil = DateTime.MinValue;
            ApplyEnabled();
            SetSetting("AutoCorrect", autoCorrect ? 1 : 0);
        };

        // An hour without corrections, for a game, a password typed in plain view or text in a third language
        var pauseItem = new ToolStripMenuItem();
        pauseItem.Click += (s, e) =>
        {
            pausedUntil = pausedUntil == DateTime.MinValue ? DateTime.Now.AddHours(1) : DateTime.MinValue;
            ApplyEnabled();
        };

        // The last auto-fixes: picking one means "this shouldn't have been corrected"
        var recentItem = new ToolStripMenuItem(L.T("Последние исправления", "Recent corrections"));
        ThemedMenuRenderer.Apply(recentItem.DropDown);
        recentItem.DropDownItems.Add("…");   // filled when the menu opens

        var menu = new ContextMenuStrip();
        ThemedMenuRenderer.Apply(menu);
        var title = new ToolStripMenuItem("KeyFish " + Application.ProductVersion);
        title.Font = menuTitleFont ?? (menuTitleFont = new Font(title.Font, FontStyle.Bold));   // one font for every rebuilt menu
        title.Click += (s, e) => ShowAbout();
        menu.Items.Add(title);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(autoItem);
        menu.Items.Add(pauseItem);
        menu.Items.Add(recentItem);
        menu.Opening += (s, e) =>
        {
            if (busy) { e.Cancel = true; return; }   // a hotkey is working through the clipboard right now
            autoItem.Checked = autoCorrect;
            pauseItem.Enabled = autoCorrect;
            pauseItem.Text = pausedUntil == DateTime.MinValue ? L.T("Пауза на 1 час", "Pause for 1 hour")
                                                               : L.F("Продолжить исправлять (пауза до {0})", "Resume (paused until {0})", pausedUntil.ToString("t", L.Culture));
            recentItem.Visible = watcher.KeepRecent;
            FillRecent(recentItem);
        };
        menu.Items.Add(L.T("Настройки…", "Settings…"), null, (s, e) => OpenSettings());
        menu.Items.Add(L.T("Проверить обновления", "Check for updates"), null, (s, e) => CheckForUpdates(true));
        menu.Items.Add(L.T("О программе…", "About…"), null, (s, e) => ShowAbout());
        menu.Items.Add(L.T("Выход", "Exit"), null, (s, e) => ExitThread());
        return menu;
    }

    void FillRecent(ToolStripMenuItem parent)
    {
        foreach (ToolStripItem old in parent.DropDownItems.Cast<ToolStripItem>().ToList()) old.Dispose();
        parent.DropDownItems.Clear();
        var fixes = watcher.Recent;
        if (fixes.Count == 0)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem(L.T("Пока ничего не исправлено", "Nothing corrected yet")) { Enabled = false });
            return;
        }
        parent.DropDownItems.Add(new ToolStripMenuItem(L.T("Нажмите, чтобы больше так не исправлять:", "Click one to never correct it again:")) { Enabled = false });
        foreach (var fix in fixes)
        {
            var f = fix;
            string label = Shorten(f.From) + "  →  " + Shorten(f.To);
            var item = new ToolStripMenuItem(label.Replace("&", "&&"));
            item.Click += (s, e) =>
            {
                watcher.NeverCorrect(f);
                tray.ShowBalloonTip(3000, "KeyFish", L.F("«{0}» добавлено в «Не исправлять».", "\"{0}\" is now in \"Never correct\".", string.Join(" ", f.Words)), ToolTipIcon.Info);
            };
            parent.DropDownItems.Add(item);
        }
        parent.DropDownItems.Add(new ToolStripSeparator());
        parent.DropDownItems.Add(L.T("Очистить список", "Clear the list"), null, (s, e) => watcher.ClearRecent());
    }

    static string Shorten(string s)
    {
        s = s.Replace("\r", "").Replace("\n", " ⏎ ").Replace("\t", " ");
        return s.Length > 40 ? s.Substring(0, 39) + "…" : s;
    }

    static Font menuTitleFont;

    // Windows switched between the light and dark theme or changed the accent color: the tray menu follows at once
    void OnSystemPreference(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color && e.Category != UserPreferenceCategory.VisualStyle) return;
        if (!Theme.Refresh()) return;
        var old = tray.ContextMenuStrip;
        tray.ContextMenuStrip = BuildMenu();
        if (old != null) old.Dispose();
    }

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
        MigrateFromLayoutFix();
        RepairAutostart();
        L.Setting = GetSetting("Language", 0);
        statsBase = GetSetting("StatsTotal", 0);
        if (GetSetting("StatsSince", 0) == 0) SetSetting("StatsSince", int.Parse(DateTime.Today.ToString("yyyyMMdd")));
        string dir = DataDir();
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
            QuickKey = GetSetting("QuickKey", 0)
        };
        autoCorrect = GetSetting("AutoCorrect", 1) != 0;
        ApplyEnabled();
        showLayout = GetSetting("LayoutColor", 1) != 0;
        showBadge = GetSetting("LayoutBadge", 0) != 0;
        playSound = GetSetting("Sound", 0) != 0;
        // Double Shift or Caps Lock comes from the keyboard hook's thread: hand it to this one
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        watcher.QuickFix += () => ui.Post(_ =>
        {
            if (settingsForm == null && W.GetForegroundWindow() != IntPtr.Zero) OnHotkey(HkLayout);
        }, null);
        LoadAppLayouts();

        var menu = BuildMenu();
        SystemEvents.UserPreferenceChanged += OnSystemPreference;

        iconEn = MakeIcon("fish.ico");
        iconRu = MakeIcon("fish-ru.ico");
        blinkEn = MakeIcon("fish-blink.ico");
        blinkRu = MakeIcon("fish-ru-blink.ico");
        iconOff = Grey(iconEn);

        tray.Icon = iconEn;
        tray.Text = "KeyFish " + Application.ProductVersion;
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (s, e) => { if (!busy) OpenSettings(); };
        tray.Visible = true;

        trayTimer.Interval = 150;
        trayTimer.Tick += (s, e) => UpdateTrayIcon();
        trayTimer.Start();

        hotkeys.Pressed += OnHotkey;
        updates.Check = CheckNow;
        updates.Install = () => { if (updates.NewVersion) OfferUpdate(updates.Latest); };
        WarnHotkeys(RegisterHotkeys());

        // Updates: half a minute after start, then once a day
        tray.BalloonTipClicked += (s, e) => { if (pendingRelease != null) OfferUpdate(pendingRelease); };
        if (updated)
            tray.ShowBalloonTip(5000, "KeyFish " + Application.ProductVersion,
                L.T("Обновление установлено 🐟", "Update installed 🐟"), ToolTipIcon.Info);
        updateTimer.Interval = 30 * 1000;
        updateTimer.Tick += (s, e) =>
        {
            updateTimer.Interval = 24 * 60 * 60 * 1000;
            if (GetSetting("CheckUpdates", 1) != 0) CheckForUpdates(false);
        };
        updateTimer.Start();

        // The copy left by an update is removed after a minute of normal work, not at once: if the new version
        // failed to start, KeyFish.old.exe would still be there to go back to
        var cleanup = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
        cleanup.Tick += (s, e) => { cleanup.Stop(); cleanup.Dispose(); Updater.CleanUp(); };
        cleanup.Start();
    }

    readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();
    Updater.Release pendingRelease;
    bool installing;

    // "Yes" downloads and installs now, "No" opens the release page to update manually
    void OfferUpdate(Updater.Release r)
    {
        if (installing) return;
        if (!r.CanInstall)
        {
            if (ThemedMessage.Show(L.F("Вышла версия {0} (у вас {1}).\n\nОткрыть страницу загрузки?",
                                    "Version {0} is out (you have {1}).\n\nOpen the download page?", r.Tag, Application.ProductVersion),
                    L.T("Обновление KeyFish", "KeyFish update"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start(r.Url);
            return;
        }

        var answer = ThemedMessage.Show(
            L.F("Вышла версия {0} (у вас {1}).\n\nОбновить сейчас? KeyFish скачает новую версию с GitHub, " +
                "проверит контрольную сумму и перезапустится.\n\n«Нет» — открыть страницу загрузки и обновиться вручную.",
                "Version {0} is out (you have {1}).\n\nUpdate now? KeyFish will download it from GitHub, " +
                "verify the checksum and restart.\n\n\"No\" opens the download page to update manually.", r.Tag, Application.ProductVersion),
            L.T("Обновление KeyFish", "KeyFish update"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
        if (answer == DialogResult.No) { Process.Start(r.Url); return; }
        if (answer != DialogResult.Yes) return;

        installing = true;
        tray.ShowBalloonTip(3000, "KeyFish", L.T("Скачиваю обновление…", "Downloading the update…"), ToolTipIcon.Info);
        var ui = SynchronizationContext.Current;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string error = Updater.Install(r);
            ui.Post(__ =>
            {
                installing = false;
                if (error == null)
                {
                    SaveStats();
                    Process.Start(Updater.ExePath, "--updated");
                    ExitThread();
                }
                else if (ThemedMessage.Show(error + "\n\n" + L.T("Открыть страницу загрузки?", "Open the download page?"),
                             L.T("Обновление KeyFish", "KeyFish update"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    Process.Start(r.Url);
            }, null);
        });
    }

    // What KeyFish knows about updates; Settings and About show it and can check again or install
    readonly UpdateInfo updates = new UpdateInfo();
    readonly List<Action> checkDone = new List<Action>();

    // Asks GitHub for the latest release; done runs on this thread when the answer is in (also if a check was already going)
    void CheckNow(Action done)
    {
        if (done != null) checkDone.Add(done);
        if (updates.Checking) return;
        updates.Checking = true;
        var ui = SynchronizationContext.Current;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Updater.Release latest = null;
            try { latest = Updater.GetLatest(); }
            catch { }
            ui.Post(__ =>
            {
                updates.Checking = false;
                updates.CheckedAt = DateTime.Now;
                updates.Failed = latest == null;
                if (latest != null) updates.Latest = latest;
                var callbacks = checkDone.ToArray();
                checkDone.Clear();
                foreach (var c in callbacks) try { c(); } catch { }
            }, null);
        });
    }

    // manual: the user clicked "Check" themselves, so also report "no updates" and errors
    void CheckForUpdates(bool manual)
    {
        CheckNow(() =>
        {
            var latest = updates.Failed ? null : updates.Latest;
            {
                if (latest != null && latest.Version > Updater.Current)
                {
                    if (!manual && GetSetting("NotifiedVersion", 0) == latest.Version.GetHashCode()) return;
                    SetSetting("NotifiedVersion", latest.Version.GetHashCode());
                    pendingRelease = latest;
                    if (manual) OfferUpdate(latest);
                    else
                        tray.ShowBalloonTip(10000, L.F("Вышла версия {0}", "KeyFish {0} is out", latest.Tag),
                            L.T("Нажмите, чтобы обновить.", "Click to update."), ToolTipIcon.Info);
                }
                else if (manual)
                {
                    ThemedMessage.Show(latest == null ? L.T("Не удалось связаться с GitHub. Проверьте интернет и попробуйте позже.",
                                                         "Couldn't reach GitHub. Check your connection and try again later.")
                                                   : L.F("У вас последняя версия: {0}.", "You have the latest version: {0}.", Application.ProductVersion),
                        L.T("Обновление KeyFish", "KeyFish update"), MessageBoxButtons.OK, latest == null ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                }
            }
        });
    }

    static string ReadText(string path) { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : ""; }

    void OpenSettings()
    {
        if (settingsForm != null) { settingsForm.Activate(); return; }
        FileQueue.Flush(2000);   // words KeyFish has just learned reach the files before Settings reads them
        string ignoredBefore = ReadText(ignoredPath), learnedBefore = ReadText(learnedPath);

        var keys = new Keys[HotkeyDefaults.Length];
        for (int i = 0; i < keys.Length; i++) keys[i] = GetHotkey(i);
        settingsForm = new SettingsForm
        {
            OpenAbout = owner => ShowAbout(owner), Updates = updates,
            BackupDir = backupDir, AutoCorrect = autoCorrect, LayoutBadge = showBadge, Sound = playSound, RecentMenu = watcher.KeepRecent, QuickKey = watcher.QuickKey, Typos = watcher.FixTypos, QuietFullscreen = watcher.QuietFullscreen, UndoCtrlZ = watcher.UndoWithCtrlZ, LayoutColor = showLayout, Autostart = IsAutostart(),
            CheckUpdates = GetSetting("CheckUpdates", 1) != 0, Hotkeys = keys, Language = L.Setting,
            Replacements = ReadText(replacementsPath), Exclusions = ReadText(exclusionsPath), Ignored = ignoredBefore, Learned = learnedBefore,
            AppLayouts = ReadText(appLayoutsPath), InstalledLayouts = new List<LayoutTable>(watcher.Layouts.All), Logo = FishLogo.Render(72)
        };
        settingsForm.Icon = iconEn;

        // Global hotkeys are off while the window is open, otherwise they couldn't be assigned
        hotkeys.UnregisterAll();
        try
        {
            if (settingsForm.ShowDialog() == DialogResult.OK)
            {
                var f = settingsForm;
                // What was there before this save goes to backups\, the newest ten are kept
                Backup(ignoredBefore, learnedBefore);
                autoCorrect = f.AutoCorrect; ApplyEnabled(); SetSetting("AutoCorrect", f.AutoCorrect ? 1 : 0);
                showBadge = f.LayoutBadge; SetSetting("LayoutBadge", f.LayoutBadge ? 1 : 0);
                playSound = f.Sound; SetSetting("Sound", f.Sound ? 1 : 0);
                watcher.KeepRecent = f.RecentMenu; SetSetting("RecentMenu", f.RecentMenu ? 1 : 0);
                if (!f.RecentMenu) watcher.ClearRecent();
                watcher.QuickKey = f.QuickKey; SetSetting("QuickKey", f.QuickKey);
                watcher.FixTypos = f.Typos; SetSetting("Typos", f.Typos ? 1 : 0);
                watcher.QuietFullscreen = f.QuietFullscreen; SetSetting("QuietFullscreen", f.QuietFullscreen ? 1 : 0);
                watcher.UndoWithCtrlZ = f.UndoCtrlZ; SetSetting("UndoCtrlZ", f.UndoCtrlZ ? 1 : 0);
                showLayout = f.LayoutColor; SetSetting("LayoutColor", f.LayoutColor ? 1 : 0);
                SetSetting("CheckUpdates", f.CheckUpdates ? 1 : 0);
                if (f.Autostart != IsAutostart()) SetAutostart(f.Autostart);
                for (int i = 0; i < f.Hotkeys.Length; i++) SetSetting("Hotkey" + (i + 1), (int)f.Hotkeys[i]);
                File.WriteAllText(replacementsPath, f.Replacements, Encoding.UTF8);
                File.WriteAllText(exclusionsPath, f.Exclusions, Encoding.UTF8);
                // KeyFish keeps learning while the window is open: keep those changes along with the user's edits
                FileQueue.Flush(2000);
                File.WriteAllText(ignoredPath, MergeWords(f.Ignored, ignoredBefore, ReadText(ignoredPath)), Encoding.UTF8);
                File.WriteAllText(learnedPath, MergeWords(f.Learned, learnedBefore, ReadText(learnedPath)), Encoding.UTF8);
                File.WriteAllText(appLayoutsPath, f.AppLayouts, Encoding.UTF8);
                LoadAppLayouts();
                watcher.ReloadSoon();
                if (f.Language != L.Setting)
                {
                    L.Setting = f.Language;
                    SetSetting("Language", f.Language);
                    var old = tray.ContextMenuStrip;
                    tray.ContextMenuStrip = BuildMenu();
                    old.Dispose();
                }
            }
        }
        finally
        {
            if (settingsForm.Logo != null) settingsForm.Logo.Dispose();
            settingsForm.Dispose();
            settingsForm = null;
            WarnHotkeys(RegisterHotkeys());
        }
    }

    void Backup(string ignoredBefore, string learnedBefore)
    {
        try
        {
            Directory.CreateDirectory(backupDir);
            var files = new Dictionary<string, string>
            {
                { "replacements.txt", ReadText(replacementsPath) }, { "exclusions.txt", ReadText(exclusionsPath) },
                { "ignored-words.txt", ignoredBefore }, { "learned-words.txt", learnedBefore }, { "app-layouts.txt", ReadText(appLayoutsPath) }
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

    void OnHotkey(int id)
    {
        if (busy) return;
        busy = true;
        hotkeyWindow = Native.GetForegroundWindow();
        try
        {
            if (id == HkLayout && watcher.Undo()) return;
            if (!watcher.HotkeyAllowed(hotkeyWindow)) return;   // an excluded app or a password field
            if (!Native.WaitModifiersReleased()) return;      // keys still held: Ctrl+C would become Ctrl+Shift+C
            if (id == HkCase) ChangeCase();
            else if (id == HkTranslit) TransformSelection(Transliterator.Convert);
            else if (id == HkLine) ConvertLine();
            else ConvertSelection();
        }
        catch { }
        finally { busy = false; }
    }

    // The window the hotkey was pressed in. Hotkeys work through Ctrl+C, Ctrl+V and selecting text; if the user switches
    // to another window meanwhile (or the window closes), none of that may land there
    static IntPtr hotkeyWindow;

    static bool SameWindow() { return Native.GetForegroundWindow() == hotkeyWindow; }

    // Paste through the clipboard. The pause before Ctrl+V is required: without it the app
    // sometimes pastes the old clipboard contents (seen with Shift+Pause)
    static bool Paste(string text)
    {
        if (!SameWindow() || !SetClipboardText(text)) return false;
        Pump(50);
        if (!SameWindow()) return false;
        Native.Chord(Native.VK_CONTROL, (byte)'V');
        Pump(150);
        return true;
    }

    void TransformSelection(Func<string, string> transform)
    {
        IDataObject saved = SaveClipboard();
        string text = GetSelectionOrLastWord();
        if (!string.IsNullOrEmpty(text))
        {
            string result = transform(text);
            if (result != text) Paste(result);
        }
        RestoreClipboard(saved);
    }

    void ChangeCase()
    {
        IDataObject saved = SaveClipboard();

        string text = GetSelectionOrLastWord();
        if (!string.IsNullOrEmpty(text))
        {
            string result = CaseConverter.Next(text);
            if (Paste(result))
            {
                // Select the pasted text again so the next press continues the cycle
                // One caret step per character: an emoji is two UTF-16 units, a line break two characters
                int caretSteps = new System.Globalization.StringInfo(result).LengthInTextElements - CountOf(result, "\r\n");
                if (caretSteps <= 5000 && SameWindow()) Native.SelectLeft(caretSteps);
                Pump(50);
            }
        }

        RestoreClipboard(saved);
    }

    static int CountOf(string s, string sub)
    {
        int n = 0;
        for (int i = s.IndexOf(sub); i >= 0; i = s.IndexOf(sub, i + sub.Length)) n++;
        return n;
    }

    static string GetSelectionOrLastWord()
    {
        string text = CopySelection();
        if (string.IsNullOrEmpty(text))
        {
            // Nothing is selected, so take the last word before the caret
            if (!SameWindow()) return null;
            Native.Chord(Native.VK_CONTROL, Native.VK_SHIFT, Native.VK_LEFT);
            Pump(30);
            text = CopySelection();
        }
        return text;
    }

    // A whole phrase typed in the wrong layout: select from the cursor to the start of the line and switch it
    void ConvertLine()
    {
        IntPtr target = Native.GetForegroundWindow();
        IDataObject saved = SaveClipboard();
        if (!SameWindow()) { RestoreClipboard(saved); return; }
        Native.Chord(Native.VK_SHIFT, Native.VK_HOME);
        Pump(30);
        string text = CopySelection();
        if (!string.IsNullOrEmpty(text))
        {
            LayoutTable layout;
            string result = watcher.ConvertText(text, target, out layout);
            if (result != null && Paste(result)) W.SwitchLayout(target, layout.Hkl);
        }
        RestoreClipboard(saved);
    }

    void ConvertSelection()
    {
        IntPtr target = Native.GetForegroundWindow();

        IDataObject saved = SaveClipboard();

        string text = GetSelectionOrLastWord();
        if (!string.IsNullOrEmpty(text))
        {
            LayoutTable layout;
            string result = watcher.ConvertText(text, target, out layout);
            if (result != null && Paste(result))
            {
                W.SwitchLayout(target, layout.Hkl);
                watcher.Learn(text, result, layout.Lang);
            }
        }

        RestoreClipboard(saved);
    }

    // Wait while still processing messages, so the clipboard and the target window keep working meanwhile
    static void Pump(int ms)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    static string CopySelection()
    {
        if (!SameWindow()) return null;
        uint before = Native.GetClipboardSequenceNumber();
        Native.Chord(Native.VK_CONTROL, (byte)'C');
        for (int i = 0; i < 40; i++)
        {
            Pump(15);
            if (Native.GetClipboardSequenceNumber() != before)
            {
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    Pump(20);
                    try { return Clipboard.ContainsText() && !IsLineCopy() ? Clipboard.GetText() : null; }
                    catch { }
                }
                return null;
            }
        }
        return null;
    }

    // With nothing selected, Ctrl+C in VS Code and Visual Studio copies the whole line. That isn't a selection:
    // converting and pasting it would add a converted copy of the line, so the last word is taken instead
    static bool IsLineCopy()
    {
        var data = Clipboard.GetDataObject();
        if (data == null) return false;
        foreach (string f in data.GetFormats(false))
        {
            if (f == "VisualStudioEditorOperationsLineCutCopyClipboardTag" || f == "MSDEVLineSelect") return true;
            if (f.IndexOf("vscode-editor-data", StringComparison.OrdinalIgnoreCase) < 0 && f != "Chromium Web Custom MIME Data Format") continue;
            try
            {
                object raw = data.GetData(f, false);
                var stream = raw as MemoryStream;
                string s = raw as string ?? (stream == null ? "" : Encoding.UTF8.GetString(stream.ToArray()) + Encoding.Unicode.GetString(stream.ToArray()));
                if (s.Replace(" ", "").IndexOf("\"isFromEmptySelection\":true", StringComparison.Ordinal) >= 0) return true;
            }
            catch { }
        }
        return false;
    }

    static bool SetClipboardText(string text)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetDataObject(Private(data), true); return true; }
            catch { Pump(30); }
        }
        return false;
    }

    // KeyFish puts text on the clipboard only for a moment: keep it out of clipboard history (Win+V)
    // and cloud clipboard sync, the way password managers do
    static IDataObject Private(IDataObject data)
    {
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[4]));
        return data;
    }

    static IDataObject SaveClipboard()
    {
        FlushRestore();   // the previous hotkey's restore first, or its text would be saved as the user's
        try
        {
            var src = Clipboard.GetDataObject();
            if (src == null) return null;
            var copy = new DataObject();
            foreach (string f in src.GetFormats(false))
            {
                try { var d = src.GetData(f, false); if (d != null) copy.SetData(f, false, d); }
                catch { }
            }
            return copy;
        }
        catch { return null; }
    }

    // The user's clipboard is given back a second later, not right after Ctrl+V: a slow app (a remote desktop, a busy
    // Electron window) may read the clipboard after that and would paste the old contents instead of the fix.
    // If anything else is copied in that second, the user's copy wins and nothing is put back
    static IDataObject restoreData;
    static uint restoreSeq;
    static System.Windows.Forms.Timer restoreTimer;

    static void RestoreClipboard(IDataObject saved)
    {
        FlushRestore();
        restoreData = saved;
        restoreSeq = Native.GetClipboardSequenceNumber();
        if (restoreTimer == null)
        {
            restoreTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            restoreTimer.Tick += (s, e) => FlushRestore();
        }
        restoreTimer.Stop();
        restoreTimer.Start();
    }

    // Puts the user's clipboard back now, if it is still waiting to be (before the next hotkey and at exit)
    static void FlushRestore()
    {
        if (restoreTimer != null) restoreTimer.Stop();
        var saved = restoreData;
        if (saved == null && restoreSeq == 0) return;
        restoreData = null;
        uint seq = restoreSeq;
        restoreSeq = 0;
        if (Native.GetClipboardSequenceNumber() == seq) PutBack(saved);
    }

    static void PutBack(IDataObject saved)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (saved == null || saved.GetFormats().Length == 0) Clipboard.Clear();
                else Clipboard.SetDataObject(Private(saved), true);   // already in the history from the user's own copy
                return;
            }
            catch { Pump(30); }
        }
    }

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

    static Icon MakeIcon(string name)
    {
        // The fish icons are drawn by tools\make-icon.ps1; the tray uses the small size for the screen scale
        using (var s = typeof(App).Assembly.GetManifestResourceStream(name))
            return new Icon(s, SystemInformation.SmallIconSize);
    }

    // The fish without color, for when KeyFish isn't correcting
    static Icon Grey(Icon icon)
    {
        using (var bmp = icon.ToBitmap())
        {
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    int v = (c.R * 30 + c.G * 59 + c.B * 11) / 100;
                    v = 110 + v * 100 / 255;   // a light, flat grey that still reads on dark and light taskbars
                    bmp.SetPixel(x, y, Color.FromArgb(c.A, v, v, v));
                }
            // The handle lives as long as KeyFish: the icon is made once
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.UserPreferenceChanged -= OnSystemPreference;   // a static event: it would keep KeyFish's objects alive
        SaveStats();
        FlushRestore();
        FileQueue.Flush(2000);
        tray.Visible = false;
        hotkeys.UnregisterAll();
        base.ExitThreadCore();
    }
}

// What KeyFish knows about updates, for Settings and About: the last answer from GitHub and what can be done next
class UpdateInfo
{
    public Updater.Release Latest;       // the newest release seen, null before the first good answer
    public DateTime CheckedAt;
    public bool Failed, Checking;
    public Action<Action> Check;         // asks GitHub now; the callback runs on the UI thread when the answer is in
    public Action Install;               // offers the new version: download, verify, restart

    public bool NewVersion { get { return Latest != null && Latest.Version > Updater.Current; } }

    public string Status
    {
        get
        {
            if (Checking) return L.T("Проверяю…", "Checking…");
            if (NewVersion) return L.F("Вышла версия {0} (у вас {1})", "Version {0} is out (you have {1})", Latest.Tag, Application.ProductVersion);
            if (Failed) return L.T("Не удалось связаться с GitHub. Проверьте интернет и попробуйте позже.", "Couldn't reach GitHub. Check your connection and try again later.");
            if (Latest != null)
                return L.F("У вас последняя версия: {0}.", "You have the latest version: {0}.", Application.ProductVersion) + " " +
                       L.F("Проверено в {0}.", "Checked at {0}.", CheckedAt.ToString(CheckedAt.Date == DateTime.Today ? "t" : "g", L.Culture));
            return L.F("Версия {0}. Нажмите «Проверить», чтобы узнать, не вышла ли новая.", "Version {0}. Click Check to see whether a newer one is out.", Application.ProductVersion);
        }
    }
}

// A short "bloop" for a correction: a rising tone that fades out, made here instead of shipping a sound file
static class Bubble
{
    static System.Media.SoundPlayer player;

    public static void Play()
    {
        try
        {
            if (player == null) { player = new System.Media.SoundPlayer(new MemoryStream(Wave())); player.Load(); }
            player.Play();
        }
        catch { }
    }

    static byte[] Wave()
    {
        const int rate = 44100;
        // Silence before: an idle sound device (many cards, Bluetooth headphones) wakes up on the first samples and drops them.
        // Silence after: some drivers cut the end of a buffer, which sounds like a chopped-off tail
        int lead = rate * 250 / 1000, n = rate * 110 / 1000, tail = rate * 150 / 1000;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        int total = lead + n + tail;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + total * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(total * 2);
        for (int i = 0; i < lead; i++) w.Write((short)0);
        double phase = 0;
        int fadeOut = rate * 5 / 1000;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double freq = 320 * Math.Pow(3.2, t);                       // 320 → about 1000 Hz
            phase += 2 * Math.PI * freq / rate;
            double envelope = Math.Min(1, i / (rate * 0.004)) * Math.Exp(-4.5 * t);
            if (i > n - fadeOut) envelope *= (double)(n - i) / fadeOut;   // the last 5 ms down to zero: no click at the end
            w.Write((short)(Math.Sin(phase) * envelope * 0.28 * short.MaxValue));
        }
        for (int i = 0; i < tail; i++) w.Write((short)0);
        w.Flush();
        return ms.ToArray();
    }
}

class HotkeyWindow : NativeWindow
{
    public event Action<int> Pressed;
    readonly System.Collections.Generic.List<int> ids = new System.Collections.Generic.List<int>();

    public HotkeyWindow() { CreateHandle(new CreateParams()); }

    public bool Register(int id, uint modifiers, Keys key)
    {
        bool ok = Native.RegisterHotKey(Handle, id, modifiers | Native.MOD_NOREPEAT, (uint)key);
        if (ok) ids.Add(id);
        return ok;
    }

    public void UnregisterAll()
    {
        foreach (int id in ids) Native.UnregisterHotKey(Handle, id);
        ids.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && Pressed != null) Pressed(m.WParam.ToInt32()); // WM_HOTKEY
        base.WndProc(ref m);
    }
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr hwnd);
    [DllImport("oleacc.dll")] public static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint id, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src, ref POINT srcPos, int key, ref BLENDFUNCTION blend, int flags);

    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
    public const byte VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_HOME = 0x24, VK_LEFT = 0x25, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    static readonly byte[] Modifiers = { VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN };

    // Wait until the user releases the hotkey's modifiers, up to 3 seconds; false if they are still held then.
    // KeyFish never releases them itself: Windows would think a key is up that the user is still holding
    public static bool WaitModifiersReleased()
    {
        for (int i = 0; i < 300; i++)
        {
            bool any = false;
            foreach (byte vk in Modifiers) if ((GetAsyncKeyState(vk) & 0x8000) != 0) any = true;
            if (!any) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    public static void Chord(params byte[] keys)
    {
        foreach (byte k in keys) keybd_event(k, 0, Flags(k), UIntPtr.Zero);
        for (int i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, Flags(keys[i]) | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void SelectLeft(int count)
    {
        if (count <= 0) return;
        keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
        for (int i = 0; i < count; i++)
        {
            keybd_event(VK_LEFT, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            keybd_event(VK_LEFT, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    static uint Flags(byte vk) { return vk == VK_LEFT || vk == VK_HOME ? KEYEVENTF_EXTENDEDKEY : 0; }
}
