using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Opens a test page in a private Edge window, types into its input field and saves the window title,
// where the page copies the field's value. Usage: BrowserTyper.exe <url> <title marker> <script> <output file>
static class BrowserTyper
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
        EnumWindows((h, l) => { if (IsWindowVisible(h) && Title(h).Contains(marker)) { found = h; return false; } return true; }, IntPtr.Zero);
        return found;
    }

    static void Key(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); }

    static void Main(string[] args)
    {
        string url = args[0], marker = args[1], script = args[2], outPath = args[3];
        Process.Start("msedge.exe", "--inprivate --new-window \"" + url + "\"");
        IntPtr w = IntPtr.Zero;
        for (int i = 0; i < 60 && w == IntPtr.Zero; i++) { Thread.Sleep(250); w = Find(marker); }
        if (w == IntPtr.Zero) { File.WriteAllText(outPath, "NOWINDOW"); return; }
        SetForegroundWindow(w);
        Thread.Sleep(1500);
        // Always start in the English layout: a previous test may have left Russian on
        PostMessage(w, 0x0050, IntPtr.Zero, LoadKeyboardLayout("00000409", 0));
        Thread.Sleep(1000);
        foreach (char c in script)
        {
            if (GetForegroundWindow() != w) { SetForegroundWindow(w); Thread.Sleep(200); if (GetForegroundWindow() != w) { File.WriteAllText(outPath, "NOFOCUS"); return; } }
            if (c == '~') { Thread.Sleep(60); continue; }
            Key(c == ' ' ? (byte)0x20 : c == ';' ? (byte)0xBA : (byte)char.ToUpper(c));
            Thread.Sleep(60);
        }
        Thread.Sleep(2000);
        File.WriteAllText(outPath, Title(w), Encoding.UTF8);
        if (GetForegroundWindow() == w)
        {
            keybd_event(0x11, 0, 0, UIntPtr.Zero); Key((byte)'W'); keybd_event(0x11, 0, 2, UIntPtr.Zero); // close the tab
        }
    }
}
