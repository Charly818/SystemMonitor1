using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace SystemMonitor
{
    internal enum FpsStatus { NoExe, Starting, Ok, Failed }

    /// <summary>FPS і frame time: читає вивід PresentMon (завантажується автоматично).</summary>
    internal sealed class FpsService
    {
        public volatile FpsStatus Status = FpsStatus.NoExe;
        public string LastError = "";

        static readonly string[][] Variants =
        {
            new[] { "--output_stdout", "--no_console_stats", "--v1_metrics", "--stop_existing_session" },
            new[] { "--output_stdout", "--no_console_stats", "--stop_existing_session" },
            new[] { "--output_stdout", "--no_console_stats", "--v1_metrics" },
            new[] { "--output_stdout", "--no_console_stats" },
            new[] { "-output_stdout" },
        };

        static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dwm.exe", "explorer.exe", "SystemMonitor.exe", "searchhost.exe", "startmenuexperiencehost.exe",
            "shellexperiencehost.exe", "textinputhost.exe", "applicationframehost.exe"
        };

        readonly object lk = new object();
        readonly Dictionary<int, Queue<double>> frames = new Dictionary<int, Queue<double>>();
        readonly Dictionary<int, Queue<KeyValuePair<double, double>>> ft = new Dictionary<int, Queue<KeyValuePair<double, double>>>();
        readonly Dictionary<int, string> names = new Dictionary<int, string>();
        Process proc;
        string exe;
        volatile bool stopped;
        bool started;

        static double Now { get { return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency; } }

        public void Start(string exePath)
        {
            if (started) return;
            started = true;
            exe = exePath;
            Status = FpsStatus.Starting;
            Thread t = new Thread(Reader);
            t.IsBackground = true;
            t.Name = "presentmon";
            t.Start();
        }

        public void Stop()
        {
            stopped = true;
            try { if (proc != null && !proc.HasExited) proc.Kill(); } catch { }
        }

        void Reader()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }

            foreach (string[] args in Variants)
            {
                if (stopped) return;
                Log.Write("запуск PresentMon: " + Path.GetFileName(exe) + " " + string.Join(" ", args));
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(exe);
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    foreach (string a in args) psi.ArgumentList.Add(a);
                    proc = Process.Start(psi);
                    proc.ErrorDataReceived += delegate (object s, DataReceivedEventArgs ev)
                    {
                        if (!string.IsNullOrWhiteSpace(ev.Data))
                        {
                            LastError = ev.Data.Trim();
                            Log.Write("PresentMon: " + ev.Data.Trim());
                        }
                    };
                    proc.BeginErrorReadLine();
                }
                catch (Exception ex)
                {
                    LastError = "не вдалося запустити: " + ex.Message;
                    Log.Write("PresentMon не запустився: " + ex.Message);
                    continue;
                }

                bool got = Consume(proc);
                int code = -1;
                try { code = proc.HasExited ? proc.ExitCode : -1; } catch { }
                Log.Write("PresentMon завершився (код " + code + ", дані були: " + got + ")");
                if (got) break;
            }
            Status = FpsStatus.Failed;
        }

        bool Consume(Process p)
        {
            int pidIdx = -1, nameIdx = -1, msIdx = -1;
            bool seen = false;
            string line;
            while (!stopped && (line = p.StandardOutput.ReadLine()) != null)
            {
                string[] parts = line.TrimEnd('\r', '\n').Split(',');
                if (pidIdx < 0)
                {
                    int idx = Array.IndexOf(parts, "ProcessID");
                    if (idx >= 0)
                    {
                        pidIdx = idx;
                        nameIdx = Array.IndexOf(parts, "Application");
                        msIdx = Array.IndexOf(parts, "MsBetweenPresents");
                        if (msIdx < 0) msIdx = Array.IndexOf(parts, "FrameTime");
                        Status = FpsStatus.Ok;
                        seen = true;
                    }
                    else if (line.Trim().Length > 0)
                    {
                        LastError = line.Trim().Length > 200 ? line.Trim().Substring(0, 200) : line.Trim();
                        Log.Write("PresentMon (stdout): " + LastError);
                    }
                    continue;
                }

                int pid;
                if (pidIdx >= parts.Length || !int.TryParse(parts[pidIdx], out pid)) continue;
                double ms = double.NaN;
                if (msIdx >= 0 && msIdx < parts.Length)
                {
                    if (!double.TryParse(parts[msIdx], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ms))
                        ms = double.NaN;
                }

                double t = Now;
                lock (lk)
                {
                    if (nameIdx >= 0 && nameIdx < parts.Length) names[pid] = parts[nameIdx];
                    Queue<double> q;
                    if (!frames.TryGetValue(pid, out q)) { q = new Queue<double>(); frames[pid] = q; }
                    q.Enqueue(t);
                    while (q.Count > 0 && q.Peek() < t - 2) q.Dequeue();
                    if (!double.IsNaN(ms))
                    {
                        Queue<KeyValuePair<double, double>> fq;
                        if (!ft.TryGetValue(pid, out fq)) { fq = new Queue<KeyValuePair<double, double>>(); ft[pid] = fq; }
                        fq.Enqueue(new KeyValuePair<double, double>(t, ms));
                        while (fq.Count > 0 && fq.Peek().Key < t - 12) fq.Dequeue();
                    }
                }
            }
            return seen;
        }

        /// <summary>FPS програми з цим PID (0, якщо кадрів немає).</summary>
        public int FpsFor(int pid)
        {
            if (pid == 0) return 0;
            double now = Now;
            lock (lk)
            {
                Queue<double> q;
                if (!frames.TryGetValue(pid, out q)) return 0;
                while (q.Count > 0 && q.Peek() < now - 1.0) q.Dequeue();
                return q.Count;
            }
        }

        /// <summary>Запасний варіант: програма, що зараз видає найбільше кадрів (крім системних).</summary>
        public void FpsBest(int skipPid, out int fps, out int pid)
        {
            fps = 0; pid = 0;
            double now = Now;
            lock (lk)
            {
                foreach (KeyValuePair<int, Queue<double>> kv in frames)
                {
                    string name;
                    if (kv.Key == skipPid) continue;
                    if (names.TryGetValue(kv.Key, out name) && Ignored.Contains(name)) continue;
                    Queue<double> q = kv.Value;
                    while (q.Count > 0 && q.Peek() < now - 1.0) q.Dequeue();
                    if (q.Count > fps) { fps = q.Count; pid = kv.Key; }
                }
            }
        }

        public List<double> FrameTimes(int pid, double window)
        {
            List<double> res = new List<double>();
            if (pid == 0) return res;
            double now = Now;
            lock (lk)
            {
                Queue<KeyValuePair<double, double>> fq;
                if (ft.TryGetValue(pid, out fq))
                    foreach (KeyValuePair<double, double> kv in fq)
                        if (kv.Key >= now - window) res.Add(kv.Value);
            }
            return res;
        }

        public string NameOf(int pid)
        {
            lock (lk) { string n; return names.TryGetValue(pid, out n) ? n : ""; }
        }
    }
}
