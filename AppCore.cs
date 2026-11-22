using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SystemMonitor
{
    /// <summary>Спільний стан програми: служби, віджети, гаряча клавіша.</summary>
    internal static class AppCore
    {
        public static SensorService Sensors;
        public static FpsService Fps;
        public static ExtrasService Extras;
        public static MediaService Media;
        public static AudioDirectionService Audio;
        public static Components Comp;
        public static MainPanel Panel;
        public static PlayerWindow Player;
        public static RadarWindow Radar;
        public static bool Hidden;

        static SettingsWindow settings;
        static StatsWindow stats;
        static HwndSource hotkeySource;
        static DispatcherTimer applyTimer;
        const int HotkeyId = 0x5A31;

        static AppConfig Cfg { get { return AppConfig.Cur; } }

        public static void Start()
        {
            Log.Init();
            Log.Write("старт, версія 2.0, ОС " + Environment.OSVersion.Version);
            AppConfig.Load();
            SessionStats.LoadDay();

            Sensors = new SensorService();
            Fps = new FpsService();
            Extras = new ExtrasService();
            Media = new MediaService();
            Audio = new AudioDirectionService();
            Comp = new Components();

            Panel = new MainPanel();
            Player = new PlayerWindow();
            Radar = new RadarWindow();

            applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            applyTimer.Tick += delegate { applyTimer.Stop(); Cfg.Save(); ApplyAll(); };

            ApplyAll();

            if (!Cfg.SetupDone)
            {
                DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                t.Tick += delegate { t.Stop(); FirstRun(); };
                t.Start();
            }
        }

        public static void RequestApply()
        {
            if (applyTimer == null) return;
            applyTimer.Stop();
            applyTimer.Start();
        }

        public static void ApplyAll()
        {
            AppConfig c = Cfg;
            if (!c.ShowPanel && !c.ShowPlayer && !c.ShowRadar) c.ShowPanel = true;   // має лишатися хоч щось, щоб відкрити меню
            Panel.ApplyConfig();
            Player.ApplyConfig();
            Radar.ApplyConfig();
            UpdateVisibility();
            UpdateServices();
            RegisterHotkey();
        }

        static void SetVis(Window w, bool on)
        {
            if (on && !Hidden) { if (!w.IsVisible) w.Show(); }
            else if (w.IsVisible) w.Hide();
        }

        static void UpdateVisibility()
        {
            SetVis(Panel, Cfg.ShowPanel);
            SetVis(Player, Cfg.ShowPlayer);
            SetVis(Radar, Cfg.ShowRadar);
        }

        static void UpdateServices()
        {
            Media.Enabled = Cfg.ShowPlayer && !Hidden;
            if (Cfg.ShowRadar && !Hidden) Audio.Start(); else Audio.Stop();
        }

        public static void ToggleHidden()
        {
            Hidden = !Hidden;
            UpdateVisibility();
            UpdateServices();
        }

        public static void SetHeat(double temp)
        {
            Panel.SetHeat(temp);
            Player.SetHeat(temp);
            Radar.SetHeat(temp);
        }

        // ------------------------------------------------------------ гаряча клавіша
        static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312 && wParam.ToInt32() == HotkeyId)
            {
                ToggleHidden();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public static bool RegisterHotkey()
        {
            if (hotkeySource == null)
            {
                HwndSourceParameters p = new HwndSourceParameters("SystemMonitorHotkey");
                p.Width = 0;
                p.Height = 0;
                p.ParentWindow = new IntPtr(-3);   // HWND_MESSAGE: невидиме вікно лише для повідомлень
                p.WindowStyle = 0;
                hotkeySource = new HwndSource(p);
                hotkeySource.AddHook(Hook);
            }
            Native.UnregisterHotKey(hotkeySource.Handle, HotkeyId);
            if (Cfg.HotkeyVk == 0) return false;
            bool ok = Native.RegisterHotKey(hotkeySource.Handle, HotkeyId, (uint)Cfg.HotkeyMods | Native.MOD_NOREPEAT, (uint)Cfg.HotkeyVk);
            if (!ok) Log.Write("гарячу клавішу зайнято: vk=" + Cfg.HotkeyVk + " mods=" + Cfg.HotkeyMods);
            return ok;
        }

        // ------------------------------------------------------------ вікна
        public static void OpenSettings(bool fresh)
        {
            if (settings != null && settings.IsLoaded)
            {
                if (!fresh) { settings.Activate(); return; }
                settings.Close();
            }
            settings = new SettingsWindow();
            settings.Show();
            settings.Activate();
        }

        public static void OpenStats()
        {
            if (stats != null && stats.IsLoaded) { stats.Activate(); return; }
            stats = new StatsWindow();
            stats.Show();
        }

        static void FirstRun()
        {
            Cfg.SetupDone = true;
            Cfg.Save();
            bool needPm = Comp.PresentMonExe() == null;
            string text = "Щоб показувати FPS і температуру процесора, програмі потрібно:\n\n"
                + (needPm ? "• завантажити PresentMon (безкоштовна програма від Intel, з GitHub): для FPS\n" : "")
                + "• встановити драйвер PawnIO (безкоштовний, його використовує LibreHardwareMonitor): для температури процесора\n\n"
                + "Завантажити й встановити зараз?";
            if (MessageBox.Show(text, "Системний монітор: перший запуск", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                if (needPm) Comp.InstallPresentMon();
                Comp.InstallPawnIo(Sensors);
            }
            if (MessageBox.Show("Запускати монітор разом з Windows?\n(без запитів, з правами адміністратора)", "Системний монітор",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                Comp.SetAutostart(true);
        }

        public static void Quit()
        {
            try
            {
                Cfg.Save();
                SessionStats.SaveDay();
                Fps.Stop();
                Sensors.Stop();
                Extras.Stop();
                Media.Stop();
                Audio.Stop();
                if (hotkeySource != null) Native.UnregisterHotKey(hotkeySource.Handle, HotkeyId);
            }
            catch (Exception ex) { Log.Write("вихід: " + ex.Message); }
            Application.Current.Shutdown();
        }
    }
}
