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

    // layouts: the installed keyboard layouts, for the easter egg
    public AboutForm(string stats, string changelog, ICollection<LayoutTable> layouts = null, UpdateInfo updates = null)
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

        var link = Theme.Label("github.com/pravken/KeyFish", Theme.Body, Theme.AccentFill);
        link.Location = new Point(139, count.Bottom + 2);
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
        MinimizeBox = false;
        Theme.Apply(this);
        ClientSize = new Size(600, 500);
        MinimumSize = new Size(380, 280);

        var card = new Card
        {
            Location = new Point(16, 16), Size = new Size(568, 418),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        list = new ChangelogView(changelog)
        {
            Location = new Point(16, 12), Size = new Size(544, 396),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        card.Controls.Add(list);
        Controls.Add(card);
        var close = new RoundButton
        {
            Text = L.T("Закрыть", "Close"), AccentStyle = true, Location = new Point(468, 448), Size = new Size(116, 34),
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

// The changelog drawn as one picture: one control paints all the lines into one buffer and scrolls by redrawing,
// so nothing flickers while the window opens or while scrolling (a label per line did both). The text is drawn by
// KeyFish itself, smooth like its other windows, and a line with 🐟 or ⇄ by Windows, which takes them from another font
class ChangelogView : Control
{
    class Block { public string Text; public bool Heading; public int Top, Height; }

    readonly List<Block> blocks = new List<Block>();
    readonly VScrollBar bar = new VScrollBar();
    int laidOutWidth = -1, total;

    public ChangelogView(string changelog)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Card;
        // A line that is just "1.8.2" becomes a "Version 1.8.2" heading; versions are already separated by blank lines
        foreach (string line in changelog.Replace("\r\n", "\n").Trim().Split('\n'))
        {
            if (line.Trim().Length == 0) continue;
            bool heading = Regex.IsMatch(line, @"^\d+\.\d+\.\d+$");
            blocks.Add(new Block { Text = heading ? L.T("Версия ", "Version ") + line : line, Heading = heading });
        }
        bar.Dock = DockStyle.Right;
        bar.ValueChanged += (s, e) => Invalidate();
        Theme.DarkScrollbars(bar);
        Controls.Add(bar);
    }

    static Font FontOf(Block b) { return b.Heading ? Theme.Section : Theme.Body; }

    int TextWidth { get { return Math.Max(50, ClientSize.Width - bar.Width - Theme.S(8)); } }

    // Heights of the lines at the current width, measured the way they are drawn
    void Arrange()
    {
        int width = TextWidth;
        if (width == laidOutWidth) return;
        laidOutWidth = width;
        int y = 0;
        using (var g = CreateGraphics())
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            foreach (var b in blocks)
            {
                if (b.Heading && y > 0) y += Theme.S(14);   // a gap before each version
                var font = FontOf(b);
                int h = Theme.HasEmoji(b.Text)
                    ? TextRenderer.MeasureText(g, b.Text, font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height
                    : (int)Math.Ceiling(g.MeasureString(b.Text, font, width).Height);
                b.Top = y;
                b.Height = h + Theme.S(2);
                y += b.Height + Theme.S(b.Heading ? 4 : 3);
            }
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int width = TextWidth, offset = bar.Value;
        using (var brush = new SolidBrush(Theme.Text))
            foreach (var b in blocks)
            {
                int top = b.Top - offset;
                if (top + b.Height < e.ClipRectangle.Top || top > e.ClipRectangle.Bottom) continue;
                var font = FontOf(b);
                if (Theme.HasEmoji(b.Text))
                {
                    TextRenderer.DrawText(g, b.Text, font, new Rectangle(0, top, width, b.Height), Theme.Text, BackColor,
                                          TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    continue;
                }
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.TextContrast = Theme.TextContrastFor(Theme.Text);
                var r = new RectangleF(0, top, width, b.Height);
                g.DrawString(b.Text, font, brush, r);
                g.DrawString(b.Text, font, brush, new RectangleF(r.X + Theme.Heavier, r.Y, r.Width, r.Height));   // a touch heavier, see Theme.Heavier
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
