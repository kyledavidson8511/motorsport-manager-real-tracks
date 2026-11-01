// Unity-free planning of the auto-test "art tour" (request flag "artTour"): where the camera stands and what it looks at for each close-up of
// the generated venue art. The art generator records what it built in ArtRegistry (ArtGenerator.cs); AutoTestArtTour.cs renders the views.
// The same file is compiled by tools/autotest-unit-tests and by tools/art-tour (offline plot of every viewpoint over the pack data).
// No UnityEngine references; .NET 3.5 BCL only. World frame = pack frame: x east, z north, y up; a camera looks along +z at yaw 0.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RealTracks.ArtGeo
{
    /// <summary>One thing the art generator built that a viewpoint can frame.</summary>
    public class ArtMark
    {
        public string Kind = "";     // "stand" (seating) | "block" (solid) | "pit" (pit building) | "tower" | "building" (a mapped building listed by a view) | "yacht" (never a landmark)
        public string Id = "";
        public V2 Centre;
        public V2 Front;             // unit vector from the object towards the track (pit building: towards the pit lane); zero for a tower
        public float Length, Depth, Height, GroundY;

        public ArtMark() { }
        public ArtMark(string kind, string id, V2 centre, V2 front, float length, float depth, float height, float groundY)
        {
            Kind = kind; Id = id; Centre = centre; Front = front; Length = length; Depth = depth; Height = height; GroundY = groundY;
        }
    }

    /// <summary>A numbered turn of the pack's corner table (corners_KEY.csv): apex and the core of the turn in metres along the track.</summary>
    public class TourTurn
    {
        public int Turn;
        public V2 Apex;
        public float S0, S1;
        public TourTurn() { }
        public TourTurn(int turn, float ax, float az, float s0, float s1) { Turn = turn; Apex = new V2(ax, az); S0 = s0; S1 = s1; }
    }

    /// <summary>Everything the planner needs; the art generator fills it (Unity side), the offline tool fills it from the pack files.</summary>
    public class TourScene
    {
        public string PackId = "", LayoutKey = "";
        public List<V3> Track = new List<V3>();            // game centreline, closed, point 0 = start line
        public List<V3> PitLane = new List<V3>();
        public List<TourTurn> Turns = new List<TourTurn>();
        public List<ArtMark> Marks = new List<ArtMark>();
        public Func<float, float, float> Ground;           // terrain height at (x, z)
        public float TerrainX0, TerrainZ0, TerrainX1, TerrainZ1;
        public float SkirtRadius = 25000f;
        /// <summary>The pack manifest's "views" (null / empty = the generic default set) and planner notes (invalid entries dropped ...).</summary>
        public List<ViewSpec> Views;
    }

    /// <summary>One planned camera. Pos == Target and Skipped != "" when it cannot be planned.</summary>
    public class TourView
    {
        public string Name = "", Subject = "", Ids = "", Skipped = "";
        public V3 Pos, Target;
        public float Fov = 50f;
        public float FarClip = 0f;      // 0 = the main camera's own
        public int InView;              // art marks inside the frustum
        public bool Ok { get { return Skipped.Length == 0; } }
    }

    /// <summary>What the art generator recorded for the tour (cleared at the start of every pack venue build).</summary>
    public static class ArtRegistry
    {
        public static TourScene Scene;

        public static void Begin(string packId, string layoutKey)
        {
            Scene = new TourScene();
            Scene.PackId = packId; Scene.LayoutKey = layoutKey;
        }

        public static void Add(ArtMark m) { if (Scene != null && m != null) Scene.Marks.Add(m); }

        public static bool Matches(string packId, string layoutKey)
        {
            return Scene != null && Scene.Ground != null && Scene.Track.Count > 3 && string.Equals(Scene.PackId, packId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Scene.LayoutKey, layoutKey, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class TourPlan
    {
        public const float Aspect = 16f / 9f;
        public const float StandsMaxFromTurn = 200f;   // a stand belongs to T12-T15 when its centre is this close to one of those apexes
        public const float BlockSlack = 250f;          // infield blocks: nearest track point within this many metres of the T16..T18 core
        public const float TowerDistance = 150f;
        public const float MinAboveGround = 2f;

        // ---- small maths

        public static V2 Perp(V2 t) { return new V2(t.Y, -t.X); }   // right of the direction of travel (pack frame, y up, left-handed)

        public static float Yaw(V3 from, V3 to) { return (float)(Math.Atan2(to.X - from.X, to.Z - from.Z) * 180.0 / Math.PI); }
        public static float PitchDown(V3 from, V3 to)
        {
            float h = (float)Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Z - from.Z) * (to.Z - from.Z));
            return (float)(Math.Atan2(from.Y - to.Y, h) * 180.0 / Math.PI);
        }

        /// <summary>Cumulative distance along the closed polyline (metres from point 0); lap = total length including the closing segment.</summary>
        public static float[] Cumulative(IList<V3> track, out float lap)
        {
            float[] s = new float[track.Count];
            for (int i = 1; i < track.Count; i++) s[i] = s[i - 1] + V2.Dist(track[i - 1].XZ, track[i].XZ);
            lap = track.Count > 0 ? s[track.Count - 1] + V2.Dist(track[track.Count - 1].XZ, track[0].XZ) : 0f;
            return s;
        }

        /// <summary>Nearest point of the closed track polyline: distance, point, unit tangent (direction of travel) and the distance along the lap.</summary>
        public static float Nearest(IList<V3> track, float[] s, V2 p, out V2 q, out V2 tangent, out float along)
        {
            float best = float.MaxValue; q = p; tangent = new V2(1, 0); along = 0f;
            int n = track.Count;
            for (int i = 0; i < n; i++)
            {
                V2 a = track[i].XZ, b = track[(i + 1) % n].XZ, ab = b - a;
                float l2 = V2.Dot(ab, ab);
                float t = l2 > 1e-9f ? MathX.Clamp01(V2.Dot(p - a, ab) / l2) : 0f;
                V2 c = a + ab * t;
                float d = V2.Dist(c, p);
                if (d < best) { best = d; q = c; tangent = ab.Norm; along = s[i] + t * (float)Math.Sqrt(l2); }
            }
            return best;
        }

        /// <summary>Point at distance `along` (wrapped) on the closed track.</summary>
        public static V3 PointAt(IList<V3> track, float[] s, float lap, float along)
        {
            float a = along % lap; if (a < 0) a += lap;
            int n = track.Count;
            for (int i = 0; i < n; i++)
            {
                float end = i + 1 < n ? s[i + 1] : lap;
                if (a <= end || i == n - 1)
                {
                    float seg = end - s[i];
                    float t = seg > 1e-6f ? MathX.Clamp01((a - s[i]) / seg) : 0f;
                    V3 p0 = track[i], p1 = track[(i + 1) % n];
                    return new V3(p0.X + (p1.X - p0.X) * t, p0.Y + (p1.Y - p0.Y) * t, p0.Z + (p1.Z - p0.Z) * t);
                }
            }
            return track[0];
        }

        /// <summary>The camera's forward vector for a view.</summary>
        public static V3 Forward(TourView v) { return (v.Target - v.Pos).Norm; }

        /// <summary>
        /// Is the world point inside the camera's frustum (vertical field of view `fov` degrees, width/height = aspect), in front of it and not further than maxDist?
        /// Same maths as a Unity camera: right = up x forward.
        /// </summary>
        public static bool InFrustum(V3 pos, V3 target, float fovDeg, float aspect, V3 p, float maxDist)
        {
            V3 f = (target - pos).Norm;
            V3 right = V3.Cross(new V3(0, 1, 0), f).Norm;
            V3 up = V3.Cross(f, right);
            V3 v = p - pos;
            float z = V3.Dot(v, f);
            if (z < 1f || v.Len > maxDist) return false;
            float ty = (float)Math.Tan(fovDeg * 0.5 * Math.PI / 180.0), tx = ty * aspect;
            return Math.Abs(V3.Dot(v, right) / z) <= tx && Math.Abs(V3.Dot(v, up) / z) <= ty;
        }

        /// <summary>True when the camera is on the front (track) side of the mark, so its seats / facade are what the camera sees.</summary>
        public static bool SeesFront(ArtMark m, V3 camPos)
        {
            return V2.Dot(m.Front, camPos.XZ - m.Centre) > 0f;
        }

        public static int CountInView(TourScene sc, TourView v, float aspect)
        {
            int n = 0;
            foreach (ArtMark m in sc.Marks)
                if (InFrustum(v.Pos, v.Target, v.Fov, aspect, new V3(m.Centre.X, m.GroundY + m.Height * 0.5f, m.Centre.Y), 600f)) n++;
            return n;
        }

        public static int CountKindInView(TourScene sc, TourView v, float aspect, string kind)
        {
            int n = 0;
            foreach (ArtMark m in sc.Marks)
                if (m.Kind == kind && InFrustum(v.Pos, v.Target, v.Fov, aspect, new V3(m.Centre.X, m.GroundY + m.Height * 0.5f, m.Centre.Y), 600f)) n++;
            return n;
        }

        // ---- the plan

        /// <summary>Plans every view of sc.Views (or the generic default set when the pack has none), in the order listed.</summary>
        public static List<TourView> Compute(TourScene sc, float aspect)
        {
            List<TourView> list = new List<TourView>();
            float lap; float[] s = Cumulative(sc.Track, out lap);
            List<ViewSpec> specs = sc.Views != null && sc.Views.Count > 0 ? sc.Views : ViewSpec.Defaults();
            foreach (ViewSpec spec in specs)
            {
                int before = list.Count;
                try
                {
                    switch (spec.Kind)
                    {
                        case ViewSpec.KStand: Stands(sc, spec, list); break;
                        case ViewSpec.KBlocks: Blocks(sc, spec, s, list); break;
                        case ViewSpec.KStraightStand: StraightStand(sc, spec, s, lap, list); break;
                        case ViewSpec.KTower: Single(sc, spec, list, Tower); break;
                        case ViewSpec.KPit: Single(sc, spec, list, Pit); break;
                        case ViewSpec.KHorizon: Horizon(sc, spec, list); break;
                        case ViewSpec.KTurn: Turn(sc, spec, s, lap, list); break;
                        case ViewSpec.KFeature: Feature(sc, spec, list); break;
                        case ViewSpec.KStart: StartStraight(sc, spec, s, lap, list); break;
                        case ViewSpec.KPoint: PointView(sc, spec, list); break;
                        default: list.Add(Skip(spec, spec.Name, "unknown view kind '" + spec.Kind + "'")); break;
                    }
                }
                catch (Exception e)
                {
                    while (list.Count > before) list.RemoveAt(list.Count - 1);
                    list.Add(Skip(spec, spec.Name, "planning failed: " + e.Message));
                }
                if (spec.Subject.Length > 0)
                    for (int i = before; i < list.Count; i++) if (list[i].Ok) list[i].Subject = spec.Subject + " (" + list[i].Subject + ")";
            }
            foreach (TourView v in list)
                if (v.Ok)
                {
                    v.InView = CountInView(sc, v, aspect);
                    int y = CountKindInView(sc, v, aspect, "yacht");
                    if (y > 0) v.Subject += " [" + y + " yachts in view]";
                }
            return list;
        }

        private delegate void Planner(TourScene sc, ViewSpec spec, TourView v);

        private static TourView Skip(ViewSpec spec, string name, string why)
        {
            TourView v = new TourView(); v.Name = name; v.Subject = spec.Kind + " view"; v.Skipped = why;
            return v;
        }

        private static void Single(TourScene sc, ViewSpec spec, List<TourView> list, Planner p)
        {
            TourView v = new TourView(); v.Name = spec.Name; v.Subject = spec.Kind + " view";
            try { p(sc, spec, v); } catch (Exception e) { v.Skipped = "planning failed: " + e.Message; }
            list.Add(v);
        }

        private static V3 Lift(TourScene sc, V2 xz, float above)
        {
            float g = sc.Ground(xz.X, xz.Y);
            return new V3(xz.X, g + above, xz.Y);
        }

        /// <summary>Keeps the camera at least MinAboveGround over the terrain.</summary>
        private static V3 Clamp(TourScene sc, V3 p)
        {
            float g = sc.Ground(p.X, p.Z) + MinAboveGround;
            return p.Y < g ? new V3(p.X, g, p.Z) : p;
        }

        private static TourTurn FindTurn(TourScene sc, int n)
        {
            foreach (TourTurn t in sc.Turns) if (t.Turn == n) return t;
            return null;
        }

        private static string F0(float v) { return v.ToString("0", CultureInfo.InvariantCulture); }

        /// <summary>The recorded art object with this OSM id ("way/460658892"; the bare number matches too), optionally of one kind.</summary>
        public static ArtMark FindMark(TourScene sc, string id, string kind)
        {
            foreach (ArtMark m in sc.Marks)
            {
                if (kind.Length > 0 && m.Kind != kind) continue;
                if (string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase) || m.Id.EndsWith("/" + id, StringComparison.OrdinalIgnoreCase)) return m;
            }
            return null;
        }

        /// <summary>Camera for a stand-like mark seen from the track side, a little oblique so the rake of the seats shows.</summary>
        public static void FrameFront(TourScene sc, ArtMark m, float spanLimit, float height, TourView v)
        {
            V2 along = Perp(m.Front);
            float span = Math.Min(m.Length, spanLimit);
            float tanV = (float)Math.Tan(v.Fov * 0.5 * Math.PI / 180.0);
            float dist = MathX.Clamp(span * 0.55f / (tanV * Aspect) + 12f, 45f, 230f);
            V2 pos2 = m.Centre + m.Front * (m.Depth * 0.5f + dist) + along * (dist * 0.28f);
            v.Pos = Clamp(sc, new V3(pos2.X, sc.Ground(pos2.X, pos2.Y) + height, pos2.Y));
            v.Target = new V3(m.Centre.X, m.GroundY + Math.Max(3f, m.Height * 0.4f), m.Centre.Y);
        }

        private static void FrameStand(TourScene sc, ArtMark m, TourView v)
        {
            v.Ids = m.Id;
            v.Subject = (m.Kind == "block" ? "solid block " : "seating stand ") + m.Id + " (" + F0(m.Length) + " x " + F0(m.Depth) + " m) seen from the track side";
            FrameFront(sc, m, 220f, Math.Max(10f, m.Height * 0.9f + 6f), v);
        }

        /// <summary>"name", "name-b", "name-c" ...</summary>
        private static string Suffixed(string name, int i) { return i == 0 ? name : name + "-" + (char)('a' + i); }

        private static void Stands(TourScene sc, ViewSpec spec, List<TourView> list)
        {
            List<ArtMark> cand = new List<ArtMark>();
            int present = 0;
            float radius = spec.Radius > 0 ? spec.Radius : StandsMaxFromTurn;
            foreach (int t in spec.Turns)
            {
                TourTurn tt = FindTurn(sc, t);
                if (tt == null) continue;
                present++;
                foreach (ArtMark m in sc.Marks)
                    if (m.Kind == "stand" && V2.Dist(m.Centre, tt.Apex) <= radius && !cand.Contains(m)) cand.Add(m);
            }
            foreach (string id in spec.OsmIds)
            {
                ArtMark m = FindMark(sc, id, "stand");
                if (m != null && !cand.Contains(m)) cand.Add(m);
            }
            cand.Sort(delegate (ArtMark a, ArtMark b) { return b.Length.CompareTo(a.Length); });
            int max = spec.Count > 0 ? spec.Count : 3;
            if (cand.Count == 0)
            {
                string near = spec.Turns.Count > 0 ? ViewSpec.TurnsText(spec.Turns) : string.Join(",", spec.OsmIds.ToArray());
                TourView v = new TourView(); v.Name = spec.Name; v.Subject = "seating stands near " + near;
                v.Skipped = spec.Turns.Count > 0 && present == 0 && spec.OsmIds.Count == 0 ? "the corner table has none of the turns " + string.Join(",", spec.Turns.ConvertAll(delegate (int x) { return x.ToString(CultureInfo.InvariantCulture); }).ToArray())
                    : "no generated seating stand " + (spec.Turns.Count > 0 ? "within " + F0(radius) + " m of " + near : "with the ids " + near);
                list.Add(v);
                return;
            }
            for (int i = 0; i < cand.Count && i < max; i++)
            {
                TourView v = new TourView(); v.Name = Suffixed(spec.Name, i); v.Fov = spec.Fov > 0 ? spec.Fov : 50f;
                FrameStand(sc, cand[i], v);
                list.Add(v);
            }
        }

        private static void Blocks(TourScene sc, ViewSpec spec, float[] s, List<TourView> list)
        {
            string span = ViewSpec.SpanText(spec.Turns);
            TourView v = new TourView(); v.Name = spec.Name; v.Subject = "solid blocks" + (span.Length > 0 ? " inside " + span : ""); v.Fov = spec.Fov > 0 ? spec.Fov : 55f;
            list.Add(v);
            List<ArtMark> sel = new List<ArtMark>();
            if (spec.Turns.Count > 0)
            {
                float lo = float.MaxValue, hi = float.MinValue; int found = 0;
                foreach (int t in spec.Turns)
                {
                    TourTurn tt = FindTurn(sc, t);
                    if (tt == null) continue;
                    found++; lo = Math.Min(lo, tt.S0 - BlockSlack); hi = Math.Max(hi, tt.S1 + BlockSlack);
                }
                if (found < spec.Turns.Count && spec.OsmIds.Count == 0) { v.Skipped = "the corner table has no " + span; return; }
                foreach (ArtMark m in sc.Marks)
                {
                    if (m.Kind != "block") continue;
                    V2 q, tg; float along;
                    Nearest(sc.Track, s, m.Centre, out q, out tg, out along);
                    if (along >= lo && along <= hi) sel.Add(m);
                }
            }
            foreach (string id in spec.OsmIds)
            {
                ArtMark m = FindMark(sc, id, "block");
                if (m != null && !sel.Contains(m)) sel.Add(m);
            }
            if (sel.Count == 0) { v.Skipped = "no solid block " + (span.Length > 0 ? "along " + span : "with the given ids"); return; }
            V2 g = new V2(0, 0);
            foreach (ArtMark m in sel) g = g + m.Centre;
            g = g * (1f / sel.Count);
            float r = 0f; StringBuilder ids = new StringBuilder();
            foreach (ArtMark m in sel) { r = Math.Max(r, V2.Dist(m.Centre, g) + m.Length * 0.5f); if (ids.Length > 0) ids.Append(','); ids.Append(m.Id); }
            V2 q0, t0; float a0;
            Nearest(sc.Track, s, g, out q0, out t0, out a0);
            V2 dir = (g - q0).Norm;
            if (dir.Len < 0.5f) dir = new V2(0, 1);
            float tanV = (float)Math.Tan(v.Fov * 0.5 * Math.PI / 180.0);
            float dist = MathX.Clamp(r / (tanV * Aspect * 0.8f) + 40f, 120f, 420f);
            V2 p2 = g - dir * dist;
            v.Pos = Clamp(sc, new V3(p2.X, sc.Ground(p2.X, p2.Y) + dist * 0.5f, p2.Y));
            v.Target = new V3(g.X, sc.Ground(g.X, g.Y) + 3f, g.Y);
            v.Ids = ids.ToString();
            v.Subject = sel.Count + " solid blocks" + (span.Length > 0 ? " inside " + span : "") + " (" + v.Ids + ")";
        }

        private static void StraightStand(TourScene sc, ViewSpec spec, float[] s, float lap, List<TourView> list)
        {
            TourView v = new TourView(); v.Name = spec.Name; v.Subject = "main-straight grandstand opposite the pits, along the straight"; v.Fov = spec.Fov > 0 ? spec.Fov : 50f;
            list.Add(v);
            if (sc.PitLane.Count == 0) { v.Skipped = "no pit lane to decide which side is opposite"; return; }
            V3 pm = sc.PitLane[sc.PitLane.Count / 2];
            V2 pq, pt; float pa;
            Nearest(sc.Track, s, pm.XZ, out pq, out pt, out pa);
            float pitSide = V2.Dot(Perp(pt), pm.XZ - pq);
            ArtMark best = null; V2 bestTan = new V2(1, 0);
            foreach (ArtMark m in sc.Marks)
            {
                if (m.Kind != "stand") continue;
                V2 q, tg; float along;
                float d = Nearest(sc.Track, s, m.Centre, out q, out tg, out along);
                if (d > 140f) continue;
                float fromStart = along; if (fromStart > lap * 0.5f) fromStart -= lap;       // -lap/2 .. lap/2
                if (fromStart < -900f || fromStart > 400f) continue;
                float side = V2.Dot(Perp(tg), m.Centre - q);
                if (side * pitSide >= 0f) continue;                                          // same side as the pits
                if (best == null || m.Length > best.Length) { best = m; bestTan = tg; }
            }
            if (best == null) { v.Skipped = "no generated stand on the main straight opposite the pits"; return; }
            ArtMark b = best;
            // the stand's long side runs with the track: look from before its upstream end, close to the track side, along the straight
            V2 p2 = b.Centre + b.Front * (b.Depth * 0.5f + 45f) - bestTan * (b.Length * 0.5f + 120f);
            v.Pos = Clamp(sc, new V3(p2.X, sc.Ground(p2.X, p2.Y) + 14f, p2.Y));
            V2 t2 = b.Centre + bestTan * (b.Length * 0.1f);
            v.Target = new V3(t2.X, b.GroundY + 6f, t2.Y);
            v.Ids = b.Id;
            v.Subject = "stand " + b.Id + " (" + F0(b.Length) + " x " + F0(b.Depth) + " m) on the main straight opposite the pits, viewed along the straight";
        }

        /// <summary>The mark a tower view frames: the one with a listed id, else the tower, else the tallest recorded object ("landmark").</summary>
        public static ArtMark PickLandmark(TourScene sc, ViewSpec spec)
        {
            foreach (string id in spec.OsmIds) { ArtMark m = FindMark(sc, id, ""); if (m != null) return m; }
            foreach (ArtMark m in sc.Marks) if (m.Kind == "tower") return m;
            ArtMark tall = null;
            foreach (ArtMark m in sc.Marks) { if (!IsLandmarkKind(m.Kind)) continue; if (tall == null || m.Height > tall.Height) tall = m; }
            return tall;
        }

        /// <summary>Generated props are never landmarks: run 18 framed "yacht/104, 38 m" as Monaco's tallest landmark. Yachts (and any future kind of
        /// scattered prop) are left out of the tallest-landmark fallback; they can still be found by an explicit id.</summary>
        public static bool IsLandmarkKind(string kind)
        {
            return kind != "yacht" && kind != "prop" && kind != "tree" && kind != "carpark";
        }

        private static void FrameTower(TourScene sc, ArtMark t, TourView v)
        {
            float lap; float[] s = Cumulative(sc.Track, out lap);
            V2 q, tg; float along;
            Nearest(sc.Track, s, t.Centre, out q, out tg, out along);
            V2 dir = (q - t.Centre).Norm;
            if (dir.Len < 0.5f) dir = new V2(0, -1);
            bool tower = t.Kind == "tower";
            float distance = tower ? TowerDistance : MathX.Clamp(Math.Max(t.Height * 2f, Math.Max(t.Length, t.Depth) * 0.9f), 80f, 250f);
            V2 p2 = t.Centre + dir * distance;
            v.Pos = Clamp(sc, new V3(p2.X, sc.Ground(p2.X, p2.Y) + 20f, p2.Y));
            v.Target = new V3(t.Centre.X, t.GroundY + t.Height * 0.5f, t.Centre.Y);
            v.Ids = t.Id;
            v.Subject = tower ? "observation tower, " + F0(t.Height) + " m, from " + F0(TowerDistance) + " m"
                : "tallest landmark: " + t.Kind + " " + t.Id + ", " + F0(t.Height) + " m, from " + F0(distance) + " m";
        }

        private static void Tower(TourScene sc, ViewSpec spec, TourView v)
        {
            v.Fov = spec.Fov > 0 ? spec.Fov : 50f;
            ArtMark t = PickLandmark(sc, spec);
            if (t == null) { v.Skipped = "no observation tower (or any other art object) was generated"; return; }
            FrameTower(sc, t, v);
        }

        private static void FramePit(TourScene sc, ArtMark p, TourView v)
        {
            V2 along = Perp(p.Front);
            float span = Math.Min(p.Length, 60f);   // a section of the facade: the whole 360 m building would not show the garage doors
            float tanV = (float)Math.Tan(v.Fov * 0.5 * Math.PI / 180.0);
            float dist = MathX.Clamp(span * 0.55f / (tanV * Aspect) + 8f, 40f, 160f);
            V2 p2 = p.Centre + p.Front * (p.Depth * 0.5f + dist) + along * (dist * 0.15f);
            v.Pos = Clamp(sc, new V3(p2.X, sc.Ground(p2.X, p2.Y) + 16f, p2.Y));
            v.Target = new V3(p.Centre.X, p.GroundY + p.Height * 0.3f, p.Centre.Y);
            v.Ids = p.Id;
            v.Subject = "pit building " + p.Id + " (" + F0(p.Length) + " x " + F0(p.Depth) + " m, " + F0(p.Height) + " m high) from the lane side";
        }

        private static void Pit(TourScene sc, ViewSpec spec, TourView v)
        {
            v.Fov = spec.Fov > 0 ? spec.Fov : 50f;
            ArtMark p = null;
            foreach (string id in spec.OsmIds) { p = FindMark(sc, id, "pit"); if (p != null) break; }
            if (p == null) foreach (ArtMark m in sc.Marks) if (m.Kind == "pit") { p = m; break; }
            if (p == null) { v.Skipped = "no pit building was generated"; return; }
            FramePit(sc, p, v);
        }

        /// <summary>Any recorded art object: by OSM id, or the nearest of `featureType` to the first listed turn's apex.</summary>
        private static void Feature(TourScene sc, ViewSpec spec, List<TourView> list)
        {
            List<ArtMark> found = new List<ArtMark>();
            foreach (string id in spec.OsmIds)
            {
                ArtMark m = FindMark(sc, id, spec.FeatureType);
                if (m != null && !found.Contains(m)) found.Add(m);
            }
            if (found.Count == 0 && spec.OsmIds.Count == 0 && spec.Turns.Count > 0)
            {
                TourTurn tt = FindTurn(sc, spec.Turns[0]);
                ArtMark best = null; float bd = float.MaxValue;
                if (tt != null)
                    foreach (ArtMark m in sc.Marks)
                    {
                        if (m.Kind != spec.FeatureType) continue;
                        float d = V2.Dist(m.Centre, tt.Apex);
                        if (d < bd) { bd = d; best = m; }
                    }
                if (best != null) found.Add(best);
            }
            if (found.Count == 0)
            {
                list.Add(Skip(spec, spec.Name, spec.OsmIds.Count > 0 ? "no recorded art object with the id(s) " + string.Join(",", spec.OsmIds.ToArray()) + " (the generator may have skipped it)"
                    : "no " + spec.FeatureType + " found near " + ViewSpec.TurnsText(spec.Turns)));
                return;
            }
            int max = spec.Count > 0 ? spec.Count : found.Count;
            for (int i = 0; i < found.Count && i < max; i++)
            {
                ArtMark m = found[i];
                TourView v = new TourView(); v.Name = Suffixed(spec.Name, i); v.Fov = spec.Fov > 0 ? spec.Fov : 50f;
                try
                {
                    if (m.Kind == "tower") FrameTower(sc, m, v);
                    else if (m.Kind == "pit") FramePit(sc, m, v);
                    else FrameStand(sc, m, v);
                }
                catch (Exception e) { v.Skipped = "planning failed: " + e.Message; }
                list.Add(v);
            }
        }

        /// <summary>The venue centre (middle of the track's bounding box, like the hub cameras) and the direction to the side edge of the terrain that is furthest away.</summary>
        public static void HorizonAim(TourScene sc, out V2 centre, out V2 dir)
        {
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (V3 p in sc.Track) { x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z); }
            centre = new V2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
            V2[] mids = { new V2(centre.X, sc.TerrainZ0), new V2(centre.X, sc.TerrainZ1), new V2(sc.TerrainX0, centre.Y), new V2(sc.TerrainX1, centre.Y) };
            V2 best = mids[0]; float bd = -1f;
            foreach (V2 m in mids) { float d = V2.Dist(m, centre); if (d > bd) { bd = d; best = m; } }
            dir = (best - centre).Norm;
        }

        private static void Horizon(TourScene sc, ViewSpec spec, List<TourView> list)
        {
            bool high = spec.From == "high";
            float above = high ? 350f : 6f, pitch = high ? 14f : 1f;
            TourView v = new TourView(); v.Name = spec.Name; v.Fov = spec.Fov > 0 ? spec.Fov : 60f;
            v.Subject = high ? "the horizon from 350 m above the venue centre at a high pitch (14 degrees down)" : "the horizon from the venue centre at a low pitch (about 1 degree down)";
            list.Add(v);
            V2 c, d;
            HorizonAim(sc, out c, out d);
            if (d.Len < 0.5f) { v.Skipped = "no terrain edge direction"; return; }
            float y = sc.Ground(c.X, c.Y) + above;
            v.Pos = new V3(c.X, y, c.Y);
            float far = 2000f;
            v.Target = new V3(c.X + d.X * far, y - (float)Math.Tan(pitch * Math.PI / 180.0) * far, c.Y + d.Y * far);
            v.FarClip = sc.SkirtRadius * 1.15f;
        }

        /// <summary>A turn's braking zone looking at its apex (the first listed turn).</summary>
        private static void Turn(TourScene sc, ViewSpec spec, float[] s, float lap, List<TourView> list)
        {
            int n = spec.Turns[0];
            TourView v = new TourView(); v.Name = spec.Name; v.Subject = "turn " + n + " kerbs and run-off from the braking zone"; v.Fov = spec.Fov > 0 ? spec.Fov : 55f;
            list.Add(v);
            TourTurn t = FindTurn(sc, n);
            if (t == null) { v.Skipped = "the corner table has no T" + n; return; }
            V3 back = PointAt(sc.Track, s, lap, t.S0 - 140f);
            v.Pos = Clamp(sc, new V3(back.X, back.Y + 22f, back.Z));
            v.Target = new V3(t.Apex.X, sc.Ground(t.Apex.X, t.Apex.Y) + 1f, t.Apex.Y);
        }

        /// <summary>Generic start straight: from 70 m before the start line, a little above the road, looking 220 m down the straight.</summary>
        private static void StartStraight(TourScene sc, ViewSpec spec, float[] s, float lap, List<TourView> list)
        {
            TourView v = new TourView(); v.Name = spec.Name; v.Subject = "the start / finish straight from 70 m before the line"; v.Fov = spec.Fov > 0 ? spec.Fov : 60f;
            list.Add(v);
            if (sc.Track.Count < 4 || lap < 400f) { v.Skipped = "no usable track"; return; }
            V3 back = PointAt(sc.Track, s, lap, -70f), ahead = PointAt(sc.Track, s, lap, 220f);
            v.Pos = Clamp(sc, new V3(back.X, back.Y + 6f, back.Z));
            v.Target = new V3(ahead.X, ahead.Y + 1.5f, ahead.Z);
        }

        /// <summary>A hand-picked camera (manifest "point" view): pos / target in pack x/z, lifted over the terrain; the camera never below MinAboveGround.</summary>
        private static void PointView(TourScene sc, ViewSpec spec, List<TourView> list)
        {
            TourView v = new TourView(); v.Name = spec.Name; v.Fov = spec.Fov > 0 ? spec.Fov : 55f;
            v.Subject = "hand-picked view from (" + F0(spec.PosX) + "," + F0(spec.PosZ) + ") +" + F0(spec.PosAbove) + " m towards (" + F0(spec.TargetX) + "," + F0(spec.TargetZ) + ")";
            list.Add(v);
            if (!spec.HasPos || !spec.HasTarget) { v.Skipped = "point view without pos/target"; return; }
            v.Pos = Clamp(sc, new V3(spec.PosX, sc.Ground(spec.PosX, spec.PosZ) + spec.PosAbove, spec.PosZ));
            v.Target = new V3(spec.TargetX, sc.Ground(spec.TargetX, spec.TargetZ) + spec.TargetAbove, spec.TargetZ);
            if (V3.Dot(v.Target - v.Pos, v.Target - v.Pos) < 1f) v.Skipped = "pos and target coincide";
        }

        /// <summary>One line for the game log / events: "name pos (x,y,z) target (x,y,z) fov N".</summary>
        public static string Describe(TourView v)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return v.Name + " pos (" + v.Pos.X.ToString("0.0", c) + "," + v.Pos.Y.ToString("0.0", c) + "," + v.Pos.Z.ToString("0.0", c) + ") target (" +
                v.Target.X.ToString("0.0", c) + "," + v.Target.Y.ToString("0.0", c) + "," + v.Target.Z.ToString("0.0", c) + ") fov " + v.Fov.ToString("0", c) +
                " yaw " + Yaw(v.Pos, v.Target).ToString("0", c) + " pitch " + PitchDown(v.Pos, v.Target).ToString("0.0", c);
        }
    }
}
