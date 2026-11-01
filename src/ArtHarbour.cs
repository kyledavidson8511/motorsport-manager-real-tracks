using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using RealTracks.ArtGeo;

namespace RealTracks
{
    /// <summary>Harbour settings (M2 w5). Pack circuits with a harbour.json only.</summary>
    public partial class Settings
    {
        /// <summary>Draw the pack's sea / harbour water at the pack's water level.</summary>
        public bool HarbourWater = true;
        /// <summary>Moor clones of the donor's yachts at the pack's berths.</summary>
        public bool HarbourYachts = true;
        /// <summary>Share of the pack's berths that get a yacht (0..1; the same ones every load).</summary>
        public float HarbourYachtDensity = 1f;
        /// <summary>Share of the small berths that get one of the donor's sailboats instead of a motor yacht (0..1; never big/medium berths).</summary>
        public float HarbourSailShare = 0.05f;
        /// <summary>Per-berth size variety: each yacht is up to this share smaller than its berth (0..0.3; same ones every load).</summary>
        public float HarbourYachtScaleJitter = 0.08f;
        /// <summary>Draw the pack's pontoon slabs (flat concrete at the water's edge level) where the pack has them.</summary>
        public bool HarbourPontoons = true;
        /// <summary>
        /// Sea to the horizon (M2 w11): the horizon skirt over the sea stays on the sea bed (no rise to the mean boundary height, no rim lift,
        /// sea-bed colour) and one water surface at the harbour's level covers it out to the skirt radius; the pack's sea polygons are cut to
        /// the terrain so no two water layers overlap. Off = the 0.2.6 skirt (it rose ~52 m above the Monaco sea: "the ocean floating").
        /// </summary>
        public bool HarbourSeaToHorizon = true;
        /// <summary>Lower the terrain's outermost row over the sea to the sea bed (the builder leaves it ~0.6 m above the water: a thin hard edge).</summary>
        public bool HarbourSeaEdgeClamp = true;
        /// <summary>Use the donor's animated water shader for harbour water. Off by default: donor shaders can impose
        /// their own world-water plane, which made Monaco's visible ocean float well above the boats.</summary>
        public bool HarbourUseDonorWaterMaterial = false;
        /// <summary>Land runs along the terrain edge up to this long between sea (a jetty or breakwater crossing the edge) count as sea for the skirt and the far sea (m; 0 = off).</summary>
        public float HarbourSeaGapM = 150f;
    }

    /// <summary>One harvested donor yacht: a clean inactive clone (meshes, materials and its LODGroup only) and its measured hull.</summary>
    public class YachtProto
    {
        public string Name = "", Path = "";
        public GameObject Clone;
        public float Length, Beam, Height, Draft;   // world metres; Draft = pivot height above the donor's water
        public Vector3 Centre;                      // bounds centre in the clone's frame (world scale)
        public float ForwardYaw;                    // bow direction in the clone's frame, degrees clockwise from +z
        public int Lods, Renderers, Verts0;
        public string BowBy = "";
        public bool Sail;                           // tall for its size (YachtPick.IsSail): a sailboat, not a motor yacht
        public string DraftBy = "";
    }

    /// <summary>
    /// Donor-specific harvest for harbours: the donor's water material (DonorProfile.WaterRoot) and yacht prefabs (DonorProfile.YachtPrefixes),
    /// plus the check that every object the profile expects is there. Runs before the donor art is hidden (VenueBuilder step 3).
    /// </summary>
    public static class DonorHarbour
    {
        public static DonorProfile Profile;
        public static Material Water;
        public static float WaterY = float.NaN;
        private static readonly List<Bounds> WaterPieces = new List<Bounds>();
        public static readonly List<YachtProto> Yachts = new List<YachtProto>();
        public static GameObject Holder;
        public static int SceneId;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Clear()
        {
            Yachts.Clear();
            WaterPieces.Clear();
            Water = null; WaterY = float.NaN; Profile = null;
            if (Holder != null) UnityEngine.Object.Destroy(Holder);
            Holder = null;
        }

