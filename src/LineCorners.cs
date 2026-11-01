using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace RealTracks
{
    /// <summary>Settings of the racing-line work (WP B).</summary>
    public partial class Settings
    {
        /// <summary>Use the pack's real per-turn corner speeds (corners_KEY.csv) instead of the game's own geometric corner rating.</summary>
        public bool LineRealCornerSpeeds = true;
        /// <summary>Top speed (km/h) the real speeds are scaled against: f = v / (0.9 x this) picks Low / Medium / High (SessionPerformance.GetSpeedForCorner).</summary>
        public float LineRefTopSpeedKmh = 330f;
    }

    /// <summary>
    /// After PathData.Prepare the game has rated every corner from the racing line's geometry. For a pack with a corner table, flat corners are removed as
    /// slowing corners and the rest take the class and speed of their real minimum speed (logic: LineCornerMap, unit-tested). Pack circuits only.
    /// </summary>
    public static class LineCorners
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void DrawGui(Settings s)
        {
            s.LineRealCornerSpeeds = GUILayout.Toggle(s.LineRealCornerSpeeds, " Use the pack's real corner speeds (flat corners stop being braking corners; fast corners are fast)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Reference top speed (km/h): " + s.LineRefTopSpeedKmh.ToString("0", Inv), GUILayout.Width(240));
            s.LineRefTopSpeedKmh = GUILayout.HorizontalSlider(s.LineRefTopSpeedKmh, 250f, 400f, GUILayout.Width(220));
            GUILayout.EndHorizontal();
        }

        public static void Apply(PathData d, PackLayout layout, LayoutPaths lp, BuiltVenue built)
        {
            try
            {
                if (!Main.Settings.LineRealCornerSpeeds) { Main.Log("RT LINE: real corner speeds are switched off; the game's own corner rating stays."); return; }
                if (layout.CornersFile == null) { Main.Log("RT LINE: the pack has no corner table (manifest 'corners'); the game's own corner rating stays."); return; }
                string file = layout.Pack.PathOf(layout.CornersFile);
                if (!File.Exists(file)) { VenueBuilder.Warn(built, "corner table " + layout.CornersFile + " is missing; the game's own rating stays."); return; }
                List<LineTurn> turns = LineCornerMap.ParseCsv(File.ReadAllLines(file));
                if (turns.Count == 0) { Main.Log("RT LINE: the corner table is empty."); return; }

                double lap = lp.S[lp.S.Length - 1] + Vector3.Distance(lp.Track[lp.Track.Count - 1], lp.Track[0]);
                double[] gateS = new double[d.gates.Count];
                for (int i = 0; i < gateS.Length; i++) gateS[i] = SAt(lp, d.gates[i].position);

                List<PathData.Corner> cs = d.corners;
                List<LineGameCorner> zones = new List<LineGameCorner>();
                for (int k = 0; k < cs.Count; k++)
                {
                    LineGameCorner z = new LineGameCorner();
                    z.Id = k;
                    double a = gateS[cs[k].startGateID], b = gateS[cs[k].endGateID];
                    if (b < a) b += lap;
                    z.ZoneStart = a; z.ZoneEnd = b + (b - a);
                    zones.Add(z);
                }
                double refTop = Main.Settings.LineRefTopSpeedKmh;
                List<LineDecision> dec = LineCornerMap.Map(zones, turns, lap, refTop);

                int low0 = 0, med0 = 0, high0 = 0;
                foreach (PathData.Corner c in cs) { if (c.type == PathData.Corner.Type.LowSpeed) low0++; else if (c.type == PathData.Corner.Type.MediumSpeed) med0++; else high0++; }
                int removed = 0, reclassed = 0;
                List<PathData.Corner> drop = new List<PathData.Corner>();
                List<int> covered = new List<int>();
                for (int k = 0; k < cs.Count; k++)
                {
                    PathData.Corner c = cs[k];
                    LineDecision x = dec[k];
                    string turnList = "[" + string.Join(",", x.Turns.ConvertAll<string>(delegate (int t) { return t.ToString(Inv); }).ToArray()) + "]";
                    string was = c.type.ToString().Replace("Speed", "") + " r" + c.radius.ToString("0", Inv);
                    foreach (int t in x.Turns) if (!covered.Contains(t)) covered.Add(t);
                    if (x.Remove)
                    {
                        drop.Add(c);
                        removed++;
                        Main.Log("RT LINE: corner " + (k + 1) + " gates " + c.startGateID + "-" + c.endGateID + " turns " + turnList + " game " + was + " -> removed (" + x.Why + ")");
                        continue;
                    }
                    c.type = x.Type == "High" ? PathData.Corner.Type.HighSpeed : x.Type == "Medium" ? PathData.Corner.Type.MediumSpeed : PathData.Corner.Type.LowSpeed;
                    c.speed = (float)x.Speed;
                    c.radius = (float)x.Radius;
                    reclassed++;
                    Main.Log("RT LINE: corner " + (k + 1) + " gates " + c.startGateID + "-" + c.endGateID + " turns " + turnList + " game " + was + " -> " + x.Type + " " + x.Speed.ToString("0.00", Inv)
                        + " (real " + x.Kmh.ToString("0", Inv) + " km/h, T" + x.By + ")");
                }
                if (removed > 0) RemoveCorners(d, drop);
                int low = 0, med = 0, high = 0;
                foreach (PathData.Corner c in d.corners) { if (c.type == PathData.Corner.Type.LowSpeed) low++; else if (c.type == PathData.Corner.Type.MediumSpeed) med++; else high++; }
                List<string> miss = new List<string>();
                foreach (LineTurn t in turns) if (!covered.Contains(t.Turn)) miss.Add("T" + t.Turn);
                built.Counts["lineCornersRemoved"] = removed;
                built.Counts["lineCornersSet"] = reclassed;
                built.Counts["corners"] = d.corners.Count;
                built.Counts["straights"] = d.straights.Count;
                Main.Log("RT LINE SUMMARY: " + turns.Count + " listed turns, game found " + cs.Count + " corners (" + low0 + " low / " + med0 + " medium / " + high0 + " high); "
                    + removed + " removed, " + reclassed + " set from real speeds (refTop " + refTop.ToString("0", Inv) + " km/h) -> " + d.corners.Count + " corners (" + low + " low / " + med + " medium / " + high
                    + " high), " + d.straights.Count + " straights; turns with no game corner: " + (miss.Count == 0 ? "none" : string.Join(",", miss.ToArray())));
            }
            catch (Exception e)
            {
                Main.Logger.Error("RT LINE: the real corner speeds could not be applied; the game's own rating stays: " + e);
            }
        }

        private static double SAt(LayoutPaths lp, Vector3 p)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < lp.Track.Count; i++)
            {
                float dx = lp.Track[i].x - p.x, dz = lp.Track[i].z - p.z;
                float dd = dx * dx + dz * dz;
                if (dd < bd) { bd = dd; best = i; }
            }
            return lp.S[best];
        }

        /// <summary>
        /// Takes corners out of the path the way PathData.CalculateCorners would never have made them: gates lose the corner and their braking / acceleration type,
        /// ids are reindexed (SimulationLapTimeModel.cs:680-684 needs index == id), and the game's own CalculateStraights / CalculateLockUpZones /
        /// CalculateGateDistanceToBrakingGates run again in the order Prepare calls them (PathData.cs:497-502). Nothing else holds corner references (grep: only gate.corner, straight.nextCorner).
        /// </summary>
        private static void RemoveCorners(PathData d, List<PathData.Corner> drop)
        {
            foreach (PathData.Gate g in d.gates)
            {
                if (g.corner != null && drop.Contains(g.corner))
                {
                    g.corner = null;
                    if (g.gateType == PathData.GateType.BrakingZone || g.gateType == PathData.GateType.AccelerationZone) g.gateType = PathData.GateType.Normal;
                }
            }
            foreach (PathData.Corner c in drop) d.corners.Remove(c);
            for (int i = 0; i < d.corners.Count; i++) { d.corners[i].id = i; d.corners[i].nextStraight = null; }
            foreach (PathData.Gate g in d.gates)
            {
                g.straight = null; g.isSparkGate = false; g.isSpeedTrap = false; g.distanceToCorner = 0f; g.isLockUpGate = false;
            }
            Call(d, "CalculateStraights");
            Call(d, "CalculateLockUpZones");
            Call(d, "CalculateGateDistanceToBrakingGates");
        }

        private static void Call(PathData d, string name)
        {
            System.Reflection.MethodInfo m = AccessTools.Method(typeof(PathData), name);
            if (m == null) throw new Exception("PathData." + name + " not found");
            m.Invoke(d, null);
        }
    }
}
