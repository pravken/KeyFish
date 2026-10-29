using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Screenshots of KeyFish windows for checking the design, without starting KeyFish itself.
// Usage: Preview.exe settings <page 0-7>|about|aquarium|message|changelog <out.png> [dark|light] [language code] [menu]
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

    // icons <out.png>: the tray icons of the look at 16, 20, 24 and 32 pixels on a dark and a light taskbar — the fish
    // (Latin, another alphabet, blinking) and the letters
    static void Icons(string outPath)
    {
        int[] sizes = { 16, 20, 24, 32 };
        const int cell = 44, label = 40;
        using (var bmp = new Bitmap(label + cell * 5, cell * sizes.Length * 2))
        using (var g = Graphics.FromImage(bmp))
        using (var font = new Font("Segoe UI", 8f))
        {
            for (int half = 0; half < 2; half++)
            {
                bool dark = half == 0;
                var bar = new Rectangle(0, half * cell * sizes.Length, bmp.Width, cell * sizes.Length);
                using (var b = new SolidBrush(dark ? Color.FromArgb(28, 28, 28) : Color.FromArgb(238, 238, 238))) g.FillRectangle(b, bar);
                for (int i = 0; i < sizes.Length; i++)
                {
                    int n = sizes[i], y = bar.Y + i * cell + (cell - n) / 2;
                    using (var b = new SolidBrush(dark ? Color.Silver : Color.DimGray)) g.DrawString(n + " px", font, b, 4, bar.Y + i * cell + 14);
                    var size = new Size(n, n);
                    var pictures = new List<Image>();
                    if (Theme.LogoSkin.TintTray)
                    {
                        pictures.Add(App.FishBitmap(LetterIcon.Latin, false, size));
                        pictures.Add(App.FishBitmap(LetterIcon.Other, false, size));
                        pictures.Add(App.FishBitmap(LetterIcon.Latin, true, size));
                    }
                    else
                        foreach (string name in new[] { "fish.ico", "fish-ru.ico", "fish-blink.ico" })
                            using (var s = typeof(App).Assembly.GetManifestResourceStream(name))
                            using (var icon = new Icon(s, size)) pictures.Add(icon.ToBitmap());
                    pictures.Add(LetterIcon.Render("EN", LetterIcon.Latin, LetterIcon.InkOn(LetterIcon.Latin), n));
                    pictures.Add(LetterIcon.Render("RU", LetterIcon.Other, LetterIcon.InkOn(LetterIcon.Other), n));
                    for (int k = 0; k < pictures.Count; k++)
                    {
                        g.DrawImage(pictures[k], new Rectangle(label + k * cell + (cell - n) / 2, y, n, n));
                        pictures[k].Dispose();
                    }
                }
            }
            bmp.Save(outPath, ImageFormat.Png);
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        SetProcessDPIAware();
        // settings <page> <out> [theme] [lang]; about|aquarium <out> [theme] [lang]
        int ta = args[0] == "settings" ? 3 : 2;
        if (args.Length > ta) typeof(Theme).GetField("Dark").SetValue(null, args[ta] == "dark");
        // "skin=N": that look (Skin.Number), whatever the date; "halloween" is skin=1
        foreach (string a in args)
            if (a.StartsWith("skin=") || a == "halloween")
            {
                Theme.Skin = Skin.Find(a == "halloween" ? 1 : int.Parse(a.Substring(5)));
                if (!Theme.Skin.Accent.IsEmpty) Theme.Accent = Theme.Skin.Accent;
            }
        // "nologo": the look without its fish (Settings → The fish in the look's style off)
        if (Array.IndexOf(args, "nologo") >= 0) Theme.SkinLogo = false;
        if (args[0] == "icons") { Icons(args[1]); return; }
        // "contrast": the high-contrast colors path, with whatever system colors this computer has
        if (Array.IndexOf(args, "contrast") >= 0) { Theme.Contrast = true; Theme.Dark = false; }
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
                Hotkeys = (Keys[])App.HotkeyDefaults.Clone(), Language = 0, StartPage = page,
                ThemeMode = args.Length > 3 ? (args[3] == "dark" ? 2 : 1) : 0,   // the theme asked for, not the one Windows has
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
                new object[] { "Вышла версия 2.0.1 (у вас 2.0.0).\n\nОбновить сейчас? KeyFish скачает новую версию с GitHub, проверит контрольную сумму и перезапустится.", "Обновление KeyFish", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information, null }, null);
        else if (args[0] == "intro")
        {
            form = new IntroForm();
            // "step=N": that step of the introduction
            foreach (string arg in args)
                if (arg.StartsWith("step="))
                    typeof(IntroForm).GetMethod("Show", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(int) }, null)
                        .Invoke(form, new object[] { int.Parse(arg.Substring(5)) });
        }
        else form = new AboutForm("Исправлено ошибок: 1 234 (с 12.03.2026)", "2.0.0\n• Новое\n\n1.8.3\n• Старое\n", LayoutTable.Installed(),
                                  FakeUpdates(args), new[] { 12, 85, 310, 82 });
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
                        var bar = c as VScrollBar;   // the changelog scrolls with a bar of its own
                        if (bar != null && bar.Enabled) { bar.Value = Math.Min(int.Parse(arg.Substring(7)), bar.Maximum - bar.LargeChange + 1); break; }
                        var sp = c as ScrollableControl;
                        if (sp != null && sp.AutoScroll && c != form && c.Visible) { sp.AutoScrollPosition = new Point(0, int.Parse(arg.Substring(7))); break; }
                        foreach (Control child in c.Controls) stack.Push(child);
                    }
                }
            // "fury": the sidebar fish beats its tail as after five clicks
            if (Array.IndexOf(args, "fury") >= 0)
            {
                var fish = Find<FishView>(form);
                if (fish != null) fish.Fury();
            }
            // "colors": open the palette of the first layout color (captured from the screen)
            if (Array.IndexOf(args, "colors") >= 0)
            {
                var sw = Find<ColorSwatch>(form);
                if (sw != null) sw.GetType().GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(sw, new object[] { EventArgs.Empty });
            }
            // "pick=N": pick look N in the gallery, as a click does: the window applies it at once and is built again
            foreach (string arg in args)
                if (arg.StartsWith("pick=") && form is SettingsForm)
                {
                    ((SettingsForm)form).ApplyLook = f => { Theme.Skin = Skin.Find(f.SkinNumber); if (!Theme.Skin.Accent.IsEmpty) Theme.Accent = Theme.Skin.Accent; Theme.Dark = f.ThemeMode == 2; return true; };
                    var gallery = Find<SkinGallery>(form);   // as a click does: Choose raises SelectedChanged
                    if (gallery != null) typeof(SkinGallery).GetMethod("Choose", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gallery, new object[] { int.Parse(arg.Substring(5)) });
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