using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// End-to-end typing test: opens its own window, types a script with real keystrokes and saves the resulting text.
// Usage: Typer.exe <output file> <script> [full]
// Script: letters and ; . , are keys, ' ' space, '<' Backspace, '#' CapsLock, '|' Pause, '~' a 60 ms pause,
// '^x' Shift+x, '%x' Ctrl+x, '[' hold Ctrl, ']' release Ctrl, '!' a tap of Shift alone. "full" makes the window borderless full screen.
static class Typer
{
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    static void Key(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); }

    [STAThread]
    static void Main(string[] args)
    {
        string outPath = args[0], script = args[1];
        var f = new Form { Width = 900, Height = 300, TopMost = true, Text = "KeyFish test" };
        var tb = new TextBox { Multiline = true, Dock = DockStyle.Fill, Font = new System.Drawing.Font("Segoe UI", 14) };
        f.Controls.Add(tb);
        if (args.Length > 2 && args[2] == "full") { f.FormBorderStyle = FormBorderStyle.None; f.WindowState = FormWindowState.Maximized; }

        var steps = new Queue<Action>();
        bool shift = false, ctrl = false;
        foreach (char c in script)
        {
            if (c == '^') { shift = true; continue; }
            if (c == '%') { ctrl = true; continue; }
            if (c == '[') { steps.Enqueue(() => keybd_event(0x11, 0, 0, UIntPtr.Zero)); continue; }
            if (c == ']') { steps.Enqueue(() => keybd_event(0x11, 0, 2, UIntPtr.Zero)); continue; }
            if (c == '~') { steps.Enqueue(() => { }); continue; }
            if (c == '!') { steps.Enqueue(() => Key(0x10)); continue; }
            bool s = shift, k = ctrl;
            shift = ctrl = false;
            byte vk = c == '<' ? (byte)0x08 : c == '#' ? (byte)0x14 : c == '|' ? (byte)0x13 : c == ' ' ? (byte)0x20 :
                      c == ';' ? (byte)0xBA : c == '.' ? (byte)0xBE : c == ',' ? (byte)0xBC : (byte)char.ToUpper(c);
            steps.Enqueue(() =>
            {
                if (s) keybd_event(0x10, 0, 0, UIntPtr.Zero);
                if (k) keybd_event(0x11, 0, 0, UIntPtr.Zero);
                Key(vk);
                if (k) keybd_event(0x11, 0, 2, UIntPtr.Zero);
                if (s) keybd_event(0x10, 0, 2, UIntPtr.Zero);
            });
        }

        f.Shown += (o, e) =>
        {
            InputLanguage.CurrentInputLanguage = InputLanguage.FromCulture(new CultureInfo("en-US"));
            tb.Focus();
            var t = new Timer { Interval = 60 };
            int warm = 10;
            t.Tick += (o2, e2) =>
            {
                if (warm-- > 0) return;
                if (GetForegroundWindow() != f.Handle)
                {
                    // Never type into someone else's window. Windows may refuse the focus to a window that wasn't
                    // just clicked; a tap of Alt is the usual way to be let through
                    SetForegroundWindow(f.Handle);
                    if (GetForegroundWindow() != f.Handle) { Key(0x12); SetForegroundWindow(f.Handle); }
                    if (GetForegroundWindow() != f.Handle) { t.Stop(); File.WriteAllText(outPath, "NOFOCUS"); f.Close(); return; }
                }
                if (steps.Count > 0) { steps.Dequeue()(); return; }
                if (warm-- > -15) return;
                t.Stop();
                string caps = "";
                if (Control.IsKeyLocked(Keys.CapsLock)) { caps = " [CAPS ON]"; Key(0x14); }
                File.WriteAllText(outPath, tb.Text + caps, System.Text.Encoding.UTF8);
                f.Close();
            };
            t.Start();
        };
        Application.Run(f);
    }
}
