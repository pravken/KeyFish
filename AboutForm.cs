using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// About window: version, statistics and a link; the changelog opens from its own button
class AboutForm : Form
{
    const string ProjectUrl = "https://github.com/pravken/KeyFish";
    const string SupportUrl = "https://ko-fi.com/pravken";

    // layouts: the installed keyboard layouts, for the easter egg; recent: caught today, in a week, in a month, and the
    // minutes that saved (null: not shown)
    public AboutForm(string stats, string changelog, ICollection<LayoutTable> layouts = null, UpdateInfo updates = null, int[] recent = null)
    {
        Text = L.T("О программе", "About KeyFish");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = MinimizeBox = false;
        Theme.Apply(this);
        ClientSize = new Size(500, 250);

        var fish = new FishView { Location = new Point(24, 24), Size = new Size(96, 96) };
        // Five clicks on the fish: the window turns into an aquarium for a few seconds
        fish.EasterEgg += (s, e) =>
        {
            foreach (Control c in Controls) if (c is Aquarium) return;   // already playing
            string[] word = Greeting(layouts);
            var tank = new Aquarium(word[0], word[1]);
            tank.Bounds = ClientRectangle;
            Controls.Add(tank);
            tank.BringToFront();
        };
        Controls.Add(fish);

        var name = Theme.Label("KeyFish " + Application.ProductVersion, Theme.Title);
        name.Location = new Point(136, 24);
        Controls.Add(name);
        // The description wraps when it is long (German, a large screen), and everything below moves down with it
        var what = Theme.Wrapped(L.T("Исправляет текст, набранный не в той раскладке", "Fixes text typed in the wrong keyboard layout"), 337, Theme.Body, Theme.SubText);
        what.Location = new Point(139, 68);
        Controls.Add(what);
        var count = Theme.Wrapped(stats, 337, Theme.Body);
        count.Location = new Point(139, what.Bottom + 2);
        Controls.Add(count);
        Control above = count;
        if (recent != null)
        {
            var strip = new StatsStrip(new[]
            {
                Tuple.Create(recent[0].ToString("N0", L.Culture), L.T("сегодня", "today")),
                Tuple.Create(recent[1].ToString("N0", L.Culture), L.T("за неделю", "this week")),
                Tuple.Create(recent[2].ToString("N0", L.Culture), L.T("за месяц", "this month")),
                Tuple.Create(L.F("≈{0} мин", "≈{0} min", recent[3].ToString("N0", L.Culture)), L.T("сэкономлено", "saved"))
            }) { Location = new Point(139, count.Bottom + 10), Size = new Size(337, 46) };
            Controls.Add(strip);
            above = strip;
        }

        var link = Theme.Label("github.com/pravken/KeyFish", Theme.Body, Theme.AccentFill);
        link.Location = new Point(139, above.Bottom + (recent != null ? 10 : 2));
        link.Cursor = Cursors.Hand;
        var underlined = new Font(Theme.Body, FontStyle.Underline);
        FormClosed += (s, e) => underlined.Dispose();
        link.MouseEnter += (s, e) => link.Font = underlined;
        link.MouseLeave += (s, e) => link.Font = Theme.Body;
        link.Click += (s, e) => { try { Process.Start(ProjectUrl); } catch { } };
        Controls.Add(link);

        // Updates: "Check for updates", then the answer; a new version is a link that installs it
        Control below = link;
        if (updates != null)
        {
            var update = Theme.Label("", Theme.Body, Theme.AccentFill);
            update.Location = new Point(139, link.Top + (int)Math.Ceiling(link.Height / Theme.Scale) + 2);
            Action refresh = () =>
            {
                bool act = !updates.Checking && (updates.NewVersion || updates.Latest == null);
                update.Text = updates.Checking ? L.T("Проверяю…", "Checking…")
                            : updates.NewVersion ? L.F("Вышла версия {0} — обновить", "Version {0} is out — update", updates.Latest.Tag)
                            : updates.Latest == null && !updates.Failed ? L.T("Проверить обновления", "Check for updates")
                            : updates.Status;
                update.ForeColor = act ? Theme.AccentFill : Theme.SubText;
                update.Cursor = act ? Cursors.Hand : Cursors.Default;
            };
            update.Click += (s, e) =>
            {
                if (updates.Checking) return;
                if (updates.NewVersion) { updates.Install(); return; }
                if (updates.Latest != null && !updates.Failed) return;
                updates.Check(() => { if (!IsDisposed) refresh(); });
                refresh();
            };
            refresh();
            Controls.Add(update);
            below = update;
        }

        // A quiet way to support the project: only here and in Settings, never as a reminder
        var support = new RoundButton
        {
            Text = L.T("Поддержать ☕", "Support ☕").Replace("☕", "").Trim(), Glyph = "", GlyphColor = Color.FromArgb(232, 72, 85),
            Location = new Point(24, 0), Size = new Size(150, 34)
        };
        support.Click += (s, e) => { try { Process.Start(SupportUrl); } catch { } };
        Controls.Add(support);

        var changes = new RoundButton { Text = L.T("Что нового", "What's new"), Location = new Point(246, 0), Size = new Size(124, 34) };
        changes.Click += (s, e) => { using (var f = new ChangelogForm(changelog)) f.ShowDialog(this); };
        var ok = new RoundButton { Text = "OK", AccentStyle = true, Location = new Point(378, 0), Size = new Size(98, 34) };
        ok.Click += (s, e) => Close();
        Controls.Add(changes);
        Controls.Add(ok);
        // The buttons sit under the text, however many lines it took ("link" is sized by its font: back to 96-DPI pixels)
        int buttons = Math.Max(192, below.Top + (int)Math.Ceiling(below.Height / Theme.Scale) + 34);
        foreach (var b in new Control[] { support, changes, ok }) b.Top = buttons;
        ClientSize = new Size(500, buttons + 58);
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) Close(); };
        Theme.SolidLabels(this);
        Theme.ScaleWindow(this);
    }
    // "Hello" in each language KeyFish knows
    static readonly Dictionary<string, string> Hello = new Dictionary<string, string>
    {
        { "en", "hello" }, { "ru", "привет" }, { "uk", "привіт" }, { "be", "прывітанне" }, { "de", "hallo" }, { "pl", "witaj" },
        { "sr-latn", "zdravo" }, { "sr-cyrl", "здраво" }, { "bg", "здравей" }, { "mk", "здраво" }, { "fr", "bonjour" }, { "es", "hola" },
        { "it", "ciao" }, { "pt", "olá" }, { "cs", "ahoj" }, { "tr", "merhaba" }, { "el", "γεια" }, { "he", "שלום" }, { "ka", "გამარჯობა" }
    };

    // The word for the easter egg: "hello" in one of the user's layouts (the interface language first) and the same keys
    // pressed in an installed layout of another alphabet — exactly the mistake KeyFish fixes for this user.
    // With a single alphabet installed: a built-in pair for the interface language
    static string[] Greeting(ICollection<LayoutTable> layouts)
    {
        if (layouts != null)
        {
            string ui = L.Code == "sr" ? "sr-cyrl" : L.Code;
            var targets = new List<LayoutTable>();
            foreach (var t in layouts) if (t.Lang != null && Hello.ContainsKey(t.Lang)) targets.Add(t);
            targets.Sort((a, b) => (b.Lang == ui ? 1 : 0) - (a.Lang == ui ? 1 : 0));
            foreach (var target in targets)
                foreach (var other in layouts)
                {
                    if (other.Script == null || other.Script == target.Script) continue;
                    string right = Hello[target.Lang];
                    var keys = target.Keys(right);
                    if (keys == null) continue;
                    string wrong = other.Type(keys);
                    if (wrong.IndexOf('\uFFFD') < 0) return new[] { wrong, right + "!" };
                }
        }
        switch (L.Code)
        {
            case "ru": return new[] { "ghbdtn", "привет!" };
            case "uk": return new[] { "ghbdsn", "привіт!" };
            case "be": return new[] { "ghsdbnfyyt", "прывітанне!" };
            case "de": return new[] { "рфддщ", "hallo!" };
            case "pl": return new[] { "цшефо", "witaj!" };
            case "sr": return new[] { "хелло", "hello!" };
            case "fr": return new[] { "ищтощгк", "bonjour!" };
            case "es": return new[] { "рщдф", "hola!" };
            default: return new[] { "руддщ", "hello!" };
        }
    }
}
// The full changelog in a scrollable window
class ChangelogForm : Form
{
    readonly ChangelogView list;

