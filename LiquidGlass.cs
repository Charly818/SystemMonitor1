using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SystemMonitor
{
    internal sealed class LgParams
    {
        public int Blur, Refraction, Dispersion, Saturation, Brightness, Highlight, Radius, Strength;
        public byte TintR, TintG, TintB;
        public double TintAlpha;

        public long Key()
        {
            long h = 17;
            h = h * 31 + Blur; h = h * 31 + Refraction; h = h * 31 + Dispersion; h = h * 31 + Saturation;
            h = h * 31 + Brightness; h = h * 31 + Highlight; h = h * 31 + Radius; h = h * 31 + Strength;
            h = h * 31 + TintR; h = h * 31 + TintG; h = h * 31 + TintB; h = h * 31 + (long)(TintAlpha * 1000);
            return h;
        }
    }

    /// <summary>
    /// «Рідке скло» в реальному часі: знімає екран позаду вікна (саме вікно від знімків приховане),
    /// розмиває, додає заломлення біля країв із легкою кольоровою дисперсією, насиченість і підфарбування.
    /// Працює вдвічі меншою роздільністю: розмиття однаково її не помічає, а процесор майже не навантажується.
    /// </summary>
    internal sealed class LiquidGlass : IDisposable
    {
        IntPtr screenDc, memDc, dib, oldBmp, bits;
        int capW, capH, sw, sh, bmpW, bmpH;
        byte[] cap, s, s0, t, o;
        long lastHash = long.MinValue;
        public WriteableBitmap Bitmap;

        bool EnsureCapture(int cw, int ch)
        {
            if (screenDc == IntPtr.Zero)
            {
                screenDc = Native.GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero) return false;
                memDc = Native.CreateCompatibleDC(screenDc);
                if (memDc == IntPtr.Zero) return false;
            }
            if (dib == IntPtr.Zero || cw != capW || ch != capH)
            {
                if (dib != IntPtr.Zero)
                {
                    Native.SelectObject(memDc, oldBmp);
                    Native.DeleteObject(dib);
                    dib = IntPtr.Zero;
                }
                dib = Native.CreateDib(memDc, cw, ch, out bits);
                if (dib == IntPtr.Zero) return false;
                oldBmp = Native.SelectObject(memDc, dib);
                capW = cw; capH = ch;
                cap = new byte[cw * ch * 4];
                lastHash = long.MinValue;
            }
            return true;
        }

        static void BoxH(byte[] src, byte[] dst, int w, int h, int r)
        {
            int win = 2 * r + 1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w * 4;
                for (int c = 0; c < 3; c++)
                {
                    int sum = 0;
                    for (int i = -r; i <= r; i++) sum += src[row + Math.Min(Math.Max(i, 0), w - 1) * 4 + c];
                    for (int x = 0; x < w; x++)
                    {
                        dst[row + x * 4 + c] = (byte)(sum / win);
                        int add = Math.Min(x + r + 1, w - 1), sub = Math.Max(x - r, 0);
                        sum += src[row + add * 4 + c] - src[row + sub * 4 + c];
                    }
                }
            }
        }

        static void BoxV(byte[] src, byte[] dst, int w, int h, int r)
        {
            int win = 2 * r + 1;
            int stride = w * 4;
            for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    int col = x * 4 + c;
                    int sum = 0;
                    for (int i = -r; i <= r; i++) sum += src[Math.Min(Math.Max(i, 0), h - 1) * stride + col];
                    for (int y = 0; y < h; y++)
                    {
                        dst[y * stride + col] = (byte)(sum / win);
                        int add = Math.Min(y + r + 1, h - 1), sub = Math.Max(y - r, 0);
                        sum += src[add * stride + col] - src[sub * stride + col];
                    }
                }
            }
        }

        double Sample(double fx, double fy, int ch)
        {
            fx -= 0.5; fy -= 0.5;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            double ax = fx - x0, ay = fy - y0;
            int x1 = Math.Min(Math.Max(x0 + 1, 0), sw - 1), y1 = Math.Min(Math.Max(y0 + 1, 0), sh - 1);
            x0 = Math.Min(Math.Max(x0, 0), sw - 1); y0 = Math.Min(Math.Max(y0, 0), sh - 1);
            double v00 = s[(y0 * sw + x0) * 4 + ch], v10 = s[(y0 * sw + x1) * 4 + ch];
            double v01 = s[(y1 * sw + x0) * 4 + ch], v11 = s[(y1 * sw + x1) * 4 + ch];
            return (v00 * (1 - ax) + v10 * ax) * (1 - ay) + (v01 * (1 - ax) + v11 * ax) * ay;
        }

        static byte Clamp(double v) { return (byte)(v < 0 ? 0 : v > 255 ? 255 : v); }

        /// <summary>Оновлює Bitmap для вікна, що стоїть у точці (left, top) розміром W x H фізичних пікселів.</summary>
        public void Render(int left, int top, int W, int H, double dpi, LgParams p)
        {
            int w2 = (W + 1) / 2, h2 = (H + 1) / 2;
            if (w2 < 4 || h2 < 4) return;

            double bevel = Math.Max(6.0, Math.Min(w2, h2) * 0.14);
            double amtMax = p.Refraction / 100.0 * bevel * 1.1;
            int rb = Math.Max(0, (int)Math.Round(p.Blur * dpi / 4.0));
            int m2 = (int)Math.Ceiling(rb * 2 + amtMax + 3);
            sw = w2 + 2 * m2;
            sh = h2 + 2 * m2;
            int cw = sw * 2, ch = sh * 2;

            if (!EnsureCapture(cw, ch)) return;
            if (!Native.BitBlt(memDc, 0, 0, cw, ch, screenDc, left - 2 * m2, top - 2 * m2, Native.SRCCOPY | Native.CAPTUREBLT)) return;
            Marshal.Copy(bits, cap, 0, cw * ch * 4);

            // зменшення вдвічі (усереднення 2x2)
            int sLen = sw * sh * 4;
            if (s == null || s.Length != sLen) { s = new byte[sLen]; t = new byte[sLen]; }
            for (int y = 0; y < sh; y++)
            {
                int r0 = 2 * y * cw * 4, r1 = r0 + cw * 4, d = y * sw * 4;
                for (int x = 0; x < sw; x++)
                {
                    int a0 = r0 + x * 8, a1 = r1 + x * 8;
                    s[d] = (byte)((cap[a0] + cap[a0 + 4] + cap[a1] + cap[a1 + 4]) >> 2);
                    s[d + 1] = (byte)((cap[a0 + 1] + cap[a0 + 5] + cap[a1 + 1] + cap[a1 + 5]) >> 2);
                    s[d + 2] = (byte)((cap[a0 + 2] + cap[a0 + 6] + cap[a1 + 2] + cap[a1 + 6]) >> 2);
                    d += 4;
                }
            }

            // якщо фон і параметри не змінились, перемальовувати не треба
            long hash = p.Key() * 1000003L + w2 * 7919L + h2;
            for (int i = 0; i < sLen; i += 13) hash = (hash ^ s[i]) * 1099511628211L;
            if (hash == lastHash && Bitmap != null) return;
            lastHash = hash;

            double k = Math.Max(0.0, Math.Min(1.0, p.Strength / 100.0));   // 1 = повне скло, 0 = майже прозоре
            if (k < 1.0)
            {
                if (s0 == null || s0.Length != sLen) s0 = new byte[sLen];
                Buffer.BlockCopy(s, 0, s0, 0, sLen);
            }
            if (rb > 0) { BoxH(s, t, sw, sh, rb); BoxV(t, s, sw, sh, rb); BoxH(s, t, sw, sh, rb); BoxV(t, s, sw, sh, rb); }
            if (k < 1.0 && rb > 0)
            {
                for (int i = 0; i < sLen; i++)
                {
                    if ((i & 3) == 3) continue;
                    s[i] = (byte)(s0[i] + (s[i] - s0[i]) * k);
                }
            }

            int oLen = w2 * h2 * 4;
            if (o == null || o.Length != oLen) o = new byte[oLen];

            double r2 = Math.Min(p.Radius * dpi / 2.0, Math.Min(w2, h2) / 2.0);
            double cx = w2 / 2.0, cy = h2 / 2.0;
            double hx = cx - r2, hy = cy - r2;
            double sat = 1 + (p.Saturation / 100.0 - 1) * k, bright = 1 + (p.Brightness / 100.0 - 1) * k, ta = p.TintAlpha;
            double disp = p.Dispersion / 100.0;
            double glowK = p.Highlight / 100.0 * 0.35;
            int oi = 0;

            for (int y = 0; y < h2; y++)
            {
                double py = y + 0.5;
                for (int x = 0; x < w2; x++)
                {
                    double px = x + 0.5;
                    double dx = px - cx, dy = py - cy;
                    double qx = Math.Abs(dx) - hx, qy = Math.Abs(dy) - hy;
                    double ox = qx > 0 ? qx : 0, oy = qy > 0 ? qy : 0;
                    double sdf = Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r2;
                    double depth = -sdf;
                    double tt = depth < bevel ? 1.0 - Math.Max(depth, 0) / bevel : 0.0;

                    double br, bg, bb, glow = 0;
                    if (tt <= 0)
                    {
                        int si = ((y + m2) * sw + (x + m2)) * 4;
                        bb = s[si]; bg = s[si + 1]; br = s[si + 2];
                    }
                    else
                    {
                        double nx, ny;   // зовнішня нормаль краю
                        if (qx > 0 && qy > 0)
                        {
                            double len = Math.Sqrt(qx * qx + qy * qy);
                            nx = (dx < 0 ? -1 : 1) * qx / len;
                            ny = (dy < 0 ? -1 : 1) * qy / len;
                        }
                        else if (qx > qy) { nx = dx < 0 ? -1 : 1; ny = 0; }
                        else { nx = 0; ny = dy < 0 ? -1 : 1; }

                        double amt = amtMax * tt * tt;   // біля краю беремо картинку з-за краю: ефект товстої лінзи
                        double aR = amt * (1 + disp), aB = amt * (1 - disp);
                        double bx = px + m2, by = py + m2;
                        br = Sample(bx + nx * aR, by + ny * aR, 2);
                        bg = Sample(bx + nx * amt, by + ny * amt, 1);
                        bb = Sample(bx + nx * aB, by + ny * aB, 0);

                        double light = Math.Max(0, -(nx + ny) * 0.7071);   // світло зверху ліворуч
                        glow = glowK * tt * tt * (0.35 + 0.65 * light);
                    }

                    double Y = 0.299 * br + 0.587 * bg + 0.114 * bb;
                    br = (Y + (br - Y) * sat) * bright;
                    bg = (Y + (bg - Y) * sat) * bright;
                    bb = (Y + (bb - Y) * sat) * bright;
                    br = br * (1 - ta) + p.TintR * ta;
                    bg = bg * (1 - ta) + p.TintG * ta;
                    bb = bb * (1 - ta) + p.TintB * ta;
                    if (glow > 0) { br += (255 - br) * glow; bg += (255 - bg) * glow; bb += (255 - bb) * glow; }

                    o[oi] = Clamp(bb); o[oi + 1] = Clamp(bg); o[oi + 2] = Clamp(br); o[oi + 3] = 255;
                    oi += 4;
                }
            }

            if (Bitmap == null || bmpW != w2 || bmpH != h2)
            {
                Bitmap = new WriteableBitmap(w2, h2, 96, 96, PixelFormats.Bgr32, null);
                bmpW = w2; bmpH = h2;
            }
            Bitmap.WritePixels(new Int32Rect(0, 0, w2, h2), o, w2 * 4, 0);
        }

        public void Dispose()
        {
            try
            {
                if (dib != IntPtr.Zero) { Native.SelectObject(memDc, oldBmp); Native.DeleteObject(dib); dib = IntPtr.Zero; }
                if (memDc != IntPtr.Zero) { Native.DeleteDC(memDc); memDc = IntPtr.Zero; }
                if (screenDc != IntPtr.Zero) { Native.ReleaseDC(IntPtr.Zero, screenDc); screenDc = IntPtr.Zero; }
            }
            catch { }
        }
    }
}
