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

// The tray icon of App: the fish or the letters and their states, the tray menu, statistics, the About window
partial class App
{
    // Something happened (a key, a click, another window in front): look now and keep looking often for a while
    void Wake()
    {
        idle = false;
        trayTimer.Interval = 150;
        // A new day may bring a holiday's look, or take it away
        if (DateTime.Today != themeDay) { themeDay = DateTime.Today; if (Theme.Refresh()) RebuildMenu(); }
        UpdateTrayIcon();
    }

    DateTime themeDay = DateTime.Today;

    void UpdateTrayIcon()
    {
        IntPtr fg = W.GetForegroundWindow();
        uint pid;
        W.GetWindowThreadProcessId(fg, out pid);
        string cls = W.ClassName(fg);
        // While the tray or menu is open, don't show the taskbar's layout; it isn't the one being typed in
        if (fg != IntPtr.Zero && pid != ownPid && cls != "Shell_TrayWnd" && cls != "NotifyIconOverflowWindow"
            && cls != "TopLevelWindowForOverflowXamlIsland")
        {
            // The tray icon shows the alphabet of the layout: Latin or another one
            bool sameWindow = fg == lastForeground;
            if (!sameWindow) { lastForeground = fg; foregroundNoRights = W.HasMoreRights(fg); ApplyAppLayout(fg, pid); }
            IntPtr hkl = W.LayoutHkl(fg);
            var layout = watcher.Layouts.Get(hkl);
            if (layout != null) { currentRu = layout.Script != null && layout.Script != "Latin"; currentLayoutName = (layout.Name ?? "").Split('-')[0].ToUpperInvariant(); currentCulture = layout.Name; }
            // The layout changed in the window being typed in (by the user or by an auto-fix): show it at the caret
            if (sameWindow && hkl != lastHkl && lastHkl != IntPtr.Zero && showBadge && layout != null && currentLayoutName.Length > 0)
            {
                if (badge == null) badge = new LayoutBadge();
                badge.Flash(currentLayoutName, TintFor(currentLayoutName, currentRu, true));
            }
            lastHkl = hkl;
        }

        // A pause from the tray menu is over
        if (pausedUntil != DateTime.MinValue && DateTime.Now >= pausedUntil) { pausedUntil = DateTime.MinValue; ApplyEnabled(); }

        int corrections = watcher.CorrectionCount, events = corrections + watcher.ExpansionCount;
        if (events != seenCorrections)
        {
            seenCorrections = events;
            blinkTicks = 4;
            // A fix or a replacement, like the blink; each with its own sound (Sounds)
            if (playSound) Sounds.Play(watcher.TypoCount != seenTypos ? Sounds.Typo : corrections != savedCorrections ? Sounds.Fix : Sounds.Replacement);
            seenTypos = watcher.TypoCount;
            if (corrections != savedCorrections)
            {
                SetSetting("StatsTotal", statsBase + corrections);
                AddDaily(corrections - savedCorrections);
                savedCorrections = corrections;
                FirstCorrectionsTip();
            }
        }

        if (watcher.UndoCount != seenUndos) { seenUndos = watcher.UndoCount; if (playSound) Sounds.Play(Sounds.Undo); }

        bool ru = showLayout && currentRu;
        if (trayLetters && currentLayoutName.Length > 0)
        {
            // Letters in a circle: the layout's color, white while blinking after a fix, grey while not correcting
            bool off = !watcher.Enabled || (watcher.Enabled && !watcher.GuardAnswering) || foregroundNoRights;
            int kind = pausedUntil != DateTime.MinValue ? 3 : off ? 2 : blinkTicks > 0 && blinkTicks % 2 == 0 ? 1 : 0;
            if (blinkTicks > 0) blinkTicks--;
            var letters = LettersIcon(currentLayoutName, TintFor(currentLayoutName, ru, showLayout), kind);
            if (tray.Icon != letters) tray.Icon = letters;
            SetTrayText(corrections);
            return;
        }
        // The password-field check isn't answering (starting, restarting): KeyFish corrects nothing until it does,
        // so the fish is grey and says why, instead of looking as if all is well
        bool guardDown = watcher.Enabled && !watcher.GuardAnswering;
        bool blink = blinkTicks > 0 && blinkTicks % 2 == 0;
        Color custom;
        Icon want = pausedUntil != DateTime.MinValue ? iconPaused
                  : !watcher.Enabled || guardDown || foregroundNoRights ? iconOff
                  : showLayout && layoutColors.TryGetValue(currentLayoutName, out custom) ? FishIcon(custom, blink)
                  : Theme.LogoSkin.TintTray ? FishIcon(ru ? LetterIcon.Other : LetterIcon.Latin, blink)
                  : blink ? (ru ? blinkRu : blinkEn) : (ru ? iconRu : iconEn);
        if (blinkTicks > 0) blinkTicks--;
        if (tray.Icon != want) tray.Icon = want;

        SetTrayText(corrections);
    }

