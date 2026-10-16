using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// Проверка новой версии по последнему релизу на GitHub
static class Updater
{
    public const string Repo = "pravken/KeyFish";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";

    public class Release
    {
        public Version Version;
        public string Tag, Url;
    }

    public static Version Current
    {
        get { return new Version(Application.ProductVersion.Split('-')[0]); }
    }

    // Последний релиз или null, если релизов нет или GitHub недоступен
    public static Release GetLatest()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using (var web = new WebClient())
        {
            web.Headers[HttpRequestHeader.UserAgent] = "KeyFish/" + Application.ProductVersion;
            web.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            string json;
            try { json = web.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest"); }
            catch (WebException) { return null; }

            var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9][0-9.]*)[^\"]*\"");
            var url = Regex.Match(json, "\"html_url\"\\s*:\\s*\"(https://github\\.com/[^\"]+/releases/tag/[^\"]+)\"");
            Version v;
            if (!tag.Success || !Version.TryParse(tag.Groups[1].Value, out v)) return null;
            return new Release { Version = v, Tag = tag.Groups[1].Value, Url = url.Success ? url.Groups[1].Value : ReleasesPage };
        }
    }
}
