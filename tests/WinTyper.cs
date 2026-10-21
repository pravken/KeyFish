using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Types a script into an existing window found by title, then reports whether KeyFish survived.
// Script: letters = keys, ' ' space, '|' Pause, '~' 60 ms pause, '\t' Tab, '!' Escape
static class WinTyper
{
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr LoadKeyboardLayout(string klid, uint flags);

    static string Title(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString(); }

    static IntPtr Find(string marker)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => { if (IsWindowVisible(h) && Title(h).IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) { found = h; return false; } return true; }, IntPtr.Zero);
        return found;
    }

    static void Key(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); }

    static void Main(string[] args)
    {
        string marker = args[0], script = args[1], outPath = args[2];
        var log = new StringBuilder();
        IntPtr w = IntPtr.Zero;
        for (int i = 0; i < 80 && w == IntPtr.Zero; i++) { Thread.Sleep(250); w = Find(marker); }
        if (w == IntPtr.Zero) { File.WriteAllText(outPath, "NOWINDOW"); return; }
        log.AppendLine("window: " + Title(w));
        SetForegroundWindow(w);
        Thread.Sleep(1500);
        PostMessage(GetForegroundWindow(), 0x0050, IntPtr.Zero, LoadKeyboardLayout("00000409", 0));
        Thread.Sleep(800);
        int before = Process.GetProcessesByName("KeyFish").Length;
        foreach (char c in script)
        {
            if (c == '~') { Thread.Sleep(60); continue; }
            byte vk = c == ' ' ? (byte)0x20 : c == '|' ? (byte)0x13 : c == '\t' ? (byte)0x09 : c == '!' ? (byte)0x1B : (byte)char.ToUpper(c);
            Key(vk);
            Thread.Sleep(80);
        }
        Thread.Sleep(3000);
        int after = Process.GetProcessesByName("KeyFish").Length;
        log.AppendLine("KeyFish processes before: " + before + ", after: " + after);
        log.AppendLine("foreground now: " + Title(GetForegroundWindow()));
        File.WriteAllText(outPath, log.ToString(), Encoding.UTF8);
    }
}
