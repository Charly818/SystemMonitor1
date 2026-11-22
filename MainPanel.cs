using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WShapes = System.Windows.Shapes;

namespace SystemMonitor
{
    internal sealed class RowDef
    {
        public string Key, Label;
        public bool Graph;
        public RowDef(string key, string label, bool graph) { Key = key; Label = label; Graph = graph; }
    }

    /// <summary>Статистика поточного сеансу і трафік за день.</summary>
    internal sealed class SessionStats
    {
        public static readonly SessionStats Cur = new SessionStats();
        public DateTime Start = DateTime.Now;
        public double MaxCpuTemp = double.NaN, MaxGpuTemp = double.NaN, MaxPing = double.NaN, MinLow1 = double.NaN;
        public double MaxFps, FpsSum, GameSeconds;
        public long FpsN;
        public string DayDate = DateTime.Today.ToString("yyyy-MM-dd");
        public double DayDown, DayUp;

        public class DayData { public string Date; public double Down, Up; }
        static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { IncludeFields = true };
        static string DayFile { get { return Path.Combine(Log.Dir, "stats.json"); } }

        public static void LoadDay()
        {
            try
            {
                if (!File.Exists(DayFile)) return;
                DayData d = JsonSerializer.Deserialize<DayData>(File.ReadAllText(DayFile), Opts);
                if (d != null && d.Date == Cur.DayDate) { Cur.DayDown = d.Down; Cur.DayUp = d.Up; }
            }
            catch { }
        }

        public static void SaveDay()
        {
            try
            {
                Directory.CreateDirectory(Log.Dir);
                File.WriteAllText(DayFile, JsonSerializer.Serialize(new DayData { Date = Cur.DayDate, Down = Cur.DayDown, Up = Cur.DayUp }, Opts));
            }
            catch { }
        }

