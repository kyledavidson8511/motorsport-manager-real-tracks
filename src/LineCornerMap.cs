using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RealTracks
{
    /// <summary>One numbered turn of a pack's corner table (corners_KEY.csv).</summary>
    public class LineTurn
    {
        public int Turn;
        public double Kmh;
        public bool Flat;
        public double ApexX, ApexZ, S0, S1;
    }

    /// <summary>A corner the game found, as a zone along the lap: from its start gate to its middle gate, mirrored past the middle (the game only assigns gates up to the middle, PathData.cs:636-648).</summary>
    public class LineGameCorner
    {
        public int Id;
        public double ZoneStart, ZoneEnd;
    }

    public class LineDecision
    {
        public int Id;
        public List<int> Turns = new List<int>();
        public bool Remove;
        public string Type = "";    // Low / Medium / High
        public double Speed, Radius, Kmh;
        public int By;
        public string Why = "";
    }

    /// <summary>Unity-free logic of the real corner-speed override (mirror of tools/trackbuild/cornermap.py; unit-tested in tools/autotest-unit-tests).</summary>
    public static class LineCornerMap
    {
        public static readonly double[] LowBand = { 0.24, 0.40 }, MedBand = { 0.44, 0.60 }, HighBand = { 0.68, 0.76 };

        /// <summary>f = v / (0.9 x refTop) -> class, speed 0..1 inside the class band, and the game "radius" (diameter, PathData.cs:652-684) that class/speed would have had.</summary>
        public static void Classify(double kmh, double refTop, out string type, out double speed, out double radius)
        {
            double f = kmh / (0.9 * refTop);
            double[] b; double r0, r1;
            if (f >= 0.68) { type = "High"; b = HighBand; r0 = 250; r1 = 320; }
            else if (f >= 0.44) { type = "Medium"; b = MedBand; r0 = 140; r1 = 250; }
            else { type = "Low"; b = LowBand; r0 = 80; r1 = 140; }
            speed = Math.Min(1.0, Math.Max(0.0, (f - b[0]) / (b[1] - b[0])));
            radius = r0 + speed * (r1 - r0);
        }

        /// <summary>Length of the overlap of two arcs [a0,a1], [b0,b1] on a circle of circumference lap.</summary>
        public static double Overlap(double a0, double a1, double b0, double b1, double lap)
        {
            double best = 0;
            double a0m = Mod(a0, lap), la = a1 - a0, b0m = Mod(b0, lap), lb = b1 - b0;
            for (int k = -1; k <= 1; k++)
            {
                double lo = Math.Max(a0m, b0m + k * lap);
                double hi = Math.Min(a0m + la, b0m + k * lap + lb);
                if (hi - lo > best) best = hi - lo;
            }
            return best;
        }

        private static double Mod(double v, double m) { double r = v % m; return r < 0 ? r + m : r; }

        /// <summary>
        /// One decision per game corner. A corner holding only flat turns, or no listed turn, is removed; otherwise the slowest non-flat turn it overlaps sets class, speed and radius.
        /// </summary>
        public static List<LineDecision> Map(List<LineGameCorner> corners, List<LineTurn> turns, double lap, double refTop)
        {
            List<LineDecision> res = new List<LineDecision>();
            foreach (LineGameCorner c in corners)
            {
                LineDecision d = new LineDecision();
                d.Id = c.Id;
                LineTurn slowest = null;
                List<string> flat = new List<string>();
                foreach (LineTurn t in turns)
                {
                    if (Overlap(c.ZoneStart, c.ZoneEnd, t.S0, t.S1, lap) <= 0) continue;
                    d.Turns.Add(t.Turn);
                    if (t.Flat) { flat.Add("T" + t.Turn); continue; }
                    if (slowest == null || t.Kmh < slowest.Kmh) slowest = t;
                }
                if (slowest == null)
                {
                    d.Remove = true;
                    d.Why = flat.Count > 0 ? "flat " + string.Join(",", flat.ToArray()) : "no listed turn";
                }
                else
                {
                    d.Kmh = slowest.Kmh; d.By = slowest.Turn;
                    Classify(slowest.Kmh, refTop, out d.Type, out d.Speed, out d.Radius);
                }
                res.Add(d);
            }
            return res;
        }

        /// <summary>Parses corners_KEY.csv (header "turn,speedKmh,flat,apexX,apexZ,s0,s1"; '#' lines ignored).</summary>
        public static List<LineTurn> ParseCsv(string[] lines)
        {
            List<LineTurn> l = new List<LineTurn>();
            CultureInfo inv = CultureInfo.InvariantCulture;
            foreach (string raw in lines)
            {
                string s = raw.Trim();
                if (s.Length == 0 || s[0] == '#' || s.StartsWith("turn")) continue;
                string[] c = s.Split(',');
                if (c.Length < 7) continue;
                LineTurn t = new LineTurn();
                t.Turn = int.Parse(c[0].Trim(), inv);
                t.Kmh = double.Parse(c[1].Trim(), inv);
                t.Flat = c[2].Trim() == "1" || c[2].Trim().ToLowerInvariant() == "true";
                t.ApexX = double.Parse(c[3].Trim(), inv); t.ApexZ = double.Parse(c[4].Trim(), inv);
                t.S0 = double.Parse(c[5].Trim(), inv); t.S1 = double.Parse(c[6].Trim(), inv);
                l.Add(t);
            }
            return l;
        }
    }
}
