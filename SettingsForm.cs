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
    public bool AutoCorrect, Typos, QuietFullscreen, UndoCtrlZ, LayoutColor, Autostart, CheckUpdates, LayoutBadge, RecentMenu;
    public int QuickKey;                 // 0 — off, 1 — double Shift, 2 — Caps Lock
    public int Language;                 // see L.Choices
    public Keys[] Hotkeys;
    public string Replacements, Exclusions, Ignored, Learned, AppLayouts;
    public IList<LayoutTable> InstalledLayouts = new List<LayoutTable>();
    public Image Logo;
    public int StartPage;                // the section to open with

    ToggleSwitch tAuto, tTypos, tQuiet, tCtrlZ, tColor, tAutostart, tUpdates, tBadge, tRecent;
    DropDown cbLanguage, cbQuickKey;
    HotkeyBox[] hotkeyBoxes;
    DataGridView gridReplacements, gridApps, gridIgnored, gridLearned;

    const int SideWidth = 250, ContentWidth = 640;
    readonly Panel content = new Panel();
    readonly List<Panel> pages = new List<Panel>();

    public SettingsForm()
    {
        Text = L.T("Настройки KeyFish", "KeyFish settings");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ShowInTaskbar = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Theme.Apply(this);
        ClientSize = new Size(SideWidth + ContentWidth + 40, 660);
    }

    protected override void OnLoad(EventArgs e)
    {
        SuspendLayout();
        // Sidebar: the fish, the name and the sections
        var side = new Panel { Location = new Point(0, 0), Size = new Size(SideWidth, ClientSize.Height), BackColor = Theme.Back };
        if (Logo != null) side.Controls.Add(new PictureBox { Image = Logo, SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(20, 18), Size = new Size(36, 36), BackColor = Color.Transparent });
        var name = Theme.Label("KeyFish", Theme.Section);
        name.Location = new Point(64, 15);
        var ver = Theme.Label(Application.ProductVersion, Theme.Small, Theme.SubText);
        ver.Location = new Point(65, 39);
        side.Controls.Add(ver);
        side.Controls.Add(name);

        var nav = new NavList { Location = new Point(8, 74), Size = new Size(SideWidth - 16, 6 * 40) };
        nav.Items.Add(Tuple.Create("\uE713", L.T("Основное", "General")));
        nav.Items.Add(Tuple.Create("\uE765", L.T("Сочетания клавиш", "Hotkeys")));
        nav.Items.Add(Tuple.Create("\uE8AC", L.T("Автозамены", "Replacements")));
        nav.Items.Add(Tuple.Create("\uE71D", L.T("Программы", "Apps")));
        nav.Items.Add(Tuple.Create("\uE82D", L.T("Слова", "Words")));
        side.Controls.Add(nav);
        Controls.Add(side);

        content.Location = new Point(SideWidth, 0);
        content.Size = new Size(ClientSize.Width - SideWidth, ClientSize.Height - 64);
        content.BackColor = Theme.Back;
        Controls.Add(content);

        pages.Add(GeneralPage());
        pages.Add(HotkeysPage());
        pages.Add(ReplacementsPage());
        pages.Add(AppsPage());
        pages.Add(WordsPage());
        foreach (var p in pages) { p.Visible = false; content.Controls.Add(p); }
        nav.Selected = Math.Max(0, Math.Min(StartPage, pages.Count - 1));
        pages[nav.Selected].Visible = true;
        nav.SelectedChanged += (s, ev) =>
        {
            for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == nav.Selected;
        };

        var save = new RoundButton { Text = L.T("Сохранить", "Save"), AccentStyle = true, Size = new Size(130, 34) };
        var cancel = new RoundButton { Text = L.T("Отмена", "Cancel"), Size = new Size(130, 34) };
        save.Location = new Point(ClientSize.Width - 28 - 130 - 10 - 130, ClientSize.Height - 50);
        cancel.Location = new Point(ClientSize.Width - 28 - 130, ClientSize.Height - 50);
        save.Click += (s, ev) => { Collect(); DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (s, ev) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(save);
        Controls.Add(cancel);
        ResumeLayout();
        base.OnLoad(e);
    }

    // Esc closes without saving, unless a field is being edited in a table
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && FocusedHotkey() != null && FocusedHotkey().Cancel()) return true;
        if (keyData == Keys.Escape && !(ActiveControl is DataGridView && ((DataGridView)ActiveControl).IsCurrentCellInEditMode))
        {
            DialogResult = DialogResult.Cancel;
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
    static void Row(Control page, ref int y, string title, string description, Control right)
    {
        const int pad = 18;
        int textWidth = ContentWidth - pad * 3 - right.Width;
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
        card.Height = Math.Max(textHeight + 28, 64);
        t.Location = new Point(pad, (card.Height - textHeight) / 2);
        if (d != null) d.Location = new Point(pad, t.Bottom + 1);
        right.Location = new Point(ContentWidth - pad - right.Width, (card.Height - right.Height) / 2);
        card.Controls.Add(right);
        page.Controls.Add(card);
        y += card.Height + 4;
    }

    static ToggleSwitch Toggle(bool value) { return new ToggleSwitch { Checked = value }; }

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

    // ---------- General ----------

    Panel GeneralPage()
    {
        int y;
        var page = Page(L.T("Основное", "General"), out y);
        Row(page, ref y, L.T("Исправлять раскладку при наборе", "Fix the keyboard layout as you type"),
            L.T("ghbdtn → привет, руддщ → hello. Автозамены тоже работают, только когда это включено.",
                "ghbdtn → привет, руддщ → hello. Replacements also work only while this is on."), tAuto = Toggle(AutoCorrect));
        Row(page, ref y, L.T("Исправлять опечатки", "Fix typos"),
            L.T("Эксперимент: пирвет → привет, через проверку орфографии Windows.", "Experimental: recieve → receive, using Windows spell check."), tTypos = Toggle(Typos));
        Row(page, ref y, L.T("Молчать в играх и полноэкранных программах", "Stay quiet in games and full-screen apps"), null, tQuiet = Toggle(QuietFullscreen));
        Row(page, ref y, L.T("Ctrl+Z отменяет автоисправление", "Ctrl+Z undoes an auto-fix"),
            L.T("Сразу после исправления, как и Pause. Следующий Ctrl+Z работает в программе как обычно.",
                "Right after the fix, like Pause. The next Ctrl+Z works in the app as usual."), tCtrlZ = Toggle(UndoCtrlZ));
        Row(page, ref y, L.T("Цвет рыбки показывает раскладку", "Fish color shows the layout"),
            L.T("Синяя — латиница, оранжевая — другой алфавит.", "Blue — Latin, orange — another alphabet."), tColor = Toggle(LayoutColor));
        Row(page, ref y, L.T("Показывать раскладку у курсора", "Show the layout at the cursor"),
            L.T("Когда раскладка меняется, рядом с текстовым курсором на секунду появляется EN, RU и т. д. Если программа не сообщает, где курсор, — рядом с мышью.",
                "When the layout changes, EN, RU and so on appear next to the text cursor for a second, or next to the mouse pointer if the app doesn't tell where its cursor is."), tBadge = Toggle(LayoutBadge));
        Row(page, ref y, L.T("Последние исправления в меню рыбки", "Recent corrections in the fish menu"),
            L.T("Нажмите на исправление в меню, и KeyFish больше не будет так исправлять. Список хранится только в памяти.",
                "Click a correction in the menu and KeyFish won't correct it again. The list is kept only in memory."), tRecent = Toggle(RecentMenu));
        Row(page, ref y, L.T("Запускать вместе с Windows", "Start with Windows"), null, tAutostart = Toggle(Autostart));
        Row(page, ref y, L.T("Проверять обновления", "Check for updates"),
            L.T("Раз в день KeyFish спрашивает у GitHub, вышла ли новая версия. Больше ничего не отправляется.",
                "Once a day KeyFish asks GitHub whether a new version is out. Nothing else is sent."), tUpdates = Toggle(CheckUpdates));

        cbLanguage = new DropDown { Width = 220 };
        cbLanguage.Items.AddRange(L.Choices);
        cbLanguage.SelectedIndex = Math.Max(0, Math.Min(Language, cbLanguage.Items.Count - 1));
        Row(page, ref y, L.T("Язык интерфейса", "Interface language"), null, cbLanguage);

        var support = new RoundButton { Text = L.T("Поддержать ☕", "Support ☕"), Size = new Size(150, 32) };
        support.Click += (s, e) => { try { Process.Start("https://ko-fi.com/pravken"); } catch { } };
        Row(page, ref y, L.T("KeyFish бесплатный", "KeyFish is free"),
            L.T("Если он экономит вам время, можно угостить автора кофе.", "If it saves you time, you can buy the author a coffee."), support);
        page.Controls.Add(new Panel { Location = new Point(0, y), Size = new Size(1, 16) });   // bottom margin when scrolled
        return page;
    }

    // ---------- Hotkeys ----------

    Panel HotkeysPage()
    {
        int y;
        var page = Page(L.T("Сочетания клавиш", "Hotkeys"), out y);
        Intro(page, ref y, L.T("Щёлкните поле и нажмите новое сочетание. Esc — отмена, Backspace — выключить сочетание.",
                              "Click a field and press the new keys. Esc cancels, Backspace turns a hotkey off."));
        hotkeyBoxes = new HotkeyBox[Hotkeys.Length];
        for (int i = 0; i < Hotkeys.Length; i++)
        {
            var box = new HotkeyBox { Location = new Point(0, 2), Size = new Size(170, 28), Value = Hotkeys[i] };
            int index = i;
            var reset = new RoundButton { Text = L.T("Сброс", "Reset"), Location = new Point(178, 0), Size = new Size(82, 32) };
            reset.Click += (s, e) => hotkeyBoxes[index].Value = App.HotkeyDefaults[index];
            var holder = new Panel { Size = new Size(260, 32), BackColor = Color.Transparent };
            holder.Controls.Add(box);
            holder.Controls.Add(reset);
            hotkeyBoxes[i] = box;
            Row(page, ref y, App.HotkeyTitles[i], null, holder);
        }

        cbQuickKey = new DropDown { Width = 260 };
        cbQuickKey.Items.AddRange(new[] { L.T("Выключено", "Off"), L.T("Двойной Shift", "Double Shift"), "Caps Lock" });
        cbQuickKey.SelectedIndex = Math.Max(0, Math.Min(QuickKey, 2));
        Row(page, ref y, L.T("Исправить последнее слово одной клавишей", "Fix the last word with one key"),
            L.T("Делает то же, что первое сочетание. Caps Lock вместе с Shift по-прежнему включает заглавные буквы.",
                "Does the same as the first hotkey. Shift+Caps Lock still turns capital letters on and off."), cbQuickKey);
        return page;
    }

    // ---------- Replacements ----------

    Panel ReplacementsPage()
    {
        int y;
        var page = Page(L.T("Автозамены", "Replacements"), out y);
        Intro(page, ref y, L.T("Наберите сокращение и пробел — KeyFish заменит его на текст. Сокращение срабатывает и в другой раскладке: «спс» заменится, даже если набрано «cgc».",
                              "Type an abbreviation and a space, and KeyFish replaces it with the text. It works in the other layout too: \"thx\" expands even when typed as «ерч»."));

        // The editor: abbreviation → text, then Add (or Save when the abbreviation is already in the table)
        var editor = new Card { Location = new Point(16, y), Size = new Size(ContentWidth, 132) };
        var lKey = Theme.Label(L.T("Сокращение", "Abbreviation"), Theme.Small, Theme.SubText); lKey.Location = new Point(16, 10);
        var lVal = Theme.Label(L.T("Замена (Enter — новая строка)", "Text (Enter — new line)"), Theme.Small, Theme.SubText); lVal.Location = new Point(206, 10);
        var key = Theme.TextField(L.T("спс", "thx"));
        key.Location = new Point(16, 32); key.Width = 160;
        var arrow = Theme.Label("→", Theme.Section, Theme.SubText); arrow.Location = new Point(180, 32);
        var val = Theme.TextField();
        val.Multiline = true; val.AcceptsReturn = true;
        val.Location = new Point(206, 32); val.Size = new Size(270, 84);
        Theme.DarkScrollbars(val);
        var add = new RoundButton { Text = L.T("Добавить", "Add"), AccentStyle = true, Location = new Point(490, 14), Size = new Size(134, 32) };
        var del = new RoundButton { Text = L.T("Удалить", "Delete"), Location = new Point(490, 50), Size = new Size(134, 32) };
        var import = new RoundButton { Text = L.T("Импорт…", "Import…"), Location = new Point(490, 86), Size = new Size(134, 32) };
        editor.Controls.AddRange(new Control[] { lKey, lVal, key, arrow, val, add, del, import });
        page.Controls.Add(editor);
        y += editor.Height + 10;

        var grid = gridReplacements = Theme.Grid();
        grid.Columns.Add(TextColumn(L.T("Сокращение", "Abbreviation"), 30));
        grid.Columns.Add(TextColumn(L.T("Замена", "Text"), 70));
        grid.ReadOnly = true;
        var holder = new Card { Location = new Point(16, y), Size = new Size(ContentWidth, content.Height - y - 12) };
        grid.Location = new Point(6, 6);
        grid.Size = new Size(holder.Width - 12, holder.Height - 12);
        holder.Controls.Add(grid);
        page.Controls.Add(holder);

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
        Intro(page, ref y, L.T("Выберите раскладку для программы — KeyFish включит её, когда вы переключитесь в окно этой программы. Здесь же можно выключить автоисправление, например в редакторах кода и терминалах.",
                              "Pick a layout for an app, and KeyFish switches to it when you switch to that app's window. You can also turn auto-correction off here, for example in code editors and terminals."));

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
        var app = new DropDown(true, L.T("Выберите или введите", "Pick or type"));
        app.Location = new Point(16, 30); app.Width = 204;
        app.Items.AddRange(RunningApps());
        var lay = new DropDown();
        lay.Location = new Point(236, 30); lay.Width = 172;
        lay.Items.AddRange(layoutNames);
        lay.SelectedIndex = 0;
        var fix = new ToggleSwitch { Checked = true, Location = new Point(428, 35) };
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
        var holder = new Card { Location = new Point(16, y), Size = new Size(ContentWidth, content.Height - y - 56) };
        grid.Location = new Point(6, 6);
        grid.Size = new Size(holder.Width - 12, holder.Height - 12);
        holder.Controls.Add(grid);
        page.Controls.Add(holder);
        y += holder.Height + 8;
        var del = new RoundButton { Text = L.T("Удалить", "Delete"), Location = new Point(16, y), Size = new Size(134, 32) };
        page.Controls.Add(del);

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

    static string StripExe(string s) { return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s.Substring(0, s.Length - 4) : s; }

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
                if (p.Id != own && p.MainWindowHandle != IntPtr.Zero && !list.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase))
                    list.Add(p.ProcessName);
            }
            catch { }
            finally { p.Dispose(); }
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    // ---------- Words ----------

    Panel WordsPage()
    {
        int y;
        var page = Page(L.T("Слова", "Words"), out y);
        Intro(page, ref y, L.T("KeyFish учится на ваших действиях: если отменить или стереть исправление, слово попадёт в «Не исправлять»; если исправить слово самому через Pause — в «Всегда исправлять».",
                              "KeyFish learns from you: undo or erase a correction and the word goes to \"Never correct\"; fix a word yourself with Pause and it goes to \"Always correct\"."));
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
        input.Location = new Point(14, 44); input.Width = width - 14 - 14 - 100 - 8;
        var add = new RoundButton { Text = L.T("Добавить", "Add"), AccentStyle = true, Location = new Point(width - 14 - 100, 42), Size = new Size(100, 30) };
        var grid = Theme.Grid();
        grid.ColumnHeadersVisible = false;
        grid.Columns.Add(TextColumn("", 100));
        grid.ReadOnly = true;
        grid.Location = new Point(6, 84);
        grid.Size = new Size(width - 12, card.Height - 84 - 50);
        var del = new RoundButton { Text = L.T("Удалить", "Delete"), Location = new Point(14, card.Height - 44), Size = new Size(100, 30) };
        var import = new RoundButton { Text = L.T("Импорт…", "Import…"), Location = new Point(122, card.Height - 44), Size = new Size(100, 30) };
        card.Controls.AddRange(new Control[] { t, input, add, grid, del, import });
        page.Controls.Add(card);

        foreach (string w in Lines(words)) grid.Rows.Add(w);
        Action refresh = () => { add.Enabled = input.Text.Trim().Length > 0 && input.Text.Trim().IndexOf(' ') < 0; del.Enabled = grid.SelectedRows.Count > 0; };
        Action doAdd = () =>
        {
            string w = input.Text.Trim();
            if (w.Length == 0 || w.IndexOf(' ') >= 0) return;
            if (!grid.Rows.Cast<DataGridViewRow>().Any(r => string.Equals((string)r.Cells[0].Value, w, StringComparison.OrdinalIgnoreCase)))
                grid.Rows.Add(w);
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
            {
                if (grid.Rows.Cast<DataGridViewRow>().Any(r => string.Equals((string)r.Cells[0].Value, w, StringComparison.OrdinalIgnoreCase))) { had++; continue; }
                grid.Rows.Add(w);
                added++;
            }
            ImportResult(added, had);
            refresh();
        };
        DeleteWithKey(grid);
        refresh();
        return grid;
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
            : L.F("Добавлено: {0}. Уже были в списке: {1}. Изменения сохранятся по кнопке «Сохранить».",
                  "Added: {0}. Already in the list: {1}. Click Save to keep the changes.", added, had);
        MessageBox.Show(this, text, L.T("Импорт", "Import"), MessageBoxButtons.OK, added + had == 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    // ---------- Saving ----------

    void Collect()
    {
        AutoCorrect = tAuto.Checked;
        Typos = tTypos.Checked;
        QuietFullscreen = tQuiet.Checked;
        UndoCtrlZ = tCtrlZ.Checked;
        LayoutColor = tColor.Checked;
        Autostart = tAutostart.Checked;
        CheckUpdates = tUpdates.Checked;
        LayoutBadge = tBadge.Checked;
        RecentMenu = tRecent.Checked;
        QuickKey = cbQuickKey.SelectedIndex;
        Language = cbLanguage.SelectedIndex;
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
    public static string NeedModifier { get { return L.T("нужен Ctrl или Alt", "add Ctrl or Alt"); } }

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

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool HideCaret(IntPtr hwnd);

    public HotkeyBox()
    {
        ReadOnly = true;
        BorderStyle = BorderStyle.FixedSingle;
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
        set { this.value = value; Show(SettingsForm.Format(value)); }
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
        recording = on;
        BackColor = on ? Theme.SelectionBack : Theme.Input;
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
        // Single letters and digits without modifiers would get in the way of typing
        bool plain = e.Modifiers == Keys.None || e.Modifiers == Keys.Shift;
        if (plain && ((code >= Keys.A && code <= Keys.Z) || (code >= Keys.D0 && code <= Keys.D9) || code == Keys.Space))
        {
            Show(SettingsForm.NeedModifier);
            return;
        }
        Finish(code | e.Modifiers);
    }

    // A hotkey is set: show it and stop listening, so the next keys don't change it by accident
    void Finish(Keys k)
    {
        value = k;
        Record(false);
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
