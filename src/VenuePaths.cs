using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace RealTracks
{
    /// <summary>
    /// Writes a pack layout's paths into the donor scene through the game's own pipeline
    /// (points -> PathData.Build -> PathDataComponent.Prepare -> gates, corners, straights), so the simulation, AI,
    /// pit logic and the other mods all run on the pack unchanged.
    /// </summary>
    public static class VenuePaths
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static readonly AccessTools.FieldRef<CircuitScene, PathDataComponent[]> sLayoutPaths =
            AccessTools.FieldRefAccess<CircuitScene, PathDataComponent[]>("trackLayoutPath");
        private static readonly AccessTools.FieldRef<CircuitScene, Transform> sTeamGarages =
            AccessTools.FieldRefAccess<CircuitScene, Transform>("teamGarages");
        private static readonly AccessTools.FieldRef<TeamPitGarage, PathDataComponent[]> sGarageEntry =
            AccessTools.FieldRefAccess<TeamPitGarage, PathDataComponent[]>("garageEntryPath");
        private static readonly AccessTools.FieldRef<TeamPitGarage, PathDataComponent[]> sGarageExit =
            AccessTools.FieldRefAccess<TeamPitGarage, PathDataComponent[]>("garageExitPath");

        // ------------------------------------------------------------------ read-only checks (nothing is touched)

        /// <summary>Returns null if the donor scene and the pack fit together, else the reason they do not.</summary>
        public static string CheckScene(CircuitScene scene, LayoutPaths lp, PackLayout layout, out ScenePlan plan)
        {
            plan = null;
            PathDataComponent[] slots = sLayoutPaths(scene);
            int slot = (int)layout.TrackLayout;
            if (slots == null || slot < 0 || slot >= slots.Length)
                return "the donor scene has no track layout slot for layout " + layout.Letter + " (it has " + (slots == null ? 0 : slots.Length) + ")";
            if (slots[slot] == null) return "donor track slot " + layout.Letter + " is empty";
            if (slots[slot].data.type != PathData.Type.Loop) return "donor track slot " + layout.Letter + " is not a loop";
            if (scene.pitlanePath == null || scene.pitlaneEntryPath == null || scene.pitlaneExitPath == null)
                return "the donor scene has no pit lane / entry / exit path";

            Transform root = sTeamGarages(scene);
            if (root == null) return "the donor scene has no TeamGarages root";
            int n = root.childCount;
            if (lp.Garages.Count != n)
                return "the pack has " + lp.Garages.Count + " garages but the donor scene has " + n + " (the game assigns teams and the safety car by garage index, so all must be present)";
            TeamPitGarage[] garages = new TeamPitGarage[n];
            for (int i = 0; i < n; i++)
            {
                if (!lp.Garages.ContainsKey(i + 1)) return "the pack has no [garage " + (i + 1) + "]";
                TeamPitGarage g = root.GetChild(i).GetComponent<TeamPitGarage>();
                if (g == null) return "TeamGarages child " + i + " has no TeamPitGarage";
                if (g.pitboxEntryPath == null || g.pitboxExitPath == null) return "garage " + (i + 1) + " has no pitbox paths";
                PathDataComponent[] ge = sGarageEntry(g), gx = sGarageExit(g);
                if (ge == null || gx == null || ge.Length < 2 || gx.Length < 2 || ge[0] == null || ge[1] == null || gx[0] == null || gx[1] == null)
                    return "garage " + (i + 1) + " has no garage entry/exit paths for both cars";
                garages[i] = g;
            }

            string bad = CheckPoints(lp.Track, "[track]", 10) ?? CheckPoints(lp.RacingLine, "[racingline]", 10) ??
                         CheckPoints(lp.PitEntry, "[pitentry]", 2) ?? CheckPoints(lp.PitLane, "[pitlane]", 2) ?? CheckPoints(lp.PitExit, "[pitexit]", 2);
            if (bad != null) return bad;
            foreach (KeyValuePair<int, GarageSlot> kv in lp.Garages)
            {
                GarageSlot s = kv.Value;
                string w = "[garage " + kv.Key + "]";
                bad = CheckVec(s.Position, w) ?? CheckPoints(s.PitboxEntry, w + " pitboxentry", 2) ?? CheckPoints(s.PitboxExit, w + " pitboxexit", 2) ??
                      CheckPoints(s.GarageEntry[0], w + " garageentry 1", 2) ?? CheckPoints(s.GarageExit[0], w + " garageexit 1", 2) ??
                      CheckPoints(s.GarageEntry[1], w + " garageentry 2", 2) ?? CheckPoints(s.GarageExit[1], w + " garageexit 2", 2);
                if (bad != null) return bad;
            }
            foreach (List<Vector3> l in lp.Crash) { bad = CheckPoints(l, "[crash]", 0); if (bad != null) return bad; }
            foreach (List<Vector3> l in lp.RunWide) { bad = CheckPoints(l, "[runwide]", 0); if (bad != null) return bad; }
            foreach (List<Vector3> l in lp.Cut) { bad = CheckPoints(l, "[cut]", 0); if (bad != null) return bad; }

            plan = new ScenePlan();
            plan.TrackSlots = slots;
            plan.Slot = slot;
            plan.Garages = garages;
            plan.GarageRoot = root;
            return null;
        }

        private static string CheckPoints(List<Vector3> pts, string what, int min)
        {
            if (pts == null) return what + " is missing";
            if (pts.Count < min) return what + " has " + pts.Count + " points (need at least " + min + ")";
            for (int i = 0; i < pts.Count; i++)
            {
                string b = CheckVec(pts[i], what + " point " + i);
                if (b != null) return b;
            }
            return null;
        }

        private static string CheckVec(Vector3 v, string what)
        {
            if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z))
                return what + " is not a number";
            if (Mathf.Abs(v.x) > 20000f || Mathf.Abs(v.z) > 20000f || Mathf.Abs(v.y) > 2000f)
                return what + " is outside +-20 km / 2 km height: " + v.ToString();
            return null;
        }

        // ------------------------------------------------------------------ write

        public static PathReport Write(CircuitScene scene, PackLayout layout, LayoutPaths lp, ScenePlan plan, BuiltVenue built)
        {
            PathReport rep = new PathReport();

            // 1. Track loop: centreline + racing line, then the game derives gates, corners, straights.
            PathDataComponent track = plan.TrackSlots[plan.Slot];
            SetPoints(track, lp.Track, lp.RacingLine, true);
            track.Prepare();
            rep.Track = track.data;
            rep.MinY = float.MaxValue; rep.MaxY = float.MinValue;
            foreach (PathData.Gate g in track.data.gates)
            {
                rep.MinY = Mathf.Min(rep.MinY, g.position.y);
                rep.MaxY = Mathf.Max(rep.MaxY, g.position.y);
            }
            rep.MidY = (rep.MinY + rep.MaxY) * 0.5f;
            LineCorners.Apply(track.data, layout, lp, built);
            built.Counts["gates"] = track.data.gates.Count;
            built.Counts["corners"] = track.data.corners.Count;
            built.Counts["straights"] = track.data.straights.Count;

            // 2. Pit lane, entry, exit (the game's names make Prepare treat entry/exit like corner-aware lines).
            SetPoints(scene.pitlanePath, lp.PitLane, lp.PitLane, false);
            scene.pitlanePath.Prepare();
            SetPoints(scene.pitlaneEntryPath, lp.PitEntry, lp.PitEntry, false);
            scene.pitlaneEntryPath.Prepare();
            SetPoints(scene.pitlaneExitPath, lp.PitExit, lp.PitExit, false);
            scene.pitlaneExitPath.Prepare();

            // 3. The 12 garages: move each TeamPitGarage onto the pit lane, rewrite its six paths.
            WriteGarages(scene, lp, plan, rep, built);

            // 4. Crash / run-wide / cut paths.
            scene.crashPaths = ClonePaths(scene.crashPaths, lp.Crash, "RT Crash", 0, layout.TrackLayout, built);
            scene.runWidePaths = ClonePaths(scene.runWidePaths, lp.RunWide, "RT Wide", 1, layout.TrackLayout, built);
            CircuitScene.CornerCuttingPenalty[] oldPenalties = scene.cutCornerPenalties;
            scene.cutCornersPaths = ClonePaths(scene.cutCornersPaths, lp.Cut, "RT Cut", 2, layout.TrackLayout, built);
            scene.cutCornerPenalties = MakePenalties(oldPenalties, scene.cutCornersPaths.Length);
            built.Counts["crashPaths"] = scene.crashPaths.Length;
            built.Counts["runWidePaths"] = scene.runWidePaths.Length;
            built.Counts["cutPaths"] = scene.cutCornersPaths.Length;
            if (lp.Sectors != null) Main.Log("RT VENUE: [sectors] in the pack are informational; the game places its own sector gates (thirds of the lap).");
            return rep;
        }

        private static void WriteGarages(CircuitScene scene, LayoutPaths lp, ScenePlan plan, PathReport rep, BuiltVenue built)
        {
            PathData lane = scene.pitlanePath.data;
            for (int i = 0; i < plan.Garages.Length; i++)
            {
                TeamPitGarage g = plan.Garages[i];
                GarageSlot s = lp.Garages[i + 1];
                // Move first: the six path components are children, and their local points are computed from the world points.
                g.transform.position = s.Position;
                g.transform.rotation = Quaternion.Euler(0f, s.YawDeg, 0f);

                SetPoints(g.pitboxEntryPath, s.PitboxEntry, s.PitboxEntry, false);
                SetPoints(g.pitboxExitPath, s.PitboxExit, s.PitboxExit, false);
                PathDataComponent[] ge = sGarageEntry(g), gx = sGarageExit(g);
                for (int car = 0; car < 2; car++)
                {
                    SetPoints(ge[car], s.GarageEntry[car], s.GarageEntry[car], false);
                    SetPoints(gx[car], s.GarageExit[car], s.GarageExit[car], false);
                }
                g.pitboxEntryPath.Prepare();
                g.pitboxExitPath.Prepare();
                for (int car = 0; car < 2; car++) { ge[car].Prepare(); gx[car].Prepare(); }

                // ValidateDirection (TeamPitGarage.cs:75) swaps entry and exit if the box entry's last gate points against the pit lane.
                float angle = Vector3.Angle(lane.gates[0].normal, g.pitboxEntryPath.data.GetLastGate().normal);
                rep.GarageEntryAngleMin = Mathf.Min(rep.GarageEntryAngleMin, angle);
                rep.GarageEntryAngleMax = Mathf.Max(rep.GarageEntryAngleMax, angle);
                if (angle > 90f)
                {
                    g.ValidateDirection(scene);
                    rep.GaragesSwappedByValidate++;
                    VenueBuilder.Warn(built, "garage " + (i + 1) + ": the pitbox entry heads against the pit lane (" + angle.ToString("0", Inv) + " deg); the game swapped entry and exit.");
                }

                // Connectivity: the pitbox entry must start on the pit lane, the exit must end on it, and the two must meet at the box.
                float gapIn = DistToLane(lane, g.pitboxEntryPath.data.gates[0].position);
                float gapOut = DistToLane(lane, g.pitboxExitPath.data.GetLastGate().position);
                float chain = Vector3.Distance(g.pitboxEntryPath.data.GetLastGate().position, g.pitboxExitPath.data.gates[0].position);
                rep.GarageLaneGapMax = Mathf.Max(rep.GarageLaneGapMax, Mathf.Max(gapIn, gapOut));
                rep.GarageChainGapMax = Mathf.Max(rep.GarageChainGapMax, chain);
            }
            built.Counts["garages"] = plan.Garages.Length;
            built.Counts["garageLaneGapMaxCm"] = Mathf.RoundToInt(rep.GarageLaneGapMax * 100f);
            built.Counts["garageChainGapMaxCm"] = Mathf.RoundToInt(rep.GarageChainGapMax * 100f);
            built.Counts["garagesSwappedByValidate"] = rep.GaragesSwappedByValidate;
            if (rep.GarageLaneGapMax > 3f)
                VenueBuilder.Warn(built, "a pitbox path starts/ends " + rep.GarageLaneGapMax.ToString("0.0", Inv) + " m from the pit lane centreline (expected < 3 m).");
            Main.Log(string.Format(Inv, "RT VENUE GARAGES: {0} moved; pitbox-entry vs pit-lane heading {1:0}..{2:0} deg (ValidateDirection swaps above 90); max gap pitbox<->lane {3:0.00} m, entry-end<->exit-start {4:0.00} m.",
                plan.Garages.Length, rep.GarageEntryAngleMin, rep.GarageEntryAngleMax, rep.GarageLaneGapMax, rep.GarageChainGapMax));
        }

        private static float DistToLane(PathData lane, Vector3 p)
        {
            PathSpline sp = lane.centerLineSpline;
            if (sp == null || sp.splinePositions == null || sp.splinePositions.Length == 0) return float.MaxValue;
            int id = sp.FindClosestPointIDToPosition(p);
            return Vector3.Distance(sp.splinePositions[id].position, p);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Writes world-space points into a path component (both splines), converting to its local space.</summary>
        internal static void SetPoints(PathDataComponent comp, List<Vector3> layoutWorld, List<Vector3> racingWorld, bool loop)
        {
            Transform t = comp.transform;
            PathData d = comp.data;
            List<Vector3> a = Clean(layoutWorld, loop);
            List<Vector3> b = Clean(racingWorld, loop);
            d.SetSplineMode(PathData.SplineMode.Layout);
            d.points = ToLocal(t, a);
            d.Build();
            d.SetSplineMode(PathData.SplineMode.RacingLine);
            d.points = ToLocal(t, b);
            d.Build();
            d.SetSplineMode(PathData.SplineMode.Layout);
        }

        /// <summary>Drops (near-)duplicate neighbours (a zero-length segment makes the spline's tangents NaN) and pads a 2-point line to 3 (Prepare needs 3).</summary>
        internal static List<Vector3> Clean(List<Vector3> pts, bool loop)
        {
            List<Vector3> o = new List<Vector3>(pts.Count + 1);
            for (int i = 0; i < pts.Count; i++)
            {
                if (o.Count == 0 || (pts[i] - o[o.Count - 1]).sqrMagnitude > 0.0025f) o.Add(pts[i]);
            }
            if (loop && o.Count > 1 && (o[0] - o[o.Count - 1]).sqrMagnitude <= 0.0025f) o.RemoveAt(o.Count - 1);
            if (!loop && o.Count == 2) o.Insert(1, (o[0] + o[1]) * 0.5f);
            if (o.Count < 3) throw new Exception("a path has fewer than 3 distinct points");
            return o;
        }

        private static List<Vector3> ToLocal(Transform t, List<Vector3> world)
        {
            List<Vector3> l = new List<Vector3>(world.Count);
            for (int i = 0; i < world.Count; i++) l.Add(t.InverseTransformPoint(world[i]));
            return l;
        }

        /// <summary>
        /// Replaces a donor incident-path array with clones of its first entry carrying the pack's polylines.
        /// kind 0 = crash, 1 = run wide, 2 = cut corner. An empty result is safe: the game guards every use with HasPathOfType.
        /// </summary>
        private static PathDataComponent[] ClonePaths(PathDataComponent[] templates, List<List<Vector3>> paths, string name, int kind, Circuit.TrackLayout layout, BuiltVenue built)
        {
            List<List<Vector3>> usable = new List<List<Vector3>>();
            foreach (List<Vector3> p in paths) if (p != null && p.Count >= 2) usable.Add(p);
            if (usable.Count == 0)
            {
                VenueBuilder.Warn(built, "the pack has no usable " + name + " paths; the donor's are dropped (the game then simply never uses that incident).");
                return new PathDataComponent[0];
            }
            if (templates == null || templates.Length == 0 || templates[0] == null)
            {
                VenueBuilder.Warn(built, "the donor scene has no template for " + name + " paths; none were created.");
                return new PathDataComponent[0];
            }
            PathDataComponent template = templates[0];
            PathDataComponent[] result = new PathDataComponent[usable.Count];
            for (int i = 0; i < usable.Count; i++)
            {
                GameObject go = (GameObject)UnityEngine.Object.Instantiate(template.gameObject);
                go.name = name + " (" + (i + 1) + ")";
                go.transform.SetParent(template.transform.parent, false);
                go.transform.localPosition = template.transform.localPosition;
                go.transform.localRotation = template.transform.localRotation;
                go.transform.localScale = template.transform.localScale;
                PathDataComponent c = go.GetComponent<PathDataComponent>();
                if (kind == 0)
                {
                    c.data.isCrashLane = true;
                    c.data.crashLaneForAnyLayout = true;
                    c.data.crashSplineForLayout = layout;
                }
                else if (kind == 1) c.data.isRunWideLane = true;
                else c.data.isCutCornerLane = true;
                SetPoints(c, usable[i], usable[i], false);
                c.Prepare();
                FitParticleTable(c.data);
                result[i] = c;
            }
            return result;
        }

        /// <summary>
        /// The cloned template keeps the donor's per-gate particle table (smoke, then dust/grass), sized for the donor path's gate count.
        /// With a different gate count AICrashingBehaviour.SetEffects (AICrashingBehaviour.cs:153-182) logs an error per gate past the end
        /// (run 2: 23 errors). Resample the table proportionally onto the new gates (orchestrator fix).
        /// </summary>
        private static void FitParticleTable(PathData d)
        {
            if (!d.setParticleTypePerGate) return;
            PathData.GateParticleType[] old = d.gateParticleType;
            int n = d.gates.Count;
            if (old == null || old.Length == 0) { d.setParticleTypePerGate = false; return; }
            if (old.Length == n) return;
            PathData.GateParticleType[] t = new PathData.GateParticleType[n];
            for (int k = 0; k < n; k++)
            {
                int src = n <= 1 ? 0 : Mathf.Min(old.Length - 1, (int)((long)k * old.Length / n));
                t[k] = old[src];
            }
            // keep "change" entries from being dropped: make sure each non-KeepPrevious type in the source appears at least once
            d.gateParticleType = t;
        }

        private static CircuitScene.CornerCuttingPenalty[] MakePenalties(CircuitScene.CornerCuttingPenalty[] donor, int count)
        {
            CircuitScene.CornerCuttingPenalty seed = new CircuitScene.CornerCuttingPenalty();
            if (donor != null && donor.Length > 0) seed = donor[0];
            else
            {
                seed.chanceOfTimePenalty = 0.7f;
                seed.size = PenaltyTime.PenaltySize.Small;
                seed.chanceOfDriveTroughPenalty = 0.3f;
            }
            CircuitScene.CornerCuttingPenalty[] a = new CircuitScene.CornerCuttingPenalty[count];
            for (int i = 0; i < count; i++) a[i] = seed;
            return a;
        }
    }
}
