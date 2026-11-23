using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfAnimatedGif;
using WShapes = System.Windows.Shapes;

namespace SystemMonitor
{
    /// <summary>
    /// Основа всіх віджетів: справжнє скло Windows (розмиття оновлюється системою в реальному часі),
    /// закруглені кути, підфарбування, блік, рамка, GIF/картинка на фоні, перетягування, збереження позиції.
    /// </summary>
    internal abstract class GlassWindow : Window
    {
        protected readonly Border Chrome = new Border();
        protected readonly Grid Layers = new Grid();
        protected readonly Image GlassImage = new Image();
        protected readonly Image BgImage = new Image();
        protected readonly WShapes.Rectangle TintRect = new WShapes.Rectangle();
        protected readonly WShapes.Rectangle HighlightRect = new WShapes.Rectangle();
        protected readonly WShapes.Rectangle SheenRect = new WShapes.Rectangle();
        protected readonly Grid ContentHost = new Grid();
        protected IntPtr Hwnd = IntPtr.Zero;

        double heatTemp = double.NaN;
        Color tintColor = Colors.White;
        double tintAlpha = 0.14;
        LiquidGlass lg;
        readonly DispatcherTimer glassTimer;
        bool liquidFailed;
        int glassErrors;
        int lastCfgMode = -99;
        bool placed;
        double lastSheenWidth = -1;
        readonly DispatcherTimer saveTimer;
        readonly DispatcherTimer topTimer;

        protected abstract string Key { get; }

        /// <summary>Режим, який реально діє: якщо Liquid Glass не вдалося запустити, показуємо суцільний.</summary>
        int Mode
        {
            get
            {
                int m = Cfg.EffectiveGlassMode();
                return (m == 3 && liquidFailed) ? 0 : m;
            }
        }
        protected virtual bool BackgroundEnabled { get { return false; } }
        protected AppConfig Cfg { get { return AppConfig.Cur; } }

        protected GlassWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            AllowsTransparency = true;   // прозорість по пікселях: кути гладкі, скло справді прозоре
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;

            Layers.ClipToBounds = true;
            Layers.SizeChanged += delegate { ApplyRegion(); };
            BgImage.Stretch = Stretch.UniformToFill;
            BgImage.IsHitTestVisible = false;
            BgImage.Visibility = Visibility.Collapsed;
            BgImage.HorizontalAlignment = HorizontalAlignment.Left;
            BgImage.VerticalAlignment = VerticalAlignment.Top;
            BgImage.SetBinding(FrameworkElement.WidthProperty, new Binding("ActualWidth") { Source = Layers });
            BgImage.SetBinding(FrameworkElement.HeightProperty, new Binding("ActualHeight") { Source = Layers });

            TintRect.IsHitTestVisible = false;
            HighlightRect.IsHitTestVisible = false;
            SheenRect.IsHitTestVisible = false;
            SheenRect.HorizontalAlignment = HorizontalAlignment.Left;

            GlassImage.Stretch = Stretch.Fill;
            GlassImage.IsHitTestVisible = false;
            GlassImage.Visibility = Visibility.Collapsed;
            GlassImage.HorizontalAlignment = HorizontalAlignment.Left;
            GlassImage.VerticalAlignment = VerticalAlignment.Top;
            RenderOptions.SetBitmapScalingMode(GlassImage, BitmapScalingMode.Linear);
            GlassImage.SetBinding(FrameworkElement.WidthProperty, new Binding("ActualWidth") { Source = Layers });
            GlassImage.SetBinding(FrameworkElement.HeightProperty, new Binding("ActualHeight") { Source = Layers });

            Layers.Children.Add(GlassImage);
            Layers.Children.Add(BgImage);
            Layers.Children.Add(TintRect);
            Layers.Children.Add(HighlightRect);
            Layers.Children.Add(SheenRect);
            Layers.Children.Add(ContentHost);
            Chrome.Child = Layers;
            Content = Chrome;

            MouseLeftButtonDown += OnDown;
            SizeChanged += delegate { ApplyRegion(); SetupSheen(); };
            Loaded += delegate { PlaceFirst(); };
            LocationChanged += delegate { if (placed) { saveTimer.Stop(); saveTimer.Start(); } };

            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            saveTimer.Tick += delegate
            {
                saveTimer.Stop();
                Cfg.Positions[Key] = new[] { Left, Top };
                Cfg.Save();
            };

            glassTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            glassTimer.Tick += delegate { GlassFrame(); };
            Closed += delegate { glassTimer.Stop(); if (lg != null) lg.Dispose(); };

            topTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            topTimer.Tick += delegate
            {
                if (IsVisible && Hwnd != IntPtr.Zero)
                    Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            };
            topTimer.Start();
        }

