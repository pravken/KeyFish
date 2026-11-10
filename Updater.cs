using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// Checks the latest GitHub release for a new version and updates in one click
static class Updater
{
    public const string Repo = "pravken/KeyFish";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
    const string DownloadPrefix = "https://github.com/" + Repo + "/releases/download/";

    // The public half of the key releases are signed with (ECDSA P-256, Windows' ECCPUBLICBLOB, base64). The private
    // half never leaves the maintainer's computer (tools/sign-release.ps1), so whoever takes over the repository or
    // the build can't publish an update KeyFish would install. Empty: no signature is asked for, only the checksum
    const string UpdateKey = "";

    public class Release
    {
        public Version Version;
        public string Tag, Url, ExeUrl, ShaUrl, SigUrl;
        public bool CanInstall { get { return ExeUrl != null && ShaUrl != null && (UpdateKey.Length == 0 || SigUrl != null); } }
    }

    public static Version Current
    {
        get { return new Version(Application.ProductVersion.Split('-')[0]); }
    }

    public static WebClient Client()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var web = new WebClient();
        web.Headers[HttpRequestHeader.UserAgent] = "KeyFish/" + Application.ProductVersion;
        return web;
    }

    // The latest release, or null if there are no releases or GitHub is unreachable
    public static Release GetLatest()
    {
        using (var web = Client())
        {
            web.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            string json;
            try { json = web.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest"); }
            catch (WebException) { return null; }

            var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9][0-9.]*)[^\"]*\"");
            var url = Regex.Match(json, "\"html_url\"\\s*:\\s*\"(https://github\\.com/[^\"]+/releases/tag/[^\"]+)\"");
            Version v;
            if (!tag.Success || !Version.TryParse(tag.Groups[1].Value, out v)) return null;
            var release = new Release { Version = v, Tag = tag.Groups[1].Value, Url = url.Success ? url.Groups[1].Value : ReleasesPage };

            // Download only files from this repository's releases
            foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
            {
                string u = m.Groups[1].Value;
                if (!u.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue;
                if (u.EndsWith("/KeyFish.exe", StringComparison.Ordinal)) release.ExeUrl = u;
                else if (u.EndsWith("/KeyFish.exe.sha256", StringComparison.Ordinal)) release.ShaUrl = u;
                else if (u.EndsWith("/KeyFish.exe.sig", StringComparison.Ordinal)) release.SigUrl = u;
            }
            return release;
        }
    }

    public static string ExePath { get { return Application.ExecutablePath; } }
    public static string OldPath { get { return Path.Combine(Path.GetDirectoryName(ExePath), "KeyFish.old.exe"); } }
    static string NewPath { get { return Path.Combine(Path.GetDirectoryName(ExePath), "KeyFish.new.exe"); } }

    // Downloads the new version, verifies its checksum and puts it in place of the current one.
    // Returns null on success or an error message; on error the old version stays as it was
    public static string Install(Release r)
    {
        if (!r.CanInstall) return L.T("В этом релизе нет файла для автообновления.", "This release has no file for automatic update.");
        try
        {
            if (File.Exists(NewPath)) File.Delete(NewPath);
            string expected;
            byte[] signature = null;
            using (var web = Client())
            {
                expected = web.DownloadString(r.ShaUrl).Trim().Split(' ', '\t')[0].ToLowerInvariant();
                if (UpdateKey.Length > 0) signature = web.DownloadData(r.SigUrl);
                web.DownloadFile(r.ExeUrl, NewPath);
            }

            string actual;
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(NewPath))
                actual = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
            if (expected.Length != 64 || actual != expected)
            {
                File.Delete(NewPath);
                return L.T("Скачанный файл не совпал с контрольной суммой релиза — обновление отменено.",
                           "The downloaded file doesn't match the release checksum — update cancelled.");
            }

            if (UpdateKey.Length > 0 && !SignedByMaintainer(NewPath, signature))
            {
                File.Delete(NewPath);
                return L.T("Подпись скачанного файла не подтверждена — обновление отменено.",
                           "The downloaded file's signature could not be verified — update cancelled.");
            }

            string version = FileVersionInfo.GetVersionInfo(NewPath).ProductVersion;
            if (version != r.Tag)
            {
                File.Delete(NewPath);
                return L.T("В скачанном файле не та версия — обновление отменено.", "The downloaded file has the wrong version — update cancelled.");
            }

            // A running exe can't be deleted but can be renamed; the old copy is removed on the next start
            if (File.Exists(OldPath)) File.Delete(OldPath);
            File.Move(ExePath, OldPath);
            try { File.Move(NewPath, ExePath); }
            catch { File.Move(OldPath, ExePath); throw; }
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return L.T("Нет прав на запись в папку программы. Загрузите новую версию вручную.",
                       "No permission to write to the app folder. Download the new version manually.");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(NewPath)) File.Delete(NewPath); } catch { }
            return L.T("Не удалось обновить: ", "Update failed: ") + ex.Message;
        }
    }

    // The file's ECDSA signature (64 bytes: r and s) checked against UpdateKey
    static bool SignedByMaintainer(string path, byte[] signature)
    {
        try
        {
            if (signature == null || signature.Length != 64) return false;
            using (var key = CngKey.Import(Convert.FromBase64String(UpdateKey), CngKeyBlobFormat.EccPublicBlob))
            using (var ecdsa = new ECDsaCng(key) { HashAlgorithm = CngAlgorithm.Sha256 })
                return ecdsa.VerifyData(File.ReadAllBytes(path), signature);
        }
        catch { return false; }
    }

    // Remove the old copy left behind by an update
    public static void CleanUp()
    {
        try { if (File.Exists(OldPath)) File.Delete(OldPath); } catch { }
    }
}

