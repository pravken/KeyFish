using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// The KeyFish logo drawn as vectors, so it stays sharp at any size and can move: the same fish as the
// tray icon (tools\make-icon.ps1), with more detail for large sizes
static class FishLogo
{
    public static readonly Color Blue = Color.FromArgb(40, 110, 220);

    public static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Draw(g, new RectangleF(0, 0, size, size), true, 0, 1, 0);
        }
        return bmp;
    }

    // r: where to draw (the logo is designed on a 64×64 grid). circle: the blue disc behind the fish.
    // tail: −1…1, how far the tail is bent. eyeOpen: 0…1 (blinking). bubbles: 0…1, how far the bubbles have risen
    public static void Draw(Graphics g, RectangleF r, bool circle, float tail, float eyeOpen, float bubbles)
    {
        var saved = g.Save();
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 64f, r.Height / 64f);
        var white = Color.White;
        var shade = Color.FromArgb(214, 228, 250);

        if (circle)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(0.5f, 0.5f, 63, 63);
                using (var b = new PathGradientBrush(path)
                {
                    CenterPoint = new PointF(22, 18),
                    CenterColor = Color.FromArgb(78, 146, 240),
                    SurroundColors = new[] { Color.FromArgb(28, 88, 196) }
                })
                    g.FillPath(b, path);
            }
            // A soft light on the top edge, like glass
            using (var p = new Pen(Color.FromArgb(70, 255, 255, 255), 1.2f)) g.DrawArc(p, 3, 3, 58, 58, 200, 110);
        }

        // Tail: bends around the point where it meets the body
        var tailState = g.Save();
        g.TranslateTransform(41, 32);
        g.RotateTransform(tail * 16);
        using (var path = new GraphicsPath())
        {
            path.AddBezier(-2, -2, 4, -6, 9, -10, 14, -13);
            path.AddBezier(14, -13, 11, -6, 10, -2, 10, 0);
            path.AddBezier(10, 0, 10, 2, 11, 6, 14, 13);
            path.AddBezier(14, 13, 9, 10, 4, 6, -2, 2);
            path.CloseFigure();
            using (var b = new LinearGradientBrush(new PointF(0, -13), new PointF(0, 13), white, shade)) g.FillPath(b, path);
            using (var p = new Pen(Color.FromArgb(60, Blue), 0.6f)) { g.DrawLine(p, 2, -1, 10, -7); g.DrawLine(p, 2, 1, 10, 7); }
        }
        g.Restore(tailState);

        // Top and bottom fins
        using (var path = new GraphicsPath())
        {
            path.AddBezier(21, 23.5f, 24, 18, 28, 14, 33, 13);
            path.AddBezier(33, 13, 33.5f, 17, 35, 20, 36, 23.5f);
            path.CloseFigure();
            using (var b = new LinearGradientBrush(new PointF(0, 13), new PointF(0, 24), white, shade)) g.FillPath(b, path);
        }
        using (var path = new GraphicsPath())
        {
            path.AddBezier(25, 41.5f, 27, 45, 29, 47, 32, 48);
            path.AddBezier(32, 48, 32, 46, 32.5f, 44, 34, 41.5f);
            path.CloseFigure();
            using (var b = new SolidBrush(shade)) g.FillPath(b, path);
        }

        // Body with a light belly shade
        using (var b = new LinearGradientBrush(new RectangleF(10, 21, 34, 22), white, shade, LinearGradientMode.Vertical))
        {
            var blend = new ColorBlend { Colors = new[] { white, white, shade }, Positions = new[] { 0f, 0.55f, 1f } };
            b.InterpolationColors = blend;
            g.FillEllipse(b, 10, 21, 34, 22);
        }
        // Gill and a few scales: thin lines in the background color
        using (var p = new Pen(Color.FromArgb(90, Blue), 1.1f)) g.DrawArc(p, 17, 25, 9, 14, -55, 110);
        using (var p = new Pen(Color.FromArgb(45, Blue), 0.8f))
        {
            g.DrawArc(p, 28, 27, 5, 6, -60, 120);
            g.DrawArc(p, 32, 30, 5, 6, -60, 120);
            g.DrawArc(p, 28, 33, 5, 6, -60, 120);
        }
        // Mouth
        using (var p = new Pen(Color.FromArgb(120, Blue), 0.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, 10.6f, 33.2f, 13, 33.6f);

        // Eye with a glint; it closes when blinking
        float eyeH = 5 * Math.Max(0.12f, eyeOpen);
        using (var b = new SolidBrush(Color.FromArgb(22, 60, 140))) g.FillEllipse(b, 15.5f, 29 - eyeH / 2, 5, eyeH);
        if (eyeOpen > 0.5f) using (var b = new SolidBrush(white)) g.FillEllipse(b, 17.6f, 27.6f, 1.6f, 1.6f);

        // Bubbles rise from the mouth
        using (var p = new Pen(Color.FromArgb(230, white), 1.6f))
        {
            float rise = bubbles * 6;
            g.DrawEllipse(p, 11, 13 - rise, 6, 6);
            g.DrawEllipse(p, 17, 7 - rise * 1.4f, 4, 4);
        }
        g.Restore(saved);
    }
}

