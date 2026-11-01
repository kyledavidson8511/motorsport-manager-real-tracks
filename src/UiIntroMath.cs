using System;
using System.Collections.Generic;

namespace RealTracks
{
    // =====================================================================================================
    // Unity-free math and raster code of the race-intro overlay (UiIntro.cs does the Unity side). It is compiled into the net8
    // test harness (tools/intro-render-test) as well, so the offline test images come from exactly this code.
    // =====================================================================================================

    /// <summary>View onto the world map as a rectangle in top-down uv (v = 0 at the top), plus the venue's position in it.</summary>
    public struct UiMapView
    {
        public double U0, V0, W, H;          // visible rectangle: left, top, width, height (uv)
        public double DotX, DotY;            // venue on screen, 0..1 (y = 0 at the top)
        public double Zoom;                  // 1 = whole map visible
    }

    public static class UiIntroMath
    {
        /// <summary>Equirectangular projection of the world-map PNG made by tools/trackbuild/worldmap.py. v = 0 at the top.</summary>
        public static void LonLatToUv(double lon, double lat, out double u, out double v)
        {
            u = (lon + 180.0) / 360.0;
            v = (90.0 - lat) / 180.0;
        }

        public static double Clamp01(double x) { return x < 0 ? 0 : (x > 1 ? 1 : x); }
        public static double Smooth(double x) { x = Clamp01(x); return x * x * (3 - 2 * x); }
        public static double EaseOutCubic(double x) { x = Clamp01(x); double a = 1 - x; return 1 - a * a * a; }

        // start and end view heights (fraction of the map height) and the start offset from the venue (uv)
        public const double StartViewH = 0.60, EndViewH = 0.16, StartOffU = 0.09, StartOffV = 0.03;

        /// <summary>
        /// The map camera at time t seconds of a map stage that lasts mapSeconds: eases from a continent-sized view with the venue
        /// off-centre to a tight view with the venue just right of the middle. It keeps drifting slowly after the zoom is done.
        /// screenAspect = screen width / height (the map texture is 2:1).
        /// </summary>
        public static UiMapView MapAt(double t, double mapSeconds, double venueU, double venueV, double screenAspect)
        {
            double k = EaseOutCubic(t / Math.Max(0.1, mapSeconds * 0.9));
            double vh = StartViewH + (EndViewH - StartViewH) * k;
            double maxH = 0.98 * 2.0 / Math.Max(0.5, screenAspect);       // the view must not be wider than the map
            if (vh > maxH) vh = maxH;
            double vw = vh * screenAspect / 2.0;
            double cu = venueU + StartOffU * (1 - k) - 0.012 * k + 0.004 * Math.Max(0, t - mapSeconds * 0.9) * 0.2;
            double cv = venueV + StartOffV * (1 - k);
            UiMapView m = new UiMapView();
            m.W = vw; m.H = vh;
            m.U0 = Math.Max(0, Math.Min(1 - vw, cu - vw / 2));
            m.V0 = Math.Max(0, Math.Min(1 - vh, cv - vh / 2));
            m.DotX = (venueU - m.U0) / vw;
            m.DotY = (venueV - m.V0) / vh;
            m.Zoom = 1.0 / vh;
            return m;
        }

        /// <summary>Pulse of the yellow ring: scale 1..1.35 with a 1.3 s period.</summary>
        public static double RingScale(double t) { return 1.0 + 0.35 * (0.5 - 0.5 * Math.Cos(t * 2 * Math.PI / 1.3)); }

        /// <summary>The ring appears once the zoom is a third done and fades in over 0.6 s.</summary>
        public static double RingAlpha(double t, double mapSeconds) { return Smooth((t - mapSeconds * 0.3) / 0.6); }

        /// <summary>Weight of the map picture over the outline stage: 1 until mapSeconds, then fading out over fadeSeconds.</summary>
        public static double MapAlpha(double t, double mapSeconds, double fadeSeconds) { return 1 - Smooth((t - mapSeconds) / Math.Max(0.05, fadeSeconds)); }

