using System;
using System.Drawing;

// A look of KeyFish: the window colors (light and dark), the accent, the fish's disc, the tray colors and what a theme
// may add (a witch's hat, bats, the night aquarium, its own sound). Theme.Skin is the one in use. A new theme is one
// more entry in All, with a Number of its own that never changes (the registry and saved settings keep the number)
class Skin
{
    public int Number;
    public string NameRu, NameEn;
    public Func<DateTime, bool> Season;   // a holiday's theme: the days it comes by itself; null for the others
    public string SeasonRu, SeasonEn;     // those days in words, for Settings
    public string ShortRu, ShortEn;       // and shorter, under its picture in the gallery
    public Palette Light, Dark;
    public Color Accent = Color.Empty;    // empty: the accent color of Windows
    public Color DiscCenter, DiscEdge;    // the disc behind the fish (the logo, About, the gallery)
    public Color Latin, Other;            // the letters icon in the tray: a Latin layout and another alphabet
    public bool TintTray;                 // the tray fish takes those colors too, instead of its blue and orange
    public bool Hat, Bats, Night;         // a witch's hat on the fish, bats instead of bubbles, the night aquarium
    public string Sound = "bloop";        // its correction sound (Sounds.Builtin)

    public string Name { get { return L.T(NameRu, NameEn); } }
    public string SeasonText { get { return Season == null ? null : L.T(SeasonRu, SeasonEn); } }
    public string SeasonShort { get { return Season == null ? null : L.T(ShortRu, ShortEn); } }

    static readonly Palette ClassicLight = new Palette(Color.FromArgb(243, 243, 243), Color.FromArgb(251, 251, 251), Color.FromArgb(229, 229, 229),
        Color.White, Color.FromArgb(214, 214, 214), Color.FromArgb(233, 233, 233), Color.FromArgb(228, 228, 228));

    public static readonly Skin Classic = new Skin
    {
        Number = 0, NameRu = "Классическая", NameEn = "Classic",
        Light = ClassicLight,
        Dark = new Palette(Color.FromArgb(32, 32, 32), Color.FromArgb(43, 43, 43), Color.FromArgb(29, 29, 29),
            Color.FromArgb(45, 45, 45), Color.FromArgb(70, 70, 70), Color.FromArgb(55, 55, 55), Color.FromArgb(60, 60, 60)),
        DiscCenter = Color.FromArgb(78, 146, 240), DiscEdge = Color.FromArgb(28, 88, 196),
        Latin = Color.FromArgb(40, 110, 220), Other = Color.FromArgb(232, 112, 30),
    };

    // Pumpkin colors, the dark theme leans to night purple
    public static readonly Skin Halloween = new Skin
    {
        Number = 1, NameRu = "Хэллоуин", NameEn = "Halloween",
        Season = day => (day.Month == 10 && day.Day >= 24) || (day.Month == 11 && day.Day <= 2),
        SeasonRu = "24 октября — 2 ноября", SeasonEn = "October 24 to November 2", ShortRu = "24.10 — 2.11", ShortEn = "Oct 24 – Nov 2",
        Light = ClassicLight,
        Dark = new Palette(Color.FromArgb(31, 26, 38), Color.FromArgb(43, 36, 52), Color.FromArgb(27, 22, 34),
            Color.FromArgb(46, 39, 56), Color.FromArgb(76, 64, 92), Color.FromArgb(58, 49, 70), Color.FromArgb(64, 54, 78)),
        Accent = Color.FromArgb(232, 110, 20),
        DiscCenter = Color.FromArgb(255, 170, 70), DiscEdge = Color.FromArgb(214, 88, 18),
        Latin = Color.FromArgb(124, 70, 200), Other = Color.FromArgb(240, 120, 20),
        TintTray = true, Hat = true, Bats = true, Night = true, Sound = "ghost",
    };

    public static readonly Skin[] All = { Classic, Halloween };

    public static Skin Find(int number)
    {
        foreach (var s in All) if (s.Number == number) return s;
        return Classic;
    }

    // The one to use: none in high contrast (the windows take the system's colors), a holiday's own theme on its days
    // unless that is switched off, otherwise the chosen one
    public static Skin For(int chosen, bool holidays, DateTime day, bool contrast)
    {
        if (contrast) return Classic;
        if (holidays) foreach (var s in All) if (s.Season != null && s.Season(day)) return s;
        return Find(chosen);
    }
}

// The window colors of a skin, in the light or the dark theme
class Palette
{
    public readonly Color Back, Card, CardBorder, Input, InputBorder, Hover, Selected;

    public Palette(Color back, Color card, Color cardBorder, Color input, Color inputBorder, Color hover, Color selected)
    {
        Back = back; Card = card; CardBorder = cardBorder; Input = input; InputBorder = inputBorder; Hover = hover; Selected = selected;
    }
}
