using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Screenshots of KeyFish windows for checking the design, without starting KeyFish itself.
// Usage: Preview.exe settings <page 0-4>|about|aquarium|message|changelog <out.png> [dark|light] [language code] [menu]
static class Preview
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    static T Find<T>(Control root) where T : Control
    {
        foreach (Control c in root.Controls)
        {
            if (c is T && c.Visible) return (T)c;
            var f = c.Visible ? Find<T>(c) : null;
            if (f != null) return f;
        }
        return null;
    }

    // "newver": a newer release is out; otherwise nothing was checked yet
    static UpdateInfo FakeUpdates(string[] args)
    {
        var u = new UpdateInfo { Check = done => { }, Install = () => { } };
        if (Array.IndexOf(args, "newver") >= 0) { u.Latest = new Updater.Release { Version = new Version(9, 0, 0), Tag = "9.0.0" }; u.CheckedAt = DateTime.Now; }
        return u;
    }

    [STAThread]
    static void Main(string[] args)
    {
        SetProcessDPIAware();
        // settings <page> <out> [theme] [lang]; about|aquarium <out> [theme] [lang]
        int ta = args[0] == "settings" ? 3 : 2;
        if (args.Length > ta) typeof(Theme).GetField("Dark").SetValue(null, args[ta] == "dark");
        L.Setting = args.Length > ta + 1 ? Array.IndexOf(L.Codes, args[ta + 1]) + 1 : 1;
        Application.EnableVisualStyles();
        Form form;
        int page = 0;
        if (args[0] == "settings")
        {
            page = int.Parse(args[1]);
            var hkl = LayoutTable.InstalledHkls();
            var layouts = new List<LayoutTable>();
            foreach (var h in hkl) layouts.Add(LayoutTable.FromHkl(h));
            // "layouts=fr-FR:kbdfr,uk-UA:kbdur1": other layouts than the installed ones, from tests\layouts
            foreach (string a in args)
                if (a.StartsWith("layouts="))
                {
                    layouts.Clear();
                    foreach (string item in a.Substring(8).Split(','))
                    {
                        string[] p = item.Split(':');
                        var t = LayoutTable.FromKlc(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), @"..\layouts\" + p[1] + ".klc"));
                        t.Name = p[0];
                        layouts.Add(t);
                    }
                }
            form = new SettingsForm
            {
                AutoCorrect = true, QuietFullscreen = true, UndoCtrlZ = true, LayoutColor = true, Autostart = true, CheckUpdates = true, RecentMenu = true, QuickKey = 1,
                Hotkeys = (Keys[])App.HotkeyDefaults.Clone(), Language = 0, StartPage = page, Logo = FishLogo.Render(72),
                Replacements = "спс = спасибо\nсув = С уважением,\\nМаксим\nthx = thank you\n",
                Exclusions = Array.IndexOf(args, "long") >= 0   // "long": enough apps for the list to scroll
                    ? "Code\ndevenv\ncmd\npowershell\npwsh\nWindowsTerminal\nidea64\npycharm64\nrider64\nKeePass\nKeePassXC\n1Password\nmstsc\n"
                    : "Code\ncmd\nWindowsTerminal\n", AppLayouts = "Telegram = " + hkl[hkl.Length - 1].ToInt64().ToString("X") + "\nCode = " + hkl[0].ToInt64().ToString("X") + "\n",
                Ignored = "lol\nкек\n", Learned = "ghbdtn\n", InstalledLayouts = layouts,
                Updates = FakeUpdates(args)
            };
        }
        else if (args[0] == "changelog")   // the real changelog of the interface language
            form = new ChangelogForm(System.IO.File.ReadAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\" + (L.Ru ? "CHANGELOG.ru.md" : "CHANGELOG.md")), System.Text.Encoding.UTF8));
        else if (args[0] == "message")
            form = (Form)Activator.CreateInstance(typeof(ThemedMessage), BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { "Вышла версия 2.0.1 (у вас 2.0.0).\n\nОбновить сейчас? KeyFish скачает новую версию с GitHub, проверит контрольную сумму и перезапустится.", "Обновление KeyFish", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information }, null);
        else form = new AboutForm("Рыбка поймала 1 234 ошибки", "2.0.0\n• Новое\n\n1.8.3\n• Старое\n", LayoutTable.Installed(),
                                  FakeUpdates(args));
        string outPath = args[args[0] == "settings" ? 2 : 1];
        int at = 3200; foreach (string a in args) if (a.StartsWith("at=")) at = int.Parse(a.Substring(3));   // aquarium: when to take the shot, ms
        var timer = new Timer { Interval = args[0] == "aquarium" || Array.Exists(args, x => x.StartsWith("at=")) ? at : 700 };
        form.Shown += (s, e) =>
        {
            // "menu": open the first drop-down of the page, to see the popup (captured from the screen)
            if (Array.IndexOf(args, "menu") >= 0)
            {
                var dd = Find<DropDown>(form);
                if (dd != null) dd.GetType().GetMethod("ShowMenu", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dd, null);
            }
            // "scroll=N": scroll the first scrolling panel down N pixels (the changelog, a long settings page)
            foreach (string arg in args)
                if (arg.StartsWith("scroll="))
                {
                    var stack = new Stack<Control>(); stack.Push(form);
                    while (stack.Count > 0)
                    {
                        var c = stack.Pop();
                        var sp = c as ScrollableControl;
                        if (sp != null && sp.AutoScroll && c != form && c.Visible) { sp.AutoScrollPosition = new Point(0, int.Parse(arg.Substring(7))); break; }
                        foreach (Control child in c.Controls) stack.Push(child);
                    }
                }
            // "colors": open the palette of the first layout color (captured from the screen)
            if (Array.IndexOf(args, "colors") >= 0)
            {
                var sw = Find<ColorSwatch>(form);
                if (sw != null) sw.GetType().GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(sw, new object[] { EventArgs.Empty });
            }
            // "focus": click into the first hotkey field, to see the recording state
            if (Array.IndexOf(args, "focus") >= 0) { var hk = Find<HotkeyBox>(form); if (hk != null) hk.Focus(); }
            if (args[0] == "aquarium")
                foreach (Control c in form.Controls) if (c is FishView) for (int i = 0; i < 5; i++) c.GetType().GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, new object[] { EventArgs.Empty });
            timer.Start();
        };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            var r = form.Bounds;
            bool screen = Array.IndexOf(args, "menu") >= 0 || Array.IndexOf(args, "colors") >= 0;
            using (var bmp = new Bitmap(r.Width, r.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    if (screen) { g.CopyFromScreen(r.Location, Point.Empty, r.Size); goto saved; }
                    IntPtr hdc = g.GetHdc();
                    PrintWindow(form.Handle, hdc, 2);
                    g.ReleaseHdc(hdc);
                    saved: ;
                }
                bmp.Save(outPath, ImageFormat.Png);
            }
            form.Close();
        };
        Application.Run(form);
    }
}