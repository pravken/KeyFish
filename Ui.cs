using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// Windows 11 look for plain WinForms: colors that follow the system light/dark theme and accent color,
// and a few self-drawn controls (switch, card, rounded button, sidebar). No extra libraries.
static class Theme
{
    public static bool Dark = ReadDark();
    public static Color Accent = ReadAccent();
    public static int Mode;   // the theme chosen in Settings: 0 — as in Windows, 1 — light, 2 — dark
    // The holiday look (Halloween: a pumpkin accent, a night-purple dark theme, the fish in a witch's hat), set at start
    // and when Settings are saved: on during the holiday unless switched off in Settings
    public static bool Holiday;

    public static bool IsHalloween(DateTime day) { return (day.Month == 10 && day.Day >= 24) || (day.Month == 11 && day.Day <= 2); }

    // Windows switched to the dark or light theme, the accent color changed or another theme was chosen in Settings:
    // read them again. True if anything changed
    public static bool Refresh()
    {
        bool dark = ReadDark();
        Color accent = ReadAccent();
        if (dark == Dark && accent == Accent) return false;
        Dark = dark;
        Accent = accent;
        return true;
    }

    // On Halloween the dark theme leans to night purple
    public static Color Back { get { return Dark ? (Holiday ? Color.FromArgb(31, 26, 38) : Color.FromArgb(32, 32, 32)) : Color.FromArgb(243, 243, 243); } }
    public static Color Card { get { return Dark ? (Holiday ? Color.FromArgb(43, 36, 52) : Color.FromArgb(43, 43, 43)) : Color.FromArgb(251, 251, 251); } }
    public static Color CardBorder { get { return Dark ? (Holiday ? Color.FromArgb(27, 22, 34) : Color.FromArgb(29, 29, 29)) : Color.FromArgb(229, 229, 229); } }
    public static Color Text { get { return Dark ? Color.White : Color.FromArgb(27, 27, 27); } }
    public static Color SubText { get { return Dark ? Color.FromArgb(200, 200, 200) : Color.FromArgb(96, 96, 96); } }
    public static Color Input { get { return Dark ? (Holiday ? Color.FromArgb(46, 39, 56) : Color.FromArgb(45, 45, 45)) : Color.White; } }
    public static Color InputBorder { get { return Dark ? (Holiday ? Color.FromArgb(76, 64, 92) : Color.FromArgb(70, 70, 70)) : Color.FromArgb(214, 214, 214); } }
    public static Color Hover { get { return Dark ? (Holiday ? Color.FromArgb(58, 49, 70) : Color.FromArgb(55, 55, 55)) : Color.FromArgb(233, 233, 233); } }
    public static Color Selected { get { return Dark ? (Holiday ? Color.FromArgb(64, 54, 78) : Color.FromArgb(60, 60, 60)) : Color.FromArgb(228, 228, 228); } }
    public static Color Danger { get { return Dark ? Color.FromArgb(255, 153, 164) : Color.FromArgb(196, 43, 28); } }
    // On a dark background Windows 11 uses a lighter shade of the accent color
    public static Color AccentFill { get { return Dark ? Blend(Accent, Color.White, 0.45) : Accent; } }
    public static Color OnAccent { get { return Dark ? Color.Black : Color.White; } }
    public static Color SelectionBack { get { return Blend(AccentFill, Card, Dark ? 0.7 : 0.82); } }

    // The screen's scale (125 % → 1.25). Windows are laid out in 96-DPI pixels and scaled by this once, when built
    // (ScaleWindow); fonts are in points and grow with the screen by themselves.
    // KEYFISH_UI_SCALE=1.5 shows the windows as on a 150 % screen, to check layouts on a 100 % one (fonts grow too)
    public static readonly float Scale = ReadScale();
    static readonly float FontBoost = Scale / ScreenScale();