        /// <summary>Progress 0..1 of the outline reveal at time t, after the map stage (starts a little after the crossfade has begun).</summary>
        public static double RevealAt(double t, double mapSeconds, double revealSeconds)
        {
            return Clamp01((t - mapSeconds - 0.2) / Math.Max(0.1, revealSeconds));
        }

        // ------------------------------------------------------------------------------------------------ outline raster

        /// <summary>
        /// Fits a track (pack-local x east, z north) into a size x size box with a margin, north up, and
        /// cuts it down to about maxPoints points. The loop is closed. Returns pixel coordinates with y UP (texture rows, row 0 at the
        /// bottom as Unity textures are).
        /// </summary>
        public static void FitOutline(IList<double> xs, IList<double> zs, int size, double margin, int maxPoints, out double[] px, out double[] py)
        {
            int n = xs.Count;
            int stride = Math.Max(1, (int)Math.Ceiling(n / (double)maxPoints));
            List<double> x = new List<double>(), z = new List<double>();
            for (int i = 0; i < n; i += stride) { x.Add(xs[i]); z.Add(zs[i]); }
            x.Add(x[0]); z.Add(z[0]);
            double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
            for (int i = 0; i < x.Count; i++)
            {
                x0 = Math.Min(x0, x[i]); x1 = Math.Max(x1, x[i]); z0 = Math.Min(z0, z[i]); z1 = Math.Max(z1, z[i]);
            }
            double span = Math.Max(Math.Max(x1 - x0, z1 - z0), 1e-6);
            double scale = (size - 2 * margin) / span;
            double offX = (size - (x1 - x0) * scale) / 2.0, offY = (size - (z1 - z0) * scale) / 2.0;
            px = new double[x.Count]; py = new double[x.Count];
            for (int i = 0; i < x.Count; i++)
            {
                px[i] = offX + (x[i] - x0) * scale;
                py[i] = offY + (z[i] - z0) * scale;                // north (larger z) is up = larger row
            }
        }

        public static void Bounds(double[] px, double[] py, out double x0, out double y0, out double x1, out double y1)
        {
            x0 = double.MaxValue; y0 = double.MaxValue; x1 = double.MinValue; y1 = double.MinValue;
            for (int i = 0; i < px.Length; i++) { x0 = Math.Min(x0, px[i]); x1 = Math.Max(x1, px[i]); y0 = Math.Min(y0, py[i]); y1 = Math.Max(y1, py[i]); }
        }
    }

    /// <summary>
    /// Incremental glow-line painter: RGBA8, straight (non-premultiplied) alpha, transparent where nothing is drawn, so the faint flag
    /// shows through. Each call to RevealTo paints only the newly revealed part of the polyline.
    /// </summary>
    public class UiOutlineRaster
    {
        public readonly int Size;
        public readonly byte[] Rgba;
        private readonly double[] mX, mY, mCum;
        private double mDrawn;                       // arc length painted so far
        public double TotalLength { get { return mCum[mCum.Length - 1]; } }
        public double HeadX, HeadY;                  // the end of what is painted
        public int SegmentsDrawn;

        public double GlowRadius = 9.0, LineHalf = 3.6, CoreHalf = 1.5;