    public ChangelogForm(string changelog)
    {
        Text = L.T("Что нового в KeyFish", "What's new in KeyFish");
        StartPosition = FormStartPosition.CenterParent;
        // Resizable for the long list, but not to the whole screen: lines that long are hard to read
        MinimizeBox = MaximizeBox = false;
        Theme.Apply(this);
        ClientSize = new Size(600, 540);
        MinimumSize = new Size(380, 280);

        list = new ChangelogView(changelog)
        {
            Location = new Point(16, 16), Size = new Size(568, 458),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        Controls.Add(list);
        var close = new RoundButton
        {
            Text = L.T("Закрыть", "Close"), AccentStyle = true, Location = new Point(468, 488), Size = new Size(116, 34),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        close.Click += (s, e) => Close();
        Controls.Add(close);
        KeyPreview = true;
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
            else if (list.Key(e.KeyCode)) e.Handled = true;
        };
        Shown += (s, e) => close.Focus();
        Theme.ScaleWindow(this);
    }
}

// The changelog drawn as one picture: one control paints everything into one buffer and scrolls by redrawing,
// so nothing flickers while the window opens or while scrolling (a label per line did both). Each version is a card:
// its number and date (and "Your version" on the running one), then its changes in groups — New, Improved, Fixed —
// each change with a bullet and its lines indented after it. The text is drawn by KeyFish itself, smooth like its
// other windows, and a line with 🐟 or ⇄ by Windows, which takes them from another font.
// The file: "2.1.0 2026-10-29" starts a version, "New:" (any line ending with a colon) a group, "• " a change
class ChangelogView : Control
{
    const int Version = 0, Group = 1, Item = 2;
    class Block { public string Text, Date; public int Kind, Group, Top, Height; public bool Current; }
    class Box { public int Top, Bottom; }