    static float ScreenScale()
    {
        try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f; }
        catch { return 1f; }
    }

    static float ReadScale()
    {
        float forced;
        string env = Environment.GetEnvironmentVariable("KEYFISH_UI_SCALE");
        if (env != null && float.TryParse(env, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out forced) && forced >= 1 && forced <= 3)
            return forced;
        return ScreenScale();
    }

    // A size in 96-DPI pixels on this screen, for what is set after a window is scaled or drawn in pixels
    public static int S(int px) { return (int)Math.Round(px * Scale); }

    // Scales a window built in 96-DPI pixels to the screen, once. Text-sized parts (see Wrapped) are already right
    public static void ScaleWindow(Form f)
    {
        f.AutoScaleMode = AutoScaleMode.None;
        if (Math.Abs(Scale - 1) > 0.01f) f.Scale(new SizeF(Scale, Scale));
    }

    // Windows 11's own font, Segoe UI Variable, at the sizes of Windows Settings; Segoe UI where it isn't installed (Windows 10)
    static readonly bool Variable = HasFont("Segoe UI Variable Text") && HasFont("Segoe UI Variable Text Semibold");
    static readonly string Regular = Variable ? "Segoe UI Variable Text" : "Segoe UI";
    static readonly string Semibold = Variable ? "Segoe UI Variable Text Semibold" : "Segoe UI Semibold";
    public static readonly Font Body = new Font(Regular, 10.5f * FontBoost);
    public static readonly Font Small = new Font(Regular, 9f * FontBoost);
    public static readonly Font Strong = new Font(Semibold, 10.5f * FontBoost);
    public static readonly Font Title = new Font(Semibold, 20f * FontBoost);
    public static readonly Font Section = new Font(Semibold, 11f * FontBoost);
    public static readonly string IconFontName = HasFont("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    // The icon font at a size, made once and kept for the life of KeyFish (a few sizes in all)
    static readonly System.Collections.Generic.Dictionary<float, Font> iconFonts = new System.Collections.Generic.Dictionary<float, Font>();
    public static Font IconFont(float size)
    {
        Font f;
        if (!iconFonts.TryGetValue(size, out f)) iconFonts[size] = f = new Font(IconFontName, size);
        return f;
    }

    static bool ReadDark()
    {
        if (Mode == 1) return false;
        if (Mode == 2) return true;
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return k != null && k.GetValue("AppsUseLightTheme") is int && (int)k.GetValue("AppsUseLightTheme") == 0;
        }
        catch { return false; }
    }

    static Color ReadAccent()
    {
        if (Holiday) return Color.FromArgb(232, 110, 20);   // pumpkin
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
            {
                object v = k == null ? null : k.GetValue("AccentColor");
                if (v is int) { int abgr = (int)v; return Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF); }
            }
        }
        catch { }
        return Color.FromArgb(0, 103, 192);
    }

    static bool HasFont(string name)
    {
        using (var f = new Font(name, 10f)) return f.Name == name;
    }

    public static Color Blend(Color a, Color b, double t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static readonly Icon AppIcon = LoadIcon();

    static Icon LoadIcon()
    {
        try { using (var s = typeof(Theme).Assembly.GetManifestResourceStream("fish.ico")) return s == null ? null : new Icon(s); }
        catch { return null; }
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

    // Dark title bar and the Mica backdrop behind it (Windows 11; older versions just ignore the attributes)
    public static void Apply(Form f)
    {
        // Every KeyFish window carries the fish in its title bar, not the default WinForms icon
        if (AppIcon != null) f.Icon = AppIcon;
        f.BackColor = Back;
        f.ForeColor = Text;
        f.Font = Body;
        f.HandleCreated += (s, e) =>
        {
            int dark = Dark ? 1 : 0, mica = 2;
            try
            {
                DwmSetWindowAttribute(f.Handle, 20, ref dark, 4);   // DWMWA_USE_IMMERSIVE_DARK_MODE
                DwmSetWindowAttribute(f.Handle, 38, ref mica, 4);   // DWMWA_SYSTEMBACKDROP_TYPE: Mica
            }
            catch { }
        };
    }

    // Scrollbars of lists and panels follow the dark theme
    public static void DarkScrollbars(Control c)
    {
        if (!Dark) return;
        c.HandleCreated += (s, e) => { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
        // A table's scrollbars are separate child controls: they get the dark theme too
        foreach (Control child in c.Controls) if (child is ScrollBar) DarkScrollbars(child);
        c.ControlAdded += (s, e) => { if (e.Control is ScrollBar) DarkScrollbars(e.Control); };
    }

    public static Label Label(string text, Font font = null, Color? color = null)
    {
        return new SmoothLabel { Text = text, Font = font ?? Body, ForeColor = color ?? Text, AutoSize = true, BackColor = Color.Transparent };
    }

    // Text is drawn twice, the second time this fraction of a pixel to the right: a touch heavier than the font's regular
    // weight, closer to how Windows 11 apps look on a dark background
    public const float Heavier = 0.4f;

    // Light text on a dark background needs fuller strokes (contrast 0) to look like Windows 11 apps;
    // dark text on a light one looks right with the usual setting and gets blotchy with the full one
    public static int TextContrastFor(Color text) { return text.GetBrightness() > 0.5f ? 0 : 4; }

    // One line of text with soft grayscale smoothing, like SmoothLabel: centered vertically, cut with "…" if too long
    public static void SmoothText(Graphics g, string text, Font font, RectangleF r, Color color, bool center, bool heavier = true)
    {
        if (HasEmoji(text))
        {
            // The smooth drawing can't take an emoji from another font and leaves an empty square; Windows' own can
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding |
                        (center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
            TextRenderer.DrawText(g, text, font, Rectangle.Round(r), color, flags);
            return;
        }
        var hint = g.TextRenderingHint;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.TextContrast = TextContrastFor(color);
        using (var b = new SolidBrush(color))
        using (var sf = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = center ? StringAlignment.Center : StringAlignment.Near, LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        })
        {
            g.DrawString(text, font, b, r, sf);
            if (heavier) g.DrawString(text, font, b, new RectangleF(r.X + Heavier, r.Y, r.Width, r.Height), sf);
        }
        g.TextRenderingHint = hint;
    }

    // Emoji and other characters beyond the basic plane (🐟) come as two UTF-16 halves
    public static bool HasEmoji(string text)
    {
        if (text != null) foreach (char c in text) if (char.IsSurrogate(c)) return true;
        return false;
    }

    // Labels are made transparent to sit on cards, but Windows smooths text (ClearType) only over a solid color it knows:
    // on a transparent background the letters come out rough. Once the window is built, each label gets the solid color
    // of what it sits on, which looks the same and gives smooth text
    public static void SolidLabels(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is Label && c.BackColor.A < 255)
            {
                Color under = Color.Empty;
                for (Control p = c.Parent; p != null && under.IsEmpty; p = p.Parent)
                    if (p is Card) under = Card;
                    else if (p.BackColor.A == 255) under = p.BackColor;
                if (!under.IsEmpty) c.BackColor = under;
            }
            SolidLabels(c);
        }
    }

    // A label that wraps within the given width
    public static Label Wrapped(string text, int width, Font font = null, Color? color = null)
    {
        var l = new SmoothLabel { Text = text, Font = font ?? Body, ForeColor = color ?? Text, AutoSize = false, BackColor = Color.Transparent };
        // The text is measured at its real width on this screen, and the height kept in 96-DPI pixels like the rest
        // of the layout, so scaling the window doesn't enlarge it a second time
        int real = l.GetPreferredSize(new Size(S(width), 0)).Height;
        l.Size = new Size(width, (int)Math.Ceiling(real / Scale) + 2);
        return l;
    }

    public static DataGridView Grid()
    {
        var g = new DataGridView
        {
            BackgroundColor = Card, BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = CardBorder, RowHeadersVisible = false, AllowUserToResizeRows = false, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
            EnableHeadersVisualStyles = false, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = S(34),
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, Font = Body, EditMode = DataGridViewEditMode.EditOnEnter
        };
        // Only a thin line between rows, no vertical lines
        g.AdvancedCellBorderStyle.Left = g.AdvancedCellBorderStyle.Right = g.AdvancedCellBorderStyle.Top = DataGridViewAdvancedCellBorderStyle.None;
        g.AdvancedCellBorderStyle.Bottom = DataGridViewAdvancedCellBorderStyle.Single;
        g.RowTemplate.Height = S(34);   // the grid doesn't scale its rows with the window
        g.DefaultCellStyle.BackColor = Card;
        g.DefaultCellStyle.ForeColor = Text;
        g.DefaultCellStyle.SelectionBackColor = SelectionBack;
        g.DefaultCellStyle.SelectionForeColor = Text;
        g.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        g.ColumnHeadersDefaultCellStyle.BackColor = Card;
        g.ColumnHeadersDefaultCellStyle.ForeColor = SubText;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Card;
        g.ColumnHeadersDefaultCellStyle.Font = Small;
        g.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        DarkScrollbars(g);
        // Nothing is selected when a table appears: a selected row would look like a choice already made
        g.VisibleChanged += (s, e) => { if (g.Visible) { g.ClearSelection(); g.CurrentCell = null; } };
        g.CellPainting += PaintCheckBox;
        // The grid leaves a one-pixel seam between cells of a row: fill the row first so the seam has the row's color
        g.RowPrePaint += (s, e) =>
        {
            bool sel = (e.State & DataGridViewElementStates.Selected) != 0;
            using (var b = new SolidBrush(sel ? SelectionBack : Card))
                e.Graphics.FillRectangle(b, e.RowBounds.X, e.RowBounds.Y, e.RowBounds.Width, e.RowBounds.Height - 1);
            e.PaintParts &= ~DataGridViewPaintParts.Background;
        };
        // ...and cover the frame line the grid draws on the left edge of each row after the first
        g.RowPostPaint += (s, e) =>
        {
            bool sel = (e.State & DataGridViewElementStates.Selected) != 0;
            using (var p = new Pen(sel ? SelectionBack : Card)) e.Graphics.DrawLine(p, 0, e.RowBounds.Y, 0, e.RowBounds.Bottom - 2);
            using (var p = new Pen(CardBorder)) e.Graphics.DrawLine(p, e.RowBounds.X, e.RowBounds.Bottom - 1, e.RowBounds.Right, e.RowBounds.Bottom - 1);
        };
        return g;
    }

    static void PaintCellBack(DataGridViewCellPaintingEventArgs e)
    {
        bool sel = (e.State & DataGridViewElementStates.Selected) != 0;
        using (var b = new SolidBrush(sel ? e.CellStyle.SelectionBackColor : e.CellStyle.BackColor))
            e.Graphics.FillRectangle(b, e.CellBounds.X, e.CellBounds.Y, e.CellBounds.Width, e.CellBounds.Height - 1);
    }

    // Check boxes in tables drawn like Windows 11 ones: a rounded square, filled with the accent color when checked
    static void PaintCheckBox(object sender, DataGridViewCellPaintingEventArgs e)
    {
        var grid = (DataGridView)sender;
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var g = e.Graphics;
        // Cells draw no borders of their own; the row draws one line under itself (RowPostPaint)
        if (!(grid.Columns[e.ColumnIndex] is DataGridViewComboBoxColumn) && !(grid.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn))
        {
            var cell = new Rectangle(e.CellBounds.X, e.CellBounds.Y, e.CellBounds.Width, e.CellBounds.Height - 1);
            g.SetClip(cell);
            // The background from the grid, the text smoothed like everywhere else in the window
            e.Paint(cell, e.PaintParts & ~DataGridViewPaintParts.Border & ~DataGridViewPaintParts.ContentForeground);
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            var style = e.CellStyle;
            SmoothText(g, Convert.ToString(e.FormattedValue), style.Font ?? grid.Font,
                new RectangleF(cell.X + style.Padding.Left + 2, cell.Y, cell.Width - style.Padding.Horizontal - 4, cell.Height),
                selected ? style.SelectionForeColor : style.ForeColor, false);
            g.ResetClip();
            e.Handled = true;
            return;
        }
        // Drop-down cells: the value and a small chevron, without the classic combo box frame
        if (grid.Columns[e.ColumnIndex] is DataGridViewComboBoxColumn && !(grid.IsCurrentCellInEditMode && grid.CurrentCell == grid[e.ColumnIndex, e.RowIndex]))
        {
            PaintCellBack(e);
            var box = new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y, e.CellBounds.Width - 36, e.CellBounds.Height);
            SmoothText(g, Convert.ToString(e.FormattedValue), e.CellStyle.Font, box, Text, false);
            var icons = IconFont(8f);
                SmoothText(g, "\uE70D", icons, new Rectangle(e.CellBounds.Right - 30, e.CellBounds.Y + 1, 22, e.CellBounds.Height), SubText, true, false);
            e.Handled = true;
            return;
        }
        if (!(grid.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn)) return;
        PaintCellBack(e);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int size = (int)(18 * g.DpiX / 96f);
        var r = new RectangleF(e.CellBounds.X + (e.CellBounds.Width - size) / 2f, e.CellBounds.Y + (e.CellBounds.Height - size) / 2f, size, size);
        bool on = e.Value is bool && (bool)e.Value;
        using (var path = Rounded(r, size / 4.5f))
        {
            if (on) using (var b = new SolidBrush(AccentFill)) g.FillPath(b, path);
            else
            {
                using (var b = new SolidBrush(Input)) g.FillPath(b, path);
                using (var p = new Pen(SubText)) g.DrawPath(p, path);
            }
        }
        if (on)
            using (var p = new Pen(OnAccent, size / 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(r.X + r.Width * 0.25f, r.Y + r.Height * 0.52f), new PointF(r.X + r.Width * 0.43f, r.Y + r.Height * 0.7f), new PointF(r.X + r.Width * 0.76f, r.Y + r.Height * 0.32f) });
        e.Handled = true;
    }

    public static TextBox TextField(string placeholder = null)
    {
        var t = new TextBox { BorderStyle = BorderStyle.None, BackColor = Input, ForeColor = Text, Font = Body };
        if (placeholder != null) t.HandleCreated += (s, e) => SetPlaceholder(t, placeholder);
        return t;
    }

    // A text field in a rounded frame, placed and sized like a button (the frame is what goes on the page)
    public static FieldFrame Framed(TextBox field, int x, int y, int width, int height)
    {
        return new FieldFrame(field) { Location = new Point(x, y), Size = new Size(width, height) };
    }

    // Gray hint text shown while a field is empty
    public static void SetPlaceholder(TextBox t, string text)
    {
        SendMessage(t.Handle, 0x1501, (IntPtr)1, text);   // EM_SETCUEBANNER
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);
}

