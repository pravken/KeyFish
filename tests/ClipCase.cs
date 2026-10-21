using System;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Puts an unusual payload on the clipboard, the way other apps do, for the Pause/clipboard crash hunt
static class ClipCase
{
    [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr h);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("user32.dll")] static extern uint RegisterClipboardFormat(string name);
    [DllImport("gdi32.dll")] static extern IntPtr CreateEnhMetaFile(IntPtr hdc, string file, IntPtr rect, string desc);
    [DllImport("gdi32.dll")] static extern IntPtr CloseEnhMetaFile(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool Rectangle(IntPtr hdc, int l, int t, int r, int b);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);

    static IntPtr Dword(int v)
    {
        IntPtr h = GlobalAlloc(0x0002, (UIntPtr)4);
        Marshal.WriteInt32(GlobalLock(h), v);
        GlobalUnlock(h);
        return h;
    }

    static IntPtr Unicode(string s)
    {
        IntPtr h = GlobalAlloc(0x0002, (UIntPtr)((s.Length + 1) * 2));
        IntPtr p = GlobalLock(h);
        Marshal.Copy(s.ToCharArray(), 0, p, s.Length);
        Marshal.WriteInt16(p, s.Length * 2, 0);
        GlobalUnlock(h);
        return h;
    }

    [STAThread]
    static void Main(string[] args)
    {
        switch (args[0])
        {
            case "password-manager":
                // Text plus the privacy formats KeePass / Bitwarden / 1Password add
                OpenClipboard(IntPtr.Zero); EmptyClipboard();
                SetClipboardData(13, Unicode("kftest-login"));                                      // CF_UNICODETEXT
                SetClipboardData(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), Dword(0));
                SetClipboardData(RegisterClipboardFormat("CanIncludeInClipboardHistory"), Dword(0));
                SetClipboardData(RegisterClipboardFormat("CanUploadToCloudClipboard"), Dword(0));
                SetClipboardData(RegisterClipboardFormat("Clipboard Viewer Ignore"), Dword(0));
                CloseClipboard();
                break;
            case "image":
                var bmp = new Bitmap(200, 120);
                using (var g = Graphics.FromImage(bmp)) g.Clear(Color.CornflowerBlue);
                Clipboard.SetImage(bmp);
                break;
            case "html":
                var d = new DataObject();
                d.SetData(DataFormats.Html, "Version:0.9\r\nStartHTML:00000097\r\nEndHTML:00000170\r\nStartFragment:00000131\r\nEndFragment:00000134\r\n<html><body><!--StartFragment--><b>hi</b><!--EndFragment--></body></html>");
                d.SetData(DataFormats.UnicodeText, "hi");
                d.SetData(DataFormats.Rtf, @"{\rtf1 hi}");
                Clipboard.SetDataObject(d, true);
                break;
            case "files":
                var files = new StringCollection();
                files.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini"));
                Clipboard.SetFileDropList(files);
                break;
            case "metafile":
                IntPtr dc = CreateEnhMetaFile(IntPtr.Zero, null, IntPtr.Zero, null);
                Rectangle(dc, 0, 0, 100, 100);
                IntPtr emf = CloseEnhMetaFile(dc);
                OpenClipboard(IntPtr.Zero); EmptyClipboard();
                SetClipboardData(14, emf);                                                       // CF_ENHMETAFILE
                CloseClipboard();
                break;
            case "empty":
                OpenClipboard(IntPtr.Zero); EmptyClipboard(); CloseClipboard();
                break;
            case "bigtext":
                Clipboard.SetText(new string('x', 2000000));
                break;
        }
    }
}
