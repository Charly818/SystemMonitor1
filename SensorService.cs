using System;
using System.Collections.Generic;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace SystemMonitor
{
    /// <summary>Датчики через LibreHardwareMonitorLib. Опитування у фоновому потоці раз на 2 секунди.</summary>
    internal sealed class SensorService
    {
        public double CpuTemp = double.NaN, CpuPower = double.NaN;
        public double GpuTemp = double.NaN, GpuLoad = double.NaN, GpuPower = double.NaN;
        public double VramUsedMb = double.NaN, VramTotalMb = double.NaN;
        public double FanBoard = double.NaN, FanGpu = double.NaN, DiskTemp = double.NaN;
        public bool? CpuOk;
        public string Backend = "starting";
        public string Error = "";

        volatile bool stop;
        volatile bool reopen;
        Computer computer;
        readonly Thread thread;

        class GpuInfo
        {
            public int Prio;
            public double Temp = double.NaN, Load = double.NaN, Used = double.NaN, Total = double.NaN, MemLoad = double.NaN, Fan = double.NaN;
            public List<KeyValuePair<string, double>> Power = new List<KeyValuePair<string, double>>();
        }

        public SensorService()
        {
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "sensors";
            thread.Start();
        }

        public void Stop()
        {
            stop = true;
            try { if (computer != null) computer.Close(); } catch { }
        }

        public void Reopen() { reopen = true; }

        void Loop()
        {
            while (!stop)
            {
                try
                {
                    if (computer == null || reopen)
                    {
                        reopen = false;
                        CloseComputer();
                        computer = OpenComputer();
                        Backend = "lib";
                        Error = "";
                        Log.Write("LibreHardwareMonitorLib запущено");
                    }
                    Read();
                }
                catch (Exception ex)
                {
                    Backend = "error";
                    Error = ex.GetType().Name + ": " + ex.Message;
                    Log.Write("помилка датчиків: " + ex);
                    CloseComputer();
                    computer = null;
                    Thread.Sleep(5000);
                    continue;
                }
                Thread.Sleep(2000);
            }
        }

        static Computer OpenComputer()
        {
            try
            {
                Computer c = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true, IsStorageEnabled = true };
                c.Open();
                return c;
            }
            catch (Exception ex)
            {
                Log.Write("повний набір датчиків не відкрився, пробую CPU+GPU: " + ex.Message);
                Computer c = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
                c.Open();
                return c;
            }
        }

        void CloseComputer()
        {
            try { if (computer != null) computer.Close(); } catch { }
        }

        static IEnumerable<IHardware> Walk(IHardware hw)
        {
            yield return hw;
            foreach (IHardware sub in hw.SubHardware)
                foreach (IHardware x in Walk(sub))
                    yield return x;
        }

        static double Val(ISensor s) { return s.Value.HasValue ? (double)s.Value.Value : double.NaN; }

        static double PickPower(List<KeyValuePair<string, double>> items, params string[] prefer)
        {
            double best = double.NaN;
            foreach (string key in prefer)
                foreach (KeyValuePair<string, double> kv in items)
                    if (kv.Value > 0 && kv.Key.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return kv.Value;
            foreach (KeyValuePair<string, double> kv in items)
                if (kv.Value > 0 && (double.IsNaN(best) || kv.Value > best)) best = kv.Value;
            return best;
        }

        /// <summary>Нулі = немає даних (драйвер PawnIO не встановлено). Пороги "Distance to TjMax" ігноруємо.</summary>
        static double PickCpuTemp(List<KeyValuePair<string, double>> temps)
        {
            List<KeyValuePair<string, double>> valid = new List<KeyValuePair<string, double>>();
            foreach (KeyValuePair<string, double> kv in temps)
                if (kv.Value > 0 && kv.Value < 150 && kv.Key.IndexOf("distance", StringComparison.OrdinalIgnoreCase) < 0) valid.Add(kv);
            if (valid.Count == 0) return double.NaN;
            string[] pref = { "Package", "Tctl", "Tdie", "Average", "Max" };
            foreach (KeyValuePair<string, double> kv in valid)
                foreach (string p in pref)
                    if (kv.Key.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return kv.Value;
            double m = double.MinValue;
            foreach (KeyValuePair<string, double> kv in valid) if (kv.Value > m) m = kv.Value;
            return m;
        }

        void Read()
        {
            List<KeyValuePair<string, double>> cpuTemps = new List<KeyValuePair<string, double>>();
            List<KeyValuePair<string, double>> cpuPw = new List<KeyValuePair<string, double>>();
            List<GpuInfo> gpus = new List<GpuInfo>();
            List<double> diskTemps = new List<double>();
            List<double> fans = new List<double>();

            foreach (IHardware top in computer.Hardware)
            {
                foreach (IHardware hw in Walk(top))
                {
                    hw.Update();
                    HardwareType t = hw.HardwareType;
                    if (t == HardwareType.Cpu)
                    {
                        foreach (ISensor s in hw.Sensors)
                        {
                            double v = Val(s);
                            if (double.IsNaN(v)) continue;
                            if (s.SensorType == SensorType.Temperature) cpuTemps.Add(new KeyValuePair<string, double>(s.Name, v));
                            else if (s.SensorType == SensorType.Power) cpuPw.Add(new KeyValuePair<string, double>(s.Name, v));
                        }
                    }
                    else if (t == HardwareType.GpuNvidia || t == HardwareType.GpuAmd || t == HardwareType.GpuIntel)
                    {
                        GpuInfo g = new GpuInfo { Prio = t == HardwareType.GpuNvidia ? 0 : t == HardwareType.GpuAmd ? 1 : 2 };
                        foreach (ISensor s in hw.Sensors)
                        {
                            double v = Val(s);
                            if (double.IsNaN(v)) continue;
                            string n = s.Name;
                            if (s.SensorType == SensorType.Temperature && n == "GPU Core") g.Temp = v;
                            else if (s.SensorType == SensorType.Load && n == "GPU Core") g.Load = v;
                            else if (s.SensorType == SensorType.Load && n == "GPU Memory") g.MemLoad = v;
                            else if (s.SensorType == SensorType.SmallData && n == "GPU Memory Used") g.Used = v;
                            else if (s.SensorType == SensorType.SmallData && n == "GPU Memory Total") g.Total = v;
                            else if (s.SensorType == SensorType.Power) g.Power.Add(new KeyValuePair<string, double>(n, v));
                            else if (s.SensorType == SensorType.Fan && v > 0) g.Fan = double.IsNaN(g.Fan) ? v : Math.Max(g.Fan, v);
                        }
                        gpus.Add(g);
                    }
                    else if (t == HardwareType.Storage)
                    {
                        foreach (ISensor s in hw.Sensors)
                        {
                            double v = Val(s);
                            if (s.SensorType != SensorType.Temperature || double.IsNaN(v)) continue;
                            string n = s.Name.ToLowerInvariant();
                            if (n.Contains("warning") || n.Contains("critical") || n.Contains("limit") || n.Contains("threshold")) continue; // це пороги
                            if (v > 0 && v < 100) diskTemps.Add(v);
                        }
                    }
                    else if (t != HardwareType.Motherboard)
                    {
                        foreach (ISensor s in hw.Sensors)  // SuperIO та інші: вентилятори
                        {
                            double v = Val(s);
                            if (s.SensorType == SensorType.Fan && !double.IsNaN(v) && v > 0) fans.Add(v);
                        }
                    }
                }
            }

            CpuTemp = PickCpuTemp(cpuTemps);
            CpuOk = !double.IsNaN(CpuTemp);
            CpuPower = PickPower(cpuPw, "Package");
            DiskTemp = diskTemps.Count > 0 ? Max(diskTemps) : double.NaN;
            FanBoard = fans.Count > 0 ? Max(fans) : double.NaN;

            if (gpus.Count > 0)
            {
                gpus.Sort((a, b) => a.Prio.CompareTo(b.Prio));
                GpuInfo g = gpus[0];
                GpuTemp = g.Temp; GpuLoad = g.Load;
                GpuPower = PickPower(g.Power, "Package", "GPU Power", "Power");
                FanGpu = g.Fan;
                VramUsedMb = g.Used; VramTotalMb = g.Total;
                if (!double.IsNaN(g.Total) && double.IsNaN(g.Used) && !double.IsNaN(g.MemLoad)) VramUsedMb = g.Total * g.MemLoad / 100.0;
            }
        }

        static double Max(List<double> l)
        {
            double m = double.MinValue;
            foreach (double d in l) if (d > m) m = d;
            return m;
        }
    }
}