// On/off switch, like in Windows 11 Settings
class ToggleSwitch : Control
{
    bool on, hover;
    public event EventHandler CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        Size = new Size(44, 22);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    // Narrator reads it as a check box with its title (AccessibleName, set by the settings row) and "checked" or not
    protected override AccessibleObject CreateAccessibilityInstance() { return new Accessible(this); }

    class Accessible : ControlAccessibleObject
    {
        readonly ToggleSwitch owner;
        public Accessible(ToggleSwitch owner) : base(owner) { this.owner = owner; }
        public override AccessibleRole Role { get { return AccessibleRole.CheckButton; } }
        public override AccessibleStates State { get { return base.State | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None); } }
        public override string DefaultAction { get { return owner.Checked ? L.T("Выключить", "Turn off") : L.T("Включить", "Turn on"); } }
        public override void DoDefaultAction() { owner.Checked = !owner.Checked; }
    }

    public bool Checked
    {
        get { return on; }
        set
        {
            if (on == value) return;
            on = value;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e) { Focus(); Checked = !Checked; base.OnClick(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) Checked = !Checked; base.OnKeyDown(e); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float h = Height - 2, w = Width - 2;
        var r = new RectangleF(1, 1, w - 1, h - 1);
        using (var path = Theme.Rounded(r, h / 2 - 0.5f))
        {
            if (on)
                using (var b = new SolidBrush(hover ? Theme.Blend(Theme.AccentFill, Theme.Back, 0.12) : Theme.AccentFill)) g.FillPath(b, path);
            else
            {
                using (var b = new SolidBrush(hover ? Theme.Hover : Theme.Input)) g.FillPath(b, path);
                using (var p = new Pen(Theme.SubText, 1f)) g.DrawPath(p, path);
            }
        }
        float knob = h * (hover ? 0.62f : 0.54f);
        float x = on ? r.Right - h / 2 - knob / 2 : r.X + h / 2 - knob / 2;
        using (var b = new SolidBrush(on ? Theme.OnAccent : Theme.SubText))
            g.FillEllipse(b, x, r.Y + (r.Height - knob) / 2, knob, knob);
        if (Focused && ShowFocusCues)
            using (var p = new Pen(Theme.Text, 1f) { DashStyle = DashStyle.Dot })
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }
}

// A rounded card that groups settings
class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Back;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 6))
        {
            using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
            using (var p = new Pen(Theme.CardBorder)) g.DrawPath(p, path);
        }
    }

    // Child controls sit on the card color, not on the window background
    protected override void OnControlAdded(ControlEventArgs e)
    {
        if (!(e.Control is DataGridView) && !(e.Control is TextBoxBase) && !(e.Control is ComboBox))
            try { e.Control.BackColor = Color.Transparent; } catch (ArgumentException) { e.Control.BackColor = Theme.Card; }
        base.OnControlAdded(e);
    }
}

// A rounded frame around a borderless text box, drawn like the drop-downs: the box's own color inside,
// an accent line at the bottom while it has the focus. A click anywhere in the frame goes to the box
class FieldFrame : Control
{
    public readonly TextBox Field;

    public FieldFrame(TextBox field)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Field = field;
        field.BorderStyle = BorderStyle.None;
        Size = new Size(170, 32);
        Cursor = Cursors.IBeam;
        Controls.Add(field);
        EventHandler redraw = (s, e) => Invalidate();
        field.GotFocus += redraw;
        field.LostFocus += redraw;
        field.BackColorChanged += redraw;
        field.EnabledChanged += redraw;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int padX = Theme.S(8), padY = Theme.S(6);
        if (Field.Multiline) Field.SetBounds(padX, padY, Width - padX - Theme.S(3), Height - padY * 2);
        else Field.SetBounds(padX, (Height - Field.Height) / 2, Width - padX * 2, Field.Height);   // a one-line box keeps its own height
    }

    protected override void OnMouseDown(MouseEventArgs e) { Field.Focus(); base.OnMouseDown(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 4))
        {
            using (var b = new SolidBrush(Field.BackColor)) g.FillPath(b, path);
            using (var p = new Pen(Theme.InputBorder)) g.DrawPath(p, path);
        }
        if (Field.Focused)
            using (var p = new Pen(Theme.AccentFill, 2f)) g.DrawLine(p, 4, Height - 1.5f, Width - 4, Height - 1.5f);
    }
}

// Rounded button: the accent one is for the main action
class RoundButton : Control
{
    bool hover, down;
    public bool AccentStyle;
    public string Glyph;                 // an icon from the system icon font before the text, e.g. a heart
    public Color? GlyphColor;

    public RoundButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Font = Theme.Body;
        Size = new Size(120, 32);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color fill = AccentStyle ? Theme.AccentFill : Theme.Input;
        if (!Enabled) fill = Theme.Blend(fill, Theme.Back, 0.5);
        else if (down) fill = Theme.Blend(fill, Theme.Back, 0.25);
        else if (hover) fill = AccentStyle ? Theme.Blend(fill, Theme.Back, 0.12) : Theme.Hover;
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 4))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            if (!AccentStyle) using (var p = new Pen(Theme.InputBorder)) g.DrawPath(p, path);
            if (Focused && ShowFocusCues) using (var p = new Pen(Theme.Text, 1.5f)) g.DrawPath(p, path);
        }
        Color fore = AccentStyle ? Theme.OnAccent : Theme.Text;
        if (!Enabled) fore = Theme.SubText;
        // Dark text on the accent color: semibold and drawn once is cleaner than the regular weight drawn twice,
        // which smears the edges of dark letters on a bright fill
        Font font = AccentStyle ? Theme.Strong : Font;
        bool heavier = !AccentStyle;
        if (string.IsNullOrEmpty(Glyph))
        {
            Theme.SmoothText(g, Text, font, ClientRectangle, fore, true, heavier);
            return;
        }
        {
            var icon = Theme.IconFont(Font.SizeInPoints);
            // The icon is smoothed the same way as the text, not with ClearType's colored edges
            SizeF iconSize = g.MeasureString(Glyph, icon);
            SizeF textSize = g.MeasureString(Text, font);
            float gap = 2, x = (Width - iconSize.Width - gap - textSize.Width) / 2;
            Theme.SmoothText(g, Glyph, icon, new RectangleF(x, 1, iconSize.Width, Height), GlyphColor ?? fore, true, false);
            Theme.SmoothText(g, Text, font, new RectangleF(x + iconSize.Width + gap, 0, textSize.Width + 4, Height), fore, false, heavier);
        }
    }
}