        // ------------------------------------------------------------ життєвий цикл
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Hwnd = new WindowInteropHelper(this).Handle;
            Log.Write("вікно " + Key + " створено, режим скла " + Cfg.EffectiveGlassMode());
            try
            {
                int ex = Native.GetWindowLong(Hwnd, Native.GWL_EXSTYLE);
                Native.SetWindowLong(Hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
                ContextMenu = BuildMenu();
                ApplyGlassMode();
                ApplyRegion();
            }
            catch (Exception exc) { Log.Write("вікно " + Key + ", стилі: " + exc); }
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            FrameworkElement fe = e.OriginalSource as FrameworkElement;
            if (fe != null && fe.Tag is string && (string)fe.Tag == "nodrag") return;
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        protected virtual ContextMenu BuildMenu()
        {
            ContextMenu m = new ContextMenu();
            m.Items.Add(MakeItem("Налаштування…", delegate { AppCore.OpenSettings(false); }));
            m.Items.Add(MakeItem("Статистика…", delegate { AppCore.OpenStats(); }));
            m.Items.Add(MakeItem("Сховати все (гаряча клавіша повертає)", delegate { AppCore.ToggleHidden(); }));
            m.Items.Add(new Separator());
            m.Items.Add(MakeItem("Вийти", delegate { AppCore.Quit(); }));
            return m;
        }

        protected static MenuItem MakeItem(string header, Action act)
        {
            MenuItem mi = new MenuItem { Header = header };
            mi.Click += delegate { act(); };
            return mi;
        }

        // ------------------------------------------------------------ позиція
        protected virtual Point DefaultPos(Rect wa, Size s)
        {
            return new Point(wa.Right - s.Width - 12, wa.Bottom - s.Height - 12);
        }

        static bool Inside(double x, double y)
        {
            return x > SystemParameters.VirtualScreenLeft - 40 && y > SystemParameters.VirtualScreenTop - 10
                && x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40
                && y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
        }

        void PlaceFirst()
        {
            double[] p;
            if (Cfg.Positions.TryGetValue(Key, out p) && p != null && p.Length == 2 && Inside(p[0], p[1]))
            {
                Left = p[0]; Top = p[1];
            }
            else
            {
                Point d = DefaultPos(SystemParameters.WorkArea, new Size(ActualWidth, ActualHeight));
                Left = d.X; Top = d.Y;
            }
            placed = true;
        }

        // ------------------------------------------------------------ вигляд
        public virtual void ApplyConfig()
        {
            ApplyStyle();
            ApplyBackground();
        }

        public void SetHeat(double temp)
        {
            heatTemp = temp;
            UpdateTint();
        }

        void ApplyStyle()
        {
            AppConfig c = Cfg;
            if (c.EffectiveGlassMode() != lastCfgMode) { lastCfgMode = c.EffectiveGlassMode(); liquidFailed = false; glassErrors = 0; }
            Chrome.CornerRadius = new CornerRadius(c.CornerRadius);
            Chrome.BorderThickness = new Thickness(c.Border > 0 ? 1 : 0);

            double rim = c.Border / 100.0;
            LinearGradientBrush rimBrush = new LinearGradientBrush(
                Color.FromArgb((byte)(255 * rim * 0.9), 255, 255, 255),
                Color.FromArgb((byte)(255 * rim * 0.2), 255, 255, 255), 45);
            rimBrush.Freeze();
            Chrome.BorderBrush = rimBrush;

            byte ha = (byte)(255 * (c.Highlight / 100.0) * 0.30);
            LinearGradientBrush hl = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            hl.GradientStops.Add(new GradientStop(Color.FromArgb(ha, 255, 255, 255), 0.0));
            hl.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.55));
            hl.Freeze();
            HighlightRect.Fill = hl;

