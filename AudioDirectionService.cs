using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using NAudio.Wave;

namespace SystemMonitor
{
    /// <summary>Короткий звуковий «сплеск» (крок, постріл): де почули й наскільки гучно.</summary>
    internal sealed class SoundBlip
    {
        public double Angle;      // градуси від «прямо перед тобою» за годинниковою стрілкою
        public float Strength;    // 0..1
        public double T;          // час появи, секунди
    }

    /// <summary>
    /// Напрямок звуку: слухає те, що грає в системі (WASAPI loopback). Для кожного каналу рахує гучність
    /// (без баса, щоб вибухи й музика не заглушали кроки), далі напрямок як векторну суму динаміків
    /// і короткі сплески. 5.1/7.1 дають спереду/ззаду/збоку, стерео лише ліво/право.
    /// </summary>
    internal sealed class AudioDirectionService
    {
        public float[] Levels = new float[2];
        public double[] Angles = { -30, 30 };   // NaN = канал не малюємо (сабвуфер)
        public float Peak = 0.05f;
        public string Status = "вимкнено";
        public string Layout = "стерео";
        public float DirX, DirY;                // напрямок звуку: X вправо, Y вперед; довжина = гучність відносно піка

        readonly object lk = new object();
        readonly object blipLock = new object();
        readonly List<SoundBlip> blips = new List<SoundBlip>();
        WasapiLoopbackCapture cap;
        bool wanted;
        double[] hpX = new double[2], hpY = new double[2];
        double hpA = 0.97;
        double avgPower = 1e-6, lastBlipT;

        public static double NowS { get { return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency; } }

        public void Start()
        {
            lock (lk)
            {
                wanted = true;
                if (cap != null) return;
                try
                {
                    cap = new WasapiLoopbackCapture();
                    Setup(cap.WaveFormat.Channels, cap.WaveFormat.SampleRate);
                    cap.DataAvailable += OnData;
                    cap.RecordingStopped += OnStopped;
                    cap.StartRecording();
                    Status = "слухаю: " + Layout + " (" + cap.WaveFormat.Channels + " кан., " + cap.WaveFormat.SampleRate + " Гц)";
                    Log.Write("радар звуку: " + Status);
                }
                catch (Exception ex)
                {
                    Status = "помилка: " + ex.Message;
                    Log.Write("радар звуку: " + ex);
                    DisposeCapture();
                }
            }
        }

        public void Stop()
        {
            lock (lk)
            {
                wanted = false;
                DisposeCapture();
                Status = "вимкнено";
            }
        }

        void DisposeCapture()
        {
            if (cap == null) return;
            try { cap.DataAvailable -= OnData; cap.RecordingStopped -= OnStopped; } catch { }
            try { cap.StopRecording(); } catch { }
            try { cap.Dispose(); } catch { }
            cap = null;
        }

        void OnStopped(object sender, StoppedEventArgs e)
        {
            bool again;
            lock (lk) { again = wanted; DisposeCapture(); }
            if (again)
                Task.Delay(2000).ContinueWith(t => { if (wanted) Start(); });  // пристрій звуку змінився: підключаємось знову
        }

        void Setup(int ch, int sampleRate)
        {
            double nan = double.NaN;
            switch (ch)
            {
                case 1: Angles = new double[] { 0 }; Layout = "моно"; break;
                case 2: Angles = new double[] { -30, 30 }; Layout = "стерео"; break;
                case 4: Angles = new double[] { -30, 30, -135, 135 }; Layout = "4.0"; break;
                case 6: Angles = new double[] { -30, 30, 0, nan, -110, 110 }; Layout = "5.1"; break;       // FL FR FC LFE SL SR
                case 8: Angles = new double[] { -30, 30, 0, nan, -150, 150, -90, 90 }; Layout = "7.1"; break; // FL FR FC LFE BL BR SL SR
                default:
                    Angles = new double[ch];
                    for (int i = 0; i < ch; i++) Angles[i] = i == 0 ? -30 : i == 1 ? 30 : nan;
                    Layout = ch + " кан.";
                    break;
            }
            Levels = new float[ch];
            hpX = new double[ch];
            hpY = new double[ch];
            hpA = 1.0 / (1.0 + 2.0 * Math.PI * 300.0 / Math.Max(sampleRate, 8000));   // фільтр високих частот ~300 Гц
            DirX = DirY = 0;
            avgPower = 1e-6;
        }