        public static void Harvest(CircuitScene scene, BuiltVenue built)
        {
            Clear();
            SceneId = scene.GetInstanceID();
            PackLayout pl = ActiveVenue.CurrentLayout;
            string donor = pl != null && pl.Pack != null ? pl.Pack.DonorScene : "";
            DonorProfile prof = DonorProfiles.For(donor);
            Profile = prof;
            Transform root = scene.transform;
            Main.Log("RT DONOR: profile " + prof + "; scene root '" + root.name + "' rot " + root.rotation.eulerAngles.ToString("F1") + " (read at runtime; generated art is pinned to world space)");
            if (!prof.Known) Warn(built, "donor scene '" + donor + "' is not in the donor table (DonorProfiles.cs): generic choices, no harbour water material or yachts from the donor");

            Transform[] all = scene.GetComponentsInChildren<Transform>(true);
            HashSet<string> names = new HashSet<string>();
            foreach (Transform t in all) names.Add(t.name);
            int found = 0;
            foreach (string e in prof.Expected)
            {
                if (names.Contains(e)) found++;
                else Warn(built, "donor '" + donor + "': expected object '" + e + "' not found");
            }
            Main.Log("RT DONOR: " + found + " of " + prof.Expected.Length + " expected donor objects found");

            if (prof.WaterRoot != null) HarvestWater(all, prof, built);
            if (prof.YachtPrefixes.Length > 0) HarvestYachts(scene, all, prof, built);
        }

        private static void Warn(BuiltVenue built, string m)
        {
            Main.Logger.Warning("RT DONOR WARNING: " + m);
            RtFallbacks.Note("donor", m);
            if (built != null) built.Warnings.Add("donor: " + m);
        }

        private static void HarvestWater(Transform[] all, DonorProfile prof, BuiltVenue built)
        {
            Renderer best = null; float bestArea = 0f; int roots = 0, rends = 0;
            foreach (Transform t in all)
            {
                if (t.name != prof.WaterRoot) continue;
                roots++;
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                {
                    rends++;
                    WaterPieces.Add(r.bounds);
                    Material m = r.sharedMaterial;
                    if (m == null || !ArtMaterials.IsUsable(m)) continue;
                    Bounds b = r.bounds;
                    float a = b.size.x * b.size.z;
                    if (best == null || a > bestArea) { best = r; bestArea = a; }
                }
            }
            if (best == null)
            {
                Warn(built, "no usable water material under '" + prof.WaterRoot + "' (" + roots + " root(s), " + rends + " renderer(s)); harbour water falls back to the generic donor water / generated water material");
                return;
            }
            Water = best.sharedMaterial;
            WaterY = best.bounds.center.y;
            Main.Log(string.Format(Inv, "RT DONOR: water material '{0}' (shader {1}) from '{2}' ({3} renderers under {4} root(s)), donor water level y={5:0.00}, largest piece {6:0} m2; renderQueue {7} ({8})",
                Water.name, Water.shader != null ? Water.shader.name : "?", best.name, rends, roots, WaterY, bestArea, Water.renderQueue,
                Water.renderQueue >= 2500 ? "transparent: the ground under it shows through, the pack paints its sea bed one colour" : "opaque"));
        }

        private static bool Matches(string lname, string[] prefixes)
        {
            foreach (string p in prefixes) if (lname.StartsWith(p)) return true;
            return false;
        }

        private static void HarvestYachts(CircuitScene scene, Transform[] all, DonorProfile prof, BuiltVenue built)
        {
            Holder = new GameObject("RT Donor Yachts");
            Holder.SetActive(false);
            Holder.transform.SetParent(scene.transform, false);
            VenueRoad.PinToWorld(Holder.transform);
            HashSet<Transform> taken = new HashSet<Transform>();
            HashSet<string> seenMesh = new HashSet<string>();
            int examined = 0;
            foreach (Transform t in all)
            {
                if (Yachts.Count >= prof.MaxYachtProtos) break;
                if (t == null || t.IsChildOf(Holder.transform)) continue;
                string lname = t.name.ToLowerInvariant();
                if (!Matches(lname, prof.YachtPrefixes)) continue;
                bool inside = false;
                for (Transform a = t.parent; a != null; a = a.parent) if (taken.Contains(a) || Matches(a.name.ToLowerInvariant(), prof.YachtPrefixes)) { inside = true; break; }
                if (inside) continue;
                examined++;
                string why;
                YachtProto y = TryYacht(t, scene.transform, seenMesh, out why);
                if (y == null) { Main.Log("RT DONOR: yacht '" + t.name + "' skipped: " + why); continue; }
                taken.Add(t);
                Yachts.Add(y);
                Main.Log(string.Format(Inv, "RT DONOR: yacht proto '{0}' [{1}] {2:0.0} x {3:0.0} x {4:0.0} m, {5} LOD level(s), {6} renderers, LOD0 {7} verts, draft offset {8:0.00} m ({11}), bow {9:0} deg ({10})",
                    y.Name, y.Path, y.Length, y.Beam, y.Height, y.Lods, y.Renderers, y.Verts0, y.Draft, y.ForwardYaw, y.BowBy, y.DraftBy));
                Main.Log(string.Format(Inv, "RT DONOR: yacht proto {0} kind={1} height {2:0.0} length {3:0.0} beam {4:0.0} (height/length {5:0.00}; sail above {6:0.00} or height/beam above {7:0.0})",
                    y.Name, y.Sail ? "sail" : "motor", y.Height, y.Length, y.Beam, y.Height / Mathf.Max(0.1f, y.Length), YachtPick.SailHeightPerLength, YachtPick.SailHeightPerBeam));
                if (y.DraftBy.StartsWith("hull geometry")) Warn(built, "yacht '" + y.Name + "': draft from the " + y.DraftBy);
            }
            int sails = 0; foreach (YachtProto q in Yachts) if (q.Sail) sails++;
            Main.Logger.Log("RT DONOR: harvested " + Yachts.Count + " yacht prototype(s) of " + examined + " candidate(s): motor " + (Yachts.Count - sails) + " / sail " + sails);
            if (Yachts.Count > 0 && sails == Yachts.Count) Warn(built, "the donor's yacht models are all sailboats: every berth gets one (no motor yacht to clone)");
            if (Yachts.Count == 0) Warn(built, "no yacht prefab could be cloned from the donor (prefixes " + string.Join(",", prof.YachtPrefixes) + "); no moored yachts");
        }