    readonly List<Block> blocks = new List<Block>();
    readonly List<Box> boxes = new List<Box>();
    readonly VScrollBar bar = new VScrollBar();
    int laidOutWidth = -1, total;

    public ChangelogView(string changelog)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Back;
        string running = Application.ProductVersion;
        int group = -1;
        foreach (string raw in changelog.Replace("\r\n", "\n").Trim().Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            var v = Regex.Match(line, @"^(\d+\.\d+\.\d+)(?:\s+(\d{4}-\d{2}-\d{2}))?$");
            if (v.Success)
            {
                group = -1;
                blocks.Add(new Block { Kind = Version, Text = v.Groups[1].Value, Date = FormatDate(v.Groups[2].Value), Current = v.Groups[1].Value == running });
            }
            else if (!line.StartsWith("•") && line.EndsWith(":"))
            {
                // New, Improved, Fixed by their names in either changelog; another name gets the next color
                string name = line.TrimEnd(':');
                group = name == "Новое" || name == "New" ? 0 : name == "Улучшено" || name == "Improved" ? 1 : name == "Исправлено" || name == "Fixed" ? 2 : group + 1;
                blocks.Add(new Block { Kind = Group, Text = name, Group = group });
            }
            else blocks.Add(new Block { Kind = Item, Text = line.TrimStart('•', ' ') });
        }
        bar.Dock = DockStyle.Right;
        bar.ValueChanged += (s, e) => Invalidate();
        Theme.DarkScrollbars(bar);
        Controls.Add(bar);
    }

    static string FormatDate(string iso)
    {
        DateTime d;
        if (!DateTime.TryParseExact(iso, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d)) return null;
        return d.ToString("d MMMM yyyy", L.Culture);
    }

    static Font FontOf(Block b) { return b.Kind == Version ? Theme.Section : b.Kind == Group ? Theme.Strong : Theme.Body; }

    // Inside a card: the padding, and where a change's text starts after its bullet
    int Pad { get { return Theme.S(16); } }
    int Indent { get { return Theme.S(16); } }
    int CardWidth { get { return Math.Max(80, ClientSize.Width - bar.Width - Theme.S(6)); } }
    int TextLeft(Block b) { return Pad + (b.Kind == Item ? Indent : 0); }
    int TextWidth(Block b) { return Math.Max(40, CardWidth - TextLeft(b) - Pad); }

