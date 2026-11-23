using System;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SystemMonitor
{
    /// <summary>
    /// Анонімна статистика використання. Працює ЛИШЕ якщо користувач погодився (запитується при першому запуску)
    /// і якщо під час збірки вказано адресу збору. Надсилається: випадковий номер установки, версія програми,
    /// версія Windows і мова системи. Імена, файли, паролі, IP-адреса та інші особисті дані не збираються.
    /// </summary>
    internal static class Telemetry
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        static DispatcherTimer first, timer;
        public static volatile string Status = "вимкнено";

        public static string Url
        {
            get
            {
                try
                {
                    foreach (AssemblyMetadataAttribute a in Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>())
                        if (a.Key == "StatsUrl" && !string.IsNullOrWhiteSpace(a.Value)) return a.Value.Trim();
                }
                catch { }
                return "";
            }
        }

        public static bool Available { get { return Url.Length > 0; } }

        public static void Start()
        {
            if (!Available) return;
            first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
            first.Tick += async delegate { first.Stop(); await SendAsync(); };
            first.Start();
            timer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            timer.Tick += async delegate { await SendAsync(); };
            timer.Start();
        }

        static async Task SendAsync()
        {
            AppConfig c = AppConfig.Cur;
            if (!Available || !c.StatsAsked || !c.StatsEnabled) { Status = "вимкнено"; return; }
            try
            {
                if (string.IsNullOrEmpty(c.InstallId)) { c.InstallId = Guid.NewGuid().ToString("N"); c.Save(); }
                string json = JsonSerializer.Serialize(new
                {
                    id = c.InstallId,
                    version = Updater.CurrentText,
                    os = Environment.OSVersion.Version.ToString(),
                    lang = CultureInfo.InstalledUICulture.Name,
                });
                HttpResponseMessage resp = await Http.PostAsync(Url, new StringContent(json, Encoding.UTF8, "application/json"));
                Status = resp.IsSuccessStatusCode ? "надіслано " + DateTime.Now.ToString("HH:mm") : "сервер відповів " + (int)resp.StatusCode;
            }
            catch (Exception ex)
            {
                Status = "не вдалося надіслати";
                Log.Write("статистика: " + ex.Message);
            }
        }
    }
}