// The logo in the About window. Click it: the fish flicks its tail. Click it five times: an easter egg
class FishView : Control
{
    readonly Timer timer = new Timer { Interval = 16 };
    float wiggle, blink = 1, bubbles;
    int clicks, ticks;
    DateTime firstClick;
    public event EventHandler EasterEgg;

    public FishView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        timer.Tick += (s, e) => Step();
        timer.Start();
    }

    void Step()
    {
        ticks++;
        // Blink every few seconds; wiggle fades out
        float t = ticks % 260;
        blink = t < 8 ? Math.Abs(t - 4) / 4f : 1;
        wiggle *= 0.94f;
        bubbles = (bubbles + 0.004f) % 1;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        wiggle = 1;
        if (DateTime.Now - firstClick > TimeSpan.FromSeconds(3)) { firstClick = DateTime.Now; clicks = 0; }
        if (++clicks >= 5)
        {
            clicks = 0;
            if (EasterEgg != null) EasterEgg(this, EventArgs.Empty);
        }
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float tail = wiggle * (float)Math.Sin(ticks * 0.6) + 0.12f * (float)Math.Sin(ticks * 0.05);
        FishLogo.Draw(e.Graphics, new RectangleF(1, 1, Width - 2, Height - 2), true, tail, blink, bubbles);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}

// Easter egg: the window fills with water, the fish swims in, eats a word typed in the wrong layout
// and lets out the right one as a bubble. Clicks are ignored until it ends, so it always plays to the end
class Aquarium : Control
{
    readonly Timer timer = new Timer { Interval = 16 };
    readonly DateTime start = DateTime.Now;
    readonly string wrong, right;
    readonly Random random = new Random();
    readonly List<PointF> specks = new List<PointF>();
    const double Duration = 6.5;

    public Aquarium(string wrong, string right)
    {
        this.wrong = wrong;
        this.right = right;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        for (int i = 0; i < 26; i++) specks.Add(new PointF((float)random.NextDouble(), (float)random.NextDouble()));
        timer.Tick += (s, e) => { if (Elapsed > Duration) Finish(); else Invalidate(); };
        timer.Start();
    }

    double Elapsed { get { return (DateTime.Now - start).TotalSeconds; } }

    void Finish()
    {
        timer.Stop();
        var parent = Parent;
        if (parent != null) parent.Controls.Remove(this);
        Dispose();
    }

