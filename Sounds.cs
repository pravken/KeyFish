using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// The sound library: what plays on each event (an auto-fix, a typo, a replacement, an undo, a hotkey, a pause).
// A sound is the look's own ("look"), none, one of the built-in ones made here instead of shipping sound files, or
// the user's .wav from the sounds folder ("file:name.wav"). Kept as "event = sound" lines (Map, Parse, Format)
static class Sounds
{
    public const int Fix = 0, Typo = 1, Replacement = 2, Undo = 3, Hotkey = 4, Pause = 5;
    public static readonly string[] Events = { "fix", "typo", "replacement", "undo", "hotkey", "pause" };
    public const string Look = "look", None = "none", FilePrefix = "file:";
    public static readonly string[] Builtin = { "bloop", "ghost", "drop", "click", "chime", "down" };

    // As before the library: the look's sound on fixes and replacements, quiet on the rest
    public static string Default(int e) { return e <= Replacement ? Look : None; }

    public static string EventName(int e)
    {
        switch (e)
        {
            case Fix: return L.T("Исправление раскладки", "Layout fix");
            case Typo: return L.T("Исправление опечатки", "Typo fix");
            case Replacement: return L.T("Автозамена и типографика", "Replacement and typography");
            case Undo: return L.T("Отмена исправления", "Undoing a fix");
            case Hotkey: return L.T("Горячая клавиша", "Hotkey");
            default: return L.T("Пауза и возобновление", "Pause and resume");
        }
    }

    public static string SoundName(string sound)
    {
        switch (sound)
        {
            case Look: return L.T("Звук темы", "Theme sound");
            case None: return L.T("Без звука", "No sound");
            case "bloop": return L.T("Всплеск", "Splash");
            case "ghost": return L.T("Гул", "Hum");
            case "drop": return L.T("Капля", "Drop");
            case "click": return L.T("Щелчок", "Click");
            case "chime": return L.T("Колокольчик", "Chime");
            case "down": return L.T("Всплеск низкий", "Low splash");
        }
        return sound.StartsWith(FilePrefix) ? Path.GetFileNameWithoutExtension(sound.Substring(FilePrefix.Length)) : sound;
    }

    public static string Dir;   // the user's sounds: <data folder>\sounds
    static readonly Dictionary<string, string> map = new Dictionary<string, string>();
    public static Dictionary<string, string> Map { get { return map; } }

    public static string Of(int e)
    {
        string s;
        return map.TryGetValue(Events[e], out s) ? s : Default(e);
    }

    // The user's .wav files, by name (at most a hundred, each up to 5 MB)
    public static List<string> Files()
    {
        var list = new List<string>();
        try
        {
            if (Dir != null && Directory.Exists(Dir))
                foreach (var f in new DirectoryInfo(Dir).GetFiles("*.wav").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Take(100))
                    if (f.Length <= MaxFile) list.Add(FilePrefix + f.Name);
        }
        catch { }
        return list;
    }

    const long MaxFile = 5 * 1024 * 1024;

    public static void Play(int e) { Play(Of(e), Theme.SoundSkin); }

    // A sound as it would play with a look: "look" is that look's own
    public static void Play(string sound, Skin look)
    {
        if (sound == Look) sound = look.Sound;
        if (sound == None || string.IsNullOrEmpty(sound)) return;
        try
        {
            System.Media.SoundPlayer p;
            if (!players.TryGetValue(sound, out p))
            {
                if (sound.StartsWith(FilePrefix))
                {
                    // Only a plain name in the sounds folder, never a path that leads elsewhere
                    string name = sound.Substring(FilePrefix.Length);
                    if (Dir == null || name != Path.GetFileName(name)) return;
                    var f = new FileInfo(Path.Combine(Dir, name));
                    if (!f.Exists || f.Length > MaxFile) return;
                    p = new System.Media.SoundPlayer(new MemoryStream(File.ReadAllBytes(f.FullName)));
                }
                else
                {
                    byte[] wave = Make(sound);
                    if (wave == null) return;
                    p = new System.Media.SoundPlayer(new MemoryStream(wave));
                }
                p.Load();
                players[sound] = p;
            }
            p.Play();
        }
        catch { }
    }

    // A sound file may have changed or been added while KeyFish runs: Settings forgets the loaded ones when saved
    public static void Forget()
    {
        foreach (string key in players.Keys.Where(k => k.StartsWith(FilePrefix)).ToList()) { players[key].Dispose(); players.Remove(key); }
    }

    static readonly Dictionary<string, System.Media.SoundPlayer> players = new Dictionary<string, System.Media.SoundPlayer>();

