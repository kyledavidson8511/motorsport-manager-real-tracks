using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using RealTracks.ArtGeo;

namespace RealTracks
{
    public partial class Settings
    {
        public bool ArtTerrain = true;
        public bool ArtOrtho = true;
        public bool ArtSkirt = true;
        public bool ArtKerbs = true;
        public bool ArtRunoff = true;
        public bool ArtGrandstands = true;
        public bool ArtBuildings = true;
        public bool ArtTrees = true;
        /// <summary>0 = no trees, 1 = dense (about 6.5 m between trees in woods).</summary>
        public float ArtTreeDensity = 0.6f;
        public bool ArtBarriers = true;
        public bool ArtWater = true;
        public bool ArtTower = true;
        public bool ArtParking = true;
        /// <summary>Reuse donor (Phoenix) grandstands, barriers and tree clusters when they were harvestable.</summary>
        public bool ArtDonorProps = true;

        // ---- M1.1 (package C) fixes; each can be switched off to get the M1 behaviour back
        /// <summary>Hub-view edge: big skirt whose colour fades from the photo's edge colour to a countryside tan (off = the M1 flat 6 km ring).</summary>
        public bool ArtFixHorizonFade = true;
        public float ArtSkirtRadiusKm = 25f;
        public float ArtHorizonFadeM = 2500f;
        public float ArtHorizonR = 0.60f, ArtHorizonG = 0.50f, ArtHorizonB = 0.36f;
        // ---- M1.1 (package G): skirt rays and the hard far edge
        /// <summary>Arc length (m) of the circular smoothing of the photo-edge colour around the perimeter; kills the radial streaks (0 = off).</summary>
        public float ArtSkirtSmoothM = 1200f;
        /// <summary>Distance (m) by which every perimeter segment has blended to the mean edge colour (0 = never, the old rays).</summary>
        public float ArtSkirtMeanByM = 1500f;
        /// <summary>Fade the outer skirt to a pale sky-haze colour and lift the far rim so it dissolves into the sky instead of ending in a hard line.</summary>
        public bool ArtHorizonFarBlend = true;
        /// <summary>Albedo of the haze colour at the rim (the lit result is about 1.5x brighter: tuned to the horizon sky of the donor scene).</summary>
        public float ArtHorizonFarR = 0.56f, ArtHorizonFarG = 0.58f, ArtHorizonFarB = 0.60f;
        /// <summary>Height (m) the far rim is lifted at the radius (quadratic from 6 km out) so high cameras see it at or above the horizon.</summary>
        public float ArtHorizonRimRiseM = 350f;
        /// <summary>Raise the far clip of the donor scene's cameras so the skirt reaches the horizon (they were 10 km).</summary>
        public bool ArtHorizonFarClip = true;
        /// <summary>Stand facing by the "long side nearest the track" rule; stands that do not face the track become solid blocks (off = M1 heuristic).</summary>
        public bool ArtFixStandFacing = true;
        /// <summary>Use Phoenix's own stadium meshes for stands. Off by default: their wings stretch into slabs across the track.</summary>
        public bool ArtDonorStands = false;
        /// <summary>Height of a mapped stand that is turned into a solid block (m) when it has no height tag.</summary>
        public float ArtBlockHeight = 7f;
        /// <summary>Procedural pit building behind the garages (off = M1: the mapped footprint, skipped because it overlaps the lane).</summary>
        public bool ArtFixPitBuilding = true;
        public bool ArtBuildingsOverTunnel = true;       // w10: a building may stand over a tunnel (footprint over the tunnel corridor is not "on the road")
        /// <summary>Plain tower column: drop the mapped window-banded building under the tower.</summary>
        public bool ArtFixTower = true;
        /// <summary>Give generated mapped buildings deterministic facade and roof variants (off = the original single material).</summary>
        public bool ArtBuildingVariety = true;
        /// <summary>0 = nearly stock; 1 = use every generated facade and roof family where the building geometry permits.</summary>
        public float ArtBuildingVarietyAmount = 0.75f;
        /// <summary>Use Monaco's warm cream, muted ochre and tile palette rather than the generic city material families.</summary>
        public bool ArtMonacoPalette = true;
        /// <summary>Give Monaco's hub-view horizon a continuous coastal blue-grey treatment rather than the generic countryside skirt.</summary>
        public bool ArtMonacoCoastalHorizon = true;
        /// <summary>Use a compact, low Monaco quay-garage treatment for the mapped pit building.</summary>
        public bool ArtMonacoPitBuilding = true;
        /// <summary>Replace the generic red-white-blue fallback run-off with Monaco's muted tarmac apron treatment.</summary>
        public bool ArtMonacoRunoffStyle = true;
        /// <summary>Place continuous concrete safety walls directly on both road edges through Monaco's Grand Hotel hairpin.</summary>
        public bool ArtMonacoHairpinSafetyWalls = true;
        /// <summary>Visible lateral space between Monaco's generated drivable shoulder and the Turn 9 wall, for the rendered car body.</summary>
        public float ArtMonacoHairpinWallClearanceM = 2.1f;
    }

    /// <summary>
    /// Generates the venue art of a pack circuit inside the donor scene: terrain with the orthophoto, horizon skirt, kerbs,
    /// painted run-off, grandstands, buildings, tower, barriers, trees, water and car parks. Geometry maths lives in
    /// ArtGeometry.cs / ArtShapes.cs (Unity-free, unit-tested); this file turns it into scene objects.
    /// </summary>
    public static class ArtGenerator
    {
        public static void HarvestDonor(CircuitScene scene, BuiltVenue built)
        {
            try
            {
                ArtMaterials.Reset();
                DonorProps.Harvest(scene, built);
                DonorHarbour.Harvest(scene, built);      // donor-profile checks, water material and yacht prototypes (ArtHarbour.cs)
            }
            catch (Exception e)
            {
                Main.Logger.Error("RT ART: donor harvest failed: " + e);
                RtFallbacks.Note("art", "donor harvest failed, generated props only: " + e.Message);
                if (built != null) built.Warnings.Add("art: donor harvest failed: " + e.Message);
            }
        }

        public static void Build(CircuitScene scene, PackLayout layout, BuiltVenue built)
        {
            Stopwatch total = Stopwatch.StartNew();
            ArtBuild b = null;
            try
            {
                if (DonorProps.SceneId != scene.GetInstanceID()) { ArtMaterials.Reset(); DonorProps.Clear(); }
                b = new ArtBuild(scene, layout, built, Main.Settings);
            }
            catch (Exception e)
            {
                Main.Logger.Error("RT ART: setup failed: " + e);
                RtFallbacks.Note("art", "setup failed, no generated art at this venue: " + e.Message);
                built.Warnings.Add("art: setup failed: " + e.Message);
                return;
            }
            b.Stage("terrain", Main.Settings.ArtTerrain, b.StageTerrain);
            b.Stage("skirt", Main.Settings.ArtSkirt, b.StageSkirt);
            b.Stage("kerbs", Main.Settings.ArtKerbs, b.StageKerbs);
            b.Stage("runoff", Main.Settings.ArtRunoff, b.StageRunoff);
            b.Stage("grandstands", Main.Settings.ArtGrandstands, b.StageStands);
            b.Stage("buildings", Main.Settings.ArtBuildings, b.StageBuildings);
            b.Stage("tower", Main.Settings.ArtTower, b.StageTower);
            b.Stage("tunnels", Main.Settings.ArtTunnels, b.StageTunnels);
            b.Stage("barriers", Main.Settings.ArtBarriers, b.StageBarriers);
            b.Stage("water", Main.Settings.ArtWater, b.StageWater);
            b.Stage("harbour", Main.Settings.HarbourWater || Main.Settings.HarbourYachts, b.StageHarbour);
            b.Stage("parking", Main.Settings.ArtParking, b.StageParking);
            b.Stage("trees", Main.Settings.ArtTrees && Main.Settings.ArtTreeDensity > 0.001f, b.StageTrees);
            b.Stage("combine", true, b.StageFlush);
            if (b.HasHarbour) b.Stage("water check", true, b.StageWaterCheck);   // M2 w11: every visible water surface with world y (harbour packs only)
            b.Finish(total.ElapsedMilliseconds);
        }

        public static void DrawGui(Settings s)
        {
            s.ArtTerrain = GUILayout.Toggle(s.ArtTerrain, " Terrain from the pack heightfield");
            s.ArtOrtho = GUILayout.Toggle(s.ArtOrtho, " Drape the orthophoto on the terrain (off = plain ground colour)");
            s.ArtSkirt = GUILayout.Toggle(s.ArtSkirt, " Horizon skirt (flat ring so there is no void past the terrain)");
            s.ArtKerbs = GUILayout.Toggle(s.ArtKerbs, " Red/white kerbs in the corners");
            s.ArtRunoff = GUILayout.Toggle(s.ArtRunoff, " Painted run-off (red-white-blue at the first big corner, tarmac where mapped)");
            s.ArtGrandstands = GUILayout.Toggle(s.ArtGrandstands, " Grandstands");
            s.ArtBuildings = GUILayout.Toggle(s.ArtBuildings, " Buildings and pit building");
            s.ArtTower = GUILayout.Toggle(s.ArtTower, " Observation tower");
            s.ArtBarriers = GUILayout.Toggle(s.ArtBarriers, " Barriers and fences");
            s.ArtWater = GUILayout.Toggle(s.ArtWater, " Water");
            s.HarbourWater = GUILayout.Toggle(s.HarbourWater, " Harbour water (sea at the pack's water level)");
            s.HarbourUseDonorWaterMaterial = GUILayout.Toggle(s.HarbourUseDonorWaterMaterial, "   Use donor animated water material (experimental; may float above the harbour)");
            s.HarbourSeaToHorizon = GUILayout.Toggle(s.HarbourSeaToHorizon, "   Sea to the horizon (skirt over the sea on the sea bed, one water level out to the skirt edge)");
            s.HarbourSeaEdgeClamp = GUILayout.Toggle(s.HarbourSeaEdgeClamp, "   Lower the terrain's outer row over the sea to the sea bed (no thin edge above the water)");
            Slider("   Jetty gap closed along the sea edge (m, 0 = off)", ref s.HarbourSeaGapM, 0f, 500f, "0");
            s.HarbourYachts = GUILayout.Toggle(s.HarbourYachts, " Harbour yachts (donor yachts moored at the pack's berths)");
            Slider("   Yacht density", ref s.HarbourYachtDensity, 0f, 1f, "0.00");
            Slider("   Sailboat share (small berths only)", ref s.HarbourSailShare, 0f, 1f, "0.00");
            Slider("   Yacht size variety", ref s.HarbourYachtScaleJitter, 0f, 0.3f, "0.00");
            s.HarbourPontoons = GUILayout.Toggle(s.HarbourPontoons, " Harbour pontoons (flat slabs along the pack's mapped pontoons)");
            s.ArtParking = GUILayout.Toggle(s.ArtParking, " Car parks");
            s.ArtTrees = GUILayout.Toggle(s.ArtTrees, " Trees");
            GUILayout.BeginHorizontal();
            GUILayout.Label("   Tree density " + s.ArtTreeDensity.ToString("0.00"), GUILayout.ExpandWidth(false));
            s.ArtTreeDensity = GUILayout.HorizontalSlider(s.ArtTreeDensity, 0f, 1f, GUILayout.Width(220));
            GUILayout.EndHorizontal();
            s.ArtDonorProps = GUILayout.Toggle(s.ArtDonorProps, " Reuse donor-scene grandstands / barriers / tree clusters when available");
            GUILayout.Space(6);
            GUILayout.Label("M1.1 venue fixes (pack circuits only)");
            s.ArtFixHorizonFade = GUILayout.Toggle(s.ArtFixHorizonFade, " Horizon fade: large skirt fading from the photo edge to countryside tan, far clip raised");
            Slider("   Skirt radius (km)", ref s.ArtSkirtRadiusKm, 5f, 60f, "0");
            Slider("   Fade distance (m)", ref s.ArtHorizonFadeM, 300f, 8000f, "0");
            Slider("   Horizon tan red", ref s.ArtHorizonR, 0f, 1f, "0.00");
            Slider("   Horizon tan green", ref s.ArtHorizonG, 0f, 1f, "0.00");
            Slider("   Horizon tan blue", ref s.ArtHorizonB, 0f, 1f, "0.00");
            Slider("   Skirt colour smoothing arc (m, 0 = rays)", ref s.ArtSkirtSmoothM, 0f, 4000f, "0");
            Slider("   Blend to mean edge colour by (m, 0 = never)", ref s.ArtSkirtMeanByM, 0f, 5000f, "0");
            s.ArtHorizonFarBlend = GUILayout.Toggle(s.ArtHorizonFarBlend, " Far edge dissolves into the sky: haze colour at the rim, rim lifted");
            Slider("   Haze red", ref s.ArtHorizonFarR, 0f, 1f, "0.00");
            Slider("   Haze green", ref s.ArtHorizonFarG, 0f, 1f, "0.00");
            Slider("   Haze blue", ref s.ArtHorizonFarB, 0f, 1f, "0.00");
            Slider("   Rim lift (m)", ref s.ArtHorizonRimRiseM, 0f, 1500f, "0");
            s.ArtHorizonFarClip = GUILayout.Toggle(s.ArtHorizonFarClip, " Raise the donor cameras' far clip to reach the skirt");
            s.ArtFixStandFacing = GUILayout.Toggle(s.ArtFixStandFacing, " Stands face the track (long side nearest it); stands that do not become solid blocks");
            s.ArtDonorStands = GUILayout.Toggle(s.ArtDonorStands, " Use Phoenix stadium meshes for stands (can stretch into slabs)");
            Slider("   Solid block height (m)", ref s.ArtBlockHeight, 3f, 20f, "0.0");
            s.ArtFixPitBuilding = GUILayout.Toggle(s.ArtFixPitBuilding, " Procedural pit building behind the garages");
            s.ArtBuildingsOverTunnel = GUILayout.Toggle(s.ArtBuildingsOverTunnel, " Buildings may stand over a tunnel (not rejected as on the road there)");
            s.ArtFixTower = GUILayout.Toggle(s.ArtFixTower, " Plain tower column (no window bands)");
            s.ArtBuildingVariety = GUILayout.Toggle(s.ArtBuildingVariety, " Building variety: facades and roofs (deterministic per mapped building)");
            Slider("   Building variety", ref s.ArtBuildingVarietyAmount, 0f, 1f, "0.00");
            s.ArtMonacoPalette = GUILayout.Toggle(s.ArtMonacoPalette, " Monaco palette: warm cream / muted ochre facades and tile / pale roofs");
            s.ArtMonacoCoastalHorizon = GUILayout.Toggle(s.ArtMonacoCoastalHorizon, " Monaco coastal horizon: continuous blue-grey sea haze (no raised rim)");
            s.ArtMonacoPitBuilding = GUILayout.Toggle(s.ArtMonacoPitBuilding, " Monaco pit building: low quay garages with cream facade");
            s.ArtMonacoRunoffStyle = GUILayout.Toggle(s.ArtMonacoRunoffStyle, " Monaco run-off: muted tarmac aprons (no red/white/blue fallback)");
            s.ArtMonacoHairpinSafetyWalls = GUILayout.Toggle(s.ArtMonacoHairpinSafetyWalls, " Monaco Grand Hotel hairpin: continuous road-edge safety walls");
            Slider("   Monaco hairpin wall clearance outside drivable edge (m)", ref s.ArtMonacoHairpinWallClearanceM, 0.8f, 4f, "0.0");
            ArtTunnels.DrawGui(s);
        }

