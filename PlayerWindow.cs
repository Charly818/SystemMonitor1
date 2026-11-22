using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SystemMonitor
{
    /// <summary>Окремий віджет-плеєр: показує, що грає (Spotify чи будь-який інший плеєр), і керує відтворенням.</summary>
    internal sealed class PlayerWindow : GlassWindow
    {
        protected override string Key { get { return "player"; } }
        protected override bool BackgroundEnabled { get { return Cfg.BackgroundOnPlayer; } }

        const double BarWidth = 226;
        readonly Image art = new Image { Width = 72, Height = 72, Stretch = Stretch.UniformToFill };
        readonly TextBlock title = new TextBlock();
        readonly TextBlock artist = new TextBlock();
        readonly TextBlock timeL = new TextBlock();
        readonly TextBlock timeR = new TextBlock();
        readonly Border barBg = new Border { Height = 4, Width = BarWidth, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
        readonly Border barFill = new Border { Height = 4, Width = 0, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
        readonly TextBlock btnPrev = new TextBlock();
        readonly TextBlock btnPlay = new TextBlock();
        readonly TextBlock btnNext = new TextBlock();
        readonly DispatcherTimer timer;
        int shownArt = -1;

        public PlayerWindow()
        {
            Grid root = new Grid { Width = 330 };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            art.Clip = new RectangleGeometry(new Rect(0, 0, 72, 72), 12, 12);
            Grid.SetColumn(art, 0);
            root.Children.Add(art);

            StackPanel right = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(right, 1);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            artist.TextTrimming = TextTrimming.CharacterEllipsis;
            right.Children.Add(title);
            right.Children.Add(artist);

            Grid bar = new Grid { Margin = new Thickness(0, 8, 0, 2), Height = 4, Width = BarWidth, HorizontalAlignment = HorizontalAlignment.Left };
            bar.Children.Add(barBg);
            bar.Children.Add(barFill);
            right.Children.Add(bar);

            Grid times = new Grid { Width = BarWidth, HorizontalAlignment = HorizontalAlignment.Left };
            timeR.HorizontalAlignment = HorizontalAlignment.Right;
            times.Children.Add(timeL);
            times.Children.Add(timeR);
            right.Children.Add(times);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
            Prepare(btnPrev, "\uE892", delegate { AppCore.Media.Previous(); });
            Prepare(btnPlay, "\uE768", delegate { AppCore.Media.PlayPause(); });
            Prepare(btnNext, "\uE893", delegate { AppCore.Media.Next(); });
            buttons.Children.Add(btnPrev);
            buttons.Children.Add(btnPlay);
            buttons.Children.Add(btnNext);
            right.Children.Add(buttons);

            root.Children.Add(right);
            ContentHost.Children.Add(root);
            root.Margin = new Thickness(14);

            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            timer.Tick += delegate { try { Refresh(); } catch (Exception ex) { Log.Write("плеєр: " + ex.Message); } };
            timer.Start();
        }

        static void Prepare(TextBlock tb, string glyph, Action click)
        {
            tb.Text = glyph;
            tb.FontFamily = new FontFamily("Segoe MDL2 Assets");
            tb.FontSize = 22;
            tb.Margin = new Thickness(0, 0, 18, 0);
            tb.Cursor = Cursors.Hand;
            tb.Tag = "nodrag";
            tb.MouseLeftButtonUp += delegate { click(); };
        }

        protected override Point DefaultPos(Rect wa, Size s)
        {
            return new Point(wa.Left + 12, wa.Bottom - s.Height - 12);
        }

        public override void ApplyConfig()
        {
            base.ApplyConfig();
            Brush fg = Util.Brush(Cfg.TextColor);
            Brush dim = Util.Brush("#B8FFFFFF");
            title.Foreground = fg; title.FontSize = Cfg.FontSize + 2; title.FontWeight = FontWeights.SemiBold;
            artist.Foreground = dim; artist.FontSize = Cfg.FontSize;
            timeL.Foreground = dim; timeL.FontSize = Math.Max(9, Cfg.FontSize - 3);
            timeR.Foreground = dim; timeR.FontSize = Math.Max(9, Cfg.FontSize - 3);
            btnPrev.Foreground = fg; btnPlay.Foreground = fg; btnNext.Foreground = fg;
            barBg.Background = Util.Brush("#40FFFFFF");
            barFill.Background = Util.Brush("#6EE7A1");
            foreach (TextBlock tb in new[] { title, artist })
                tb.Effect = new DropShadowEffect { ShadowDepth = 1, BlurRadius = 2, Opacity = 0.8, Color = Colors.Black };
        }

        void Refresh()
        {
            if (!IsVisible) return;
            MediaInfo mi = AppCore.Media.Info;
            if (mi == null || !mi.HasSession)
            {
                title.Text = "Нічого не грає";
                artist.Text = "Увімкніть музику у Spotify";
                timeL.Text = ""; timeR.Text = "";
                barFill.Width = 0;
                btnPlay.Text = "\uE768";
                art.Source = null;
                shownArt = -1;
                return;
            }

            title.Text = string.IsNullOrEmpty(mi.Title) ? "Без назви" : mi.Title;
            artist.Text = mi.Artist;
            btnPlay.Text = mi.Playing ? "\uE769" : "\uE768";

            if (mi.ArtVersion != shownArt)
            {
                shownArt = mi.ArtVersion;
                art.Source = Decode(mi.Art);
            }

            double pos = mi.PositionSec;
            if (mi.Playing) pos += (DateTime.UtcNow - mi.UpdatedUtc).TotalSeconds;
            double dur = mi.DurationSec;
            if (dur > 0)
            {
                pos = Math.Max(0, Math.Min(pos, dur));
                barFill.Width = BarWidth * pos / dur;
                timeL.Text = Util.Mmss(pos);
                timeR.Text = Util.Mmss(dur);
            }
            else
            {
                barFill.Width = 0;
                timeL.Text = ""; timeR.Text = "";
            }
        }

        static BitmapImage Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                BitmapImage bi = new BitmapImage();
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    bi.EndInit();
                }
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }
    }
}
