using System;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

// Windows notifications with a button. KeyFish isn't a Store app, so Windows learns its name and icon from
// HKCU\Software\Classes\AppUserModelId\KeyFish (the icon: a small PNG in %LOCALAPPDATA%\KeyFish). A notification
// lasts a minute, and its button works while KeyFish runs. Where one can't be shown, Show returns false and the
// caller uses the tray balloon instead
static class Toasts
{
    const string Id = "KeyFish";
    static ToastNotification current;   // held so its Activated handler lives as long as the notification

    // clicked runs on the window thread (ui), not the one Windows calls back on
    public static bool Show(string title, string text, string button, Action clicked, SynchronizationContext ui)
    {
        try { return ShowCore(title, text, button, clicked, ui); }
        catch { return false; }   // Windows without these notifications (or the types missing) — the balloon then
    }

    [MethodImpl(MethodImplOptions.NoInlining)]   // the Windows Runtime types load only here, inside the try above
    static bool ShowCore(string title, string text, string button, Action clicked, SynchronizationContext ui)
    {
        Register();
        var xml = new XmlDocument();
        xml.LoadXml("<toast><visual><binding template='ToastGeneric'><text>" + SecurityElement.Escape(title) + "</text><text>" +
                    SecurityElement.Escape(text) + "</text></binding></visual>" +
                    (button == null ? "" : "<actions><action content='" + SecurityElement.Escape(button) + "' arguments='go'/></actions>") + "</toast>");
        var toast = new ToastNotification(xml) { ExpirationTime = DateTimeOffset.Now.AddMinutes(1) };
        toast.Activated += (s, e) =>
        {
            var a = e as ToastActivatedEventArgs;
            if (a != null && a.Arguments == "go" && clicked != null) ui.Post(_ => clicked(), null);
        };
        ToastNotificationManager.CreateToastNotifier(Id).Show(toast);
        current = toast;
        return true;
    }

    // KeyFish's name and fish for the notifications, written once per start
    static bool registered;

    static void Register()
    {
        if (registered) return;
        string icon = null;
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyFish");
            Directory.CreateDirectory(dir);
            icon = Path.Combine(dir, "notification-icon.png");
            using (var bmp = new Bitmap(64, 64))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    FishLogo.Draw(g, new RectangleF(0, 0, 64, 64), true, 0, 1, -1);
                }
                bmp.Save(icon, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        catch { icon = null; }
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + Id))
        {
            k.SetValue("DisplayName", "KeyFish");
            if (icon != null) k.SetValue("IconUri", icon);
        }
        registered = true;
    }
}
