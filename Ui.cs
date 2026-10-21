using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// Windows 11 look for plain WinForms: colors that follow the system light/dark theme and accent color,
// and a few self-drawn controls (switch, card, rounded button, sidebar). No extra libraries.
static class Theme
{
    public static readonly bool Dark = ReadDark();
    public static readonly Color Accent = ReadAccent();

    public static Color Back { get { return Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243); } }
    public static Color Card { get { return Dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(251, 251, 251); } }
    public static Color CardBorder { get { return Dark ? Color.FromArgb(29, 29, 29) : Color.FromArgb(229, 229, 229); } }
    public static Color Text { get { return Dark ? Color.White : Color.FromArgb(27, 27, 27); } }
    public static Color SubText { get { return Dark ? Color.FromArgb(200, 200, 200) : Color.FromArgb(96, 96, 96); } }
    public static Color Input { get { return Dark ? Color.FromArgb(45, 45, 45) : Color.White; } }
    public static Color InputBorder { get { return Dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(214, 214, 214); } }
    public static Color Hover { get { return Dark ? Color.FromArgb(55, 55, 55) : Color.FromArgb(233, 233, 233); } }
    public static Color Selected { get { return Dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(228, 228, 228); } }
    public static Color Danger { get { return Dark ? Color.FromArgb(255, 153, 164) : Color.FromArgb(196, 43, 28); } }
    // On a dark background Windows 11 uses a lighter shade of the accent color
    public static Color AccentFill { get { return Dark ? Blend(Accent, Color.White, 0.45) : Accent; } }
    public static Color OnAccent { get { return Dark ? Color.Black : Color.White; } }
    public static Color SelectionBack { get { return Blend(AccentFill, Card, Dark ? 0.7 : 0.82); } }

    public static readonly Font Body = new Font("Segoe UI", 10f);
    public static readonly Font Small = new Font("Segoe UI", 9f);
    public static readonly Font Strong = new Font("Segoe UI Semibold", 10f);
    public static readonly Font Title = new Font("Segoe UI Semibold", 20f);
    public static readonly Font Section = new Font("Segoe UI Semibold", 11f);
    public static readonly string IconFontName = HasFont("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    static bool ReadDark()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return k != null && k.GetValue("AppsUseLightTheme") is int && (int)k.GetValue("AppsUseLightTheme") == 0;
        }
        catch { return false; }
    }

    static Color ReadAccent()
    {
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
    }

    public static Label Label(string text, Font font = null, Color? color = null)
    {
        return new Label { Text = text, Font = font ?? Body, ForeColor = color ?? Text, AutoSize = true, BackColor = Color.Transparent };
    }

    // A label that wraps within the given width
    public static Label Wrapped(string text, int width, Font font = null, Color? color = null)
    {
        var l = new Label { Text = text, Font = font ?? Body, ForeColor = color ?? Text, AutoSize = false, BackColor = Color.Transparent };
        var size = TextRenderer.MeasureText(text, l.Font, new Size(width, 0), TextFormatFlags.WordBreak);
        l.Size = new Size(width, size.Height + 2);
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
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 34,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, Font = Body, EditMode = DataGridViewEditMode.EditOnEnter
        };
        // Only a thin line between rows, no vertical lines
        g.AdvancedCellBorderStyle.Left = g.AdvancedCellBorderStyle.Right = g.AdvancedCellBorderStyle.Top = DataGridViewAdvancedCellBorderStyle.None;
        g.AdvancedCellBorderStyle.Bottom = DataGridViewAdvancedCellBorderStyle.Single;
        g.RowTemplate.Height = 34;
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
            e.Paint(cell, e.PaintParts & ~DataGridViewPaintParts.Border);
            g.ResetClip();
            e.Handled = true;
            return;
        }
        // Drop-down cells: the value and a small chevron, without the classic combo box frame
        if (grid.Columns[e.ColumnIndex] is DataGridViewComboBoxColumn && !(grid.IsCurrentCellInEditMode && grid.CurrentCell == grid[e.ColumnIndex, e.RowIndex]))
        {
            PaintCellBack(e);
            var box = new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y, e.CellBounds.Width - 36, e.CellBounds.Height);
            TextRenderer.DrawText(g, Convert.ToString(e.FormattedValue), e.CellStyle.Font, box, Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            using (var icons = new Font(IconFontName, 8f))
                TextRenderer.DrawText(g, "\uE70D", icons, new Rectangle(e.CellBounds.Right - 30, e.CellBounds.Y, 22, e.CellBounds.Height), SubText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
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
        var t = new TextBox { BorderStyle = BorderStyle.FixedSingle, BackColor = Input, ForeColor = Text, Font = Body };
        if (placeholder != null) t.HandleCreated += (s, e) => SetPlaceholder(t, placeholder);
        return t;
    }

    // Gray hint text shown while a field is empty
    public static void SetPlaceholder(TextBox t, string text)
    {
        SendMessage(t.Handle, 0x1501, (IntPtr)1, text);   // EM_SETCUEBANNER
    }

    public static ComboBox Combo()
    {
        var c = new ComboBox { FlatStyle = FlatStyle.Flat, BackColor = Input, ForeColor = Text, Font = Body };
        DarkScrollbars(c);
        return c;
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
    }

    public bool Checked
    {
        get { return on; }
        set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
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

// Rounded button: the accent one is for the main action
class RoundButton : Control
{
    bool hover, down;
    public bool AccentStyle;

    public RoundButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Font = Theme.Body;
        Size = new Size(120, 32);
        Cursor = Cursors.Hand;
        TabStop = true;
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
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

// The sidebar with sections, each with an icon from the system icon font
class NavList : Control
{
    public readonly System.Collections.Generic.List<Tuple<string, string>> Items = new System.Collections.Generic.List<Tuple<string, string>>();
    int selected, hover = -1;
    public event EventHandler SelectedChanged;
    readonly Font icons = new Font(Theme.IconFontName, 12f);
    public int ItemHeight = 40;

    public NavList()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Back;
        Font = Theme.Body;
        TabStop = true;
    }

    public int Selected
    {
        get { return selected; }
        set { if (selected == value) return; selected = value; Invalidate(); if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty); }
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
            TextRenderer.DrawText(g, Items[i].Item1, icons, new Rectangle(r.X + 12, r.Y, 24, r.Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Items[i].Item2, Font, new Rectangle(r.X + 46, r.Y, r.Width - 50, r.Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
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
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.SubText;
        base.OnRenderItemText(e);
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
    readonly Font chevron = new Font(Theme.IconFontName, 8f);

    public DropDown(bool editable = false, string placeholder = null)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Font = Theme.Body;
        Size = new Size(200, 32);
        TabStop = !editable;
        Cursor = Cursors.Hand;
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
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 4))
        {
            using (var b = new SolidBrush(hover && edit == null ? Theme.Hover : Theme.Input)) g.FillPath(b, path);
            using (var p = new Pen(Theme.InputBorder)) g.DrawPath(p, path);
        }
        // An accent line at the bottom while active, like Windows 11 fields
        if (focused || open)
            using (var p = new Pen(Theme.AccentFill, 2f)) g.DrawLine(p, 4, Height - 1.5f, Width - 4, Height - 1.5f);
        if (edit == null)
            TextRenderer.DrawText(g, Text, Font, new Rectangle(10, 0, Width - 10 - Height, Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(g, "\uE70D", chevron, new Rectangle(Width - Height, 0, Height - 4, Height), Theme.SubText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) chevron.Dispose();
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

    public void Flash(string text, bool latin)
    {
        if (picture != null) picture.Dispose();
        picture = Render(text, latin ? Color.FromArgb(0, 103, 192) : Color.FromArgb(222, 100, 0));
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
                    g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, w, h + 1), sf);
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
        var c = Cursor.Position;
        return Fit(new Point(c.X + 14, c.Y + 20), size);
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
