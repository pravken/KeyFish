using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool updated = Array.IndexOf(args, "--updated") >= 0;
        bool created;
        using (var mutex = new Mutex(true, "KeyFish_SingleInstance", out created))
        {
            if (!created)
            {
                // После обновления старая копия ещё закрывается — ждём её, а не выходим
                if (!updated) return;
                try { if (!mutex.WaitOne(15000)) return; }
                catch (AbandonedMutexException) { }
            }
            Updater.CleanUp();
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
                   "KeePass\r\nKeePassXC\r\n1Password\r\nmstsc\r\n";
        }
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

    // Рыбка в трее: синяя — английская раскладка, оранжевая — русская, мигает после автоисправления
    Icon iconEn, iconRu, blinkEn, blinkRu;
    bool showLayout, currentRu;
    int seenCorrections, blinkTicks;
    readonly System.Windows.Forms.Timer trayTimer = new System.Windows.Forms.Timer();
    readonly int ownPid = Process.GetCurrentProcess().Id;

    void UpdateTrayIcon()
    {
        IntPtr fg = W.GetForegroundWindow();
        uint pid;
        W.GetWindowThreadProcessId(fg, out pid);
        string cls = W.ClassName(fg);
        // Пока открыт трей или меню, раскладку панели задач не показываем — она не та, в которой печатают
        if (fg != IntPtr.Zero && pid != ownPid && cls != "Shell_TrayWnd" && cls != "NotifyIconOverflowWindow"
            && cls != "TopLevelWindowForOverflowXamlIsland")
        {
            int lang = W.LayoutLanguage(fg);
            if (lang == 0x19) currentRu = true;
            else if (lang == 0x09) currentRu = false;
        }

        int corrections = watcher.CorrectionCount, events = corrections + watcher.ExpansionCount;
        if (events != seenCorrections)
        {
            seenCorrections = events;
            blinkTicks = 4;
            if (corrections != savedCorrections)
            {
                SetSetting("StatsTotal", statsBase + corrections);
                savedCorrections = corrections;
            }
        }

        bool ru = showLayout && currentRu;
        Icon want = blinkTicks > 0 && blinkTicks % 2 == 0 ? (ru ? blinkRu : blinkEn) : (ru ? iconRu : iconEn);
        if (blinkTicks > 0) blinkTicks--;
        if (tray.Icon != want) tray.Icon = want;

        string text = "KeyFish " + Application.ProductVersion + (showLayout ? (currentRu ? " — RU" : " — EN") : "") +
                      "\n" + StatsLine(statsBase + corrections);
        if (text.Length > 63) text = text.Substring(0, 63);
        if (tray.Text != text) tray.Text = text;
    }

    // Статистика: всего исправленных ошибок и с какого дня считаем
    int statsBase, savedCorrections;

    void SaveStats()
    {
        int corrections = watcher.CorrectionCount;
        if (corrections != savedCorrections) { SetSetting("StatsTotal", statsBase + corrections); savedCorrections = corrections; }
    }

    static string StatsLine(int total)
    {
        return L.Ru ? "Поймано ошибок: " + total.ToString("N0", new System.Globalization.CultureInfo("ru-RU"))
                    : "Mistakes caught: " + total.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }

    static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        if (m10 == 1 && m100 != 11) return one;
        if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
        return many;
    }

    void ShowAbout()
    {
        string changes = "";
        using (var s = typeof(App).Assembly.GetManifestResourceStream(L.Ru ? "changelog-ru.txt" : "changelog-en.txt"))
            if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) changes = r.ReadToEnd();

        int total = statsBase + watcher.CorrectionCount;
        int since = GetSetting("StatsSince", 0);
        string sinceText = since > 0 ? new DateTime(since / 10000, since / 100 % 100, since % 100).ToString("d", L.Ru ? new System.Globalization.CultureInfo("ru-RU") : System.Globalization.CultureInfo.CurrentCulture) : "";
        string stats = L.Ru
            ? "🐟 Рыбка поймала " + total.ToString("N0", new System.Globalization.CultureInfo("ru-RU")) + " " + Plural(total, "ошибку", "ошибки", "ошибок") + (since > 0 ? " с " + sinceText : "")
            : "🐟 The fish has caught " + total.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + (total == 1 ? " mistake" : " mistakes") + (since > 0 ? " since " + sinceText : "");

        MessageBox.Show("KeyFish " + Application.ProductVersion + "\n" + stats + "\n\n" + L.T("Что нового:", "What's new:") + "\n\n" + changes.Trim(),
            L.T("О программе", "About KeyFish"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static int GetSetting(string name, int def)
    {
        using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey))
            return k == null ? def : Convert.ToInt32(k.GetValue(name, def));
    }

    static void SetSetting(string name, int value)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey)) k.SetValue(name, value, RegistryValueKind.DWord);
    }

    // Горячие клавиши: id = индекс + 1, хранятся в реестре как значение Keys (клавиша + модификаторы), 0 — выключено
    public static string[] HotkeyTitles
    {
        get
        {
            return new[]
            {
                L.T("Раскладка выделенного текста; сразу после автоисправления — отмена",
                    "Switch layout of selected text; right after an auto-fix — undo it"),
                L.T("Раскладка выделенного текста — запасное сочетание", "Switch layout of selected text — alternative keys"),
                L.T("Регистр выделенного текста (aa → Aa → AA)", "Change case of selected text (aa → Aa → AA)"),
                L.T("Транслитерация (привет ⇄ privet)", "Transliterate (привет ⇄ privet)")
            };
        }
    }
    public static readonly Keys[] HotkeyDefaults =
    {
        Keys.Pause, Keys.Control | Keys.Shift | Keys.Space, Keys.Shift | Keys.F3, Keys.Shift | Keys.Pause
    };
    const int HkLayout = 1, HkLayoutAlt = 2, HkCase = 3, HkTranslit = 4;

    static Keys GetHotkey(int i) { return (Keys)GetSetting("Hotkey" + (i + 1), (int)HotkeyDefaults[i]); }

    // Регистрирует все сочетания заново; возвращает те, что заняты другими программами
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
            tray.ShowBalloonTip(4000, "KeyFish", L.T("Эти сочетания заняты другой программой: ", "These hotkeys are taken by another app: ") +
                string.Join(", ", failed) + L.T(". Их можно поменять в настройках.", ". You can change them in Settings."), ToolTipIcon.Warning);
    }

    readonly string exclusionsPath, ignoredPath, replacementsPath, learnedPath;
    ToolStripMenuItem autoItem;
    SettingsForm settingsForm;

    // Меню собирается заново при смене языка
    ContextMenuStrip BuildMenu()
    {
        autoItem = new ToolStripMenuItem(L.T("Автоисправление при наборе", "Auto-correct while typing")) { Checked = watcher.Enabled };
        autoItem.Click += (s, e) =>
        {
            watcher.Enabled = !watcher.Enabled;
            autoItem.Checked = watcher.Enabled;
            SetSetting("AutoCorrect", watcher.Enabled ? 1 : 0);
        };

        var menu = new ContextMenuStrip();
        var title = new ToolStripMenuItem("KeyFish " + Application.ProductVersion);
        title.Font = new Font(title.Font, FontStyle.Bold);
        title.Click += (s, e) => ShowAbout();
        menu.Items.Add(title);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(autoItem);
        menu.Items.Add(L.T("Настройки…", "Settings…"), null, (s, e) => OpenSettings());
        menu.Items.Add(L.T("Проверить обновления", "Check for updates"), null, (s, e) => CheckForUpdates(true));
        menu.Items.Add(L.T("О программе и что нового…", "About and what's new…"), null, (s, e) => ShowAbout());
        menu.Items.Add(L.T("Выход", "Exit"), null, (s, e) => ExitThread());
        return menu;
    }

    // До версии 1.6.0 программа называлась LayoutFix: переносим её настройки и автозапуск
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
        L.Setting = GetSetting("Language", 0);
        statsBase = GetSetting("StatsTotal", 0);
        if (GetSetting("StatsSince", 0) == 0) SetSetting("StatsSince", int.Parse(DateTime.Today.ToString("yyyyMMdd")));
        string dir = Path.GetDirectoryName(Application.ExecutablePath);
        exclusionsPath = Path.Combine(dir, "exclusions.txt");
        ignoredPath = Path.Combine(dir, "ignored-words.txt");
        learnedPath = Path.Combine(dir, "learned-words.txt");
        replacementsPath = Path.Combine(dir, "replacements.txt");
        if (!File.Exists(exclusionsPath)) File.WriteAllText(exclusionsPath, DefaultExclusions, Encoding.UTF8);
        if (!File.Exists(replacementsPath)) File.WriteAllText(replacementsPath, DefaultReplacements, Encoding.UTF8);

        watcher = new KeyboardWatcher(exclusionsPath, ignoredPath, replacementsPath, learnedPath)
        {
            Enabled = GetSetting("AutoCorrect", 1) != 0,
            FixTypos = GetSetting("Typos", 0) != 0,
            QuietFullscreen = GetSetting("QuietFullscreen", 1) != 0
        };
        showLayout = GetSetting("LayoutColor", 1) != 0;

        var menu = BuildMenu();

        iconEn = MakeIcon("fish.ico");
        iconRu = MakeIcon("fish-ru.ico");
        blinkEn = MakeIcon("fish-blink.ico");
        blinkRu = MakeIcon("fish-ru-blink.ico");

        tray.Icon = iconEn;
        tray.Text = "KeyFish " + Application.ProductVersion;
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (s, e) => OpenSettings();
        tray.Visible = true;

        trayTimer.Interval = 150;
        trayTimer.Tick += (s, e) => UpdateTrayIcon();
        trayTimer.Start();

        hotkeys.Pressed += OnHotkey;
        WarnHotkeys(RegisterHotkeys());

        // Обновления: через полминуты после запуска, затем раз в сутки
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
    }

    readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();
    Updater.Release pendingRelease;
    bool installing;

    // «Да» — скачать и поставить сейчас, «Нет» — открыть страницу релиза и обновиться вручную
    void OfferUpdate(Updater.Release r)
    {
        if (installing) return;
        if (!r.CanInstall)
        {
            if (MessageBox.Show(L.T("Вышла версия " + r.Tag + " (у вас " + Application.ProductVersion + ").\n\nОткрыть страницу загрузки?",
                                    "Version " + r.Tag + " is out (you have " + Application.ProductVersion + ").\n\nOpen the download page?"),
                    L.T("Обновление KeyFish", "KeyFish update"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start(r.Url);
            return;
        }

        var answer = MessageBox.Show(
            L.T("Вышла версия " + r.Tag + " (у вас " + Application.ProductVersion + ").\n\nОбновить сейчас? KeyFish скачает новую версию с GitHub, " +
                "проверит контрольную сумму и перезапустится.\n\n«Нет» — открыть страницу загрузки и обновиться вручную.",
                "Version " + r.Tag + " is out (you have " + Application.ProductVersion + ").\n\nUpdate now? KeyFish will download it from GitHub, " +
                "verify the checksum and restart.\n\n\"No\" opens the download page to update manually."),
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
                else if (MessageBox.Show(error + "\n\n" + L.T("Открыть страницу загрузки?", "Open the download page?"),
                             L.T("Обновление KeyFish", "KeyFish update"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    Process.Start(r.Url);
            }, null);
        });
    }

    // manual — пользователь сам нажал «Проверить»: сообщаем и об отсутствии обновлений, и об ошибке
    void CheckForUpdates(bool manual)
    {
        var ui = SynchronizationContext.Current;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Updater.Release latest = null;
            try { latest = Updater.GetLatest(); }
            catch { }
            ui.Post(__ =>
            {
                if (latest != null && latest.Version > Updater.Current)
                {
                    if (!manual && GetSetting("NotifiedVersion", 0) == latest.Version.GetHashCode()) return;
                    SetSetting("NotifiedVersion", latest.Version.GetHashCode());
                    pendingRelease = latest;
                    if (manual) OfferUpdate(latest);
                    else
                        tray.ShowBalloonTip(10000, L.T("Вышла версия ", "KeyFish ") + latest.Tag + L.T("", " is out"),
                            L.T("Нажмите, чтобы обновить.", "Click to update."), ToolTipIcon.Info);
                }
                else if (manual)
                {
                    MessageBox.Show(latest == null ? L.T("Не удалось связаться с GitHub. Проверьте интернет и попробуйте позже.",
                                                         "Couldn't reach GitHub. Check your connection and try again later.")
                                                   : L.T("У вас последняя версия: ", "You have the latest version: ") + Application.ProductVersion + ".",
                        L.T("Обновление KeyFish", "KeyFish update"), MessageBoxButtons.OK, latest == null ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                }
            }, null);
        });
    }

    static string ReadText(string path) { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : ""; }

    void OpenSettings()
    {
        if (settingsForm != null) { settingsForm.Activate(); return; }

        var keys = new Keys[HotkeyDefaults.Length];
        for (int i = 0; i < keys.Length; i++) keys[i] = GetHotkey(i);
        settingsForm = new SettingsForm
        {
            AutoCorrect = watcher.Enabled, Typos = watcher.FixTypos, QuietFullscreen = watcher.QuietFullscreen, LayoutColor = showLayout, Autostart = IsAutostart(),
            CheckUpdates = GetSetting("CheckUpdates", 1) != 0, Hotkeys = keys, Language = L.Setting,
            Replacements = ReadText(replacementsPath), Exclusions = ReadText(exclusionsPath), Ignored = ReadText(ignoredPath), Learned = ReadText(learnedPath)
        };
        settingsForm.Icon = iconEn;

        // Пока окно открыто, глобальные сочетания выключены — иначе их нельзя назначить
        hotkeys.UnregisterAll();
        try
        {
            if (settingsForm.ShowDialog() == DialogResult.OK)
            {
                var f = settingsForm;
                watcher.Enabled = f.AutoCorrect; autoItem.Checked = f.AutoCorrect; SetSetting("AutoCorrect", f.AutoCorrect ? 1 : 0);
                watcher.FixTypos = f.Typos; SetSetting("Typos", f.Typos ? 1 : 0);
                watcher.QuietFullscreen = f.QuietFullscreen; SetSetting("QuietFullscreen", f.QuietFullscreen ? 1 : 0);
                showLayout = f.LayoutColor; SetSetting("LayoutColor", f.LayoutColor ? 1 : 0);
                SetSetting("CheckUpdates", f.CheckUpdates ? 1 : 0);
                if (f.Autostart != IsAutostart()) SetAutostart(f.Autostart);
                for (int i = 0; i < f.Hotkeys.Length; i++) SetSetting("Hotkey" + (i + 1), (int)f.Hotkeys[i]);
                File.WriteAllText(replacementsPath, f.Replacements, Encoding.UTF8);
                File.WriteAllText(exclusionsPath, f.Exclusions, Encoding.UTF8);
                File.WriteAllText(ignoredPath, f.Ignored, Encoding.UTF8);
                File.WriteAllText(learnedPath, f.Learned, Encoding.UTF8);
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
            settingsForm.Dispose();
            settingsForm = null;
            WarnHotkeys(RegisterHotkeys());
        }
    }

    void OnHotkey(int id)
    {
        if (busy) return;
        busy = true;
        try
        {
            if (id == HkLayout && watcher.Undo()) return;
            if (id == HkCase) ChangeCase();
            else if (id == HkTranslit) TransformSelection(Transliterator.Convert);
            else ConvertSelection();
        }
        catch { }
        finally { busy = false; }
    }

    // Вставка через буфер обмена. Пауза перед Ctrl+V обязательна: без неё приложение
    // иногда успевает вставить старое содержимое буфера (видно на Shift+Pause)
    static bool Paste(string text)
    {
        if (!SetClipboardText(text)) return false;
        Pump(50);
        Native.Chord(Native.VK_CONTROL, (byte)'V');
        Pump(150);
        return true;
    }

    void TransformSelection(Func<string, string> transform)
    {
        Native.WaitModifiersReleased();
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
        Native.WaitModifiersReleased();
        IDataObject saved = SaveClipboard();

        string text = GetSelectionOrLastWord();
        if (!string.IsNullOrEmpty(text))
        {
            string result = CaseConverter.Next(text);
            if (Paste(result))
            {
                // Выделяем вставленный текст снова, чтобы следующее нажатие продолжило цикл
                int caretSteps = result.Length - CountOf(result, "\r\n");
                if (caretSteps <= 5000) Native.SelectLeft(caretSteps);
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
            // Ничего не выделено — берём последнее слово перед курсором
            Native.Chord(Native.VK_CONTROL, Native.VK_SHIFT, Native.VK_LEFT);
            Pump(30);
            text = CopySelection();
        }
        return text;
    }

    void ConvertSelection()
    {
        Native.WaitModifiersReleased();
        IntPtr target = Native.GetForegroundWindow();

        IDataObject saved = SaveClipboard();

        string text = GetSelectionOrLastWord();
        if (!string.IsNullOrEmpty(text))
        {
            bool toRu = !Converter.IsMostlyRussian(text);
            string result = Converter.Convert(text, toRu);
            if (Paste(result))
            {
                Native.SwitchLayout(target, toRu ? "00000419" : "00000409");
                watcher.Learn(text, result, toRu);
            }
        }

        RestoreClipboard(saved);
    }

    // Ждём, продолжая обрабатывать сообщения: пока KeyFish владеет буфером обмена,
    // другое приложение не может скопировать текст, не получив от нас ответа
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
                    try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                    catch { }
                }
                return null;
            }
        }
        return null;
    }

    static bool SetClipboardText(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(text); return true; }
            catch { Pump(30); }
        }
        return false;
    }

    static IDataObject SaveClipboard()
    {
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

    static void RestoreClipboard(IDataObject saved)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (saved == null || saved.GetFormats().Length == 0) Clipboard.Clear();
                else Clipboard.SetDataObject(saved, true);
                return;
            }
            catch { Pump(30); }
        }
    }

    static bool IsAutostart()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            return k != null && k.GetValue(AppName) != null;
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
        // Значки-рыбки рисует tools\make-icon.ps1; для трея берём маленький размер с учётом масштаба экрана
        using (var s = typeof(App).Assembly.GetManifestResourceStream(name))
            return new Icon(s, SystemInformation.SmallIconSize);
    }

    protected override void ExitThreadCore()
    {
        SaveStats();
        tray.Visible = false;
        hotkeys.UnregisterAll();
        base.ExitThreadCore();
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
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    public const byte VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LEFT = 0x25, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    static readonly byte[] Modifiers = { VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN };

    // Ждём, пока пользователь отпустит клавиши горячей комбинации, иначе Ctrl+C превратится в Ctrl+Shift+C
    public static void WaitModifiersReleased()
    {
        for (int i = 0; i < 100; i++)
        {
            bool any = false;
            foreach (byte vk in Modifiers) if ((GetAsyncKeyState(vk) & 0x8000) != 0) any = true;
            if (!any) return;
            Thread.Sleep(10);
        }
        foreach (byte vk in Modifiers) keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
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

    static uint Flags(byte vk) { return vk == VK_LEFT ? KEYEVENTF_EXTENDEDKEY : 0; }

    public static void SwitchLayout(IntPtr hwnd, string klid)
    {
        IntPtr hkl = LoadKeyboardLayout(klid, 0);
        if (hkl != IntPtr.Zero) PostMessage(hwnd, 0x0050, IntPtr.Zero, hkl); // WM_INPUTLANGCHANGEREQUEST
    }
}
