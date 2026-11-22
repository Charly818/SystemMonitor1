using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SystemMonitor
{
    internal sealed class StatsWindow : Window
    {
        readonly TextBlock text = new TextBlock { FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 14, Margin = new Thickness(16) };
        readonly DispatcherTimer timer;

        public StatsWindow()
        {
            Title = "Статистика";
            Icon = Util.AppIcon();
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            Content = text;
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += delegate { Refresh(); };
            timer.Start();
            Closed += delegate { timer.Stop(); };
            Refresh();
        }

        static string T(double v) { return double.IsNaN(v) ? "—" : string.Format("{0:0}°C", v); }

        void Refresh()
        {
            SessionStats s = SessionStats.Cur;
            string avg = s.FpsN > 0 ? string.Format("{0:0}", s.FpsSum / s.FpsN) : "—";
            string low = double.IsNaN(s.MinLow1) ? "—" : string.Format("{0:0}", s.MinLow1);
            string ping = double.IsNaN(s.MaxPing) ? "—" : string.Format("{0:0} мс", s.MaxPing);
            text.Text =
                "Монітор працює:        " + Util.Dur((DateTime.Now - s.Start).TotalSeconds) + "\n" +
                "Макс. температура CPU: " + T(s.MaxCpuTemp) + "\n" +
                "Макс. температура GPU: " + T(s.MaxGpuTemp) + "\n" +
                "Час у іграх:           " + Util.Dur(s.GameSeconds) + "\n" +
                "Середній FPS у іграх:  " + avg + "\n" +
                "Макс. FPS:             " + (s.MaxFps > 0 ? string.Format("{0:0}", s.MaxFps) : "—") + "\n" +
                "Найгірший 1% low:      " + low + "\n" +
                "Макс. ping:            " + ping + "\n" +
                "Завантажено сьогодні:  ↓ " + Fmt.Bytes(s.DayDown) + "   ↑ " + Fmt.Bytes(s.DayUp) + "\n\n" +
                "(рахується, поки монітор запущений)";
        }
    }
}
