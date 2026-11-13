using System.Drawing.Imaging;
using System.IO;

// The detailed logo (FishLogo, the one in About) as PNG files, one per size, in the classic blue whatever the skin:
// tools\make-icon.ps1 puts them into fish.ico as its larger frames.
// Usage: LogoIcon.exe <out dir> <size> [size ...]   — writes logo-<size>.png
static class LogoIcon
{
    static void Main(string[] args)
    {
        Theme.Skin = Skin.Classic;
        Theme.SkinLogo = false;
        Directory.CreateDirectory(args[0]);
        for (int i = 1; i < args.Length; i++)
        {
            int size = int.Parse(args[i]);
            using (var bmp = FishLogo.Render(size)) bmp.Save(Path.Combine(args[0], "logo-" + size + ".png"), ImageFormat.Png);
        }
    }
}