// The sidebar with sections, each with an icon from the system icon font
class NavList : Control
{
    public readonly System.Collections.Generic.List<Tuple<string, string>> Items = new System.Collections.Generic.List<Tuple<string, string>>();
    int selected, hover = -1;
    public event EventHandler SelectedChanged;
    readonly Font icons = Theme.IconFont(12f);
    public int ItemHeight = Theme.S(40);

    public NavList()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Back;
        Font = Theme.Body;
        TabStop = true;
        AccessibleRole = AccessibleRole.PageTabList;
    }

    public int Selected
    {
        get { return selected; }
        set
        {
            if (selected == value) return;
            selected = value;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.Selection, selected);
            AccessibilityNotifyClients(AccessibleEvents.Focus, selected);
            if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
        }
    }

    // Narrator reads the sections as tabs, with their names and which one is open
    protected override AccessibleObject CreateAccessibilityInstance() { return new Accessible(this); }

    class Accessible : ControlAccessibleObject
    {
        readonly NavList owner;
        public Accessible(NavList owner) : base(owner) { this.owner = owner; }
        public override AccessibleRole Role { get { return AccessibleRole.PageTabList; } }
        public override int GetChildCount() { return owner.Items.Count; }
        public override AccessibleObject GetChild(int index) { return index >= 0 && index < owner.Items.Count ? new Item(this, owner, index) : null; }
        public override AccessibleObject GetFocused() { return owner.Focused ? GetChild(owner.selected) : null; }
        public override AccessibleObject GetSelected() { return GetChild(owner.selected); }
    }

    class Item : AccessibleObject
    {
        readonly AccessibleObject parent;
        readonly NavList owner;
        readonly int index;
        public Item(AccessibleObject parent, NavList owner, int index) { this.parent = parent; this.owner = owner; this.index = index; }
        public override AccessibleObject Parent { get { return parent; } }
        public override string Name { get { return owner.Items[index].Item2; } }
        public override AccessibleRole Role { get { return AccessibleRole.PageTab; } }
        public override AccessibleStates State
        {
            get
            {
                var s = AccessibleStates.Selectable | AccessibleStates.Focusable;
                if (index == owner.selected) s |= AccessibleStates.Selected | (owner.Focused ? AccessibleStates.Focused : 0);
                return s;
            }
        }
        public override Rectangle Bounds { get { return owner.RectangleToScreen(new Rectangle(0, index * owner.ItemHeight, owner.Width, owner.ItemHeight)); } }
        public override void DoDefaultAction() { owner.Selected = index; }
        public override string DefaultAction { get { return L.T("Открыть", "Open"); } }
    }

    int At(Point p) { int i = p.Y / ItemHeight; return i >= 0 && i < Items.Count ? i : -1; }
    protected override void OnMouseMove(MouseEventArgs e) { int i = At(e.Location); if (i != hover) { hover = i; Invalidate(); } Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { int i = At(e.Location); if (i >= 0) { Focus(); Selected = i; } base.OnMouseDown(e); }
    protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && selected < Items.Count - 1) Selected = selected + 1;
        if (e.KeyCode == Keys.Up && selected > 0) Selected = selected - 1;
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        for (int i = 0; i < Items.Count; i++)
        {
            var r = new Rectangle(4, i * ItemHeight + 2, Width - 8, ItemHeight - 4);
            if (i == selected || i == hover)
                using (var path = Theme.Rounded(r, 4))
                using (var b = new SolidBrush(i == selected ? Theme.Selected : Theme.Hover)) g.FillPath(b, path);
            if (i == selected)
                using (var path = Theme.Rounded(new RectangleF(r.X, r.Y + r.Height / 2 - 8, 3, 16), 1.5f))
                using (var b = new SolidBrush(Theme.AccentFill)) g.FillPath(b, path);
            Color under = i == selected ? Theme.Selected : i == hover ? Theme.Hover : BackColor;
            Theme.SmoothText(g, Items[i].Item1, icons, new Rectangle(r.X + 10, r.Y + 1, 28, r.Height), Theme.Text, true, false);
            Theme.SmoothText(g, Items[i].Item2, Font, new Rectangle(r.X + 44, r.Y, r.Width - 48, r.Height), Theme.Text, false);
            if (Focused && ShowFocusCues && i == selected)
                using (var p = new Pen(Theme.Text) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, r);
        }
    }
}

// Menus in the theme colors with rounded corners (Windows 11 rounds popup windows on request)
class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    public ThemedMenuRenderer() : base(new ThemedColors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Smoothed like the rest of KeyFish's text, not with ClearType's thinner colored letters
        string text = (e.Text ?? "").Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&");
        Theme.SmoothText(e.Graphics, text, e.TextFont, e.TextRectangle, e.Item.Enabled ? Theme.Text : Theme.SubText, false);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.Rounded(new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2), 4))
        using (var b = new SolidBrush(Theme.Hover)) g.FillPath(b, path);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        using (var p = new Pen(Theme.AccentFill, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            g.DrawLines(p, new[] { new PointF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.52f), new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.74f), new PointF(r.X + r.Width * 0.82f, r.Y + r.Height * 0.3f) });
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.SubText;
        base.OnRenderArrow(e);
    }

    class ThemedColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color MenuBorder { get { return Theme.InputBorder; } }
        public override Color MenuItemBorder { get { return Color.Transparent; } }
        public override Color MenuItemSelected { get { return Theme.Hover; } }
        public override Color SeparatorDark { get { return Theme.CardBorder; } }
        public override Color SeparatorLight { get { return Theme.CardBorder; } }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // Apply to a menu: colors, padding, rounded corners
    public static void Apply(ToolStripDropDown menu)
    {
        menu.Renderer = new ThemedMenuRenderer();
        menu.Font = Theme.Body;
        menu.BackColor = Theme.Card;
        menu.ForeColor = Theme.Text;
        menu.Padding = new Padding(0, 4, 0, 4);
        menu.Opening += (s, e) => { try { int round = 2; DwmSetWindowAttribute(menu.Handle, 33, ref round, 4); } catch { } };   // DWMWA_WINDOW_CORNER_PREFERENCE
        menu.ItemAdded += (s, e) => { e.Item.Padding = new Padding(4, 5, 4, 5); e.Item.ForeColor = Theme.Text; };
    }
}

// Drop-down list like in Windows 11: a rounded field with a chevron and a themed menu.
// Editable: the field is a text box, and the menu offers suggestions
class DropDown : Control
{
    public readonly System.Collections.Generic.List<string> Items = new System.Collections.Generic.List<string>();
    readonly TextBox edit;
    bool hover, open;
    int selected = -1;
    public event EventHandler SelectedIndexChanged;
    readonly Font chevron = Theme.IconFont(8f);

