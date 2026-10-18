using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// Settings window: toggles, hotkeys and the lists that used to be edited in Notepad
class SettingsForm : Form
{
    public bool AutoCorrect, Typos, QuietFullscreen, LayoutColor, Autostart, CheckUpdates;
    public int Language;                 // 0 — same as Windows, 1 — Russian, 2 — English
    public Keys[] Hotkeys;
    public string Replacements, Exclusions, Ignored, Learned;

    CheckBox cbAuto, cbTypos, cbQuiet, cbColor, cbAutostart, cbUpdates;
    ComboBox cbLanguage;
    HotkeyBox[] hotkeyBoxes;
    TextBox tbReplacements, tbExclusions, tbIgnored, tbLearned;

    public SettingsForm()
    {
        Text = L.T("Настройки KeyFish", "KeyFish settings");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = true;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(560, 534);
    }

    protected override void OnLoad(EventArgs e)
    {
        var tabs = new TabControl { Location = new Point(10, 10), Size = new Size(540, 474) };
        tabs.TabPages.Add(MainPage());
        tabs.TabPages.Add(TextPage(L.T("Автозамены", "Replacements"),
            L.T("Сокращение = замена, по одной на строку. Наберите сокращение и пробел — оно заменится. " +
                "\\n внутри замены — перенос строки. Строки с # в начале не действуют.",
                "abbreviation = text, one per line. Type the abbreviation and a space to expand it. " +
                "\\n inside the text is a line break. Lines starting with # are ignored."), Replacements, out tbReplacements));
        tabs.TabPages.Add(TextPage(L.T("Исключения", "Excluded apps"),
            L.T("Программы, где автоисправление выключено: имя процесса по одному в строке, " +
                "как в Диспетчере задач (например, Code или WINWORD).",
                "Apps where auto-correction is off: one process name per line, " +
                "as shown in Task Manager (for example, Code or WINWORD)."), Exclusions, out tbExclusions));
        tabs.TabPages.Add(TextPage(L.T("Не исправлять", "Never correct"),
            L.T("Слова, которые программа никогда не исправляет. Сюда попадают слова, исправление которых вы отменили или стёрли.",
                "Words KeyFish never corrects. Words land here when you undo or erase their correction."), Ignored, out tbIgnored));
        tabs.TabPages.Add(TextPage(L.T("Всегда исправлять", "Always correct"),
            L.T("Слова, которые программа всегда переводит в другую раскладку. Сюда попадают слова, которые вы сами исправили через Pause.",
                "Words KeyFish always switches to the other layout. Words land here when you fix them yourself with Pause."), Learned, out tbLearned));
        Controls.Add(tabs);

        var ok = new Button { Text = L.T("Сохранить", "Save"), DialogResult = DialogResult.OK, Location = new Point(364, 496), Size = new Size(90, 28) };
        var cancel = new Button { Text = L.T("Отмена", "Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(460, 496), Size = new Size(90, 28) };
        ok.Click += (s, ev) => Collect();
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        base.OnLoad(e);
    }

    TabPage MainPage()
    {
        var page = new TabPage(L.T("Основное", "General")) { Padding = new Padding(10) };
        int y = 12;
        cbAuto = Check(page, L.T("Исправлять раскладку при наборе (ghbdtn → привет) и автозамены",
                                 "Fix the keyboard layout as you type (ghbdtn → привет) and expand replacements"), AutoCorrect, ref y);
        cbTypos = Check(page, L.T("Исправлять опечатки (эксперимент): пирвет → привет",
                                  "Fix typos (experimental): recieve → receive"), Typos, ref y);
        cbQuiet = Check(page, L.T("Не исправлять в играх и полноэкранных программах",
                                  "Stay quiet in games and full-screen apps"), QuietFullscreen, ref y);
        cbColor = Check(page, L.T("Цвет рыбки по раскладке: синяя — EN, оранжевая — RU",
                                  "Fish color shows the layout: blue — EN, orange — RU"), LayoutColor, ref y);
        cbAutostart = Check(page, L.T("Запускать вместе с Windows", "Start with Windows"), Autostart, ref y);
        cbUpdates = Check(page, L.T("Проверять обновления", "Check for updates"), CheckUpdates, ref y);

        page.Controls.Add(new Label { Text = L.T("Язык:", "Language:"), Location = new Point(14, y + 4), AutoSize = true });
        cbLanguage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(100, y), Size = new Size(220, 24) };
        cbLanguage.Items.AddRange(new object[] { L.T("Как в Windows", "Same as Windows"), "Русский", "English" });
        cbLanguage.SelectedIndex = Language;
        page.Controls.Add(cbLanguage);
        y += 36;

        y += 6;
        page.Controls.Add(new Label
        {
            Text = L.T("Горячие клавиши (щёлкните поле и нажмите сочетание; Backspace — выключить):",
                       "Hotkeys (click a field and press the keys; Backspace turns it off):"),
            Location = new Point(12, y), AutoSize = true, Font = new Font(Font, FontStyle.Bold)
        });
        y += 28;

        hotkeyBoxes = new HotkeyBox[Hotkeys.Length];
        for (int i = 0; i < Hotkeys.Length; i++)
        {
            page.Controls.Add(new Label { Text = App.HotkeyTitles[i], Location = new Point(12, y + 4), Size = new Size(300, 36) });
            var box = new HotkeyBox { Location = new Point(320, y), Size = new Size(130, 24), Value = Hotkeys[i] };
            int index = i;
            var reset = new Button { Text = L.T("Сброс", "Reset"), Location = new Point(456, y - 1), Size = new Size(60, 26) };
            reset.Click += (s, e) => hotkeyBoxes[index].Value = App.HotkeyDefaults[index];
            page.Controls.Add(box);
            page.Controls.Add(reset);
            hotkeyBoxes[i] = box;
            y += 42;
        }
        return page;
    }

    static CheckBox Check(Control parent, string text, bool value, ref int y)
    {
        var cb = new CheckBox { Text = text, Checked = value, Location = new Point(12, y), AutoSize = true };
        parent.Controls.Add(cb);
        y += 28;
        return cb;
    }

    static TabPage TextPage(string title, string hint, string text, out TextBox box)
    {
        var page = new TabPage(title) { Padding = new Padding(10) };
        page.Controls.Add(new Label { Text = hint, Location = new Point(10, 10), Size = new Size(505, 40) });
        box = new TextBox
        {
            Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, WordWrap = false,
            Location = new Point(10, 54), Size = new Size(505, 315), Font = new Font("Consolas", 10f),
            Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n")
        };
        // Without this, switching to the tab selects all the text and a single keystroke would erase it
        var tb = box;
        tb.Enter += (s, e) => tb.BeginInvoke(new Action(() => tb.Select(0, 0)));
        page.Controls.Add(box);
        return page;
    }

    void Collect()
    {
        AutoCorrect = cbAuto.Checked;
        Typos = cbTypos.Checked;
        QuietFullscreen = cbQuiet.Checked;
        LayoutColor = cbColor.Checked;
        Autostart = cbAutostart.Checked;
        CheckUpdates = cbUpdates.Checked;
        Language = cbLanguage.SelectedIndex;
        for (int i = 0; i < Hotkeys.Length; i++) Hotkeys[i] = hotkeyBoxes[i].Value;
        Replacements = tbReplacements.Text;
        Exclusions = tbExclusions.Text;
        Ignored = tbIgnored.Text;
        Learned = tbLearned.Text;
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

// A field that records the pressed key combination
class HotkeyBox : TextBox
{
    Keys value;

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        TextAlign = HorizontalAlignment.Center;
        ShortcutsEnabled = false;
    }

    public Keys Value
    {
        get { return value; }
        set { this.value = value; Text = SettingsForm.Format(value); }
    }

    protected override bool IsInputKey(Keys keyData) { return true; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        Keys code = e.KeyCode;
        if (code == Keys.ShiftKey || code == Keys.ControlKey || code == Keys.Menu || code == Keys.LWin || code == Keys.RWin)
        {
            Text = (e.Modifiers == Keys.None ? "" : SettingsForm.Format(e.Modifiers)) + "…";
            return;
        }
        if ((code == Keys.Back || code == Keys.Delete) && e.Modifiers == Keys.None) { Value = Keys.None; return; }
        // Single letters and digits without modifiers would get in the way of typing
        bool plain = e.Modifiers == Keys.None || e.Modifiers == Keys.Shift;
        if (plain && ((code >= Keys.A && code <= Keys.Z) || (code >= Keys.D0 && code <= Keys.D9) || code == Keys.Space))
        {
            Text = SettingsForm.NeedModifier;
            return;
        }
        Value = code | e.Modifiers;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (Text.EndsWith("…") || Text == SettingsForm.NeedModifier) Text = SettingsForm.Format(value);
        base.OnKeyUp(e);
    }

    protected override void OnLeave(EventArgs e)
    {
        Text = SettingsForm.Format(value);
        base.OnLeave(e);
    }
}
