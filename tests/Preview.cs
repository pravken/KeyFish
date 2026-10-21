using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Screenshots of KeyFish windows for checking the design, without starting KeyFish itself.
// Usage: Preview.exe settings <page 0-4>|about|aquarium <out.png> [dark|light] [language code] [menu]
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

    [STAThread]
    static void Main(string[] args)
    {
        SetProcessDPIAware();
        if (args.Length > 3) typeof(Theme).GetField("Dark").SetValue(null, args[3] == "dark");
        L.Setting = args.Length > 4 ? Array.IndexOf(L.Codes, args[4]) + 1 : 1;
        Application.EnableVisualStyles();
        Form form;
        int page = 0;
        if (args[0] == "settings")
        {
            page = int.Parse(args[1]);
            var hkl = LayoutTable.InstalledHkls();
            var layouts = new List<LayoutTable>();
            foreach (var h in hkl) layouts.Add(LayoutTable.FromHkl(h));
            form = new SettingsForm
            {
                AutoCorrect = true, QuietFullscreen = true, UndoCtrlZ = true, LayoutColor = true, Autostart = true, CheckUpdates = true,
                Hotkeys = (Keys[])App.HotkeyDefaults.Clone(), Language = 0, StartPage = page, Logo = FishLogo.Render(72),
                Replacements = "спс = спасибо\nсув = С уважением,\\nМаксим\nthx = thank you\n",
                Exclusions = "Code\ncmd\nWindowsTerminal\n", AppLayouts = "Telegram = " + hkl[hkl.Length - 1].ToInt64().ToString("X") + "\nCode = " + hkl[0].ToInt64().ToString("X") + "\n",
                Ignored = "lol\nкек\n", Learned = "ghbdtn\n", InstalledLayouts = layouts
            };
        }
        else form = new AboutForm("Рыбка поймала 1 234 ошибки", "2.0.0\n• Новое\n\n1.8.3\n• Старое\n", LayoutTable.Installed());
        string outPath = args[args[0] == "settings" ? 2 : 1];
        var timer = new Timer { Interval = args[0] == "aquarium" ? 3200 : 700 };
        form.Shown += (s, e) =>
        {
            // "menu": open the first drop-down of the page, to see the popup (captured from the screen)
            if (Array.IndexOf(args, "menu") >= 0)
            {
                var dd = Find<DropDown>(form);
                if (dd != null) dd.GetType().GetMethod("ShowMenu", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dd, null);
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
            bool screen = Array.IndexOf(args, "menu") >= 0;
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