    public DropDown(bool editable = false, string placeholder = null)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Font = Theme.Body;
        Size = new Size(200, 32);
        TabStop = !editable;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ComboBox;
        if (editable)
        {
            edit = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = Theme.Body };
            if (placeholder != null) edit.HandleCreated += (s, e) => Theme.SetPlaceholder(edit, placeholder);
            edit.TextChanged += (s, e) => OnTextChanged(e);
            edit.KeyDown += (s, e) => { if (e.KeyCode == Keys.Down) { ShowMenu(); e.Handled = true; } };
            edit.Cursor = Cursors.IBeam;
            Controls.Add(edit);
        }
    }

    // Narrator reads the field's title (AccessibleName, set by the settings row) and the chosen value
    protected override AccessibleObject CreateAccessibilityInstance() { return new Accessible(this); }

    class Accessible : ControlAccessibleObject
    {
        readonly DropDown owner;
        public Accessible(DropDown owner) : base(owner) { this.owner = owner; }
        public override AccessibleRole Role { get { return AccessibleRole.ComboBox; } }
        public override string Value { get { return owner.Text; } }
    }

    public override string Text
    {
        get { return edit != null ? edit.Text : (selected >= 0 && selected < Items.Count ? Items[selected] : ""); }
        set { if (edit != null) edit.Text = value; else SelectedIndex = Items.IndexOf(value); }
    }

    public int SelectedIndex
    {
        get { return selected; }
        set
        {
            if (selected == value) return;
            selected = value;
            if (edit != null && value >= 0) edit.Text = Items[value];
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }
    }

    public string SelectedItem { get { return selected >= 0 && selected < Items.Count ? Items[selected] : null; } }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (edit != null)
        {
            int pad = (int)(10 * DeviceDpi / 96f);
            edit.Bounds = new Rectangle(pad, (Height - edit.PreferredHeight) / 2, Width - pad - Height, edit.PreferredHeight);
        }
        base.OnLayout(e);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { if (edit != null) edit.Focus(); Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowMenu(); base.OnMouseDown(e); }
    protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && e.Alt || e.KeyCode == Keys.Space || e.KeyCode == Keys.F4) ShowMenu();
        else if (e.KeyCode == Keys.Down && selected < Items.Count - 1) SelectedIndex = selected + 1;
        else if (e.KeyCode == Keys.Up && selected > 0) SelectedIndex = selected - 1;
        base.OnKeyDown(e);
    }

    void ShowMenu()
    {
        if (Items.Count == 0) return;
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, MaximumSize = new Size(Width + 200, 420) };
        ThemedMenuRenderer.Apply(menu);
        for (int i = 0; i < Items.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(Items[i]) { Checked = edit == null && i == selected };
            item.Click += (s, e) => { SelectedIndex = index; if (edit != null) { edit.Text = Items[index]; edit.SelectAll(); edit.Focus(); } };
            menu.Items.Add(item);
        }
        menu.MinimumSize = new Size(Width, 0);
        menu.Closed += (s, e) => { open = false; Invalidate(); BeginInvoke(new Action(menu.Dispose)); };
        open = true;
        Invalidate();
        menu.Show(this, new Point(0, Height + 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool focused = Focused || (edit != null && edit.Focused);
        Color fill = hover && edit == null ? Theme.Hover : Theme.Input;
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 4))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            using (var p = new Pen(Theme.InputBorder)) g.DrawPath(p, path);
        }
        // An accent line at the bottom while active, like Windows 11 fields
        if (focused || open)
            using (var p = new Pen(Theme.AccentFill, 2f)) g.DrawLine(p, 4, Height - 1.5f, Width - 4, Height - 1.5f);
        if (edit == null)
            Theme.SmoothText(g, Text, Font, new Rectangle(8, 0, Width - 8 - Height, Height), Theme.Text, false);
        Theme.SmoothText(g, "\uE70D", chevron, new Rectangle(Width - Height, 1, Height - 4, Height), Theme.SubText, true, false);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
// The layout next to the text caret (or the mouse pointer, where an app has no caret Windows can see):
// a small "EN" / "RU" tag that shows for a moment when the layout changes. It never takes focus and lets
// clicks through. Drawn into a per-pixel transparent window, so the rounded edges are smooth
class LayoutBadge : Form
{
    readonly Timer timer = new Timer { Interval = 30 };
    int shownAt;
    Bitmap picture;
    Point where;

    public LayoutBadge()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        timer.Tick += (s, e) => Step();
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // layered, click-through, no taskbar button, topmost, never activated
            cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8 | 0x08000000;
            return cp;
        }
    }

    public void Flash(string text, Color fill)
    {
        if (picture != null) picture.Dispose();
        picture = Render(text, fill);
        where = Place(picture.Size);
        shownAt = Environment.TickCount;
        Draw(255);
        Native.ShowWindow(Handle, 4);   // SW_SHOWNOACTIVATE
        Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // topmost, no move/size/activate
        timer.Start();
    }

    // Visible for a second, then fades out
    void Step()
    {
        int t = Environment.TickCount - shownAt;
        if (t < 1000) return;
        if (t >= 1300) { timer.Stop(); Native.ShowWindow(Handle, 0); return; }
        Draw((byte)(255 * (1300 - t) / 300));
    }

    static Bitmap Render(string text, Color fill)
    {
        float scale;
        using (var screen = Graphics.FromHwnd(IntPtr.Zero)) scale = screen.DpiX / 96f;
        using (var font = new Font("Segoe UI Semibold", 12f * scale, GraphicsUnit.Pixel))
        {
            Size measured = TextRenderer.MeasureText(text, font);
            int w = measured.Width + (int)(4 * scale), h = (int)(18 * scale);
            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, w - 1, h - 1), 4 * scale))
                using (var b = new SolidBrush(fill))
                    g.FillPath(b, path);
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var ink = new SolidBrush(LetterIcon.InkOn(fill)))
                    g.DrawString(text, font, ink, new RectangleF(0, 0, w, h + 1), sf);
            }
            return bmp;
        }
    }

    // Under the caret if the focused app shows one, otherwise next to the mouse pointer
    static Point Place(Size size)
    {
        IntPtr fg = W.GetForegroundWindow();
        uint pid;
        var info = new W.GUITHREADINFO();
        info.cbSize = Marshal.SizeOf(info);
        if (fg != IntPtr.Zero && W.GetGUIThreadInfo(W.GetWindowThreadProcessId(fg, out pid), ref info) && info.hwndCaret != IntPtr.Zero)
        {
            var p = new Native.POINT { x = info.rcCaret.left, y = info.rcCaret.bottom };
            if (Native.ClientToScreen(info.hwndCaret, ref p) && (p.x != 0 || p.y != 0)) return Fit(new Point(p.x, p.y + 3), size);
        }
        // Browsers and Electron apps (Chrome, Edge, Telegram, VS Code…) draw their own caret and don't show the one above,
        // but they report it to accessibility tools. Not asked of a hung window: the question would wait for its answer
        // The question goes to the app itself, so it is asked from a helper thread: an app busy for a while (not yet hung)
        // would hold KeyFish's window thread — its hotkeys and tray — for as long. No answer in 150 ms: the mouse it is
        IntPtr target = info.hwndFocus != IntPtr.Zero ? info.hwndFocus : fg;
        if (fg != IntPtr.Zero && !Native.IsHungAppWindow(fg))
        {
            Rectangle found = Rectangle.Empty;
            var ask = new System.Threading.Thread(() => { Rectangle r; if (AccessibleCaret(target, out r)) found = r; }) { IsBackground = true };
            ask.SetApartmentState(System.Threading.ApartmentState.MTA);
            ask.Start();
            if (ask.Join(150) && !found.IsEmpty) return Fit(new Point(found.Left, found.Bottom + 3), size);
        }
        var c = Cursor.Position;
        return Fit(new Point(c.X + 14, c.Y + 20), size);
    }

    // The caret's place on screen from the window's accessibility object (MSAA, OBJID_CARET)
    static bool AccessibleCaret(IntPtr hwnd, out Rectangle caret)
    {
        caret = Rectangle.Empty;
        try
        {
            Guid iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");   // IAccessible
            object obj;
            if (Native.AccessibleObjectFromWindow(hwnd, 0xFFFFFFF8, ref iid, out obj) != 0 || obj == null) return false;
            int x, y, w, h;
            ((Accessibility.IAccessible)obj).accLocation(out x, out y, out w, out h, 0);
            Marshal.ReleaseComObject(obj);
            if ((x == 0 && y == 0) || h <= 0) return false;   // no caret right now
            caret = new Rectangle(x, y, Math.Max(w, 1), h);
            return true;
        }
        catch { return false; }
    }

    static Point Fit(Point p, Size size)
    {
        var area = Screen.FromPoint(p).WorkingArea;
        return new Point(Math.Max(area.Left, Math.Min(p.X, area.Right - size.Width)), Math.Max(area.Top, Math.Min(p.Y, area.Bottom - size.Height)));
    }

    void Draw(byte alpha)
    {
        IntPtr screen = Native.GetDC(IntPtr.Zero);
        IntPtr mem = Native.CreateCompatibleDC(screen);
        IntPtr bitmap = picture.GetHbitmap(Color.FromArgb(0));
        IntPtr old = Native.SelectObject(mem, bitmap);
        try
        {
            var size = new Native.SIZE { cx = picture.Width, cy = picture.Height };
            var pos = new Native.POINT { x = where.X, y = where.Y };
            var src = new Native.POINT();
            var blend = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };   // AC_SRC_ALPHA
            Native.UpdateLayeredWindow(Handle, screen, ref pos, ref size, mem, ref src, 0, ref blend, 2);          // ULW_ALPHA
        }
        finally
        {
            Native.SelectObject(mem, old);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); if (picture != null) picture.Dispose(); }
        base.Dispose(disposing);
    }
}

// A label drawn with soft grayscale smoothing, the way Windows 11's own apps draw text. The classic way (GDI ClearType)
// makes thin, jagged letters on a dark background. Measured and drawn by the same engine, so wrapping stays right
class SmoothLabel : Label
{
    public SmoothLabel() { UseCompatibleTextRendering = true; }

