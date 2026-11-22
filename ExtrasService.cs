using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace SystemMonitor
{
    /// <summary>Запуск зовнішніх програм без вікна.</summary>
    internal static class Proc
    {
        public static int Run(string file, int timeoutMs, out string output, params string[] args)
        {
            output = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                foreach (string a in args) psi.ArgumentList.Add(a);
                using (Process p = Process.Start(psi))
                {
                    System.Threading.Tasks.Task<string> so = p.StandardOutput.ReadToEndAsync();
                    System.Threading.Tasks.Task<string> se = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        output = "час очікування вичерпано";
                        return -2;
                    }
                    p.WaitForExit();
                    output = (so.Result + se.Result).Trim();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.GetType().Name + ": " + ex.Message;
                return -1;
            }
        }
    }

    /// <summary>Ping, заряд мишки (Bluetooth), найважча за CPU програма.</summary>
    internal sealed class ExtrasService
    {
        public double PingMs = double.NaN;
        public double MouseLevel = double.NaN;
        public string MouseStatus = "перевіряю…";
        public string TopName = "";
        public double TopCpu;

        volatile bool stop;

        const string PsMouse =
            "$key = '{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2'\n" +
            "$out = @()\n" +
            "Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -like 'BTH*' } | ForEach-Object {\n" +
            "  $p = Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName $key -ErrorAction SilentlyContinue\n" +
            "  if ($p -and $null -ne $p.Data) { $out += [pscustomobject]@{ Name = $_.FriendlyName; Level = [int]$p.Data } }\n" +
            "}\n" +
            "if ($out.Count -gt 0) { ConvertTo-Json -InputObject @($out) -Compress }\n";

        public ExtrasService()
        {
            StartThread("ping", PingLoop);
            StartThread("mouse", MouseLoop);
            StartThread("top", TopLoop);
        }

        public void Stop() { stop = true; }

        static void StartThread(string name, ThreadStart fn)
        {
            Thread t = new Thread(fn);
            t.IsBackground = true;
            t.Name = name;
            t.Start();
        }

        void PingLoop()
        {
            while (!stop)
            {
                AppConfig c = AppConfig.Cur;
                if (c.Rows.ContainsKey("ping") && c.Rows["ping"])
                {
                    try
                    {
                        using (Ping p = new Ping())
                        {
                            PingReply r = p.Send(string.IsNullOrWhiteSpace(c.PingHost) ? "1.1.1.1" : c.PingHost.Trim(), 1000);
                            PingMs = r.Status == IPStatus.Success ? r.RoundtripTime : double.NaN;
                        }
                    }
                    catch { PingMs = double.NaN; }
                }
                Thread.Sleep(2000);
            }
        }

        void MouseLoop()
        {
            while (!stop)
            {
                AppConfig c = AppConfig.Cur;
                if (c.Rows.ContainsKey("mouse") && c.Rows["mouse"])
                {
                    string output;
                    Proc.Run("powershell", 60000, out output, "-NoProfile", "-Command", PsMouse);
                    List<KeyValuePair<string, int>> devices = new List<KeyValuePair<string, int>>();
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            using (JsonDocument doc = JsonDocument.Parse(output))
                            {
                                foreach (JsonElement e in doc.RootElement.EnumerateArray())
                                    devices.Add(new KeyValuePair<string, int>(e.GetProperty("Name").GetString(), e.GetProperty("Level").GetInt32()));
                            }
                        }
                    }
                    catch { }

                    string[] filters = (c.MouseName ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    bool found = false;
                    foreach (KeyValuePair<string, int> d in devices)
                    {
                        foreach (string f in filters)
                        {
                            if (d.Key != null && d.Key.IndexOf(f.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                MouseLevel = d.Value;
                                MouseStatus = d.Key + ": " + d.Value + "%";
                                found = true;
                                break;
                            }
                        }
                        if (found) break;
                    }
                    if (!found)
                    {
                        MouseLevel = double.NaN;
                        List<string> names = new List<string>();
                        foreach (KeyValuePair<string, int> d in devices) names.Add(d.Key);
                        MouseStatus = "Windows не віддає заряд мишки (пристрої із зарядом: " + (names.Count > 0 ? string.Join(", ", names) : "жодного") + ")";
                    }
                }
                for (int i = 0; i < 60 && !stop; i++) Thread.Sleep(1000);
            }
        }

        void TopLoop()
        {
            try { TopLoopCore(); }
            catch (Exception ex) { Log.Write("топ-процес, потік: " + ex.Message); }
        }

        void TopLoopCore()
        {
            Dictionary<int, TimeSpan> prev = new Dictionary<int, TimeSpan>();
            DateTime last = DateTime.UtcNow;
            int cores = Environment.ProcessorCount;
            int me = Process.GetCurrentProcess().Id;
            while (!stop)
            {
                AppConfig c = AppConfig.Cur;
                if (c.Rows.ContainsKey("top") && c.Rows["top"])
                {
                    DateTime now = DateTime.UtcNow;
                    double dt = Math.Max((now - last).TotalSeconds, 0.001);
                    Dictionary<int, TimeSpan> cur = new Dictionary<int, TimeSpan>();
                    string bestName = "";
                    double best = 0;
                    foreach (Process p in Process.GetProcesses())
                    {
                        try
                        {
                            if (p.Id == 0 || p.Id == me) continue;
                            TimeSpan t = p.TotalProcessorTime;
                            cur[p.Id] = t;
                            TimeSpan old;
                            if (prev.TryGetValue(p.Id, out old))
                            {
                                double pct = (t - old).TotalSeconds / dt / cores * 100.0;
                                if (pct > best) { best = pct; bestName = p.ProcessName + ".exe"; }
                            }
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                    prev = cur;
                    last = now;
                    TopName = bestName;
                    TopCpu = best;
                }
                Thread.Sleep(3000);
            }
        }
    }
}
