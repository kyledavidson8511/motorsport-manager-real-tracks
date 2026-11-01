using System;
using System.Collections.Generic;
using System.Globalization;

// Unity-free (see AutoTestJson.cs): the request field "clips" and the pure decision logic of the turn-targeted clips, unit-tested in tools/autotest-unit-tests.
namespace RealTracks
{
    /// <summary>One entry of an item's "clips" array: record 'Seconds' of race time starting when a car is 'LeadMeters' before turn 'Turn'.</summary>
    public class AtClipRequest
    {
        public const double DefaultLeadMeters = 250, DefaultSeconds = 12;
        public int Turn;
        public double LeadMeters = DefaultLeadMeters, Seconds = DefaultSeconds;
        public string Label { get { return "T" + Turn.ToString(CultureInfo.InvariantCulture); } }
    }

    /// <summary>What happened to one requested clip (summary.json "clips").</summary>
    public class AtClipOut
    {
        public string Turn = "", Dir = "", Car = "", Reason = "";
        public int Frames, Lap;
        public bool Skipped;
    }

    public static class AtClips
    {
        public const int MaxClips = 8;
        /// <summary>A clip starts only while at least this share of the lead distance is still ahead, so the approach is in the clip.</summary>
        public const double MinRemainingShare = 0.4;

        /// <summary>
        /// Parses the optional "clips" array of an item. Absent or not an array -> empty. Entries that are not objects, have no usable turn ("T3", "t3", "3", 3),
        /// or a non-positive or non-numeric lead / seconds are skipped (the rest still count). leadMeters is clamped to 20..2000, seconds to 2..60; at most MaxClips.
        /// </summary>
        public static List<AtClipRequest> Parse(Dictionary<string, object> item)
        {
            List<AtClipRequest> res = new List<AtClipRequest>();
            List<object> arr = AtJson.Arr(item, "clips");
            if (arr == null) return res;
            for (int i = 0; i < arr.Count && res.Count < MaxClips; i++)
            {
                Dictionary<string, object> o = arr[i] as Dictionary<string, object>;
                if (o == null) continue;
                int turn;
                object tv;
                o.TryGetValue("turn", out tv);
                if (!ParseTurn(tv, out turn)) continue;
                AtClipRequest c = new AtClipRequest();
                c.Turn = turn;
                if (!ReadNum(o, "leadMeters", AtClipRequest.DefaultLeadMeters, out c.LeadMeters)) continue;
                if (!ReadNum(o, "seconds", AtClipRequest.DefaultSeconds, out c.Seconds)) continue;
                c.LeadMeters = Math.Max(20, Math.Min(2000, c.LeadMeters));
                c.Seconds = Math.Max(2, Math.Min(60, c.Seconds));
                res.Add(c);
            }
            return res;
        }

        private static bool ReadNum(Dictionary<string, object> o, string key, double dflt, out double v)
        {
            v = dflt;
            object x;
            if (!o.TryGetValue(key, out x) || x == null) return true;
            if (!(x is double)) return false;
            v = (double)x;
            return !double.IsNaN(v) && !double.IsInfinity(v) && v > 0;
        }

        /// <summary>"T3", "t03", " 3 " or the number 3 -> 3. Zero, negatives, fractions and other text are refused.</summary>
        public static bool ParseTurn(object v, out int turn)
        {
            turn = 0;
            if (v is double)
            {
                double d = (double)v;
                if (d != Math.Floor(d) || d < 1 || d > 99) return false;
                turn = (int)d;
                return true;
            }
            string s = v as string;
            if (s == null) return false;
            s = s.Trim();
            if (s.Length > 0 && (s[0] == 'T' || s[0] == 't')) s = s.Substring(1);
            if (s.Length == 0 || s.Length > 2) return false;
            for (int i = 0; i < s.Length; i++) if (s[i] < '0' || s[i] > '9') return false;
            turn = int.Parse(s, CultureInfo.InvariantCulture);
            return turn >= 1;
        }

        /// <summary>Metres still to drive from the car to the turn along the lap (0..lapLength), wrapping over the start/finish line.</summary>
        public static double DistanceToTurn(double carDist, double turnDist, double lapLength)
        {
            if (lapLength <= 0) return double.PositiveInfinity;
            double r = (turnDist - carDist) % lapLength;
            if (r < 0) r += lapLength;
            return r;
        }

        /// <summary>
        /// True when a car should start the clip: it is on lap 2 or later (1-based current lap, the field has spread out), within leadMeters of the turn,
        /// and not so close that the approach would be missing (at least MinRemainingShare of the lead is still ahead).
        /// </summary>
        public static bool ShouldStart(int carLap, double distToTurn, double leadMeters)
        {
            if (carLap < 2) return false;
            if (double.IsNaN(distToTurn) || distToTurn > leadMeters) return false;
            return distToTurn >= leadMeters * MinRemainingShare;
        }

        /// <summary>Race lap-time based give-up: a clip armed on leader lap armedLap times out once the leader is more than 2 laps further.</summary>
        public static bool TimedOut(int armedLap, int leaderLap)
        {
            return leaderLap > armedLap + 2;
        }

        /// <summary>Finds a turn in a corner table.</summary>
        public static LineTurn FindTurn(List<LineTurn> turns, int turn)
        {
            if (turns == null) return null;
            foreach (LineTurn t in turns) if (t.Turn == turn) return t;
            return null;
        }

        /// <summary>The compass yaw (degrees, 0 = +z, 90 = +x, the Unity convention) of the vector from (x0,z0) to (x1,z1).</summary>
        public static double YawDegrees(double x0, double z0, double x1, double z1)
        {
            return Math.Atan2(x1 - x0, z1 - z0) * 180.0 / Math.PI;
        }

        /// <summary>Moves 'cur' towards 'target' (degrees) by at most 'maxStep', the short way round.</summary>
        public static double StepAngle(double cur, double target, double maxStep)
        {
            double diff = ((target - cur) % 360.0 + 540.0) % 360.0 - 180.0;
            if (Math.Abs(diff) <= maxStep) return target;
            return cur + (diff > 0 ? maxStep : -maxStep);
        }
    }
}