    // A text with an emoji is drawn by Windows' own text drawing: the smooth one shows an empty square instead of 🐟
    protected override void OnTextChanged(EventArgs e)
    {
        UseCompatibleTextRendering = !Theme.HasEmoji(Text);
        base.OnTextChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!UseCompatibleTextRendering) { base.OnPaint(e); return; }
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        e.Graphics.TextContrast = Theme.TextContrastFor(ForeColor);
        base.OnPaint(e);
        e.Graphics.TranslateTransform(Theme.Heavier, 0);   // a touch heavier, see Theme.Heavier
        base.OnPaint(e);
        e.Graphics.ResetTransform();
    }
}

// KeyFish's messages in the same style as its windows (the system message box stays light in the dark theme).
// Used like MessageBox.Show: the same buttons and icons, the same results
class ThemedMessage : Form
{
    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        return Show(null, text, caption, buttons, icon);
    }

    public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        using (var m = new ThemedMessage(text, caption, buttons, icon, null))
        {
            if (owner == null) { m.TopMost = true; m.StartPosition = FormStartPosition.CenterScreen; }
            return m.ShowDialog(owner);
        }
    }

    // A message with buttons of its own; returns the index of the one clicked, the last one for Esc or closing
    public static int Choose(string text, string caption, MessageBoxIcon icon, params string[] buttons)
    {
        using (var m = new ThemedMessage(text, caption, MessageBoxButtons.OK, icon, buttons))
        {
            m.TopMost = true;
            m.StartPosition = FormStartPosition.CenterScreen;
            m.ShowDialog();
            return m.chosen < 0 ? buttons.Length - 1 : m.chosen;
        }
    }

    int chosen = -1;

    ThemedMessage(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, string[] own)
    {
        Text = caption;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        Theme.Apply(this);

        const int width = 460, pad = 24, iconSize = 32;
        string glyph = icon == MessageBoxIcon.Warning ? "\uE7BA" : icon == MessageBoxIcon.Question ? "\uE9CE" : "\uE946";
        Color tint = icon == MessageBoxIcon.Warning ? Color.FromArgb(252, 185, 0) : Theme.AccentFill;
        var mark = new Label
        {
            Text = glyph, Font = Theme.IconFont(20f), ForeColor = tint, BackColor = Theme.Back, AutoSize = false,
            Location = new Point(pad, pad - 2), Size = new Size(iconSize, iconSize), TextAlign = ContentAlignment.MiddleCenter
        };
        var message = Theme.Wrapped(text, width - pad * 3 - iconSize);
        message.Location = new Point(pad * 2 + iconSize, pad);
        Controls.Add(mark);
        Controls.Add(message);

        var choices = new System.Collections.Generic.List<Tuple<string, DialogResult>>();
        if (own != null)
            foreach (string label in own) choices.Add(Tuple.Create(label, (DialogResult)(100 + choices.Count)));
        else if (buttons == MessageBoxButtons.YesNo || buttons == MessageBoxButtons.YesNoCancel)
        {
            choices.Add(Tuple.Create(L.T("Да", "Yes"), DialogResult.Yes));
            choices.Add(Tuple.Create(L.T("Нет", "No"), DialogResult.No));
            if (buttons == MessageBoxButtons.YesNoCancel) choices.Add(Tuple.Create(L.T("Отмена", "Cancel"), DialogResult.Cancel));
        }
        else choices.Add(Tuple.Create("OK", DialogResult.OK));

        int y = Math.Max(message.Bottom, pad + iconSize) + pad;
        int x = width - pad;
        for (int i = choices.Count - 1; i >= 0; i--)
        {
            var c = choices[i];
            int w = Math.Max(104, TextRenderer.MeasureText(c.Item1, Theme.Body).Width + 28);
            var b = new RoundButton { Text = c.Item1, AccentStyle = i == 0, Size = new Size(w, 34) };
            x -= b.Width;
            b.Location = new Point(x, y);
            x -= 8;
            var result = c.Item2;
            int index = i;
            b.Click += (s, e) => { chosen = own != null ? index : -1; DialogResult = result; Close(); };
            Controls.Add(b);
            if (i == 0) ActiveControl = b;
        }
        // Esc answers like the system box: Cancel, or No where there is no Cancel, or OK
        DialogResult escape = buttons == MessageBoxButtons.YesNoCancel ? DialogResult.Cancel : buttons == MessageBoxButtons.YesNo ? DialogResult.No : DialogResult.OK;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = escape; Close(); } };
        ClientSize = new Size(width, y + 34 + pad);
        Theme.SolidLabels(this);
        Theme.ScaleWindow(this);
    }
}

// The layout's letters in a circle (EN, RU…): the tray icon when chosen in Settings instead of the fish
static class LetterIcon
{
    // Blue for Latin and orange for another alphabet; on Halloween, purple and pumpkin
    public static Color Latin { get { return Theme.Holiday ? Color.FromArgb(124, 70, 200) : Color.FromArgb(40, 110, 220); } }
    public static Color Other { get { return Theme.Holiday ? Color.FromArgb(240, 120, 20) : Color.FromArgb(232, 112, 30); } }
    public static readonly Color Off = Color.FromArgb(150, 150, 150);
    static readonly Color DarkInk = Color.FromArgb(32, 32, 32);

    // White letters as before, unless the color is so light that white barely stands out on it (white, pale yellow…):
    // then dark ones. By the WCAG contrast ratio; it leans to white, as the default colors look best with it
    public static Color InkOn(Color fill)
    {
        return 1.05 / (Luminance(fill) + 0.05) >= 2.3 ? Color.White : DarkInk;
    }

    static double Luminance(Color c)
    {
        Func<int, double> lin = v => { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); };
        return 0.2126 * lin(c.R) + 0.7152 * lin(c.G) + 0.0722 * lin(c.B);
    }

    // A light circle gets a thin darker rim, so it doesn't melt into a light taskbar or window
    static bool Light(Color fill) { return InkOn(fill) != Color.White; }

    public static Bitmap Render(string text, Color fill, Color ink, int size)
    {
        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp)) Draw(g, new RectangleF(0, 0, size, size), text, fill, ink);
        return bmp;
    }

    public static void Draw(Graphics g, RectangleF r, string text, Color fill, Color ink)
    {
        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using (var b = new SolidBrush(fill)) g.FillEllipse(b, r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
        if (Light(fill))
        {
            float w = Math.Max(1, r.Width / 16);
            using (var p = new Pen(Theme.Blend(fill, Color.Black, 0.35), w))
                g.DrawEllipse(p, r.X + w / 2 + 0.5f, r.Y + w / 2 + 0.5f, r.Width - w - 1, r.Height - w - 1);
        }
        if (!string.IsNullOrEmpty(text))
            // The letters as a shape, centered by their real outline: centered by the font's metrics they sit low
            using (var path = new GraphicsPath())
            using (var family = new FontFamily("Segoe UI"))
            {
                path.AddString(text, family, (int)FontStyle.Bold, 100, PointF.Empty, StringFormat.GenericTypographic);
                var bounds = path.GetBounds();
                float scale = Math.Min(r.Width * (text.Length > 2 ? 0.66f : 0.58f) / bounds.Width, r.Height * 0.40f / bounds.Height);
                using (var m = new Matrix())
                {
                    m.Translate(-(bounds.X + bounds.Width / 2), -(bounds.Y + bounds.Height / 2), MatrixOrder.Append);
                    m.Scale(scale, scale, MatrixOrder.Append);
                    m.Translate(r.X + r.Width / 2, r.Y + r.Height / 2, MatrixOrder.Append);
                    path.Transform(m);
                }
                using (var b = new SolidBrush(ink)) g.FillPath(b, path);
            }
        g.Restore(saved);
    }
}

// A small looping picture next to a setting that shows what it does: a word typed with a mistake and fixed, the tray
// icon following the layout and so on. Every story ends the way it began, so the loop has no seam. It plays while its
// page is on screen (and from the start when the switch is flipped); hidden, it stops
class FeaturePreview : Control
{
    readonly Action<Graphics, RectangleF, int> paint;   // the picture at a moment of the story, ms from its start
    readonly int length;
    readonly Timer timer = new Timer { Interval = 33 };
    int started, ms;

