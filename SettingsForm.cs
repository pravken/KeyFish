using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

// Settings window in the Windows 11 style: sections in a sidebar, switches, and tables for the lists
// that used to be plain text files (replacements, apps, words)
class SettingsForm : Form
{
    public bool AutoCorrect, Typos, QuietFullscreen, UndoCtrlZ, LayoutColor, Autostart, CheckUpdates, LayoutBadge, RecentMenu, Sound, TrayLetters, Typography;
    public string BackupDir;             // where KeyFish keeps copies of the settings made before each save
    public Action<IWin32Window> OpenAbout;   // the About window, which otherwise opens only from the tray menu
    public UpdateInfo Updates;               // the update status and how to check or install
    public int QuickKey;                 // 0 — off, 1 — double Shift, 2 — Caps Lock
    public int Language;                 // see L.Choices
    public int ThemeMode;                // see Theme.Mode
    public int SkinNumber;               // the look chosen (Skin.Number)
    public bool Holiday = true;          // a holiday's own look while the holiday lasts
    public bool SkinLogo = true, SkinSound = true;   // the look changes the fish and the sound too
    public Color CustomAccent = Skin.DefaultAccent, CustomFish = Skin.DefaultFish;   // the colors of the custom look
    public Dictionary<string, string> SoundMap = new Dictionary<string, string>();   // event → sound (Sounds)
    public string SoundsDir;             // where the user's .wav files are
    public Keys[] Hotkeys;
    public string Replacements, Exclusions, Ignored, Learned, AppLayouts;
    public IList<LayoutTable> InstalledLayouts = new List<LayoutTable>();
    public Dictionary<string, Color> LayoutColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);   // "EN" → color
    public int StartPage { get; set; }   // the section to open with (tests\Preview opens any)
    // Statistics: mistakes corrected in all, since when, and today, in a week, in a month and the minutes that saved
    // (a period's -1: the count by day began after it did)
    public int StatsTotal;
    public DateTime? StatsSince;
    public int[] StatsRecent;
    public Dictionary<int, int> StatsDays;   // yyyyMMdd → mistakes caught that day
    public int StatsDaysSince;               // the day counting by day began, yyyyMMdd
    public Action ResetStats;
    // Apply: KeyFish takes the values of this window's fields and keeps them; ApplyLook: only the look's, at once when
    // one is picked. True when the window has to be built again to show them (another language, theme or look)
    public Func<SettingsForm, bool> Apply, ApplyLook;
    string applied;        // the settings as last applied (Snapshot), to ask about changes left unapplied on closing
    bool building;         // the window is being built: its controls' first values aren't changes
    bool rebuildPending;   // a look was picked and the window is about to be built again
    bool applyPending;     // a setting was changed and is about to be applied
    // Arrow keys going through the theme or language list: the window is built again once they stop
    readonly Timer rebuildTimer = new Timer { Interval = 600 };
    Action updatesRefresh;  // the Updates card of the window as built now (a check may end after a rebuild)
    // The fields for a new item, kept over a rebuild: abbreviation and text, app, layout, correct, and the new words
    TextBox replKey, replVal;
    DropDown appName, appLayout;
    ToggleSwitch appFix;
    readonly List<TextBox> wordInputs = new List<TextBox>();
    // Importing settings: KeyFish keeps a copy of the current ones first, whatever was changed before in the window
    public bool Replacing { get; private set; }
    Size baseSize;         // the window's size in 96-DPI pixels, to build it again
    NavList nav;
    Panel content;

    ToggleSwitch tAuto, tTypos, tQuiet, tCtrlZ, tColor, tAutostart, tUpdates, tBadge, tRecent, tSound, tLetters, tTypography;
    DropDown cbLanguage, cbTheme, cbQuickKey;
    ToggleSwitch tHoliday, tSkinLogo, tSkinSound;
    SkinGallery gallery;
    DropDown[] soundBoxes;
    List<string>[] soundIds;
    HotkeyBox[] hotkeyBoxes;
    DataGridView gridReplacements, gridApps, gridIgnored, gridLearned;

    const int SideWidth = 250, ContentWidth = 640;
    readonly List<Panel> pages = new List<Panel>();

    public SettingsForm()
    {
        Text = L.T("Настройки KeyFish", "KeyFish settings");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ShowInTaskbar = true;
        KeyPreview = true;
        Theme.Apply(this);
        ClientSize = new Size(SideWidth + ContentWidth + 40, 660);
        rebuildTimer.Tick += (s, e) => Rebuild();
    }

    protected override void OnLoad(EventArgs e)
    {
        SuspendLayout();
        // The window must fit above the taskbar once scaled (1080p at 150 % has room for less than 660 px): then it is
        // shorter and the pages scroll
        int chrome = Height - ClientSize.Height;
        int room = (int)((Screen.FromPoint(Cursor.Position).WorkingArea.Height - chrome - 8) / Theme.Scale);
        if (room < ClientSize.Height) ClientSize = new Size(ClientSize.Width, Math.Max(480, room));
        // The same across: a narrow screen at a large scale (1366 px at 150 %) gets a narrower window, and the pages
        // scroll sideways instead of the window running off the screen
        int chromeW = Width - ClientSize.Width;
        int roomW = (int)((Screen.FromPoint(Cursor.Position).WorkingArea.Width - chromeW - 8) / Theme.Scale);
        if (roomW < ClientSize.Width) ClientSize = new Size(Math.Max(SideWidth + 400, roomW), ClientSize.Height);
        baseSize = ClientSize;
        building = true;
        Build();
        building = false;
        Theme.ScaleWindow(this);   // built in 96-DPI pixels, now sized for this screen
        CenterToScreen();
        ResumeLayout();
        Shown += (s, ev) => PlayPictures();   // only now is the window on screen to be seen
        // Files put into the sounds folder while Settings is open show up when the window is back in front
        Activated += (s, ev) => FillSounds();
        Collect();
        applied = Snapshot();
        base.OnLoad(e);
    }

    // The sidebar, the pages and the buttons, in 96-DPI pixels of a window of baseSize (scaled by the caller)
    void Build()
    {
        replKey = replVal = null;
        appName = appLayout = null;
        appFix = null;
        wordInputs.Clear();
        // Sidebar: the fish, the name and the sections
        var side = new Panel { Location = new Point(0, 0), Size = new Size(SideWidth, baseSize.Height), BackColor = Theme.Back };
        var fish = new FishView { Location = new Point(20, 18), Size = new Size(36, 36) };
        fish.EasterEgg += (s, ev) => fish.Fury();
        side.Controls.Add(fish);
        var name = Theme.Label("KeyFish", Theme.Section);
        name.Location = new Point(64, 15);
        var ver = Theme.Label(Application.ProductVersion, Theme.Small, Theme.SubText);
        ver.Location = new Point(65, 39);
        side.Controls.Add(ver);
        side.Controls.Add(name);

        nav = new NavList { Location = new Point(8, 74), Size = new Size(SideWidth - 16, 9 * 40) };
        // The sections in the order of use: correction, what KeyFish is about, first; the system settings last
        nav.Items.Add(Tuple.Create("\uE70F", L.T("Исправление", "Correction")));
        nav.Items.Add(Tuple.Create("\uE790", L.T("Оформление", "Appearance")));
        nav.Items.Add(Tuple.Create("\uEA8F", L.T("Значок и звуки", "Icon and sounds")));
        nav.Items.Add(Tuple.Create("\uE765", L.T("Сочетания клавиш", "Hotkeys")));
        nav.Items.Add(Tuple.Create("\uE8AC", L.T("Автозамены", "Replacements")));
        nav.Items.Add(Tuple.Create("\uE71D", L.T("Программы", "Apps")));
        nav.Items.Add(Tuple.Create("\uE82D", L.T("Списки слов", "Word lists")));
        nav.Items.Add(Tuple.Create("\uE9D2", L.T("Статистика", "Statistics")));
        nav.Items.Add(Tuple.Create("\uE713", L.T("Система", "System")));
        side.Controls.Add(nav);

        // About, at the bottom of the sidebar, level with Close
        var about = new RoundButton
        {
            Text = L.T("О программе", "About KeyFish"), Glyph = "\uE946", Location = new Point(16, baseSize.Height - 50), Size = new Size(SideWidth - 32, 34)
        };
        about.Click += (s, ev) => { if (OpenAbout != null) OpenAbout(this); };
        side.Controls.Add(about);
        Controls.Add(side);

        content = new Panel { Location = new Point(SideWidth, 0) };
        content.Size = new Size(baseSize.Width - SideWidth, baseSize.Height - 64);
        content.BackColor = Theme.Back;
        Controls.Add(content);

        PickSample();
        pages.Add(CorrectionPage());
        pages.Add(LooksPage());
        pages.Add(CuesPage());
        pages.Add(HotkeysPage());
        pages.Add(ReplacementsPage());
        pages.Add(AppsPage());
        pages.Add(WordsPage());
        pages.Add(StatsPage());
        pages.Add(SystemPage());
        foreach (var p in pages) { p.Visible = false; content.Controls.Add(p); }
        nav.Selected = Math.Max(0, Math.Min(StartPage, pages.Count - 1));
        pages[nav.Selected].Visible = true;
        nav.SelectedChanged += (s, ev) =>
        {
            for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == nav.Selected;
            PlayPictures();
        };

        // Every change applies at once, as in Windows Settings: the window has only Close
        var close = new RoundButton { Text = L.T("Закрыть", "Close"), AccentStyle = true, Size = new Size(130, 34) };
        close.Location = new Point(baseSize.Width - 28 - 130, baseSize.Height - 50);
        close.Click += (s, ev) => Close();
        Controls.Add(close);
        Watch(this);
        Theme.SolidLabels(this);
    }

    // The fields (Collect has just filled them) go to KeyFish; the window is built again if it has to show them
    void ApplyNow()
    {
        bool rebuild = Apply != null && Apply(this);
        applied = Snapshot();
        if (rebuild) ScheduleRebuild();
    }

    // Every control that holds a setting reports its changes; the switches and lists made for entering a new item
    // report too, which changes nothing in the snapshot and so applies nothing
    void Watch(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is ToggleSwitch) ((ToggleSwitch)c).CheckedChanged += (s, e) => Changed();
            else if (c is DropDown) ((DropDown)c).SelectedIndexChanged += (s, e) => Changed();
            else if (c is HotkeyBox) ((HotkeyBox)c).ValueChanged += (s, e) => Changed();
            else if (c is DataGridView)
            {
                var grid = (DataGridView)c;
                grid.RowsAdded += (s, e) => Changed();
                grid.RowsRemoved += (s, e) => Changed();
                grid.CellValueChanged += (s, e) => Changed();
                grid.CellEndEdit += (s, e) => Changed();
            }
            Watch(c);
        }
    }

    // A setting changed: it is applied once the event that changed it is over (several changes at once, one apply)
    void Changed()
    {
        if (building || applied == null || applyPending || IsDisposed) return;
        applyPending = true;
        BeginInvoke(new Action(() =>
        {
            applyPending = false;
            if (IsDisposed) return;
            Collect();
            if (Snapshot() != applied) ApplyNow();
        }));
    }

    // A look picked (a card, the theme, a color, a switch of the look) applies at once; the window is built again
    // in its new colors, or only redrawn when just the fish changed
    void LookChanged()
    {
        if (building || ApplyLook == null) return;
        Collect();
        if (ApplyLook(this)) ScheduleRebuild();
        else Invalidate(true);
    }

    // Once the click that changed it is over; arrow keys going through a list wait until they stop, so each key
    // doesn't rebuild the window
    void ScheduleRebuild()
    {
        var list = ActiveControl as DropDown;
        if (list != null && list.ByKeyboard) { rebuildTimer.Stop(); rebuildTimer.Start(); return; }
        if (rebuildPending) return;
        rebuildPending = true;
        BeginInvoke(new Action(() => Rebuild()));
    }

    // The window built again in place, in its new language and colors, with the values in it, the fields for new
    // items as they were, on the same section and scroll, with the same control focused. collect: false when the
    // fields were set without the controls (an import) and are already applied
    void Rebuild(bool collect = true)
    {
        rebuildPending = false;
        rebuildTimer.Stop();
        if (IsDisposed) return;
        if (collect)
        {
            // Anything not applied yet (a table cell) is applied first: the rebuilt window takes it as applied
            Collect();
            if (applied != null && Snapshot() != applied && Apply != null) Apply(this);
        }
        int scroll = -pages[nav.Selected].AutoScrollPosition.Y;
        StartPage = nav.Selected;
        var focus = PathOf(ActiveControl);
        string[] texts = { replKey.Text, replVal.Text, appName.Text, wordInputs[0].Text, wordInputs[1].Text };
        int layout = appLayout.SelectedIndex;
        bool fix = appFix.Checked;
        SuspendLayout();
        var old = Controls.Cast<Control>().ToList();
        Controls.Clear();
        foreach (var c in old) c.Dispose();
        pages.Clear();
        Text = L.T("Настройки KeyFish", "KeyFish settings");
        Theme.Recolor(this);
        building = true;
        Build();
        replKey.Text = texts[0]; replVal.Text = texts[1]; appName.Text = texts[2]; wordInputs[0].Text = texts[3]; wordInputs[1].Text = texts[4];
        appLayout.SelectedIndex = layout;
        appFix.Checked = fix;
        building = false;
        // Only the new controls are scaled: the window keeps its size and place, no jump to 96-DPI size and back
        if (Math.Abs(Theme.Scale - 1) > 0.01f)
            foreach (Control c in Controls) c.Scale(new SizeF(Theme.Scale, Theme.Scale));
        ResumeLayout();
        pages[nav.Selected].AutoScrollPosition = new Point(0, scroll);
        // The window's own active control, not Focus(): it takes effect even while another window is in front
        var again = At(focus);
        if (again != null) ActiveControl = again;
        PlayPictures();
        // Everything in the window is applied now; in another language the files' header comments read
        // differently, which is no change to apply
        Collect();
        applied = Snapshot();
    }

    // Where a control is in the window: its page (or -1 outside the pages), then the index at each level below it.
    // The window is built the same way every time, so the same path finds the same control after a rebuild. The
    // pages are counted in their own order: Windows reorders them inside their panel as they are shown
    List<int> PathOf(Control c)
    {
        var path = new List<int>();
        for (; c != null && c != this && !pages.Contains(c as Panel); c = c.Parent)
            path.Insert(0, c.Parent == null ? -1 : c.Parent.Controls.IndexOf(c));
        if (c == null) return null;
        path.Insert(0, c == this ? -1 : pages.IndexOf((Panel)c));
        return path;
    }

    Control At(List<int> path)
    {
        if (path == null || path.Count == 0 || path[0] >= pages.Count) return null;
        Control c = path[0] < 0 ? (Control)this : pages[path[0]];
        foreach (int i in path.Skip(1))
        {
            if (i < 0 || i >= c.Controls.Count) return null;
            c = c.Controls[i];
        }
        return c;
    }

    // All the settings in one string: the same values and files an export has, and autostart. Not the look's: those
    // apply as soon as they are picked
    static readonly string[] LookValues = { "Theme", "Holiday", "Skin", "SkinLogo", "SkinSound", "CustomAccent", "CustomFish" };
    string Snapshot()
    {
        var sb = new StringBuilder(Autostart ? "1" : "0");
        foreach (var v in Values()) if (Array.IndexOf(LookValues, v.Key) < 0) sb.Append('\n').Append(v.Key).Append('=').Append(v.Value);
        foreach (var f in Files()) sb.Append('\n').Append(f.Key).Append('=').Append(f.Value);
        return sb.ToString();
    }

    // A change not applied yet (a table cell still being edited) is applied on closing
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (applied != null)
        {
            Collect();
            if (Snapshot() != applied && Apply != null) { Apply(this); applied = Snapshot(); }
        }
        base.OnFormClosing(e);
    }

    // Esc closes the window, unless a field is being edited in a table
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && FocusedHotkey() != null && FocusedHotkey().Cancel()) return true;
        // (a check box cell counts as edited as soon as it is current; only a text being typed in keeps Esc)
        if (keyData == Keys.Escape && !(ActiveControl is DataGridView && ((DataGridView)ActiveControl).EditingControl is TextBox))
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    HotkeyBox FocusedHotkey() { return hotkeyBoxes == null ? null : hotkeyBoxes.FirstOrDefault(b => b.Focused); }

    // ---------- Building blocks ----------

    Panel Page(string title, out int y)
    {
        var page = new Panel { Location = new Point(0, 0), Size = content.Size, AutoScroll = true, BackColor = Theme.Back };
        Theme.DarkScrollbars(page);
        var t = Theme.Label(title, Theme.Title);
        t.Location = new Point(16, 18);
        page.Controls.Add(t);
        y = 76;
        return page;
    }

    // One setting: a card with a title, an optional description and a control on the right
    // picture: an optional "before → after" picture, just left of the control; the titles stay in one column
    static void Row(Control page, ref int y, string title, string description, Control right, FeaturePreview picture = null)
    {
        if (right.AccessibleName == null) right.AccessibleName = title;   // Narrator: "Fix typos, check box, checked"
        const int pad = 18, left = pad;
        int textWidth = ContentWidth - pad * 3 - right.Width - (picture == null ? 0 : picture.Width + 16);
        var card = new Card { Location = new Point(16, y), Width = ContentWidth };
        // The title (and description) are centered vertically, like in Windows Settings
        var t = Theme.Wrapped(title, textWidth);
        card.Controls.Add(t);
        Label d = null;
        int textHeight = t.Height;
        if (!string.IsNullOrEmpty(description))
        {
            d = Theme.Wrapped(description, textWidth, Theme.Small, Theme.SubText);
            card.Controls.Add(d);
            textHeight += d.Height + 1;
        }
        card.Height = Math.Max(Math.Max(textHeight, right.Height) + 28, 64);
        t.Location = new Point(left, (card.Height - textHeight) / 2);
        if (d != null) d.Location = new Point(left, t.Bottom + 1);
        if (picture != null)
        {
            picture.Location = new Point(ContentWidth - pad - right.Width - 16 - picture.Width, (card.Height - picture.Height) / 2);
            picture.AccessibleName = title;
            card.Controls.Add(picture);
            // Flipping the switch shows the change again
            var toggle = right as ToggleSwitch;
            if (toggle != null) toggle.CheckedChanged += (s, e) => picture.Play();
        }
        right.Location = new Point(ContentWidth - pad - right.Width, (card.Height - right.Height) / 2);
        card.Controls.Add(right);
        page.Controls.Add(card);
        y += card.Height + 4;
    }

    static ToggleSwitch Toggle(bool value) { return new ToggleSwitch { Checked = value }; }

    // Sounds: on or off, and a sound for each event (the look's own, none, a built-in one or the user's .wav),
    // each with a button to listen to it before saving
    void SoundsCard(Control page, ref int y)
    {
        const int pad = 18, boxW = 220, playW = 40;
        var card = new Card { Location = new Point(16, y), Width = ContentWidth };
        tSound = Toggle(Sound);
        string title = L.T("Звуки", "Sounds");
        int textW = ContentWidth - pad * 3 - tSound.Width;
        var t = Theme.Wrapped(title, textW);
        t.Location = new Point(pad, 14);
        var d = Theme.Wrapped(L.T("Звуковые сигналы при исправлении и изменении текста. Для каждого события можно выбрать звук или отключить его. Собственные звуки добавляются в папку звуков в формате .wav.",
                                  "Sound signals when text is corrected or changed. A sound can be chosen or turned off for each event. Custom sounds are added to the sounds folder as .wav files."), textW, Theme.Small, Theme.SubText);
        d.Location = new Point(pad, t.Bottom + 1);
        tSound.AccessibleName = title;
        tSound.Location = new Point(ContentWidth - pad - tSound.Width, 18);
        card.Controls.Add(t);
        card.Controls.Add(d);
        card.Controls.Add(tSound);
        int cy = Math.Max(d.Bottom, tSound.Bottom) + 14;
        soundBoxes = new DropDown[Sounds.Events.Length];
        soundIds = new List<string>[Sounds.Events.Length];
        for (int i = 0; i < Sounds.Events.Length; i++)
        {
            int e = i;
            var name = Theme.Wrapped(Sounds.EventName(e), ContentWidth - pad * 3 - boxW - playW - 8);
            var box = new DropDown { Size = new Size(boxW, 32), AccessibleName = Sounds.EventName(e) };
            box.Location = new Point(ContentWidth - pad - playW - 8 - boxW, cy);
            name.Location = new Point(pad, cy + (32 - name.Height) / 2);
            var play = new RoundButton { Glyph = "\uE768", Text = "", Size = new Size(playW, 32), Location = new Point(ContentWidth - pad - playW, cy),
                                         AccessibleName = L.F("Прослушать: {0}", "Play: {0}", Sounds.EventName(e)) };
            play.Click += (s, ev) =>
            {
                int k = soundBoxes[e].SelectedIndex;
                if (k >= 0) Sounds.Play(soundIds[e][k], tSkinSound.Checked ? gallery.SelectedSkin : Skin.Classic);
            };
            soundBoxes[e] = box;
            card.Controls.Add(name);
            card.Controls.Add(box);
            card.Controls.Add(play);
            cy += 40;
        }
        FillSounds();
        // With the sounds off, their choices are grey: they do nothing until the sounds are on
        var parts = card.Controls.Cast<Control>().Where(c => c is DropDown || (c is RoundButton && c != tSound)).ToList();
        Action follow = () => { foreach (var c in parts) c.Enabled = tSound.Checked; };
        tSound.CheckedChanged += (s, ev) => follow();
        follow();
        var folder = new RoundButton { Text = L.T("Папка звуков", "Sounds folder"), Glyph = "\uE838", Size = new Size(170, 32), Location = new Point(pad, cy + 4) };
        folder.Click += (s, ev) =>
        {
            try { System.IO.Directory.CreateDirectory(SoundsDir); Process.Start("explorer.exe", "\"" + SoundsDir + "\""); } catch { }
        };
        folder.Enabled = SoundsDir != null;
        card.Controls.Add(folder);
        card.Height = folder.Bottom + 16;
        page.Controls.Add(card);
        y += card.Height + 4;
    }

    // The choices for each event: the look's sound, none, the built-in ones, the user's files; keeps what is chosen
    void FillSounds()
    {
        if (soundBoxes == null) return;
        var files = Sounds.Files();
        for (int e = 0; e < soundBoxes.Length; e++)
        {
            string current;
            if (soundIds[e] != null && soundBoxes[e].SelectedIndex >= 0) current = soundIds[e][soundBoxes[e].SelectedIndex];
            else if (!SoundMap.TryGetValue(Sounds.Events[e], out current)) current = Sounds.Default(e);
            var ids = new List<string> { Sounds.Look, Sounds.None };
            ids.AddRange(Sounds.Builtin);
            ids.AddRange(files);
            if (!ids.Contains(current)) ids.Add(current);   // a file that is gone stays chosen until changed
            soundIds[e] = ids;
            var box = soundBoxes[e];
            box.Items.Clear();
            box.Items.AddRange(ids.Select(Sounds.SoundName));
            box.SelectedIndex = ids.IndexOf(current);
        }
    }

    // The look: light or dark, the gallery of looks, a big moving picture of the chosen one as it will be, and what it
    // changes besides the colors. The pictures follow the choices at once, and the sound can be listened to before saving
    void LooksCard(Control page, ref int y)
    {
        const int pad = 18, bigW = 260, bigH = 156;
        var card = new Card { Location = new Point(16, y), Width = ContentWidth };
        cbTheme = new DropDown { Size = new Size(170, 34) };
        cbTheme.Items.AddRange(new[] { L.T("Системный", "System default"), L.T("Светлый", "Light"), L.T("Тёмный", "Dark") });
        cbTheme.SelectedIndex = Math.Max(0, Math.Min(ThemeMode, 2));
        cbTheme.AccessibleName = L.T("Цветовой режим", "Color mode");
        cbTheme.Location = new Point(ContentWidth - pad - cbTheme.Width, 16);
        string title = L.T("Тема", "Theme");
        int textW = ContentWidth - pad * 3 - cbTheme.Width;
        var t = Theme.Wrapped(title, textW);
        t.Location = new Point(pad, 14);
        var d = Theme.Wrapped(L.T("Цветовая схема окон, значок в области уведомлений и звук исправления. Изменения применяются сразу.",
                                  "Window colors, the notification area icon and the correction sound. Changes apply immediately."), textW, Theme.Small, Theme.SubText);
        d.Location = new Point(pad, t.Bottom + 1);
        card.Controls.Add(t);
        card.Controls.Add(d);
        card.Controls.Add(cbTheme);
        // The pictures in the light or dark theme as chosen here: as in Windows, light, dark
        Func<bool> dark = () => cbTheme.SelectedIndex == 0 ? Theme.WindowsDark() : cbTheme.SelectedIndex == 2;
        int top = Math.Max(d.Bottom, cbTheme.Bottom) + 12;
        // In high contrast the windows take the system's colors and no look applies: say so instead of offering one
        if (Theme.Contrast)
        {
            var note = Theme.Wrapped(L.T("Включён режим высокой контрастности: окна KeyFish используют системные цвета, тема не применяется.",
                                         "High contrast mode is on: KeyFish windows use system colors, and the theme does not apply."), ContentWidth - pad * 2, Theme.Small, Theme.Text);
            note.Location = new Point(pad, top);
            card.Controls.Add(note);
            top = note.Bottom + 10;
        }
        gallery = new SkinGallery(ContentWidth - pad * 2) { Location = new Point(pad, top), Selected = SkinNumber, Logo = SkinLogo, Dark = dark(), AccessibleName = title };
        gallery.CustomSkin = Skin.Make(CustomAccent, CustomFish);
        card.Controls.Add(gallery);
        top = gallery.Bottom + 12;

        // The colors of "Custom": choosing one shows it in the pictures and picks that look
        string colorsText = L.T("Цвета своей темы:", "Custom theme colors:");
        var colorsLabel = Theme.Wrapped(colorsText, TextRenderer.MeasureText(colorsText, Theme.Body).Width + 8);
        colorsLabel.Location = new Point(pad, top + (34 - colorsLabel.Height) / 2);
        card.Controls.Add(colorsLabel);
        int sx = pad + colorsLabel.Width + 12;
        var swatches = new List<ColorSwatch>();
        Action<string, Color, Color> swatch = (name, value, fallback) =>
        {
            var label = Theme.Wrapped(name, TextRenderer.MeasureText(name, Theme.Small).Width + 8, Theme.Small, Theme.SubText);
            var sw = new ColorSwatch { Letters = "", Color = value, Location = new Point(sx, top), AccessibleName = L.F("{0}: цвет своей темы", "{0}: custom theme color", name) };
            label.Location = new Point(sx + sw.Width + 6, top + (34 - label.Height) / 2);
            sw.Click += (s, e) => ColorPicker.Show(sw, sw.Color, c =>
            {
                sw.Color = c ?? fallback;
                sw.Invalidate();
                CustomAccent = swatches[0].Color;
                CustomFish = swatches[1].Color;
                gallery.CustomSkin = Skin.Make(CustomAccent, CustomFish);
                gallery.Selected = Skin.Custom.Number;
                Invalidate(true);
                LookChanged();
            });
            swatches.Add(sw);
            card.Controls.Add(sw);
            card.Controls.Add(label);
            sx = label.Right + 18;
        };
        swatch(L.T("Акцент", "Accent"), CustomAccent, Skin.DefaultAccent);
        swatch(L.T("Значок", "Icon"), CustomFish, Skin.DefaultFish);
        top += 34 + 18;

        tSkinLogo = Toggle(SkinLogo);
        tSkinSound = Toggle(SkinSound);
        tHoliday = Toggle(Holiday);
        var big = new FeaturePreview((g, r, ms) =>
            SkinGallery.Draw(g, new RectangleF(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4), gallery.SelectedSkin, gallery.Dark, tSkinLogo.Checked,
                             (float)Math.Sin(ms / 1000.0 * 5) * 0.7f, ms % 3000 / 3000f), 6000)
        { Size = new Size(bigW, bigH), Location = new Point(pad, top), AccessibleName = L.T("Предварительный просмотр", "Preview") };
        card.Controls.Add(big);
        gallery.SelectedChanged += (s, e) => { big.Invalidate(); LookChanged(); };
        if (Theme.Contrast)
        {
            gallery.Enabled = tSkinLogo.Enabled = tSkinSound.Enabled = tHoliday.Enabled = false;
            foreach (var sw in swatches) sw.Enabled = false;
        }
        cbTheme.SelectedIndexChanged += (s, e) => { gallery.Dark = dark(); big.Invalidate(); LookChanged(); };
        tSkinLogo.CheckedChanged += (s, e) => { gallery.Logo = tSkinLogo.Checked; big.Invalidate(); LookChanged(); };
        tSkinSound.CheckedChanged += (s, e) => LookChanged();
        tHoliday.CheckedChanged += (s, e) => LookChanged();

        // Beside the picture: what the look changes, one switch each
        int x = pad + bigW + 20, w = ContentWidth - pad - x, cy = top;
        Action<string, string, ToggleSwitch> line = (name, about, toggle) =>
        {
            int lineW = w - toggle.Width - 12;
            var n = Theme.Wrapped(name, lineW);
            n.Location = new Point(x, cy);
            card.Controls.Add(n);
            int bottom = n.Bottom;
            if (about != null)
            {
                var a = Theme.Wrapped(about, lineW, Theme.Small, Theme.SubText);
                a.Location = new Point(x, n.Bottom + 1);
                card.Controls.Add(a);
                bottom = a.Bottom;
            }
            toggle.AccessibleName = name;
            toggle.Location = new Point(x + w - toggle.Width, cy + 2);
            card.Controls.Add(toggle);
            cy = bottom + 12;
        };
        line(L.T("Значок в стиле темы", "Themed icon"), L.T("Логотип и значок в области уведомлений.", "The logo and the notification area icon."), tSkinLogo);
        line(L.T("Звук темы", "Theme sound"), L.T("Используется как звук исправления.", "Used as the correction sound."), tSkinSound);
        var listen = new RoundButton { Text = L.T("Прослушать", "Play"), Glyph = "\uE768", Size = new Size(130, 30), Location = new Point(x, cy - 6) };
        listen.Click += (s, e) => Sounds.Play(Sounds.Look, tSkinSound.Checked ? gallery.SelectedSkin : Skin.Classic);
        card.Controls.Add(listen);
        cy = listen.Bottom + 12;
        var seasons = Skin.All.Where(k => k.Season != null).Select(k => k.Name + ": " + k.SeasonText);
        line(L.T("Автоматически включать праздничные темы", "Turn on holiday themes automatically"), string.Join("; ", seasons) + ".", tHoliday);
        card.Height = Math.Max(big.Bottom, cy - 12) + 16;
        page.Controls.Add(card);
        y += card.Height + 4;
    }

    // A heading over a group of settings, like in Windows Settings
    static void Group(Control page, ref int y, string title)
    {
        y += 14;
        var l = Theme.Label(title, Theme.Strong);
        l.Location = new Point(18, y);
        page.Controls.Add(l);
        y += l.Height + 8;
    }

    // The pictures of the page now shown start one after another, top to bottom, and keep looping
    void PlayPictures()
    {
        var page = pages.FirstOrDefault(p => p.Visible);
        if (page == null) return;
        var found = new List<FeaturePreview>();
        var stack = new Stack<Control>();
        stack.Push(page);
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            var f = c as FeaturePreview;
            if (f != null) found.Add(f);
            foreach (Control child in c.Controls) stack.Push(child);
        }
        found.Sort((a, b) => a.Parent.Top.CompareTo(b.Parent.Top));
        for (int i = 0; i < found.Count; i++) found[i].Play(200 + i * 180);
    }

    // A color for each installed layout: a click on its circle opens the palette
    // The icon in the color of the layout: each installed layout's own icon in its color, with its name, and a click on
    // one chooses that color (the layout code in the tray and the tag at the cursor use it too), then the switch
    void ColorsRow(Panel page, ref int y)
    {
        string title = L.T("Цвет значка показывает раскладку", "Icon color indicates the layout");
        var holder = new Panel { BackColor = Color.Transparent };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<Tuple<ColorSwatch, Label>>();
        var tags = new List<Tuple<string, bool>>();
        // A layout's own default may change when another layout's color does: the swatches without a chosen color follow
        Action refresh = () =>
        {
            for (int i = 0; i < items.Count; i++)
                if (!items[i].Item1.Custom) { items[i].Item1.Color = ColorOf(tags[i].Item1, tags[i].Item2); items[i].Item1.Invalidate(); }
        };
        foreach (var t in InstalledLayouts)
        {
            string name = (t.Name ?? "").Split('-')[0].ToUpperInvariant();
            if (name.Length == 0 || !seen.Add(name)) continue;
            bool other = t.Script != null && t.Script != "Latin";
            var swatch = new ColorSwatch { Letters = name, Picture = FishIn };
            swatch.Custom = LayoutColors.ContainsKey(name);
            swatch.Color = ColorOf(name, other);
            swatch.AccessibleName = L.F("Цвет {0}", "Color of {0}", name);
            swatch.Click += (s, e) => ColorPicker.Show(swatch, swatch.Color, c =>
            {
                if (c == null) { LayoutColors.Remove(name); swatch.Custom = false; }
                else { LayoutColors[name] = c.Value; swatch.Color = c.Value; swatch.Custom = true; }
                refresh();
                swatch.Invalidate();
                Changed();
            });
            // The name beside its icon, centered on it
            var label = Theme.Wrapped(name, TextRenderer.MeasureText(name, Theme.Small).Width + 8, Theme.Small, Theme.SubText);
            label.Height = swatch.Height;
            label.TextAlign = ContentAlignment.MiddleLeft;
            holder.Controls.Add(swatch);
            holder.Controls.Add(label);
            items.Add(Tuple.Create(swatch, label));
            tags.Add(Tuple.Create(name, other));
        }
        // Two icons to a line, in columns as wide as the widest name; more layouts take more lines
        const int perLine = 2, gap = 12, lineGap = 4;
        int column = items.Count == 0 ? 0 : items.Max(i => i.Item1.Width + 2 + i.Item2.Width) + gap;
        for (int i = 0; i < items.Count; i++)
        {
            var at = new Point(i % perLine * column, i / perLine * (34 + lineGap));
            items[i].Item1.Location = at;
            items[i].Item2.Location = new Point(at.X + items[i].Item1.Width + 2, at.Y);
        }
        int lines = Math.Max(1, (items.Count + perLine - 1) / perLine);
        holder.Height = lines * 34 + (lines - 1) * lineGap;
        tColor = Toggle(LayoutColor);
        tColor.AccessibleName = title;
        tColor.Location = new Point(Math.Min(items.Count, perLine) * column + 4, (holder.Height - tColor.Height) / 2);
        holder.Controls.Add(tColor);
        holder.Width = tColor.Right;
        Row(page, ref y, title,
            L.T("Значок в области уведомлений окрашивается в цвет текущей раскладки; этот же цвет используется для кода раскладки и индикатора у курсора. Чтобы изменить цвет, нажмите на значок раскладки.",
                "The notification area icon takes the color of the current layout; the same color is used for the layout code and the indicator at the cursor. To change a color, click the layout's icon."), holder);
    }

    // The tray icon in a color, for the color choice of a layout
    Image FishIn(Color color)
    {
        string key = "fish|" + color.ToArgb();
        Image image;
        if (sampleIcons.TryGetValue(key, out image)) return image;
        using (var fish = Pictures.Fish(false)) image = global::LayoutColors.Recolor((Bitmap)fish, color);
        sampleIcons[key] = image;
        return image;
    }

    // The extended dictionaries of the installed layouts' languages: the switch downloads or removes them at once,
    // not on Save; it stays grey while that goes on
    void DictionaryRow(Panel page, ref int y)
    {
        var layouts = InstalledLayouts.Where(t => t.Lang != null && Dictionaries.Available.Contains(t.Lang)).ToList();
        var langs = layouts.Select(t => t.Lang).Distinct().ToList();
        string tags = string.Join(", ", layouts.Select(TagOf).Where(s => s.Length > 0).Distinct());
        Func<bool> installed = () => langs.Count > 0 && langs.All(Dictionaries.Installed);
        var toggle = Toggle(installed());
        toggle.Enabled = langs.Count > 0;
        bool syncing = false;
        toggle.CheckedChanged += (s, e) =>
        {
            bool remove = !toggle.Checked;
            if (syncing || remove != installed()) return;
            toggle.Enabled = false;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string error = null;
                if (remove) Dictionaries.Remove(langs);
                else error = Dictionaries.Download(langs);
                foreach (string lang in langs) try { Detector.Reload(lang); } catch { }   // KeyFish uses the new words at once
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        syncing = true;
                        toggle.Checked = installed();
                        syncing = false;
                        toggle.Enabled = true;
                        if (error != null) ThemedMessage.Show(this, error, L.T("Расширенный словарь", "Extended dictionary"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }));
                }
                catch (InvalidOperationException) { }   // Settings was closed meanwhile
            });
        };
        Row(page, ref y, L.T("Расширенный словарь", "Extended dictionary"),
            L.F("Около 100 тысяч дополнительных слов на язык ({0}): редкие слова, имена собственные, разговорная лексика. Загружается с GitHub при включении (около 1 МБ).",
                "About 100,000 additional words per language ({0}): rare words, proper names, colloquial vocabulary. Downloaded from GitHub when turned on (about 1 MB).",
                tags), toggle, Typing(rareTyped, rareWord));
    }

    // A word typed with a mistake and what KeyFish makes of it (undo: then Ctrl+Z puts the typed word back)
    static FeaturePreview Typing(string typed, string fixedWord, bool undo = false)
    {
        return new FeaturePreview((g, r, ms) => Pictures.Typing(g, r, ms, typed, fixedWord, undo), Pictures.TypingLength(typed, undo), Pictures.TypingRest(typed, undo));
    }

    // The tray icon as the layout is switched there and back
    static FeaturePreview Switching(Func<Image> first, Func<Image> second)
    {
        return new FeaturePreview((g, r, ms) => Pictures.Switching(g, r, ms, first, second), Pictures.SwitchLength, Pictures.SwitchRest);
    }

    // ---------- the pictures show the user's own layouts ----------

    // The two layouts the pictures switch between: the first Latin one and the first with another alphabet
    string latinTag = "EN", otherTag = "RU";
    // A word of the other layout's language, and what the same keys type in the Latin layout
    string sampleWord = "привет", sampleTyped = "ghbdtn";
    // A word only the extended dictionary knows, for its picture (the sample word where there is none)
    string rareWord = "дедлайн", rareTyped = "ltlkfqy";
    static readonly Dictionary<string, string> RareWords = new Dictionary<string, string> { { "ru", "дедлайн" } };

    static readonly Dictionary<string, string> SampleWords = new Dictionary<string, string>
    {
        { "ru", "привет" }, { "uk", "привіт" }, { "be", "вітаю" }, { "bg", "здравей" }, { "mk", "здраво" },
        { "sr", "здраво" }, { "el", "γεια" }, { "he", "שלום" }, { "ka", "გამარჯობა" }
    };

    static string TagOf(LayoutTable t) { return (t.Name ?? "").Split('-')[0].ToUpperInvariant(); }

    // Without a layout of another alphabet (or a word for its language) the pictures keep EN, RU and ghbdtn → привет
    void PickSample()
    {
        var latin = InstalledLayouts.FirstOrDefault(t => t.Script == "Latin" && TagOf(t).Length > 0);
        var other = InstalledLayouts.FirstOrDefault(t => t.Script != null && t.Script != "Latin" && TagOf(t).Length > 0);
        if (latin != null) latinTag = TagOf(latin);
        if (other == null) return;
        otherTag = TagOf(other);
        string word;
        if (latin == null || other.Lang == null || !SampleWords.TryGetValue(other.Lang.Split('-')[0], out word)) return;
        var keys = other.Keys(word);
        if (keys == null) return;
        string typed = latin.Type(keys);
        if (typed.IndexOf('\uFFFD') >= 0 || typed == word) return;
        sampleWord = rareWord = word;
        sampleTyped = rareTyped = typed;
        string rare;
        if (!RareWords.TryGetValue(other.Lang.Split('-')[0], out rare)) return;
        var rareKeys = other.Keys(rare);
        string rareType = rareKeys == null ? null : latin.Type(rareKeys);
        if (rareType == null || rareType.IndexOf('\uFFFD') >= 0 || rareType == rare) return;
        rareWord = rare;
        rareTyped = rareType;
    }

    // A typo in the sample word: two letters swapped (привет → пирвет)
    string SampleTypo() { return sampleWord.Substring(0, 1) + sampleWord[2] + sampleWord[1] + sampleWord.Substring(3); }

    // The color the user chose for a layout, or the default one: blue for Latin, orange for the rest
    Color ColorOf(string tag, bool other)
    {
        return global::LayoutColors.For(tag, other, InstalledLayouts, LayoutColors);
    }

    // The tray icons in the pictures, made once per layout and color (the colors may change while Settings is open)
    readonly Dictionary<string, Image> sampleIcons = new Dictionary<string, Image>();

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        foreach (var image in sampleIcons.Values) image.Dispose();
        sampleIcons.Clear();
        rebuildTimer.Dispose();
        base.OnFormClosed(e);
    }
    Func<Image> SampleIcon(bool letters, bool other)
    {
        return () =>
        {
            string tag = other ? otherTag : latinTag;
            Color color = ColorOf(tag, other);
            string key = letters + "|" + tag + "|" + color.ToArgb();
            Image image;
            if (sampleIcons.TryGetValue(key, out image)) return image;
            if (letters) image = Pictures.Letters(tag, color);
            else using (var fish = Pictures.Fish(false)) image = global::LayoutColors.Recolor((Bitmap)fish, color);
            sampleIcons[key] = image;
            return image;
        };
    }

    static void Intro(Control page, ref int y, string text)
    {
        var l = Theme.Wrapped(text, ContentWidth, Theme.Body, Theme.SubText);
        l.Location = new Point(16, y);
        page.Controls.Add(l);
        y += l.Height + 12;
    }

    static DataGridViewTextBoxColumn TextColumn(string header, int weight, bool readOnly = true)
    {
        return new DataGridViewTextBoxColumn { HeaderText = header, FillWeight = weight, ReadOnly = readOnly, SortMode = DataGridViewColumnSortMode.Automatic };
    }

    // Delete removes the selected rows
    static void DeleteWithKey(DataGridView grid)
    {
        grid.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Delete || grid.IsCurrentCellInEditMode) return;
            DeleteSelected(grid);
            e.Handled = true;
        };
    }

    static void DeleteSelected(DataGridView grid)
    {
        foreach (DataGridViewRow r in grid.SelectedRows.Cast<DataGridViewRow>().ToList()) grid.Rows.Remove(r);
    }

    static IEnumerable<string> Lines(string text)
    {
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string s = raw.Trim();
            if (s.Length > 0 && !s.StartsWith("#")) yield return s;
        }
    }

    // ---------- Statistics ----------

    static readonly Font Big = new Font(Theme.Title.FontFamily, Theme.Title.Size * 1.25f, Theme.Title.Style);

    // The counts, and how they are made: only numbers are kept, never what was typed
    Panel StatsPage()
    {
        int y;
        var page = Page(L.T("Статистика", "Statistics"), out y);
        var card = StatsCard(StatsTotal, StatsSince, StatsRecent);
        card.Location = new Point(16, y);
        page.Controls.Add(card);
        y = card.Bottom + 4;
        // The last 30 days by day
        var chartCard = new Card { Location = new Point(16, y), Width = ContentWidth };
        var chartTitle = Theme.Wrapped(L.T("Исправления за последние 30 дней", "Corrections over the last 30 days"), ContentWidth - 36);
        chartTitle.Location = new Point(18, 14);
        chartCard.Controls.Add(chartTitle);
        var chart = new StatsChart(StatsDays, StatsDaysSince) { Location = new Point(18, chartTitle.Bottom + 8), Size = new Size(ContentWidth - 36, 120) };
        chartCard.Controls.Add(chart);
        chartCard.Height = chart.Bottom + 12;
        page.Controls.Add(chartCard);
        y = chartCard.Bottom + 8;
        if (StatsRecent != null && StatsRecent.Take(3).Any(n => n < 0))
            Intro(page, ref y, L.T("Прочерк означает, что подсчёт по дням начат позже начала периода и число за весь период неизвестно.",
                                   "A dash means that counting by day began after the start of the period, so the number for the whole period is unknown."));
        y += 8;
        InfoCard(page, ref y, L.T("Набранный текст не записывается", "Typed text is not recorded"),
            L.T("KeyFish не ведёт журнал набора. Для статистики сохраняются только счётчики: общее число исправлений, дата начала подсчёта и число исправлений за каждый из последних 62 дней. Слова, исправленный текст, названия программ и время исправлений не сохраняются.",
                "KeyFish does not keep a typing log. Only counters are stored for the statistics: the total number of corrections, the date counting began and the number of corrections for each of the last 62 days. Words, corrected text, app names and correction times are not stored."));
        InfoCard(page, ref y, L.T("Где хранятся данные", "Where the data is stored"),
            L.T("Счётчики хранятся в реестре Windows в разделе HKCU\\Software\\KeyFish (StatsTotal, StatsSince, StatsDays) и не передаются за пределы компьютера.",
                "The counters are stored in the Windows registry under HKCU\\Software\\KeyFish (StatsTotal, StatsSince, StatsDays) and never leave the computer."));
        InfoCard(page, ref y, L.T("Что учитывается", "What is counted"),
            L.T("Автоматические исправления при наборе: раскладки, регистра и опечаток. Автозамены, типографика и действия, выполненные по сочетаниям клавиш, не учитываются.",
                "Automatic corrections while typing: of the layout, the case and typos. Replacements, typography and actions performed with hotkeys are not counted."));
        InfoCard(page, ref y, L.T("Сэкономленное время", "Time saved"),
            L.T("Рассчитывается из 4 секунд на каждое исправление: столько в среднем занимает удалить слово, переключить раскладку и набрать его заново.",
                "Calculated as 4 seconds per correction: on average, the time it takes to erase a word, switch the layout and type it again."));
        if (ResetStats != null)
        {
            y += 8;
            var reset = new RoundButton { Text = L.T("Сбросить", "Reset"), Size = new Size(170, 34) };
            reset.Click += (s, e) =>
            {
                if (ThemedMessage.Show(this, L.T("Все счётчики статистики будут обнулены, подсчёт начнётся заново с сегодняшнего дня. Продолжить?",
                                                 "All statistics counters will be set to zero, and counting will start again from today. Continue?"),
                                       L.T("Сброс статистики", "Reset statistics"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                ResetStats();
                StatsTotal = 0;
                StatsSince = DateTime.Today;
                StatsRecent = new[] { 0, 0, 0, 0 };
                StatsDays = new Dictionary<int, int>();
                StatsDaysSince = DateTime.Today.Year * 10000 + DateTime.Today.Month * 100 + DateTime.Today.Day;
                Rebuild();
            };
            Row(page, ref y, L.T("Сбросить статистику", "Reset statistics"),
                L.T("Все счётчики обнуляются, подсчёт начинается заново с сегодняшнего дня.", "All counters are set to zero, and counting starts again from today."), reset);
        }
        return End(page, y);
    }

    // The total as a big number with what it counts under it, and the count by period under a line
    static Card StatsCard(int total, DateTime? since, int[] recent)
    {
        int width = ContentWidth;
        var card = new Card { Width = width };
        var big = Theme.Wrapped(total.ToString("N0", L.Culture), width - 24, Big);
        big.TextAlign = ContentAlignment.MiddleCenter;
        big.Location = new Point(12, 10);
        card.Controls.Add(big);
        // "corrected since" rather than "N mistakes": no plural forms needed this way
        var caption = Theme.Wrapped(since.HasValue ? L.F("исправлено ошибок с {0}", "mistakes corrected since {0}", since.Value.ToString("d", L.Culture))
                                                   : L.T("исправлено ошибок", "mistakes corrected"), width - 24, Theme.Small, Theme.SubText);
        caption.TextAlign = ContentAlignment.MiddleCenter;
        caption.Location = new Point(12, big.Bottom - 4);
        card.Controls.Add(caption);
        card.Height = caption.Bottom + 12;
        if (recent != null)
        {
            // A dash: the count by day is younger than the period
            Func<int, string> number = n => n < 0 ? "—" : n.ToString("N0", L.Culture);
            var strip = new StatsStrip(new[]
            {
                Tuple.Create(number(recent[0]), L.T("сегодня", "today")),
                Tuple.Create(number(recent[1]), L.T("за неделю", "this week")),
                Tuple.Create(number(recent[2]), L.T("за месяц", "this month")),
                Tuple.Create(L.F("≈{0} мин", "≈{0} min", recent[3].ToString("N0", L.Culture)), L.T("сэкономлено", "saved"))
            }) { Location = new Point(0, caption.Bottom + 10), Size = new Size(width, 56) };
            card.Controls.Add(strip);
            card.Height = strip.Bottom + 6;
        }
        return card;
    }

    // A card of text alone: a title and an explanation under it
    static void InfoCard(Control page, ref int y, string title, string text)
    {
        const int pad = 18;
        var card = new Card { Location = new Point(16, y), Width = ContentWidth };
        var t = Theme.Wrapped(title, ContentWidth - pad * 2);
        t.Location = new Point(pad, 14);
        var d = Theme.Wrapped(text, ContentWidth - pad * 2, Theme.Small, Theme.SubText);
        d.Location = new Point(pad, t.Bottom + 2);
        card.Controls.Add(t);
        card.Controls.Add(d);
        card.Height = d.Bottom + 14;
        page.Controls.Add(card);
        y += card.Height + 4;
    }

    // ---------- General ----------

    // The language, autostart, updates (the status and the daily check side by side), all settings in one file, and
    // the donation last: it is the least needed
    Panel SystemPage()
    {
        int y;
        var page = Page(L.T("Система", "System"), out y);
        cbLanguage = new DropDown { Size = new Size(170, 34) };   // as wide and tall as the buttons below
        cbLanguage.Items.AddRange(L.Choices);
        cbLanguage.SelectedIndex = Math.Max(0, Math.Min(Language, cbLanguage.Items.Count - 1));
        Row(page, ref y, L.T("Язык интерфейса", "Interface language"), null, cbLanguage);
        Row(page, ref y, L.T("Запускать вместе с Windows", "Start with Windows"), null, tAutostart = Toggle(Autostart));
        UpdatesRow(page, ref y);
        Row(page, ref y, L.T("Проверять обновления", "Check for updates"),
            L.T("Раз в сутки KeyFish запрашивает у GitHub сведения о новой версии. Другие данные не передаются.",
                "Once a day KeyFish checks GitHub for a new version. No other data is sent."), tUpdates = Toggle(CheckUpdates));
        ArchiveRow(page, ref y);
        page.Controls.Add(SupportCard(ref y));
        return End(page, y);
    }

    Panel LooksPage()
    {
        int y;
        var page = Page(L.T("Оформление", "Appearance"), out y);
        LooksCard(page, ref y);
        return End(page, y);
    }

    Panel CorrectionPage()
    {
        int y;
        var page = Page(L.T("Исправление", "Correction"), out y);
        Row(page, ref y, L.T("Исправлять раскладку при наборе", "Fix the keyboard layout as you type"),
            L.T("Пример: ghbdtn → привет, руддщ → hello. Автозамены работают только при включённом параметре.",
                "Example: ghbdtn → привет, руддщ → hello. Replacements work only while this is on."), tAuto = Toggle(AutoCorrect),
            Typing(sampleTyped, sampleWord));
        DictionaryRow(page, ref y);
        Row(page, ref y, L.T("Исправлять опечатки", "Fix typos"),
            L.T("Экспериментальная функция. Использует проверку орфографии Windows: пирвет → привет.", "Experimental. Uses Windows spell checking: recieve → receive."), tTypos = Toggle(Typos),
            Typing(SampleTypo(), sampleWord));
        Row(page, ref y, L.T("Типографика", "Typography"),
            L.T("Дефис между словами заменяется тире, три точки — многоточием, (c) (r) (tm) — символами © ® ™.",
                "A hyphen between words is replaced with a dash, three dots with an ellipsis, (c) (r) (tm) with © ® ™."), tTypography = Toggle(Typography),
            Typing("(c)", "\u00A9"));
        Row(page, ref y, L.T("Отмена автоисправления клавишами Ctrl+Z", "Undo auto-correction with Ctrl+Z"),
            L.T("Действует сразу после исправления, как Pause. Повторное нажатие Ctrl+Z работает в программе как обычно.",
                "Works right after a correction, as Pause does. Pressing Ctrl+Z again works in the app as usual."), tCtrlZ = Toggle(UndoCtrlZ),
            Typing(sampleTyped, sampleWord, true));
        Row(page, ref y, L.T("Не исправлять в играх и полноэкранных программах", "Don't correct in games and full-screen apps"), null, tQuiet = Toggle(QuietFullscreen));
        return End(page, y);
    }

    Panel CuesPage()
    {
        int y;
        var page = Page(L.T("Значок и звуки", "Icon and sounds"), out y);
        ColorsRow(page, ref y);
        Row(page, ref y, L.T("Код раскладки вместо значка", "Layout code instead of the icon"),
            L.T("В области уведомлений отображается код раскладки (EN, RU и т. д.) в цвете раскладки. Серый цвет означает, что исправление отключено или приостановлено.",
                "The notification area shows the layout code (EN, RU, etc.) in the layout color. Grey means correction is off or paused."),
            tLetters = Toggle(TrayLetters), Switching(SampleIcon(true, false), SampleIcon(true, true)));
        Row(page, ref y, L.T("Показывать раскладку у курсора", "Show the layout at the cursor"),
            L.T("При смене раскладки её код (EN, RU и т. д.) на секунду отображается рядом с текстовым курсором, а если программа не сообщает его положение, — рядом с указателем мыши.",
                "When the layout changes, its code (EN, RU, etc.) appears next to the text cursor for one second, or next to the mouse pointer if the app does not report the cursor position."), tBadge = Toggle(LayoutBadge),
            new FeaturePreview((g, r, ms) => Pictures.Badge(g, r, ms, sampleWord, otherTag, ColorOf(otherTag, true), latinTag, ColorOf(latinTag, false)),
                               Pictures.BadgeLength, Pictures.BadgeRest));
        Row(page, ref y, L.T("Последние исправления в меню значка", "Recent corrections in the icon menu"),
            L.T("Выбор исправления в меню добавляет слово в список «Не исправлять». Список хранится только в оперативной памяти.",
                "Choosing a correction in the menu adds the word to the Never correct list. The list is kept in memory only."), tRecent = Toggle(RecentMenu));
        SoundsCard(page, ref y);
        return End(page, y);
    }

    // All settings in one file: to move to another computer or to go back to an earlier state
    void ArchiveRow(Panel page, ref int y)
    {
        var export = new RoundButton { Text = L.T("Экспорт…", "Export…"), Location = new Point(0, 0), Size = new Size(110, 32) };
        var import = new RoundButton { Text = L.T("Импорт…", "Import…"), Location = new Point(118, 0), Size = new Size(110, 32) };
        export.Click += (s, e) => ExportSettings();
        import.Click += (s, e) => ImportSettings();
        var archive = new Panel { Size = new Size(228, 32), BackColor = Color.Transparent };
        archive.Controls.Add(export);
        archive.Controls.Add(import);
        Row(page, ref y, L.T("Экспорт и импорт настроек", "Export and import settings"),
            L.T("Все настройки в одном файле для переноса на другой компьютер. Перед изменением настроек их предыдущая копия сохраняется в папке backups (последние 10 копий); копии также можно импортировать.",
                "All settings in one file, for moving to another computer. Before settings are changed, a copy of the previous ones is saved in the backups folder (the last 10 copies); these copies can also be imported."), archive);
    }

    static Panel End(Panel page, int y)
    {
        page.Controls.Add(new Panel { Location = new Point(0, y), Size = new Size(1, 16) });   // bottom margin when scrolled
        return page;
    }

    // KeyFish is free: the way to support it, at the end of System and in About
    static Card SupportCard(ref int y)
    {
        var support = new RoundButton
        {
            // Like in the About window: a red heart reads well on a plain button, not on the accent color
            Text = L.T("Поддержать", "Donate"), Glyph = "\uEB52", GlyphColor = Color.FromArgb(232, 72, 85), Size = new Size(170, 34)
        };
        support.Click += (s, e) => { try { Process.Start("https://ko-fi.com/pravken"); } catch { } };
        var holder = new Panel();
        Row(holder, ref y, L.T("KeyFish распространяется бесплатно", "KeyFish is free of charge"),
            L.T("Поддержать разработку можно добровольным пожертвованием.", "Development can be supported with a voluntary donation."), support);
        var card = (Card)holder.Controls[0];
        holder.Controls.Remove(card);
        return card;
    }

    // Updates: what the last check found, and a button that checks now — or installs, when a new version is out
    void UpdatesRow(Panel page, ref int y)
    {
        if (Updates == null) return;
        var button = new RoundButton { Size = new Size(170, 34) };
        // The status may grow by a line ("Couldn't reach GitHub…"): the row is made for the longest one
        string longest = L.T("Не удалось подключиться к GitHub. Проверьте подключение к интернету и повторите попытку позже.", "Could not connect to GitHub. Check the internet connection and try again later.");
        Row(page, ref y, L.T("Обновления", "Updates"), longest, button);
        var status = (Label)button.Parent.Controls[1];
        Action refresh = updatesRefresh = () =>
        {
            status.Text = Updates.Status;
            status.ForeColor = Updates.NewVersion ? Theme.AccentFill : Theme.SubText;
            button.Text = Updates.NewVersion ? L.T("Обновить", "Update") : L.T("Проверить", "Check");
            button.AccentStyle = Updates.NewVersion;
            button.Enabled = !Updates.Checking;
            button.Invalidate();
        };
        button.Click += (s, e) =>
        {
            if (Updates.NewVersion) { Updates.Install(); return; }
            Updates.Check(() => { if (!IsDisposed && updatesRefresh != null) updatesRefresh(); });
            refresh();
        };
        refresh();
    }

    // ---------- Hotkeys ----------

    Panel HotkeysPage()
    {
        int y;
        var page = Page(L.T("Сочетания клавиш", "Hotkeys"), out y);
        Intro(page, ref y, L.T("Чтобы изменить сочетание, щёлкните поле и нажмите новые клавиши. Esc — отмена, Backspace — отключить сочетание.",
                              "To change a hotkey, click its field and press the new keys. Esc cancels; Backspace turns the hotkey off."));
        hotkeyBoxes = new HotkeyBox[Hotkeys.Length];
        for (int i = 0; i < Hotkeys.Length; i++)
        {
            var box = new HotkeyBox { Value = Hotkeys[i] };
            int index = i;
            var reset = new RoundButton { Text = L.T("Сброс", "Reset"), Location = new Point(178, 0), Size = new Size(82, 32) };
            reset.Click += (s, e) => hotkeyBoxes[index].Value = App.HotkeyDefaults[index];
            var holder = new Panel { Size = new Size(260, 32), BackColor = Color.Transparent };
            holder.Controls.Add(Theme.Framed(box, 0, 0, 170, 32));   // as tall as Reset next to it
            holder.Controls.Add(reset);
            hotkeyBoxes[i] = box;
            Row(page, ref y, App.HotkeyTitles[i], null, holder);
        }

        cbQuickKey = new DropDown { Width = 260 };
        cbQuickKey.Items.AddRange(new[] { L.T("Выключено", "Off"), L.T("Двойной Shift", "Double Shift"), "Caps Lock" });
        cbQuickKey.SelectedIndex = Math.Max(0, Math.Min(QuickKey, 2));
        Row(page, ref y, L.T("Исправление последнего слова одной клавишей", "Fix the last word with a single key"),
            L.T("Выполняет то же действие, что и первое сочетание. Shift+Caps Lock по-прежнему включает и выключает заглавные буквы.",
                "Performs the same action as the first hotkey. Shift+Caps Lock still turns capital letters on and off."), cbQuickKey);
        return page;
    }

    // ---------- Replacements ----------

    Panel ReplacementsPage()
    {
        int y;
        var page = Page(L.T("Автозамены", "Replacements"), out y);
        Intro(page, ref y, L.T("Сокращение, за которым следует пробел, заменяется заданным текстом. Сокращение распознаётся и в другой раскладке: «спс» заменяется, даже если набрано «cgc».",
                              "An abbreviation followed by a space is replaced with the specified text. Abbreviations are recognized in the other layout too: \"thx\" expands even when typed as «ерч»."));

        // The editor: abbreviation → text, then Add (or Save when the abbreviation is already in the table)
        var editor = new Card { Location = new Point(16, y), Size = new Size(ContentWidth, 132) };
        var lKey = Theme.Label(L.T("Сокращение", "Abbreviation"), Theme.Small, Theme.SubText); lKey.Location = new Point(16, 10);
        var lVal = Theme.Label(L.T("Замена (Enter — новая строка)", "Text (Enter — new line)"), Theme.Small, Theme.SubText); lVal.Location = new Point(206, 10);
        var key = replKey = Theme.TextField(L.T("спс", "thx"));

        var arrow = Theme.Label("→", Theme.Section, Theme.SubText); arrow.Location = new Point(180, 32);
        var val = replVal = Theme.TextField();
        val.Multiline = true; val.AcceptsReturn = true;

        Theme.DarkScrollbars(val);
        var add = new RoundButton { Text = L.T("Добавить", "Add"), AccentStyle = true, Location = new Point(490, 32), Size = new Size(134, 32) };
        editor.Controls.AddRange(new Control[] { lKey, lVal, Theme.Framed(key, 16, 32, 160, 32), arrow, Theme.Framed(val, 206, 32, 270, 86), add });
        page.Controls.Add(editor);
        y += editor.Height + 10;

        var grid = gridReplacements = Theme.Grid();
        grid.Columns.Add(TextColumn(L.T("Сокращение", "Abbreviation"), 30));
        grid.Columns.Add(TextColumn(L.T("Замена", "Text"), 70));
        grid.ReadOnly = true;
        RoundButton del, import;
        page.Controls.Add(ListCard(grid, y, true, out del, out import));

        // The table shows line breaks as ⏎; the real text is kept in the row's Tag
        foreach (var kv in ParseReplacements(Replacements)) grid.Rows[grid.Rows.Add(kv.Key, Show(kv.Value))].Tag = kv.Value;

        Func<DataGridViewRow> find = () => grid.Rows.Cast<DataGridViewRow>()
            .FirstOrDefault(r => string.Equals((string)r.Cells[0].Value, key.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        Action refresh = () =>
        {
            add.Text = find() != null ? L.T("Сохранить", "Save") : L.T("Добавить", "Add");
            // No spaces or "=" in an abbreviation, and no "#" at its start: the file would read it as a comment
            string k = key.Text.Trim();
            add.Enabled = k.Length > 0 && k.IndexOf(' ') < 0 && k.IndexOf('=') < 0 && !k.StartsWith("#") && val.Text.Trim().Length > 0;
            del.Enabled = grid.SelectedRows.Count > 0;
        };
        key.TextChanged += (s, e) => refresh();
        val.TextChanged += (s, e) => refresh();
        grid.SelectionChanged += (s, e) =>
        {
            if (grid.SelectedRows.Count == 1 && grid.Focused)
            {
                key.Text = (string)grid.SelectedRows[0].Cells[0].Value;
                val.Text = ((string)grid.SelectedRows[0].Tag).Replace("\n", "\r\n");
            }
            refresh();
        };
        add.Click += (s, e) =>
        {
            if (!add.Enabled) return;
            string k = key.Text.Trim(), v = val.Text.Replace("\r\n", "\n").Trim();
            var row = find();
            if (row == null) { grid.Rows.Add(k, Show(v)); row = grid.Rows[grid.Rows.Count - 1]; }
            else { row.Cells[0].Value = k; row.Cells[1].Value = Show(v); }
            row.Tag = v;
            grid.ClearSelection();
            row.Selected = true;
            grid.FirstDisplayedScrollingRowIndex = row.Index;
            key.Text = val.Text = "";
            key.Focus();
        };
        del.Click += (s, e) => { DeleteSelected(grid); key.Text = val.Text = ""; refresh(); };
        // Replacements from a file of another program; the ones already in the table stay as they are
        import.Click += (s, e) =>
        {
            string path = PickFile(Importer.ReplacementFilter);
            if (path == null) return;
            int added = 0, had = 0;
            foreach (var kv in Importer.Replacements(path))
            {
                if (grid.Rows.Cast<DataGridViewRow>().Any(r => string.Equals((string)r.Cells[0].Value, kv.Key, StringComparison.OrdinalIgnoreCase))) { had++; continue; }
                grid.Rows[grid.Rows.Add(kv.Key, Show(kv.Value))].Tag = kv.Value;
                added++;
            }
            ImportResult(added, had);
            refresh();
        };
        DeleteWithKey(grid);
        refresh();
        return page;
    }

    static string Show(string value) { return value.Replace("\t", " ").Replace("\n", " ⏎ "); }

    // Same format KeyboardWatcher reads, see ReplacementFile
    static Dictionary<string, string> ParseReplacements(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in ReplacementFile.Parse(text)) map[kv.Key] = kv.Value;
        return map;
    }

    // ---------- Apps: a layout per app, and where auto-correction is off ----------

    string LayoutName(LayoutTable t)
    {
        string n;
        try
        {
            n = new CultureInfo((int)(t.Hkl.ToInt64() & 0xFFFF)).NativeName;
            n = char.ToUpper(n[0]) + n.Substring(1);
        }
        catch { n = t.Name ?? t.Hkl.ToInt64().ToString("X"); }
        // Two layouts of one language (say, US and US-International): tell them apart by the layout id
        if (InstalledLayouts.Count(o => o != t && (o.Hkl.ToInt64() & 0xFFFF) == (t.Hkl.ToInt64() & 0xFFFF)) > 0)
            n += " (" + ((t.Hkl.ToInt64() >> 16) & 0xFFFF).ToString("X4") + ")";
        return n;
    }

    static string NoLayout { get { return L.T("Не менять", "Don't change"); } }

    Panel AppsPage()
    {
        int y;
        var page = Page(L.T("Программы", "Apps"), out y);
        Intro(page, ref y, L.T("Раскладка, назначенная программе, включается при переходе в её окно. Здесь же можно отключить автоисправление для отдельных программ, например редакторов кода и терминалов.",
                              "A layout assigned to an app is turned on when its window becomes active. Auto-correction can also be turned off here for individual apps, such as code editors and terminals."));

        var layoutNames = new List<string> { NoLayout };
        var byName = new Dictionary<string, long>();
        foreach (var t in InstalledLayouts)
        {
            string n = LayoutName(t);
            if (byName.ContainsKey(n)) continue;
            byName[n] = t.Hkl.ToInt64();
            layoutNames.Add(n);
        }

        var editor = new Card { Location = new Point(16, y), Size = new Size(ContentWidth, 92) };
        var lApp = Theme.Label(L.T("Программа (имя процесса)", "App (process name)"), Theme.Small, Theme.SubText); lApp.Location = new Point(16, 10);
        var lLay = Theme.Label(L.T("Раскладка", "Layout"), Theme.Small, Theme.SubText); lLay.Location = new Point(236, 10);
        var lFix = Theme.Label(L.T("Исправлять", "Correct"), Theme.Small, Theme.SubText); lFix.Location = new Point(424, 10);
        var app = appName = new DropDown(true, L.T("Выберите или введите", "Pick or type"));
        app.Location = new Point(16, 30); app.Width = 204;
        // Going through every process takes a moment on a busy computer: the list fills in after the window is shown
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            var apps = RunningApps();
            try { BeginInvoke(new Action(() => app.Items.AddRange(apps))); }
            catch (InvalidOperationException) { }   // Settings was closed meanwhile
        });
        var lay = appLayout = new DropDown();
        lay.Location = new Point(236, 30); lay.Width = 172;
        lay.Items.AddRange(layoutNames);
        lay.SelectedIndex = 0;
        var fix = appFix = new ToggleSwitch { Checked = true, Location = new Point(428, 35) };
        var add = new RoundButton { Text = L.T("Добавить", "Add"), AccentStyle = true, Location = new Point(490, 30), Size = new Size(134, 32) };
        editor.Controls.AddRange(new Control[] { lApp, lLay, lFix, app, lay, fix, add });
        page.Controls.Add(editor);
        y += editor.Height + 10;

        var grid = gridApps = Theme.Grid();
        grid.Columns.Add(TextColumn(L.T("Программа", "App"), 40));
        // Layout cells are picked from a themed menu, not the classic combo box (see the CellClick handler below)
        var layCol = new DataGridViewComboBoxColumn
        {
            HeaderText = L.T("Раскладка", "Layout"), FillWeight = 40, FlatStyle = FlatStyle.Flat, ReadOnly = true,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing
        };
        layCol.Items.AddRange(layoutNames.ToArray());
        grid.Columns.Add(layCol);
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = L.T("Исправлять", "Correct"), FillWeight = 20, FlatStyle = FlatStyle.Flat });
        RoundButton del, import;
        page.Controls.Add(ListCard(grid, y, false, out del, out import));

        // Rows from both files: exclusions.txt (correction off) and app-layouts.txt (process = layout)
        var excluded = new HashSet<string>(Lines(Exclusions).Select(StripExe), StringComparer.OrdinalIgnoreCase);
        var layouts = ParseAppLayouts(AppLayouts);
        var names = new List<string>();
        foreach (string n in excluded.Concat(layouts.Keys)) if (!names.Contains(n, StringComparer.OrdinalIgnoreCase)) names.Add(n);
        foreach (string n in names)
        {
            long hkl;
            string layName = layouts.TryGetValue(n, out hkl) ? byName.FirstOrDefault(kv => kv.Value == hkl).Key : null;
            grid.Rows.Add(n, layName ?? NoLayout, !excluded.Contains(n));
        }

        Action refresh = () => { add.Enabled = StripExe(app.Text.Trim()).Length > 0; del.Enabled = grid.SelectedRows.Count > 0; };
        app.TextChanged += (s, e) => refresh();
        grid.SelectionChanged += (s, e) => refresh();
        add.Click += (s, e) =>
        {
            string n = StripExe(app.Text.Trim());
            if (n.Length == 0) return;
            var row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => string.Equals((string)r.Cells[0].Value, n, StringComparison.OrdinalIgnoreCase));
            if (row == null) { grid.Rows.Add(n, lay.SelectedItem ?? NoLayout, fix.Checked); row = grid.Rows[grid.Rows.Count - 1]; }
            else { row.Cells[1].Value = lay.SelectedItem ?? NoLayout; row.Cells[2].Value = fix.Checked; }
            grid.ClearSelection();
            row.Selected = true;
            app.Text = "";
            lay.SelectedIndex = 0;
            fix.Checked = true;
        };
        del.Click += (s, e) => { DeleteSelected(grid); refresh(); };
        DeleteWithKey(grid);
        // A checkbox click applies at once, not when the cell is left
        grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.DataError += (s, e) => e.ThrowException = false;
        grid.CellClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 1) return;
            var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
            ThemedMenuRenderer.Apply(menu);
            var cell = grid[1, e.RowIndex];
            foreach (string n in layoutNames)
            {
                string value = n;
                var item = new ToolStripMenuItem(n) { Checked = (string)cell.Value == n };
                item.Click += (s2, e2) => cell.Value = value;
                menu.Items.Add(item);
            }
            var r = grid.GetCellDisplayRectangle(1, e.RowIndex, false);
            menu.MinimumSize = new Size(r.Width, 0);
            menu.Closed += (s2, e2) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(grid, new Point(r.X, r.Bottom + 2));
        };
        refresh();

        appLayoutIds = byName;
        return page;
    }

    Dictionary<string, long> appLayoutIds;

    public static string StripExe(string s) { return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s.Substring(0, s.Length - 4) : s; }

    // "process = layout id in hex", one per line
    public static Dictionary<string, long> ParseAppLayouts(string text)
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (string s in Lines(text))
        {
            int eq = s.IndexOf('=');
            long hkl;
            if (eq > 0 && long.TryParse(s.Substring(eq + 1).Trim(), NumberStyles.HexNumber, null, out hkl))
                map[StripExe(s.Substring(0, eq).Trim())] = hkl;
        }
        return map;
    }

    // Apps with a window right now, to pick from instead of typing a process name
    static List<string> RunningApps()
    {
        var list = new List<string>();
        int own = Process.GetCurrentProcess().Id;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id != own && p.MainWindowHandle != IntPtr.Zero && !list.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)
                    && !string.Equals(p.ProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                    list.Add(p.ProcessName);
            }
            catch { }
            finally { p.Dispose(); }
        }
        // Store apps show up as ApplicationFrameHost above; their real names come from the frames
        foreach (IntPtr frame in W.TopWindows("ApplicationFrameWindow"))
        {
            string name = W.ProcessName(frame);
            if (name.Length > 0 && !string.Equals(name, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) && !list.Contains(name, StringComparer.OrdinalIgnoreCase))
                list.Add(name);
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    // ---------- Words ----------

    Panel WordsPage()
    {
        int y;
        var page = Page(L.T("Списки слов", "Word lists"), out y);
        Intro(page, ref y, L.T("Списки пополняются автоматически: отменённое или стёртое исправление добавляет слово в «Не исправлять», а слово, исправленное вручную (клавишей Pause или повторным набором в другой раскладке), — во «Всегда исправлять».",
                              "The lists are filled automatically: an undone or erased correction adds the word to \"Never correct\", and a word corrected manually (with Pause, or by retyping it in the other layout) goes to \"Always correct\"."));
        int half = (ContentWidth - 12) / 2;
        gridIgnored = WordList(page, new Point(16, y), half, L.T("Не исправлять", "Never correct"), Ignored);
        gridLearned = WordList(page, new Point(16 + half + 12, y), half, L.T("Всегда исправлять", "Always correct"), Learned);
        return page;
    }

    DataGridView WordList(Panel page, Point at, int width, string title, string words)
    {
        var card = new Card { Location = at, Size = new Size(width, content.Height - at.Y - 12) };
        var t = Theme.Label(title, Theme.Section);
        t.Location = new Point(14, 12);
        var input = Theme.TextField(L.T("Новое слово", "New word"));
        wordInputs.Add(input);

        var add = new RoundButton { Text = L.T("Добавить", "Add"), AccentStyle = true, Location = new Point(width - 14 - 100, 42), Size = new Size(100, 30) };
        var grid = Theme.Grid();
        grid.ColumnHeadersVisible = false;
        grid.Columns.Add(TextColumn("", 100));
        grid.ReadOnly = true;
        grid.Location = new Point(6, 84);
        grid.Size = new Size(width - 12, card.Height - 84 - ListBar);
        var del = ListButton(L.T("Удалить", "Delete"), 0, card.Height);
        var import = ListButton(L.T("Импорт…", "Import…"), 1, card.Height);
        card.Controls.AddRange(new Control[] { t, Theme.Framed(input, 14, 42, width - 14 - 14 - 100 - 8, 30), add, grid, del, import });   // as tall as Add
        page.Controls.Add(card);

        // Words are kept without the punctuation around them, so "солвере," already in the list is "солвере"
        Func<string, bool> addWord = word =>
        {
            string w = global::WordList.Normalize(word);
            if (w.Length == 0 || grid.Rows.Cast<DataGridViewRow>().Any(r => (string)r.Cells[0].Value == w)) return false;
            grid.Rows.Add(w);
            return true;
        };
        foreach (string w in Lines(words)) addWord(w);
        Func<bool> canAdd = () => { string w = global::WordList.Normalize(input.Text); return w.Length > 0 && w.IndexOf(' ') < 0; };
        Action refresh = () => { add.Enabled = canAdd(); del.Enabled = grid.SelectedRows.Count > 0; };
        Action doAdd = () =>
        {
            if (!canAdd()) return;
            addWord(input.Text);
            input.Text = "";
        };
        input.TextChanged += (s, e) => refresh();
        input.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { doAdd(); e.SuppressKeyPress = true; } };
        grid.SelectionChanged += (s, e) => refresh();
        add.Click += (s, e) => doAdd();
        del.Click += (s, e) => { DeleteSelected(grid); refresh(); };
        // A word list from a file: one word per line (or the first column of a table)
        import.Click += (s, e) =>
        {
            string path = PickFile(Importer.WordFilter);
            if (path == null) return;
            int added = 0, had = 0;
            foreach (string w in Importer.Words(path))
                if (addWord(w)) added++;
                else if (global::WordList.Normalize(w).Length > 0) had++;
            ImportResult(added, had);
            refresh();
        };
        DeleteWithKey(grid);
        refresh();
        return grid;
    }

    // Under every list, the same buttons in the same place: Delete, then Import
    const int ListBar = 50;
    static RoundButton ListButton(string text, int index, int cardHeight)
    {
        return new RoundButton { Text = text, Location = new Point(14 + index * 108, cardHeight - 44), Size = new Size(100, 30) };
    }

    // A list in its own card down to the bottom of the page, with Delete (and Import) under it
    Card ListCard(DataGridView grid, int y, bool withImport, out RoundButton del, out RoundButton import)
    {
        var card = new Card { Location = new Point(16, y), Size = new Size(ContentWidth, content.Height - y - 12) };
        grid.Location = new Point(6, 6);
        grid.Size = new Size(card.Width - 12, card.Height - 6 - ListBar);
        card.Controls.Add(grid);
        del = ListButton(L.T("Удалить", "Delete"), 0, card.Height);
        card.Controls.Add(del);
        import = withImport ? ListButton(L.T("Импорт…", "Import…"), 1, card.Height) : null;
        if (import != null) card.Controls.Add(import);
        return card;
    }

    string PickFile(string filter)
    {
        using (var dialog = new OpenFileDialog { Filter = filter, Title = L.T("Импорт", "Import") })
            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    void ImportResult(int added, int had)
    {
        string text = added + had == 0
            ? L.T("В файле не нашлось ничего, что можно добавить.", "Nothing to add was found in the file.")
            : L.F("Добавлено: {0}. Уже были в списке: {1}.", "Added: {0}. Already in the list: {1}.", added, had);
        ThemedMessage.Show(this, text, L.T("Импорт", "Import"), MessageBoxButtons.OK, added + had == 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    // ---------- One file with everything ----------

    Dictionary<string, int> Values()
    {
        var v = new Dictionary<string, int>
        {
            { "AutoCorrect", AutoCorrect ? 1 : 0 }, { "Typos", Typos ? 1 : 0 }, { "QuietFullscreen", QuietFullscreen ? 1 : 0 },
            { "UndoCtrlZ", UndoCtrlZ ? 1 : 0 }, { "LayoutColor", LayoutColor ? 1 : 0 }, { "CheckUpdates", CheckUpdates ? 1 : 0 },
            { "LayoutBadge", LayoutBadge ? 1 : 0 }, { "RecentMenu", RecentMenu ? 1 : 0 }, { "QuickKey", QuickKey }, { "Sound", Sound ? 1 : 0 }, { "Typography", Typography ? 1 : 0 },
            { "TrayLetters", TrayLetters ? 1 : 0 },
            { "Language", Language }, { "Theme", ThemeMode }, { "Holiday", Holiday ? 1 : 0 },
            { "Skin", SkinNumber }, { "SkinLogo", SkinLogo ? 1 : 0 }, { "SkinSound", SkinSound ? 1 : 0 },
            { "CustomAccent", CustomAccent.ToArgb() }, { "CustomFish", CustomFish.ToArgb() }
        };
        for (int i = 0; i < Hotkeys.Length; i++) v["Hotkey" + (i + 1)] = (int)Hotkeys[i];
        return v;
    }

    Dictionary<string, string> Files()
    {
        return new Dictionary<string, string>
        {
            { "replacements.txt", Replacements }, { "exclusions.txt", Exclusions }, { "ignored-words.txt", Ignored },
            { "learned-words.txt", Learned }, { "app-layouts.txt", AppLayouts },
            { "layout-colors.txt", global::LayoutColors.Format(LayoutColors) }, { "sounds.txt", Sounds.Format(SoundMap) }
        };
    }

    void ExportSettings()
    {
        Collect();
        using (var dialog = new SaveFileDialog
        {
            Filter = L.T("Настройки KeyFish", "KeyFish settings") + " (*.zip)|*.zip", FileName = "KeyFish-settings-" + DateTime.Now.ToString("yyyy-MM-dd") + ".zip",
            Title = L.T("Экспорт", "Export")
        })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { SettingsArchive.Save(dialog.FileName, Files(), Values()); }
            catch (Exception ex) { ThemedMessage.Show(this, ex.Message, L.T("Экспорт", "Export"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    // Replaces everything with the file's contents and applies it, as the Apply button does (the current state goes to
    // backups first); the window is then built again to show it
    void ImportSettings()
    {
        string path;
        using (var dialog = new OpenFileDialog { Filter = L.T("Настройки KeyFish", "KeyFish settings") + " (*.zip)|*.zip", Title = L.T("Импорт", "Import") })
        {
            if (BackupDir != null && System.IO.Directory.Exists(BackupDir)) dialog.InitialDirectory = BackupDir;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            path = dialog.FileName;
        }
        Dictionary<string, string> files;
        Dictionary<string, int> values;
        if (!SettingsArchive.Load(path, out files, out values))
        {
            ThemedMessage.Show(this, L.T("Это не файл настроек KeyFish.", "This isn't a KeyFish settings file."), L.T("Импорт", "Import"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (ThemedMessage.Show(this, L.T("Заменить все настройки содержимым файла? Нынешние сохранятся в папке backups.",
                                      "Replace all settings with the file's contents? The current ones are kept in the backups folder."),
                            L.T("Импорт", "Import"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        Collect();   // anything the file doesn't have stays as it is in this window
        string text;
        if (files.TryGetValue("replacements.txt", out text)) Replacements = text;
        if (files.TryGetValue("exclusions.txt", out text)) Exclusions = text;
        if (files.TryGetValue("ignored-words.txt", out text)) Ignored = text;
        if (files.TryGetValue("learned-words.txt", out text)) Learned = text;
        if (files.TryGetValue("app-layouts.txt", out text)) AppLayouts = text;
        if (files.TryGetValue("layout-colors.txt", out text)) LayoutColors = global::LayoutColors.Parse(text.Trim());
        if (files.TryGetValue("sounds.txt", out text)) SoundMap = Sounds.Parse(text);
        Func<string, bool, bool> flag = (name, current) => { int v; return values.TryGetValue(name, out v) ? v != 0 : current; };
        AutoCorrect = flag("AutoCorrect", AutoCorrect); Typos = flag("Typos", Typos); QuietFullscreen = flag("QuietFullscreen", QuietFullscreen);
        UndoCtrlZ = flag("UndoCtrlZ", UndoCtrlZ); LayoutColor = flag("LayoutColor", LayoutColor); CheckUpdates = flag("CheckUpdates", CheckUpdates);
        LayoutBadge = flag("LayoutBadge", LayoutBadge); RecentMenu = flag("RecentMenu", RecentMenu); Sound = flag("Sound", Sound); Typography = flag("Typography", Typography);
        TrayLetters = flag("TrayLetters", TrayLetters);
        int n;
        if (values.TryGetValue("QuickKey", out n)) QuickKey = Math.Max(0, Math.Min(n, 2));
        if (values.TryGetValue("Language", out n)) Language = Math.Max(0, n);
        if (values.TryGetValue("Theme", out n)) ThemeMode = Math.Max(0, Math.Min(n, 2));
        if (values.TryGetValue("Holiday", out n)) Holiday = n != 0;
        if (values.TryGetValue("Skin", out n)) SkinNumber = Skin.Find(n).Number;
        if (values.TryGetValue("SkinLogo", out n)) SkinLogo = n != 0;
        if (values.TryGetValue("SkinSound", out n)) SkinSound = n != 0;
        if (values.TryGetValue("CustomAccent", out n) && n != 0) CustomAccent = Color.FromArgb(255, Color.FromArgb(n));
        if (values.TryGetValue("CustomFish", out n) && n != 0) CustomFish = Color.FromArgb(255, Color.FromArgb(n));
        for (int i = 0; i < Hotkeys.Length; i++)
            if (values.TryGetValue("Hotkey" + (i + 1), out n) && (n == 0 || HotkeyBox.Allowed((Keys)n))) Hotkeys[i] = (Keys)n;
        Replacing = true;
        try { if (Apply != null) Apply(this); }
        finally { Replacing = false; }
        applied = Snapshot();
        BeginInvoke(new Action(() => Rebuild(false)));   // after the click on Import is over: the button goes with the rest
    }

    // ---------- Saving ----------

    void Collect()
    {
        AutoCorrect = tAuto.Checked;
        Typos = tTypos.Checked;
        QuietFullscreen = tQuiet.Checked;
        UndoCtrlZ = tCtrlZ.Checked;
        LayoutColor = tColor.Checked;
        TrayLetters = tLetters.Checked;
        Autostart = tAutostart.Checked;
        CheckUpdates = tUpdates.Checked;
        LayoutBadge = tBadge.Checked;
        Sound = tSound.Checked;
        SoundMap = new Dictionary<string, string>();
        for (int e = 0; e < soundBoxes.Length; e++)
            if (soundBoxes[e].SelectedIndex >= 0) SoundMap[Sounds.Events[e]] = soundIds[e][soundBoxes[e].SelectedIndex];
        RecentMenu = tRecent.Checked;
        Typography = tTypography.Checked;
        QuickKey = cbQuickKey.SelectedIndex;
        Language = cbLanguage.SelectedIndex;
        ThemeMode = cbTheme.SelectedIndex;
        Holiday = tHoliday.Checked;
        SkinNumber = gallery.Selected;
        SkinLogo = tSkinLogo.Checked;
        SkinSound = tSkinSound.Checked;
        for (int i = 0; i < Hotkeys.Length; i++) Hotkeys[i] = hotkeyBoxes[i].Value;

        gridApps.EndEdit();
        var sb = new StringBuilder(L.T("# Автозамены KeyFish: сокращение = замена (\\n — перенос строки). Удобнее править в настройках.\r\n",
                                       "# KeyFish replacements: abbreviation = text (\\n is a line break). Easier to edit in Settings.\r\n"));
        foreach (DataGridViewRow r in gridReplacements.Rows)
            sb.Append((string)r.Cells[0].Value).Append(" = ").Append(ReplacementFile.Escape((string)r.Tag)).Append("\r\n");
        Replacements = sb.ToString();

        var ex = new StringBuilder(L.T("# Программы, где автоисправление выключено (имя процесса). Удобнее править в настройках.\r\n",
                                       "# Apps where auto-correction is off (process name). Easier to edit in Settings.\r\n"));
        var lay = new StringBuilder(L.T("# Раскладка для программы: процесс = код раскладки. Удобнее править в настройках.\r\n",
                                        "# Layout per app: process = layout id. Easier to edit in Settings.\r\n"));
        foreach (DataGridViewRow r in gridApps.Rows)
        {
            string n = (string)r.Cells[0].Value;
            if (!(r.Cells[2].Value is bool && (bool)r.Cells[2].Value)) ex.Append(n).Append("\r\n");
            long hkl;
            if (r.Cells[1].Value is string && appLayoutIds.TryGetValue((string)r.Cells[1].Value, out hkl))
                lay.Append(n).Append(" = ").Append(hkl.ToString("X")).Append("\r\n");
        }
        Exclusions = ex.ToString();
        AppLayouts = lay.ToString();
        Ignored = string.Join("\r\n", gridIgnored.Rows.Cast<DataGridViewRow>().Select(r => (string)r.Cells[0].Value)) + "\r\n";
        Learned = string.Join("\r\n", gridLearned.Rows.Cast<DataGridViewRow>().Select(r => (string)r.Cells[0].Value)) + "\r\n";
    }

    public static string Off { get { return L.T("выключено", "off"); } }
    public static string NeedModifier { get { return L.T("требуется Ctrl или Alt", "Ctrl or Alt required"); } }

    public static string Format(Keys k)
    {
        if (k == Keys.None) return Off;
        var parts = new List<string>();
        if ((k & Keys.Control) != 0) parts.Add("Ctrl");
        if ((k & Keys.Alt) != 0) parts.Add("Alt");
        if ((k & Keys.Shift) != 0) parts.Add("Shift");
        Keys code = k & Keys.KeyCode;
        string name;
        switch (code)
        {
            case Keys.Pause: name = "Pause"; break;
            case Keys.Space: name = "Space"; break;
            case Keys.Return: name = "Enter"; break;
            case Keys.Back: name = "Backspace"; break;
            case Keys.Oemtilde: name = "`"; break;
            default:
                name = code >= Keys.D0 && code <= Keys.D9 ? ((char)('0' + (code - Keys.D0))).ToString() : code.ToString();
                break;
        }
        parts.Add(name);
        return string.Join("+", parts);
    }
}

// A field that records the pressed key combination. Clicking it starts recording: the field lights up
// and asks for the keys; Esc cancels, Backspace turns the hotkey off
class HotkeyBox : TextBox
{
    Keys value;
    bool recording;
    public event EventHandler ValueChanged;

    // A field starts or stops listening for keys: KeyFish's own hotkeys are off meanwhile, or they couldn't be assigned
    public static event Action<bool> RecordingChanged;

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool HideCaret(IntPtr hwnd);

    public HotkeyBox()
    {
        ReadOnly = true;
        BorderStyle = BorderStyle.None;   // the rounded frame around it is a FieldFrame
        BackColor = Theme.Input;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        TextAlign = HorizontalAlignment.Center;
        ShortcutsEnabled = false;
        Cursor = Cursors.Hand;
    }

    public static string Prompt { get { return L.T("Нажмите сочетание…", "Press the keys…"); } }

    public Keys Value
    {
        get { return value; }
        set { this.value = value; Show(SettingsForm.Format(value)); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
    }

    // Text without a caret or a blue selection: the field is a button that listens, not a text box
    void Show(string text)
    {
        Text = text;
        SelectionLength = 0;
        if (IsHandleCreated) HideCaret(Handle);
    }

    void Record(bool on)
    {
        if (recording != on && RecordingChanged != null) RecordingChanged(on);
        recording = on;
        BackColor = on ? Theme.SelectionBack : Theme.Input;
        ForeColor = on ? Theme.OnSelected : Theme.Text;
        Font = on ? Theme.Strong : Theme.Body;
        Show(on ? Prompt : SettingsForm.Format(value));
    }

    // Esc while recording: keep the old hotkey
    public bool Cancel()
    {
        if (!recording) return false;
        Record(false);
        var form = FindForm();
        if (form != null) form.ActiveControl = null;
        return true;
    }

    protected override void OnEnter(EventArgs e) { Record(true); base.OnEnter(e); }
    protected override void OnLeave(EventArgs e) { Record(false); base.OnLeave(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (!recording) Record(true); else Show(Text); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); BeginInvoke(new Action(() => Show(Text))); }

    protected override bool IsInputKey(Keys keyData) { return true; }

    static string Modifiers(Keys m)
    {
        var parts = new List<string>();
        if ((m & Keys.Control) != 0) parts.Add("Ctrl");
        if ((m & Keys.Alt) != 0) parts.Add("Alt");
        if ((m & Keys.Shift) != 0) parts.Add("Shift");
        return string.Join("+", parts);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (!recording) Record(true);
        Keys code = e.KeyCode;
        if (code == Keys.ShiftKey || code == Keys.ControlKey || code == Keys.Menu || code == Keys.LWin || code == Keys.RWin)
        {
            Show(Modifiers(e.Modifiers) + "+…");
            return;
        }
        if ((code == Keys.Back || code == Keys.Delete) && e.Modifiers == Keys.None) { Finish(Keys.None); return; }
        if (!Allowed(code | e.Modifiers))
        {
            Show(SettingsForm.NeedModifier);
            return;
        }
        Finish(code | e.Modifiers);
    }

    // A hotkey is taken from every app, so without Ctrl or Alt only keys that type nothing and move nothing can be one
    // (Pause, Scroll Lock, F1–F24): Enter, Tab, letters, punctuation or arrows would stop working everywhere else
    public static bool Allowed(Keys k)
    {
        Keys code = k & Keys.KeyCode;
        if (code == Keys.None || code == Keys.ShiftKey || code == Keys.ControlKey || code == Keys.Menu) return false;
        if ((k & (Keys.Control | Keys.Alt)) != 0) return true;
        return code == Keys.Pause || code == Keys.Scroll || (code >= Keys.F1 && code <= Keys.F24);
    }

    // A hotkey is set: show it and stop listening, so the next keys don't change it by accident
    void Finish(Keys k)
    {
        value = k;
        Record(false);
        if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        var form = FindForm();
        if (form != null) form.ActiveControl = null;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        // Modifiers released without a key, or a refused key: ask again
        if (recording && (Text.EndsWith("…") || Text == SettingsForm.NeedModifier) && e.Modifiers == Keys.None) Show(Prompt);
        base.OnKeyUp(e);
    }
}

// Import of replacements and word lists from other programs' files. Understands the common plain formats:
// "abbreviation = text", a table (tab, comma or semicolon between the columns), AutoHotkey hotstrings
// (::btw::by the way) and Espanso matches (trigger / replace). Anything else in the file is skipped
static class Importer
{
    public static string ReplacementFilter
    {
        get
        {
            return L.T("Автозамены (txt, csv, AutoHotkey, Espanso)", "Replacements (txt, csv, AutoHotkey, Espanso)") +
                   "|*.txt;*.csv;*.tsv;*.ahk;*.yml;*.yaml|" + L.T("Все файлы", "All files") + "|*.*";
        }
    }

    public static string WordFilter
    {
        get { return L.T("Списки слов (txt, csv)", "Word lists (txt, csv)") + "|*.txt;*.csv;*.tsv;*.dic|" + L.T("Все файлы", "All files") + "|*.*"; }
    }

    public static List<KeyValuePair<string, string>> Replacements(string path)
    {
        var list = new List<KeyValuePair<string, string>>();
        string[] lines = ReadLines(path);
        if (lines == null) return list;
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".yml" || ext == ".yaml" || lines.Any(l => l.TrimStart().StartsWith("- trigger:")))
        {
            Espanso(lines, list);
            return list;
        }
        var hotstring = new System.Text.RegularExpressions.Regex(@"^:[^:]*:(.+?)::(.+)$");
        foreach (string raw in lines)
        {
            string s = raw.Trim();
            if (s.Length == 0 || s.StartsWith("#") || s.StartsWith(";") || s.StartsWith("//")) continue;
            var m = hotstring.Match(s);
            if (m.Success) { Add(list, m.Groups[1].Value, AutoHotkeyText(m.Groups[2].Value)); continue; }
            string[] cells = Cells(s, ext == ".csv");
            if (cells != null) { Add(list, cells[0], cells[1].Replace("\\n", "\n")); continue; }
            foreach (var kv in ReplacementFile.Parse(s)) Add(list, kv.Key, kv.Value);
        }
        return list;
    }

    public static List<string> Words(string path)
    {
        var list = new List<string>();
        string[] lines = ReadLines(path);
        if (lines == null) return list;
        foreach (string raw in lines)
        {
            string s = raw.Trim();
            if (s.Length == 0 || s.StartsWith("#") || s.StartsWith(";") || s.StartsWith("//")) continue;
            int cut = s.IndexOfAny(new[] { '\t', ',', ';', '/' });   // a table's first column; "word/flags" in spell-checker lists
            if (cut >= 0) s = s.Substring(0, cut);
            s = s.Trim().Trim('"').Trim();
            if (s.Length > 0 && s.Length <= 40 && s.IndexOf(' ') < 0 && s.Any(char.IsLetter) && !list.Contains(s, StringComparer.OrdinalIgnoreCase))
                list.Add(s);
        }
        return list;
    }

    static void Add(List<KeyValuePair<string, string>> list, string key, string value)
    {
        key = key.Trim();
        value = value.Trim();
        // The same rules as the editor: one word without "=", not starting with "#", and some text to put in
        if (key.Length == 0 || key.Length > 40 || key.IndexOfAny(new[] { ' ', '=', '\t' }) >= 0 || key.StartsWith("#") || value.Length == 0) return;
        if (list.Any(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))) return;
        list.Add(new KeyValuePair<string, string>(key, value));
    }

    // Two cells from a table row: tab-separated, or comma/semicolon-separated with "quoted" cells. null if it isn't one
    static string[] Cells(string s, bool csv)
    {
        int tab = s.IndexOf('\t');
        if (tab > 0) return new[] { s.Substring(0, tab), Unquote(s.Substring(tab + 1).Split('\t')[0]) };
        if (!csv) return null;
        var cells = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < s.Length && s[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',' || c == ';') { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString());
        return cells.Count >= 2 ? new[] { cells[0], cells[1] } : null;
    }

    static string Unquote(string s)
    {
        s = s.Trim();
        return s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"' ? s.Substring(1, s.Length - 2).Replace("\"\"", "\"") : s;
    }

    // AutoHotkey escapes: `n a line break, `t a tab, `` a backtick
    static string AutoHotkeyText(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '`' && i + 1 < s.Length)
            {
                char n = s[++i];
                sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }

    // Espanso: "- trigger: ..." followed by "replace: ..." (one-line values; forms and variables are skipped)
    static void Espanso(string[] lines, List<KeyValuePair<string, string>> list)
    {
        string trigger = null;
        foreach (string raw in lines)
        {
            string s = raw.Trim();
            if (s.StartsWith("- ")) { trigger = null; s = s.Substring(2).Trim(); }
            if (s.StartsWith("trigger:")) trigger = YamlValue(s.Substring(8));
            else if (s.StartsWith("replace:") && trigger != null)
            {
                string value = YamlValue(s.Substring(8));
                if (value != null && value != "|" && value != ">" && value.IndexOf("{{", StringComparison.Ordinal) < 0) Add(list, trigger, value);
                trigger = null;
            }
        }
    }

    static string YamlValue(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'') return s.Substring(1, s.Length - 2).Replace("''", "'");
        if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
        {
            var sb = new StringBuilder();
            string inner = s.Substring(1, s.Length - 2);
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '\\' && i + 1 < inner.Length)
                {
                    char n = inner[++i];
                    sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
                }
                else sb.Append(inner[i]);
            }
            return sb.ToString();
        }
        return s;
    }

    // UTF-8 or UTF-16 (with a BOM), otherwise UTF-8 if it is valid, otherwise the old Cyrillic Windows encoding
    static string[] ReadLines(string path)
    {
        byte[] bytes;
        try { bytes = System.IO.File.ReadAllBytes(path); }
        catch { return null; }
        if (bytes.Length > 5 * 1024 * 1024) return null;   // not a list of replacements
        string text;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        else
        {
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            try { text = new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start); }
            catch (DecoderFallbackException) { text = Encoding.GetEncoding(1251).GetString(bytes); }
        }
        return text.Replace("\r\n", "\n").Split('\n');
    }
}

// KeyFish settings in one zip file: the text files as they are, and the switches and hotkeys in settings.txt (name=number)
static class SettingsArchive
{
    public static readonly string[] Values =
    {
        "AutoCorrect", "Typos", "QuietFullscreen", "UndoCtrlZ", "LayoutColor", "CheckUpdates", "LayoutBadge", "RecentMenu", "QuickKey", "Sound", "TrayLetters", "Typography",
        "Language", "Theme", "Holiday", "Skin", "SkinLogo", "SkinSound", "CustomAccent", "CustomFish", "Hotkey1", "Hotkey2", "Hotkey3", "Hotkey4", "Hotkey5", "Hotkey6", "Hotkey7", "Hotkey8"
    };
    static readonly string[] FileNames = { "replacements.txt", "exclusions.txt", "ignored-words.txt", "learned-words.txt", "app-layouts.txt", "layout-colors.txt", "sounds.txt" };

    public static void Save(string path, Dictionary<string, string> files, Dictionary<string, int> values)
    {
        using (var zip = new System.IO.Compression.ZipArchive(System.IO.File.Create(path), System.IO.Compression.ZipArchiveMode.Create))
        {
            Write(zip, "keyfish.txt", "KeyFish settings, version " + Application.ProductVersion + "\r\n");
            foreach (var kv in files) if (kv.Value != null && Array.IndexOf(FileNames, kv.Key) >= 0) Write(zip, kv.Key, kv.Value);
            var sb = new StringBuilder();
            foreach (var kv in values) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
            Write(zip, "settings.txt", sb.ToString());
        }
    }

    // Only the known names are read; anything else in the file is ignored
    public static bool Load(string path, out Dictionary<string, string> files, out Dictionary<string, int> values)
    {
        files = new Dictionary<string, string>();
        values = new Dictionary<string, int>();
        try
        {
            using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
            {
                if (zip.GetEntry("keyfish.txt") == null) return false;
                foreach (string name in FileNames)
                {
                    var entry = zip.GetEntry(name);
                    if (entry != null && entry.Length < 5 * 1024 * 1024) files[name] = Read(entry);
                }
                var settings = zip.GetEntry("settings.txt");
                if (settings != null)
                    foreach (string line in Read(settings).Split('\n'))
                    {
                        int eq = line.IndexOf('=');
                        int v;
                        if (eq > 0 && Array.IndexOf(Values, line.Substring(0, eq).Trim()) >= 0 && int.TryParse(line.Substring(eq + 1).Trim(), out v))
                            values[line.Substring(0, eq).Trim()] = v;
                    }
            }
            return true;
        }
        catch { return false; }
    }

    static void Write(System.IO.Compression.ZipArchive zip, string name, string text)
    {
        using (var w = new System.IO.StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false))) w.Write(text);
    }

    // Reads at most 5 MB of text: a zip's stated size can lie, and the real data could be gigabytes
    static string Read(System.IO.Compression.ZipArchiveEntry entry)
    {
        const int limit = 5 * 1024 * 1024;
        var sb = new StringBuilder();
        var buffer = new char[64 * 1024];
        using (var r = new System.IO.StreamReader(entry.Open(), Encoding.UTF8))
            for (int n; (n = r.Read(buffer, 0, buffer.Length)) > 0; )
            {
                if (sb.Length + n > limit) throw new System.IO.InvalidDataException("too large");
                sb.Append(buffer, 0, n);
            }
        return sb.ToString();
    }
}

// The statistics in Settings as a row of numbers with a caption under each, split by thin lines, under a line that
// parts them from the total above
class StatsStrip : Control
{
    readonly Tuple<string, string>[] items;

    public StatsStrip(Tuple<string, string>[] items)
    {
        this.items = items;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = string.Join(", ", items.Select(i => i.Item1 + " " + i.Item2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        float side = Theme.S(12), top = Theme.S(6), h = Height - top;
        using (var p = new Pen(Theme.CardBorder)) g.DrawLine(p, side, 0, Width - side, 0);
        float w = (float)Width / items.Length, valueH = h * 0.56f;
        for (int i = 0; i < items.Length; i++)
        {
            float x = i * w;
            if (i > 0)
                using (var p = new Pen(Theme.CardBorder)) g.DrawLine(p, x, top + h * 0.15f, x, top + h * 0.85f);
            Theme.SmoothText(g, items[i].Item1, Theme.Section, new RectangleF(x, top, w, valueH), Theme.Text, true);
            Theme.SmoothText(g, items[i].Item2, Theme.Small, new RectangleF(x, top + valueH - 2, w, h - valueH), Theme.SubText, true, false);
        }
    }
}

// Mistakes caught per day over the last 30 days, today on the right. A day before counting by day began has no bar;
// hovering a bar tells its date and number
class StatsChart : Control
{
    const int Days = 30;
    readonly int[] counts = new int[Days];   // -1: not counted yet
    readonly DateTime[] dates = new DateTime[Days];
    readonly ToolTip tip = new ToolTip();
    int hover = -1;

    public StatsChart(Dictionary<int, int> days, int since)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        for (int i = 0; i < Days; i++)
        {
            var d = DateTime.Today.AddDays(i - (Days - 1));
            int key = d.Year * 10000 + d.Month * 100 + d.Day, n;
            dates[i] = d;
            counts[i] = key < since ? -1 : days != null && days.TryGetValue(key, out n) ? n : 0;
        }
        AccessibleRole = AccessibleRole.Chart;
        AccessibleName = L.T("Исправления за последние 30 дней", "Corrections over the last 30 days");
    }

    string Describe(int i)
    {
        return dates[i].ToString("M", L.Culture) + ": " + (counts[i] < 0 ? L.T("нет данных", "no data") : counts[i].ToString("N0", L.Culture));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float labels = Theme.Small.GetHeight(g) + Theme.S(4);
        float area = Height - labels, slot = (float)Width / Days, bar = slot * 0.62f;
        int max = Math.Max(1, counts.Max());
        using (var line = new Pen(Theme.CardBorder)) g.DrawLine(line, 0, area - 0.5f, Width, area - 0.5f);
        for (int i = 0; i < Days; i++)
        {
            if (counts[i] <= 0) continue;
            float h = Math.Max(Theme.S(3), (area - Theme.S(4)) * counts[i] / max);
            var r = new RectangleF(i * slot + (slot - bar) / 2, area - h, bar, h);
            using (var path = Theme.Rounded(r, Math.Min(bar / 2, Theme.S(3))))
            using (var b = new SolidBrush(Color.FromArgb(i == hover ? 255 : 200, Theme.AccentFill)))
                g.FillPath(b, path);
        }
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        // Under the bars: the first day, the middle one and today
        var row = new RectangleF(0, area + Theme.S(2), Width, labels);
        Theme.SmoothText(g, dates[0].ToString("M", L.Culture), Theme.Small, row, Theme.SubText, false, false);
        Theme.SmoothText(g, dates[Days / 2].ToString("M", L.Culture), Theme.Small, row, Theme.SubText, true, false);
        string today = L.T("сегодня", "today");
        float w = g.MeasureString(today, Theme.Small).Width + 4;
        Theme.SmoothText(g, today, Theme.Small, new RectangleF(Width - w, row.Y, w, row.Height), Theme.SubText, false, false);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = Math.Max(0, Math.Min(Days - 1, (int)(e.X / ((float)Width / Days))));
        if (i != hover)
        {
            hover = i;
            tip.Show(Describe(i), this, e.X + 12, e.Y + 16);
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = -1;
        tip.Hide(this);
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tip.Dispose();
        base.Dispose(disposing);
    }
}
