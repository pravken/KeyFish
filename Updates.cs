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

// App's update checks and one-click updates, and what Settings and About know about them (UpdateInfo)
partial class App
{
    readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();

    Updater.Release pendingRelease;

    bool installing;

    // "Yes" downloads and installs now, "No" opens the release page to update manually
    void OfferUpdate(Updater.Release r)
    {
        if (installing) return;
        if (!r.CanInstall)
        {
            if (ThemedMessage.Show(L.F("Доступна версия {0} (установлена {1}).\n\nОткрыть страницу загрузки?",
                                    "Version {0} is available (installed: {1}).\n\nOpen the download page?", r.Tag, Application.ProductVersion),
                    L.T("Обновление KeyFish", "KeyFish update"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start(r.Url);
            return;
        }

        var answer = ThemedMessage.Show(
            L.F("Вышла версия {0} (у вас {1}).\n\nОбновить сейчас? KeyFish скачает новую версию с GitHub, " +
                "проверит контрольную сумму и перезапустится.\n\n«Нет» — открыть страницу загрузки и обновиться вручную.",
                "Version {0} is out (you have {1}).\n\nUpdate now? KeyFish will download it from GitHub, " +
                "verify the checksum and restart.\n\n\"No\" opens the download page to update manually.", r.Tag, Application.ProductVersion),
            L.T("Обновление KeyFish", "KeyFish update"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
        if (answer == DialogResult.No) { Process.Start(r.Url); return; }
        if (answer != DialogResult.Yes) return;

        installing = true;
        tray.ShowBalloonTip(3000, "KeyFish", L.T("Загрузка обновления…", "Downloading the update…"), ToolTipIcon.Info);
        var ui = SynchronizationContext.Current;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string error = Updater.Install(r);
            ui.Post(__ =>
            {
                installing = false;
                if (error == null)
                {
                    SaveStats();
                    Process.Start(Updater.ExePath, "--updated");
                    ExitThread();
                }
                else if (ThemedMessage.Show(error + "\n\n" + L.T("Открыть страницу загрузки?", "Open the download page?"),
                             L.T("Обновление KeyFish", "KeyFish update"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    Process.Start(r.Url);
            }, null);
        });
    }

    // What KeyFish knows about updates; Settings and About show it and can check again or install
    readonly UpdateInfo updates = new UpdateInfo();

    readonly List<Action> checkDone = new List<Action>();

    // Asks GitHub for the latest release; done runs on this thread when the answer is in (also if a check was already going)
    void CheckNow(Action done)
    {
        if (done != null) checkDone.Add(done);
        if (updates.Checking) return;
        updates.Checking = true;
        var ui = SynchronizationContext.Current;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Updater.Release latest = null;
            try { latest = Updater.GetLatest(); }
            catch { }
            ui.Post(__ =>
            {
                updates.Checking = false;
                updates.CheckedAt = DateTime.Now;
                updates.Failed = latest == null;
                if (latest != null) updates.Latest = latest;
                var callbacks = checkDone.ToArray();
                checkDone.Clear();
                foreach (var c in callbacks) try { c(); } catch { }
            }, null);
        });
    }

    // manual: the user clicked "Check" themselves, so also report "no updates" and errors
    void CheckForUpdates(bool manual)
    {
        CheckNow(() =>
        {
            var latest = updates.Failed ? null : updates.Latest;
            {
                if (latest != null && latest.Version > Updater.Current)
                {
                    if (!manual && GetSetting("NotifiedVersion", 0) == latest.Version.GetHashCode()) return;
                    SetSetting("NotifiedVersion", latest.Version.GetHashCode());
                    pendingRelease = latest;
                    if (manual) OfferUpdate(latest);
                    else
                        tray.ShowBalloonTip(10000, L.F("Доступна версия {0}", "KeyFish {0} is available", latest.Tag),
                            L.T("Нажмите, чтобы обновить.", "Click to update."), ToolTipIcon.Info);
                }
                else if (manual)
                {
                    ThemedMessage.Show(latest == null ? L.T("Не удалось подключиться к GitHub. Проверьте подключение к интернету и повторите попытку позже.",
                                                         "Could not connect to GitHub. Check the internet connection and try again later.")
                                                   : L.F("Установлена последняя версия: {0}.", "The latest version is installed: {0}.", Application.ProductVersion),
                        L.T("Обновление KeyFish", "KeyFish update"), MessageBoxButtons.OK, latest == null ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                }
            }
        });
    }
}

// What KeyFish knows about updates, for Settings and About: the last answer from GitHub and what can be done next
class UpdateInfo
{
    public Updater.Release Latest;       // the newest release seen, null before the first good answer
    public DateTime CheckedAt;
    public bool Failed, Checking;
    public Action<Action> Check;         // asks GitHub now; the callback runs on the UI thread when the answer is in
    public Action Install;               // offers the new version: download, verify, restart

    public bool NewVersion { get { return Latest != null && Latest.Version > Updater.Current; } }

    public string Status
    {
        get
        {
            if (Checking) return L.T("Проверка…", "Checking…");
            if (NewVersion) return L.F("Доступна версия {0} (установлена {1})", "Version {0} is available (installed: {1})", Latest.Tag, Application.ProductVersion);
            if (Failed) return L.T("Не удалось подключиться к GitHub. Проверьте подключение к интернету и повторите попытку позже.", "Could not connect to GitHub. Check the internet connection and try again later.");
            if (Latest != null)
                return L.F("Установлена последняя версия: {0}.", "The latest version is installed: {0}.", Application.ProductVersion) + " " +
                       L.F("Проверено в {0}.", "Checked at {0}.", CheckedAt.ToString(CheckedAt.Date == DateTime.Today ? "t" : "g", L.Culture));
            return L.F("Версия {0}. Чтобы проверить наличие новой версии, нажмите «Проверить».", "Version {0}. Click Check to look for a newer version.", Application.ProductVersion);
        }
    }
}