            UpdateTint();
            ApplyGlassMode();
            ApplyRegion();
            lastSheenWidth = -1;
            SetupSheen();
        }

        void UpdateTint()
        {
            AppConfig c = Cfg;
            int mode = Mode;
            Color baseC = mode == 0 ? Util.Hex(c.SolidColor, "#14161C") : Util.Hex(c.TintColor);
            double a = mode == 0 ? 1.0 : c.TintStrength / 100.0;
            if (c.TempTint && !double.IsNaN(heatTemp))
            {
                double frac = Math.Max(0, Math.Min(1, (heatTemp - 55) / 35.0));
                if (frac > 0)
                {
                    baseC = Util.Lerp(baseC, Color.FromRgb(255, 70, 50), frac);
                    if (mode != 0) a = Math.Min(1.0, a + frac * 0.30);
                }
            }
            tintColor = baseC;
            tintAlpha = a;
            // у режимі Liquid Glass підфарбування вже вшите в картинку скла
            TintRect.Fill = mode == 3 ? Brushes.Transparent : (Brush)Util.Brush(Color.FromArgb((byte)(a * 255), baseC.R, baseC.G, baseC.B));
        }

        void ApplyGlassMode()
        {
            if (Hwnd == IntPtr.Zero) return;
            int mode = Mode;
            Background = Brushes.Transparent;

            if (mode == 3)
            {
                // ховаємо наше вікно від знімків екрана, щоб розмивати лише те, що позаду нього
                if (!Native.SetWindowDisplayAffinity(Hwnd, Native.WDA_EXCLUDEFROMCAPTURE))
                {
                    Log.Write("вікно " + Key + ": Liquid Glass недоступний (SetWindowDisplayAffinity), переходжу на суцільний");
                    liquidFailed = true;
                    UpdateTint();
                    ApplyGlassMode();
                    return;
                }
                GlassImage.Visibility = Visibility.Visible;
                glassTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(5, Math.Min(60, Cfg.GlassFps)));
                glassTimer.Start();
            }
            else
            {
                Native.SetWindowDisplayAffinity(Hwnd, 0);
                GlassImage.Visibility = Visibility.Collapsed;
                glassTimer.Stop();
            }
        }

        /// <summary>Один кадр Liquid Glass: знімок екрана позаду вікна, розмиття, заломлення, показ.</summary>
        void GlassFrame()
        {
            if (!IsVisible || Hwnd == IntPtr.Zero || Mode != 3) return;
            try
            {
                Native.RECT rc;
                if (!Native.GetWindowRect(Hwnd, out rc)) return;
                double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                AppConfig c = Cfg;
                LgParams p = new LgParams
                {
                    Blur = c.Blur, Refraction = c.Refraction, Dispersion = c.Dispersion, Saturation = c.Saturation,
                    Brightness = c.Brightness, Highlight = c.Highlight, Radius = c.CornerRadius, Strength = c.GlassStrength,
                    TintR = tintColor.R, TintG = tintColor.G, TintB = tintColor.B, TintAlpha = tintAlpha,
                };
                if (lg == null) lg = new LiquidGlass();
                lg.Render(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top, dpi, p);
                if (lg.Bitmap != null && !ReferenceEquals(GlassImage.Source, lg.Bitmap)) GlassImage.Source = lg.Bitmap;
                glassErrors = 0;
            }
            catch (Exception ex)
            {
                if (++glassErrors == 1) Log.Write("Liquid Glass, кадр: " + ex);
                if (glassErrors > 20)
                {
                    Log.Write("Liquid Glass: забагато помилок, переходжу на суцільний");
                    liquidFailed = true;
                    UpdateTint();
                    ApplyGlassMode();
                }
            }
        }

        /// <summary>Гладко заокруглює всі шари скла напівпрозорою маскою (без зубчастих кутів).</summary>
        void ApplyRegion()
        {
            double w = Layers.ActualWidth, h = Layers.ActualHeight;
            if (w <= 1 || h <= 1) return;
            double r = Math.Max(0, Cfg.CornerRadius - (Cfg.Border > 0 ? 1 : 0));
            RectangleGeometry geo = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
            DrawingBrush mask = new DrawingBrush(new GeometryDrawing(Brushes.Black, null, geo));
            mask.Freeze();
            Layers.OpacityMask = mask;
        }

        /// <summary>Живий «рідкий» блік: м'яка світла смуга повільно пропливає по склу.</summary>
        void SetupSheen()
        {
            if (!Cfg.AnimatedSheen)
            {
                SheenRect.Visibility = Visibility.Collapsed;
                lastSheenWidth = -1;
                return;
            }
            double w = ActualWidth;
            if (w <= 10 || Math.Abs(w - lastSheenWidth) < 1) return;
            lastSheenWidth = w;

            LinearGradientBrush b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(38, 255, 255, 255), 0.5));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0));
            b.Freeze();
            SheenRect.Fill = b;
            SheenRect.Width = w * 0.45;
            SheenRect.Visibility = Visibility.Visible;

            TranslateTransform tt = new TranslateTransform();
            TransformGroup g = new TransformGroup();
            g.Children.Add(new SkewTransform(-18, 0));
            g.Children.Add(tt);
            SheenRect.RenderTransform = g;
            DoubleAnimation anim = new DoubleAnimation(-w * 0.6, w * 1.1, TimeSpan.FromSeconds(7));
            anim.RepeatBehavior = RepeatBehavior.Forever;
            tt.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        // ------------------------------------------------------------ GIF / картинка на фоні
        protected void ApplyBackground()
        {
            string p = Cfg.BackgroundPath;
            try { ImageBehavior.SetAnimatedSource(BgImage, null); } catch { }
            BgImage.Source = null;
            if (!BackgroundEnabled || string.IsNullOrEmpty(p) || !File.Exists(p))
            {
                BgImage.Visibility = Visibility.Collapsed;
                return;
            }
            try
            {
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(p);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                if (p.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) ImageBehavior.SetAnimatedSource(BgImage, bi);
                else BgImage.Source = bi;
                BgImage.Opacity = Cfg.BackgroundOpacity / 100.0;
                BgImage.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Log.Write("фон: " + ex.Message);
                BgImage.Visibility = Visibility.Collapsed;
            }
        }
    }
}
