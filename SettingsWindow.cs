using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SystemMonitor
{
    internal sealed class SettingsWindow : Window
    {
        readonly DispatcherTimer statusTimer;
        TextBlock stAdmin, stSensors, stCpu, stPm, stPawn, stMouse, stAuto, stAudio;
        static AppConfig Cfg { get { return AppConfig.Cur; } }

        public SettingsWindow()
        {
            Title = "Налаштування монітора";
            Width = 620;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;

            TabControl tabs = new TabControl { Margin = new Thickness(8) };
            tabs.Items.Add(MakeTab("Вигляд", BuildLook()));
            tabs.Items.Add(MakeTab("Фон (GIF)", BuildBackground()));
            tabs.Items.Add(MakeTab("Рядки", BuildRows()));
            tabs.Items.Add(MakeTab("Віджети", BuildWidgets()));
            tabs.Items.Add(MakeTab("Компоненти", BuildComponents()));
            Content = tabs;

            statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            statusTimer.Tick += delegate { RefreshStatus(); };
            statusTimer.Start();
            Closed += delegate { statusTimer.Stop(); };
            RefreshStatus();
        }

        static TabItem MakeTab(string header, UIElement content)
        {
            return new TabItem { Header = header, Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content } };
        }

        static StackPanel NewPanel() { return new StackPanel { Margin = new Thickness(14) }; }

        // ------------------------------------------------------------ допоміжні елементи
        static void AddHeader(StackPanel p, string text)
        {
            p.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
        }

        static void AddNote(StackPanel p, string text)
        {
            p.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 4) });
        }

        static void AddCheck(StackPanel p, string label, Func<bool> get, Action<bool> set)
        {
            CheckBox cb = new CheckBox { Content = label, IsChecked = get(), Margin = new Thickness(0, 3, 0, 3) };
            cb.Click += delegate { set(cb.IsChecked == true); AppCore.RequestApply(); };
            p.Children.Add(cb);
        }

        static void AddSlider(StackPanel p, string label, int min, int max, Func<int> get, Action<int> set)
        {
            Grid g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            TextBlock lb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Slider sl = new Slider { Minimum = min, Maximum = max, Value = get(), IsSnapToTickEnabled = true, TickFrequency = 1 };
            TextBlock val = new TextBlock { Text = get().ToString(), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(sl, 1); Grid.SetColumn(val, 2);
            sl.ValueChanged += delegate
            {
                int v = (int)Math.Round(sl.Value);
                val.Text = v.ToString();
                set(v);
                AppCore.RequestApply();
            };
            g.Children.Add(lb); g.Children.Add(sl); g.Children.Add(val);
            p.Children.Add(g);
        }

        static void AddHex(StackPanel p, string label, Func<string> get, Action<string> set)
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = label, Width = 210, VerticalAlignment = VerticalAlignment.Center });
            TextBox tb = new TextBox { Text = get(), Width = 100 };
            Border sw = new Border { Width = 24, Height = 24, Margin = new Thickness(8, 0, 0, 0), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Util.Brush(get()) };
            Action commit = delegate
            {
                Color c;
                string t = tb.Text.Trim();
                if (Util.TryHex(t, out c)) { set(t); sw.Background = Util.Brush(c); AppCore.RequestApply(); }
            };
            tb.LostFocus += delegate { commit(); };
            tb.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) commit(); };
            row.Children.Add(tb); row.Children.Add(sw);
            p.Children.Add(row);
        }

        static TextBox AddText(StackPanel p, string label, Func<string> get, Action<string> set)
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = label, Width = 210, VerticalAlignment = VerticalAlignment.Center });
            TextBox tb = new TextBox { Text = get(), Width = 200 };
            Action commit = delegate { set(tb.Text.Trim()); AppConfig.Cur.Save(); };
            tb.LostFocus += delegate { commit(); };
            tb.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) commit(); };
            row.Children.Add(tb);
            p.Children.Add(row);
            return tb;
        }

        static TextBlock AddStatus(StackPanel p, string label)
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = label, Width = 190, VerticalAlignment = VerticalAlignment.Top });
            TextBlock v = new TextBlock { Width = 360, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray };
            row.Children.Add(v);
            p.Children.Add(row);
            return v;
        }

        // ------------------------------------------------------------ вкладки
        UIElement BuildLook()
        {
            StackPanel p = NewPanel();
            AddHeader(p, "Готові стилі");
            WrapPanel presets = new WrapPanel();
            foreach (string name in AppConfig.PresetNames)
            {
                string n = name;
                Button b = new Button { Content = n, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4, 10, 4) };
                b.Click += delegate
                {
                    AppConfig.ApplyPreset(n);
                    AppCore.ApplyAll();
                    AppCore.OpenSettings(true);
                };
                presets.Children.Add(b);
            }
            p.Children.Add(presets);

            AddHeader(p, "Скло");
            StackPanel modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            modeRow.Children.Add(new TextBlock { Text = "Спосіб скла", Width = 210, VerticalAlignment = VerticalAlignment.Center });
            ComboBox cb = new ComboBox { Width = 230 };
            cb.Items.Add("Авто (рекомендовано)");
            cb.Items.Add("Суцільний (без скла)");
            cb.Items.Add("Acrylic (запасний)");
            cb.Items.Add("Windows 11 Acrylic");
            cb.SelectedIndex = Math.Max(0, Math.Min(3, Cfg.GlassMode + 1));
            cb.SelectionChanged += delegate { Cfg.GlassMode = cb.SelectedIndex - 1; AppCore.RequestApply(); };
            modeRow.Children.Add(cb);
            p.Children.Add(modeRow);
            AddNote(p, "Скло розмиває те, що позаду, і оновлюється системою в реальному часі. Якщо панель виглядає непрозорою або чорною, виберіть інший спосіб.");

            AddSlider(p, "Закруглення кутів", 0, 40, () => Cfg.CornerRadius, v => Cfg.CornerRadius = v);
            AddSlider(p, "Підфарбування, %", 0, 100, () => Cfg.TintStrength, v => Cfg.TintStrength = v);
            AddSlider(p, "Блік зверху, %", 0, 100, () => Cfg.Highlight, v => Cfg.Highlight = v);
            AddSlider(p, "Рамка, %", 0, 100, () => Cfg.Border, v => Cfg.Border = v);
            AddCheck(p, "Скло теплішає разом із температурою", () => Cfg.TempTint, v => Cfg.TempTint = v);
            AddCheck(p, "Живий рідкий блік (повільна світла смуга)", () => Cfg.AnimatedSheen, v => Cfg.AnimatedSheen = v);

            AddHeader(p, "Кольори та текст");
            AddHex(p, "Колір скла", () => Cfg.TintColor, v => Cfg.TintColor = v);
            AddHex(p, "Колір суцільного фону", () => Cfg.SolidColor, v => Cfg.SolidColor = v);
            AddHex(p, "Колір тексту", () => Cfg.TextColor, v => Cfg.TextColor = v);
            AddSlider(p, "Розмір шрифту", 9, 24, () => Cfg.FontSize, v => Cfg.FontSize = v);
            AddSlider(p, "Відступ усередині", 4, 30, () => Cfg.Padding, v => Cfg.Padding = v);
            AddSlider(p, "Висота графіків", 24, 90, () => Cfg.GraphHeight, v => Cfg.GraphHeight = v);
            return p;
        }

        UIElement BuildBackground()
        {
            StackPanel p = NewPanel();
            AddHeader(p, "GIF або картинка на фоні віджетів");
            TextBlock path = new TextBlock { Text = string.IsNullOrEmpty(Cfg.BackgroundPath) ? "(не вибрано)" : Cfg.BackgroundPath, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) };
            p.Children.Add(path);
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal };
            Button pick = new Button { Content = "Вибрати файл…", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
            pick.Click += delegate
            {
                Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.Filter = "GIF та зображення|*.gif;*.png;*.jpg;*.jpeg;*.bmp|Усі файли|*.*";
                if (dlg.ShowDialog() == true)
                {
                    Cfg.BackgroundPath = dlg.FileName;
                    path.Text = dlg.FileName;
                    AppCore.RequestApply();
                }
            };
            Button clear = new Button { Content = "Прибрати фон", Padding = new Thickness(12, 4, 12, 4) };
            clear.Click += delegate { Cfg.BackgroundPath = ""; path.Text = "(не вибрано)"; AppCore.RequestApply(); };
            btns.Children.Add(pick); btns.Children.Add(clear);
            p.Children.Add(btns);

            AddSlider(p, "Прозорість фону (видимість), %", 0, 100, () => Cfg.BackgroundOpacity, v => Cfg.BackgroundOpacity = v);
            AddCheck(p, "Показувати на головній панелі", () => Cfg.BackgroundOnPanel, v => Cfg.BackgroundOnPanel = v);
            AddCheck(p, "Показувати на плеєрі", () => Cfg.BackgroundOnPlayer, v => Cfg.BackgroundOnPlayer = v);
            AddNote(p, "0% = фон не видно, 100% = повністю видно. GIF анімується. Для читабельності тексту краще 20–50%.");
            return p;
        }

        UIElement BuildRows()
        {
            StackPanel p = NewPanel();
            AddHeader(p, "Що показувати на панелі");
            foreach (RowDef r in MainPanel.RowDefs)
            {
                string key = r.Key;
                AddCheck(p, r.Label, () => Cfg.Rows.ContainsKey(key) && Cfg.Rows[key], v => Cfg.Rows[key] = v);
            }
            AddHeader(p, "Параметри");
            AddText(p, "Назва мишки в Windows (через кому)", () => Cfg.MouseName, v => Cfg.MouseName = v);
            AddText(p, "Хост для ping", () => Cfg.PingHost, v => Cfg.PingHost = v);
            return p;
        }

        static string HotkeyText()
        {
            AppConfig c = Cfg;
            string s = "";
            if ((c.HotkeyMods & 2) != 0) s += "Ctrl+";
            if ((c.HotkeyMods & 1) != 0) s += "Alt+";
            if ((c.HotkeyMods & 4) != 0) s += "Shift+";
            if ((c.HotkeyMods & 8) != 0) s += "Win+";
            if (c.HotkeyVk == 0) return "(не задано)";
            return s + KeyInterop.KeyFromVirtualKey(c.HotkeyVk);
        }

        UIElement BuildWidgets()
        {
            StackPanel p = NewPanel();
            AddHeader(p, "Які віджети показувати");
            AddCheck(p, "Головна панель", () => Cfg.ShowPanel, v => Cfg.ShowPanel = v);
            AddCheck(p, "Плеєр Spotify (окремий віджет)", () => Cfg.ShowPlayer, v => Cfg.ShowPlayer = v);
            AddCheck(p, "Радар звуку (звідки чути звук)", () => Cfg.ShowRadar, v => Cfg.ShowRadar = v);
            AddNote(p, "Кожен віджет можна перетягнути мишкою в будь-яке місце, позиція запам'ятається.");

            AddHeader(p, "Сховати / показати всі віджети однією клавішею");
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = "Гаряча клавіша", Width = 210, VerticalAlignment = VerticalAlignment.Center });
            TextBox tb = new TextBox { Width = 200, IsReadOnly = true, Text = HotkeyText() };
            tb.PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                e.Handled = true;
                Key k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift
                    || k == Key.RightShift || k == Key.LWin || k == Key.RWin || k == Key.None) return;
                Cfg.HotkeyVk = KeyInterop.VirtualKeyFromKey(k);
                Cfg.HotkeyMods = (int)Keyboard.Modifiers;
                Cfg.Save();
                tb.Text = HotkeyText();
                if (!AppCore.RegisterHotkey()) tb.Text += "  (зайнята, оберіть іншу)";
            };
            row.Children.Add(tb);
            p.Children.Add(row);
            AddNote(p, "Клацніть у поле й натисніть потрібну клавішу (можна з Ctrl/Alt/Shift). За замовчуванням F9.");

            AddHeader(p, "Радар звуку");
            AddSlider(p, "Розмір, px", 100, 320, () => Cfg.RadarSize, v => Cfg.RadarSize = v);
            AddSlider(p, "Чутливість, %", 10, 150, () => Cfg.RadarSensitivity, v => Cfg.RadarSensitivity = v);
            AddNote(p, "Радар слухає звук системи. У стерео видно лише ліво/право. Щоб бачити спереду/ззаду/збоку, "
                     + "потрібен багатоканальний звук: у Windows (Параметри → Звук → пристрій → Формат) поставте 5.1 або 7.1, "
                     + "а в грі виберіть той самий режим динаміків.");
            stAudio = AddStatus(p, "Стан радара:");
            return p;
        }

        UIElement BuildComponents()
        {
            StackPanel p = NewPanel();
            AddHeader(p, "Стан");
            stAdmin = AddStatus(p, "Права адміністратора:");
            stSensors = AddStatus(p, "Датчики:");
            stCpu = AddStatus(p, "Температура CPU:");
            stPm = AddStatus(p, "PresentMon (FPS):");
            stPawn = AddStatus(p, "Драйвер PawnIO:");
            stMouse = AddStatus(p, "Заряд мишки:");
            stAuto = AddStatus(p, "Автозапуск:");

            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
            Button b1 = new Button { Content = "Завантажити PresentMon", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
            b1.Click += delegate { AppCore.Comp.InstallPresentMon(); };
            Button b2 = new Button { Content = "Встановити PawnIO", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
            b2.Click += delegate { AppCore.Comp.InstallPawnIo(AppCore.Sensors); };
            Button b3 = new Button { Content = "Відкрити журнал", Padding = new Thickness(10, 4, 10, 4) };
            b3.Click += delegate
            {
                try { Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true }); } catch { }
            };
            btns.Children.Add(b1); btns.Children.Add(b2); btns.Children.Add(b3);
            p.Children.Add(btns);

            bool autoOn = AppCore.Comp.AutostartEnabled();
            AppCore.Comp.AutoMsg = autoOn ? "увімкнено" : "вимкнено";
            AddCheck(p, "Запускати разом з Windows (без запитів)", () => autoOn, v => AppCore.Comp.SetAutostart(v));
            AddNote(p, "Журнал: " + Log.FilePath);
            return p;
        }

        // ------------------------------------------------------------ статус
        void RefreshStatus()
        {
            if (stAdmin == null) return;
            bool admin = false;
            try { admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { }
            stAdmin.Text = admin ? "так" : "ні: програма має запускатися від адміністратора";

            SensorService s = AppCore.Sensors;
            stSensors.Text = s.Backend == "lib" ? "LibreHardwareMonitorLib: працює" : s.Backend + (s.Error.Length > 0 ? ": " + s.Error : "");
            stCpu.Text = !s.CpuOk.HasValue ? "…" : s.CpuOk.Value ? "є дані" : "немає даних: потрібен драйвер PawnIO (кнопка нижче), потім перезавантажте ПК";

            FpsService f = AppCore.Fps;
            string exe = AppCore.Comp.PresentMonExe();
            if (f.Status == FpsStatus.Ok) stPm.Text = "працює";
            else if (exe != null)
                stPm.Text = f.Status == FpsStatus.Starting ? "запускається…" : "не запустився: " + (f.LastError.Length > 0 ? f.LastError : "причину дивіться в журналі");
            else stPm.Text = AppCore.Comp.PmMsg.Length > 0 ? AppCore.Comp.PmMsg : "не встановлено";

            stPawn.Text = AppCore.Comp.PawnMsg.Length > 0 ? AppCore.Comp.PawnMsg : "статус невідомий (дивіться «Температура CPU»)";
            stMouse.Text = AppCore.Extras.MouseStatus;
            stAuto.Text = AppCore.Comp.AutoMsg.Length > 0 ? AppCore.Comp.AutoMsg : "—";
            if (stAudio != null) stAudio.Text = AppCore.Audio.Status;
        }
    }
}