        private static string PathOf(Transform t, Transform root)
        {
            string s = t.name;
            for (Transform p = t.parent; p != null && p != root; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        private static YachtProto TryYacht(Transform t, Transform root, HashSet<string> seenMesh, out string why)
        {
            why = "";
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { why = "no renderers"; return null; }
            foreach (Renderer r in rs) if (r.isPartOfStaticBatch) { why = "static-batched renderer '" + r.name + "' (its mesh is the combined batch)"; return null; }
            List<Renderer> lod0 = new List<Renderer>();
            LODGroup lg = t.GetComponent<LODGroup>();
            int lods = 1;
            if (lg != null)
            {
                LOD[] l = lg.GetLODs();
                lods = l.Length;
                if (l.Length > 0 && l[0].renderers != null) foreach (Renderer r in l[0].renderers) if (r != null) lod0.Add(r);
            }
            if (lod0.Count == 0) foreach (Renderer r in rs) lod0.Add(r);
            Matrix4x4 toLocal = Matrix4x4.TRS(t.position, t.rotation, Vector3.one).inverse;
            Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), mx = -mn;
            string key = null; int verts = 0;
            List<Vector3> pts = new List<Vector3>();
            foreach (Renderer r in lod0)
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Mesh mesh = mf.sharedMesh;
                if (key == null) key = mesh.name + "#" + mesh.vertexCount;
                verts += mesh.vertexCount;
                foreach (Material m in r.sharedMaterials) if (!ArtMaterials.IsUsable(m)) { why = "a material of '" + r.name + "' is missing/unsupported"; return null; }
                Matrix4x4 M = toLocal * r.transform.localToWorldMatrix;
                Bounds b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + new Vector3(((i & 1) == 0 ? -1 : 1) * b.extents.x, ((i & 2) == 0 ? -1 : 1) * b.extents.y, ((i & 4) == 0 ? -1 : 1) * b.extents.z);
                    Vector3 w = M.MultiplyPoint3x4(corner);
                    mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w);
                }
                if (mesh.isReadable && pts.Count < 20000)
                {
                    Vector3[] vs = mesh.vertices;
                    for (int i = 0; i < vs.Length; i += 2) pts.Add(M.MultiplyPoint3x4(vs[i]));
                }
            }
            if (key == null) { why = "no mesh"; return null; }
            if (seenMesh.Contains(key)) { why = "same model as an earlier prototype (" + key + ")"; return null; }
            Vector3 size = mx - mn;
            if (Mathf.Max(size.x, size.z) < 4f || Mathf.Max(size.x, size.z) > 200f) { why = "implausible hull size " + size; return null; }
            seenMesh.Add(key);

