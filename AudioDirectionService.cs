using System;
using System.Threading.Tasks;
using NAudio.Wave;

namespace SystemMonitor
{
    /// <summary>
    /// Напрямок звуку: слухає те, що грає в системі (WASAPI loopback), рахує гучність кожного каналу.
    /// При 5.1/7.1 видно спереду/ззаду/збоку, при стерео лише ліво/право.
    /// </summary>
    internal sealed class AudioDirectionService
    {
        public float[] Levels = new float[2];
        public double[] Angles = { -30, 30 };   // градуси від «прямо перед тобою», за годинниковою; NaN = канал не малюємо (сабвуфер)
        public float Peak = 0.05f;
        public string Status = "вимкнено";

        readonly object lk = new object();
        WasapiLoopbackCapture cap;
        bool wanted;

        public void Start()
        {
            lock (lk)
            {
                wanted = true;
                if (cap != null) return;
                try
                {
                    cap = new WasapiLoopbackCapture();
                    Setup(cap.WaveFormat.Channels);
                    cap.DataAvailable += OnData;
                    cap.RecordingStopped += OnStopped;
                    cap.StartRecording();
                    Status = "слухаю: " + cap.WaveFormat.Channels + " кан., " + cap.WaveFormat.SampleRate + " Гц";
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

        void Setup(int ch)
        {
            double nan = double.NaN;
            switch (ch)
            {
                case 1: Angles = new double[] { 0 }; break;
                case 2: Angles = new double[] { -30, 30 }; break;
                case 4: Angles = new double[] { -30, 30, -135, 135 }; break;
                case 6: Angles = new double[] { -30, 30, 0, nan, -110, 110 }; break;       // FL FR FC LFE SL SR
                case 8: Angles = new double[] { -30, 30, 0, nan, -150, 150, -90, 90 }; break; // FL FR FC LFE BL BR SL SR
                default:
                    Angles = new double[ch];
                    for (int i = 0; i < ch; i++) Angles[i] = i == 0 ? -30 : i == 1 ? 30 : nan;
                    break;
            }
            Levels = new float[ch];
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

        void OnData(object sender, WaveInEventArgs e)
        {
            WasapiLoopbackCapture c = cap;
            if (c == null || e.BytesRecorded <= 0) return;
            WaveFormat wf = c.WaveFormat;
            int ch = wf.Channels;
            int bps = wf.BitsPerSample / 8;
            if (ch <= 0 || bps <= 0) return;
            int frames = e.BytesRecorded / (bps * ch);
            if (frames <= 0) return;

            double[] sum = new double[ch];
            for (int f = 0; f < frames; f++)
                for (int k = 0; k < ch; k++)
                {
                    float v = Sample(e.Buffer, (f * ch + k) * bps, wf);
                    sum[k] += v * v;
                }

            float[] old = Levels;
            float[] now = new float[ch];
            float maxRms = 0;
            for (int k = 0; k < ch; k++)
            {
                float rms = (float)Math.Sqrt(sum[k] / frames);
                float prev = k < old.Length ? old[k] : 0;
                now[k] = Math.Max(rms, prev * 0.80f);   // швидка атака, плавний спад
                if (rms > maxRms) maxRms = rms;
            }
            Peak = Math.Max(Peak * 0.9995f, maxRms);
            Levels = now;
        }
    }
}
