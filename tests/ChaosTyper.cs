using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Chaos test for KeyFish: words typed with random edits in between (Backspace runs, arrows, Home/End, Ctrl+Left,
// Ctrl+Backspace, Delete, clicks into the text and, in the "rough" mode, CapsLock, single Shift taps and Pause).
// Usage: ChaosTyper.exe <out file> <seed> <operations> <mode> [sentinel]
//   mode en     — English words and plain edits only: the text must come out the same with KeyFish running or not
//   mode mixed  — English and Russian words (Russian typed with the keys of the Russian layout while the English one
//                 may be on), plus CapsLock, Shift taps and Pause: KeyFish must not fall over or get stuck
//   sentinel    — at the end, CapsLock off, a new line and "ghbdtn " typed: with KeyFish working it comes out "привет "
// Types only into its own window: before every key the window must be in front, or the run stops (NOFOCUS).
static class ChaosTyper
{
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct U { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public U u; }
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] i, int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern IntPtr LoadKeyboardLayout(string id, uint flags);
    [DllImport("user32.dll")] static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);

    static readonly string[] En = ("the of and to in is you that it he was for on are as with his they at be this have from or one had by word " +
        "but not what all were we when your can said there use each which she do how their if will up other about out many then them these " +
        "some her would make like him into time has look two more write go see number no way could people my than first water been call who " +
        "oil its now find long down day did get come made may part over new sound take only little work know place year live me back give most " +
        "very after thing our just name good sentence man think say great where help through much before line right too mean old any same tell " +
        "boy follow came want show also around form three small set put end does another well large must big even such because turn here why").Split(' ');
    static readonly string Ru = "привет как дела давно не виделись всё нормально а у тебя что ты думаешь об этом я не знаю может быть завтра " +
        "тогда созвонимся вечером спасибо большое очень помог извини опоздаю минут на десять работа проект задача сделать";
    const string RuKeys = "йцукенгшщзхъфывапролджэячсмитьбюё";
    const string EnKeys = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";

    static IntPtr target;
    static bool lost;
    static readonly Random rnd = new Random();

    static INPUT Key(ushort vk, bool up, bool ext)
    {
        var i = new INPUT { type = 1 };
        i.u.ki.wVk = vk;
        i.u.ki.wScan = (ushort)MapVirtualKey(vk, 0);
        i.u.ki.dwFlags = (up ? 2u : 0u) | (ext ? 1u : 0u);
        return i;
    }

    static void Send(params INPUT[] ins)
    {
        if (lost) return;
        if (GetForegroundWindow() != target)
        {
            for (int i = 0; i < 100 && GetForegroundWindow() != target; i++) Thread.Sleep(20);
            if (GetForegroundWindow() != target) SetForegroundWindow(target);
            Thread.Sleep(50);
            // Windows may refuse the focus to a window that wasn't just clicked; a tap of Alt is the usual way through
            if (GetForegroundWindow() != target)
            {
                var alt = new[] { Key(0x12, false, false), Key(0x12, true, false) };
                SendInput((uint)alt.Length, alt, Marshal.SizeOf(typeof(INPUT)));
                SetForegroundWindow(target);
                Thread.Sleep(50);
            }
            if (GetForegroundWindow() != target) { lost = true; return; }
        }
        SendInput((uint)ins.Length, ins, Marshal.SizeOf(typeof(INPUT)));
    }

    static void Tap(ushort vk, bool ext = false) { Send(Key(vk, false, ext)); Thread.Sleep(5); Send(Key(vk, true, ext)); }
    static void Chord(ushort mod, ushort vk, bool ext = false) { Send(Key(mod, false, false)); Thread.Sleep(5); Tap(vk, ext); Thread.Sleep(5); Send(Key(mod, true, false)); }
    static void Pause() { Thread.Sleep(15 + rnd.Next(100)); }

    // A character as the US layout's key, with Shift where it needs it
    static void Char(char c)
    {
        bool shift = char.IsUpper(c) || "~!@#$%^&*()_+{}|:\"<>?".IndexOf(c) >= 0;
        char k = char.ToLowerInvariant(c);
        ushort vk;
        int i = "`-=[]\\;',./".IndexOf(k);
        if (k == ' ') { Tap(0x20); return; }
        if (k >= 'a' && k <= 'z') vk = (ushort)char.ToUpperInvariant(k);
        else if (k >= '0' && k <= '9') vk = k;
        else if (i >= 0) vk = new ushort[] { 0xC0, 0xBD, 0xBB, 0xDB, 0xDD, 0xDC, 0xBA, 0xDE, 0xBC, 0xBE, 0xBF }[i];
        else if ("~_+{}|:\"<>?".IndexOf(c) >= 0) { Char("`-=[]\\;',./"["~_+{}|:\"<>?".IndexOf(c)]); return; }
        else return;
        if (shift) Send(Key(0x10, false, false));
        Tap(vk);
        if (shift) Send(Key(0x10, true, false));
    }

    static string Keys(string ruWord)
    {
        var sb = new StringBuilder();
        foreach (char c in ruWord) { int i = RuKeys.IndexOf(c); sb.Append(i >= 0 ? EnKeys[i] : c); }
        return sb.ToString();
    }

    [STAThread]
    static void Main(string[] args)
    {
        string outPath = args[0];
        int seed = int.Parse(args[1]), ops = int.Parse(args[2]);
        bool rough = args[3] == "mixed", sentinel = args.Length > 4 && args[4] == "sentinel";
        var r = new Random(seed);
        string[] ru = Ru.Split(' ');
        timeBeginPeriod(1);

        var f = new Form { Width = 1000, Height = 600, TopMost = true, Text = "KeyFish chaos" };
        var tb = new TextBox { Multiline = true, Dock = DockStyle.Fill, WordWrap = true, Font = new System.Drawing.Font("Segoe UI", 12), ScrollBars = ScrollBars.Vertical };
        f.Controls.Add(tb);
        var log = new StringBuilder();
        f.Shown += (o, e) =>
        {
            ActivateKeyboardLayout(LoadKeyboardLayout("00000409", 0), 0);
            tb.Focus();
            target = f.Handle;
            var screen = tb.RectangleToScreen(tb.ClientRectangle);
            new Thread(() =>
            {
                Thread.Sleep(1500);
                for (int n = 0; n < ops && !lost; n++)
                {
                    int op = r.Next(100);
                    if (op < 55)
                    {
                        // A word and a space, sometimes Enter or a comma
                        string w = rough && r.Next(3) == 0 ? Keys(ru[r.Next(ru.Length)]) : En[r.Next(En.Length)];
                        foreach (char c in w) { Char(c); Pause(); }
                        int end = r.Next(20);
                        if (end == 0) Tap(0x0D); else if (end == 1) { Char(','); Tap(0x20); } else Tap(0x20);
                        log.Append('w');
                    }
                    else if (op < 63) { int k = 1 + r.Next(6); for (int i = 0; i < k; i++) { Tap(0x08); Pause(); } log.Append('b'); }
                    else if (op < 68) { int k = 1 + r.Next(4); for (int i = 0; i < k; i++) { Tap(0x25, true); Pause(); } log.Append('<'); }
                    else if (op < 72) { int k = 1 + r.Next(4); for (int i = 0; i < k; i++) { Tap(0x27, true); Pause(); } log.Append('>'); }
                    else if (op < 74) { Tap(0x24, true); log.Append('H'); }
                    else if (op < 78) { Tap(0x23, true); log.Append('E'); }
                    else if (op < 81) { Chord(0x11, 0x25, true); log.Append('['); }
                    else if (op < 84) { Chord(0x11, 0x08); log.Append('B'); }
                    else if (op < 86) { Tap(0x2E, true); log.Append('D'); }
                    else if (op < 90)
                    {
                        // A click somewhere in the text
                        int x = screen.Left + 10 + r.Next(Math.Max(1, screen.Width - 40)), y = screen.Top + 10 + r.Next(Math.Max(1, Math.Min(200, screen.Height - 20)));
                        if (GetForegroundWindow() == target)
                        {
                            SetCursorPos(x, y);
                            var down = new INPUT { type = 0 }; down.u.mi.dwFlags = 0x0002;
                            var up = new INPUT { type = 0 }; up.u.mi.dwFlags = 0x0004;
                            Send(down); Thread.Sleep(10); Send(up);
                        }
                        log.Append('c');
                    }
                    else if (!rough) { Tap(0x23, true); log.Append('E'); }
                    else if (op < 93) { Tap(0x14); log.Append('C'); }                 // CapsLock
                    else if (op < 96) { Tap(0x10); Thread.Sleep(40 + r.Next(200)); log.Append('s'); }   // Shift alone, sometimes twice (quick fix)
                    else { Tap(0x13); Thread.Sleep(300); log.Append('P'); }           // Pause
                    Pause();
                }
                if (sentinel && !lost)
                {
                    if (Control.IsKeyLocked(System.Windows.Forms.Keys.CapsLock)) Tap(0x14);
                    Thread.Sleep(300);
                    Chord(0x11, 0x23, true);   // Ctrl+End
                    Tap(0x0D);
                    Thread.Sleep(300);
                    foreach (char c in "ghbdtn ") { Char(c); Thread.Sleep(80); }
                }
                Thread.Sleep(2500);
                if (Control.IsKeyLocked(System.Windows.Forms.Keys.CapsLock) && GetForegroundWindow() == target) Tap(0x14);
                string text = "";
                f.Invoke((Action)(() => { text = tb.Text; }));
                File.WriteAllText(outPath, (lost ? "NOFOCUS\r\n" : "") + text, new UTF8Encoding(false));
                File.WriteAllText(outPath + ".ops", log.ToString());
                f.Invoke((Action)(() => f.Close()));
            }) { IsBackground = true }.Start();
        };
        Application.Run(f);
    }
}
