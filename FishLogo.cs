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
            Draw(g, new RectangleF(0, 0, size, size), true, 0, 1, -1);
        }
        return bmp;
    }

    // r: where to draw (the logo is designed on a 64×64 grid). circle: the blue disc behind the fish.
    // tail: −1…1, how far the tail is bent. eyeOpen: 0…1 (blinking).
    // bubbles: below 0 — the two still bubbles of the icon; 0…1 — the phase of a looping stream of bubbles
    public static void Draw(Graphics g, RectangleF r, bool circle, float tail, float eyeOpen, float bubbles)
    {
        var saved = g.Save();
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 64f, r.Height / 64f);
        var white = Color.White;
        var shade = Color.FromArgb(214, 228, 250);

        bool holiday = Theme.Holiday;
        if (circle)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(0.5f, 0.5f, 63, 63);
                // On Halloween the disc is a pumpkin
                using (var b = new PathGradientBrush(path)
                {
                    CenterPoint = new PointF(22, 18),
                    CenterColor = holiday ? Color.FromArgb(255, 170, 70) : Color.FromArgb(78, 146, 240),
                    SurroundColors = new[] { holiday ? Color.FromArgb(214, 88, 18) : Color.FromArgb(28, 88, 196) }
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

        if (holiday) Hat(g);

        // Bubbles rise from the mouth; on Halloween, bats
        if (holiday)
        {
            if (bubbles < 0) { Bat(g, 8.5f, 15.5f, 5f, 0.3f, 1); Bat(g, 15.5f, 7.5f, 3.6f, 0.75f, 1); }
            else for (int i = 0; i < 2; i++) BatFlight(g, (bubbles + i * 0.5f) % 1, i);
        }
        else if (bubbles < 0)
            using (var p = new Pen(Color.FromArgb(230, white), 1.6f))
            {
                g.DrawEllipse(p, 11, 13, 6, 6);
                g.DrawEllipse(p, 17, 7, 4, 4);
            }
        else
            for (int i = 0; i < BubbleSizes.Length; i++)
                Bubble(g, (bubbles + (float)i / BubbleSizes.Length) % 1, BubbleSizes[i], i);
        g.Restore(saved);
    }

    static readonly float[] BubbleSizes = { 3f, 2.2f, 2.6f };

    static readonly Color HatColor = Color.FromArgb(52, 28, 74), BatColor = Color.FromArgb(38, 20, 52);

    // A witch's hat on the fish's head, its tip bent back (on the 64×64 grid). bold: a bigger hat for small icons,
    // where the usual one would be a smudge
    public static void Hat(Graphics g, bool bold = false)
    {
        var state = g.Save();
        if (bold) { g.TranslateTransform(20, 22); g.ScaleTransform(1.45f, 1.45f); g.TranslateTransform(-20, -20); }
        using (var cone = new GraphicsPath())
        {
            cone.AddBezier(14.5f, 21.5f, 17, 15, 19.5f, 10, 23, 6.5f);
            cone.AddBezier(23, 6.5f, 26, 4, 29, 4.5f, 31, 6.5f);      // the tip bends back over the head
            cone.AddBezier(31, 6.5f, 27.5f, 7, 25.5f, 9, 25.5f, 13);
            cone.AddBezier(25.5f, 13, 25.5f, 16, 26.5f, 18.5f, 27.5f, 20.5f);
            cone.CloseFigure();
            using (var b = new LinearGradientBrush(new PointF(14, 0), new PointF(28, 0), Color.FromArgb(78, 46, 104), HatColor)) g.FillPath(b, cone);
        }
        // An orange band and the brim
        using (var p = new Pen(Color.FromArgb(255, 150, 40), 2f)) g.DrawLine(p, 16, 18.6f, 26.6f, 17.8f);
        using (var b = new SolidBrush(HatColor)) g.FillEllipse(b, 10.5f, 19.2f, 21, 4.6f);
        using (var p = new Pen(Color.FromArgb(90, 255, 255, 255), 0.6f)) g.DrawArc(p, 11, 19.4f, 20, 4, 190, 120);
        g.Restore(state);
    }

    // A bat at (x, y), wingspan about 2·size; flap 0…1 is how high the wings are
    public static void Bat(Graphics g, float x, float y, float size, float flap, float alpha)
    {
        float up = (flap - 0.5f) * size * 0.9f;
        var wing = new[]
        {
            new PointF(0, 0), new PointF(-0.35f * size, -0.25f * size - up * 0.5f), new PointF(-1f * size, -0.35f * size - up),
            new PointF(-0.8f * size, 0.15f * size - up * 0.4f), new PointF(-0.55f * size, 0.05f * size), new PointF(-0.35f * size, 0.3f * size),
            new PointF(0, 0.2f * size)
        };
        using (var b = new SolidBrush(Color.FromArgb((int)(255 * Math.Max(0, Math.Min(1, alpha))), BatColor)))
        {
            var left = new PointF[wing.Length];
            var right = new PointF[wing.Length];
            for (int i = 0; i < wing.Length; i++)
            {
                left[i] = new PointF(x + wing[i].X, y + wing[i].Y);
                right[i] = new PointF(x - wing[i].X, y + wing[i].Y);
            }
            g.FillPolygon(b, left);
            g.FillPolygon(b, right);
            g.FillEllipse(b, x - 0.22f * size, y - 0.3f * size, 0.44f * size, 0.6f * size);
            // Ears
            g.FillPolygon(b, new[] { new PointF(x - 0.2f * size, y - 0.2f * size), new PointF(x - 0.14f * size, y - 0.48f * size), new PointF(x - 0.04f * size, y - 0.25f * size) });
            g.FillPolygon(b, new[] { new PointF(x + 0.2f * size, y - 0.2f * size), new PointF(x + 0.14f * size, y - 0.48f * size), new PointF(x + 0.04f * size, y - 0.25f * size) });
        }
    }

    // One bat of the stream at its moment t (0…1): it leaves the mouth, flaps up beside the hat and fades away
    static void BatFlight(Graphics g, float t, int seed)
    {
        float ease = 1 - (1 - t) * (1 - t);
        float x = 11.5f - 3 * ease + 1.2f * (float)Math.Sin((t * 4 + seed) * Math.PI);
        float y = 26 - 19 * ease;
        float size = 1.8f + 3.4f * Math.Min(1, t / 0.25f);
        float alpha = Math.Min(1, t / 0.15f) * (t > 0.8f ? (1 - t) / 0.2f : 1);
        float flap = 0.5f + 0.5f * (float)Math.Sin(t * 40 + seed);
        Bat(g, x, y, size, flap, alpha);
    }

    // One bubble of the stream at its own moment t (0…1): it appears at the mouth growing from a point, rises with a
    // gentle sway, and at the top pops — the ring widens, thins and fades while a few droplets fly off
    static void Bubble(Graphics g, float t, float radius, int seed)
    {
        const float pop = 0.82f;
        float rise = Math.Min(t, pop) / pop;
        float ease = 1 - (1 - rise) * (1 - rise);                       // fast at the mouth, slowing near the top
        float x = 12.5f + 5 * ease + 0.9f * (float)Math.Sin((rise * 3 + seed * 0.7f) * Math.PI);
        float y = 24 - 17 * ease;
        float grow = Math.Min(1, t / 0.12f);
        float r = radius * (0.25f + 0.75f * (1 - (1 - grow) * (1 - grow)));
        float alpha = grow;
        float width = 1.4f;
        if (t > pop)
        {
            float k = (t - pop) / (1 - pop);                            // 0…1 through the pop
            r *= 1 + 0.7f * k;
            alpha = (1 - k) * (1 - k);
            width = 1.4f * (1 - 0.7f * k);
            // Droplets fly off the bursting ring
            using (var b = new SolidBrush(Color.FromArgb((int)(220 * (1 - k)), Color.White)))
                for (int d = 0; d < 4; d++)
                {
                    double a = (d * 90 + 45 + seed * 20) * Math.PI / 180;
                    float dist = r + 1.6f * k;
                    float dot = 0.7f * (1 - k);
                    g.FillEllipse(b, x + (float)Math.Cos(a) * dist - dot, y + (float)Math.Sin(a) * dist - dot, dot * 2, dot * 2);
                }
        }
        if (alpha <= 0.01f) return;
        using (var p = new Pen(Color.FromArgb((int)(230 * alpha), Color.White), width))
            g.DrawEllipse(p, x - r, y - r, r * 2, r * 2);
        // A small glint, like on a real bubble
        if (t < pop)
            using (var b = new SolidBrush(Color.FromArgb((int)(150 * alpha), Color.White)))
                g.FillEllipse(b, x - r * 0.45f, y - r * 0.55f, r * 0.45f, r * 0.35f);
    }
}