    public FeaturePreview(Action<Graphics, RectangleF, int> paint, int length)
    {
        this.paint = paint;
        this.length = length;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(150, 40);
        AccessibleRole = AccessibleRole.Graphic;
        timer.Tick += (s, e) => Step();
    }

    // Plays the story from the start, after a delay (the pictures of a page start one after another), then loops
    public void Play(int delay = 0)
    {
        started = Environment.TickCount + delay;
        ms = 0;
        timer.Start();
        Invalidate();
    }

    void Step()
    {
        if (!Visible) { timer.Stop(); return; }   // the page or the window was hidden; showing the page starts it again
        ms = Environment.TickCount - started;   // negative while the delay lasts
        if (ms >= length) { started += length * (ms / length); ms %= length; }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        paint(e.Graphics, new RectangleF(0, 0, Width, Height), Math.Max(0, ms));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}

// The stories of the pictures and what they are made of: a small text field, tray icons, a key being pressed,
// the layout tag under a caret
static class Pictures
{
    static Font font, keyFont;
    static Font Font { get { return font ?? (font = new Font(Theme.Small.FontFamily, Theme.Small.Size * 0.95f)); } }
    static Font KeyFont { get { return keyFont ?? (keyFont = new Font(Theme.Small.FontFamily, Theme.Small.Size * 0.85f)); } }

    const int Letter = 110, Start = 400;   // ms per typed letter; the empty field before the first one

    // 0 before `from`, 1 after `from + span`, eased in between
    static float Ease(int ms, int from, int span)
    {
        float t = Math.Max(0, Math.Min(1, (ms - from) / (float)span));
        return t < 0.5f ? 2 * t * t : 1 - (float)Math.Pow(-2 * t + 2, 2) / 2;
    }

    // A key held from `at` for `span` ms: fades in, stays, fades out
    static float Held(int ms, int at, int span) { return Math.Min(Ease(ms, at, 120), 1 - Ease(ms, at + span - 150, 150)); }

    // The caret blinks while nothing happens and stays on while typing
    static bool CaretOn(int ms, bool typing) { return typing || (ms / 500) % 2 == 0; }

    // ---------- a word typed with a mistake ----------

    static int FixAt(string typed) { return Start + typed.Length * Letter + 300; }

    // How long the typing story lasts: typing, the fix, (Ctrl+Z putting the word back), a look, selecting, deleting
    public static int TypingLength(string typed, bool undo) { return FixAt(typed) + (undo ? 1500 : 0) + 3000; }

    // The word is typed letter by letter, KeyFish fixes it (it flashes in the accent color), with undo Ctrl+Z puts
    // the typed word back; then the word is selected and deleted, and the field is empty again, as at the start
    public static void Typing(Graphics g, RectangleF r, int ms, string typed, string fixedWord, bool undo, string undoKey = "Ctrl+Z")
    {
        var box = Box(g, r);
        var inner = new RectangleF(box.X + Theme.S(5), box.Y, box.Width - Theme.S(10), box.Height);
        int fixAt = FixAt(typed), undoAt = fixAt + 1100, end = TypingLength(typed, undo);
        int selectAt = end - 1700, deleteAt = end - 1000;

        int letters = ms < Start ? 0 : Math.Min(typed.Length, (ms - Start) / Letter + 1);
        string sofar = typed.Substring(0, letters);
        float mix = Ease(ms, fixAt, 350) - (undo ? Ease(ms, undoAt + 250, 300) : 0);   // 0: the typed word, 1: the fixed one
        var fixedColor = Theme.Blend(Theme.AccentFill, Theme.Text, Ease(ms, fixAt + 500, 600));
        string word = ms >= deleteAt ? "" : mix > 0.5f ? fixedWord : sofar;
        float wordWidth = Width(g, word);

        float sel = ms >= deleteAt ? 0 : Ease(ms, selectAt, 300);   // the selection grows from the end to the start
        if (sel > 0)
            using (var b = new SolidBrush(Theme.SelectionBack))
                g.FillRectangle(b, inner.X + wordWidth * (1 - sel), box.Y + box.Height * 0.2f, wordWidth * sel + Theme.S(1), box.Height * 0.6f);

        if (ms < deleteAt)
        {
            if (mix <= 0) Text(g, sofar, inner, 1, Theme.Text);
            else if (mix >= 1) Text(g, fixedWord, inner, 1, fixedColor);
            else { Text(g, sofar, inner, 1 - mix, Theme.Text); Text(g, fixedWord, inner, mix, fixedColor); }
        }
        if (undo) Key(g, box, undoKey, Held(ms, undoAt, 650));
        if (sel <= 0 && CaretOn(ms, ms >= Start && ms < fixAt)) Caret(g, box, inner.X + wordWidth);
    }

    // ---------- the tray icon following the layout ----------

    public const int SwitchLength = 4800;

    // The layout is switched there and back: the key combination shows, the icon changes, and later back again
    public static void Switching(Graphics g, RectangleF r, int ms, Func<Image> first, Func<Image> second)
    {
        var box = Box(g, r);
        float t = Ease(ms, 1100, 350) - Ease(ms, 3500, 350);
        float s = box.Height - Theme.S(5), cx = box.X + box.Width * 0.3f;
        var dest = new RectangleF(cx - s / 2, box.Y + (box.Height - s) / 2, s, s);
        if (t < 1) Draw(g, first(), dest, 1 - t);
        if (t > 0) Draw(g, second(), dest, t);
        Key(g, box, "Alt+Shift", Math.Max(Held(ms, 900, 700), Held(ms, 3300, 700)));
    }

    static void Draw(Graphics g, Image image, RectangleF dest, float alpha)
    {
        if (alpha >= 1) { g.DrawImage(image, dest); return; }
        using (var attr = new System.Drawing.Imaging.ImageAttributes())
        {
            attr.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = Math.Max(0, alpha) });
            g.DrawImage(image, Rectangle.Round(dest), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attr);
        }
    }

    public static Image Fish(bool other)
    {
        using (var s = typeof(Pictures).Assembly.GetManifestResourceStream(other ? "fish-ru.ico" : "fish.ico"))
        using (var icon = new Icon(s, 32, 32))
            return icon.ToBitmap();
    }

    public static Image Letters(string text, Color fill) { return LetterIcon.Render(text, fill, LetterIcon.InkOn(fill), 32); }

    // ---------- the layout tag at the caret ----------

    // The layout is switched there and back while the caret waits after a word; each time the tag pops up under it
    public static void Badge(Graphics g, RectangleF r, int ms, string word, string firstTag, Color firstColor, string secondTag, Color secondColor)
    {
        var box = Box(g, r);
        var inner = new RectangleF(box.X + Theme.S(5), box.Y, box.Width - Theme.S(10), box.Height);
        Text(g, word, inner, 1, Theme.Text);
        float x = inner.X + Width(g, word);
        Caret(g, box, x);
        Key(g, box, "Alt+Shift", Math.Max(Held(ms, 900, 700), Held(ms, 3300, 700)));
        bool second = ms >= 2400;
        int at = second ? 3500 : 1100;
        float t = Math.Min(Ease(ms, at, 250), 1 - Ease(ms, at + 1000, 300));
        if (t <= 0) return;
        string text = second ? secondTag : firstTag;
        Color fill = second ? secondColor : firstColor;
        float w = Math.Max(Theme.S(22), g.MeasureString(text, KeyFont).Width + Theme.S(2)), h = Theme.S(13);
        var tag = new RectangleF(x - Theme.S(1), box.Bottom - Theme.S(5) - h * (1 - t) * 0.3f, w, h);
        int a = (int)(255 * t);
        using (var path = Theme.Rounded(tag, Theme.S(3)))
        using (var b = new SolidBrush(Color.FromArgb(a, fill))) g.FillPath(b, path);
        using (var f = new Font(Theme.Small.FontFamily, Math.Max(1, Theme.Small.Size * 0.72f), FontStyle.Bold))
            Theme.SmoothText(g, text, f, tag, Color.FromArgb(a, LetterIcon.InkOn(fill)), true, false);
    }

    public const int BadgeLength = 4800;

    // ---------- parts ----------

    static void Text(Graphics g, string text, RectangleF r, float alpha, Color color)
    {
        if (text.Length == 0) return;
        Theme.SmoothText(g, text, Font, r, Color.FromArgb((int)(255 * Math.Max(0, Math.Min(1, alpha))), color), false, false);
    }

    // Where the caret goes after the text (MeasureString pads the text a little on both sides)
    static float Width(Graphics g, string text)
    {
        return text.Length == 0 ? 0 : Math.Max(0, g.MeasureString(text, Font).Width - Theme.S(4));
    }

    static void Caret(Graphics g, RectangleF box, float x)
    {
        using (var p = new Pen(Theme.Text, 1)) g.DrawLine(p, x + Theme.S(1), box.Y + box.Height * 0.25f, x + Theme.S(1), box.Bottom - box.Height * 0.25f);
    }

    // A key combination at the right end of the frame, shown while it is "pressed"
    static void Key(Graphics g, RectangleF box, string text, float alpha)
    {
        if (alpha <= 0) return;
        var size = g.MeasureString(text, KeyFont);
        float h = box.Height - Theme.S(8), w = size.Width + Theme.S(4);
        var key = new RectangleF(box.Right - w - Theme.S(4), box.Y + (box.Height - h) / 2, w, h);
        int a = (int)(255 * Math.Min(1, alpha));
        using (var path = Theme.Rounded(key, Theme.S(3)))
        {
            using (var b = new SolidBrush(Color.FromArgb(a, Theme.Hover))) g.FillPath(b, path);
            using (var p = new Pen(Color.FromArgb(a, Theme.InputBorder))) g.DrawPath(p, path);
        }
        Theme.SmoothText(g, text, KeyFont, key, Color.FromArgb(a, Theme.SubText), true, false);
    }

    // The frame every picture sits in: the same size in every row, so the pictures line up as one column
    public static RectangleF Box(Graphics g, RectangleF r)
    {
        var box = new RectangleF(r.X + 0.5f, r.Y + r.Height * 0.18f, r.Width - 1, r.Height * 0.64f);
        using (var path = Theme.Rounded(box, Theme.S(4)))
        {
            using (var b = new SolidBrush(Theme.Input)) g.FillPath(b, path);
            using (var p = new Pen(Theme.InputBorder)) g.DrawPath(p, path);
        }
        return box;
    }
}

