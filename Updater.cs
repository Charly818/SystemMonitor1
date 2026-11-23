using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SystemMonitor
{
    /// <summary>
    /// Автооновлення через GitHub Releases: перевіряє останній опублікований реліз, завантажує SystemMonitor.exe,
    /// підміняє себе й перезапускається. Чернетки (draft) релізів користувачі не бачать.
    /// </summary>
    internal static class Updater
    {
        static readonly HttpClient Http = Create();
        static bool busy;
        static string lastPrompted = "";
        static DispatcherTimer timer, first;

        public static volatile string Status = "ще не перевіряли";

        static HttpClient Create()
        {
            HttpClient c = new HttpClient();
            c.Timeout = TimeSpan.FromMinutes(10);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("system-monitor-updater/1.0");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        public static Version Current
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v ?? new Version(0, 0, 0, 0);
            }
        }

        public static string CurrentText { get { Version v = Current; return v.Major + "." + v.Minor + "." + Math.Max(v.Build, 0); } }

        /// <summary>Репозиторій "власник/назва": вшивається під час збірки в GitHub Actions, інакше береться з налаштувань.</summary>
        public static string Repo
        {
            get
            {
                try
                {
                    foreach (AssemblyMetadataAttribute a in Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>())
                        if (a.Key == "UpdateRepo" && !string.IsNullOrWhiteSpace(a.Value)) return a.Value.Trim();
                }
                catch { }
                return (AppConfig.Cur.UpdateRepo ?? "").Trim();
            }
        }

        static Version Norm(Version v)
        {
            return new Version(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        }

        static bool TryParse(string tag, out Version v)
        {
            v = null;
            if (string.IsNullOrWhiteSpace(tag)) return false;
            string t = tag.Trim().TrimStart('v', 'V');
            int dash = t.IndexOf('-');
            if (dash >= 0) t = t.Substring(0, dash);
            if (!Version.TryParse(t, out v)) return false;
            v = Norm(v);
            return true;
        }

        /// <summary>Прибирає файли, що лишилися після попереднього оновлення.</summary>
        public static void Cleanup()
        {
            try
            {
                string dir = Path.GetDirectoryName(Environment.ProcessPath ?? "");
                if (string.IsNullOrEmpty(dir)) return;
                foreach (string n in new[] { "SystemMonitor.old.exe", "SystemMonitor.new.exe" })
                {
                    string f = Path.Combine(dir, n);
                    if (File.Exists(f)) { try { File.Delete(f); } catch { } }
                }
            }
            catch { }
        }

        public static void Start()
        {
            first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            first.Tick += async delegate { first.Stop(); await CheckAsync(false); };
            first.Start();
            timer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            timer.Tick += async delegate { await CheckAsync(false); };
            timer.Start();
        }

        public static async Task CheckAsync(bool manual)
        {
            if (busy) return;
            busy = true;
            try
            {
                string repo = Repo;
                if (string.IsNullOrEmpty(repo)) { Status = "репозиторій не вказано"; return; }
                Status = "перевіряю…";
                string json = await Http.GetStringAsync("https://api.github.com/repos/" + repo + "/releases/latest");
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    string tag = root.GetProperty("tag_name").GetString();
                    Version latest;
                    if (!TryParse(tag, out latest)) { Status = "не вдалося розпізнати версію " + tag; return; }
                    if (latest <= Norm(Current)) { Status = "у вас остання версія (" + CurrentText + ")"; return; }

                    string url = null, digest = null;
                    long size = 0;
                    foreach (JsonElement a in root.GetProperty("assets").EnumerateArray())
                    {
                        if (a.GetProperty("name").GetString() != "SystemMonitor.exe") continue;
                        url = a.GetProperty("browser_download_url").GetString();
                        size = a.GetProperty("size").GetInt64();
                        JsonElement d;
                        if (a.TryGetProperty("digest", out d) && d.ValueKind == JsonValueKind.String) digest = d.GetString();
                        break;
                    }
                    if (url == null) { Status = "у релізі " + tag + " немає SystemMonitor.exe"; return; }

                    Status = "доступна версія " + tag;
                    bool go = AppConfig.Cur.AutoUpdate;
                    if (!go && (manual || lastPrompted != tag))
                    {
                        lastPrompted = tag;
                        go = MessageBox.Show("Доступна нова версія " + tag + " (у вас " + CurrentText + ").\nОновити зараз? Програма перезапуститься.",
                            "Системний монітор: оновлення", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                    }
                    if (go) await ApplyAsync(url, size, digest, tag);
                }
            }
            catch (Exception ex)
            {
                Log.Write("оновлення: " + ex);
                Status = manual ? "помилка: " + ex.Message : "не вдалося перевірити оновлення";
            }
            finally { busy = false; }
        }

        static async Task ApplyAsync(string url, long size, string digest, string tag)
        {
            string cur = Environment.ProcessPath;
            if (string.IsNullOrEmpty(cur)) throw new Exception("не вдалося визначити шлях до програми");
            string dir = Path.GetDirectoryName(cur);
            string nw = Path.Combine(dir, "SystemMonitor.new.exe");
            string old = Path.Combine(dir, "SystemMonitor.old.exe");

            Status = "завантажую " + tag + "…";
            byte[] data = await Http.GetByteArrayAsync(url);
            if (size > 0 && data.Length != size) throw new Exception("розмір завантаженого файлу не збігається");
            if (data.Length < 1000000 || data[0] != (byte)'M' || data[1] != (byte)'Z') throw new Exception("завантажений файл не схожий на програму");
            if (!string.IsNullOrEmpty(digest) && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                using (SHA256 sha = SHA256.Create())
                {
                    string h = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
                    if (!h.Equals(digest.Substring(7), StringComparison.OrdinalIgnoreCase)) throw new Exception("контрольна сума не збігається");
                }
            }

            File.WriteAllBytes(nw, data);
            Status = "встановлюю " + tag + "…";
            try { if (File.Exists(old)) File.Delete(old); } catch { }
            File.Move(cur, old);                       // запущену програму можна перейменувати, але не перезаписати
            try { File.Move(nw, cur); }
            catch { File.Move(old, cur); throw; }      // якщо не вийшло, повертаємо стару

            Log.Write("оновлено до " + tag + ", перезапуск");
            Process.Start(new ProcessStartInfo(cur, "--restarted") { UseShellExecute = true, WorkingDirectory = dir });
            AppCore.Quit();
        }
    }
}