        static float Sample(byte[] b, int off, WaveFormat wf)
        {
            switch (wf.BitsPerSample)
            {
                case 32:
                    return wf.Encoding == WaveFormatEncoding.IeeeFloat || wf.Encoding == WaveFormatEncoding.Extensible
                        ? BitConverter.ToSingle(b, off)
                        : BitConverter.ToInt32(b, off) / 2147483648f;
                case 16: return BitConverter.ToInt16(b, off) / 32768f;
                case 24: return ((b[off] << 8 | b[off + 1] << 16 | b[off + 2] << 24) >> 8) / 8388608f;
                default: return 0;
            }
        }

        /// <summary>Свіжі сплески (старші за 1.3 с відкидаються).</summary>
        public SoundBlip[] SnapshotBlips()
        {
            double now = NowS;
            lock (blipLock)
            {
                blips.RemoveAll(b => now - b.T > 1.3);
                return blips.ToArray();
            }
        }

        void OnData(object sender, WaveInEventArgs e)
        {
            WasapiLoopbackCapture c = cap;
            if (c == null || e.BytesRecorded <= 0) return;
            WaveFormat wf = c.WaveFormat;
            int ch = wf.Channels;
            int bps = wf.BitsPerSample / 8;
            if (ch <= 0 || bps <= 0 || ch != Angles.Length) return;
            int frames = e.BytesRecorded / (bps * ch);
            if (frames <= 0) return;

            bool hp = AppConfig.Cur.RadarHighPass;
            double[] sum = new double[ch];
            for (int f = 0; f < frames; f++)
                for (int k = 0; k < ch; k++)
                {
                    double v = Sample(e.Buffer, (f * ch + k) * bps, wf);
                    if (hp)
                    {
                        double y = hpA * (hpY[k] + v - hpX[k]);   // прибираємо бас: кроки й постріли лежать вище
                        hpX[k] = v; hpY[k] = y; v = y;
                    }
                    sum[k] += v * v;
                }

            double[] ang = Angles;
            float[] old = Levels;
            float[] now = new float[ch];
            float maxRms = 0;
            double totalP = 0, vx = 0, vy = 0;
            int active = 0;
            for (int k = 0; k < ch; k++)
            {
                double pk = sum[k] / frames;
                float rms = (float)Math.Sqrt(pk);
                float prev = k < old.Length ? old[k] : 0;
                now[k] = Math.Max(rms, prev * 0.80f);   // швидка атака, плавний спад
                if (rms > maxRms) maxRms = rms;
                if (!double.IsNaN(ang[k]))
                {
                    active++;
                    totalP += pk;
                    double th = ang[k] * Math.PI / 180.0;
                    vx += pk * Math.Sin(th);
                    vy += pk * Math.Cos(th);
                }
            }
            Peak = Math.Max(Peak * 0.9995f, maxRms);
            Levels = now;

            // ---- напрямок
            double pAvg = totalP / Math.Max(active, 1);
            double amp = Math.Sqrt(pAvg);
            double level = Math.Min(1.0, amp / Math.Max(Peak, 0.03));
            double azimuth = 0, ux = 0, uy = 1;
            bool sound = amp > 0.0015 && totalP > 1e-12;
            if (sound)
            {
                if (ch == 2)
                {
                    double l = Math.Sqrt(sum[0] / frames), r = Math.Sqrt(sum[1] / frames);
                    double pan = (r - l) / (r + l + 1e-12);          // -1 ліво ... +1 право
                    azimuth = pan * 90.0;
                    ux = Math.Sin(azimuth * Math.PI / 180.0);
                    uy = Math.Cos(azimuth * Math.PI / 180.0);
                }
                else
                {
                    ux = vx / totalP;     // довжина вектора = наскільки звук зосереджений в одному напрямку
                    uy = vy / totalP;
                    azimuth = Math.Atan2(vx, vy) * 180.0 / Math.PI;
                }
                const float k2 = 0.30f;
                DirX = DirX * (1 - k2) + (float)(ux * level) * k2;
                DirY = DirY * (1 - k2) + (float)(uy * level) * k2;
            }
            else
            {
                DirX *= 0.85f;
                DirY *= 0.85f;
            }

            // ---- сплески: різке зростання гучності відносно середнього рівня
            double t = NowS;
            if (sound && pAvg > avgPower * 2.5 && amp > 0.008 && t - lastBlipT > 0.08)
            {
                lastBlipT = t;
                lock (blipLock)
                {
                    blips.Add(new SoundBlip { Angle = azimuth, Strength = (float)Math.Min(1.0, 0.35 + level), T = t });
                    if (blips.Count > 16) blips.RemoveAt(0);
                }
            }
            avgPower = avgPower * 0.97 + pAvg * 0.03;
        }
    }
}
