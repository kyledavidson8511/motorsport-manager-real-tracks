using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace RealTracks
{
    /// <summary>Venue shell settings (WP3a). Every behaviour has a toggle or slider (UMM panel, Ctrl+F10).</summary>
    public partial class Settings
    {
        /// <summary>Master switch: build pack venues into the donor scene at all. Off = a pack circuit just plays as the donor.</summary>
        public bool VenueBuild = true;
        /// <summary>Hide the donor scene's art (renderers only; nothing is destroyed).</summary>
        public bool VenueHideDonorArt = true;
        /// <summary>Generate the road (Env_Track layer + MeshCollider) at the real width.</summary>
        public bool VenueRoad = true;
        /// <summary>Extra visual/physical shoulder per side so the game's permitted racing-line spread remains on the rendered surface.</summary>
        public float VenueRoadAiMarginM = 1.5f;
        /// <summary>Call the art generator (terrain, kerbs, props).</summary>
        public bool VenueCallArt = true;
        /// <summary>Re-point the scene cameras at the venue.</summary>
        public bool VenueCameras = true;
        /// <summary>Slowly orbit the intro / hub cameras instead of holding them still.</summary>
        public bool VenueCameraOrbit = true;
        /// <summary>Orbit speed in degrees per second.</summary>
        public float VenueCameraOrbitSpeed = 4f;
        /// <summary>Total width of the pit lane surface (m).</summary>
        public float VenuePitLaneWidth = 12f;
        /// <summary>Surface the ground in front of the pit boxes and garages (so cars and crews stand on road, not the void).</summary>
        public bool VenuePitApron = true;
        /// <summary>Log the corner list the game derived from the pack's centreline.</summary>
        public bool VenueLogCorners = true;
    }

    /// <summary>
    /// WP3a: the venue shell. When a pack circuit's donor scene is set up, this rewrites the donor's paths from the pack,
    /// moves the 12 garages, turns the donor art off, generates the road (Env_Track + MeshCollider), calls the art generator,
    /// re-points the cameras and the minimap bounds. Stock circuits are never touched (every entry point returns first).
    /// </summary>
    public static partial class VenueBuilder
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Scene instance ids (CircuitScene.GetInstanceID) - this mod's own state only.
        private static int sBuiltSceneId;
        private static int sFailedSceneId;
        private static string sLastSummary = "";
        private static readonly HashSet<int> sStartRan = new HashSet<int>();

        public static void Init() { }

        /// <summary>True when the game runs in 2D mode (the donor's 2D scene loads, so there is nothing to build).
        /// Read from the same preference EventLoadingState.DoCircuitLoad uses, so this file does not depend on WP2's Registration.Running2D.</summary>
        private static bool Is2D()
        {
            if (App.instance == null || App.instance.preferencesManager == null) return false;
            return !App.instance.preferencesManager.GetSettingBool(Preference.pName.Video_3D, true);
        }

        public static void DrawGui(Settings s)
        {
            s.VenueBuild = GUILayout.Toggle(s.VenueBuild, " Build pack venues into the donor scene (off = a pack circuit plays as the plain donor venue)");
            if (!s.VenueBuild) return;
            s.VenueHideDonorArt = GUILayout.Toggle(s.VenueHideDonorArt, " Hide the donor venue's own scenery (renderers only; nothing is deleted)");
            s.VenueRoad = GUILayout.Toggle(s.VenueRoad, " Generate the road at its real width (with collision, so 3D cars follow the surface)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Road edge compatibility shoulder (m per side): " + s.VenueRoadAiMarginM.ToString("0.0", Inv), GUILayout.Width(310));
            s.VenueRoadAiMarginM = GUILayout.HorizontalSlider(s.VenueRoadAiMarginM, 0f, 3f, GUILayout.Width(220));
            GUILayout.EndHorizontal();
            s.VenuePitApron = GUILayout.Toggle(s.VenuePitApron, " Surface the pit apron in front of the pit boxes and garages");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Pit lane width (m): " + s.VenuePitLaneWidth.ToString("0.0", Inv), GUILayout.Width(190));
            s.VenuePitLaneWidth = GUILayout.HorizontalSlider(s.VenuePitLaneWidth, 8f, 20f, GUILayout.Width(220));
            GUILayout.EndHorizontal();
            s.VenueCallArt = GUILayout.Toggle(s.VenueCallArt, " Generate venue art (terrain, kerbs, run-off, props)");
            s.VenueCameras = GUILayout.Toggle(s.VenueCameras, " Re-point the intro, hub and simulating cameras at the venue");
            if (s.VenueCameras)
            {
                s.VenueCameraOrbit = GUILayout.Toggle(s.VenueCameraOrbit, " Slowly orbit those cameras");
                GUILayout.BeginHorizontal();
                GUILayout.Label("Orbit speed (deg/s): " + s.VenueCameraOrbitSpeed.ToString("0.0", Inv), GUILayout.Width(190));
                s.VenueCameraOrbitSpeed = GUILayout.HorizontalSlider(s.VenueCameraOrbitSpeed, 0f, 20f, GUILayout.Width(220));
                GUILayout.EndHorizontal();
            }
            s.VenueLogCorners = GUILayout.Toggle(s.VenueLogCorners, " Log the corner list the game derived from the pack");
            if (!string.IsNullOrEmpty(sLastSummary))
            {
                GUILayout.Label("Last venue build: " + sLastSummary);
            }
        }

        // ------------------------------------------------------------------ entry points (called from VenuePatches)

        /// <summary>
        /// Prefix of CircuitScene.SetTrackLayout. Builds the venue once per scene instance, BEFORE the original runs
        /// (so SetPathIDs, the grid and the pit IDs all see the new paths), and forces the layout slot to the pack's.
        /// Stock circuits: returns immediately.
        /// </summary>
        public static void BeforeSetTrackLayout(CircuitScene scene, ref Circuit.TrackLayout layoutArg)
        {
            PackLayout layout = ActiveVenue.CurrentLayout;
            if (layout == null) { OnStockScene(); return; }
            if (!Main.Enabled || Main.Settings == null || !Main.Settings.VenueBuild || Is2D()) return;
            int id = scene.GetInstanceID();
            if (id == sFailedSceneId) return;
            if (id != sBuiltSceneId)
            {
                if (!Build(scene, layout)) { sFailedSceneId = id; return; }
            }
            Circuit.TrackLayout want = layout.TrackLayout;
            if (layoutArg != want)
            {
                Main.Log("RT VENUE: SetTrackLayout(" + layoutArg + ") forced to " + want + " for " + layout.Pack.Display + " " + layout.Key + ".");
                layoutArg = want;
            }
        }

        /// <summary>Postfix of CircuitScene.SetTrackLayout: repair the ERS zones (SessionManager.SetCircuit computed them from the donor's Track A).</summary>
        public static void AfterSetTrackLayout(CircuitScene scene)
        {
            if (ActiveVenue.CurrentLayout == null) return;
            if (!Main.Enabled || Main.Settings == null || !Main.Settings.VenueBuild || Is2D()) return;
            if (scene.GetInstanceID() != sBuiltSceneId) return;
            RepairErsZones(scene);
        }

        /// <summary>
        /// Postfix of CircuitScene.Start. If Start ran before our build it only records that; if it ran after, it de-duplicates
        /// the MeshColliders it added to our road and re-asserts the collision state.
        /// </summary>
        public static void AfterStart(CircuitScene scene)
        {
            if (ActiveVenue.CurrentLayout == null) return;
            if (!Main.Enabled || Main.Settings == null || !Main.Settings.VenueBuild || Is2D()) return;
            BuiltVenue b = ActiveVenue.Built;
            if (b == null || b.SceneInstanceId != scene.GetInstanceID()) { sStartRan.Add(scene.GetInstanceID()); return; }
            int removed = VenueRoad.DedupeColliders(b);
            VenueRoad.SetCollisionState(scene, b);
            Main.Log("RT VENUE: CircuitScene.Start ran AFTER the build: removed " + removed + " duplicate collider(s); path collision active=" +
                     scene.isPathCollisionMeshActive + ", mask=" + scene.pathCollisionLayerMask + ".");
        }

        /// <summary>Stock scene: only this mod's own statics are reset (never the scene).</summary>
        private static void OnStockScene()
        {
            if (ActiveVenue.Built != null) ActiveVenue.Built = null;
            sBuiltSceneId = 0;
            sFailedSceneId = 0;
        }

        // ------------------------------------------------------------------ the build

        private static bool Build(CircuitScene scene, PackLayout layout)
        {
            Settings st = Main.Settings;
            string name = layout.Pack.Display + " " + layout.Key;
            int sceneId = scene.GetInstanceID();
            bool startRanBefore = sStartRan.Contains(sceneId);

            // ---- 1. parse everything first; a bad pack must fail before the scene is touched.
            LayoutPaths lp;
            try
            {
                lp = layout.Paths;
                PackTerrain terrain = layout.Pack.Terrain;
                List<Feature> features = layout.Pack.Features;
                Main.Log("RT VENUE: parsed " + name + ": " + lp.Track.Count + " track points, " + lp.Garages.Count + " garages, " +
                         (features != null ? features.Count : 0) + " features, terrain " + (terrain != null ? terrain.Nx + "x" + terrain.Nz : "none") + ".");
            }
            catch (Exception e)
            {
                Main.Logger.Error("RT VENUE: " + name + ": pack failed to parse, the donor scene is left untouched: " + e.Message);
                return false;
            }

            ScenePlan plan;
            string problem = VenuePaths.CheckScene(scene, lp, layout, out plan);
            if (problem != null)
            {
                Main.Logger.Error("RT VENUE: " + name + ": " + problem + " - the donor scene is left untouched.");
                return false;
            }

            BuiltVenue built = new BuiltVenue();
            built.Layout = layout;
            built.SceneInstanceId = sceneId;
            built.Counts["startRanBeforeBuild"] = startRanBefore ? 1 : 0;
            ActiveVenue.Built = null; // a half-built venue must never look finished

            try
            {
                // ---- 2a. survey the donor (read only) BEFORE anything changes: renderers to hide, asphalt material, Env_Track meshes.
                DonorSurvey survey = VenueDonor.Survey(scene);
                // Donor road meshes off the Env_Track layer first: PathSpline.SetPositionsToGatePlane raycasts onto that layer during Prepare.
                VenueDonor.TakeOffTrackLayer(survey, built);

                // ---- 2b. paths: track, pit, garages, crash/runwide/cut.
                PathReport report = VenuePaths.Write(scene, layout, lp, plan, built);

                // ---- 3. let the art generator clone donor prefabs while the donor art is still there.
                if (st.VenueCallArt)
                {
                    try { ArtGenerator.HarvestDonor(scene, built); }
                    catch (Exception e) { Warn(built, "ArtGenerator.HarvestDonor failed (the race is unaffected): " + e); }
                }

                // ---- 4. donor art off.
                if (st.VenueHideDonorArt) VenueDonor.HideArt(scene, survey, built);
                else Main.Log("RT VENUE: donor art left visible (VenueHideDonorArt is off).");

                // ---- 5. road.
                if (st.VenueRoad)
                {
                    try { VenueRoad.Build(scene, layout, lp, report.Track, survey.Asphalt, plan, built); }
                    catch (Exception e) { Warn(built, "road generation failed (cars fall back to path height): " + e); }
                }
                VenueRoad.SetCollisionState(scene, built);

                // ---- 6. art (terrain, kerbs, props). Never allowed to break the race.
                if (st.VenueCallArt)
                {
                    try { ArtGenerator.Build(scene, layout, built); }
                    catch (Exception e) { Warn(built, "ArtGenerator.Build failed (the race is unaffected): " + e); }
                }
                VenueRoad.CheckTrackLayerOwnership(scene, built);

                // ---- 7. cameras.
                if (st.VenueCameras)
                {
                    try { VenueCameras.Apply(scene, lp, report.Track, built); }
                    catch (Exception e) { Warn(built, "camera placement failed: " + e); }
                }

                // ---- 8. minimap bounds.
                try { SetMinimapBounds(scene, layout, report, built); }
                catch (Exception e) { Warn(built, "minimap bounds failed: " + e); }

                // ---- 9. publish.
                sBuiltSceneId = sceneId;
                ActiveVenue.Built = built;
                sLastSummary = Summary(layout, report, built);
                Main.Logger.Log("RT VENUE BUILT: " + sLastSummary);
                foreach (string w in built.Warnings) Main.Logger.Warning("RT VENUE WARNING: " + w);
                if (st.VenueLogCorners) LogCorners(report.Track);
                return true;
            }
            catch (Exception e)
            {
                // The scene may be half rewritten; mark it failed so we do not try again, and say so loudly.
                Main.Logger.Error("RT VENUE: build of " + name + " failed part-way (the scene may be inconsistent): " + e);
                return false;
            }
        }

        internal static void Warn(BuiltVenue built, string msg)
        {
            built.Warnings.Add(msg);
        }

        private static void RepairErsZones(CircuitScene scene)
        {
            try
            {
                PersistentEventData ped = Game.instance != null ? Game.instance.persistentEventData : null;
                if (ped == null) return;
                List<PathData.Straight> straights = new List<PathData.Straight>(scene.GetTrackPath().data.straights);
                straights.Sort(delegate (PathData.Straight a, PathData.Straight b) { return b.length.CompareTo(a.length); });
                List<int> ids = ped.ersStartGateIds;
                ids.Clear();
                for (int i = 0; i < Mathf.Min(3, straights.Count); i++) ids.Add(straights[i].startGateID);
                Main.Log("RT VENUE: ERS zone start gates recomputed from the pack's straights: " + ids.Count + " zone(s).");
            }
            catch (Exception e)
            {
                Main.Logger.Error("RT VENUE: ERS zone repair failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ minimap

        private static void SetMinimapBounds(CircuitScene scene, PackLayout layout, PathReport rep, BuiltVenue built)
        {
            Rect r = layout.MinimapBounds; // pack-local x/z = world x/z (identity frame)
            BoxCollider bc = scene.trackBounds;
            if (bc == null)
            {
                Warn(built, "CircuitScene.trackBounds is null; minimap bounds not set.");
                return;
            }
            Transform tr = bc.transform;
            // Axis-aligned world box: remove any rotation, then size/centre from the transform
            // (collider.bounds reads zero while the scene root is inactive, as the spike found).
            tr.rotation = Quaternion.identity;
            Vector3 ls = tr.lossyScale;
            float sx = Mathf.Abs(ls.x) < 1e-4f ? 1f : Mathf.Abs(ls.x);
            float sy = Mathf.Abs(ls.y) < 1e-4f ? 1f : Mathf.Abs(ls.y);
            float sz = Mathf.Abs(ls.z) < 1e-4f ? 1f : Mathf.Abs(ls.z);
            Vector3 worldCentre = new Vector3(r.x + r.width * 0.5f, rep.MidY, r.y + r.height * 0.5f);
            bc.center = tr.InverseTransformPoint(worldCentre);
            bc.size = new Vector3(r.width / sx, 10f / sy, r.height / sz);

            Vector3 c2 = tr.TransformPoint(bc.center);
            Vector3 sz2 = new Vector3(bc.size.x * sx, bc.size.y * sy, bc.size.z * sz);
            built.MinimapWorldBounds = new Rect(c2.x - sz2.x * 0.5f, c2.z - sz2.z * 0.5f, sz2.x, sz2.z);
            bool ok = Mathf.Abs(built.MinimapWorldBounds.x - r.x) < 0.05f && Mathf.Abs(built.MinimapWorldBounds.y - r.y) < 0.05f &&
                      Mathf.Abs(built.MinimapWorldBounds.width - r.width) < 0.05f && Mathf.Abs(built.MinimapWorldBounds.height - r.height) < 0.05f;
            Main.Log(string.Format(Inv, "RT VENUE MINIMAP: world x {0:0.0}..{1:0.0}, z {2:0.0}..{3:0.0} (wanted x {4:0.0}..{5:0.0}, z {6:0.0}..{7:0.0}) {8}.",
                built.MinimapWorldBounds.xMin, built.MinimapWorldBounds.xMax, built.MinimapWorldBounds.yMin, built.MinimapWorldBounds.yMax,
                r.xMin, r.xMax, r.yMin, r.yMax, ok ? "OK" : "MISMATCH"));
            if (!ok) Warn(built, "minimap bounds do not match the layout's rectangle.");
        }

        // ------------------------------------------------------------------ logging

        private static string Summary(PackLayout layout, PathReport rep, BuiltVenue built)
        {
            PathData d = rep.Track;
            int low = 0, med = 0, high = 0;
            foreach (PathData.Corner c in d.corners)
            {
                if (c.type == PathData.Corner.Type.LowSpeed) low++;
                else if (c.type == PathData.Corner.Type.MediumSpeed) med++;
                else high++;
            }
            float longest = 0f;
            foreach (PathData.Straight s in d.straights) longest = Mathf.Max(longest, s.length);
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat(Inv, "{0} {1} ({2}): {3:0} m lap, {4} gates, {5} corners ({6} low / {7} medium / {8} high), {9} straights (longest {10:0} m), height range {11:0.0} m; ",
                layout.Pack.Display, layout.Letter, layout.Name, d.length, d.gates.Count, d.corners.Count, low, med, high, d.straights.Count, longest, rep.MaxY - rep.MinY);
            sb.Append("counts {");
            bool first = true;
            List<string> keys = new List<string>(built.Counts.Keys);
            keys.Sort();
            foreach (string k in keys)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(k).Append('=').Append(built.Counts[k]);
            }
            sb.Append("}; ").Append(built.Warnings.Count).Append(" warning(s).");
            return sb.ToString();
        }

        private static void LogCorners(PathData d)
        {
            StringBuilder sb = new StringBuilder("RT VENUE CORNERS:");
            foreach (PathData.Corner c in d.corners)
            {
                PathData.Gate g = d.gates[c.startGateID];
                sb.AppendFormat(Inv, " #{0}@{1:0}m {2} r{3:0} {4};", c.id + 1, g.id * 15f, c.type.ToString().Replace("Speed", ""), c.radius, c.direction);
            }
            Main.Logger.Log(sb.ToString());
        }
    }

    /// <summary>Read-only facts gathered from the scene before any change.</summary>
    public class ScenePlan
    {
        public PathDataComponent[] TrackSlots;
        public int Slot;
        public TeamPitGarage[] Garages;
        public Transform GarageRoot;
    }

    /// <summary>What the path writer produced (for the road, cameras, minimap and the summary).</summary>
    public class PathReport
    {
        public PathData Track;
        public float MinY, MaxY, MidY;
        public float GarageEntryAngleMin = 999f, GarageEntryAngleMax = -1f;
        public float GarageLaneGapMax, GarageChainGapMax;
        public int GaragesSwappedByValidate;
    }
}
