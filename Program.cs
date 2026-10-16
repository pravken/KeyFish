using System;
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
    static void Main()
    {
        bool created;
        using (new Mutex(true, "LayoutFix_SingleInstance", out created))
        {
            if (!created) return;
            Application.EnableVisualStyles();
            Application.Run(new App());
        }
    }
}

class App : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "LayoutFix";

    const string SettingsKey = @"Software\LayoutFix";
    const string DefaultExclusions =
        "# Программы, где автоисправление при наборе выключено.\r\n" +
        "# Имя процесса по одному в строке (как в Диспетчере задач, .exe можно не писать).\r\n" +
        "Code\r\ndevenv\r\ncmd\r\npowershell\r\npwsh\r\nWindowsTerminal\r\nidea64\r\npycharm64\r\nrider64\r\n" +
        "KeePass\r\nKeePassXC\r\n1Password\r\nmstsc\r\n";

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

        int corrections = watcher.CorrectionCount;
        if (corrections != seenCorrections)
        {
            seenCorrections = corrections;
            blinkTicks = 4;
        }

        bool ru = showLayout && currentRu;
        Icon want = blinkTicks > 0 && blinkTicks % 2 == 0 ? (ru ? blinkRu : blinkEn) : (ru ? iconRu : iconEn);
        if (blinkTicks > 0) blinkTicks--;
        if (tray.Icon != want) tray.Icon = want;

        string text = "LayoutFix " + Application.ProductVersion + (showLayout ? (currentRu ? " — RU" : " — EN") : "");
        if (tray.Text != text) tray.Text = text;
    }

    static void ShowAbout()
    {
        string changes = "";
        using (var s = typeof(App).Assembly.GetManifestResourceStream("changelog.txt"))
            if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) changes = r.ReadToEnd();
        MessageBox.Show("LayoutFix " + Application.ProductVersion + "\n\nЧто нового:\n\n" + changes.Trim(),
            "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    public App()
    {
        hotkeys.Pressed += OnHotkey;
        bool okPause = hotkeys.Register(HkLayout, 0, Keys.Pause);
        bool okSpace = hotkeys.Register(HkLayoutAlt, Native.MOD_CONTROL | Native.MOD_SHIFT, Keys.Space);
        bool okCase = hotkeys.Register(HkCase, Native.MOD_SHIFT, Keys.F3);

        string exclusions = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "exclusions.txt");
        if (!File.Exists(exclusions)) File.WriteAllText(exclusions, DefaultExclusions, Encoding.UTF8);
        string ignoredWords = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "ignored-words.txt");
        watcher = new KeyboardWatcher(exclusions, ignoredWords) { Enabled = GetSetting("AutoCorrect", 1) != 0 };

        var autostart = new ToolStripMenuItem("Запускать с Windows") { Checked = IsAutostart() };
        autostart.Click += (s, e) => { SetAutostart(!autostart.Checked); autostart.Checked = IsAutostart(); };

        var auto = new ToolStripMenuItem("Автоисправление при наборе") { Checked = watcher.Enabled };
        auto.Click += (s, e) =>
        {
            watcher.Enabled = !watcher.Enabled;
            auto.Checked = watcher.Enabled;
            SetSetting("AutoCorrect", watcher.Enabled ? 1 : 0);
        };

        watcher.FixTypos = GetSetting("Typos", 0) != 0;
        var typos = new ToolStripMenuItem("Исправлять опечатки (эксперимент)") { Checked = watcher.FixTypos };
        typos.Click += (s, e) =>
        {
            watcher.FixTypos = !watcher.FixTypos;
            typos.Checked = watcher.FixTypos;
            SetSetting("Typos", watcher.FixTypos ? 1 : 0);
        };

        var menu = new ContextMenuStrip();
        var title = new ToolStripMenuItem("LayoutFix " + Application.ProductVersion);
        title.Font = new Font(title.Font, FontStyle.Bold);
        title.Click += (s, e) => ShowAbout();
        menu.Items.Add(title);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Pause или Ctrl+Shift+Space — раскладка") { Enabled = false });
        menu.Items.Add(new ToolStripMenuItem("Pause сразу после автоисправления — отменить") { Enabled = false });
        menu.Items.Add(new ToolStripMenuItem("Shift+F3 — регистр (aa → Aa → AA)") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        var layoutColor = new ToolStripMenuItem("Цвет рыбки по раскладке") { Checked = GetSetting("LayoutColor", 1) != 0 };
        layoutColor.Click += (s, e) =>
        {
            showLayout = !showLayout;
            layoutColor.Checked = showLayout;
            SetSetting("LayoutColor", showLayout ? 1 : 0);
        };

        menu.Items.Add(auto);
        menu.Items.Add(typos);
        menu.Items.Add(layoutColor);
        menu.Items.Add("Программы-исключения…", null, (s, e) => Process.Start("notepad.exe", "\"" + exclusions + "\""));
        menu.Items.Add("Слова, которые не исправлять…", null, (s, e) =>
        {
            if (!File.Exists(ignoredWords)) File.WriteAllText(ignoredWords, "", Encoding.UTF8);
            Process.Start("notepad.exe", "\"" + ignoredWords + "\"");
        });
        menu.Items.Add(autostart);
        menu.Items.Add("О программе и что нового…", null, (s, e) => ShowAbout());
        menu.Items.Add("Выход", null, (s, e) => ExitThread());

        iconEn = MakeIcon("fish.ico");
        iconRu = MakeIcon("fish-ru.ico");
        blinkEn = MakeIcon("fish-blink.ico");
        blinkRu = MakeIcon("fish-ru-blink.ico");
        showLayout = GetSetting("LayoutColor", 1) != 0;

        tray.Icon = iconEn;
        tray.Text = "LayoutFix " + Application.ProductVersion;
        tray.ContextMenuStrip = menu;
        tray.Visible = true;

        trayTimer.Interval = 150;
        trayTimer.Tick += (s, e) => UpdateTrayIcon();
        trayTimer.Start();

        if (!okPause || !okSpace || !okCase)
            tray.ShowBalloonTip(4000, "LayoutFix",
                "Часть горячих клавиш занята другой программой: " +
                (okPause ? "" : "Pause ") + (okSpace ? "" : "Ctrl+Shift+Space ") + (okCase ? "" : "Shift+F3"),
                ToolTipIcon.Warning);
    }

    const int HkLayout = 1, HkLayoutAlt = 2, HkCase = 3;

    void OnHotkey(int id)
    {
        if (busy) return;
        busy = true;
        try
        {
            if (id == HkLayout && watcher.Undo()) return;
            if (id == HkCase) ChangeCase();
            else ConvertSelection();
        }
        catch { }
        finally { busy = false; }
    }

    void ChangeCase()
    {
        Native.WaitModifiersReleased();
        IDataObject saved = SaveClipboard();

        string text = GetSelectionOrLastWord();
        if (!string.IsNullOrEmpty(text))
        {
            string result = CaseConverter.Next(text);
            if (SetClipboardText(result))
            {
                Native.Chord(Native.VK_CONTROL, (byte)'V');
                Pump(100);
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
            if (SetClipboardText(result))
            {
                Native.Chord(Native.VK_CONTROL, (byte)'V');
                Pump(150);
                Native.SwitchLayout(target, toRu ? "00000419" : "00000409");
            }
        }

        RestoreClipboard(saved);
    }

    // Ждём, продолжая обрабатывать сообщения: пока LayoutFix владеет буфером обмена,
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
