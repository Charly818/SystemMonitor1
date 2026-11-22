using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SystemMonitor
{
    internal static class Util
    {
        static readonly Dictionary<string, SolidColorBrush> Cache = new Dictionary<string, SolidColorBrush>();

        static ImageSource appIcon;
        static bool iconTried;

        /// <summary>Іконка програми, вшита в .exe (для вікон налаштувань і статистики).</summary>
        public static ImageSource AppIcon()
        {
            if (iconTried) return appIcon;
            iconTried = true;
            try
            {
                IntPtr h = Native.ExtractIcon(IntPtr.Zero, Environment.ProcessPath ?? "", 0);
                if (h.ToInt64() > 1)
                {
                    BitmapSource bs = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bs.Freeze();
                    Native.DestroyIcon(h);
                    appIcon = bs;
                }
            }
            catch { }
            return appIcon;
        }

        public static Color Hex(string s, string fallback = "#FFFFFF")
        {
            try { return (Color)ColorConverter.ConvertFromString(s); }
            catch { return (Color)ColorConverter.ConvertFromString(fallback); }
        }

        public static bool TryHex(string s, out Color c)
        {
            try { c = (Color)ColorConverter.ConvertFromString(s); return true; }
            catch { c = Colors.White; return false; }
        }

        public static Color Lerp(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        public static SolidColorBrush Brush(string hex)
        {
            SolidColorBrush b;
            if (!Cache.TryGetValue(hex, out b))
            {
                b = new SolidColorBrush(Hex(hex));
                b.Freeze();
                Cache[hex] = b;
            }
            return b;
        }

        public static SolidColorBrush Brush(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static string Dur(double sec)
        {
            int s = (int)Math.Max(sec, 0);
            return (s / 3600) + ":" + ((s % 3600) / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        public static string Mmss(double sec)
        {
            int s = (int)Math.Max(sec, 0);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        /// <summary>1% low: FPS за найповільнішими 1% кадрів (мс). Потрібно щонайменше 30 кадрів, інакше NaN.</summary>
        public static double Low1(List<double> frameTimes)
        {
            if (frameTimes.Count < 30) return double.NaN;
            List<double> sorted = new List<double>(frameTimes);
            sorted.Sort();
            sorted.Reverse();
            int n = Math.Max(1, sorted.Count / 100);
            double sum = 0;
            for (int i = 0; i < n; i++) sum += sorted[i];
            double avg = sum / n;
            return avg > 0 ? 1000.0 / avg : double.NaN;
        }

        /// <summary>Стискає ряд до n точок, беручи максимум у кожній групі (щоб не загубити стрибки frame time).</summary>
        public static List<double> BucketMax(List<double> v, int n)
        {
            if (v.Count <= n) return new List<double>(v);
            List<double> res = new List<double>();
            double step = (double)v.Count / n;
            for (int i = 0; i < n; i++)
            {
                int a = (int)(i * step);
                int b = Math.Max((int)((i + 1) * step), a + 1);
                double m = double.MinValue;
                for (int j = a; j < b && j < v.Count; j++) if (v[j] > m) m = v[j];
                res.Add(m);
            }
            return res;
        }
    }

    internal static class Fmt
    {
        public static string Pct(double p) { return double.IsNaN(p) ? "  — " : string.Format("{0,3:0}%", p); }
        public static string Temp(double t) { return double.IsNaN(t) ? "  — " : string.Format("{0,3:0}°C", t); }
        public static string Watt(double w) { return double.IsNaN(w) ? "  — " : string.Format("{0,3:0}W", w); }
        public static string Rpm(double r) { return double.IsNaN(r) ? "   —" : string.Format("{0,4:0}", r); }
        public static string Speed(double bps)
        {
            return bps >= 1048576 ? string.Format("{0,6:0.0} MB/s", bps / 1048576) : string.Format("{0,6:0} KB/s", bps / 1024);
        }
        public static string Bytes(double b)
        {
            return b >= 1073741824 ? string.Format("{0:0.0}G", b / 1073741824) : string.Format("{0:0}M", b / 1048576);
        }

        public static string ColorLoad(double p)
        {
            if (double.IsNaN(p)) return "#C8C8C8";
            return p < 60 ? "#6EE7A1" : p < 85 ? "#FFD166" : "#FF6B6B";
        }
        public static string ColorPing(double ms)
        {
            if (double.IsNaN(ms)) return "#C8C8C8";
            return ms < 60 ? "#6EE7A1" : ms < 120 ? "#FFD166" : "#FF6B6B";
        }
        public static string ColorFps(double f)
        {
            if (double.IsNaN(f)) return "#C8C8C8";
            return f >= 60 ? "#6EE7A1" : f >= 30 ? "#FFD166" : "#FF6B6B";
        }
    }

    internal static class CpuMeter
    {
        static ulong lastIdle, lastTotal;
        static bool init;

        public static double Read()
        {
            ulong idle, total;
            if (!Native.TryGetCpuTimes(out idle, out total)) return double.NaN;
            double res = double.NaN;
            if (init && total > lastTotal)
                res = 100.0 * (1.0 - (double)(idle - lastIdle) / (double)(total - lastTotal));
            lastIdle = idle; lastTotal = total; init = true;
            return double.IsNaN(res) ? res : Math.Max(0, Math.Min(100, res));
        }
    }

    internal sealed class TrafficMeter
    {
        long lastRx, lastTx;
        DateTime last;
        bool init;

        public void Read(out double downBps, out double upBps, out long dRx, out long dTx)
        {
            long rx = 0, tx = 0;
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    try
                    {
                        IPv4InterfaceStatistics st = ni.GetIPv4Statistics();
                        rx += st.BytesReceived; tx += st.BytesSent;
                    }
                    catch { }
                }
            }
            catch { }

            DateTime now = DateTime.UtcNow;
            downBps = upBps = 0; dRx = dTx = 0;
            if (init)
            {
                double dt = Math.Max((now - last).TotalSeconds, 0.001);
                dRx = Math.Max(0, rx - lastRx); dTx = Math.Max(0, tx - lastTx);
                downBps = dRx / dt; upBps = dTx / dt;
            }
            lastRx = rx; lastTx = tx; last = now; init = true;
        }
    }
}