// Extended dictionaries: about a hundred thousand more words for each language of the installed layouts (rare words,
// names, slang). Downloaded only when the user asks, from this repository's release Tag, and checked against the
// checksums published with it. They live next to the settings, in the "dictionaries" folder
static class Dictionaries
{
    public const string Tag = "dictionaries-2";
    const string Prefix = "https://github.com/" + Updater.Repo + "/releases/download/" + Tag + "/";
    const long MaxSize = 16 * 1024 * 1024;

    public static string Dir;   // set at start

    // Languages the release has a dictionary for (Belarusian has no frequency list, Georgian no more words to add)
    public static readonly string[] Available =
        { "en", "ru", "uk", "de", "pl", "bg", "mk", "el", "he", "fr", "es", "it", "pt", "cs", "tr", "sr-latn", "sr-cyrl" };

    static string PathOf(string lang) { return Path.Combine(Dir, lang + ".txt.gz"); }

    public static bool Installed(string lang) { return Dir != null && File.Exists(PathOf(lang)); }

    // The words of an installed dictionary ("word count" lines), read as they are used: the whole file was once held in
    // memory at once, tens of megabytes. Nothing if there is none or it can't be opened; the words up to the damage if
    // the file breaks off
    public static IEnumerable<string> Lines(string lang)
    {
        if (!Installed(lang)) yield break;
        StreamReader reader = null;
        try
        {
            reader = new StreamReader(new System.IO.Compression.GZipStream(File.OpenRead(PathOf(lang)),
                                                                          System.IO.Compression.CompressionMode.Decompress), System.Text.Encoding.UTF8);
        }
        catch { }
        if (reader == null) yield break;
        using (reader)
        {
            for (int n = 0; n < 2000000; n++)
            {
                string line;
                try { line = reader.ReadLine(); }
                catch { line = null; }
                if (line == null) yield break;
                yield return line;
            }
        }
    }

    // Downloads the dictionaries of these languages (those the release has); null on success or an error message.
    // A file that doesn't match its checksum is thrown away
    public static string Download(IEnumerable<string> langs)
    {
        string current = null;
        try
        {
            Directory.CreateDirectory(Dir);
            var sums = new Dictionary<string, string>();
            using (var web = Updater.Client())
            {
                foreach (string line in web.DownloadString(Prefix + "dictionaries.sha256").Split('\n'))
                {
                    var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && parts[0].Length == 64) sums[parts[1]] = parts[0].ToLowerInvariant();
                }
                foreach (string lang in langs)
                {
                    string expected;
                    if (!sums.TryGetValue(lang + ".txt.gz", out expected)) continue;
                    current = lang;
                    string temp = PathOf(lang) + ".download";
                    web.DownloadFile(Prefix + lang + ".txt.gz", temp);
                    string actual;
                    using (var sha = SHA256.Create())
                    using (var f = File.OpenRead(temp))
                        actual = f.Length > MaxSize ? "" : BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                    if (actual != expected)
                    {
                        File.Delete(temp);
                        return L.T("Загруженный словарь не совпал с контрольной суммой и не установлен.",
                                   "The downloaded dictionary does not match its checksum and was not installed.");
                    }
                    if (File.Exists(PathOf(lang))) File.Delete(PathOf(lang));
                    File.Move(temp, PathOf(lang));
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            try { if (current != null && File.Exists(PathOf(current) + ".download")) File.Delete(PathOf(current) + ".download"); } catch { }
            return L.T("Не удалось загрузить словарь: ", "Could not download the dictionary: ") + ex.Message;
        }
    }

    public static void Remove(IEnumerable<string> langs)
    {
        foreach (string lang in langs) try { if (Installed(lang)) File.Delete(PathOf(lang)); } catch { }
    }
}