        /// <summary>A solid, even-odd fill of the closed circuit outline.  This is intentionally separate from the
        /// progressively painted line: the UI Mask consumes the alpha channel to clip the national flag to the circuit
        /// silhouette instead of the outline's rectangular bounds.</summary>
        public byte[] FilledMask()
        {
            byte[] rgba = new byte[Size * Size * 4];
            for (int y = 0; y < Size; y++)
            {
                double scanY = y + 0.5;
                List<double> hit = new List<double>();
                for (int i = 1; i < mX.Length; i++)
                {
                    double ay = mY[i - 1], by = mY[i];
                    // Half-open test means a vertex shared by two segments is counted once.
                    if (!((ay <= scanY && by > scanY) || (by <= scanY && ay > scanY))) continue;
                    double t = (scanY - ay) / (by - ay);
                    hit.Add(mX[i - 1] + (mX[i] - mX[i - 1]) * t);
                }
                hit.Sort();
                for (int i = 0; i + 1 < hit.Count; i += 2)
                {
                    int x0 = (int)Math.Ceiling(hit[i] - 0.5);
                    int x1 = (int)Math.Floor(hit[i + 1] - 0.5);
                    if (x0 < 0) x0 = 0;
                    if (x1 >= Size) x1 = Size - 1;
                    for (int x = x0; x <= x1; x++)
                    {
                        int o = (y * Size + x) * 4;
                        rgba[o] = rgba[o + 1] = rgba[o + 2] = rgba[o + 3] = 255;
                    }
                }
            }
            return rgba;
        }

        public UiOutlineRaster(int size, double[] px, double[] py)
        {
            Size = size; mX = px; mY = py;
            Rgba = new byte[size * size * 4];
            mCum = new double[px.Length];
            for (int i = 1; i < px.Length; i++) mCum[i] = mCum[i - 1] + Math.Sqrt((px[i] - px[i - 1]) * (px[i] - px[i - 1]) + (py[i] - py[i - 1]) * (py[i] - py[i - 1]));
            HeadX = px[0]; HeadY = py[0];
        }

        private void PointAt(double s, out double x, out double y)
        {
            if (s <= 0) { x = mX[0]; y = mY[0]; return; }
            if (s >= TotalLength) { x = mX[mX.Length - 1]; y = mY[mY.Length - 1]; return; }
            int i = Array.BinarySearch(mCum, s);
            if (i < 0) i = ~i;                       // first vertex beyond s
            if (i == 0) i = 1;
            double a = (s - mCum[i - 1]) / Math.Max(1e-9, mCum[i] - mCum[i - 1]);
            x = mX[i - 1] + (mX[i] - mX[i - 1]) * a;
            y = mY[i - 1] + (mY[i] - mY[i - 1]) * a;
        }

        /// <summary>Paints the outline up to fraction p (0..1) of its length. Returns true if anything new was painted.</summary>
        public bool RevealTo(double p)
        {
            double target = Math.Max(0, Math.Min(1, p)) * TotalLength;
            if (target <= mDrawn + 0.5 && !(target >= TotalLength && mDrawn < TotalLength)) return false;
            double from = mDrawn;
            // walk the original segments that overlap [from, target]
            for (int i = 1; i < mX.Length; i++)
            {
                double a = mCum[i - 1], b = mCum[i];
                if (b <= from || a >= target) continue;
                double sa = Math.Max(a, from), sb = Math.Min(b, target);
                double ax, ay, bx, by;
                PointAt(sa, out ax, out ay); PointAt(sb, out bx, out by);
                Segment(ax, ay, bx, by);
                SegmentsDrawn++;
            }
            mDrawn = target;
            PointAt(target, out HeadX, out HeadY);
            return true;
        }