            YachtProto y = new YachtProto();
            y.Name = t.name; y.Path = PathOf(t, root); y.Lods = lods; y.Renderers = rs.Length; y.Verts0 = verts;
            bool alongZ = size.z >= size.x;
            y.Length = alongZ ? size.z : size.x; y.Beam = alongZ ? size.x : size.z; y.Height = size.y;
            y.Centre = (mn + mx) * 0.5f;
            // bow: the narrower end of the hull (readable meshes), else the model's +z / +x (moving donor boats travel along +z)
            y.ForwardYaw = alongZ ? 0f : 90f; y.BowBy = "model axis (mesh not readable)";
            if (pts.Count > 50)
            {
                float lo = alongZ ? mn.z : mn.x, len = y.Length;
                float wa = 0f, wb = 0f;
                float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
                foreach (Vector3 p in pts)
                {
                    float u = ((alongZ ? p.z : p.x) - lo) / len, lat = alongZ ? p.x : p.z;
                    if (u < 0.12f) { aMin = Mathf.Min(aMin, lat); aMax = Mathf.Max(aMax, lat); }
                    else if (u > 0.88f) { bMin = Mathf.Min(bMin, lat); bMax = Mathf.Max(bMax, lat); }
                }
                wa = aMax - aMin; wb = bMax - bMin;
                if (wa > 0 && wb > 0)
                {
                    bool bowHigh = wb < wa;
                    y.ForwardYaw = alongZ ? (bowHigh ? 0f : 180f) : (bowHigh ? 90f : 270f);
                    y.BowBy = string.Format(Inv, "narrower end (end widths {0:0.0} / {1:0.0} m)", wa, wb);
                }
            }
            y.Sail = YachtPick.IsSail(y.Length, y.Beam, y.Height);
            // draft (pivot height above its waterline): the donor water piece right under the yacht when one is there and the waterline
            // it gives cuts the hull in its lower half; else the hull geometry (waterline 12 % / sailboat 7 % of the height above the keel)
            float bottom = mn.y, wy, geo = -(bottom + (y.Sail ? 0.07f : 0.12f) * size.y);
            if (LocalWaterY(t.position, out wy))
            {
                float d = t.position.y - wy, line = -d;   // waterline in the pivot frame
                if (line >= bottom && line <= bottom + 0.5f * size.y) { y.Draft = d; y.DraftBy = string.Format(Inv, "donor water under it at y={0:0.00}", wy); }
                else { y.Draft = geo; y.DraftBy = string.Format(Inv, "hull geometry (the donor water under it at y={0:0.00} would put the waterline {1:0.0} m off the keel)", wy, line - bottom); }
            }
            else { y.Draft = geo; y.DraftBy = "hull geometry (no donor water piece under it)"; }

