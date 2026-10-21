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
    public AboutForm(string stats, string changelog, ICollection<LayoutTable> layouts = null)
    {
        Text = L.T("О программе", "About KeyFish");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
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
        var what = Theme.Label(L.T("Исправляет текст, набранный не в той раскладке", "Fixes text typed in the wrong keyboard layout"), Theme.Body, Theme.SubText);
        what.Location = new Point(139, 68);
        Controls.Add(what);
        var count = Theme.Label(stats, Theme.Body);
        count.Location = new Point(139, 94);
        Controls.Add(count);

        var link = new LinkLabel
        {
            Text = "github.com/pravken/KeyFish", Location = new Point(139, 120), AutoSize = true, Font = Theme.Body, BackColor = Color.Transparent,
            LinkColor = Theme.AccentFill, ActiveLinkColor = Theme.AccentFill, VisitedLinkColor = Theme.AccentFill, LinkBehavior = LinkBehavior.HoverUnderline
        };
        link.LinkClicked += (s, e) => { try { Process.Start(ProjectUrl); } catch { } };
        Controls.Add(link);

        // A quiet way to support the project: only here and in Settings, never as a reminder
        var support = new RoundButton { Text = L.T("Поддержать ☕", "Support ☕"), Location = new Point(24, 192), Size = new Size(140, 34) };
        support.Click += (s, e) => { try { Process.Start(SupportUrl); } catch { } };
        Controls.Add(support);

        var changes = new RoundButton { Text = L.T("Что нового", "What's new"), Location = new Point(246, 192), Size = new Size(124, 34) };
        changes.Click += (s, e) => { using (var f = new ChangelogForm(changelog)) f.ShowDialog(this); };
        var ok = new RoundButton { Text = "OK", AccentStyle = true, Location = new Point(378, 192), Size = new Size(98, 34) };
        ok.Click += (s, e) => Close();
        Controls.Add(changes);
        Controls.Add(ok);
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) Close(); };
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
    public ChangelogForm(string changelog)
    {
        Text = L.T("Что нового в KeyFish", "What's new in KeyFish");
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Theme.Apply(this);
        ClientSize = new Size(600, 500);
        MinimumSize = new Size(380, 280);

        var card = new Card
        {
            Location = new Point(16, 16), Size = new Size(568, 418),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        var box = new RichTextBox
        {
            ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None,
            Font = Theme.Body, DetectUrls = false, Location = new Point(16, 12), Size = new Size(540, 396),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        Theme.DarkScrollbars(box);
        // A line that is just "1.8.2" becomes a "Version 1.8.2" heading; versions are already separated by blank lines
        var heading = new Font("Segoe UI Semibold", 12f);
        FormClosed += (s, e) => heading.Dispose();
        foreach (string line in changelog.Replace("\r\n", "\n").Trim().Split('\n'))
        {
            bool isHeading = Regex.IsMatch(line, @"^\d+\.\d+\.\d+$");
            box.SelectionFont = isHeading ? heading : box.Font;
            box.AppendText((isHeading ? L.T("Версия ", "Version ") + line : line) + "\n");
        }
        card.Controls.Add(box);
        box.BackColor = Theme.Card;
        Controls.Add(card);
        var close = new RoundButton
        {
            Text = L.T("Закрыть", "Close"), AccentStyle = true, Location = new Point(468, 448), Size = new Size(116, 34),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        close.Click += (s, e) => Close();
        Controls.Add(close);
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Shown += (s, e) => { box.SelectionStart = 0; box.SelectionLength = 0; close.Focus(); };
    }
}
