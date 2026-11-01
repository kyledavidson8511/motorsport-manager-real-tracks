// Unity-free: the pack manifest's optional "views" array (SPEC-pack-format.md, "Art-tour views") and the generic default set.
// ViewSpec describes WHAT the auto-test art tour should photograph; TourPlan (ArtTour.cs) decides WHERE the camera stands.
// Compiled by the mod, by tools/autotest-unit-tests and by tools/art-tour. .NET 3.5 BCL only; input is the plain object tree
// MiniJson / AtJson produce (Dictionary<string, object>, List<object>, double, string, bool).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RealTracks.ArtGeo
{
    public class ViewSpec
    {
        public const string KStand = "stand", KBlocks = "blocks", KStraightStand = "straight-stand", KTower = "tower", KPit = "pit",
            KHorizon = "horizon", KTurn = "turn", KFeature = "feature", KStart = "start", KPoint = "point";
        public static readonly string[] Kinds = { KStand, KBlocks, KStraightStand, KTower, KPit, KHorizon, KTurn, KFeature, KStart, KPoint };
        public static readonly string[] FeatureTypes = { "stand", "block", "pit", "tower" };

        public string Name = "", Kind = "", FeatureType = "", Subject = "", From = "";
        public List<int> Turns = new List<int>();
        public List<string> OsmIds = new List<string>();
        public int Count;          // stand / feature: how many views at most (0 = the kind's default)
        public float Radius;       // stand: metres from a listed turn's apex (0 = default 200)
        public float Fov;          // 0 = the kind's default
        /// <summary>point: camera and target in pack x/z, each lifted posAbove / targetAbove metres over the terrain (a hand-picked first-look view).</summary>
        public bool HasPos, HasTarget;
        public float PosX, PosZ, TargetX, TargetZ, PosAbove = 2f, TargetAbove;

        public static bool KnownKind(string k) { return Array.IndexOf(Kinds, k) >= 0; }

        /// <summary>The generic set used when a pack has no "views": start straight, T1, pit building, tower (or tallest landmark), horizon low and high.</summary>
        public static List<ViewSpec> Defaults()
        {
            List<ViewSpec> l = new List<ViewSpec>();
            l.Add(Make("start-straight", KStart));
            ViewSpec t1 = Make("t1", KTurn); t1.Turns.Add(1); l.Add(t1);
            l.Add(Make("pit-building", KPit));
            l.Add(Make("tower", KTower));
            ViewSpec lo = Make("horizon-low", KHorizon); lo.From = "low"; l.Add(lo);
            ViewSpec hi = Make("horizon-high", KHorizon); hi.From = "high"; l.Add(hi);
            return l;
        }

        private static ViewSpec Make(string name, string kind) { ViewSpec v = new ViewSpec(); v.Name = name; v.Kind = kind; return v; }

        /// <summary>
        /// Parses and validates the manifest's "views" value. Valid entries are returned; every problem is appended to `errors` as a sentence
        /// ("views[2] 'x': ..."). Returns null when `raw` is null (no views key); a non-array value is one error and an empty list.
        /// </summary>
        public static List<ViewSpec> ParseList(object raw, List<string> errors)
        {
            if (raw == null) return null;
            List<ViewSpec> res = new List<ViewSpec>();
            List<object> arr = raw as List<object>;
            if (arr == null) { errors.Add("views must be an array"); return res; }
            List<string> names = new List<string>();
            for (int i = 0; i < arr.Count; i++)
            {
                string where = "views[" + i + "]";
                Dictionary<string, object> o = arr[i] as Dictionary<string, object>;
                if (o == null) { errors.Add(where + " is not an object"); continue; }
                List<string> e = new List<string>();
                ViewSpec v = ParseOne(o, e);
                if (v.Name.Length > 0) where += " '" + v.Name + "'";
                if (v.Name.Length > 0 && names.Contains(v.Name.ToLowerInvariant())) e.Add("duplicate name");
                if (e.Count > 0) { foreach (string s in e) errors.Add(where + ": " + s); continue; }
                names.Add(v.Name.ToLowerInvariant());
                res.Add(v);
            }
            return res;
        }

        private static ViewSpec ParseOne(Dictionary<string, object> o, List<string> e)
        {
            ViewSpec v = new ViewSpec();
            v.Name = StrOf(o, "name", e);
            v.Kind = StrOf(o, "kind", e);
            v.FeatureType = StrOf(o, "featureType", e);
            v.Subject = StrOf(o, "subject", e);
            v.From = StrOf(o, "from", e);
            if (v.Name.Length == 0) e.Add("name is required");
            else if (!ValidName(v.Name)) e.Add("name must be 1-40 characters of a-z 0-9 - _ (it becomes part of a screenshot file name)");
            if (v.Kind.Length == 0) e.Add("kind is required");
            else if (!KnownKind(v.Kind)) e.Add("unknown kind '" + v.Kind + "' (one of " + string.Join(", ", Kinds) + ")");
            object t;
            if (o.TryGetValue("turns", out t))
            {
                List<object> l = t as List<object>;
                if (l == null) e.Add("turns must be an array of turn numbers");
                else foreach (object x in l)
                    {
                        double d = x is double ? (double)x : double.NaN;
                        if (double.IsNaN(d) || d != Math.Floor(d) || d < 1 || d > 99) { e.Add("turns must be whole numbers 1..99"); break; }
                        v.Turns.Add((int)d);
                    }
            }
            if (o.TryGetValue("osmIds", out t))
            {
                List<object> l = t as List<object>;
                if (l == null) e.Add("osmIds must be an array of strings");
                else foreach (object x in l)
                    {
                        string s = x as string;
                        if (s == null || s.Trim().Length == 0) { e.Add("osmIds must be non-empty strings like \"way/460658892\""); break; }
                        v.OsmIds.Add(s.Trim());
                    }
            }
            v.Count = (int)Num(o, "count", 0, 1, 6, e);
            v.Radius = (float)Num(o, "radius", 0, 20, 1000, e);
            v.Fov = (float)Num(o, "fov", 0, 20, 90, e);
            v.HasPos = XZ(o, "pos", e, out v.PosX, out v.PosZ);
            v.HasTarget = XZ(o, "target", e, out v.TargetX, out v.TargetZ);
            v.PosAbove = (float)Num(o, "posAbove", 2, -50, 1000, e);
            v.TargetAbove = (float)Num(o, "targetAbove", 0, -50, 1000, e);
            if (v.FeatureType.Length > 0 && Array.IndexOf(FeatureTypes, v.FeatureType) < 0) e.Add("featureType must be one of " + string.Join(", ", FeatureTypes));
            if (v.Kind == KHorizon && v.From.Length > 0 && v.From != "low" && v.From != "high") e.Add("a horizon view's from must be \"low\" or \"high\"");
            if (v.Kind == KHorizon && v.From.Length == 0) v.From = "low";
            // what each kind needs
            switch (v.Kind)
            {
                case KStand: case KBlocks:
                    if (v.Turns.Count == 0 && v.OsmIds.Count == 0) e.Add(v.Kind + " needs turns or osmIds");
                    break;
                case KTurn:
                    if (v.Turns.Count == 0) e.Add("turn needs turns (the first one is framed)");
                    break;
                case KPoint:
                    if (!v.HasPos || !v.HasTarget) e.Add("point needs pos and target ([x, z] in pack metres)");
                    break;
                case KFeature:
                    if (v.OsmIds.Count == 0 && !(v.FeatureType.Length > 0 && v.Turns.Count > 0)) e.Add("feature needs osmIds, or a featureType together with turns");
                    break;
            }
            return v;
        }

        private static bool XZ(Dictionary<string, object> o, string k, List<string> e, out float x, out float z)
        {
            x = z = 0f;
            object t;
            if (!o.TryGetValue(k, out t) || t == null) return false;
            List<object> l = t as List<object>;
            if (l == null || l.Count != 2 || !(l[0] is double) || !(l[1] is double) || double.IsNaN((double)l[0]) || double.IsNaN((double)l[1])
                || Math.Abs((double)l[0]) > 1e6 || Math.Abs((double)l[1]) > 1e6) { e.Add(k + " must be [x, z] (two numbers, pack metres)"); return false; }
            x = (float)(double)l[0]; z = (float)(double)l[1];
            return true;
        }

        public static bool ValidName(string n)
        {
            if (n.Length < 1 || n.Length > 40) return false;
            foreach (char c in n) if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
            return true;
        }

        private static string StrOf(Dictionary<string, object> o, string k, List<string> e)
        {
            object x;
            if (!o.TryGetValue(k, out x) || x == null) return "";
            string s = x as string;
            if (s == null) { e.Add(k + " must be a string"); return ""; }
            return s.Trim();
        }

        private static double Num(Dictionary<string, object> o, string k, double def, double min, double max, List<string> e)
        {
            object x;
            if (!o.TryGetValue(k, out x) || x == null) return def;
            if (!(x is double)) { e.Add(k + " must be a number"); return def; }
            double d = (double)x;
            if (d < min || d > max) { e.Add(k + " must be " + min.ToString(CultureInfo.InvariantCulture) + ".." + max.ToString(CultureInfo.InvariantCulture)); return def; }
            return d;
        }

        /// <summary>"T12-T15" / "T5" / "T3,T7" for a log or subject line.</summary>
        public static string TurnsText(List<int> turns)
        {
            if (turns == null || turns.Count == 0) return "";
            List<int> s = new List<int>(turns); s.Sort();
            if (s.Count == 1) return "T" + s[0];
            bool consecutive = true;
            for (int i = 1; i < s.Count; i++) if (s[i] != s[i - 1] + 1) consecutive = false;
            if (consecutive) return "T" + s[0] + "-T" + s[s.Count - 1];
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Count; i++) { if (i > 0) sb.Append(','); sb.Append('T').Append(s[i]); }
            return sb.ToString();
        }

        /// <summary>"T16-T18" for the span from the lowest to the highest listed turn (blocks views).</summary>
        public static string SpanText(List<int> turns)
        {
            if (turns == null || turns.Count == 0) return "";
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (int t in turns) { lo = Math.Min(lo, t); hi = Math.Max(hi, t); }
            return lo == hi ? "T" + lo : "T" + lo + "-T" + hi;
        }

        public override string ToString()
        {
            return Name + " (" + Kind + (Turns.Count > 0 ? " " + TurnsText(Turns) : "") + (OsmIds.Count > 0 ? " " + string.Join(",", OsmIds.ToArray()) : "") + ")";
        }
    }
}
