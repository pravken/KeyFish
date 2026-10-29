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

// Windows functions KeyFish calls, the hidden window that receives hotkeys, and the correction sound

// A short "bloop" for a correction: a rising tone that fades out, made here instead of shipping a sound file
static class Bubble
{
    static System.Media.SoundPlayer player, spooky;

    public static void Play()
    {
        try
        {
            // On Halloween a ghostly "ooo" instead of the bloop
            if (Theme.Holiday)
            {
                if (spooky == null) { spooky = new System.Media.SoundPlayer(new MemoryStream(Ghost())); spooky.Load(); }
                spooky.Play();
                return;
            }
            if (player == null) { player = new System.Media.SoundPlayer(new MemoryStream(Wave())); player.Load(); }
            player.Play();
        }
        catch { }
    }

    // A soft falling "ooo" with a wobble and two fading echoes, as short and quiet as the bloop
    static byte[] Ghost()
    {
        const int rate = 44100;
        int lead = rate * 250 / 1000, n = rate * 260 / 1000, echo = rate * 110 / 1000, tail = rate * 150 / 1000;
        int total = lead + n + echo * 2 + tail;
        var mix = new double[n + echo * 2];
        double phase = 0;
        int fadeOut = rate * 20 / 1000;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double freq = 620 * Math.Pow(0.62, t) * (1 + 0.03 * Math.Sin(2 * Math.PI * 7 * i / rate));   // 620 → 385 Hz, wavering
            phase += 2 * Math.PI * freq / rate;
            double envelope = Math.Min(1, i / (rate * 0.03)) * (1 - 0.4 * t);
            if (i > n - fadeOut) envelope *= (double)(n - i) / fadeOut;
            double s = (Math.Sin(phase) + 0.25 * Math.Sin(2 * phase)) * envelope * 0.18;
            mix[i] += s;
            mix[i + echo] += s * 0.35;
            mix[i + echo * 2] += s * 0.12;
        }
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + total * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(total * 2);
        for (int i = 0; i < lead; i++) w.Write((short)0);
        foreach (double s in mix) w.Write((short)(Math.Max(-1, Math.Min(1, s)) * short.MaxValue));
        for (int i = 0; i < tail; i++) w.Write((short)0);
        w.Flush();
        return ms.ToArray();
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
    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);

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
