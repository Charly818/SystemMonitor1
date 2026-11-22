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
    /// Радар звуку: показує, з якого боку чути звук. 5.1/7.1 дають спереду/ззаду/збоку, стерео лише ліво/право.
    /// </summary>
    internal sealed class RadarWindow : GlassWindow
    {
        protected override string Key { get { return "radar"; } }

        readonly Canvas canvas = new Canvas();
        readonly DispatcherTimer timer;
        readonly List<WShapes.Path> wedges = new List<WShapes.Path>();
        readonly WShapes.Line arrow = new WShapes.Line { StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Triangle, Visibility = Visibility.Collapsed };
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
            double[] ang = AppCore.Audio.Angles;
            built = ang.Length;
            double c = size / 2;
            WShapes.Ellipse ring = new WShapes.Ellipse { Width = size, Height = size, Stroke = Util.Brush("#40FFFFFF"), StrokeThickness = 1 };
            canvas.Children.Add(ring);
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

            arrow.X1 = c; arrow.Y1 = c; arrow.X2 = c; arrow.Y2 = c;
            arrow.Stroke = Util.Brush(Cfg.TextColor);
            canvas.Children.Add(arrow);

            WShapes.Ellipse dot = new WShapes.Ellipse { Width = 8, Height = 8, Fill = Util.Brush(Cfg.TextColor) };
            Canvas.SetLeft(dot, c - 4); Canvas.SetTop(dot, c - 4);
            canvas.Children.Add(dot);
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

            double sens = Cfg.RadarSensitivity / 50.0;
            double peak = Math.Max(a.Peak, 0.03);
            double vx = 0, vy = 0, total = 0;
            for (int i = 0; i < wedges.Count; i++)
            {
                WShapes.Path w = wedges[i];
                if (w == null) continue;
                double v = lv[i] < 0.003 ? 0 : Math.Min(1.0, Math.Pow(Math.Min(1.0, lv[i] / peak) * sens, 0.8));
                w.Opacity = 0.12 + 0.88 * v;
                w.Fill = Util.Brush(ColorFor(v));
                double t = ang[i] * Math.PI / 180.0;
                vx += v * Math.Sin(t);
                vy += v * Math.Cos(t);
                total += v;
            }

            double len = Math.Sqrt(vx * vx + vy * vy);
            if (total > 0.15 && len > 0.12)
            {
                double c = size / 2;
                double k = (size * 0.44) * Math.Min(1.0, len / Math.Max(total, 0.001));
                arrow.X2 = c + vx / len * k;
                arrow.Y2 = c - vy / len * k;
                arrow.Visibility = Visibility.Visible;
            }
            else arrow.Visibility = Visibility.Collapsed;
        }
    }
}
