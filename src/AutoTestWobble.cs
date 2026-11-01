using System;
using System.Collections.Generic;
using System.Globalization;

namespace RealTracks
{
    /// <summary>
    /// Steering smoothness (WP B3, Kyle: "cars doing lots of wobbling coming into some turns ... they should be as smooth as possible"). Unity-free.
    /// Per car, per simulation step, the heading is fed in; a zig-zag filter on the unwrapped heading counts a reversal each time the car turns back the other way
    /// after swinging at least ThresholdDeg (peak to peak). Reversals with a swing below SmallDeg are the wobble (a real S-bend swings tens of degrees).
    /// </summary>
    public class AtWobbleTracker
    {
        public const double ThresholdDeg = 2.0, SmallDeg = 10.0;
        private bool has;
        private double last, ext, peak;
        private int dir;
        public int Reversals, Small;
        public double Meters;

        /// <summary>Feeds one step. Returns true if a reversal was counted (swing in degrees in `swing`).</summary>
        public bool Step(double headingDeg, double stepMeters, out double swing)
        {
            swing = 0;
            Meters += stepMeters;
            if (!has) { has = true; last = headingDeg; ext = headingDeg; peak = headingDeg; dir = 0; return false; }
            double d = headingDeg - last;
            while (d > 180) d -= 360;
            while (d < -180) d += 360;
            last += d;
            double h = last;
            if (dir == 0)
            {
                if (Math.Abs(h - ext) >= ThresholdDeg) { dir = h > ext ? 1 : -1; ext = h; }
                return false;
            }
            if ((dir > 0 && h > ext) || (dir < 0 && h < ext)) { ext = h; return false; }
            if (Math.Abs(ext - h) >= ThresholdDeg)
            {
                swing = Math.Abs(ext - peak);
                peak = ext;
                dir = -dir;
                ext = h;
                Reversals++;
                if (swing < SmallDeg) Small++;
                return true;
            }
            return false;
        }

        public void Reset() { has = false; dir = 0; }
    }

    public class AtWobbleBucket { public int Gate, Reversals, Small; }

    /// <summary>Per item: totals and per gate bucket (BucketGates gates each), top ranges.</summary>
    public class AtWobble
    {
        public const int BucketGates = 5;
        public double Meters;
        public int Reversals, Small, Cars;
        private readonly Dictionary<int, AtWobbleBucket> buckets = new Dictionary<int, AtWobbleBucket>();

        // laps >= 2 (the race-start melee of lap 1 is legitimate avoidance in both venues) and steps, for the per-step comparison
        public double LateMeters;
        public int Steps, LateSteps, LateSmall, EdgeSmall;

        /// <summary>One counted reversal with context: the car's lap (1-based, 0 unknown) and whether it was near the steering band edge.</summary>
        public void Add(int gate, double swing, double meters, int lap, bool nearBandEdge)
        {
            Add(gate, swing, meters);
            if (swing < AtWobbleTracker.SmallDeg)
            {
                if (lap >= 2) LateSmall++;
                if (nearBandEdge) EdgeSmall++;
            }
        }

        /// <summary>One sampled step of `m` metres on lap `lap`.</summary>
        public void AddStep(double m, int lap)
        {
            Meters += m; Steps++;
            if (lap >= 2) { LateMeters += m; LateSteps++; }
        }

        public double LateSmallPerKm { get { return LateMeters > 0 ? LateSmall / (LateMeters / 1000.0) : 0; } }
        public double LateStepsPerKm { get { return LateMeters > 0 ? LateSteps / (LateMeters / 1000.0) : 0; } }

        public void Add(int gate, double swing, double meters)
        {
            Reversals++;
            if (swing < AtWobbleTracker.SmallDeg) Small++;
            int b = gate < 0 ? 0 : gate / BucketGates;
            AtWobbleBucket x;
            if (!buckets.TryGetValue(b, out x)) { x = new AtWobbleBucket(); x.Gate = b * BucketGates; buckets[b] = x; }
            x.Reversals++;
            if (swing < AtWobbleTracker.SmallDeg) x.Small++;
        }

        public void AddMeters(double m) { Meters += m; }

        public double PerKm { get { return Meters > 0 ? Reversals / (Meters / 1000.0) : 0; } }
        public double SmallPerKm { get { return Meters > 0 ? Small / (Meters / 1000.0) : 0; } }

        /// <summary>The n buckets with the most small-swing reversals (ties: most reversals).</summary>
        public List<AtWobbleBucket> Top(int n)
        {
            List<AtWobbleBucket> l = new List<AtWobbleBucket>(buckets.Values);
            l.Sort(delegate (AtWobbleBucket a, AtWobbleBucket b)
            {
                int c = b.Small.CompareTo(a.Small);
                return c != 0 ? c : b.Reversals.CompareTo(a.Reversals);
            });
            if (l.Count > n) l.RemoveRange(n, l.Count - n);
            return l;
        }