// A color chosen for a layout ("EN", "RU"…): the tray fish, the letters and the layout tag use it.
// Kept in the registry as one text value: "EN=#2870DC;RU=#D83B3B"
static class LayoutColors
{
    // The palette offered in Settings, in the spirit of Windows 11's accent colors
    public static readonly Color[] Palette =
    {
        Color.FromArgb(40, 110, 220), Color.FromArgb(232, 112, 30), Color.FromArgb(216, 59, 59), Color.FromArgb(214, 65, 140),
        Color.FromArgb(142, 68, 201), Color.FromArgb(91, 95, 199), Color.FromArgb(15, 157, 154), Color.FromArgb(46, 155, 78),
        Color.FromArgb(125, 170, 30), Color.FromArgb(227, 169, 11), Color.FromArgb(141, 110, 99), Color.FromArgb(95, 107, 122)
    };

    public static Dictionary<string, Color> Parse(string text)
    {
        var map = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in (text ?? "").Split(';'))
        {
            int eq = part.IndexOf('=');
            int rgb;
            if (eq > 0 && part.Length > eq + 1 && part[eq + 1] == '#' &&
                int.TryParse(part.Substring(eq + 2), System.Globalization.NumberStyles.HexNumber, null, out rgb))
                map[part.Substring(0, eq).Trim()] = Color.FromArgb(255, Color.FromArgb(rgb));
        }
        return map;
    }

    public static string Format(Dictionary<string, Color> map)
    {
        var parts = new List<string>();
        foreach (var kv in map) parts.Add(kv.Key + "=#" + (kv.Value.ToArgb() & 0xFFFFFF).ToString("X6"));
        return string.Join(";", parts);
    }

    // The fish icon in another color: the colored disc takes the new color; the white fish stays white,
    // or turns dark on a light color
    public static Bitmap Recolor(Bitmap source, Color target)
    {
        var result = new Bitmap(source.Width, source.Height);
        var ink = LetterIcon.InkOn(target);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                var c = source.GetPixel(x, y);
                int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
                float sat = max == 0 ? 0 : (max - min) / (float)max, value = max / 255f;
                Func<int, int, int> mix = (t, i) => (int)Math.Round((i * (1 - sat) + t * sat) * value);
                result.SetPixel(x, y, Color.FromArgb(c.A, mix(target.R, ink.R), mix(target.G, ink.G), mix(target.B, ink.B)));
            }
        return result;
    }
}

// A layout's letters in a circle of its color; a click opens the palette
class ColorSwatch : Control
{
    public string Letters;
    public Color Color;
    public bool Custom;                  // chosen by the user, not the default for the alphabet
    bool hover;

    public ColorSwatch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Size = new Size(34, 34);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); base.OnKeyDown(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float ring = Theme.S(3);
        LetterIcon.Draw(g, new RectangleF(ring, ring, Width - ring * 2, Height - ring * 2), Letters, Color, LetterIcon.InkOn(Color));
        if (hover || (Focused && ShowFocusCues))
            using (var p = new Pen(Theme.Text, Math.Max(1.5f, Theme.Scale * 1.5f))) g.DrawEllipse(p, 1, 1, Width - 2.5f, Height - 2.5f);
    }
}

// The palette that opens under a swatch: the colors, "Default" and "Other color…" (the Windows color dialog)
static class ColorPicker
{
    public static void Show(Control owner, Color current, Action<Color?> chosen)
    {
        var menu = new ToolStripDropDown { Padding = Padding.Empty };
        ThemedMenuRenderer.Apply(menu);
        menu.Padding = new Padding(0);
        const int cols = 6, dot = 26, gap = 8, pad = 12;
        var panel = new Panel { BackColor = Theme.Card, Size = new Size(pad * 2 + cols * dot + (cols - 1) * gap, pad * 2 + 2 * dot + gap + 10 + 32) };
        var palette = LayoutColors.Palette;
        int hover = -1;
        Func<int, Rectangle> cell = i => new Rectangle(pad + (i % cols) * (dot + gap), pad + (i / cols) * (dot + gap), dot, dot);
        panel.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < palette.Length; i++)
            {
                var r = cell(i);
                using (var b = new SolidBrush(palette[i])) g.FillEllipse(b, r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);
                if (i == hover || palette[i].ToArgb() == current.ToArgb())
                    using (var p = new Pen(Theme.Text, 1.6f)) g.DrawEllipse(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
        };
        panel.MouseMove += (s, e) =>
        {
            int i = -1;
            for (int k = 0; k < palette.Length; k++) if (cell(k).Contains(e.Location)) i = k;
            if (i != hover) { hover = i; panel.Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; panel.Invalidate(); }
        };
        panel.MouseClick += (s, e) =>
        {
            for (int k = 0; k < palette.Length; k++)
                if (cell(k).Contains(e.Location)) { menu.Close(); chosen(palette[k]); return; }
        };
        int by = pad + 2 * dot + gap + 10, bw = (panel.Width - pad * 2 - 8) / 2;
        var reset = new RoundButton { Text = L.T("По умолчанию", "Default"), Location = new Point(pad, by), Size = new Size(bw, 30), Font = Theme.Small };
        var other = new RoundButton { Text = L.T("Другой…", "Other…"), Location = new Point(pad + bw + 8, by), Size = new Size(bw, 30), Font = Theme.Small };
        reset.Click += (s, e) => { menu.Close(); chosen(null); };
        other.Click += (s, e) =>
        {
            menu.Close();
            using (var dialog = new ColorDialog { Color = current, FullOpen = true, AnyColor = true })
                if (dialog.ShowDialog(owner.FindForm()) == DialogResult.OK) chosen(dialog.Color);
        };
        panel.Controls.Add(reset);
        panel.Controls.Add(other);
        if (Math.Abs(Theme.Scale - 1) > 0.01f) panel.Scale(new SizeF(Theme.Scale, Theme.Scale));
        var host = new ToolStripControlHost(panel) { Padding = Padding.Empty, Margin = Padding.Empty, AutoSize = false, Size = panel.Size };
        menu.Items.Add(host);
        menu.Closed += (s, e) => owner.BeginInvoke(new Action(menu.Dispose));
        menu.Show(owner, new Point(owner.Width, owner.Height + 2), ToolStripDropDownDirection.BelowLeft);   // opens leftwards: the swatches sit at the right edge
    }
}
