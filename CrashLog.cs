using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// crash.log next to KeyFish's settings: what went wrong and where in KeyFish's code, so a crash can be found and fixed.
// It never holds typed text or the clipboard, and nothing is sent anywhere: the user decides whether to pass it on.
// A crash of the .NET runtime itself (0xc0000005 in clr.dll) can't be caught from inside; Windows keeps a record of it
// in its event log, and KeyFish copies that record here the next time it starts (or, for the guard process, at once)
static class CrashLog
{
    static string path, marker;
    static readonly object gate = new object();
    const long MaxSize = 256 * 1024;   // past this the older half goes

    public static string Path { get { return path; } }

    // Windows' record of the crash that ended the last run, once found (KeyFish then offers to report it)
    public static volatile string LastCrash;

    // The user's own folder (and so their Windows user name) never goes into the log
    static string Private(string text)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home) ? text : System.Text.RegularExpressions.Regex.Replace(text,
            System.Text.RegularExpressions.Regex.Escape(home), "%USERPROFILE%", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    // At the very start of a process. guard: the password-guard process, which only writes its own errors
    public static void Start(string dir, bool guard)
    {
        path = System.IO.Path.Combine(dir, "crash.log");
        string role = guard ? "guard" : "main";
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Write("crash (" + role + ")", e.ExceptionObject as Exception);
        if (guard) return;
        // Errors on the window thread: written down, and KeyFish carries on instead of showing .NET's error box
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => Write("error", e.Exception);

        // The marker exists while KeyFish runs. Found at start, it means the last run didn't end by Exit or an update:
        // a crash, or Windows shutting down. Windows' event log tells which
        marker = System.IO.Path.Combine(dir, "~keyfish.running");
        DateTime since = DateTime.MinValue;
        try { if (File.Exists(marker)) since = File.GetLastWriteTimeUtc(marker); } catch { }
        try { File.WriteAllText(marker, DateTime.UtcNow.ToString("o")); } catch { }
        if (since != DateTime.MinValue)
            ThreadPool.QueueUserWorkItem(_ => FromWindows(since, DateTime.UtcNow, "the last run"));
    }

    // KeyFish is closing on purpose
    public static void Stop()
    {
        try { if (marker != null) File.Delete(marker); } catch { }
    }

    public static void Write(string what, Exception ex)
    {
        Append(what, ex == null ? "(no details)" : ex.ToString());
    }

    // Windows' own records of KeyFish crashing between from and to (UTC): Application Error, .NET Runtime, Windows
    // Error Reporting. Written only if there are any, so a Windows shutdown leaves nothing
    public static void FromWindows(DateTime from, DateTime to, string which)
    {
        try
        {
            string query = "*[System[Provider[@Name='Application Error' or @Name='.NET Runtime' or @Name='Windows Error Reporting'] and " +
                           "TimeCreated[@SystemTime>='" + from.AddSeconds(-5).ToString("o") + "' and @SystemTime<='" + to.AddMinutes(1).ToString("o") + "']]]";
            var found = new StringBuilder();
            int count = 0;
            using (var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(
                new System.Diagnostics.Eventing.Reader.EventLogQuery("Application", System.Diagnostics.Eventing.Reader.PathType.LogName, query)))
            {
                System.Diagnostics.Eventing.Reader.EventRecord record;
                while (count < 10 && (record = reader.ReadEvent()) != null)
                    using (record)
                    {
                        string text;
                        try { text = record.FormatDescription(); } catch { text = null; }
                        if (text == null || text.IndexOf("KeyFish", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        found.Append(record.TimeCreated.HasValue ? record.TimeCreated.Value.ToString("yyyy-MM-dd HH:mm:ss") : "?")
                             .Append("  ").Append(record.ProviderName).Append(" ").Append(record.Id).Append("\r\n")
                             .Append(text.Trim()).Append("\r\n\r\n");
                        count++;
                    }
            }
            if (count == 0) return;
            Append("Windows recorded a crash during " + which, found.ToString().TrimEnd());
            if (which == "the last run") LastCrash = Private(found.ToString().TrimEnd());
        }
        catch (Exception ex) { Append("couldn't read Windows' event log", ex.Message); }
    }

    // "Windows 10.0 build 26200 (25H2)": Environment.OSVersion says 6.2 to apps without a Windows 10 manifest
    static string WindowsVersion()
    {
        try
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                if (k != null)
                    return "Windows " + k.GetValue("CurrentMajorVersionNumber") + "." + k.GetValue("CurrentMinorVersionNumber") +
                           " build " + k.GetValue("CurrentBuild") + "." + k.GetValue("UBR") + " (" + k.GetValue("DisplayVersion") + ")";
        }
        catch { }
        return Environment.OSVersion.VersionString;
    }

    static void Append(string what, string details)
    {
        if (path == null) return;
        try
        {
            string entry = "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  KeyFish " + Application.ProductVersion +
                           "  " + WindowsVersion() + (Environment.Is64BitProcess ? " x64" : " x86") + "\r\n" +
                           what + "\r\n" + Private(details) + "\r\n\r\n";
            lock (gate)
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxSize)
                {
                    string old = File.ReadAllText(path, Encoding.UTF8);
                    int cut = old.IndexOf("=== ", old.Length / 2, StringComparison.Ordinal);
                    File.WriteAllText(path, cut > 0 ? old.Substring(cut) : "", Encoding.UTF8);
                }
                File.AppendAllText(path, entry, Encoding.UTF8);
            }
        }
        catch { }
    }
}
