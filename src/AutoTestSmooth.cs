using System;
using System.Collections.Generic;

namespace RealTracks
{
    /// <summary>
    /// Smoothness of a race at normal (1x) speed, from real frame times (Unity-free; added after Kyle's M1 feedback:
    /// "the overall smoothness of the experience running a race is the correct ultimate value", 2026-10-28).
    /// Stutter = a frame longer than 50 ms (a visible jerk); hitch = longer than 100 ms (a freeze you notice).
    /// </summary>
    public class AtSmooth
    {
        public const double StutterSec = 0.050, HitchSec = 0.100;
        private readonly List<double> frames = new List<double>();
        public string Diag = "";   // screen size, vsync, target frame rate, focus

        public void Add(double seconds)
        {
            if (seconds > 0 && seconds < 5 && !double.IsNaN(seconds)) frames.Add(seconds);
        }

        public int Frames { get { return frames.Count; } }

        public double Seconds
        {
            get { double t = 0; for (int i = 0; i < frames.Count; i++) t += frames[i]; return t; }
        }

        public AtSmoothOut Result()
        {
            AtSmoothOut o = new AtSmoothOut();
            o.Frames = frames.Count;
            o.Seconds = Seconds;
            o.Diag = Diag;
            if (o.Frames == 0) return o;
            List<double> s = new List<double>(frames);
            s.Sort();
            o.P50Ms = 1000 * Pct(s, 0.50);
            o.P95Ms = 1000 * Pct(s, 0.95);
            o.P99Ms = 1000 * Pct(s, 0.99);
            o.MaxMs = 1000 * s[s.Count - 1];
            int st = 0, hi = 0;
            for (int i = 0; i < frames.Count; i++) { if (frames[i] > StutterSec) st++; if (frames[i] > HitchSec) hi++; }
            o.Stutters = st; o.Hitches = hi;
            double min = o.Seconds / 60.0;
            o.StuttersPerMin = min > 0 ? st / min : 0;
            o.HitchesPerMin = min > 0 ? hi / min : 0;
            o.MeanFps = o.Seconds > 0 ? o.Frames / o.Seconds : 0;
            return o;
        }

        private static double Pct(List<double> sorted, double p)
        {
            int k = (int)Math.Ceiling(p * sorted.Count) - 1;
            if (k < 0) k = 0;
            if (k >= sorted.Count) k = sorted.Count - 1;
            return sorted[k];
        }
    }

    public class AtSmoothOut
    {
        public int Frames, Stutters, Hitches;
        public double Seconds, P50Ms, P95Ms, P99Ms, MaxMs, StuttersPerMin, HitchesPerMin, MeanFps;
        public string Diag = "";

        public string Describe()
        {
            if (Frames == 0) return "no frames sampled";
            return Frames + " frames over " + AtJson.Num(Math.Round(Seconds, 1)) + " s at 1x: mean " + AtJson.Num(Math.Round(MeanFps, 1)) + " fps; frame time p50 "
                + AtJson.Num(Math.Round(P50Ms, 1)) + " ms, p95 " + AtJson.Num(Math.Round(P95Ms, 1)) + " ms, p99 " + AtJson.Num(Math.Round(P99Ms, 1)) + " ms, worst "
                + AtJson.Num(Math.Round(MaxMs, 0)) + " ms; stutters (>50 ms) " + Stutters + " (" + AtJson.Num(Math.Round(StuttersPerMin, 1)) + "/min), hitches (>100 ms) "
                + Hitches + " (" + AtJson.Num(Math.Round(HitchesPerMin, 1)) + "/min)" + (Diag.Length > 0 ? "; " + Diag : "");
        }
    }

    public static class AtSmoothCheck
    {
        /// <summary>
        /// Pack vs the stock item in the same run: p95 frame time within 20 % of stock (or under 40 ms), and hitches/min no more than
        /// 20 % above stock + 2/min (or at most 2/min). Stock items are the baseline (always pass, informational).
        /// </summary>
        public static AtCheck Evaluate(AtItemData d, AtItemData stock)
        {
            AtSmoothOut f = d.Smooth.Result();
            if (f.Frames < 30) return new AtCheck("smoothness", false, "too few 1x frames sampled (" + f.Frames + ")", 0);
            string detail = f.Describe();
            if (!d.IsPack) return new AtCheck("smoothness", true, detail + "; stock item (the baseline)", Math.Round(f.P95Ms, 1));
            if (stock == null || stock.Smooth.Frames < 30)
            {
                bool ok = f.P95Ms <= 66.7 && f.HitchesPerMin <= 6;
                return new AtCheck("smoothness", ok, detail + "; no stock item to compare against (absolute bar: p95 <= 66.7 ms, hitches <= 6/min)", Math.Round(f.P95Ms, 1));
            }
            AtSmoothOut b = stock.Smooth.Result();
            bool p95ok = f.P95Ms <= Math.Max(40.0, b.P95Ms * 1.2);
            bool hitchOk = f.HitchesPerMin <= Math.Max(2.0, b.HitchesPerMin * 1.2 + 2.0);
            detail += " | stock " + stock.Location + " " + stock.Layout + ": p95 " + AtJson.Num(Math.Round(b.P95Ms, 1)) + " ms, hitches "
                + AtJson.Num(Math.Round(b.HitchesPerMin, 1)) + "/min, mean " + AtJson.Num(Math.Round(b.MeanFps, 1)) + " fps"
                + (p95ok ? "" : " | p95 frame time more than 20 % worse than stock")
                + (hitchOk ? "" : " | more than 20 % (+2/min) more hitches than stock");
            return new AtCheck("smoothness", p95ok && hitchOk, detail, Math.Round(f.P95Ms, 1));
        }
    }
}