        public string Describe()
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            string s = Reversals + " heading reversals (swing >= " + AtWobbleTracker.ThresholdDeg.ToString("0", inv) + " deg) over " + (Meters / 1000.0).ToString("0.0", inv) + " km driven: "
                + PerKm.ToString("0.00", inv) + "/km, of which wobble (swing < " + AtWobbleTracker.SmallDeg.ToString("0", inv) + " deg) " + Small + " = " + SmallPerKm.ToString("0.00", inv) + "/km";
            List<AtWobbleBucket> top = Top(5);
            if (top.Count > 0)
            {
                List<string> t = new List<string>();
                foreach (AtWobbleBucket b in top) t.Add("gates " + b.Gate + "-" + (b.Gate + BucketGates - 1) + ": " + b.Small + "/" + b.Reversals);
                s += "; worst ranges (wobble/all): " + string.Join(", ", t.ToArray());
            }
            return s;
        }
    }

    public static class AtWobbleCheck
    {
        /// <summary>Set by the mod (SteeringPitch.Summary) so the check's detail carries the fix's own counters; null in the unit tests.</summary>
        public static Func<AtItemData, string> ExtraDetail;

        /// <summary>Informational: the pack's wobble rate no more than 1.2 x the stock item's + 0.5/km. Never gates the run.</summary>
        public static AtCheck Evaluate(AtItemData d, AtItemData stock)
        {
            AtWobble w = d.Wobble;
            if (w.Meters < 1000) return new AtCheck("steeringSmoothness", true, "too little driving sampled (" + (w.Meters / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " km)", 0) { Informational = true };
            string detail = w.Describe();
            bool ok = true;
            if (d.IsPack && stock != null && stock.Wobble.Meters >= 1000)
            {
                double lim = stock.Wobble.SmallPerKm * 1.2 + 0.5;
                ok = w.SmallPerKm <= lim;
                detail += " | stock " + stock.Location + " " + stock.Layout + ": wobble " + stock.Wobble.SmallPerKm.ToString("0.00", CultureInfo.InvariantCulture) + "/km (limit " + lim.ToString("0.00", CultureInfo.InvariantCulture) + ")";
            }
            else if (d.IsPack) detail += " | no stock item to compare against";
            else detail += " | stock item (the baseline)";
            if (d.WobbleVisual.Meters >= 1000) detail += " | visual (3D car yaw): " + d.WobbleVisual.Describe();
            if (d.WobbleVisual.Meters >= 1000) detail += " | visual laps>=2: " + d.WobbleVisual.LateSmallPerKm.ToString("0.00", CultureInfo.InvariantCulture) + "/km, " + d.WobbleVisual.LateStepsPerKm.ToString("0", CultureInfo.InvariantCulture)
                + " steps/km; sim wobble near band edge " + d.Wobble.EdgeSmall + "/" + d.Wobble.Small + ", on lap>=2 " + d.Wobble.LateSmall + "/" + d.Wobble.Small;
            // Pass criterion (what Kyle sees): the visual yaw on laps >= 2, per km, with the limit scaled by how many sim steps per km the pack needs vs the stock item
            // (the sim steers per step, so a slower venue gets more steps per km; never tighter than the plain limit).
            if (d.IsPack && stock != null && d.WobbleVisual.LateMeters >= 1000 && stock.WobbleVisual.LateMeters >= 1000)
            {
                double f = stock.WobbleVisual.LateStepsPerKm > 0 ? Math.Max(1.0, d.WobbleVisual.LateStepsPerKm / stock.WobbleVisual.LateStepsPerKm) : 1.0;
                double vlim = (stock.WobbleVisual.LateSmallPerKm * 1.2 + 0.5) * f;
                bool vok = d.WobbleVisual.LateSmallPerKm <= vlim;
                detail += " | visual criterion: " + d.WobbleVisual.LateSmallPerKm.ToString("0.00", CultureInfo.InvariantCulture) + "/km vs limit " + vlim.ToString("0.00", CultureInfo.InvariantCulture)
                    + " (stock " + stock.WobbleVisual.LateSmallPerKm.ToString("0.00", CultureInfo.InvariantCulture) + " x1.2 + 0.5, x" + f.ToString("0.00", CultureInfo.InvariantCulture) + " step density) -> " + (vok ? "ok" : "FAIL");
                ok = vok;
            }
            if (ExtraDetail != null) { try { string x = ExtraDetail(d); if (!string.IsNullOrEmpty(x)) detail += " | " + x; } catch { } }
            AtCheck c = new AtCheck("steeringSmoothness", ok, detail, Math.Round(w.SmallPerKm, 3));
            c.Informational = true;
            return c;
        }
    }
}