// The logo in About and in the Settings sidebar. Click it: the fish flicks its tail. Click it five times: an easter egg
class FishView : Control
{
    readonly Timer timer = new Timer { Interval = 33 };   // 30 frames a second is plenty for a blink and a tail flick
    float wiggle, blink = 1, bubbles, fury;
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
        VisibleChanged += (s, e) => timer.Enabled = Visible;   // no animation while the window is hidden
    }

    void Step()
    {
        ticks++;
        // Blink every few seconds; wiggle fades out
        float t = ticks % 130;
        blink = t < 4 ? Math.Abs(t - 2) / 2f : 1;
        wiggle *= 0.8836f;
        fury = Math.Max(0, fury - 0.014f);   // about two and a half seconds
        bubbles = (bubbles + 0.008f + fury * 0.03f) % 1;
        Invalidate();
    }

    // The fish beats its tail as hard as it can for a moment
    public void Fury() { fury = 1; }

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
        float strength = Math.Min(1, fury * 3);   // full strength until the last third, then it calms down
        float tail = wiggle * (float)Math.Sin(ticks * 1.2) + 0.12f * (float)Math.Sin(ticks * 0.1) + strength * 1.7f * (float)Math.Sin(ticks * 2.6);
        float shake = strength * Math.Max(1, Width / 48f) * (float)Math.Sin(ticks * 3.7);   // the whole fish trembles with it
        FishLogo.Draw(e.Graphics, new RectangleF(1 + shake, 1, Width - 2, Height - 2), true, tail, blink, bubbles);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}

