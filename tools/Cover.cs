using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;

// The README cover (1280×640, also fits GitHub's social preview): the fish from the app itself under water,
// the name, what it does and two examples. Writes docs\cover-en.png and docs\cover-ru.png:
//   csc /r:System.Drawing.dll /out:tests\bin\Cover.exe tools\Cover.cs
//   tests\bin\Cover.exe KeyFish.exe docs
static class Cover
{
    static MethodInfo drawFish;

    static void Main(string[] args)
    {
        // The fish is drawn by the app's own code (FishLogo.Draw), so the cover always matches the logo
        drawFish = Assembly.LoadFrom(args[0]).GetType("FishLogo").GetMethod("Draw");
        string dir = args[1];
        Render(System.IO.Path.Combine(dir, "cover-en.png"), "Fixes text typed in the wrong keyboard layout",
               new[] { "ghbdtn vbh", "привет мир", "руддщ", "hello" }, "Windows 10 / 11  ·  19 languages  ·  free, source on GitHub");
        Render(System.IO.Path.Combine(dir, "cover-ru.png"), "Исправляет текст, набранный не в той раскладке",
               new[] { "ghbdtn vbh", "привет мир", "руддщ", "hello" }, "Windows 10 / 11  ·  19 языков  ·  бесплатно, код на GitHub");
    }

    static string Face(params string[] names)
    {
        using (var installed = new InstalledFontCollection())
            foreach (string n in names)
                foreach (var f in installed.Families)
                    if (f.Name == n) return n;
        return "Segoe UI";
    }

    static void Render(string path, string tagline, string[] examples, string footer)
    {
        const int W = 1280, H = 640;
        using (var bmp = new Bitmap(W, H))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            // Deep water: lighter near the surface
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(18, 58, 112), Color.FromArgb(6, 18, 42), 90f))
                g.FillRectangle(b, 0, 0, W, H);

            // Light coming down from the surface
            var rnd = new Random(7);
            for (int i = 0; i < 7; i++)
            {
                float x = 80 + i * 170 + rnd.Next(-40, 40), w = 50 + rnd.Next(60);
                using (var p = new GraphicsPath())
                {
                    p.AddPolygon(new[] { new PointF(x, -10), new PointF(x + w, -10), new PointF(x + w * 2.6f - 120, H), new PointF(x + w * 1.2f - 200, H) });
                    using (var b = new LinearGradientBrush(new RectangleF(0, 0, W, H), Color.FromArgb(26, 170, 210, 255), Color.FromArgb(0, 170, 210, 255), 90f))
                        g.FillPath(b, p);
                }
            }

            // A glow behind the fish
            var fishRect = new RectangleF(96, 140, 360, 360);
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(fishRect.X - 110, fishRect.Y - 110, fishRect.Width + 220, fishRect.Height + 220);
                using (var b = new PathGradientBrush(p) { CenterColor = Color.FromArgb(90, 80, 150, 255), SurroundColors = new[] { Color.FromArgb(0, 80, 150, 255) } })
                    g.FillPath(b, p);
            }

            // Bubbles rising across the picture
            for (int i = 0; i < 26; i++)
            {
                float r = 3 + (float)rnd.NextDouble() * 9, x = rnd.Next(W), y = rnd.Next(H);
                if (x > 520 && x < 1200 && y > 120 && y < 560) continue;   // keep the text clear
                int a = 40 + rnd.Next(70);
                using (var pen = new Pen(Color.FromArgb(a, 200, 230, 255), 1.4f)) g.DrawEllipse(pen, x, y, r * 2, r * 2);
                using (var b = new SolidBrush(Color.FromArgb(a / 2, 255, 255, 255))) g.FillEllipse(b, x + r * 0.5f, y + r * 0.4f, r * 0.6f, r * 0.5f);
            }

            // The fish, slightly tilted as if swimming up
            var state = g.Save();
            g.TranslateTransform(fishRect.X + fishRect.Width / 2, fishRect.Y + fishRect.Height / 2);
            g.RotateTransform(-6);
            drawFish.Invoke(null, new object[] { g, new RectangleF(-fishRect.Width / 2, -fishRect.Height / 2, fishRect.Width, fishRect.Height), true, 0.35f, 1f, -1f });
            g.Restore(state);

            // Text
            string semibold = Face("Segoe UI Variable Text Semibold", "Segoe UI Semibold");
            string regular = Face("Segoe UI Variable Text", "Segoe UI");
            string mono = Face("Cascadia Mono", "Cascadia Code", "Consolas");
            float left = 540;
            using (var title = new Font(semibold, 96f, GraphicsUnit.Pixel))
                g.DrawString("KeyFish", title, Brushes.White, left - 6, 118);
            // The tagline as large as fits: Russian is longer than English
            float tagSize = 32f;
            while (tagSize > 20)
                using (var probe = new Font(regular, tagSize, GraphicsUnit.Pixel))
                {
                    if (g.MeasureString(tagline, probe).Width <= W - left - 40) break;
                    tagSize -= 1f;
                }
            using (var tag = new Font(regular, tagSize, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(Color.FromArgb(196, 220, 252)))
                g.DrawString(tagline, tag, b, left, 246 + (32f - tagSize) / 2);

            // Examples: what was typed, struck through in a dark chip → the fix in a bright one
            using (var code = new Font(mono, 34f, GraphicsUnit.Pixel))
            using (var arrow = new Font(regular, 34f, GraphicsUnit.Pixel))
            {
                // The typed words in one column, so the arrows and the fixes line up
                float column = 0;
                for (int i = 0; i < examples.Length; i += 2)
                    column = Math.Max(column, g.MeasureString(examples[i], code, PointF.Empty, StringFormat.GenericTypographic).Width + 40);
                float y = 326;
                for (int i = 0; i < examples.Length; i += 2)
                {
                    Chip(g, examples[i], code, left, y, Color.FromArgb(70, 255, 255, 255), Color.FromArgb(150, 175, 205), true, column);
                    using (var b = new SolidBrush(Color.FromArgb(255, 176, 92))) g.DrawString("→", arrow, b, left + column + 14, y + 4);
                    Chip(g, examples[i + 1], code, left + column + 70, y, Color.White, Color.FromArgb(14, 46, 96), false);
                    y += 82;
                }
            }
            using (var small = new Font(regular, 24f, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(Color.FromArgb(150, 180, 220)))
                g.DrawString(footer, small, b, left, 516);

            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    // A rounded chip with text; returns its right edge
    static float Chip(Graphics g, string text, Font font, float x, float y, Color fill, Color ink, bool struck, float width = 0)
    {
        var size = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
        var r = new RectangleF(x, y, Math.Max(width, size.Width + 40), 60);
        using (var p = Rounded(r, 16))
        {
            if (struck)
                using (var pen = new Pen(Color.FromArgb(110, 255, 255, 255), 1.5f)) { using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillPath(b, p); g.DrawPath(pen, p); }
            else
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
        }
        using (var b = new SolidBrush(ink)) g.DrawString(text, font, b, x + 20, y + 30 - size.Height / 2, StringFormat.GenericTypographic);
        if (struck)
            using (var pen = new Pen(Color.FromArgb(200, 255, 120, 110), 3f)) g.DrawLine(pen, x + 16, y + 31, x + 24 + size.Width, y + 31);
        return r.Right;
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
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
}
