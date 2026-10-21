using System;
using System.IO;
using System.Linq;
using System.Text;

// Checks Layouts.cs: tables read from Windows for installed layouts must match the same layouts' .klc files,
// and prints the key rows of every .klc table so they can be eyeballed
static class LayoutCheck
{
    static string Row(LayoutTable t, int from, int to, bool shift)
    {
        var sb = new StringBuilder();
        for (int s = from; s <= to; s++) { char c = t.CharFor(s, shift, false, false); sb.Append(c == '\0' ? '·' : c); }
        return sb.ToString();
    }

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string dir = args[0];
        var installed = LayoutTable.Installed();
        foreach (var t in installed)
            Console.WriteLine("installed: {0} hkl={1:X} lang={2} cyrillic={3}  {4}|{5}|{6}", t.Name, t.Hkl.ToInt64(), t.Lang ?? "-", t.Cyrillic,
                Row(t, 0x10, 0x1B, false), Row(t, 0x1E, 0x28, false), Row(t, 0x2C, 0x35, false));

        foreach (string file in Directory.GetFiles(dir, "*.klc").OrderBy(f => f))
        {
            var k = LayoutTable.FromKlc(file, Path.GetFileName(file) == "kbdus.klc" ? "en" : null);
            Console.WriteLine("{0,-7} lang={1,-8} {2}|{3}|{4}  shift: {5}", k.Name, k.Lang ?? "-",
                Row(k, 0x10, 0x1B, false), Row(k, 0x1E, 0x28, false), Row(k, 0x2C, 0x35, false), Row(k, 0x10, 0x19, true));
            var match = installed.FirstOrDefault(t => t.Lang == k.Lang && t.Lang != null);
            if (match == null) continue;
            int diff = 0;
            for (int s = 1; s < 0x60; s++)
                for (int st = 0; st < 2; st++)
                    { char a = match.CharFor(s, st == 1, false, false), b = k.CharFor(s, st == 1, false, false); if (a != b) { diff++; Console.WriteLine("      scan {0:X2} {1}: windows [{2}] klc [{3}]", s, st == 1 ? "shift" : "plain", a == '\0' ? "none" : a.ToString(), b == '\0' ? "none" : b.ToString()); } }
            Console.WriteLine("   vs Windows {0}: {1} differences", match.Name, diff);
        }
    }
}