        private void Segment(double ax, double ay, double bx, double by)
        {
            double r = GlowRadius * 1.6;
            int x0 = (int)Math.Floor(Math.Min(ax, bx) - r), x1 = (int)Math.Ceiling(Math.Max(ax, bx) + r);
            int y0 = (int)Math.Floor(Math.Min(ay, by) - r), y1 = (int)Math.Ceiling(Math.Max(ay, by) + r);
            if (x0 < 0) x0 = 0; if (y0 < 0) y0 = 0; if (x1 >= Size) x1 = Size - 1; if (y1 >= Size) y1 = Size - 1;
            double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double t = len2 < 1e-9 ? 0 : ((x + 0.5 - ax) * dx + (y + 0.5 - ay) * dy) / len2;
                    t = t < 0 ? 0 : (t > 1 ? 1 : t);
                    double ex = x + 0.5 - (ax + dx * t), ey = y + 0.5 - (ay + dy * t);
                    double d = Math.Sqrt(ex * ex + ey * ey);
                    if (d > r) continue;
                    // layers composited over each other: glow (deep red), line (red), core (hot pink-white)
                    double aG = 0.55 * Math.Exp(-(d / GlowRadius) * (d / GlowRadius) * 1.6);
                    double aL = UiIntroMath.Clamp01((LineHalf - d) / 1.4);
                    double aC = UiIntroMath.Clamp01((CoreHalf - d) / 1.0);
                    double al = aG, cr = 200, cg = 12, cb = 24;
                    Over(ref al, ref cr, ref cg, ref cb, aL, 238, 38, 44);
                    Over(ref al, ref cr, ref cg, ref cb, aC, 255, 214, 214);
                    int o = (y * Size + x) * 4;
                    double dstA = Rgba[o + 3] / 255.0;
                    if (al > dstA + 0.004 || (al > dstA - 0.004 && cg > Rgba[o + 1]))   // lighten: overlapping segments never pile up their glow; at equal alpha the hotter colour wins
                    {
                        Rgba[o] = (byte)Math.Round(cr); Rgba[o + 1] = (byte)Math.Round(cg); Rgba[o + 2] = (byte)Math.Round(cb);
                        Rgba[o + 3] = (byte)Math.Round(al * 255);
                    }
                }
        }

        private static void Over(ref double a, ref double r, ref double g, ref double b, double sa, double sr, double sg, double sb)
        {
            if (sa <= 0) return;
            double oa = sa + a * (1 - sa);
            r = (sr * sa + r * a * (1 - sa)) / oa;
            g = (sg * sa + g * a * (1 - sa)) / oa;
            b = (sb * sa + b * a * (1 - sa)) / oa;
            a = oa;
        }
    }

    /// <summary>Small procedural sprites for the overlay (RGBA8, straight alpha).</summary>
    public static class UiIntroSprites
    {
        /// <summary>
        /// The map marker: a yellow ring (radius 0.26 of the half-size, 2.2 px thick at 64 px) in a soft warm halo with a faint
        /// four-point flare, like the stock movie's pulsing dot.
        /// </summary>
        public static byte[] Marker(int size)
        {
            byte[] px = new byte[size * size * 4];
            double c = (size - 1) / 2.0, half = size / 2.0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    double dx = (x - c) / half, dy = (y - c) / half, d = Math.Sqrt(dx * dx + dy * dy);
                    double ring = Math.Exp(-Math.Pow((d - 0.26) / 0.045, 2));
                    double halo = 0.38 * Math.Exp(-Math.Pow(d / 0.42, 2));
                    double flare = 0.30 * Math.Exp(-Math.Pow(Math.Min(Math.Abs(dx), Math.Abs(dy)) / 0.03, 2)) * Math.Exp(-d * 3.2);
                    double a = Math.Min(1, ring + halo + flare);
                    double mix = Math.Min(1, ring * 1.5);                    // ring pure yellow, halo and flare warm orange
                    int o = (y * size + x) * 4;
                    px[o] = (byte)(255 * (mix + (1 - mix) * 1.0));
                    px[o + 1] = (byte)(235 * mix + 170 * (1 - mix));
                    px[o + 2] = (byte)(10 * mix + 60 * (1 - mix));
                    px[o + 3] = (byte)(a * 255);
                }
            return px;
        }

        /// <summary>A soft white dot used for the glowing head of the red line (tinted by the Image).</summary>
        public static byte[] Spark(int size)
        {
            byte[] px = new byte[size * size * 4];
            double c = (size - 1) / 2.0, half = size / 2.0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    double d = Math.Sqrt(Math.Pow((x - c) / half, 2) + Math.Pow((y - c) / half, 2));
                    int o = (y * size + x) * 4;
                    px[o] = 255; px[o + 1] = 235; px[o + 2] = 235;
                    px[o + 3] = (byte)(255 * Math.Min(1, Math.Exp(-d * d * 7) * 1.1));
                }
            return px;
        }
    }
}
