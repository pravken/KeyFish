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

    static readonly Palette ClassicDark = new Palette(Color.FromArgb(32, 32, 32), Color.FromArgb(43, 43, 43), Color.FromArgb(29, 29, 29),
        Color.FromArgb(45, 45, 45), Color.FromArgb(70, 70, 70), Color.FromArgb(55, 55, 55), Color.FromArgb(60, 60, 60));
    static readonly Palette ClassicLight = new Palette(Color.FromArgb(243, 243, 243), Color.FromArgb(251, 251, 251), Color.FromArgb(229, 229, 229),
        Color.White, Color.FromArgb(214, 214, 214), Color.FromArgb(233, 233, 233), Color.FromArgb(228, 228, 228));

    public static readonly Skin Classic = new Skin
    {
        Number = 0, NameRu = "Классическая", NameEn = "Classic",
        Light = ClassicLight,
        Dark = ClassicDark,
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

    // The user's own: an accent color and a fish color, and the windows take a hint of the accent (Paint)
    public static readonly Color DefaultAccent = Color.FromArgb(0, 128, 120), DefaultFish = Color.FromArgb(30, 150, 136);
    static Skin custom;
    public static Skin Custom { get { return custom ?? (custom = Make(DefaultAccent, DefaultFish)); } }

    static Skin[] all;
    public static Skin[] All { get { return all ?? (all = new[] { Classic, Halloween, Custom }); } }

    // A custom skin with these colors: for Settings to show one before it is saved
    public static Skin Make(Color accent, Color fish)
    {
        var s = new Skin { Number = 2, NameRu = "Своё", NameEn = "Custom", TintTray = true, Other = Color.FromArgb(232, 112, 30) };
        Paint(s, accent, fish);
        return s;
    }

    // The colors of the custom skin in use, from the settings
    public static void SetCustom(Color accent, Color fish) { Paint(Custom, accent, fish); }

    static void Paint(Skin s, Color accent, Color fish)
    {
        if (accent.IsEmpty || accent.A == 0) accent = DefaultAccent;
        if (fish.IsEmpty || fish.A == 0) fish = DefaultFish;
        accent = Color.FromArgb(255, accent);
        fish = Color.FromArgb(255, fish);
        s.Accent = accent;
        s.DiscCenter = Mix(fish, Color.White, 0.3);
        s.DiscEdge = Mix(fish, Color.Black, 0.3);
        s.Latin = fish;
        // The classic colors with a little of the accent: more in the dark theme, where it shows less
        var l = ClassicLight; var d = ClassicDark;
        s.Light = new Palette(Mix(l.Back, accent, 0.04), Mix(l.Card, accent, 0.025), Mix(l.CardBorder, accent, 0.06), l.Input,
                              Mix(l.InputBorder, accent, 0.08), Mix(l.Hover, accent, 0.07), Mix(l.Selected, accent, 0.1));
        s.Dark = new Palette(Mix(d.Back, accent, 0.07), Mix(d.Card, accent, 0.07), Mix(d.CardBorder, accent, 0.05), Mix(d.Input, accent, 0.07),
                             Mix(d.InputBorder, accent, 0.15), Mix(d.Hover, accent, 0.12), Mix(d.Selected, accent, 0.16));
    }

    static Color Mix(Color a, Color b, double t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

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