        public static void AddTraffic(long rx, long tx)
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            if (Cur.DayDate != today) { Cur.DayDate = today; Cur.DayDown = 0; Cur.DayUp = 0; }
            Cur.DayDown += rx; Cur.DayUp += tx;
        }
    }

    /// <summary>Невеликий графік: кілька ліній на прозорому полотні.</summary>
    internal sealed class GraphView
    {
        public readonly Canvas Canvas = new Canvas();
        readonly WShapes.Polyline[] lines;
        readonly double w, h;

        public GraphView(double width, double height, string caption, string[] colors, double fontSize)
        {
            w = width; h = height;
            Canvas.Width = w; Canvas.Height = h;
            Canvas.Margin = new Thickness(0, 2, 0, 2);
            WShapes.Line baseLine = new WShapes.Line { X1 = 0, Y1 = h - 1, X2 = w, Y2 = h - 1, Stroke = Util.Brush("#66FFFFFF"), StrokeThickness = 1 };
            Canvas.Children.Add(baseLine);
            TextBlock cap = new TextBlock { Text = caption, FontFamily = new FontFamily("Consolas"), FontSize = Math.Max(8, fontSize - 3), Foreground = Util.Brush("#B8FFFFFF") };
            Canvas.SetLeft(cap, 2); Canvas.SetTop(cap, 0);
            Canvas.Children.Add(cap);
            lines = new WShapes.Polyline[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                lines[i] = new WShapes.Polyline { Stroke = Util.Brush(colors[i]), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
                Canvas.Children.Add(lines[i]);
            }
        }

        /// <summary>total = скільки точок займають усю ширину (графік заповнюється справа).</summary>
        public void Draw(int idx, List<double> values, double ymax, int total)
        {
            PointCollection pts = new PointCollection();
            int n = values.Count;
            if (n >= 2)
            {
                for (int i = 0; i < n; i++)
                {
                    double x = w * (i + (total - n)) / Math.Max(total - 1, 1);
                    double y = h - 2 - Math.Min(values[i], ymax) / ymax * (h - 4);
                    pts.Add(new Point(x, y));
                }
            }
            lines[idx].Points = pts;
        }
    }

    /// <summary>Головна панель: рядки з даними та графіки.</summary>
    internal sealed class MainPanel : GlassWindow
    {
        public static readonly RowDef[] RowDefs =
        {
            new RowDef("cpu", "CPU: навантаження й температура", false),
            new RowDef("gpu", "GPU: навантаження й температура", false),
            new RowDef("vram", "VRAM відеокарти", false),
            new RowDef("watt", "Споживання CPU | GPU, Вт", false),
            new RowDef("fan", "Вентилятори: плата | GPU, об/хв", false),
            new RowDef("fps", "FPS", false),
            new RowDef("low1", "1% low FPS", false),
            new RowDef("ft", "Графік frame time", true),
            new RowDef("load", "Графік навантаження CPU/GPU", true),
            new RowDef("ram", "RAM", false),
            new RowDef("disk", "Диск: заповненість і температура", false),
            new RowDef("ping", "Ping", false),
            new RowDef("mouse", "Заряд мишки", false),
            new RowDef("top", "Найважча програма (CPU)", false),
            new RowDef("net", "Інтернет: ↓ та ↑", false),
            new RowDef("day", "Трафік за день", false),
            new RowDef("clock", "Годинник", false),
            new RowDef("game", "Тривалість гри", false),
        };

        protected override string Key { get { return "panel"; } }
        protected override bool BackgroundEnabled { get { return Cfg.BackgroundOnPanel; } }

        readonly StackPanel stack = new StackPanel();
        readonly Dictionary<string, TextBlock> texts = new Dictionary<string, TextBlock>();
        readonly Dictionary<string, GraphView> graphs = new Dictionary<string, GraphView>();
        readonly Dictionary<string, KeyValuePair<string, string>> cache = new Dictionary<string, KeyValuePair<string, string>>();
        readonly DispatcherTimer timer;
        readonly TrafficMeter traffic = new TrafficMeter();
        readonly List<double> cpuHist = new List<double>();
        readonly List<double> gpuHist = new List<double>();

        int lastFg, gamePid;
        double gameStart, gameLast;
        string diskLabel = "C:";
        double diskPct = double.NaN, diskStamp = -100;

        public MainPanel()
        {
            ContentHost.Children.Add(stack);
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += delegate { try { Tick(); } catch (Exception ex) { Log.Write("tick: " + ex); } };
            timer.Start();
            try { diskLabel = Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\'); } catch { }
        }

        public override void ApplyConfig()
        {
            base.ApplyConfig();
            Build();
        }

        static double Measure(string s, double fontSize)
        {
            FormattedText ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), fontSize, Brushes.White, 1.0);
            return ft.WidthIncludingTrailingWhitespace;
        }

        void Build()
        {
            stack.Children.Clear();
            texts.Clear();
            graphs.Clear();
            double fs = Cfg.FontSize;
            string[] samples = { "VRAM 88.8/88 GB 100%", "RAM 88.8/88 GB 100%", "TOP 123456789012 100%", "CPU 100% 100°C", "↓ 1023.9 MB/s", "DAY ↓ 888.8G ↑ 888.8G" };
            double w = 0;
            foreach (string s in samples) w = Math.Max(w, Measure(s, fs));
            w = Math.Ceiling(w) + 4;

            stack.Width = w;
            stack.Margin = new Thickness(Cfg.Padding);

            bool any = false;
            foreach (RowDef r in RowDefs)
            {
                bool on;
                if (!Cfg.Rows.TryGetValue(r.Key, out on) || !on) continue;
                any = true;
                if (r.Key == "net") { AddText("down"); AddText("up"); }
                else if (r.Graph) AddGraph(r.Key, w);
                else AddText(r.Key);
            }
            if (!any) AddText("cpu");
        }

        void AddText(string key)
        {
            TextBlock tb = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = Cfg.FontSize,
                Foreground = Util.Brush(Cfg.TextColor),
                TextWrapping = TextWrapping.NoWrap,
                Effect = new DropShadowEffect { ShadowDepth = 1, BlurRadius = 2, Opacity = 0.85, Color = Colors.Black },
            };
            KeyValuePair<string, string> c;
            if (cache.TryGetValue(key, out c)) { tb.Text = c.Key; if (c.Value != null) tb.Foreground = Util.Brush(c.Value); }
            texts[key] = tb;
            stack.Children.Add(tb);
        }

        void AddGraph(string key, double w)
        {
            string[] colors = key == "load" ? new[] { "#6EE7A1", "#6AA9FF" } : new[] { "#FFD166" };
            string caption = key == "load" ? "CPU / GPU, 2 хв" : "frame time";
            GraphView g = new GraphView(w, Cfg.GraphHeight, caption, colors, Cfg.FontSize);
            graphs[key] = g;
            stack.Children.Add(g.Canvas);
        }

        void Set(string key, string text, string colorHex = null)
        {
            cache[key] = new KeyValuePair<string, string>(text, colorHex);
            TextBlock tb;
            if (texts.TryGetValue(key, out tb))
            {
                tb.Text = text;
                tb.Foreground = Util.Brush(colorHex ?? Cfg.TextColor);
            }
        }

        static int ForegroundPid()
        {
            try { uint pid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid); return (int)pid; }
            catch { return 0; }
        }

        void Tick()
        {
            SensorService s = AppCore.Sensors;
            ExtrasService ex = AppCore.Extras;
            FpsService fps = AppCore.Fps;
            SessionStats sess = SessionStats.Cur;
            double nowS = Environment.TickCount64 / 1000.0;

            // ---- основні показники
            double cpu = CpuMeter.Read();
            Native.MEMORYSTATUSEX ms = new Native.MEMORYSTATUSEX();
            Native.GlobalMemoryStatusEx(ms);
            double totalGb = ms.ullTotalPhys / 1073741824.0;
            double usedGb = (ms.ullTotalPhys - ms.ullAvailPhys) / 1073741824.0;
            double ramPct = ms.dwMemoryLoad;

            double down, up; long dRx, dTx;
            traffic.Read(out down, out up, out dRx, out dTx);
            SessionStats.AddTraffic(dRx, dTx);

            Set("cpu", "CPU " + Fmt.Pct(cpu) + " " + Fmt.Temp(s.CpuTemp), Fmt.ColorLoad(cpu));
            Set("gpu", "GPU " + Fmt.Pct(s.GpuLoad) + " " + Fmt.Temp(s.GpuTemp), Fmt.ColorLoad(s.GpuLoad));

            if (!double.IsNaN(s.VramTotalMb) && s.VramTotalMb > 0)
            {
                double u = double.IsNaN(s.VramUsedMb) ? 0 : s.VramUsedMb;
                double pct = u / s.VramTotalMb * 100;
                Set("vram", string.Format("VRAM {0,4:0.0}/{1:0} GB {2,3:0}%", u / 1024, s.VramTotalMb / 1024, pct), Fmt.ColorLoad(pct));
            }
            else Set("vram", "VRAM   —");

            Set("watt", "PWR " + Fmt.Watt(s.CpuPower) + " | " + Fmt.Watt(s.GpuPower));
            Set("fan", "FAN " + Fmt.Rpm(s.FanBoard) + " | " + Fmt.Rpm(s.FanGpu));
            Set("ram", string.Format("RAM {0,4:0.0}/{1:0} GB {2,3:0}%", usedGb, totalGb, ramPct), Fmt.ColorLoad(ramPct));

            if (nowS - diskStamp > 10)
            {
                diskStamp = nowS;
                try
                {
                    DriveInfo di = new DriveInfo(diskLabel);
                    diskPct = 100.0 * (1.0 - (double)di.AvailableFreeSpace / di.TotalSize);
                }
                catch { diskPct = double.NaN; }
            }
            Set("disk", "DSK " + diskLabel + " " + Fmt.Pct(diskPct) + " " + Fmt.Temp(s.DiskTemp), Fmt.ColorLoad(diskPct));

            Set("ping", double.IsNaN(ex.PingMs) ? "PING   —" : string.Format("PING {0,4:0} ms", ex.PingMs), Fmt.ColorPing(ex.PingMs));
            if (double.IsNaN(ex.MouseLevel)) Set("mouse", "MSE    —");
            else Set("mouse", "MSE " + Fmt.Pct(ex.MouseLevel), Fmt.ColorLoad(100 - ex.MouseLevel));

            string tn = ex.TopName ?? "";
            Set("top", tn.Length == 0 ? "TOP   —" : "TOP " + (tn.Length > 12 ? tn.Substring(0, 12) : tn.PadRight(12)) + string.Format("{0,3:0}%", ex.TopCpu));
            Set("down", "↓ " + Fmt.Speed(down));
            Set("up", "↑ " + Fmt.Speed(up));
            Set("day", "DAY ↓" + Fmt.Bytes(sess.DayDown).PadLeft(5) + " ↑" + Fmt.Bytes(sess.DayUp).PadLeft(5));
            Set("clock", "TIME " + DateTime.Now.ToString("HH:mm"));

            // ---- FPS, 1% low, frame time, тривалість гри
            if (fps.Status == FpsStatus.NoExe)
            {
                string exe = AppCore.Comp.PresentMonExe();
                if (exe != null) fps.Start(exe);
            }
            int fg = ForegroundPid();
            if (fg != 0 && fg != Environment.ProcessId) lastFg = fg;

            int f = 0, gp = 0;
            if (fps.Status == FpsStatus.Ok)
            {
                f = fps.FpsFor(lastFg);
                gp = f > 0 ? lastFg : 0;
                if (f == 0) fps.FpsBest(Environment.ProcessId, out f, out gp);   // PID вікна міг не збігтися з PID гри
                if (f == 0) Set("fps", "FPS    —", "#C8C8C8");
                else Set("fps", string.Format("FPS {0,4:0}", f), Fmt.ColorFps(f));
            }
            else
            {
                string hint = fps.Status == FpsStatus.NoExe ? "FPS  — немає PM" : fps.Status == FpsStatus.Starting ? "FPS  …" : "FPS  — помилка PM";
                Set("fps", hint, "#C8C8C8");
            }

            double low1 = double.NaN;
            List<double> ftView = new List<double>();
            if (f > 0 && gp != 0)
            {
                low1 = Util.Low1(fps.FrameTimes(gp, 10.0));
                ftView = Util.BucketMax(fps.FrameTimes(gp, 3.0), 100);
                if (gamePid != gp) { gamePid = gp; gameStart = nowS; }
                gameLast = nowS;
                sess.FpsSum += f; sess.FpsN++; sess.GameSeconds += 1;
                if (f > sess.MaxFps) sess.MaxFps = f;
                if (!double.IsNaN(low1)) sess.MinLow1 = double.IsNaN(sess.MinLow1) ? low1 : Math.Min(sess.MinLow1, low1);
            }
            else if (gamePid != 0 && nowS - gameLast > 20) gamePid = 0;

            Set("low1", double.IsNaN(low1) ? "1%LOW   —" : string.Format("1%LOW {0,4:0}", low1), Fmt.ColorFps(low1));
            Set("game", gamePid == 0 ? "GAME   —" : "GAME " + Util.Dur(nowS - gameStart));

            // ---- графіки
            cpuHist.Add(double.IsNaN(cpu) ? 0 : cpu);
            gpuHist.Add(double.IsNaN(s.GpuLoad) ? 0 : s.GpuLoad);
            if (cpuHist.Count > 120) cpuHist.RemoveAt(0);
            if (gpuHist.Count > 120) gpuHist.RemoveAt(0);
            GraphView gl;
            if (graphs.TryGetValue("load", out gl)) { gl.Draw(0, cpuHist, 100, 120); gl.Draw(1, gpuHist, 100, 120); }
            GraphView gf;
            if (graphs.TryGetValue("ft", out gf))
            {
                double ymax = 33.4;
                foreach (double v in ftView) ymax = Math.Max(ymax, Math.Min(v * 1.1, 100));
                gf.Draw(0, ftView, ymax, Math.Max(ftView.Count, 2));
            }

            // ---- статистика сеансу
            if (!double.IsNaN(s.CpuTemp)) sess.MaxCpuTemp = double.IsNaN(sess.MaxCpuTemp) ? s.CpuTemp : Math.Max(sess.MaxCpuTemp, s.CpuTemp);
            if (!double.IsNaN(s.GpuTemp)) sess.MaxGpuTemp = double.IsNaN(sess.MaxGpuTemp) ? s.GpuTemp : Math.Max(sess.MaxGpuTemp, s.GpuTemp);
            if (!double.IsNaN(ex.PingMs)) sess.MaxPing = double.IsNaN(sess.MaxPing) ? ex.PingMs : Math.Max(sess.MaxPing, ex.PingMs);

            double hot = double.NaN;
            if (!double.IsNaN(s.CpuTemp)) hot = s.CpuTemp;
            if (!double.IsNaN(s.GpuTemp)) hot = double.IsNaN(hot) ? s.GpuTemp : Math.Max(hot, s.GpuTemp);
            AppCore.SetHeat(hot);

            if (Environment.TickCount64 / 1000 % 60 == 0) SessionStats.SaveDay();
        }
    }
}
