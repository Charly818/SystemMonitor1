using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SystemMonitor
{
    /// <summary>Завантаження PresentMon, встановлення драйвера PawnIO, автозапуск через Планувальник завдань.</summary>
    internal sealed class Components
    {
        const string PmApi = "https://api.github.com/repos/GameTechDev/PresentMon/releases/latest";
        const string PawnIoUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
        const string TaskName = "SystemMonitorGlass";

        public volatile string PmMsg = "";
        public volatile string PawnMsg = "";
        public volatile string AutoMsg = "";

        static readonly HttpClient Http = CreateClient();

        static HttpClient CreateClient()
        {
            HttpClient c = new HttpClient();
            c.Timeout = TimeSpan.FromMinutes(5);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("system-monitor/2.0");
            return c;
        }

        public static string ToolsDir { get { return Path.Combine(Log.Dir, "tools"); } }

        public string PresentMonExe()
        {
            try
            {
                if (!Directory.Exists(ToolsDir)) return null;
                string[] found = Directory.GetFiles(ToolsDir, "PresentMon*.exe");
                Array.Sort(found);
                return found.Length > 0 ? found[found.Length - 1] : null;
            }
            catch { return null; }
        }

        static async Task Download(string url, string dest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            byte[] data = await Http.GetByteArrayAsync(url);
            string tmp = dest + ".part";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
        }

        public void InstallPresentMon() { Task.Run(InstallPresentMonAsync); }

        async Task InstallPresentMonAsync()
        {
            if (PmMsg.StartsWith("завантажую")) return;
            PmMsg = "завантажую…";
            try
            {
                string json = await Http.GetStringAsync(PmApi);
                List<KeyValuePair<string, string>> assets = new List<KeyValuePair<string, string>>();
                using (JsonDocument doc = JsonDocument.Parse(json))
                    foreach (JsonElement a in doc.RootElement.GetProperty("assets").EnumerateArray())
                        assets.Add(new KeyValuePair<string, string>(a.GetProperty("name").GetString(), a.GetProperty("browser_download_url").GetString()));
                Log.Write("PresentMon, файли релізу: " + string.Join(", ", assets.ConvertAll(x => x.Key)));

                List<KeyValuePair<string, string>> cands = assets.FindAll(x =>
                {
                    string n = x.Key.ToLowerInvariant();
                    return n.EndsWith(".exe") && n.Contains("presentmon") && !n.Contains("intel") && n.Contains("x64");
                });
                if (cands.Count == 0) cands = assets.FindAll(x => x.Key.ToLowerInvariant().EndsWith(".exe"));
                if (cands.Count == 0) { PmMsg = "не знайшов .exe у релізі (див. журнал)"; return; }
                cands.Sort((a, b) => a.Key.Length.CompareTo(b.Key.Length));

                await Download(cands[0].Value, Path.Combine(ToolsDir, "PresentMon-console.exe"));
                Log.Write("PresentMon завантажено: " + cands[0].Key);
                PmMsg = "завантажено";
            }
            catch (Exception ex)
            {
                Log.Write("PresentMon: помилка завантаження: " + ex.Message);
                PmMsg = "не вдалося завантажити (див. журнал)";
            }
        }

        public void InstallPawnIo(SensorService sensors) { Task.Run(() => InstallPawnIoAsync(sensors)); }

        async Task InstallPawnIoAsync(SensorService sensors)
        {
            if (PawnMsg.StartsWith("встановлюю")) return;
            PawnMsg = "встановлюю…";
            bool ok = false;
            string output;
            int rc = Proc.Run("winget", 900000, out output, "install", "--id", "namazso.PawnIO", "-e", "--silent",
                              "--accept-package-agreements", "--accept-source-agreements");
            Log.Write("winget PawnIO: код " + rc + "\n" + Tail(output));
            ok = rc == 0;
            if (!ok)
            {
                try
                {
                    string dest = Path.Combine(ToolsDir, "PawnIO_setup.exe");
                    await Download(PawnIoUrl, dest);
                    rc = Proc.Run(dest, 600000, out output, "-install", "-silent");
                    Log.Write("PawnIO_setup: код " + rc + "\n" + Tail(output));
                    ok = rc == 0;
                }
                catch (Exception ex) { Log.Write("PawnIO: помилка: " + ex.Message); }
            }
            PawnMsg = ok ? "встановлено" : "не вдалося (див. журнал)";
            if (ok) sensors.Reopen();
        }

        static string Tail(string s) { return s != null && s.Length > 600 ? s.Substring(s.Length - 600) : (s ?? ""); }

        public bool AutostartEnabled()
        {
            string o;
            return Proc.Run("schtasks", 15000, out o, "/Query", "/TN", TaskName) == 0;
        }

        public void SetAutostart(bool on)
        {
            Task.Run(() =>
            {
                string o;
                if (on)
                {
                    string exe = Environment.ProcessPath ?? "";
                    string q = exe.Replace("'", "''");
                    string ps =
                        "$a = New-ScheduledTaskAction -Execute '" + q + "'; " +
                        "$t = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME; " +
                        "$p = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest; " +
                        "$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero); " +
                        "Register-ScheduledTask -TaskName '" + TaskName + "' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null";
                    int rc = Proc.Run("powershell", 60000, out o, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps);
                    Log.Write("автозапуск увімкнено: код " + rc + " " + Tail(o));
                    AutoMsg = rc == 0 ? "увімкнено" : "не вдалося (див. журнал)";
                }
                else
                {
                    int rc = Proc.Run("schtasks", 30000, out o, "/Delete", "/TN", TaskName, "/F");
                    Log.Write("автозапуск вимкнено: код " + rc + " " + Tail(o));
                    AutoMsg = rc == 0 ? "вимкнено" : "не вдалося вимкнути";
                }
            });
        }
    }
}