            // clean clone: deactivate the source so nothing on the copy wakes up, copy, restore, strip everything but meshes and LODs
            bool was = t.gameObject.activeSelf;
            t.gameObject.SetActive(false);
            GameObject c = (GameObject)UnityEngine.Object.Instantiate(t.gameObject);
            t.gameObject.SetActive(was);
            c.name = "RT yacht proto " + t.name;
            c.transform.SetParent(Holder.transform, false);
            c.transform.localPosition = Vector3.zero;
            c.transform.localRotation = Quaternion.identity;
            c.transform.localScale = t.lossyScale;
            foreach (Component k in c.GetComponentsInChildren<Component>(true))
            {
                if (k == null || k is Transform || k is MeshFilter || k is MeshRenderer || k is LODGroup) continue;
                try { UnityEngine.Object.DestroyImmediate(k); } catch (Exception) { }
            }
            c.SetActive(false);
            y.Clone = c;
            return y;
        }

        /// <summary>Top of the donor water piece whose footprint holds p, the highest one not more than 5 m above p.</summary>
        private static bool LocalWaterY(Vector3 p, out float y)
        {
            y = float.NaN;
            foreach (Bounds b in WaterPieces)
            {
                if (p.x < b.min.x - 1f || p.x > b.max.x + 1f || p.z < b.min.z - 1f || p.z > b.max.z + 1f) continue;
                float top = b.max.y;
                if (top > p.y + 5f) continue;
                if (float.IsNaN(y) || top > y) y = top;
            }
            return !float.IsNaN(y);
        }

        /// <summary>Prototype for berth i (YachtPick: motor yachts, a sailboat only on some small berths), or null.</summary>
        public static YachtProto For(char cls, int i, float berthLen, float sailShare)
        {
            List<float> lens = new List<float>(); List<bool> sail = new List<bool>();
            foreach (YachtProto y in Yachts) { lens.Add(y.Length); sail.Add(y.Sail); }
            int k = YachtPick.Pick(cls, i, berthLen, lens, sail, sailShare);
            return k < 0 ? null : Yachts[k];
        }
    }

    internal partial class ArtBuild
    {
        private PackHarbour mHarbour;
        private bool mHarbourTried;
        private SeaMask mSea;
        private bool mSeaTried;
        private float[] mSkirtRings;
        private float mSkirtSeaMaxY = float.NaN, mSkirtOldSeaY = float.NaN;
        private int mSkirtSeaVerts;

        /// <summary>The pack's harbour.json, loaded once (null when the pack has none or it fails to load: StageHarbour warns).</summary>
        internal PackHarbour Harbour()
        {
            if (mHarbourTried) return mHarbour;
            mHarbourTried = true;
            if (mPack.HarbourFile == null) return null;
            string path = mPack.PathOf(mPack.HarbourFile);
            if (path == null || !System.IO.File.Exists(path)) return null;
            try { mHarbour = PackHarbour.Load(path); }
            catch (Exception e) { Main.Log("RT SEA: harbour.json could not be read: " + e.Message); mHarbour = null; }
            return mHarbour;
        }

        internal bool HasHarbour { get { return mPack.HarbourFile != null; } }

        /// <summary>
        /// Which skirt edge samples look out onto the pack's sea (SeaGeo.Mask), computed once before the terrain is meshed; also clamps the
        /// terrain's outer row over the sea to the sea bed (HarbourSeaEdgeClamp). Null when the pack has no harbour or the setting is off.
        /// </summary>
        internal SeaMask Sea()
        {
            if (mSeaTried) return mSea;
            mSeaTried = true;
            PackHarbour h = Harbour();
            if (h == null || h.Water.Count == 0) return null;
            if (!mS.HarbourSeaToHorizon) { Main.Log("RT SEA: sea to the horizon off (setting): the skirt over the sea rises to the mean boundary height as in 0.2.6"); return null; }
            PackTerrain t = mTerrain;
            CultureInfo inv = CultureInfo.InvariantCulture;
            mSea = SeaGeo.Mask(t.Nx, t.Nz, t.X0, t.Z0, t.Dx, t.Dz, h.IsSea, h.WaterLevel, h.WaterLevel - Mathf.Max(0f, h.SeabedDrop), Mathf.Max(0f, mS.HarbourSeaGapM));
            int seaS = mSea.SeaSamples, seaC = mSea.SeaCorners;
            int clamped = 0; float drop = 0f;
            if (mS.HarbourSeaEdgeClamp && mSea.Any) clamped = SeaGeo.ClampEdge(t.H, mSea, out drop);
            Main.Log(string.Format(inv, "RT SEA: sea level y={0:0.00} (harbour.json), sea bed y={1:0.00}; skirt edge samples over the sea {2} of {3} (sides z-min {4}, z-max {5}, x-min {6}, x-max {7}), corners {8} of 4; jetty gaps closed {12} run(s) / {13} samples (up to {14:0} m), corners added {15}; terrain edge cells lowered to the sea bed {9} (max {10:0.00} m{11})",
                mSea.SurfaceY, mSea.FloorY, seaS, mSea.Samples, Cnt(mSea.Side[0]), Cnt(mSea.Side[1]), Cnt(mSea.Side[2]), Cnt(mSea.Side[3]), seaC, clamped, drop, mS.HarbourSeaEdgeClamp ? "" : "; setting off", mSea.GapRuns, mSea.Filled, mS.HarbourSeaGapM, mSea.CornersFilled));
            if (!mSea.Any) Warn("harbour sea: no terrain edge looks out onto the pack's sea polygons (probe " + SeaGeo.ProbeOut + " m outside): no far sea, the skirt is unchanged");
            return mSea;
        }

        private static int Cnt(bool[] a) { int n = 0; if (a != null) foreach (bool b in a) if (b) n++; return n; }

        /// <summary>Called by StageSkirt after the skirt meshes are built: highest skirt vertex over the far sea (must be at or below the sea level).</summary>
        internal void NoteSkirtOverSea(List<MeshData> parts, float[] rings, float outerYOld)
        {
            mSkirtRings = rings;
            if (mSea == null || !mSea.Any) return;
            mSkirtSeaMaxY = float.NaN; mSkirtSeaVerts = 0;
            foreach (MeshData m in parts)
                for (int i = 0; i < m.Count; i++)
                {
                    V3 p = m.P[i];
                    if (!SeaGeo.OverFarSea(mSea, p.X, p.Z)) continue;
                    mSkirtSeaVerts++;
                    if (float.IsNaN(mSkirtSeaMaxY) || p.Y > mSkirtSeaMaxY) mSkirtSeaMaxY = p.Y;
                }
            mSkirtOldSeaY = outerYOld;
            Main.Log(string.Format(CultureInfo.InvariantCulture, "RT SEA: skirt over the sea: {0} vertices, highest y={1:0.00} (sea level {2:0.00}: {3}); sea rows coloured with the photo's sea bed out to the rim; 0.2.6 put them at y={4:0.0} from 400 m out ({5:0.0} m above the water) plus the rim lift",
                mSkirtSeaVerts, mSkirtSeaMaxY, mSea.SurfaceY, !(mSkirtSeaMaxY > mSea.SurfaceY) ? "at or below, OK" : "ABOVE THE SEA", outerYOld, outerYOld - mSea.SurfaceY));
            if (mSkirtSeaMaxY > mSea.SurfaceY) Warn("harbour sea: a skirt vertex over the sea is above the sea level (" + mSkirtSeaMaxY.ToString("0.00", CultureInfo.InvariantCulture) + ")");
        }

        /// <summary>The far sea (SeaGeo.FarSea) at the harbour level over the sea part of the skirt, out to the skirt radius.</summary>
        private int BuildFarSea(string matName)
        {
            SeaMask sea = Sea();
            if (sea == null || !sea.Any) return 0;
            float radius = Mathf.Clamp(mS.ArtSkirtRadiusKm, 2f, 80f) * 1000f;
            float[] rings = mSkirtRings ?? SkirtGeo.RingDistances(radius);
            MeshData md = SeaGeo.FarSea(sea, rings, 20f);
            if (md.Count == 0) return 0;
            int n = 0;
            foreach (MeshData part in MeshData.Split(md, 60000)) { NewObject("RT far sea " + n, part, new Material[] { ArtMaterials.Get(matName) }, false); n++; }
            float lo, hi; SeaGeo.YRange(md, out lo, out hi);
            Bump("farSeaMeshes", n);
            Main.Log(string.Format(CultureInfo.InvariantCulture, "RT SEA: far sea {0} mesh(es), {1} verts, y {2:0.00}..{3:0.00}, out to {4:0} m from the terrain edge ({5} rings), material {6}{7}",
                n, md.Count, lo, hi, rings[rings.Length - 1], rings.Length, ArtMaterials.Get(matName) != null ? ArtMaterials.Get(matName).name : matName, mSkirtRings == null ? " (skirt off or M1 ring: rings from the radius setting)" : ""));
            return n;
        }

        /// <summary>
        /// The in-game measure for Kyle's "ocean floating" note: every enabled renderer in the venue whose material is water, with world y
        /// range and source (pack = our art root, donor = anything else); a donor one is a WARNING. Harbour packs only.
        /// </summary>
        public void StageWaterCheck()
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            PackHarbour h = Harbour();
            float level = h != null ? h.WaterLevel : float.NaN;
            int seaN = 0, inland = 0, donor = 0; float dev = 0f;
            // CircuitScene's component walk did not include the generated root on some donor scenes, yielding a
            // misleading zero-water summary.  Always walk that root explicitly, then include other scene renderers.
            List<Renderer> scan = new List<Renderer>();
            HashSet<int> seen = new HashSet<int>();
            Renderer[] packRenderers = mRoot.GetComponentsInChildren<Renderer>(true);
            Renderer[] sceneRenderers = mScene.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in packRenderers) if (r != null && seen.Add(r.GetInstanceID())) scan.Add(r);
            foreach (Renderer r in sceneRenderers) if (r != null && seen.Add(r.GetInstanceID())) scan.Add(r);
            Main.Log("RT WATER: verifier scanning " + scan.Count + " renderer(s), including " + packRenderers.Length + " under RT Art");
            foreach (Renderer r in scan)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                bool water = false; string mname = "";
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    string sh = m.shader != null ? m.shader.name : "";
                    if (m == DonorHarbour.Water || m == DonorProps.Water || m.name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 || sh.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0)
                    { water = true; mname = m.name; break; }
                }
                if (!water) continue;
                Bounds b = r.bounds;
                bool pack = r.transform.IsChildOf(mRoot.transform);
                string kind;
                if (!pack) { kind = "donor"; donor++; }
                else if (r.name.StartsWith("RT water_harbour") || r.name.StartsWith("RT far sea")) { kind = "pack sea"; seaN++; dev = Mathf.Max(dev, Mathf.Max(Mathf.Abs(b.min.y - level), Mathf.Abs(b.max.y - level))); }
                else { kind = "pack inland (OSM pool/fountain at its ground)"; inland++; }
                Main.Log(string.Format(inv, "RT WATER: visible '{0}' source={1} y {2:0.00}..{3:0.00} x {4:0}..{5:0} z {6:0}..{7:0} material '{8}'",
                    r.name, kind, b.min.y, b.max.y, b.min.x, b.max.x, b.min.z, b.max.z, mname));
            }
            Main.Log(string.Format(inv, "RT WATER SUMMARY: sea level y={0:0.00}; visible water surfaces: pack sea {1} (max deviation from the sea level {2:0.00} m), pack inland {3}, donor {4}; skirt over the sea {5} vertices, highest y={6:0.00}{7}",
                level, seaN, dev, inland, donor, mSkirtSeaVerts, mSkirtSeaMaxY, mSea != null && mSea.Any ? "" : " (no sea mask: sea to the horizon off or no sea at the edge)"));
            if (donor > 0) Warn("harbour sea: " + donor + " donor water renderer(s) still visible (see the RT WATER lines)");
            if (dev > 0.05f) Warn("harbour sea: a pack sea surface is " + dev.ToString("0.00", inv) + " m off the sea level");
        }

        /// <summary>Sea / harbour water and moored yachts from the pack's harbour.json. Never fails the build (Stage catches).</summary>
        public void StageHarbour()
        {
            if (mPack.HarbourFile == null) { mStageLog.Add("harbour: none in pack"); return; }
            string path = mPack.PathOf(mPack.HarbourFile);
            if (path == null || !System.IO.File.Exists(path)) { Warn("harbour file " + mPack.HarbourFile + " missing"); return; }
            PackHarbour h = Harbour();
            if (h == null) h = PackHarbour.Load(path);   // throws with the parse error (Stage logs it)
            foreach (string p in h.Problems) Warn("harbour.json: " + p);
            float level = h.WaterLevel;
            CultureInfo inv = CultureInfo.InvariantCulture;

            // ---- water
            int polys = 0; double area = 0;
            string matName = "water";
            Material donorWater = DonorHarbour.Water != null && ArtMaterials.IsUsable(DonorHarbour.Water) ? DonorHarbour.Water : DonorProps.Water;
            if (mS.HarbourUseDonorWaterMaterial && donorWater != null && ArtMaterials.IsUsable(donorWater))
            {
                ArtMaterials.Set("water_harbour", donorWater); matName = "water_harbour";
                Main.Log("RT SEA: using requested donor water material '" + donorWater.name + "' (its shader controls the visible water surface)");
            }
            else
            {
                Main.Log("RT SEA: using generated harbour water at mesh sea level; donor animated water material " +
                    (donorWater != null ? "'" + donorWater.name + "' is disabled by default because it can float above the harbour" : "is unavailable"));
            }
            SeaMask sea = mS.HarbourWater ? Sea() : null;
            bool farSea = sea != null && sea.Any;
            int trisBefore = 0, trisAfter = 0;
            if (mS.HarbourWater)
            {
                foreach (HarbourWater w in h.Water)
                {
                    MeshData md = Ribbons.Drape(w.Outer, w.Holes, 250f, delegate (float x, float z) { return level; }, 0f, 20f, null);
                    trisBefore += md.T.Count / 3;
                    // with the far sea, the polygons' parts beyond the terrain (the builder extends them ~3 km straight out) are cut off: the far
                    // sea covers that at the same level out to the horizon, and two see-through water layers would double-blend (a hard edge)
                    if (farSea) md = SeaGeo.ClipToRect(md, mTerrain.X0, mTerrain.XMax, mTerrain.Z0, mTerrain.ZMax);
                    trisAfter += md.T.Count / 3;
                    if (md.Count == 0) continue;
                    AddMesh(matName, md);
                    polys++; area += w.AreaM2 > 0 ? w.AreaM2 : Poly.Area(w.Outer);
                }
                if (farSea)
                {
                    int fs = BuildFarSea(matName);
                    Main.Log(string.Format(inv, "RT SEA: harbour polygons cut to the terrain rectangle ({0} -> {1} triangles); far sea meshes {2}", trisBefore, trisAfter, fs));
                }
            }
            Bump("harbourWaterPolys", polys);

            // ---- pontoons (pack data from M2 w8; older packs have none)
            int pont = 0; double pontArea = 0;
            if (mS.HarbourPontoons && h.Pontoons.Count > 0)
            {
                float top = level + h.PontoonTop;
                foreach (HarbourWater w in h.Pontoons)
                {
                    MeshData md = Ribbons.Drape(w.Outer, w.Holes, 40f, delegate (float x, float z) { return top; }, 0f, 4f, null);
                    if (md.Count == 0) continue;
                    AddMesh("concrete", md);
                    pont++; pontArea += w.AreaM2 > 0 ? w.AreaM2 : Poly.Area(w.Outer);
                }
            }
            Bump("pontoons", pont);
            Main.Log(string.Format(inv, "RT ART: harbour pontoons {0} of {1} slabs, {2:0} m2, top y={3:0.00} ({4:0.0} m above the water), material concrete{5}",
                pont, h.Pontoons.Count, pontArea, level + h.PontoonTop, h.PontoonTop, mS.HarbourPontoons ? "" : " (setting off)"));

            // ---- yachts
            int placed = 0, big = 0, med = 0, small = 0, density = 0, noProto = 0, lodLevels = 0, motor = 0, sailN = 0, sailBigMed = 0;
            long verts0 = 0;
            float sMin = float.MaxValue, sMax = 0f, yLo = float.MaxValue, yHi = float.MinValue;
            Dictionary<string, int> perProto = new Dictionary<string, int>();
            if (mS.HarbourYachts && h.Yachts.Count > 0)
            {
                if (DonorHarbour.Yachts.Count == 0 || DonorHarbour.SceneId != mScene.GetInstanceID()) { noProto = h.Yachts.Count; Warn("harbour: " + h.Yachts.Count + " berths but no donor yacht prototypes (donor " + mPack.DonorScene + ")"); }
                else
                {
                    GameObject holder = new GameObject("RT Yachts");
                    holder.transform.SetParent(mRoot.transform, false);
                    for (int i = 0; i < h.Yachts.Count; i++)
                    {
                        HarbourYacht y = h.Yachts[i];
                        if (!PackHarbour.Keep(i, mS.HarbourYachtDensity)) { density++; continue; }
                        YachtProto p = DonorHarbour.For(y.Cls, i, y.Length, mS.HarbourSailShare);
                        if (p == null || p.Clone == null) { noProto++; continue; }
                        float s = YachtPick.Scale(y.Length, YachtPick.BeamOf(y.Length, y.Beam), p.Length, p.Beam, i, mS.HarbourYachtScaleJitter);
                        Quaternion rot = Quaternion.Euler(0f, y.YawDeg - p.ForwardYaw, 0f);
                        Vector3 c = rot * (p.Centre * s);
                        GameObject go = (GameObject)UnityEngine.Object.Instantiate(p.Clone);
                        go.name = "RT yacht " + i + " " + y.Cls;
                        go.transform.SetParent(holder.transform, false);
                        go.transform.position = new Vector3(y.X - c.x, level + p.Draft * s, y.Z - c.z);
                        go.transform.rotation = rot;
                        go.transform.localScale = p.Clone.transform.localScale * s;
                        go.SetActive(true);
                        placed++; lodLevels += p.Lods; verts0 += p.Verts0;
                        if (p.Sail) { sailN++; if (y.Cls != 's') sailBigMed++; } else motor++;
                        sMin = Mathf.Min(sMin, s); sMax = Mathf.Max(sMax, s);
                        float keel = go.transform.position.y + (p.Centre.y - p.Height * 0.5f) * s;   // hull bottom (world)
                        yLo = Mathf.Min(yLo, keel - level); yHi = Mathf.Max(yHi, keel - level);
                        int cnt; perProto.TryGetValue(p.Name, out cnt); perProto[p.Name] = cnt + 1;
                        if (y.Cls == 'b') big++; else if (y.Cls == 's') small++; else med++;
                        ArtRegistry.Add(new ArtMark("yacht", "yacht/" + i, new V2(y.X, y.Z), new V2(0, 0), y.Length, p.Beam * s, p.Height * s, level));
                    }
                }
            }
            Bump("yachts", placed);
            Bump("yachtsMotor", motor);
            Bump("yachtsSail", sailN);
            if (placed > 0)
            {
                List<string> pp = new List<string>();
                foreach (KeyValuePair<string, int> kv in perProto) pp.Add(kv.Key + "=" + kv.Value);
                Main.Log(string.Format(inv, "RT ART: harbour yachts by kind: motor {0} / sail {1} ({2:0.0} % motor; sail on big/medium berths {3}; setting Sailboat share {4:0.00}, small berths only); by model: {5}; scale {6:0.00}..{7:0.00} (jitter {8:0.00}); keel below the water {9:0.0}..{10:0.0} m",
                    motor, sailN, 100f * motor / placed, sailBigMed, mS.HarbourSailShare, string.Join(", ", pp.ToArray()), sMin, sMax, mS.HarbourYachtScaleJitter, -yHi, -yLo));
                if (motor == 0) Warn("harbour: no motor yacht among the donor's models: every berth got a sailboat");
            }
            Main.Logger.Log(string.Format(inv, "RT ART: harbour water polys {0} area {1:0} m2 y={2:0.00} material {3}; yachts placed {4} (big {5} / med {6} / small {7}), skipped {8} (density {9}, no donor prototype {10}) of {11} berths; {12} prototypes, LOD levels per yacht {13:0.0}, LOD0 verts if all near {14}",
                polys, area, level, matName == "water" ? "generated" : ArtMaterials.Get(matName).name, placed, big, med, small, density + noProto, density, noProto, h.Yachts.Count,
                DonorHarbour.Yachts.Count, placed > 0 ? (float)lodLevels / placed : 0f, verts0));
        }
    }
}