    public static Dictionary<string, string> Parse(string text)
    {
        var m = new Dictionary<string, string>();
        foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            int eq = raw.IndexOf('=');
            if (eq <= 0) continue;
            string e = raw.Substring(0, eq).Trim(), s = raw.Substring(eq + 1).Trim();
            if (Array.IndexOf(Events, e) >= 0 && Known(s)) m[e] = s;
        }
        return m;
    }

    static bool Known(string s)
    {
        return s == Look || s == None || Array.IndexOf(Builtin, s) >= 0 ||
               (s.StartsWith(FilePrefix) && s.Length > FilePrefix.Length && s.Substring(FilePrefix.Length) == Path.GetFileName(s.Substring(FilePrefix.Length)));
    }

    public static string Format(Dictionary<string, string> m)
    {
        var sb = new StringBuilder();
        foreach (string e in Events) { string s; if (m.TryGetValue(e, out s)) sb.Append(e).Append(" = ").Append(s).Append("\r\n"); }
        return sb.ToString();
    }

    public static void Load(Dictionary<string, string> m)
    {
        map.Clear();
        foreach (var kv in m) map[kv.Key] = kv.Value;
    }

    // ---------- The built-in sounds ----------

    const int Rate = 44100;

    internal static byte[] Make(string name)
    {
        switch (name)
        {
            // A rising "bloop": 320 → about 1000 Hz
            case "bloop": return Tone(110, t => 320 * Math.Pow(3.2, t), t => Math.Exp(-4.5 * t), 0.28, 0, 0);
            // The same falling, for an undo
            case "down": return Tone(130, t => 1000 * Math.Pow(0.32, t), t => Math.Exp(-4 * t), 0.26, 0, 0);
            // A water drop: a quick fall from high up
            case "drop": return Tone(70, t => 1500 * Math.Pow(0.35, t), t => Math.Exp(-6 * t), 0.26, 0, 0);
            // A soft wooden click
            case "click": return Tone(28, t => 1800 - 600 * t, t => Math.Exp(-12 * t), 0.22, 0.3, 0);
            // A small bell: a tone with an upper partial and a long fade
            case "chime": return Tone(420, t => 1320, t => Math.Exp(-5.5 * t), 0.13, 0.5, 0);
            // A ghostly "ooo": 620 → 385 Hz, wavering, with two fading echoes
            case "ghost": return Tone(260, t => 620 * Math.Pow(0.62, t), t => Math.Min(1, t / 0.115) * (1 - 0.4 * t), 0.18, 0.25, 110, 7);
        }
        return null;
    }

    // One tone as a WAV: ms long, its pitch and loudness over t = 0…1, an upper partial (an octave up, by its share),
    // echoes after echoMs (two, fading), and a wobble of the pitch (Hz). Silence before: an idle sound device (many cards,
    // Bluetooth headphones) wakes up on the first samples and drops them. Silence after: some drivers cut the end
    // of a buffer, which sounds like a chopped-off tail
    static byte[] Tone(int ms, Func<double, double> freq, Func<double, double> envelope, double volume, double upper, int echoMs, double wobble = 0)
    {
        int lead = Rate * 250 / 1000, n = Rate * ms / 1000, echo = Rate * echoMs / 1000, tail = Rate * 150 / 1000;
        var mix = new double[n + echo * 2];
        double phase = 0;
        int fadeIn = Rate * 4 / 1000, fadeOut = Rate * 8 / 1000;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double f = freq(t) * (1 + (wobble > 0 ? 0.03 * Math.Sin(2 * Math.PI * wobble * i / Rate) : 0));
            phase += 2 * Math.PI * f / Rate;
            double e = envelope(t) * Math.Min(1, (double)i / fadeIn);
            if (i > n - fadeOut) e *= (double)(n - i) / fadeOut;   // down to zero at the end: no click
            double s = (Math.Sin(phase) + upper * Math.Sin(2 * phase)) * e * volume;
            mix[i] += s;
            if (echo > 0) { mix[i + echo] += s * 0.35; mix[i + echo * 2] += s * 0.12; }
        }
        int total = lead + mix.Length + tail;
        var stream = new MemoryStream();
        var w = new BinaryWriter(stream);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + total * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(total * 2);
        for (int i = 0; i < lead; i++) w.Write((short)0);
        foreach (double s in mix) w.Write((short)(Math.Max(-1, Math.Min(1, s)) * short.MaxValue));
        for (int i = 0; i < tail; i++) w.Write((short)0);
        w.Flush();
        return stream.ToArray();
    }
}