    // The first two auto-fixes ever: say what happened and how to take one back. The word itself isn't shown:
    // Windows keeps notifications in its notification center
    void FirstCorrectionsTip()
    {
        int shown = GetSetting("CorrectionTips", 0);
        if (shown >= 2) return;
        SetSetting("CorrectionTips", shown + 1);
        string undo = SettingsForm.Format(GetHotkey(0)) + (watcher.UndoWithCtrlZ ? L.T(" или Ctrl+Z", " or Ctrl+Z") : "");
        tray.ShowBalloonTip(6000, "KeyFish",
            L.F("Слово было набрано не в той раскладке — KeyFish его исправил. Если исправлять было не нужно, нажмите {0} сразу после: слово вернётся, и KeyFish его запомнит.",
                "That word was typed in the wrong layout, so KeyFish fixed it. If it shouldn't have, press {0} right after: the word comes back, and KeyFish remembers it.", undo),
            ToolTipIcon.Info);
    }

    void SetTrayText(int corrections)
    {
        bool guardDown = watcher.Enabled && !watcher.GuardAnswering;
        string text = "KeyFish " + Application.ProductVersion + (showLayout && currentLayoutName.Length > 0 ? " — " + currentLayoutName : "") +
                      "\n" + (guardDown ? L.T("Проверка полей паролей запускается…", "The password-field check is starting…")
                               : foregroundNoRights ? L.T("Не работает в программе от администратора", "Can't work in an app run as admin")
                               : pausedUntil != DateTime.MinValue ? PausedText : StatsLine(statsBase + corrections));
        if (text.Length > 63) text = text.Substring(0, 63);
        if (tray.Text != text) tray.Text = text;
    }

    Icon LettersIcon(string text, Color tint, int kind)
    {
        string key = text + "|" + tint.ToArgb() + "|" + kind;
        Icon icon;
        if (letterIcons.TryGetValue(key, out icon)) return icon;
        int size = SystemInformation.SmallIconSize.Width;
        Color onTint = LetterIcon.InkOn(tint);   // white, or dark on a light color
        Color fill = kind >= 2 ? LetterIcon.Off : kind == 1 ? onTint : tint;
        Color ink = kind == 1 ? tint : kind >= 2 ? Color.White : onTint;
        using (var bmp = LetterIcon.Render(text, fill, ink, size))
        {
            if (kind == 3) using (var g = Graphics.FromImage(bmp)) PauseSign(g, size);   // paused: grey with a pause sign
            icon = Icon.FromHandle(bmp.GetHicon());
        }
        letterIcons[key] = icon;
        return icon;
    }

    // Colors the user chose for layouts (Settings → Layout colors); the others keep blue for Latin, orange for the rest
    Dictionary<string, Color> layoutColors = new Dictionary<string, Color>();

    // useColors: false when "Fish color shows the layout" is off (the tray stays blue); the layout tag always uses colors
    Color TintFor(string name, bool other, bool useColors)
    {
        Color c;
        if (!useColors) return LetterIcon.Latin;
        if (layoutColors.TryGetValue(name, out c)) return c;
        return other ? LetterIcon.Other : LetterIcon.Latin;
    }

    // The tray fish in a chosen color, made once per color; on Halloween it wears a witch's hat
    readonly Dictionary<string, Icon> fishIcons = new Dictionary<string, Icon>();

    Icon FishIcon(Color color, bool blink)
    {
        string key = color.ToArgb() + "|" + blink + "|" + Theme.LogoSkin.Number;
        Icon icon;
        if (fishIcons.TryGetValue(key, out icon)) return icon;
        using (var colored = FishBitmap(color, blink, SystemInformation.SmallIconSize)) icon = Icon.FromHandle(colored.GetHicon());
        fishIcons[key] = icon;
        return icon;
    }