        private static void Slider(string label, ref float v, float a, float b, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + " " + v.ToString(fmt), GUILayout.ExpandWidth(false));
            v = GUILayout.HorizontalSlider(v, a, b, GUILayout.Width(220));
            GUILayout.EndHorizontal();
        }
    }

    /// <summary>One run of the art generator for one scene.</summary>
    internal partial class ArtBuild
    {
        private readonly CircuitScene mScene;
        private readonly PackLayout mLayout;
        private readonly BuiltVenue mBuilt;
        private readonly Settings mS;
        private readonly TrackPack mPack;
        private readonly LayoutPaths mPaths;
        private readonly PackTerrain mTerrain;
        private readonly GameObject mRoot;
        private readonly int mLayer;
        private readonly RoadIndex mRoad = new RoadIndex(64f, 64f);       // track, pit lane, pit entry / exit
        private readonly RoadIndex mGarage = new RoadIndex(64f, 64f);     // garage and pit-box paths (props must keep off these too)
        private readonly RoadIndex mAvoid = new RoadIndex(64f, 64f);      // painted run-off (no trees there)
        private readonly MeshBatcher mBatch = new MeshBatcher();
        private float[] mRoadLeft, mRoadRight;
        private readonly DonorBatch mDonor = new DonorBatch();
        private readonly List<List<V2>> mSolids = new List<List<V2>>();   // footprints trees must avoid
        private readonly HashSet<long> mNearRoad = new HashSet<long>();
        private readonly HashSet<long> mOsmKerb = new HashSet<long>();
        private readonly List<Feature> mFeatures;
        private List<CornerSpan> mCorners;
        private List<V2> mTrack2;
        private List<float> mTrackHalf;                                   // max(left, right) half-width per track point (stand facing rule)
        private readonly RoadIndex mPitLaneOnly = new RoadIndex(64f, 64f); // pit lane centreline only (pit building clearance log)
        private int mRenderers, mDrawCalls;
        private readonly Dictionary<string, int> mCount = new Dictionary<string, int>();
        private readonly List<string> mStageLog = new List<string>();
        private Texture2D mOrtho;
        private bool mHasOrtho;
        private HeightFn mTy;

        public ArtBuild(CircuitScene scene, PackLayout layout, BuiltVenue built, Settings s)
        {
            mScene = scene; mLayout = layout; mBuilt = built; mS = s;
            mPack = layout.Pack;
            mPaths = layout.Paths;
            mTerrain = mPack.Terrain;
            if (mTerrain == null) throw new Exception("pack has no terrain");
            PackTerrain t = mTerrain;
            mTy = delegate (float x, float z) { return t.Sample(x, z); };
            int l = LayerMask.NameToLayer("Env_WorldDetail");
            mLayer = l >= 0 ? l : 0;
            ArtRegistry.Begin(mPack.Id, mLayout.Key);   // what gets built is recorded for the auto-test art tour (ArtTour.cs)
            mRoot = new GameObject("RT Art");
            mRoot.transform.SetParent(scene.transform, false);
            // The pack frame is world space. The donor's scene root is not at the world origin, so pin this root to world identity
            // like the road holder (VenueRoad.EnsureHolder); otherwise terrain, photo and paint sit offset from the road (M1 run 4 verifier).
            VenueRoad.PinToWorld(mRoot.transform);
            Transform sr = scene.transform;
            Main.Log("RT ART: scene root '" + sr.name + "' pos " + sr.position.ToString("F1") + " rot " + sr.rotation.eulerAngles.ToString("F1") + " scale " + sr.lossyScale.ToString("F3") + "; art root now pos " + mRoot.transform.position.ToString("F1") + " scale " + mRoot.transform.lossyScale.ToString("F3"));
            mRoot.layer = mLayer;
            mFeatures = mPack.Features;
            mBatch.CellSize = 400f;

            List<Vector3> track = mPaths.Track;
            float shoulder = Mathf.Clamp(mS.VenueRoadAiMarginM, 0f, 3f);
            mRoadLeft = new float[mPaths.WidthLeft.Length]; mRoadRight = new float[mPaths.WidthRight.Length];
            for (int i = 0; i < mRoadLeft.Length; i++) { mRoadLeft[i] = mPaths.WidthLeft[i] + shoulder; mRoadRight[i] = mPaths.WidthRight[i] + shoulder; }
            mRoad.AddPolyline(ToV3(track), mRoadLeft, mRoadRight, 6f, 6f, true);
            if (mPaths.PitEntry.Count > 1) mRoad.AddPolyline(ToV3(mPaths.PitEntry), 5f, false);
            if (mPaths.PitLane.Count > 1) mRoad.AddPolyline(ToV3(mPaths.PitLane), 7f, false);
            if (mPaths.PitExit.Count > 1) mRoad.AddPolyline(ToV3(mPaths.PitExit), 5f, false);
            foreach (GarageSlot g in mPaths.Garages.Values)
            {
                if (g.PitboxEntry != null && g.PitboxEntry.Count > 1) mGarage.AddPolyline(ToV3(g.PitboxEntry), 3f, false);
                if (g.PitboxExit != null && g.PitboxExit.Count > 1) mGarage.AddPolyline(ToV3(g.PitboxExit), 3f, false);
                for (int c = 0; c < 2; c++)
                {
                    if (g.GarageEntry[c] != null && g.GarageEntry[c].Count > 1) mGarage.AddPolyline(ToV3(g.GarageEntry[c]), 3f, false);
                    if (g.GarageExit[c] != null && g.GarageExit[c].Count > 1) mGarage.AddPolyline(ToV3(g.GarageExit[c]), 3f, false);
                }
            }
            // coarse "near the road" cells so the terrain clamp only runs where it matters
            foreach (List<Vector3> line in new List<Vector3>[] { mPaths.Track, mPaths.PitLane, mPaths.PitEntry, mPaths.PitExit })
                foreach (Vector3 p in line)
                    for (int dx = -2; dx <= 2; dx++)
                        for (int dz = -2; dz <= 2; dz++)
                            mNearRoad.Add(CellKey(Mathf.FloorToInt(p.x / 16f) + dx, Mathf.FloorToInt(p.z / 16f) + dz));

            mTrack2 = new List<V2>();
            foreach (Vector3 p in track) mTrack2.Add(new V2(p.x, p.z));
            mTrackHalf = new List<float>();
            for (int i = 0; i < track.Count; i++) mTrackHalf.Add(Mathf.Max(mRoadLeft[i], mRoadRight[i]));
            if (mPaths.PitLane.Count > 1) mPitLaneOnly.AddPolyline(ToV3(mPaths.PitLane), 0f, false);
            float lap = mPaths.S[mPaths.S.Length - 1] + Vector3.Distance(track[track.Count - 1], track[0]);
            mCorners = Corners.Find(mTrack2, mPaths.S, lap);
            InitTunnels();
        }

        private static long CellKey(int cx, int cz) { return ((long)cx << 32) ^ (cz & 0xffffffffL); }

        private static List<V3> ToV3(List<Vector3> l)
        {
            List<V3> r = new List<V3>(l.Count);
            foreach (Vector3 v in l) r.Add(new V3(v.x, v.y, v.z));
            return r;
        }

        private void Bump(string k, int n) { int v; mCount.TryGetValue(k, out v); mCount[k] = v + n; }

        public void Stage(string name, bool enabled, Action a)
        {
            if (!enabled) { mStageLog.Add(name + " off"); return; }
            Stopwatch sw = Stopwatch.StartNew();
            try { a(); mStageLog.Add(name + " " + sw.ElapsedMilliseconds + "ms"); }
            catch (Exception e)
            {
                Main.Logger.Error("RT ART: stage '" + name + "' failed: " + e);
                RtFallbacks.Note("art stage " + name, "failed, that part of the art is missing: " + e.Message);
                mBuilt.Warnings.Add("art: stage " + name + " failed: " + e.Message);
                mStageLog.Add(name + " FAILED");
            }
        }

        public void Finish(long ms)
        {
            foreach (KeyValuePair<string, int> kv in mCount) mBuilt.Counts[kv.Key] = kv.Value;
            mBuilt.Counts["drawCallsEstimate"] = mDrawCalls;
            string msg = "RT ART: " + mPack.Id + "/" + mLayout.Key + " terrainChunks=" + Get("terrainChunks") + " terrainVerts=" + Get("terrainVerts") +
                         " kerbSegments=" + Get("kerbSegments") + " grandstands=" + Get("grandstands") + " buildings=" + Get("buildings") +
                         " trees=" + Get("trees") + " barriersM=" + Get("barriersM") + " waterPlanes=" + Get("waterPlanes") +
                         " drawCallsEstimate=" + mDrawCalls + " warnings=" + CountArt() + " shader=" + ArtMaterials.ShaderName + " in " + ms + "ms [" + string.Join(", ", mStageLog.ToArray()) + "]";
            Main.Logger.Log(msg);
            PublishTourScene();
            Main.Log("RT ART M1.1: stands seating=" + Get("grandstands") + " blocks=" + Get("standBlocks") + " turned=" + Get("standsTurned") + " skipped=" + Get("standsSkipped") +
                     " pitBuilding=" + Get("pitBuilding") + " towers=" + Get("towers") + " skirtMeshes=" + Get("skirt") + " farClipCameras=" + Get("farClipCameras"));
        }

        /// <summary>Hands the auto-test art tour what it needs: the track, pit lane, turn table, terrain and the objects recorded above. Never fails the build.</summary>
        private void PublishTourScene()
        {
            try
            {
                TourScene sc = ArtRegistry.Scene;
                if (sc == null) return;
                foreach (Vector3 p in mPaths.Track) sc.Track.Add(new V3(p.x, p.y, p.z));
                foreach (Vector3 p in mPaths.PitLane) sc.PitLane.Add(new V3(p.x, p.y, p.z));
                if (mLayout.CornersFile != null)
                {
                    string file = mPack.PathOf(mLayout.CornersFile);
                    if (file != null && File.Exists(file))
                        foreach (LineTurn t in LineCornerMap.ParseCsv(File.ReadAllLines(file)))
                            sc.Turns.Add(new TourTurn(t.Turn, (float)t.ApexX, (float)t.ApexZ, (float)t.S0, (float)t.S1));
                }
                PackTerrain t0 = mTerrain;
                sc.Ground = delegate (float x, float z) { return t0.Sample(x, z); };
                sc.TerrainX0 = t0.X0; sc.TerrainZ0 = t0.Z0; sc.TerrainX1 = t0.XMax; sc.TerrainZ1 = t0.ZMax;
                sc.SkirtRadius = Mathf.Clamp(mS.ArtSkirtRadiusKm, 2f, 80f) * 1000f;
                sc.Views = mPack.Views;      // null / empty: the planner uses the generic default set
                if (mPack.ViewErrors.Count > 0) RtFallbacks.Note("art tour views", mPack.ViewErrors.Count + " invalid manifest view(s) dropped" + ((mPack.Views == null || mPack.Views.Count == 0) ? ", the generic default set is used" : "") + ": " + string.Join("; ", mPack.ViewErrors.ToArray()));
                else if (mPack.Views != null && mPack.Views.Count == 0) RtFallbacks.Note("art tour views", "the manifest's views array is empty, the generic default set is used");
                Main.Log("RT ART TOUR: recorded " + sc.Marks.Count + " objects for the auto-test art tour (" + CountKind(sc, "stand") + " stands, " + CountKind(sc, "block") + " blocks, " + CountKind(sc, "pit") + " pit building, " + CountKind(sc, "tower") + " tower), " + sc.Turns.Count + " turns");
            }
            catch (Exception e) { Main.Log("RT ART TOUR: could not record the scene: " + e.Message); ArtRegistry.Scene = null; RtFallbacks.Note("art tour", "could not record the scene: " + e.Message); }
        }

        private static int CountKind(TourScene sc, string kind) { int n = 0; foreach (ArtMark m in sc.Marks) if (m.Kind == kind) n++; return n; }

        private int Get(string k) { int v; return mCount.TryGetValue(k, out v) ? v : 0; }
        private int CountArt() { int n = 0; foreach (string w in mBuilt.Warnings) if (w.StartsWith("art:")) n++; return n; }
        private void Warn(string m) { mBuilt.Warnings.Add("art: " + m); Main.Log("RT ART: warning: " + m); RtFallbacks.Note("art", m); }

        // ------------------------------------------------------------------ helpers

        private float TY(float x, float z) { return mTerrain.Sample(x, z); }

        private bool OnRoad(V2 p, float margin) { return mRoad.Clearance(p.X, p.Y) < margin; }

        private GameObject NewObject(string name, MeshData md, Material[] mats, bool shadows)
        {
            Mesh m = BuildMesh(md);
            m.name = name;
            return Emit(name, m, mats, shadows);
        }

        private GameObject Emit(string name, Mesh m, Material[] mats, bool shadows)
        {
            GameObject go = new GameObject(name);
            go.layer = mLayer;
            go.transform.SetParent(mRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.receiveShadows = true;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            mRenderers++;
            mDrawCalls += mats.Length;
            return go;
        }

        private static Mesh BuildMesh(MeshData md)
        {
            string err = md.Validate();
            if (err != null) throw new Exception("generated mesh invalid: " + err);
            if (md.Count >= 65000) throw new Exception("mesh has " + md.Count + " vertices");
            Mesh m = new Mesh();
            m.vertices = ToVec3(md.P);
            m.normals = ToVec3(md.N);
            Vector2[] uv = new Vector2[md.UV.Count];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(md.UV[i].X, md.UV[i].Y);
            m.uv = uv;
            m.triangles = md.T.ToArray();
            m.RecalculateBounds();
            ArtMaterials.Track(m);
            return m;
        }

        private static Vector3[] ToVec3(List<V3> l)
        {
            Vector3[] a = new Vector3[l.Count];
            for (int i = 0; i < a.Length; i++) a[i] = new Vector3(l[i].X, l[i].Y, l[i].Z);
            return a;
        }

        private void AddMesh(string mat, MeshData md) { if (md != null && md.Count > 0) mBatch.Add(mat, md); }

        // ------------------------------------------------------------------ terrain

        private Texture2D LoadOrtho()
        {
            string path = mPack.PathOf(mPack.OrthoFile);
            if (path == null || !File.Exists(path)) { Warn("orthophoto file missing"); return null; }
            Texture2D t = new Texture2D(4, 4, TextureFormat.RGB24, true);
            if (!t.LoadImage(File.ReadAllBytes(path))) { Warn("orthophoto could not be decoded"); UnityEngine.Object.Destroy(t); return null; }
            ArtMaterials.Track(t);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            // edge colour for the ground outside the photo and the horizon skirt
            try
            {
                int w = t.width, h = t.height;
                Color sum = Color.black; int n = 0;
                Color[][] strips = { t.GetPixels(0, 0, w, 1), t.GetPixels(0, h - 1, w, 1), t.GetPixels(0, 0, 1, h), t.GetPixels(w - 1, 0, 1, h) };
                foreach (Color[] s in strips) for (int i = 0; i < s.Length; i += 7) { sum += s[i]; n++; }
                if (n > 0) ArtMaterials.GroundColour = sum / n;
                if (mS.ArtFixHorizonFade) ArtMaterials.SkirtEdge = EdgeColours(t);
            }
            catch (Exception e) { Main.Log("RT ART: ortho edge colour unavailable: " + e.Message); RtFallbacks.Note("art ortho", "edge colour unavailable, default ground colour used: " + e.Message); }
            int w0 = t.width, h0 = t.height, mips = t.mipmapCount;
            try
            {
                t.Apply(true);
                if (w0 % 4 == 0 && h0 % 4 == 0) t.Compress(true);
                t.Apply(false, true);
            }
            catch (Exception e) { Main.Log("RT ART: ortho compress skipped: " + e.Message); RtFallbacks.Note("art ortho", "texture compression skipped, uncompressed orthophoto used: " + e.Message); }
            Main.Log("RT ART: orthophoto " + w0 + "x" + h0 + " format " + t.format + " mips " + t.mipmapCount + " (was " + mips + ")");
            return t;
        }

        /// <summary>Average photo colour along the outer ~25 px of each skirt segment (SkirtGeo row order: sides z-min, z-max, x-min, x-max, then corners), lightly smoothed.</summary>
        private static Color[] EdgeColours(Texture2D t)
        {
            int w = t.width, h = t.height, segs = SkirtGeo.SegsPerSide;
            int band = Mathf.Max(2, Mathf.Min(w, h) / 80);
            Color[] edge = new Color[SkirtGeo.Rows];
            for (int s = 0; s < 4; s++)
            {
                int span = s < 2 ? w : h;
                for (int j = 0; j < segs; j++)
                {
                    int a = (int)((long)span * j / segs), b = (int)((long)span * (j + 1) / segs), len = Mathf.Max(1, b - a);
                    if (a + len > span) len = span - a;
                    Color[] px;
                    switch (s)
                    {
                        case 0: px = t.GetPixels(a, 0, len, band); break;
                        case 1: px = t.GetPixels(a, h - band, len, band); break;
                        case 2: px = t.GetPixels(0, a, band, len); break;
                        default: px = t.GetPixels(w - band, a, band, len); break;
                    }
                    edge[s * segs + j] = Avg(px);
                }
                // 3-tap smoothing along the side so the skirt does not show per-segment stripes
                Color[] sm = new Color[segs];
                for (int j = 0; j < segs; j++)
                {
                    Color a0 = edge[s * segs + Mathf.Max(0, j - 1)], a1 = edge[s * segs + j], a2 = edge[s * segs + Mathf.Min(segs - 1, j + 1)];
                    sm[j] = (a0 + a1 * 2f + a2) * 0.25f;
                }
                for (int j = 0; j < segs; j++) edge[s * segs + j] = sm[j];
            }
            edge[4 * segs + 0] = Avg(t.GetPixels(0, 0, band, band));
            edge[4 * segs + 1] = Avg(t.GetPixels(w - band, 0, band, band));
            edge[4 * segs + 2] = Avg(t.GetPixels(0, h - band, band, band));
            edge[4 * segs + 3] = Avg(t.GetPixels(w - band, h - band, band, band));
            return edge;
        }

        private static Color Avg(Color[] px)
        {
            if (px == null || px.Length == 0) return ArtMaterials.GroundColour;
            double r = 0, g = 0, b = 0;
            foreach (Color c in px) { r += c.r; g += c.g; b += c.b; }
            float n = px.Length;
            return new Color((float)(r / n), (float)(g / n), (float)(b / n), 1f);
        }

        public void StageTerrain()
        {
            if (HasHarbour) Sea();   // M2 w11: the sea mask, and the terrain's outer row over the sea down to the sea bed, before meshing
            PackTerrain t = mTerrain;
            Rect ob = mPack.OrthoBounds;
            bool haveBounds = ob.width > 1f && ob.height > 1f;
            if (mS.ArtOrtho && haveBounds) mOrtho = LoadOrtho();
            mHasOrtho = mOrtho != null;
            float ox = ob.xMin, oz = ob.yMin, ow = ob.width, oh = ob.height;
            if (!haveBounds) { ox = t.X0; oz = t.Z0; ow = (t.Nx - 1) * t.Dx; oh = (t.Nz - 1) * t.Dz; }

            int stride = 1;
            while ((long)(t.Nx / stride) * (t.Nz / stride) > 1600000L) stride++;
            int[] xs = TerrainGeo.AxisSamples(t.Nx, stride), zs = TerrainGeo.AxisSamples(t.Nz, stride);
            List<TerrainGeo.Range> plan = TerrainGeo.Plan(xs.Length, zs.Length, 128);

            Func<float, float, float, float> adjust = delegate (float x, float z, float y)
            {
                if (mTunnelIdx != null && mTunnelIdx.Clearance(x, z) < 6f) return y;     // over a tunnel the ground stays where the pack put it (the road runs under it)
                if (!mNearRoad.Contains(CellKey(Mathf.FloorToInt(x / 16f), Mathf.FloorToInt(z / 16f)))) return y;
                float roadY;
                float c = mRoad.Nearest(x, z, out roadY);
                if (c >= 6f) return y;
                float allowed = roadY - 0.05f + Mathf.Max(c, 0f) * 0.5f;
                return y > allowed ? allowed : y;
            };

            Material ground = ArtMaterials.Get("ground");
            ground.mainTextureScale = new Vector2(ow / 16f, oh / 16f);
            Material photo = null;
            if (mHasOrtho)
            {
                photo = ArtMaterials.Make("Terrain photo", mOrtho, Color.white, 0f);
                ArtMaterials.Set("terrain", photo);
            }
            int verts = 0, chunks = 0;
            foreach (TerrainGeo.Range r in plan)
            {
                TerrainGeo.Chunk c = TerrainGeo.BuildChunk(t.H, t.Nx, t.Nz, t.X0, t.Z0, t.Dx, t.Dz, xs, zs, r, ox, oz, ow, oh, mHasOrtho, adjust);
                string err = c.M.Validate();
                if (err != null) throw new Exception("terrain chunk invalid: " + err);
                if (c.M.Count >= 65000) throw new Exception("terrain chunk too large: " + c.M.Count);
                Mesh m = new Mesh();
                m.name = "RT terrain " + chunks;
                m.vertices = ToVec3(c.M.P);
                m.normals = ToVec3(c.M.N);
                Vector2[] uv = new Vector2[c.M.UV.Count];
                for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(c.M.UV[i].X, c.M.UV[i].Y);
                m.uv = uv;
                List<Material> mats = new List<Material>();
                List<int[]> subs = new List<int[]>();
                if (c.TriOrtho.Count > 0) { subs.Add(c.TriOrtho.ToArray()); mats.Add(photo); }
                if (c.TriOther.Count > 0) { subs.Add(c.TriOther.ToArray()); mats.Add(ground); }
                if (subs.Count == 0) { UnityEngine.Object.Destroy(m); continue; }
                m.subMeshCount = subs.Count;
                for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i);
                m.RecalculateBounds();
                ArtMaterials.Track(m);
                Emit("RT terrain " + chunks, m, mats.ToArray(), false);
                m.UploadMeshData(true);
                verts += c.M.Count; chunks++;
            }
            Bump("terrainChunks", chunks); Bump("terrainVerts", verts);
            Main.Log("RT ART: terrain " + t.Nx + "x" + t.Nz + " stride " + stride + " -> " + chunks + " chunks, " + verts + " verts, ortho " + (mHasOrtho ? "on" : "off"));
        }

        public void StageSkirt()
        {
            PackTerrain t = mTerrain;
            LogFog("before");
            if (!mS.ArtFixHorizonFade)
            {
                int stride = Mathf.Max(1, t.Nx / 256);
                int[] xs = TerrainGeo.AxisSamples(t.Nx, stride), zs = TerrainGeo.AxisSamples(t.Nz, Mathf.Max(1, t.Nz / 256));
                float outerY0;
                MeshData m0 = TerrainGeo.Skirt(t.H, t.Nx, t.Nz, t.X0, t.Z0, t.Dx, t.Dz, xs, zs, 6000f, 16f, out outerY0);
                NewObject("RT skirt", m0, new Material[] { ArtMaterials.Get("skirt") }, false);
                Bump("skirt", 1);
                Main.Log("RT ART: skirt (M1 flat ring, 6 km; ArtFixHorizonFade off)");
                if (HasHarbour && Sea() != null && Sea().Any) Warn("harbour sea: the M1 flat skirt (horizon fade off) is not sea-aware: it may stand above the sea; the far sea is still drawn");
                return;
            }
            float radius = Mathf.Clamp(mS.ArtSkirtRadiusKm, 2f, 80f) * 1000f;
            float fade = Mathf.Max(100f, mS.ArtHorizonFadeM);
            Color far = new Color(Mathf.Clamp01(mS.ArtHorizonR), Mathf.Clamp01(mS.ArtHorizonG), Mathf.Clamp01(mS.ArtHorizonB), 1f);
            Color haze = new Color(Mathf.Clamp01(mS.ArtHorizonFarR), Mathf.Clamp01(mS.ArtHorizonFarG), Mathf.Clamp01(mS.ArtHorizonFarB), 1f);
            float outerY; float[] rings;
            float rise = mS.ArtHorizonFarBlend ? Mathf.Clamp(mS.ArtHorizonRimRiseM, 0f, 3000f) : 0f;
            bool monacoCoast = mPack != null && mPack.Id == "monaco" && mS.ArtMonacoCoastalHorizon;
            if (monacoCoast)
            {
                // The hub camera sees the complete skirt.  A raised, tan countryside rim reads as a huge wedge around the harbour;
                // Monaco needs one muted coastal atmosphere across both land and sea instead.
                fade = Mathf.Max(fade, 6500f);
                far = new Color(0.39f, 0.55f, 0.62f, 1f);
                haze = new Color(0.55f, 0.65f, 0.69f, 1f);
                rise = 0f;
            }
            SeaMask sea = HasHarbour ? Sea() : null;
            if (sea != null && !sea.Any) sea = null;
            List<MeshData> parts = SkirtGeo.Build(t.H, t.Nx, t.Nz, t.X0, t.Z0, t.Dx, t.Dz, radius, rise, sea, out outerY, out rings);
            SkirtGeo.ColourSpec spec = new SkirtGeo.ColourSpec();
            spec.FadeM = fade; spec.Radius = radius; spec.SmoothM = Mathf.Max(0f, mS.ArtSkirtSmoothM); spec.MeanByM = Mathf.Max(0f, mS.ArtSkirtMeanByM);
            spec.HorizonBlend = mS.ArtHorizonFarBlend; spec.Tan = new float[] { far.r, far.g, far.b }; spec.Haze = new float[] { haze.r, haze.g, haze.b };
            if (monacoCoast) spec.Sea = new float[] { far.r, far.g, far.b };
            float xSize = (t.Nx - 1) * t.Dx, zSize = (t.Nz - 1) * t.Dz;
            Material mat = ArtMaterials.MakeSkirtFade(rings, spec, xSize, zSize, sea != null ? SeaGeo.SeaRows(sea) : null);
            if (HasHarbour) NoteSkirtOverSea(parts, rings, outerY);
            int verts = 0;
            for (int i = 0; i < parts.Count; i++) { NewObject("RT skirt " + i, parts[i], new Material[] { mat }, false); verts += parts[i].Count; }
            Bump("skirt", parts.Count);
            Color e0 = ArtMaterials.SkirtEdge != null ? ArtMaterials.SkirtEdge[0] : ArtMaterials.GroundColour;
            Main.Log("RT ART: skirt radius " + (radius / 1000f).ToString("0.#") + " km, " + rings.Length + " rings (" + string.Join(",", Array.ConvertAll(rings, r => r.ToString("0"))) + " m), " + parts.Count + " meshes " + verts + " verts, outer height " + outerY.ToString("0.0") +
                     " m; colour ramp: edge " + Hex(ArtMaterials.GroundColour) + " (first segment " + Hex(e0) + ", " + (ArtMaterials.SkirtEdge != null ? "per-segment from the photo" : "single colour") + ") -> " + (monacoCoast ? "coastal blue " : "tan ") + Hex(far) + " over " + fade.ToString("0") + " m" + (monacoCoast ? "; Monaco coastal horizon enabled, rim lift suppressed" : ""));
            try
            {
                float[] raw = ArtMaterials.LastRaw, sm = ArtMaterials.LastSmooth, mean = ArtMaterials.LastMean;
                SkirtGeo.ColourSpec old = new SkirtGeo.ColourSpec();
                old.FadeM = spec.FadeM; old.Radius = radius; old.SmoothM = 0f; old.MeanByM = 0f; old.HorizonBlend = false; old.Tan = spec.Tan;
                float d1 = 1000f, d5 = 5000f, mb1, pb1, ma1, pa1, mb5, pb5, ma5, pa5;
                SkirtGeo.NeighbourStep(raw, raw, mean, xSize, zSize, d1, old, out mb1, out pb1);
                SkirtGeo.NeighbourStep(raw, sm, mean, xSize, zSize, d1, spec, out ma1, out pa1);
                SkirtGeo.NeighbourStep(raw, raw, mean, xSize, zSize, d5, old, out mb5, out pb5);
                SkirtGeo.NeighbourStep(raw, sm, mean, xSize, zSize, d5, spec, out ma5, out pa5);
                float[] rim = new float[3]; SkirtGeo.Colour(raw, sm, mean, 0, radius, spec, rim);
                Main.Log("RT ART: skirt smoothing arc " + spec.SmoothM.ToString("0") + " m, mean colour by " + spec.MeanByM.ToString("0") + " m (mean " + Hex(new Color(mean[0], mean[1], mean[2])) + "); neighbour-segment colour step max/p95 at 1 km before "
                         + mb1.ToString("0.000") + "/" + pb1.ToString("0.000") + " after " + ma1.ToString("0.000") + "/" + pa1.ToString("0.000") + ", at 5 km before " + mb5.ToString("0.000") + "/" + pb5.ToString("0.000") + " after " + ma5.ToString("0.000") + "/" + pa5.ToString("0.000"));
                Main.Log("RT ART: skirt far edge: haze blend " + (spec.HorizonBlend ? "on" : "off") + ", haze albedo " + Hex(haze) + ", outer-ring colour " + Hex(new Color(rim[0], rim[1], rim[2])) + ", rim lift " + rise.ToString("0") + " m at " + (radius / 1000f).ToString("0.#") + " km, opaque lit " + ArtMaterials.ShaderName + " shader (no vertex alpha)");
            }
            catch (Exception e) { Main.Log("RT ART: skirt colour stats unavailable: " + e.Message); RtFallbacks.Note("art skirt", "colour statistics unavailable (smoothing / haze not logged or applied as designed): " + e.Message); }
            if (mS.ArtHorizonFarClip) ExtendFarClip(radius * 1.15f);
            Main.Log("RT ART: fog left as the game sets it (EnvironmentManager.PrepareForSession / SetEnviromentVariables write RenderSettings.fog* itself); nothing to restore on unload");
        }

        private static string Hex(Color c)
        {
            return "#" + Mathf.RoundToInt(c.r * 255f).ToString("X2") + Mathf.RoundToInt(c.g * 255f).ToString("X2") + Mathf.RoundToInt(c.b * 255f).ToString("X2");
        }

        private void LogFog(string when)
        {
            try
            {
                Main.Log("RT ART: RenderSettings fog " + when + ": " + (RenderSettings.fog ? "on " : "off ") + RenderSettings.fogMode + " density " + RenderSettings.fogDensity.ToString("0.00000") + " colour " + Hex(RenderSettings.fogColor));
            }
            catch (Exception e) { Main.Log("RT ART: fog state unreadable: " + e.Message); RtFallbacks.Note("art fog", "fog state unreadable: " + e.Message); }
        }

        /// <summary>AnimatedCamera copies the chosen donor camera's clip planes onto the game camera (AnimatedCamera.cs:63-64, 94-95) and restores its own when it stops (:38-39).</summary>
        private void ExtendFarClip(float want)
        {
            int n = 0; float oldMax = 0f;
            foreach (Camera c in mScene.GetComponentsInChildren<Camera>(true))
            {
                if (c == null || c.farClipPlane >= want) continue;
                oldMax = Mathf.Max(oldMax, c.farClipPlane);
                c.farClipPlane = want;
                n++;
            }
            Bump("farClipCameras", n);
            Main.Log("RT ART: far clip raised to " + want.ToString("0") + " m on " + n + " donor cameras (was up to " + oldMax.ToString("0") + " m); the cameras are part of the donor scene and go with it on unload");
        }

        // ------------------------------------------------------------------ kerbs and run-off

        private bool NearOsmKerb(float x, float z) { return mOsmKerb.Contains(CellKey(Mathf.FloorToInt(x / 8f), Mathf.FloorToInt(z / 8f))); }

        public void StageKerbs()
        {
            // OSM kerb lines first (they also stop the procedural kerbs doubling up)
            int osm = 0;
            foreach (Feature f in mFeatures)
            {
                if (f.Kind != "kerb" || f.Coords.Count < 2) continue;
                List<V2> line = Lines.Resample(V2s(f.Coords), 2f);
                foreach (V2 p in line) mOsmKerb.Add(CellKey(Mathf.FloorToInt(p.X / 8f), Mathf.FloorToInt(p.Y / 8f)));
                foreach (List<V2> run in Lines.Runs(line, delegate (V2 p) { return mRoad.Clearance(p.X, p.Y) > -0.2f; }))
                {
                    int n = run.Count;
                    float[] a = new float[n], b = new float[n];
                    for (int i = 0; i < n; i++) { a[i] = -0.6f; b[i] = 0.6f; }
                    MeshData md = Ribbons.Build(run, false, a, b, 1, delegate (int row, int col, float x, float z) { return TY(x, z) + 0.05f; }, Kerbs.TileLength, 0f, null);
                    AddMesh("kerb", md);
                    osm += n;
                }
            }
            RibbonKeep keep = delegate (int row, int col, float x, float z) { return !NearOsmKerb(x, z); };
            int n2 = mPaths.Track.Count, segs = 0;
            foreach (CornerSpan c in mCorners)
            {
                int inside = c.Sign > 0 ? -1 : 1;
                List<int> rows = Kerbs.Rows(c.Start, c.End, n2, 2, 2);
                AddMesh("kerb", Kerbs.Strip(ToV3(mPaths.Track), mRoadLeft, mRoadRight, rows, inside, 1.5f, mTy, keep));
                segs += rows.Count;
                if (c.MinRadius < 150f)
                {
                    List<int> outRows = Kerbs.Rows(c.Apex, c.End, n2, 0, 2);
                    AddMesh("kerb", Kerbs.Strip(ToV3(mPaths.Track), mRoadLeft, mRoadRight, outRows, -inside, 1.5f, mTy, keep));
                    segs += outRows.Count;
                }
            }
            Bump("kerbSegments", segs + osm);
            Main.Log("RT ART: kerbs: " + mCorners.Count + " corners found, " + segs + " rows procedural, " + osm + " points from OSM kerb lines");
        }

        private static List<V2> V2s(List<Vector2> l)
        {
            List<V2> r = new List<V2>(l.Count);
            foreach (Vector2 v in l) r.Add(new V2(v.x, v.y));
            return r;
        }

        public void StageRunoff()
        {
            const float PaintWidth = 14f;
            bool monacoRunoff = mPack != null && mPack.Id == "monaco" && mS.ArtMonacoRunoffStyle;
            // Real painted run-off from OSM colours wins; the generated T1 stripes are only a fallback (orchestrator, M1).
            bool osmPaint = false;
            foreach (Feature f in mFeatures)
                if (f.Kind == "runoff" && (f.Colour == "red" || f.Colour == "blue")) { osmPaint = true; break; }
            CornerSpan t1 = osmPaint ? null : Corners.FirstBig(mCorners, 150f, 60f);
            if (osmPaint) { Main.Log("RT ART: painted run-off from the pack's OSM colours (generated T1 stripes not needed)"); }
            else if (t1 == null) { Warn("no big corner found for the painted run-off"); }
            else
            {
                int n = mPaths.Track.Count;
                int side = t1.Sign > 0 ? 1 : -1; // outside of the corner: right of a left-hander
                List<int> rows = Kerbs.Rows(t1.Start, t1.End, n, 14, 4);
                List<V2> c = new List<V2>();
                List<float> oa = new List<float>(), ob = new List<float>(), roadY = new List<float>();
                int rc = rows.Count;
                List<V3> avoid = new List<V3>();
                for (int k = 0; k < rc; k++)
                {
                    int i = rows[k];
                    c.Add(mTrack2[i]);
                    roadY.Add(mPaths.Track[i].y);
                    float taper = Mathf.Clamp01(Mathf.Min(k, rc - 1 - k) / 6f);
                    float w = PaintWidth * taper;
                    if (side > 0) { float e = mRoadRight[i] + 0.25f; oa.Add(e); ob.Add(e + Mathf.Max(w, 0.01f)); }
                    else { float e = -mRoadLeft[i] - 0.25f; ob.Add(e); oa.Add(e - Mathf.Max(w, 0.01f)); }
                }
                RibbonY yf = delegate (int row, int col, float x, float z) { return Mathf.Max(TY(x, z), roadY[row] - 0.02f) + 0.05f; };
                RibbonKeep keep = delegate (int row, int col, float x, float z) { return mRoad.Clearance(x, z) > 0.1f; };
                MeshData md = Ribbons.Build(c, false, oa, ob, 6, yf, 6f, 6f, keep);
                AddMesh(monacoRunoff ? "monaco_runoff" : "paint", md);
                // trees keep off the paint
                for (int k = 0; k < rc; k++)
                {
                    int i = rows[k];
                    float mid = side > 0 ? mRoadRight[i] + PaintWidth * 0.5f : -mRoadLeft[i] - PaintWidth * 0.5f;
                    V2 t = (mTrack2[(i + 1) % n] - mTrack2[(i + n - 1) % n]).Norm;
                    V2 right = new V2(t.Y, -t.X);
                    V2 p = mTrack2[i] + right * mid;
                    avoid.Add(new V3(p.X, 0, p.Y));
                }
                mAvoid.AddPolyline(avoid, PaintWidth * 0.5f + 4f, false);
                Bump("paintedRunoffRows", rc);
                Main.Log("RT ART: " + (monacoRunoff ? "Monaco muted-tarmac apron" : "painted run-off") + " at corner starting " + t1.SStart.ToString("0") + " m (" + t1.TotalAngleDeg.ToString("0") + " deg, min radius " + t1.MinRadius.ToString("0") + " m), " + (side > 0 ? "right" : "left") + " side, " + rc + " rows");
            }
            int polys = 0;
            // largest first, smaller ones lifted a little more so stripes laid over an apron don't z-fight
            List<Feature> runoffs = new List<Feature>();
            foreach (Feature f in mFeatures) if (f.Kind == "runoff" && f.Coords.Count >= 3) runoffs.Add(f);
            List<float> areas = new List<float>();
            List<List<V2>> rings = new List<List<V2>>();
            foreach (Feature f in runoffs) { List<V2> r = Poly.Clean(V2s(f.Coords)); rings.Add(r); areas.Add(Mathf.Abs((float)Poly.Area(r))); }
            int[] order = new int[runoffs.Count];
            for (int k = 0; k < order.Length; k++) order[k] = k;
            Array.Sort(order, (a, b) => areas[b].CompareTo(areas[a]));
            for (int rank = 0; rank < order.Length; rank++)
            {
                int k = order[rank];
                Feature f = runoffs[k];
                List<V2> ring = rings[k];
                if (areas[k] < 20) continue;
                string mat = "runoff";
                if (!string.IsNullOrEmpty(f.Colour)) mat = "runoff:" + f.Colour.Split(';')[0].Trim().ToLowerInvariant();
                else if (f.Surface == "gravel") mat = "runoff:gravel";
                float lift = 0.05f + 0.02f * Mathf.Min(rank, 10) / 10f;
                MeshData md = Ribbons.Drape(ring, null, 4f, mTy, lift, 8f,
                    delegate (V2 cen) { return mRoad.Clearance(cen.X, cen.Y) < 0.2f || mAvoid.Clearance(cen.X, cen.Y) < 0f; });
                AddMesh(mat, md);
                polys++;
            }
            Bump("runoffPolys", polys);
        }

        // ------------------------------------------------------------------ footprints

        private bool FootprintClear(List<V2> ring, float margin, bool includeGarages) { return FootprintClear(ring, margin, includeGarages, 0f); }

        private bool FootprintClear(List<V2> ring, float margin, bool includeGarages, float garageMargin) { return FootprintClear(ring, margin, includeGarages, garageMargin, false); }

        /// <summary>overTunnel: points inside a tunnel's corridor do not count as "on the road" (a building may stand on the hill above the tunnel shell; w10).</summary>
        private bool FootprintClear(List<V2> ring, float margin, bool includeGarages, float garageMargin, bool overTunnel)
        {
            Func<V2, bool> bad = delegate (V2 p)
            {
                if (overTunnel && mTunnelIdx != null && mTunnelIdx.Clearance(p.X, p.Y) < 0f) return false;
                if (mRoad.Clearance(p.X, p.Y) < margin) return true;
                if (includeGarages && mGarage.Clearance(p.X, p.Y) < garageMargin) return true;
                return false;
            };
            // vertices, edge points every ~4 m (a long thin footprint can cross a road between its corners) and the centroid
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                V2 a = ring[i], b = ring[(i + 1) % n];
                int steps = Mathf.Max(2, Mathf.CeilToInt(V2.Dist(a, b) / 4f));
                for (int k = 0; k < steps; k++) if (bad(V2.Lerp(a, b, (float)k / steps))) return false;
            }
            return !bad(Poly.Centroid(ring));
        }

        /// <summary>As MinGround but on the pack terrain itself (the tunnel-aware TY lowers the ground to the road inside a tunnel; a building above it stands on the hill).</summary>
        private float MinGroundRaw(List<V2> ring, out float maxG)
        {
            float mn = float.MaxValue, mx = float.MinValue;
            Action<V2> s = delegate (V2 p) { float y = mTerrain.Sample(p.X, p.Y); if (y < mn) mn = y; if (y > mx) mx = y; };
            foreach (V2 p in ring) s(p);
            s(Poly.Centroid(ring));
            maxG = mx;
            return mn;
        }

        private float MinGround(List<V2> ring, out float maxG)
        {
            float mn = float.MaxValue, mx = float.MinValue;
            Action<V2> s = delegate (V2 p) { float y = TY(p.X, p.Y); if (y < mn) mn = y; if (y > mx) mx = y; };
            foreach (V2 p in ring) s(p);
            s(Poly.Centroid(ring));
            maxG = mx;
            return mn;
        }

        /// <summary>Minimum distance from a footprint's outline (sampled every ~2 m) to an indexed polyline: its Clearance plus `halfWidth` the index was built with.</summary>
        private static float MinDistTo(List<V2> ring, RoadIndex idx, float halfWidth)
        {
            float best = float.MaxValue;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                V2 a = ring[i], b = ring[(i + 1) % n];
                int steps = Mathf.Max(1, Mathf.CeilToInt(V2.Dist(a, b) / 2f));
                for (int k = 0; k < steps; k++) { V2 p = V2.Lerp(a, b, (float)k / steps); best = Mathf.Min(best, idx.Clearance(p.X, p.Y) + halfWidth); }
            }
            return best;
        }

        private static string V2s(V2 v) { return "(" + v.X.ToString("0.0") + "," + v.Y.ToString("0.0") + ")"; }

        public void StageStands()
        {
            int seating = 0, blocks = 0, dupSkipped = 0, donorUsed = 0, generated = 0, onRoad = 0, turned = 0, fromPack = 0, byRule = 0;
            List<RtProto> protos = (mS.ArtDonorProps && mS.ArtDonorStands) ? DonorProps.Accepted("grandstand") : new List<RtProto>();
            List<List<V2>> kept = new List<List<V2>>();
            // largest first, so a bleachers way drawn inside a grandstand way is the one dropped
            List<Feature> stands = new List<Feature>();
            foreach (Feature f in mFeatures) if (f.Kind == "grandstand" && f.Coords.Count >= 3) stands.Add(f);
            Dictionary<Feature, List<V2>> rings = new Dictionary<Feature, List<V2>>();
            foreach (Feature f in stands) rings[f] = Poly.Clean(V2s(f.Coords));
            stands.Sort((a, b) => Poly.Area(rings[b]).CompareTo(Poly.Area(rings[a])));
            foreach (Feature f in stands)
            {
                List<V2> ring = rings[f];
                if (Poly.Area(ring) < 30) continue;
                string id = f.Osm ?? f.Name ?? "?";
                V2 centre, u, v; float hu, hv;
                Poly.OrientedBox(ring, out centre, out u, out v, out hu, out hv);
                V2 front; float depth, length; string role = "seating";
                float rY; V2 cp;
                mRoad.Nearest(centre.X, centre.Y, out rY, out cp);
                V2 legacy = StandRules.LegacyFront(ring, (cp - centre).Norm);
                if (mS.ArtFixStandFacing)
                {
                    bool dup = false;
                    if (f.Role == "skip") dup = true;
                    else if (f.Role == null) foreach (List<V2> k in kept) if (StandRules.OverlapShare(ring, k) > 0.2f) { dup = true; break; }
                    if (dup) { dupSkipped++; Main.Log("RT ART: stand " + id + " skipped: mostly inside a larger stand"); continue; }
                    StandDecision d = null;
                    if (f.Role != null && f.HasFront) { fromPack++; }
                    else { d = StandRules.Decide(ring, mTrack2, mTrackHalf); byRule++; }
                    V2 fr = d != null ? d.Front : new V2(f.Front.x, f.Front.y).Norm;
                    role = d != null ? d.Role : f.Role;
                    front = fr;
                    // length runs along the track edge, depth away from it, measured on the footprint's own box
                    bool depthIsV = Mathf.Abs(V2.Dot(front, v)) >= Mathf.Abs(V2.Dot(front, u));
                    depth = (depthIsV ? hv : hu) * 2f; length = (depthIsV ? hu : hv) * 2f;
                    if (d != null) Main.Log("RT ART: stand " + id + " " + role + " front " + V2s(front) + " edge " + d.EdgeDist.ToString("0") + " m, face " + d.FaceDeg.ToString("0") + " deg, along " + d.AlongDeg.ToString("0") + " deg, " + length.ToString("0") + "x" + depth.ToString("0") + " m");
                    if (V2.Dot(front, legacy) < 0.7f) turned++;
                }
                else
                {
                    // M1 heuristic (kept for the off switch)
                    front = legacy;
                    bool depthIsV = Mathf.Abs(V2.Dot(front, v)) >= Mathf.Abs(V2.Dot(front, u));
                    depth = (depthIsV ? hv : hu) * 2f; length = (depthIsV ? hu : hv) * 2f;
                }
                if (!FootprintClear(ring, 1.0f, true)) { onRoad++; Warn("grandstand " + id + " overlaps the road and was skipped"); continue; }
                float height = f.Height > 0 ? f.Height : -1f;
                if (role == "block")
                {
                    float bh = height > 0 ? Mathf.Clamp(height, 3f, 30f) : Mathf.Clamp(mS.ArtBlockHeight, 3f, 20f);
                    float mx;
                    float mn = MinGround(ring, out mx);
                    MeshData walls = new MeshData(), roof = new MeshData();
                    Shapes.Extrude(ring, mn - 0.6f, mn + bh + (mx - mn) * 0.5f, 12f, 7f, 10f, walls, roof);
                    AddMesh("bld_wall", walls); AddMesh("bld_roof", roof);
                    mSolids.Add(ring); kept.Add(ring);
                    ArtRegistry.Add(new ArtMark("block", id, centre, front, length, depth, bh, mn));
                    blocks++;
                    continue;
                }
                depth = Mathf.Clamp(depth, 4f, 40f);
                height = height > 0 ? Mathf.Clamp(height, 5f, 30f) : Mathf.Clamp(depth * 0.55f, 5f, 15f);
                float baseY = TY(centre.X + front.X * depth * 0.5f, centre.Y + front.Y * depth * 0.5f);
                bool done = false;
                foreach (RtProto p in protos)
                    if (mDonor.PlaceStand(p, centre, front, length, depth, height, baseY)) { done = true; donorUsed++; break; }
                if (!done)
                {
                    Shapes.StandParts sp = new Shapes.StandParts();
                    Shapes.Stand(sp, centre, front, length, depth, height, baseY, true);
                    AddMesh("stand_seat", sp.Seats); AddMesh("stand_crowd", sp.Crowd); AddMesh("concrete", sp.Concrete); AddMesh("stand_roof", sp.Roof);
                    generated++;
                }
                mSolids.Add(ring); kept.Add(ring);
                ArtRegistry.Add(new ArtMark("stand", id, centre, front, length, depth, height, baseY));
                seating++;
            }
            Bump("grandstands", seating); Bump("standBlocks", blocks);
            Bump("standsTurned", turned); Bump("standsSkipped", dupSkipped + onRoad);
            Main.Log("RT ART: grandstands by kind: seating " + seating + " (" + generated + " generated, " + donorUsed + " donor), solid blocks " + blocks + "; skipped " + dupSkipped + " duplicates + " + onRoad + " on the road; " +
                     turned + " faced differently from the M1 heuristic (rule " + (mS.ArtFixStandFacing ? "on" : "off") + ", decided by the pack " + fromPack + " / at run time " + byRule + ")");
        }

        public void StageBuildings()
        {
            int made = 0, skipped = 0, pitMade = 0, towerSkipped = 0, tunnelBuildings = 0, roofDetails = 0;
            Dictionary<string, int> facades = new Dictionary<string, int>(), roofs = new Dictionary<string, int>();
            List<V2> towers = new List<V2>();
            int recorded = 0;
            HashSet<string> wantedIds = new HashSet<string>();
            if (mPack != null && mPack.Views != null)
                foreach (ViewSpec vs in mPack.Views) if (vs.Kind == "tower") foreach (string oid in vs.OsmIds) wantedIds.Add(oid.IndexOf('/') >= 0 ? oid : "way/" + oid);
            if (mS.ArtTower && mS.ArtFixTower)
                foreach (Feature f in mFeatures) if (f.Kind == "tower" && f.Coords.Count >= 1) towers.Add(new V2(f.Coords[0].x, f.Coords[0].y));
            foreach (Feature f in mFeatures)
            {
                if ((f.Kind != "building" && f.Kind != "pitbuilding") || f.Coords.Count < 3) continue;
                bool pit = f.Kind == "pitbuilding";
                List<V2> ring = Poly.Clean(V2s(f.Coords));
                if (Poly.Area(ring) < 20) continue;
                if (!pit && towers.Count > 0)
                {
                    bool underTower = false;
                    foreach (V2 tp in towers) if (Poly.Contains(ring, tp) || Poly.Contains(ring, tp + new V2(1.5f, 0)) || Poly.Contains(ring, tp + new V2(-1.5f, 0))) { underTower = true; break; }
                    if (underTower) { towerSkipped++; Main.Log("RT ART: building " + (f.Osm ?? "?") + " (" + (f.Height > 0 ? f.Height.ToString("0") + " m" : "no height") + ") sits under the observation tower: not extruded (the plain tower replaces it)"); continue; }
                }
                if (pit && mS.ArtFixPitBuilding) { if (PitBuilding(f, ring)) { pitMade++; made++; } else skipped++; continue; }
                bool overTun = mS.ArtBuildingsOverTunnel && mTunnelIdx != null;
                if (!FootprintClear(ring, 1.5f, pit, 0f, overTun))
                {
                    skipped++;
                    Warn((pit ? "pit building " : "building ") + (f.Osm ?? f.Name) + " overlaps the road / pit lane / garages and was skipped");
                    continue;
                }
                float height = f.Height > 0 ? f.Height : (f.Levels > 0 ? f.Levels * 3.5f : 8f);
                height = Mathf.Clamp(height, 3f, 120f);
                float maxG;
                bool onTunnel = false;
                if (overTun) foreach (V2 tp in ring) if (mTunnelIdx.Clearance(tp.X, tp.Y) < 6f) { onTunnel = true; break; }
                float minG = onTunnel ? MinGroundRaw(ring, out maxG) : MinGround(ring, out maxG);
                if (onTunnel) { tunnelBuildings++; Main.Log("RT ART: building " + (f.Osm ?? "?") + " stands over a tunnel: footprint kept, base on the hill above the shell (ground " + minG.ToString("0.0") + ".." + maxG.ToString("0.0") + ")"); }
                float roofY = minG + height + (maxG - minG) * 0.5f;
                MeshData walls = new MeshData(), roof = new MeshData(), roofFurniture = new MeshData();
                Shapes.Extrude(ring, minG - 0.6f, roofY, 12f, 7f, 10f, walls, roof);
                string wallMat = pit ? "pit_wall" : BuildingFacade(f, height);
                string roofMat = pit ? "bld_roof" : BuildingRoof(f, height);
                AddMesh(wallMat, walls);
                AddMesh(roofMat, roof);
                if (!pit) roofDetails += RoofFurniture(f, ring, roofY, height, roofFurniture);
                AddMesh("bld_roof_units", roofFurniture);
                CountVariant(facades, wallMat); CountVariant(roofs, roofMat);
                mSolids.Add(ring);
                made++;
                if (!pit && f.Osm != null && wantedIds.Contains(f.Osm))
                {
                    // a building a pack view asks for by id (e.g. the tower view's landmark): record it so the art tour can frame it
                    V2 cc = Poly.Centroid(ring); float bx0 = float.MaxValue, bx1 = float.MinValue, bz0 = float.MaxValue, bz1 = float.MinValue;
                    foreach (V2 rp in ring) { bx0 = Mathf.Min(bx0, rp.X); bx1 = Mathf.Max(bx1, rp.X); bz0 = Mathf.Min(bz0, rp.Y); bz1 = Mathf.Max(bz1, rp.Y); }
                    ArtRegistry.Add(new ArtMark("building", f.Osm, cc, new V2(0, 0), bx1 - bx0, bz1 - bz0, height, minG));
                    Main.Log("RT ART: landmark building " + f.Osm + " recorded for the art tour: " + height.ToString("0") + " m high, footprint " + (bx1 - bx0).ToString("0") + " x " + (bz1 - bz0).ToString("0") + " m");
                    recorded++;
                }
            }
            Bump("buildings", made);
            if (wantedIds.Count > 0 && recorded == 0) Warn("tower view landmark " + string.Join(",", new List<string>(wantedIds).ToArray()) + " was not built as a building (missing from the pack, under the tower, or overlapping the road): the tower view will use the tower or the tallest stand/block");
            Main.Log("RT ART: buildings " + made + " (" + skipped + " skipped, " + towerSkipped + " under the tower, pit building " + (pitMade > 0 ? "placed" : "not placed") + ", " + tunnelBuildings + " over a tunnel, " + roofDetails + " rooftop units/terrace slabs); facades " + Variants(facades) + "; roofs " + Variants(roofs) + "; variety " + (mS.ArtBuildingVariety ? mS.ArtBuildingVarietyAmount.ToString("0.00") : "off") + "; Monaco palette " + ((mPack.Id == "monaco" && mS.ArtMonacoPalette) ? "on" : "off"));
        }

        // Stable across Mono/.NET processes; do not use string.GetHashCode(), which is not a pack contract.
        private static uint StableHash(string text)
        {
            uint h = 2166136261u;
            if (text == null) return h;
            for (int i = 0; i < text.Length; i++) { h ^= text[i]; h *= 16777619u; }
            return h;
        }

        private string BuildingFacade(Feature f, float height)
        {
            if (!mS.ArtBuildingVariety || mS.ArtBuildingVarietyAmount < 0.05f) return "bld_wall";
            uint h = StableHash((f.Osm ?? f.Name ?? "building") + ":facade");
            if (mPack.Id == "monaco" && mS.ArtMonacoPalette)
            {
                int monaco = (int)(h % 10u);
                // Dense Monaco blocks are predominantly pale masonry, with restrained apricot/render accents and a few modern towers.
                if (height >= 42f && monaco >= 7) return "bld_wall_glass";
                if (monaco <= 4) return "bld_monaco_cream";
                if (monaco <= 6) return "bld_monaco_ochre";
                return monaco == 7 ? "bld_monaco_pale" : "bld_wall_stone";
            }
            int slots = mS.ArtBuildingVarietyAmount < 0.35f ? 2 : (mS.ArtBuildingVarietyAmount < 0.7f ? 3 : 4);
            int choice = (int)(h % (uint)slots);
            // Tall blocks are more likely to read as modern curtain-wall; the rest retain a mostly masonry city fabric.
            if (height >= 35f && choice >= 1) return "bld_wall_glass";
            return choice == 1 ? "bld_wall_stone" : (choice == 2 ? "bld_wall_render" : "bld_wall");
        }

        private string BuildingRoof(Feature f, float height)
        {
            if (!mS.ArtBuildingVariety || mS.ArtBuildingVarietyAmount < 0.05f) return "bld_roof";
            uint h = StableHash((f.Osm ?? f.Name ?? "building") + ":roof");
            if (mPack.Id == "monaco" && mS.ArtMonacoPalette)
            {
                int monaco = (int)(h % 10u);
                // The aerial reference is tile-rich, but its roofs are muted in aggregate; keep bright orange off large flat slabs.
                if (monaco <= 3) return "bld_roof_monaco_tile";
                if (monaco <= 6) return "bld_roof_monaco_pale";
                if (monaco == 9) return "bld_roof_units";
                return "bld_roof";
            }
            int slots = mS.ArtBuildingVarietyAmount < 0.45f ? 2 : 4;
            int choice = (int)(h % (uint)slots);
            // Monaco's mapped buildings are mostly flat-roofed. Keep their aerial palette Mediterranean-neutral:
            // pale gravel / warm limestone / slate rather than broad artificial green or saturated orange slabs.
            if (choice == 1) return "bld_roof_terracotta";
            if (choice == 2) return "bld_roof_green";
            if (choice == 3) return "bld_roof_units";
            return "bld_roof";
        }

        /// <summary>Deterministic HVAC clusters and small terrace slabs turn otherwise featureless flat extrusions into roofs that read at race-camera height.</summary>
        private int RoofFurniture(Feature f, List<V2> ring, float roofY, float height, MeshData outMesh)
        {
            if (!mS.ArtBuildingVariety || mS.ArtBuildingVarietyAmount < 0.20f || height < 6f) return 0;
            V2 c, u, v; float hu, hv;
            Poly.OrientedBox(ring, out c, out u, out v, out hu, out hv);
            if (hu < 4f || hv < 4f) return 0;
            uint h = StableHash((f.Osm ?? f.Name ?? "building") + ":roof-furniture");
            int count = 1 + (int)(h % 3u);
            if (mS.ArtBuildingVarietyAmount < 0.55f) count = 1;
            float du = Mathf.Min(3.2f, hu * 0.32f), dv = Mathf.Min(2.4f, hv * 0.30f);
            for (int i = 0; i < count; i++)
            {
                uint q = h + (uint)(i * 2246822519u);
                float fu = ((q & 255u) / 255f - 0.5f) * Mathf.Max(0f, hu * 1.15f - du);
                float fv = (((q >> 8) & 255u) / 255f - 0.5f) * Mathf.Max(0f, hv * 1.15f - dv);
                V2 p = c + u * fu + v * fv;
                float y = roofY + (i == 0 && height < 18f ? 0.22f : 0.75f);
                V2 a = p - u * du - v * dv, b = p + u * du - v * dv, cc = p + u * du + v * dv, d = p - u * du + v * dv;
                Shapes.BoxFromCorners(outMesh, new V3(a.X, y, a.Y), new V3(b.X, y, b.Y), new V3(cc.X, y, cc.Y), new V3(d.X, y, d.Y), i == 0 && height < 18f ? 0.18f : 0.70f);
            }
            return count;
        }

        private static void CountVariant(Dictionary<string, int> counts, string key)
        {
            int n; counts.TryGetValue(key, out n); counts[key] = n + 1;
        }

        private static string Variants(Dictionary<string, int> counts)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in counts) parts.Add(kv.Key + "=" + kv.Value);
            parts.Sort(StringComparer.Ordinal);
            return string.Join(",", parts.ToArray());
        }

        /// <summary>The procedural pit building on the pack's footprint (already offset behind the garages by the builder). False when it cannot be placed clear of the lane and garage paths.</summary>
        private bool PitBuilding(Feature f, List<V2> ring)
        {
            // front = towards the pit lane: from the pack, else from the nearest lane point
            V2 centre, u, v; float hu, hv;
            Poly.OrientedBox(ring, out centre, out u, out v, out hu, out hv);
            V2 toLane;
            if (f.HasFront) toLane = new V2(f.Front.x, f.Front.y).Norm;
            else { float ry; V2 cp; mPitLaneOnly.Nearest(centre.X, centre.Y, out ry, out cp); toLane = (cp - centre).Norm; }
            const float LaneEdge = 7f, GarageHalf = 3f;
            float laneMin = MinDistTo(ring, mPitLaneOnly, 0f), garageMin = MinDistTo(ring, mGarage, GarageHalf);
            bool clear = FootprintClear(ring, 3f, true, 2.5f);
            if (!clear)
            {
                Warn("pit building " + (f.Osm ?? "?") + " (footprint " + hu.ToString("0") + "x" + hv.ToString("0") + " half-extents at " + V2s(centre) + ") is within " + laneMin.ToString("0.0") + " m of the pit lane centreline / " + garageMin.ToString("0.0") + " m of the garage paths: skipped");
                return false;
            }
            bool monacoPit = mPack != null && mPack.Id == "monaco" && mS.ArtMonacoPitBuilding;
            float height = f.Height > 0 ? Mathf.Clamp(f.Height, 8f, 25f) : 13f;
            if (monacoPit) height = Mathf.Min(height, 7.2f);
            // ground at the front: median of terrain samples along the front edge
            List<float> gs = new List<float>();
            foreach (V2 p in ring) gs.Add(TY(p.X, p.Y));
            gs.Sort();
            float ground = gs[gs.Count / 2], mx;
            float mn = MinGround(ring, out mx);
            PitParts pp = new PitParts();
            V2[] box = PitShape.Build(pp, ring, toLane, ground, mn - 0.6f, height);
            AddMesh(monacoPit ? "pit_monaco_facade" : "pit_facade", pp.Facade);
            AddMesh(monacoPit ? "pit_monaco_side" : "pit_side", pp.Walls);
            AddMesh(monacoPit ? "bld_roof_monaco_pale" : "bld_roof", pp.Roof);
            AddMesh(monacoPit ? "pit_monaco_slab" : "stand_roof", pp.Slabs);
            mSolids.Add(ring);
            V2 pfc = V2.Lerp(box[0], box[1], 0.5f), pbc = V2.Lerp(box[2], box[3], 0.5f);
            ArtRegistry.Add(new ArtMark("pit", f.Osm ?? "pit", V2.Lerp(pfc, pbc, 0.5f), (pfc - pbc).Norm, V2.Dist(box[0], box[1]), V2.Dist(box[1], box[2]), height, ground));
            Bump("pitBuilding", 1);
            Main.Log("RT ART: pit building placed: footprint " + box[0].X.ToString("0.0") + "," + box[0].Y.ToString("0.0") + " / " + box[1].X.ToString("0.0") + "," + box[1].Y.ToString("0.0") + " / " + box[2].X.ToString("0.0") + "," + box[2].Y.ToString("0.0") + " / " + box[3].X.ToString("0.0") + "," + box[3].Y.ToString("0.0") +
                     "; " + V2.Dist(box[0], box[1]).ToString("0") + " x " + V2.Dist(box[1], box[2]).ToString("0") + " m, " + height.ToString("0.0") + " m high (" + (monacoPit ? "Monaco low quay garages, cream facade and shallow balcony" : "3 levels: garages, two glass floors") + "), ground " + ground.ToString("0.0") +
                     "; clearance to the pit lane centreline " + laneMin.ToString("0.0") + " m (to the lane edge " + (laneMin - LaneEdge).ToString("0.0") + " m), to the garage paths " + garageMin.ToString("0.0") + " m");
            return true;
        }

        public void StageTower()
        {
            int made = 0;
            foreach (Feature f in mFeatures)
            {
                if (f.Kind != "tower" || f.Coords.Count < 1) continue;
                V2 c = f.Coords.Count >= 3 ? Poly.Centroid(V2s(f.Coords)) : new V2(f.Coords[0].x, f.Coords[0].y);
                if (mRoad.Clearance(c.X, c.Y) < 6f) { Warn("tower too close to the road, skipped"); continue; }
                float h = f.Height > 20f ? f.Height : 77f;
                Shapes.TowerParts tp = new Shapes.TowerParts();
                Shapes.Tower(tp, c.X, c.Y, TY(c.X, c.Y), Mathf.Min(h, 200f));
                AddMesh("tower_concrete", tp.Concrete); AddMesh("tower_glass", tp.Glass);
                mSolids.Add(new List<V2> { new V2(c.X - 5, c.Y - 5), new V2(c.X + 5, c.Y - 5), new V2(c.X + 5, c.Y + 5), new V2(c.X - 5, c.Y + 5) });
                made++;
                ArtRegistry.Add(new ArtMark("tower", f.Osm ?? "tower", c, new V2(0, 0), 0f, 0f, Mathf.Min(h, 200f), TY(c.X, c.Y)));
                Main.Log("RT ART: tower at " + V2s(c) + " " + Mathf.Min(h, 200f).ToString("0") + " m: plain concrete shaft (no window bands), observation deck kept; pack windows flag " + (f.Windows ? "true" : "false") + ", mapped building under it " + (mS.ArtFixTower ? "not extruded" : "extruded (ArtFixTower off)"));
            }
            Bump("towers", made);
        }

        // ------------------------------------------------------------------ barriers and fences

        public void StageBarriers()
        {
            RtProto barrier = null;
            if (mS.ArtDonorProps) { List<RtProto> l = DonorProps.Accepted("barrier"); if (l.Count > 0) barrier = l[0]; }
            float metres = 0;
            int mapped = 0;
            foreach (Feature f in mFeatures)
            {
                if ((f.Kind != "barrier" && f.Kind != "fence") || f.Coords.Count < 2) continue;
                mapped++;
                foreach (List<V2> run0 in Lines.Runs(Lines.Resample(V2s(f.Coords), 3f), delegate (V2 p) { return mRoad.Clearance(p.X, p.Y) > 0.3f; }))
                    foreach (List<V2> run in Lines.Chop(run0, 80f))
                    {
                        if (f.Kind == "fence")
                        {
                            MeshData md = new MeshData();
                            Shapes.FencePanel(md, run, mTy, 3.5f, 3f, 3f);
                            AddMesh("fence", md);
                        }
                        else metres += AddBarrierRun(run, barrier);
                        if (f.Kind == "fence") metres += Lines.Length(run);
                    }
            }
            int generated = 0;
            if (mapped == 0) generated = GenerateBrakingBarriers(barrier, ref metres);
            int monacoHairpinWalls = 0;
            if (mS.ArtMonacoHairpinSafetyWalls) monacoHairpinWalls = AddMonacoHairpinSafetyWalls(barrier, ref metres);
            Bump("barriersM", Mathf.RoundToInt(metres));
            Main.Log("RT ART: barriers/fences " + mapped + " mapped lines, " + generated + " generated braking-zone runs, Monaco hairpin edge walls " + monacoHairpinWalls + ", " + Mathf.RoundToInt(metres) + " m" + (barrier != null ? " (donor barrier tiles)" : ""));
        }

        /// <summary>Monaco's Grand Hotel hairpin needs walls on the road edge, not on the lower terrain contour imported from OSM.
        /// Both edges are covered deliberately: it matches the closed street circuit and keeps the visual boundary coherent while
        /// the configurable road shoulder handles the separate AI compatibility issue.</summary>
        private int AddMonacoHairpinSafetyWalls(RtProto donor, ref float metres)
        {
            if (mPack == null || mPack.Id != "monaco" || mTrack2 == null || mTrack2.Count < 3) return 0;
            V2 referenceApex = new V2(686.36f, 567.35f); // turn 9 apex, packs/monaco/corners_GP.csv
            int apex = 0;
            float best = float.MaxValue;
            for (int i = 0; i < mTrack2.Count; i++)
            {
                float d = V2.Dist(mTrack2[i], referenceApex);
                if (d < best) { best = d; apex = i; }
            }
            List<V2> left = HairpinEdgeRun(apex, false), right = HairpinEdgeRun(apex, true);
            if (left.Count < 2 || right.Count < 2)
            {
                Warn("Monaco hairpin safety walls could not be generated: insufficient road samples");
                return 0;
            }
            // These are deliberately generated rather than donor-tiled: the donor barriers follow their original terrain and can
            // appear down the slope at Monaco's hairpin instead of on the actual generated road edge.
            metres += AddBarrierRun(left, donor, true);
            metres += AddBarrierRun(right, donor, true);
            Main.Log("RT ART: Monaco hairpin safety walls at turn 9 apex " + V2s(mTrack2[apex]) + ": " + Mathf.RoundToInt(Lines.Length(left)) + " m left + " + Mathf.RoundToInt(Lines.Length(right)) + " m right, generated concrete " + mS.ArtMonacoHairpinWallClearanceM.ToString("0.0") + " m outside the drivable edge (donor barrier tiles bypassed)");
            return 2;
        }

        private List<V2> HairpinEdgeRun(int apex, bool rightSide)
        {
            const float HalfLengthM = 42f;
            int n = mTrack2.Count;
            List<V2> before = new List<V2>(), after = new List<V2>();
            // Art meshes do not take part in Motorsport Manager's vehicle collision system.  Leave enough physical-looking
            // apron beyond the road's compatibility shoulder for the rendered vehicle envelope before the wall begins.
            float shoulder = Mathf.Clamp(mS.VenueRoadAiMarginM, 0f, 3f) + Mathf.Clamp(mS.ArtMonacoHairpinWallClearanceM, 0.8f, 4f);
            for (int direction = -1; direction <= 1; direction += 2)
            {
                List<V2> target = direction < 0 ? before : after;
                int i = apex;
                float travelled = 0f;
                while (true)
                {
                    V2 tangent = (mTrack2[(i + 1) % n] - mTrack2[(i + n - 1) % n]).Norm;
                    V2 roadRight = new V2(tangent.Y, -tangent.X);
                    float offset = rightSide ? mPaths.WidthRight[i] + shoulder : -mPaths.WidthLeft[i] - shoulder;
                    target.Add(mTrack2[i] + roadRight * offset);
                    int next = (i + direction + n) % n;
                    float step = V2.Dist(mTrack2[i], mTrack2[next]);
                    if (travelled + step > HalfLengthM) break;
                    travelled += step;
                    i = next;
                }
            }
            before.Reverse();
            if (after.Count > 0) after.RemoveAt(0); // apex is already the last point of before
            before.AddRange(after);
            return before;
        }

        private float AddBarrierRun(List<V2> run, RtProto donor, bool forceGenerated = false)
        {
            if (!forceGenerated && donor != null && mDonor.TileAlong(donor, run, mTy)) return Lines.Length(run);
            MeshData md = new MeshData();
            Shapes.BoxStrip(md, run, mTy, 0.45f, 0.95f, 3f, 4f);
            AddMesh("barrier", md);
            return Lines.Length(run);
        }

        private int GenerateBrakingBarriers(RtProto donor, ref float metres)
        {
            int n = mPaths.Track.Count, runs = 0;
            foreach (CornerSpan c in mCorners)
            {
                if (c.MinRadius > 100f || c.TotalAngleDeg < 50f) continue;
                int side = c.Sign > 0 ? 1 : -1;
                List<V2> line = new List<V2>();
                int from = (c.Start - 26 + n * 2) % n, count = 26 + ((c.Apex - c.Start + n) % n) + 8;
                for (int k = 0; k < count; k++)
                {
                    int i = (from + k) % n;
                    V2 t = (mTrack2[(i + 1) % n] - mTrack2[(i + n - 1) % n]).Norm;
                    V2 right = new V2(t.Y, -t.X);
                    float off = (side > 0 ? mPaths.WidthRight[i] : -mPaths.WidthLeft[i]);
                    off += side * 22f;
                    line.Add(mTrack2[i] + right * off);
                }
                foreach (List<V2> run0 in Lines.Runs(line, delegate (V2 p) { return mRoad.Clearance(p.X, p.Y) > 8f; }))
                {
                    metres += AddBarrierRun(run0, donor);
                    runs++;
                }
            }
            return runs;
        }

        // ------------------------------------------------------------------ water, car parks

        public void StageWater()
        {
            int made = 0;
            foreach (Feature f in mFeatures)
            {
                if (f.Kind != "water" || f.Coords.Count < 3) continue;
                List<V2> ring = Poly.Clean(V2s(f.Coords));
                if (Poly.Area(ring) < 20) continue;
                List<float> hs = new List<float>();
                foreach (V2 p in Lines.Resample(ring, 10f)) hs.Add(TY(p.X, p.Y));
                hs.Sort();
                float level = hs[hs.Count / 2] + 0.03f;
                MeshData md = Ribbons.Drape(ring, null, 1e9f, delegate (float x, float z) { return level; }, 0f, 20f,
                    delegate (V2 cen) { return mRoad.Clearance(cen.X, cen.Y) < 0.5f; });
                if (md.Count == 0) continue;
                string mat = "water";
                if (DonorProps.Water != null && ArtMaterials.IsUsable(DonorProps.Water)) { ArtMaterials.Set("water_donor", DonorProps.Water); mat = "water_donor"; }
                AddMesh(mat, md);
                mSolids.Add(ring);
                made++;
            }
            Bump("waterPlanes", made);
        }

        public void StageParking()
        {
            int made = 0;
            foreach (Feature f in mFeatures)
            {
                if (f.Kind != "parking" || f.Coords.Count < 3) continue;
                List<V2> ring = Poly.Clean(V2s(f.Coords));
                if (Poly.Area(ring) < 50) continue;
                MeshData md = Ribbons.Drape(ring, null, 5f, mTy, 0.05f, 10f,
                    delegate (V2 cen) { return mRoad.Clearance(cen.X, cen.Y) < 0.3f; });
                AddMesh("parking", md);
                made++;
            }
            Bump("carParks", made);
        }

        // ------------------------------------------------------------------ trees

        public void StageTrees()
        {
            float density = Mathf.Clamp01(mS.ArtTreeDensity);
            float spacing = Mathf.Lerp(20f, 6.5f, density);
            const int Cap = 6000;
            MeshData[] canopy = new MeshData[4], trunk = new MeshData[4];
            for (int v = 0; v < 4; v++) { canopy[v] = new MeshData(); trunk[v] = new MeshData(); Shapes.UnitTree(v, canopy[v], trunk[v]); }
            List<RtProto> donorTrees = mS.ArtDonorProps ? DonorProps.Accepted("tree") : new List<RtProto>();
            Rng rng = new Rng(4242);
            int count = 0, donorCount = 0, donorVerts = 0;
            Func<V2, bool> reject = delegate (V2 p)
            {
                if (mRoad.Clearance(p.X, p.Y) < 6f) return true;
                if (mAvoid.Clearance(p.X, p.Y) < 0f) return true;
                if (mGarage.Clearance(p.X, p.Y) < 4f) return true;
                foreach (List<V2> s in mSolids) if (Poly.Contains(s, p)) return true;
                return false;
            };
            Action<V2, bool> plant = delegate (V2 p, bool wood)
            {
                float y = TY(p.X, p.Y) - 0.1f;
                if (wood && donorTrees.Count > 0 && donorVerts < 500000 && rng.Next() < 0.5f)
                {
                    RtProto d = donorTrees[rng.Int(donorTrees.Count)];
                    if (mDonor.PlaceTree(d, p, y, rng.Range(0f, 360f), rng.Range(0.8f, 1.2f))) { donorCount++; donorVerts += d.Verts; count++; return; }
                }
                int v = rng.Next() < 0.2f ? 3 : rng.Int(3);
                float h = rng.Range(5f, 11f), w = rng.Range(0.9f, 1.3f);
                float yaw = rng.Range(0f, 360f);
                MeshData ca = new MeshData(), tr = new MeshData();
                ca.AppendTransformed(canopy[v], new V3(p.X, y, p.Y), yaw, h * w, h, h * w);
                tr.AppendTransformed(trunk[v], new V3(p.X, y, p.Y), yaw, h * w, h, h * w);
                AddMesh("tree_canopy", ca); AddMesh("tree_trunk", tr);
                count++;
            };
            int woods = 0;
            foreach (Feature f in mFeatures)
            {
                if (count >= Cap) break;
                if (f.Kind == "trees" && f.Coords.Count >= 3)
                {
                    List<V2> ring = Poly.Clean(V2s(f.Coords));
                    float sp = donorTrees.Count > 0 ? Mathf.Max(spacing, 12f) : spacing;
                    foreach (V2 p in Scatter.InPolygon(ring, null, sp, 100 + woods, reject, Mathf.Min(2500, Cap - count))) plant(p, true);
                    woods++;
                }
                else if (f.Kind == "tree" && f.Coords.Count >= 1)
                {
                    V2 p = new V2(f.Coords[0].x, f.Coords[0].y);
                    if (!reject(p)) plant(p, false);
                }
            }
            Bump("trees", count);
            Main.Log("RT ART: trees " + count + " (" + donorCount + " donor clusters) in " + woods + " woods, spacing " + spacing.ToString("0.0") + " m");
        }

        // ------------------------------------------------------------------ combine and create objects

        public void StageFlush()
        {
            List<MeshBatcher.Batch> batches = mBatch.Finish();
            int objs = 0, verts = 0;
            foreach (MeshBatcher.Batch b in batches)
            {
                Material mat = ArtMaterials.Get(b.Material);
                bool shadows = b.Material != "tunnel_light" && b.Material != "kerb" && b.Material != "paint" && b.Material != "monaco_runoff" && !b.Material.StartsWith("runoff") && b.Material != "parking" && !b.Material.StartsWith("water");
                NewObject("RT " + b.Material + " " + b.CellX + "," + b.CellZ, b.Mesh, new Material[] { mat }, shadows);
                objs++; verts += b.Mesh.Count;
            }
            int d = mDonor.Flush(this);
            Main.Log("RT ART: combined into " + objs + " generated meshes (" + verts + " verts) + " + d + " donor-prop meshes; " + mRenderers + " renderers");
        }

        internal GameObject EmitDonor(string name, Mesh m, Material mat)
        {
            return Emit(name, m, new Material[] { mat }, true);
        }
    }

    /// <summary>Places harvested donor props (stands, barriers, trees) and combines them per material and cell.</summary>
    internal class DonorBatch
    {
        private class Group { public Material Mat; public List<CombineInstance> Items = new List<CombineInstance>(); public List<int> Verts = new List<int>(); }
        private readonly Dictionary<string, Group> mGroups = new Dictionary<string, Group>();

        private void Add(Material mat, Mesh mesh, int sub, Matrix4x4 m, float x, float z)
        {
            string key = mat.GetInstanceID() + "|" + Mathf.FloorToInt(x / 400f) + "|" + Mathf.FloorToInt(z / 400f);
            Group g;
            if (!mGroups.TryGetValue(key, out g)) { g = new Group(); g.Mat = mat; mGroups[key] = g; }
            CombineInstance ci = new CombineInstance();
            ci.mesh = mesh; ci.subMeshIndex = sub; ci.transform = m;
            g.Items.Add(ci); g.Verts.Add(mesh.vertexCount);
        }

        private void PlaceProto(RtProto p, Matrix4x4 world, float x, float z)
        {
            foreach (RtPart part in p.Parts)
                for (int s = 0; s < part.Mesh.subMeshCount && s < part.Mats.Length; s++)
                    if (part.Mats[s] != null) Add(part.Mats[s], part.Mesh, s, world * part.Local, x, z);
        }

        /// <summary>Axis of the prop that carries the "back" (its upper vertices bias towards it), as a unit local vector; false when not directional.</summary>
        private static bool BackAxis(RtProto p, out Vector3 back, out bool lengthIsX)
        {
            back = Vector3.back; lengthIsX = true;
            float ymin = p.Min.y, ymax = p.Max.y;
            if (ymax - ymin < 1f) return false;
            Vector3 c = (p.Min + p.Max) * 0.5f, e = (p.Max - p.Min) * 0.5f;
            double sx = 0, sz = 0; int n = 0;
            foreach (RtPart part in p.Parts)
            {
                Vector3[] vs = part.Mesh.vertices;
                for (int i = 0; i < vs.Length; i += 3)
                {
                    Vector3 w = part.Local.MultiplyPoint3x4(vs[i]);
                    if (w.y < ymin + (ymax - ymin) * 0.8f) continue;
                    sx += (w.x - c.x) / Mathf.Max(e.x, 0.1f); sz += (w.z - c.z) / Mathf.Max(e.z, 0.1f); n++;
                }
            }
            if (n < 8) return false;
            float bx = (float)(sx / n), bz = (float)(sz / n);
            if (Mathf.Max(Mathf.Abs(bx), Mathf.Abs(bz)) < 0.12f) return false;
            if (Mathf.Abs(bx) > Mathf.Abs(bz)) { back = new Vector3(Mathf.Sign(bx), 0, 0); lengthIsX = false; }
            else { back = new Vector3(0, 0, Mathf.Sign(bz)); lengthIsX = true; }
            return true;
        }

        public bool PlaceStand(RtProto p, V2 centre, V2 front, float length, float depth, float height, float baseY)
        {
            Vector3 back; bool lenX;
            if (!BackAxis(p, out back, out lenX)) return false;
            Vector3 size = p.Size;
            float pl = lenX ? size.x : size.z, pd = lenX ? size.z : size.x;
            if (pl < 1f || pd < 1f || size.y < 1f) return false;
            float sl = length / pl, sd = depth / pd, sy = height / size.y;
            if (sl < 0.35f || sl > 3.5f || sd < 0.35f || sd > 3.5f || sy < 0.35f || sy > 3.5f) return false;
            Vector3 localFront = -back;
            float alpha = Mathf.Atan2(localFront.x, localFront.z) * Mathf.Rad2Deg;
            float beta = Mathf.Atan2(front.X, front.Y) * Mathf.Rad2Deg;
            Vector3 scale = lenX ? new Vector3(sl, sy, sd) : new Vector3(sd, sy, sl);
            Vector3 c = (p.Min + p.Max) * 0.5f;
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(centre.X, baseY, centre.Y), Quaternion.Euler(0, beta - alpha, 0), scale) *
                          Matrix4x4.TRS(new Vector3(-c.x, -p.Min.y, -c.z), Quaternion.identity, Vector3.one);
            PlaceProto(p, m, centre.X, centre.Y);
            return true;
        }

        public bool PlaceTree(RtProto p, V2 pos, float y, float yaw, float scale)
        {
            Vector3 c = (p.Min + p.Max) * 0.5f;
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(pos.X, y, pos.Y), Quaternion.Euler(0, yaw, 0), Vector3.one * scale) *
                          Matrix4x4.TRS(new Vector3(-c.x, -p.Min.y, -c.z), Quaternion.identity, Vector3.one);
            PlaceProto(p, m, pos.X, pos.Y);
            return true;
        }

        /// <summary>Tiles a barrier prop end to end along a polyline. False when the prop is unsuitable.</summary>
        public bool TileAlong(RtProto p, List<V2> run, HeightFn ty)
        {
            Vector3 size = p.Size;
            bool lenX = size.x >= size.z;
            float len = lenX ? size.x : size.z;
            if (len < 1f || len > 12f || size.y < 0.3f || size.y > 3f) return false;
            Vector3 c = (p.Min + p.Max) * 0.5f;
            Matrix4x4 toOrigin = Matrix4x4.TRS(new Vector3(-c.x, -p.Min.y, -c.z), Quaternion.identity, Vector3.one);
            float acc = len * 0.5f;
            for (int i = 0; i + 1 < run.Count; i++)
            {
                V2 a = run[i], b = run[i + 1];
                float seg = V2.Dist(a, b);
                if (seg < 1e-3f) continue;
                V2 d = (b - a) * (1f / seg);
                float yawDir = Mathf.Atan2(d.X, d.Y) * Mathf.Rad2Deg;
                float yaw = lenX ? yawDir - 90f : yawDir;
                float pos = acc;
                while (pos <= seg)
                {
                    V2 q = a + d * pos;
                    Matrix4x4 m = Matrix4x4.TRS(new Vector3(q.X, ty(q.X, q.Y), q.Y), Quaternion.Euler(0, yaw, 0), Vector3.one) * toOrigin;
                    PlaceProto(p, m, q.X, q.Y);
                    pos += len;
                }
                acc = pos - seg;
            }
            return true;
        }

        public int Flush(ArtBuild owner)
        {
            int made = 0;
            foreach (KeyValuePair<string, Group> kv in mGroups)
            {
                Group g = kv.Value;
                int start = 0;
                while (start < g.Items.Count)
                {
                    int end = start, sum = 0;
                    while (end < g.Items.Count && sum + g.Verts[end] < 60000) { sum += g.Verts[end]; end++; }
                    if (end == start) { start++; continue; }
                    CombineInstance[] arr = new CombineInstance[end - start];
                    g.Items.CopyTo(start, arr, 0, end - start);
                    Mesh m = new Mesh();
                    m.name = "RT donor combined";
                    m.CombineMeshes(arr, true, true);
                    ArtMaterials.Track(m);
                    owner.EmitDonor("RT donor " + g.Mat.name, m, g.Mat);
                    made++;
                    start = end;
                }
            }
            mGroups.Clear();
            return made;
        }
    }
}