    int Measure(Graphics g, string text, Font font, int width)
    {
        return Theme.HasEmoji(text)
            ? TextRenderer.MeasureText(g, text, font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height
            : (int)Math.Ceiling(g.MeasureString(text, font, width).Height);
    }

    // Heights of the lines and the cards at the current width, measured the way they are drawn
    void Arrange()
    {
        int width = CardWidth;
        if (width == laidOutWidth) return;
        laidOutWidth = width;
        boxes.Clear();
        int y = 0;
        Box box = null;
        using (var g = CreateGraphics())
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            foreach (var b in blocks)
            {
                if (b.Kind == Version)
                {
                    if (box != null) { box.Bottom = y + Pad - Theme.S(4); y = box.Bottom + Theme.S(12); }
                    box = new Box { Top = y };
                    boxes.Add(box);
                    y += Pad - Theme.S(2);
                }
                else if (b.Kind == Group) y += Theme.S(8);
                b.Top = y;
                b.Height = Measure(g, b.Text, FontOf(b), TextWidth(b)) + Theme.S(2);
                y += b.Height + Theme.S(b.Kind == Version ? 4 : b.Kind == Group ? 2 : 3);
            }
            if (box != null) { box.Bottom = y + Pad - Theme.S(4); y = box.Bottom + Theme.S(2); }
        }
        total = y;
        UpdateBar();
    }

    void UpdateBar()
    {
        int view = ClientSize.Height;
        bar.Enabled = total > view;
        bar.Minimum = 0;
        bar.Maximum = Math.Max(0, total - 1);
        bar.LargeChange = Math.Max(1, view);
        bar.SmallChange = Theme.S(24);
        bar.Value = Math.Max(0, Math.Min(bar.Value, Math.Max(0, total - view)));
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Arrange(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); if (IsHandleCreated) { Arrange(); UpdateBar(); } }

    void ScrollTo(int y) { bar.Value = Math.Max(0, Math.Min(y, Math.Max(0, total - ClientSize.Height))); }

    // Arrows, Page Up/Down, Space, Home and End scroll like in a text box; false for other keys
    public bool Key(Keys key)
    {
        int line = Theme.S(24), page = ClientSize.Height - line, y = bar.Value;
        if (key == Keys.Down) y += line;
        else if (key == Keys.Up) y -= line;
        else if (key == Keys.PageDown || key == Keys.Space) y += page;
        else if (key == Keys.PageUp) y -= page;
        else if (key == Keys.Home) y = 0;
        else if (key == Keys.End) y = total;
        else return false;
        ScrollTo(y);
        return true;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollTo(bar.Value - e.Delta * SystemInformation.MouseWheelScrollLines * Theme.S(20) / 120);
        base.OnMouseWheel(e);
    }

    protected override void OnMouseEnter(EventArgs e) { Focus(); base.OnMouseEnter(e); }   // the wheel goes to the control in focus

