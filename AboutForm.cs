using System;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// About window: version, statistics and a link; the changelog opens from its own button
class AboutForm : Form
{
    const string ProjectUrl = "https://github.com/pravken/KeyFish";
    const string SupportUrl = "https://ko-fi.com/pravken";

    public AboutForm(Image fish, string stats, string changelog)
    {
        Text = L.T("О программе", "About KeyFish");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(420, 210);

        Controls.Add(new PictureBox { Image = fish, SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(20, 20), Size = new Size(64, 64) });
        Controls.Add(new Label
        {
            Text = "KeyFish " + Application.ProductVersion, Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            Location = new Point(100, 18), AutoSize = true
        });
        Controls.Add(new Label
        {
            Text = L.T("Исправляет текст, набранный не в той раскладке", "Fixes text typed in the wrong keyboard layout"),
            Location = new Point(102, 52), AutoSize = true
        });
        Controls.Add(new Label { Text = stats, Location = new Point(102, 76), AutoSize = true });

        var link = new LinkLabel { Text = "github.com/pravken/KeyFish", Location = new Point(102, 104), AutoSize = true };
        link.LinkClicked += (s, e) => Process.Start(ProjectUrl);
        Controls.Add(link);

        // A quiet way to support the project: only here, never as a reminder
        var support = new Button { Text = L.T("Поддержать ☕", "Support ☕"), Location = new Point(20, 162), Size = new Size(120, 30) };
        support.Click += (s, e) => Process.Start(SupportUrl);
        Controls.Add(support);

        var changes = new Button { Text = L.T("Что нового", "What's new"), Location = new Point(196, 162), Size = new Size(110, 30) };
        changes.Click += (s, e) => { using (var f = new ChangelogForm(changelog)) f.ShowDialog(this); };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(316, 162), Size = new Size(84, 30) };
        Controls.Add(changes);
        Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = ok;
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
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(560, 460);
        MinimumSize = new Size(360, 260);

        var box = new RichTextBox
        {
            ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, BackColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10f), DetectUrls = false,
            Location = new Point(12, 12), Size = new Size(536, 390),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        // A line that is just "1.8.2" becomes a bold "Version 1.8.2" heading; versions are already separated by blank lines
        var bold = new Font(box.Font, FontStyle.Bold);
        foreach (string line in changelog.Replace("\r\n", "\n").Trim().Split('\n'))
        {
            bool heading = Regex.IsMatch(line, @"^\d+\.\d+\.\d+$");
            box.SelectionFont = heading ? bold : box.Font;
            box.AppendText((heading ? L.T("Версия ", "Version ") + line : line) + "\n");
        }
        Controls.Add(box);
        var close = new Button
        {
            Text = L.T("Закрыть", "Close"), DialogResult = DialogResult.OK, Location = new Point(448, 414), Size = new Size(100, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        Controls.Add(close);
        AcceptButton = close;
        CancelButton = close;
        Shown += (s, e) => { box.SelectionStart = 0; box.SelectionLength = 0; close.Focus(); };
    }
}