    // The tray fish in a color at a size, in the skin's hat when it has one (tests\Preview draws the theme sheets with it)
    internal static Bitmap FishBitmap(Color color, bool blink, Size size)
    {
        using (var s = typeof(App).Assembly.GetManifestResourceStream(blink ? "fish-blink.ico" : "fish.ico"))
        using (var source = new Icon(s, size))
        using (var bmp = source.ToBitmap())
        {
            var colored = LayoutColors.Recolor(bmp, color);
            if (Theme.LogoSkin.Hat)
                using (var g = Graphics.FromImage(colored))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.ScaleTransform(colored.Width / 64f, colored.Height / 64f);
                    FishLogo.Hat(g, colored.Width <= 20);
                }
            return colored;
        }
    }

    // An hour without corrections, or back to correcting; from the hotkey it says which, since nothing else shows it
    void TogglePause(bool tell)
    {
        if (!autoCorrect) return;
        pausedUntil = pausedUntil == DateTime.MinValue ? DateTime.Now.AddHours(1) : DateTime.MinValue;
        ApplyEnabled();
        if (playSound) Sounds.Play(Sounds.Pause);
        if (tell)
            tray.ShowBalloonTip(2500, "KeyFish", pausedUntil != DateTime.MinValue ? PausedText : L.T("Исправление снова включено.", "Correcting again."), ToolTipIcon.Info);
        UpdateTrayIcon();
    }

    string PausedText { get { return L.F("Пауза до {0}", "Paused until {0}", pausedUntil.ToString("t", L.Culture)); } }

    void SaveStats()
    {
        int corrections = watcher.CorrectionCount;
        if (corrections != savedCorrections) { SetSetting("StatsTotal", statsBase + corrections); AddDaily(corrections - savedCorrections); savedCorrections = corrections; }
    }

    // Mistakes caught per day, for the last two months: only the numbers, kept as "yyyyMMdd=n;…" in the registry
    Dictionary<int, int> daily;

    Dictionary<int, int> Daily()
    {
        if (daily != null) return daily;
        daily = new Dictionary<int, int>();
        foreach (string part in (GetText("StatsDays") ?? "").Split(';'))
        {
            int eq = part.IndexOf('='), day, n;
            if (eq > 0 && int.TryParse(part.Substring(0, eq), out day) && int.TryParse(part.Substring(eq + 1), out n) && n > 0) daily[day] = n;
        }
        return daily;
    }

    static int DayKey(DateTime d) { return d.Year * 10000 + d.Month * 100 + d.Day; }

    void AddDaily(int n)
    {
        if (n <= 0) return;
        var d = Daily();
        int today = DayKey(DateTime.Today), oldest = DayKey(DateTime.Today.AddDays(-62));
        int cur;
        d.TryGetValue(today, out cur);
        d[today] = cur + n;
        foreach (int k in d.Keys.Where(k => k < oldest).ToList()) d.Remove(k);
        try { SetText("StatsDays", string.Join(";", d.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value))); } catch { }
    }

    // Caught today, in the last 7 and 30 days, and the minutes that saved: erasing a word, switching the layout and
    // typing it again takes about 4 seconds
    int[] RecentStats(int total)
    {
        var d = Daily();
        Func<int, int> last = days => d.Where(kv => kv.Key >= DayKey(DateTime.Today.AddDays(1 - days))).Sum(kv => kv.Value);
        return new[] { last(1), last(7), last(30), (int)Math.Round(total * 4 / 60.0) };
    }

    static string StatsLine(int total)
    {
        return L.F("Поймано ошибок: {0}", "Mistakes caught: {0}", total.ToString("N0", L.Culture));
    }

    static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        if (m10 == 1 && m100 != 11) return one;
        if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
        return many;
    }

    // owner: the window it opens over (Settings), or none when opened from the tray
    void ShowAbout(IWin32Window owner = null)
    {
        string changes = "";
        using (var s = typeof(App).Assembly.GetManifestResourceStream(L.Ru ? "changelog-ru.txt" : "changelog-en.txt"))
            if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) changes = r.ReadToEnd();

        int total = statsBase + watcher.CorrectionCount;
        int since = GetSetting("StatsSince", 0);
        string sinceText = "";
        try { if (since > 0) sinceText = new DateTime(since / 10000, since / 100 % 100, since % 100).ToString("d", L.Culture); }
        catch { since = 0; }
        string count = total.ToString("N0", L.Culture);
        string stats;
        if (L.Ru) stats = "Рыбка поймала " + count + " " + Plural(total, "ошибку", "ошибки", "ошибок") + (since > 0 ? " с " + sinceText : "");
        else if (L.Code == "en") stats = "The fish has caught " + count + (total == 1 ? " mistake" : " mistakes") + (since > 0 ? " since " + sinceText : "");
        else stats = StatsLine(total) + (since > 0 ? L.F("", " (since {0})", sinceText) : "");   // no plural forms needed this way

        SaveStats();

        if (aboutForm != null) { aboutForm.Activate(); return; }
        using (aboutForm = new AboutForm(stats, changes, new List<LayoutTable>(watcher.Layouts.All), updates, RecentStats(total)) { Icon = iconEn })
            aboutForm.ShowDialog(owner);
        aboutForm = null;
    }

    AboutForm aboutForm;

    // The menu is rebuilt when the language changes
    ContextMenuStrip BuildMenu()
    {
        autoItem = new ToolStripMenuItem(L.T("Автоисправление при наборе", "Auto-correct while typing")) { Checked = autoCorrect };
        autoItem.Click += (s, e) =>
        {
            autoCorrect = !autoCorrect;
            pausedUntil = DateTime.MinValue;
            ApplyEnabled();
            SetSetting("AutoCorrect", autoCorrect ? 1 : 0);
        };

        // An hour without corrections, for a game, a password typed in plain view or text in a third language
        var pauseItem = new ToolStripMenuItem();
        pauseItem.Click += (s, e) => TogglePause(false);

        // The last auto-fixes: picking one means "this shouldn't have been corrected"
        var recentItem = new ToolStripMenuItem(L.T("Последние исправления", "Recent corrections"));
        ThemedMenuRenderer.Apply(recentItem.DropDown);
        recentItem.DropDownItems.Add("…");   // filled when the menu opens

        // The layout now on, and switches used often enough to want them without opening Settings
        var layoutItem = new ToolStripMenuItem { Enabled = false };
        var typosItem = new ToolStripMenuItem(L.T("Исправлять опечатки", "Fix typos"));
        typosItem.Click += (s, e) => { watcher.FixTypos = !watcher.FixTypos; SetSetting("Typos", watcher.FixTypos ? 1 : 0); };
        var typographyItem = new ToolStripMenuItem(L.T("Типографика", "Typography"));
        typographyItem.Click += (s, e) => { watcher.Typography = !watcher.Typography; SetSetting("Typography", watcher.Typography ? 1 : 0); };
        var soundItem = new ToolStripMenuItem(L.T("Звук при исправлении", "Sound on corrections"));
        soundItem.Click += (s, e) => { playSound = !playSound; SetSetting("Sound", playSound ? 1 : 0); };

        var menu = new ContextMenuStrip();
        ThemedMenuRenderer.Apply(menu);
        var title = new ToolStripMenuItem("KeyFish " + Application.ProductVersion);
        title.Font = menuTitleFont ?? (menuTitleFont = new Font(title.Font, FontStyle.Bold));   // one font for every rebuilt menu
        title.Click += (s, e) => ShowAbout();
        menu.Items.Add(title);
        menu.Items.Add(layoutItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(autoItem);
        menu.Items.Add(pauseItem);
        menu.Items.Add(recentItem);
        var whyItem = new ToolStripMenuItem(L.T("Почему не исправилось?", "Why wasn't it corrected?"));
        ThemedMenuRenderer.Apply(whyItem.DropDown);
        whyItem.DropDownItems.Add("…");   // filled when the menu opens
        menu.Items.Add(whyItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(typosItem);
        menu.Items.Add(typographyItem);
        menu.Items.Add(soundItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Opening += (s, e) =>
        {
            if (busy) { e.Cancel = true; return; }   // a hotkey is working through the clipboard right now
            autoItem.Checked = autoCorrect;
            pauseItem.Enabled = autoCorrect;
            pauseItem.Text = pausedUntil == DateTime.MinValue ? L.T("Пауза на 1 час", "Pause for 1 hour")
                                                               : L.F("Продолжить исправлять (пауза до {0})", "Resume (paused until {0})", pausedUntil.ToString("t", L.Culture));
            recentItem.Visible = watcher.KeepRecent;
            FillRecent(recentItem);
            FillWhy(whyItem);
            typosItem.Checked = watcher.FixTypos;
            typographyItem.Checked = watcher.Typography;
            soundItem.Checked = playSound;
            layoutItem.Text = L.F("Раскладка: {0}", "Layout: {0}", LayoutDisplayName());
        };
        menu.Items.Add(L.T("Настройки…", "Settings…"), null, (s, e) => OpenSettings());
        menu.Items.Add(L.T("Проверить обновления", "Check for updates"), null, (s, e) => CheckForUpdates(true));
        menu.Items.Add(L.T("О программе…", "About…"), null, (s, e) => ShowAbout());
        menu.Items.Add(L.T("Выход", "Exit"), null, (s, e) => ExitThread());
        return menu;
    }

    // "Русский (Россия)" for ru-RU, in the interface language where Windows has it; the short tag otherwise
    string LayoutDisplayName()
    {
        try
        {
            var c = new System.Globalization.CultureInfo(currentCulture);
            string name = L.Ru ? c.NativeName : c.DisplayName;
            return name.Length > 0 ? char.ToUpper(name[0]) + name.Substring(1) : currentLayoutName;
        }
        catch { return currentLayoutName.Length > 0 ? currentLayoutName : "—"; }
    }

    // The last words KeyFish left alone and why, each with what can be done about it
    void FillWhy(ToolStripMenuItem parent)
    {
        foreach (ToolStripItem old in parent.DropDownItems.Cast<ToolStripItem>().ToList()) old.Dispose();
        parent.DropDownItems.Clear();
        var list = watcher.Verdicts;
        if (list.Count == 0)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem(L.T("Пока всё исправлялось как надо", "Nothing left alone yet")) { Enabled = false });
            return;
        }
        foreach (var v in list)
        {
            string app = v.App.Length > 0 ? v.App : "?";
            string head = v.At.ToString("t", L.Culture) + " · " + app + " · ";
            string reason, action = null;
            Action act = null;
            var verdict = v;
            switch (v.Reason)
            {
                case "excluded":
                    reason = L.T("программа в исключениях", "the app is excluded");
                    action = L.F("Исправлять в {0}", "Correct in {0}", app);
                    act = () => IncludeApp(verdict.App);
                    break;
                case "fullscreen":
                    reason = L.T("игра или полноэкранный режим", "a game or full screen");
                    if (watcher.QuietFullscreen)
                    {
                        action = L.T("Не молчать в полноэкранных программах", "Don't stay quiet in full screen");
                        act = () => { watcher.QuietFullscreen = false; SetSetting("QuietFullscreen", 0); };
                    }
                    break;
                case "admin": reason = L.T("программа запущена от имени администратора", "the app runs as administrator"); break;
                case "password": reason = L.T("поле пароля", "a password field"); break;
                case "not-checked": reason = L.T("проверка полей паролей ещё не ответила", "the password-field check hadn't answered"); break;
                case "no-model": reason = L.T("для этой раскладки нет словаря", "no dictionary for this layout"); break;
                case "symbols": reason = L.T("в слове цифры или символы", "digits or symbols in the word"); break;
                case "code": reason = L.T("похоже на ссылку, путь или код", "looks like a link, a path or code"); break;
                case "ignored":
                    reason = L.F("«{0}» в списке «Не исправлять»", "\"{0}\" is in Never correct", v.Word);
                    action = L.T("Убрать из «Не исправлять»", "Take it out of Never correct");
                    act = () => watcher.Unignore(verdict.Word);
                    break;
                default:   // "unsure"
                    reason = L.F("«{0}» — не уверен, что это ошибка", "\"{0}\": not sure it's a mistake", v.Word);
                    action = L.T("Исправлять это слово", "Correct this word");
                    act = () => watcher.LearnWord(verdict.Word);
                    break;
            }
            var item = new ToolStripMenuItem(head + reason);
            if (act != null)
            {
                ThemedMenuRenderer.Apply(item.DropDown);
                var fix = new ToolStripMenuItem(action);
                fix.Click += (s, e) => act();
                item.DropDownItems.Add(fix);
            }
            else item.Enabled = false;
            parent.DropDownItems.Add(item);
        }
    }

    // "Correct in Code": the app leaves the exclusions (the line with its name is removed, the rest of the file stays)
    void IncludeApp(string app)
    {
        try
        {
            var kept = ReadText(exclusionsPath).Replace("\r\n", "\n").Split('\n')
                .Where(l => !string.Equals(SettingsForm.StripExe(l.Trim()), app, StringComparison.OrdinalIgnoreCase));
            File.WriteAllText(exclusionsPath, string.Join("\r\n", kept), Encoding.UTF8);
            watcher.ReloadSoon();
        }
        catch { }
    }

    void FillRecent(ToolStripMenuItem parent)
    {
        foreach (ToolStripItem old in parent.DropDownItems.Cast<ToolStripItem>().ToList()) old.Dispose();
        parent.DropDownItems.Clear();
        var fixes = watcher.Recent;
        if (fixes.Count == 0)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem(L.T("Пока ничего не исправлено", "Nothing corrected yet")) { Enabled = false });
            return;
        }
        parent.DropDownItems.Add(new ToolStripMenuItem(L.T("Нажмите, чтобы больше так не исправлять:", "Click one to never correct it again:")) { Enabled = false });
        foreach (var fix in fixes)
        {
            var f = fix;
            string label = Shorten(f.From) + "  →  " + Shorten(f.To);
            var item = new ToolStripMenuItem(label.Replace("&", "&&"));
            item.Click += (s, e) =>
            {
                watcher.NeverCorrect(f);
                // Without the word: Windows keeps notifications in its notification center
                tray.ShowBalloonTip(3000, "KeyFish", L.T("Слово добавлено в «Не исправлять».", "The word is now in \"Never correct\"."), ToolTipIcon.Info);
            };
            parent.DropDownItems.Add(item);
        }
        parent.DropDownItems.Add(new ToolStripSeparator());
        parent.DropDownItems.Add(L.T("Очистить список", "Clear the list"), null, (s, e) => watcher.ClearRecent());
    }

    static string Shorten(string s)
    {
        s = s.Replace("\r", "").Replace("\n", " ⏎ ").Replace("\t", " ");
        return s.Length > 40 ? s.Substring(0, 39) + "…" : s;
    }

    static Font menuTitleFont;

    // Windows switched between the light and dark theme or changed the accent color: the tray menu follows at once
    void OnSystemPreference(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color && e.Category != UserPreferenceCategory.VisualStyle &&
            e.Category != UserPreferenceCategory.Accessibility) return;   // Accessibility: high contrast on or off
        if (Theme.Refresh()) RebuildMenu();
    }

    void RebuildMenu()
    {
        var old = tray.ContextMenuStrip;
        tray.ContextMenuStrip = BuildMenu();
        if (old != null) old.Dispose();
    }

    // The computer was locked (or another user switched in): what KeyFish remembers about recent typing is forgotten —
    // the word being typed, the last words, recent corrections and why words were left alone
    void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect || e.Reason == SessionSwitchReason.RemoteDisconnect)
            try { watcher.ClearMemory(); } catch { }
    }

    static Icon MakeIcon(string name)
    {
        // The fish icons are drawn by tools\make-icon.ps1; the tray uses the small size for the screen scale
        using (var s = typeof(App).Assembly.GetManifestResourceStream(name))
            return new Icon(s, SystemInformation.SmallIconSize);
    }

    // The fish without color, for when KeyFish isn't correcting
    Icon iconPaused;   // grey fish with a pause sign: paused from the menu or the hotkey

    // The grey icon with a pause sign (two bars) in the lower right corner
    static Icon Paused(Icon icon)
    {
        using (var grey = Grey(icon))
        using (var bmp = grey.ToBitmap())
        {
            using (var g = Graphics.FromImage(bmp)) PauseSign(g, bmp.Width);
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    static void PauseSign(Graphics g, int size)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float d = size * 0.56f, x = size - d, y = size - d;
        using (var b = new SolidBrush(Color.FromArgb(235, 40, 40, 40))) g.FillEllipse(b, x, y, d, d);
        float bw = d * 0.17f, bh = d * 0.46f, gap = d * 0.12f;
        using (var b = new SolidBrush(Color.White))
        {
            g.FillRectangle(b, x + d / 2 - gap / 2 - bw, y + (d - bh) / 2, bw, bh);
            g.FillRectangle(b, x + d / 2 + gap / 2, y + (d - bh) / 2, bw, bh);
        }
    }

    static Icon Grey(Icon icon)
    {
        using (var bmp = icon.ToBitmap())
        {
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    int v = (c.R * 30 + c.G * 59 + c.B * 11) / 100;
                    v = 110 + v * 100 / 255;   // a light, flat grey that still reads on dark and light taskbars
                    bmp.SetPixel(x, y, Color.FromArgb(c.A, v, v, v));
                }
            // The handle lives as long as KeyFish: the icon is made once
            return Icon.FromHandle(bmp.GetHicon());
        }
    }
}