    static float Ease(double x) { x = Math.Max(0, Math.Min(1, x)); return (float)(x * x * (3 - 2 * x)); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        double t = Elapsed;
        float w = Width, h = Height;

        // The window behind, then the water level: rises in the first second, drains in the last one
        g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
        float level = Ease(t / 0.9) * (1 - Ease((t - (Duration - 1)) / 0.9));
        float top = h * (1 - level);
        using (var path = new GraphicsPath())
        {
            var wave = new List<PointF>();
            for (float x = 0; x <= w + 8; x += 8)
                wave.Add(new PointF(x, top + 4 * (float)Math.Sin(x / 34 + t * 3)));
            path.AddLines(wave.ToArray());
            path.AddLine(w + 8, top, w + 8, h);
            path.AddLine(w + 8, h, 0, h);
            path.CloseFigure();
            using (var b = new LinearGradientBrush(new RectangleF(0, top - 5, w, h - top + 10), Color.FromArgb(120, 190, 250), Color.FromArgb(20, 70, 170), 90f))
                g.FillPath(b, path);
        }
        if (level < 0.05) return;
        var clip = g.Clip;
        g.SetClip(new RectangleF(0, top + 4, w, h));

        // Light rays and floating specks
        using (var b = new SolidBrush(Color.FromArgb(18, 255, 255, 255)))
            for (int i = 0; i < 3; i++)
            {
                float x = w * (0.2f + 0.3f * i) + 20 * (float)Math.Sin(t + i);
                g.FillPolygon(b, new[] { new PointF(x, top), new PointF(x + 40, top), new PointF(x - 30, h), new PointF(x - 90, h) });
            }
        using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
            foreach (var p in specks)
            {
                float y = (float)((p.Y * h - t * 18) % h + h) % h;
                g.FillEllipse(b, p.X * w + 6 * (float)Math.Sin(t * 2 + p.Y * 9), y, 2.5f, 2.5f);
            }

        // The wrong word floats on the right until the fish reaches it
        float wordX = w * 0.68f, wordY = h * 0.52f;
        double eat = 3.0;
        using (var font = new Font("Segoe UI Semibold", 15f))
        {
            if (t < eat)
            {
                var size = g.MeasureString(wrong, font);
                float bob = 4 * (float)Math.Sin(t * 2.4);
                using (var b = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                    g.DrawString(wrong, font, b, wordX - size.Width / 2, wordY - size.Height / 2 + bob);
            }
            else
            {
                // The right word rises in a bubble
                float k = Ease((t - eat) / 2.2);
                float y = wordY - k * h * 0.42f;
                var size = g.MeasureString(right, font);
                float d = Math.Max(size.Width, size.Height) + 22;
                using (var b = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) g.FillEllipse(b, wordX - d / 2, y - d / 2, d, d);
                using (var p = new Pen(Color.FromArgb(220, 255, 255, 255), 2f)) g.DrawEllipse(p, wordX - d / 2, y - d / 2, d, d);
                using (var b = new SolidBrush(Color.White)) g.DrawString(right, font, b, wordX - size.Width / 2, y - size.Height / 2);
                // Crumbs of the eaten word turn into little bubbles
                using (var p = new Pen(Color.FromArgb((int)(200 * (1 - k)), 255, 255, 255), 1.5f))
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i * 0.9f;
                        float r = 6 + 40 * k;
                        g.DrawEllipse(p, wordX + r * (float)Math.Cos(a) - 3, wordY + r * (float)Math.Sin(a) - 3 - 30 * k, 6, 6);
                    }
            }
        }

        // The fish swims from the left edge to the word, then on and out to the right
        float fishSize = Math.Min(w, h) * 0.42f;
        float mouth = fishSize * 54f / 64f;   // the mirrored fish's mouth, from its left edge
        float fx = (float)(-fishSize + (wordX - mouth + fishSize) * Ease(t / eat));
        if (t > eat) fx = wordX - mouth + (float)((t - eat) * w * 0.32);
        float fy = wordY - fishSize / 2 + 10 * (float)Math.Sin(t * 3);
        float tail = (float)Math.Sin(t * 14) * 0.9f;
        // The logo faces left, the fish swims right: mirror it
        var state = g.Save();
        g.TranslateTransform(fx + fishSize, fy);
        g.ScaleTransform(-1, 1);
        FishLogo.Draw(g, new RectangleF(0, 0, fishSize, fishSize), false, tail, 1, (float)(t * 0.6 % 1));
        g.Restore(state);
        g.Clip = clip;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
