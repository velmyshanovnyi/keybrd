using System;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Threading;

namespace Keybrd.Core
{
    /// <summary>
    /// Перевірка нової версії на GitHub. Програма лише читає маленький файл dist/version.txt з репозиторію
    /// і порівнює з власною версією; нічого про комп'ютер чи користувача не надсилає і сама себе не оновлює.
    /// Без інтернету мовчить і запуск не затримує: запит іде у фоновому потоці з коротким тайм-аутом.
    /// </summary>
    public static class Updater
    {
        public const string RepoUrl = "https://github.com/velmyshanovnyi/keybrd";
        public const string DownloadUrl = RepoUrl + "/raw/main/dist/Keybrd.exe";
        private const string VersionUrl = "https://raw.githubusercontent.com/velmyshanovnyi/keybrd/main/dist/version.txt";

        public static Version Current
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        /// <summary>Найновіша версія, знайдена за цей запуск (null — не перевіряли або нової немає).</summary>
        public static Version Newer { get; private set; }

        /// <summary>Перевірити у фоні. onDone викликається у фоновому потоці: нова версія або null.</summary>
        public static void Check(Action<Version> onDone)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Version found = null;
                try
                {
                    string url = AppConfig.Current.DebugUpdateUrl;
                    if (string.IsNullOrEmpty(url)) url = VersionUrl;
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    using (var client = new QuickClient())
                    {
                        string text = client.DownloadString(url).Trim();
                        if (text.Length > 0 && text.Length < 40 && Version.TryParse(text, out var remote))
                        {
                            remote = new Version(remote.Major, remote.Minor, Math.Max(0, remote.Build));
                            if (remote > Current) found = remote;
                        }
                    }
                }
                catch { /* немає інтернету, файлу чи доступу — це не помилка для користувача */ }
                Newer = found;
                DebugLog.Write("UPDATE", found == null ? "none" : found.ToString());
                try { onDone?.Invoke(found); } catch { }
            });
        }

        public static void OpenDownload()
        {
            try { Process.Start(new ProcessStartInfo(DownloadUrl) { UseShellExecute = true }); } catch { }
        }

        public static void OpenRepo()
        {
            try { Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true }); } catch { }
        }

        /// <summary>WebClient з тайм-аутом 6 секунд і без кешу.</summary>
        private sealed class QuickClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                var r = base.GetWebRequest(address);
                if (r != null)
                {
                    r.Timeout = 6000;
                    r.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                    if (r is HttpWebRequest h) { h.ReadWriteTimeout = 6000; h.UserAgent = "Keybrd"; }
                }
                return r;
            }
        }
    }
}