// Easter egg: the window fills with water, the fish swims in, eats a word typed in the wrong layout
// and lets out the right one as a bubble. Clicks are ignored until it ends, so it always plays to the end.
// On Halloween the water is night, the specks are fireflies, the fish is a ghost and the word rises in a moon with a bat
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

    // 0…1 with a small overshoot at the end, like a bubble popping into shape
    static float Spring(float x) { const float s = 1.9f; x -= 1; return x * x * ((s + 1) * x + s) + 1; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        double t = Elapsed;
        float w = Width, h = Height;
        bool night = Theme.Holiday;

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
            using (var b = new LinearGradientBrush(new RectangleF(0, top - 5, w, h - top + 10),
                       night ? Color.FromArgb(96, 56, 140) : Color.FromArgb(120, 190, 250), night ? Color.FromArgb(18, 10, 32) : Color.FromArgb(20, 70, 170), 90f))
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
        using (var b = new SolidBrush(night ? Color.FromArgb(150, 255, 170, 60) : Color.FromArgb(70, 255, 255, 255)))
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
            // The wrong word melts as the fish reaches it
            float melt = Ease((t - (eat - 0.3)) / 0.45);
            if (melt < 1)
            {
                var size = g.MeasureString(wrong, font);
                float bob = 4 * (float)Math.Sin(t * 2.4);
                var st = g.Save();
                g.TranslateTransform(wordX, wordY + bob);
                g.ScaleTransform(1 - 0.6f * melt, 1 - 0.6f * melt);
                using (var b = new SolidBrush(Color.FromArgb((int)(235 * (1 - melt)), 255, 255, 255)))
                    g.DrawString(wrong, font, b, -size.Width / 2, -size.Height / 2);
                g.Restore(st);
            }
            if (t > eat)
            {
                // The right word appears in a bubble: it grows from a small one with a little spring,
                // the text shows up a moment later, then the bubble rises
                float grow = (float)Math.Max(0, Math.Min(1, (t - eat) / 0.7));
                float scale = 0.15f + 0.85f * Spring(grow);
                float fade = Ease((t - eat) / 0.5);
                float textFade = Ease((t - eat - 0.25) / 0.45);
                float k = Ease((t - eat - 0.5) / 2.2);
                float y = wordY - k * h * 0.42f;
                var size = g.MeasureString(right, font);
                float d = Math.Max(size.Width, size.Height) + 22;
                var st = g.Save();
                g.TranslateTransform(wordX, y);
                g.ScaleTransform(scale, scale);
                if (night)
                {
                    // A full moon with a bat flapping across it
                    using (var b = new SolidBrush(Color.FromArgb((int)(230 * fade), 255, 176, 70))) g.FillEllipse(b, -d / 2, -d / 2, d, d);
                    using (var b = new SolidBrush(Color.FromArgb((int)(60 * fade), 255, 230, 160))) g.FillEllipse(b, -d / 2 + 5, -d / 2 + 4, d * 0.4f, d * 0.3f);
                    FishLogo.Bat(g, d * 0.32f, -d * 0.42f, d * 0.2f, 0.5f + 0.5f * (float)Math.Sin(t * 18), fade);
                    using (var b = new SolidBrush(Color.FromArgb((int)(255 * textFade), 46, 22, 60))) g.DrawString(right, font, b, -size.Width / 2, -size.Height / 2);
                }
                else
                {
                    using (var b = new SolidBrush(Color.FromArgb((int)(60 * fade), 255, 255, 255))) g.FillEllipse(b, -d / 2, -d / 2, d, d);
                    using (var p = new Pen(Color.FromArgb((int)(220 * fade), 255, 255, 255), 2f)) g.DrawEllipse(p, -d / 2, -d / 2, d, d);
                    // A glint on the bubble
                    using (var p = new Pen(Color.FromArgb((int)(150 * fade), 255, 255, 255), 2.5f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                        g.DrawArc(p, -d / 2 + 7, -d / 2 + 7, d - 14, d - 14, 200, 50);
                    using (var b = new SolidBrush(Color.FromArgb((int)(255 * textFade), 255, 255, 255))) g.DrawString(right, font, b, -size.Width / 2, -size.Height / 2);
                }
                g.Restore(st);
                // Crumbs of the eaten word turn into little bubbles
                float c = Ease((t - eat) / 2.2);
                using (var p = new Pen(Color.FromArgb((int)(200 * (1 - c) * fade), 255, 255, 255), 1.5f))
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i * 0.9f;
                        float r = 6 + 40 * c;
                        g.DrawEllipse(p, wordX + r * (float)Math.Cos(a) - 3, wordY + r * (float)Math.Sin(a) - 3 - 30 * c, 6, 6);
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
        if (night)
        {
            // A ghost: the fish drawn into its own picture, then put down see-through and wavering
            int size = Math.Max(8, (int)fishSize);
            using (var ghost = new Bitmap(size, size))
            {
                using (var gg = Graphics.FromImage(ghost))
                {
                    gg.SmoothingMode = SmoothingMode.AntiAlias;
                    FishLogo.Draw(gg, new RectangleF(0, 0, size, size), false, tail, 1, (float)(t * 0.6 % 1));
                }
                using (var attr = new System.Drawing.Imaging.ImageAttributes())
                {
                    float a = 0.55f + 0.2f * (float)Math.Sin(t * 5);
                    attr.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix00 = 0.9f, Matrix11 = 0.95f, Matrix22 = 1f, Matrix33 = a });
                    g.DrawImage(ghost, new Rectangle(0, 0, size, size), 0, 0, size, size, GraphicsUnit.Pixel, attr);
                }
            }
        }
        else FishLogo.Draw(g, new RectangleF(0, 0, fishSize, fishSize), false, tail, 1, (float)(t * 0.6 % 1));
        g.Restore(state);
        g.Clip = clip;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
