using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
    readonly Panel list;

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
        // The text is drawn by KeyFish itself, like in its other windows: smooth, and with 🐟 and ⇄ shown from another
        // font when the main one lacks them (the system text box left empty squares there)
        list = new Panel
        {
            AutoScroll = true, BackColor = Theme.Card, Location = new Point(16, 12), Size = new Size(540, 396),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        Theme.DarkScrollbars(list);
        // A line that is just "1.8.2" becomes a "Version 1.8.2" heading; versions are already separated by blank lines
        foreach (string line in changelog.Replace("\r\n", "\n").Trim().Split('\n'))
        {
            bool isHeading = Regex.IsMatch(line, @"^\d+\.\d+\.\d+$");
            if (line.Trim().Length == 0) continue;
            list.Controls.Add(new SmoothLabel
            {
                Text = isHeading ? L.T("Версия ", "Version ") + line : line, Font = isHeading ? Theme.Section : Theme.Body,
                ForeColor = Theme.Text, BackColor = Theme.Card, AutoSize = false, Tag = isHeading
            });
        }
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
            // The keys scroll the list like a text box would
            int line = Theme.S(24), page = list.ClientSize.Height - line, y = -list.AutoScrollPosition.Y;
            if (e.KeyCode == Keys.Down) y += line;
            else if (e.KeyCode == Keys.Up) y -= line;
            else if (e.KeyCode == Keys.PageDown || e.KeyCode == Keys.Space) y += page;
            else if (e.KeyCode == Keys.PageUp) y -= page;
            else if (e.KeyCode == Keys.Home) y = 0;
            else if (e.KeyCode == Keys.End) y = int.MaxValue / 2;
            else return;
            list.AutoScrollPosition = new Point(0, Math.Max(0, y));
            e.Handled = true;
        };
        list.Resize += (s, e) => Arrange();
        Shown += (s, e) => { Arrange(); close.Focus(); };
        Theme.ScaleWindow(this);
    }

    // Lines one under another at the list's current width; called again when the window is resized
    void Arrange()
    {
        list.SuspendLayout();
        int width = list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Theme.S(4);
        int y = 0, top = list.AutoScrollPosition.Y;
        foreach (Control c in list.Controls)
        {
            bool heading = (bool)c.Tag;
            if (heading && y > 0) y += Theme.S(14);   // a gap before each version
            c.Width = width;
            c.Height = c.GetPreferredSize(new Size(width, 0)).Height + Theme.S(2);
            c.Location = new Point(0, y + top);
            y += c.Height + Theme.S(heading ? 4 : 3);
        }
        list.ResumeLayout();
    }
}