    // The groups' marks: the accent for what is new, blue for what got better, green for what was fixed
    static Color GroupColor(int group)
    {
        if (group == 0) return Theme.AccentFill;
        if (group == 1) return Theme.Dark ? Color.FromArgb(110, 170, 255) : Color.FromArgb(30, 100, 210);
        return Theme.Dark ? Color.FromArgb(108, 203, 95) : Color.FromArgb(16, 124, 16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int width = CardWidth, offset = bar.Value;
        foreach (var box in boxes)
        {
            if (box.Bottom - offset < e.ClipRectangle.Top || box.Top - offset > e.ClipRectangle.Bottom) continue;
            using (var path = Theme.Rounded(new RectangleF(0.5f, box.Top - offset + 0.5f, width - 1.5f, box.Bottom - box.Top - 1), Theme.S(6)))
            {
                using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
                using (var p = new Pen(Theme.CardBorder)) g.DrawPath(p, path);
            }
        }
        int group = 0;
        foreach (var b in blocks)
        {
            if (b.Kind == Group) group = b.Group;
            if (b.Kind == Version) group = 0;
            int top = b.Top - offset;
            if (top + b.Height < e.ClipRectangle.Top || top > e.ClipRectangle.Bottom) continue;
            var font = FontOf(b);
            int left = TextLeft(b), textWidth = TextWidth(b);
            if (b.Kind == Version)
            {
                // The number, then the date in grey and the badge of the running version
                Write(g, b.Text, font, new Rectangle(left, top, textWidth, b.Height), Theme.Text);
                float x = left + g.MeasureString(b.Text, font).Width + Theme.S(8);
                float mid = top + b.Height / 2f;
                if (b.Date != null)
                {
                    var size = g.MeasureString(b.Date, Theme.Small);
                    Write(g, b.Date, Theme.Small, new Rectangle((int)x, (int)(mid - size.Height / 2), (int)size.Width + 4, (int)size.Height + 2), Theme.SubText);
                    x += size.Width + Theme.S(8);
                }
                if (b.Current)
                {
                    string label = L.T("Ваша версия", "Your version");
                    var size = g.MeasureString(label, Theme.Small);
                    var pill = new RectangleF(x, mid - size.Height / 2 - Theme.S(2), size.Width + Theme.S(12), size.Height + Theme.S(4));
                    using (var path = Theme.Rounded(pill, pill.Height / 2))
                    using (var brush = new SolidBrush(Theme.AccentFill)) g.FillPath(brush, path);
                    Write(g, label, Theme.Small, Rectangle.Round(new RectangleF(pill.X + Theme.S(6), pill.Y + Theme.S(2), size.Width + 4, size.Height)), Theme.OnAccent);
                }
                continue;
            }
            if (b.Kind == Group)
            {
                // A colored dot before the group's name
                float d = Theme.S(8);
                using (var brush = new SolidBrush(GroupColor(b.Group))) g.FillEllipse(brush, left, top + (b.Height - d) / 2f, d, d);
                Write(g, b.Text, font, new Rectangle(left + Theme.S(14), top, textWidth - Theme.S(14), b.Height), Theme.Text);
                continue;
            }
            // A change: the bullet in the group's color, the text indented after it
            using (var brush = new SolidBrush(GroupColor(group)))
            {
                float d = Theme.S(4), lineH = font.GetHeight(g);
                g.FillEllipse(brush, Pad + Theme.S(2), top + lineH / 2f - d / 2f + Theme.S(1), d, d);
            }
            Write(g, b.Text, font, new Rectangle(left, top, textWidth, b.Height), Theme.Text);
        }
    }

    void Write(Graphics g, string text, Font font, Rectangle r, Color color)
    {
        if (Theme.HasEmoji(text))
        {
            TextRenderer.DrawText(g, text, font, r, color, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            return;
        }
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.TextContrast = Theme.TextContrastFor(color);
        using (var brush = new SolidBrush(color))
        {
            g.DrawString(text, font, brush, r);
            g.DrawString(text, font, brush, new RectangleF(r.X + Theme.Heavier, r.Y, r.Width, r.Height));   // a touch heavier, see Theme.Heavier
        }
    }
}

// The first start: three short steps on what KeyFish does, how to take a fix back and where the settings are,
// each with a looping picture like those in Settings. Shown once, to new users only
class IntroForm : Form
{
    readonly string[] titles, texts;
    readonly FeaturePreview[] pictures;
    readonly SmoothLabel title, text;
    readonly Control dots;
    readonly RoundButton back, next;
    int step;

    public IntroForm()
    {
        Text = L.T("Добро пожаловать в KeyFish", "Welcome to KeyFish");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        Theme.Apply(this);
        ClientSize = new Size(520, 330);

        titles = new[]
        {
            L.T("KeyFish исправляет раскладку на лету", "KeyFish fixes the layout as you type"),
            L.T("Если исправлять было не нужно", "If a fix wasn't wanted"),
            L.T("Всё остальное — в настройках", "Everything else is in Settings")
        };
        texts = new[]
        {
            L.T("Набрали слово не в той раскладке — после пробела KeyFish сам переведёт его: ghbdtn → привет.",
                "Typed a word in the wrong layout? After the space KeyFish switches it: ghbdtn → привет."),
            L.T("Нажмите Pause сразу после исправления: слово вернётся, а KeyFish запомнит его и больше не тронет.",
                "Press Pause right after the fix: the word comes back, and KeyFish remembers it and leaves it alone."),
            L.T("Двойной щелчок по рыбке в трее открывает настройки: горячие клавиши, автозамены, исключения, расширенный словарь. Рыбка показывает раскладку и сереет, когда KeyFish не исправляет.",
                "Double-click the fish in the tray for Settings: hotkeys, replacements, exclusions, the extended dictionary. The fish shows the layout and turns grey while KeyFish isn't correcting.")
        };
        pictures = new[]
        {
            new FeaturePreview((g, r, ms) => Pictures.Typing(g, r, ms, "ghbdtn", "привет", false), Pictures.TypingLength("ghbdtn", false)),
            new FeaturePreview((g, r, ms) => Pictures.Typing(g, r, ms, "ghbdtn", "привет", true, "Pause"), Pictures.TypingLength("ghbdtn", true)),
            new FeaturePreview((g, r, ms) => Pictures.Switching(g, r, ms, () => fishEn, () => fishRu), Pictures.SwitchLength)
        };
        foreach (var p in pictures)
        {
            p.Size = new Size(240, 52);
            p.Location = new Point((520 - 240) / 2, 34);
            p.Visible = false;
            Controls.Add(p);
        }
        title = new SmoothLabel { Font = Theme.Section, ForeColor = Theme.Text, AutoSize = false, Location = new Point(32, 112), Size = new Size(456, 30), TextAlign = ContentAlignment.MiddleCenter };
        text = new SmoothLabel { Font = Theme.Body, ForeColor = Theme.SubText, AutoSize = false, Location = new Point(48, 146), Size = new Size(424, 90), TextAlign = ContentAlignment.TopCenter };
        // Which step this is: one dot per step, the current one filled with the accent color
        dots = new Panel { Location = new Point(32, 276), Size = new Size(80, 34), BackColor = Theme.Back };
        dots.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float d = Theme.S(8), gap = Theme.S(8), y = (dots.Height - d) / 2;
            for (int k = 0; k < titles.Length; k++)
            {
                var r = new RectangleF(k * (d + gap) + 1, y, d, d);
                if (k == step) using (var b = new SolidBrush(Theme.AccentFill)) e.Graphics.FillEllipse(b, r);
                else using (var p = new Pen(Theme.SubText, 1.2f)) e.Graphics.DrawEllipse(p, r);
            }
        };
        back = new RoundButton { Text = L.T("Назад", "Back"), Location = new Point(264, 276), Size = new Size(108, 34) };
        next = new RoundButton { AccentStyle = true, Location = new Point(380, 276), Size = new Size(108, 34) };
        back.Click += (s, e) => Show(step - 1);
        next.Click += (s, e) => { if (step == titles.Length - 1) Close(); else Show(step + 1); };
        Controls.AddRange(new Control[] { title, text, dots, back, next });
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Show(0);
        Theme.SolidLabels(this);
        Theme.ScaleWindow(this);
        Shown += (s, e) => { next.Focus(); pictures[step].Play(300); };
    }

    readonly Image fishEn = Pictures.Fish(false), fishRu = Pictures.Fish(true);

    void Show(int i)
    {
        step = i;
        for (int k = 0; k < pictures.Length; k++) pictures[k].Visible = k == i;
        if (IsHandleCreated) pictures[i].Play(150);
        title.Text = titles[i];
        text.Text = texts[i];
        dots.Invalidate();
        back.Visible = i > 0;
        next.Text = i == titles.Length - 1 ? L.T("Начать", "Start") : L.T("Далее", "Next");
        next.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { fishEn.Dispose(); fishRu.Dispose(); }
        base.Dispose(disposing);
    }
}

// The statistics in About as a row of numbers with a caption under each, split by thin lines
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
        float w = (float)Width / items.Length, valueH = Height * 0.56f;
        for (int i = 0; i < items.Length; i++)
        {
            float x = i * w;
            if (i > 0)
                using (var p = new Pen(Theme.InputBorder)) g.DrawLine(p, x, Height * 0.15f, x, Height * 0.85f);
            Theme.SmoothText(g, items[i].Item1, Theme.Section, new RectangleF(x, 0, w, valueH), Theme.Text, true);
            Theme.SmoothText(g, items[i].Item2, Theme.Small, new RectangleF(x, valueH - 2, w, Height - valueH), Theme.SubText, true, false);
        }
    }
}
