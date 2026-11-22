using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WShapes = System.Windows.Shapes;

namespace SystemMonitor
{
    /// <summary>
    /// Радар звуку: сектори за каналами, стрілка загального напрямку і короткі сплески (кроки, постріли),
    /// що з'являються там, звідки почули звук, і згасають за секунду.
    /// </summary>
    internal sealed class RadarWindow : GlassWindow
    {
        protected override string Key { get { return "radar"; } }

        const int BlipPool = 14;
        readonly Canvas canvas = new Canvas();
        readonly DispatcherTimer timer;
        readonly List<WShapes.Path> wedges = new List<WShapes.Path>();
        readonly List<WShapes.Ellipse> blipDots = new List<WShapes.Ellipse>();
        readonly WShapes.Line arrow = new WShapes.Line { StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Triangle, Visibility = Visibility.Collapsed };
        readonly TextBlock label = new TextBlock();
        int built = -1;
        double size = 170;

        public RadarWindow()
        {
            ContentHost.Children.Add(canvas);
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += delegate { try { Frame(); } catch (Exception ex) { Log.Write("радар: " + ex.Message); } };
            IsVisibleChanged += delegate { if (IsVisible) timer.Start(); else timer.Stop(); };
        }

        protected override Point DefaultPos(Rect wa, Size s)
        {
            return new Point(wa.Left + 12, wa.Top + 12);
        }

        public override void ApplyConfig()
        {
            base.ApplyConfig();
            size = Math.Max(100, Cfg.RadarSize);
            canvas.Width = size; canvas.Height = size;
            canvas.Margin = new Thickness(10);
            built = -1;
            Rebuild();
        }

        static Geometry Wedge(double cx, double cy, double r0, double r1, double angDeg, double halfDeg)
        {
            Func<double, double, Point> P = delegate (double rr, double deg)
            {
                double t = deg * Math.PI / 180.0;
                return new Point(cx + rr * Math.Sin(t), cy - rr * Math.Cos(t));
            };
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(P(r1, angDeg - halfDeg), true, true);
                ctx.ArcTo(P(r1, angDeg + halfDeg), new Size(r1, r1), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(P(r0, angDeg + halfDeg), true, false);
                ctx.ArcTo(P(r0, angDeg - halfDeg), new Size(r0, r0), 0, false, SweepDirection.Counterclockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        void Rebuild()
        {
            canvas.Children.Clear();
            wedges.Clear();
            blipDots.Clear();
            double[] ang = AppCore.Audio.Angles;
            built = ang.Length;
            double c = size / 2;

            canvas.Children.Add(new WShapes.Ellipse { Width = size, Height = size, Stroke = Util.Brush("#40FFFFFF"), StrokeThickness = 1 });
            WShapes.Ellipse inner = new WShapes.Ellipse { Width = size * 0.3, Height = size * 0.3, Stroke = Util.Brush("#30FFFFFF"), StrokeThickness = 1 };
            Canvas.SetLeft(inner, c - size * 0.15); Canvas.SetTop(inner, c - size * 0.15);
            canvas.Children.Add(inner);

            int active = 0;
            foreach (double a in ang) if (!double.IsNaN(a)) active++;
            double half = Math.Min(40, 360.0 / Math.Max(active, 1) / 2 * 0.85);
            if (active <= 2) half = 38;

            for (int i = 0; i < ang.Length; i++)
            {
                if (double.IsNaN(ang[i])) { wedges.Add(null); continue; }
                WShapes.Path p = new WShapes.Path
                {
                    Data = Wedge(c, c, size * 0.19, size * 0.47, ang[i], half),
                    Fill = Util.Brush("#6EE7A1"),
                    Stroke = Util.Brush("#30FFFFFF"),
                    StrokeThickness = 1,
                    Opacity = 0.12,
                };
                wedges.Add(p);
                canvas.Children.Add(p);
            }

            for (int i = 0; i < BlipPool; i++)
            {
                WShapes.Ellipse d = new WShapes.Ellipse { Width = 10, Height = 10, Visibility = Visibility.Collapsed, Fill = Util.Brush("#FFD166"), Stroke = Util.Brush("#80FFFFFF"), StrokeThickness = 1 };
                blipDots.Add(d);
                canvas.Children.Add(d);
            }

            arrow.X1 = c; arrow.Y1 = c; arrow.X2 = c; arrow.Y2 = c;
            arrow.Stroke = Util.Brush(Cfg.TextColor);
            canvas.Children.Add(arrow);

            WShapes.Ellipse dot = new WShapes.Ellipse { Width = 8, Height = 8, Fill = Util.Brush(Cfg.TextColor) };
            Canvas.SetLeft(dot, c - 4); Canvas.SetTop(dot, c - 4);
            canvas.Children.Add(dot);

            label.Text = AppCore.Audio.Layout;
            label.FontSize = 10;
            label.Foreground = Util.Brush("#C0FFFFFF");
            Canvas.SetLeft(label, 2); Canvas.SetTop(label, size - 14);
            canvas.Children.Add(label);
        }

        static string ColorFor(double v)
        {
            return v < 0.5 ? "#6EE7A1" : v < 0.8 ? "#FFD166" : "#FF6B6B";
        }

        void Frame()
        {
            AudioDirectionService a = AppCore.Audio;
            float[] lv = a.Levels;
            double[] ang = a.Angles;
            if (lv.Length != built || ang.Length != built) { Rebuild(); return; }
            if (label.Text != a.Layout) label.Text = a.Layout;

            double sens = Cfg.RadarSensitivity / 50.0;
            double peak = Math.Max(a.Peak, 0.03);
            for (int i = 0; i < wedges.Count; i++)
            {
                WShapes.Path w = wedges[i];
                if (w == null) continue;
                double v = lv[i] < 0.002 ? 0 : Math.Min(1.0, Math.Pow(Math.Min(1.0, lv[i] / peak) * sens, 0.8));
                w.Opacity = 0.12 + 0.88 * v;
                w.Fill = Util.Brush(ColorFor(v));
            }

            // стрілка загального напрямку (довжина = гучність, зосередженість = напрямленість)
            double dx = a.DirX * sens, dy = a.DirY * sens;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 0.10)
            {
                double c = size / 2;
                double k = Math.Min(1.0, len) / len * size * 0.44;
                arrow.X2 = c + dx * k;
                arrow.Y2 = c - dy * k;
                arrow.Visibility = Visibility.Visible;
            }
            else arrow.Visibility = Visibility.Collapsed;

            // сплески
            SoundBlip[] bl = Cfg.RadarBlips ? a.SnapshotBlips() : new SoundBlip[0];
            double now = AudioDirectionService.NowS;
            double cc = size / 2, rr = size * 0.43;
            for (int i = 0; i < blipDots.Count; i++)
            {
                WShapes.Ellipse d = blipDots[i];
                if (i >= bl.Length) { d.Visibility = Visibility.Collapsed; continue; }
                SoundBlip b = bl[bl.Length - 1 - i];
                double age = now - b.T;
                double t = b.Angle * Math.PI / 180.0;
                double sz = 6 + 12 * b.Strength;
                d.Width = sz; d.Height = sz;
                Canvas.SetLeft(d, cc + rr * Math.Sin(t) - sz / 2);
                Canvas.SetTop(d, cc - rr * Math.Cos(t) - sz / 2);
                d.Opacity = Math.Max(0, 1.0 - age / 1.3);
                d.Fill = Util.Brush(ColorFor(b.Strength));
                d.Visibility = Visibility.Visible;
            }
        }
    }
}
