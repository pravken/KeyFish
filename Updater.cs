using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// Проверка новой версии по последнему релизу на GitHub и обновление в один клик
static class Updater
{
    public const string Repo = "pravken/KeyFish";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
    const string DownloadPrefix = "https://github.com/" + Repo + "/releases/download/";

    public class Release
    {
        public Version Version;
        public string Tag, Url, ExeUrl, ShaUrl;
        public bool CanInstall { get { return ExeUrl != null && ShaUrl != null; } }
    }

    public static Version Current
    {
        get { return new Version(Application.ProductVersion.Split('-')[0]); }
    }

    static WebClient Client()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var web = new WebClient();
        web.Headers[HttpRequestHeader.UserAgent] = "KeyFish/" + Application.ProductVersion;
        return web;
    }

    // Последний релиз или null, если релизов нет или GitHub недоступен
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

            // Скачиваем только файлы из релизов этого репозитория
            foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
            {
                string u = m.Groups[1].Value;
                if (!u.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue;
                if (u.EndsWith("/KeyFish.exe", StringComparison.Ordinal)) release.ExeUrl = u;
                else if (u.EndsWith("/KeyFish.exe.sha256", StringComparison.Ordinal)) release.ShaUrl = u;
            }
            return release;
        }
    }

    public static string ExePath { get { return Application.ExecutablePath; } }
    public static string OldPath { get { return Path.Combine(Path.GetDirectoryName(ExePath), "KeyFish.old.exe"); } }
    static string NewPath { get { return Path.Combine(Path.GetDirectoryName(ExePath), "KeyFish.new.exe"); } }

    // Скачивает новую версию, сверяет контрольную сумму и ставит на место текущей.
    // Возвращает null при успехе или текст ошибки; старая версия при ошибке остаётся как была
    public static string Install(Release r)
    {
        if (!r.CanInstall) return L.T("В этом релизе нет файла для автообновления.", "This release has no file for automatic update.");
        try
        {
            if (File.Exists(NewPath)) File.Delete(NewPath);
            string expected;
            using (var web = Client())
            {
                expected = web.DownloadString(r.ShaUrl).Trim().Split(' ', '\t')[0].ToLowerInvariant();
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

            string version = FileVersionInfo.GetVersionInfo(NewPath).ProductVersion;
            if (version != r.Tag)
            {
                File.Delete(NewPath);
                return L.T("В скачанном файле не та версия — обновление отменено.", "The downloaded file has the wrong version — update cancelled.");
            }

            // Запущенный exe нельзя удалить, но можно переименовать: старая копия удалится при следующем запуске
            if (File.Exists(OldPath)) File.Delete(OldPath);
            File.Move(ExePath, OldPath);
            try { File.Move(NewPath, ExePath); }
            catch { File.Move(OldPath, ExePath); throw; }
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return L.T("Нет прав на запись в папку программы. Скачайте новую версию вручную.",
                       "No permission to write to the app folder. Please download the new version manually.");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(NewPath)) File.Delete(NewPath); } catch { }
            return L.T("Не удалось обновить: ", "Update failed: ") + ex.Message;
        }
    }

    // Убираем старую копию, оставшуюся после обновления
    public static void CleanUp()
    {
        try { if (File.Exists(OldPath)) File.Delete(OldPath); } catch { }
    }
}
