using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace RealTracks
{
    /// <summary>
    /// The drivable surface: a ribbon along the pack's centreline at its real width (and the pit lane, entry, exit and apron),
    /// on the Env_Track layer with MeshColliders. 3D cars ray-cast down onto that layer (UnityVehicle.Update) to sit on the road,
    /// and TeamPitCrew / PathSpline.SetPositionsToGatePlane do the same.
    /// </summary>
    public static class VenueRoad
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly AccessTools.FieldRef<CircuitScene, bool> sCollisionActive =
            AccessTools.FieldRefAccess<CircuitScene, bool>("mIsPathCollisionMeshActive");
        private static readonly AccessTools.FieldRef<CircuitScene, int> sCollisionMask =
            AccessTools.FieldRefAccess<CircuitScene, int>("mPathCollisionLayerMask");

        /// <summary>Rows per chunk: 2 vertices per row, so a chunk is at most 2 * MaxRows = 24,000 vertices (limit for 16-bit meshes is 65,000).</summary>
        private const int MaxRows = 12000;
        private const float Spacing = 2f;

        private struct Row
        {
            public Vector3 P, Right, Fwd;
            public float Dist;
        }

        private class Mats
        {
            public Material Road, Plain, Concrete;
        }

        public static Transform EnsureHolder(CircuitScene scene, string name)
        {
            Transform t = scene.transform.Find(name);
            if (t != null) return t;
            GameObject go = new GameObject(name);
            go.transform.SetParent(scene.transform, false);
            // world identity regardless of the scene root's own transform
            PinToWorld(go.transform);
            return go.transform;
        }

        /// <summary>World position 0, rotation identity and world scale 1, whatever the parent's transform.</summary>
        public static void PinToWorld(Transform t)
        {
            t.position = Vector3.zero;
            t.rotation = Quaternion.identity;
            Transform p = t.parent;
            Vector3 ps = p != null ? p.lossyScale : Vector3.one;
            t.localScale = new Vector3(ps.x != 0 ? 1f / ps.x : 1f, ps.y != 0 ? 1f / ps.y : 1f, ps.z != 0 ? 1f / ps.z : 1f);
        }

        // ------------------------------------------------------------------ build

        public static void Build(CircuitScene scene, PackLayout layout, LayoutPaths lp, PathData track, Material fallback, ScenePlan plan, BuiltVenue built)
        {
            Settings st = Main.Settings;
            int envTrack = LayerMask.NameToLayer("Env_Track");
            if (envTrack < 0) { VenueBuilder.Warn(built, "the Env_Track layer does not exist; no road generated."); return; }
            Transform holder = EnsureHolder(scene, "RealTracks Road");
            Vector3 rs = scene.transform.lossyScale;
            if (Mathf.Abs(rs.x - 1f) > 1e-3f || Mathf.Abs(rs.y - 1f) > 1e-3f || Mathf.Abs(rs.z - 1f) > 1e-3f)
                VenueBuilder.Warn(built, "the scene root is scaled " + rs + "; generated meshes assume a unit-scale root.");
            Mats m = MakeMats(fallback);

            Stats stats = new Stats();

            // ---- the track
            List<Row> rows = SampleRows(track.centerLineSpline, Spacing);
            if (rows.Count < 4) throw new Exception("the track spline produced no usable samples");
            float total = lp.S[lp.S.Length - 1] + Vector3.Distance(lp.Track[lp.Track.Count - 1], lp.Track[0]);
            float splineLen = track.centerLineSpline.length;
            float[] a = new float[rows.Count], b = new float[rows.Count];
            float shoulder = Mathf.Clamp(st.VenueRoadAiMarginM, 0f, 3f);
            float wMin = float.MaxValue, wMax = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                float wl, wr;
                WidthAt(lp, total, rows[i].Dist / splineLen, out wl, out wr);
                a[i] = -(wl + shoulder);
                b[i] = wr + shoulder;
                wMin = Mathf.Min(wMin, wl + wr);
                wMax = Mathf.Max(wMax, wl + wr);
            }
            Strip(holder, "RT Road", rows, a, b, 0.02f, m.Road, 14f, envTrack, true, built, stats);
            built.Counts["roadRows"] = rows.Count;
            built.Counts["roadWidthMinCm"] = Mathf.RoundToInt(wMin * 100f);
            built.Counts["roadWidthMaxCm"] = Mathf.RoundToInt(wMax * 100f);

            // ---- the pit lane, entry, exit (centred on their paths; the garage side gets most of the width) and the apron
            float W = Mathf.Clamp(st.VenuePitLaneWidth, 8f, 20f);
            PathData lane = scene.pitlanePath.data;
            float side = GarageSide(lane, lp);
            float away = W * 0.3f, toward = W - away;
            List<Row> laneRows = SampleRows(lane.centerLineSpline, Spacing);
            if (laneRows.Count >= 2)
            {
                float[] la = new float[laneRows.Count], lb = new float[laneRows.Count];
                for (int i = 0; i < laneRows.Count; i++)
                {
                    if (side > 0f) { la[i] = -away; lb[i] = toward; } else { la[i] = -toward; lb[i] = away; }
                }
                Strip(holder, "RT Pit Lane", laneRows, la, lb, 0.04f, m.Plain, 14f, envTrack, true, built, stats);

                if (st.VenuePitApron)
                {
                    float outer = ApronOuter(lane, lp, side, toward + 8f);
                    float[] pa = new float[laneRows.Count], pb = new float[laneRows.Count];
                    for (int i = 0; i < laneRows.Count; i++)
                    {
                        if (side > 0f) { pa[i] = toward - 0.2f; pb[i] = outer; } else { pa[i] = -outer; pb[i] = -toward + 0.2f; }
                    }
                    Strip(holder, "RT Pit Apron", laneRows, pa, pb, 0.03f, m.Concrete, 10f, envTrack, true, built, stats);
                    built.Counts["pitApronOuterCm"] = Mathf.RoundToInt(outer * 100f);
                }
            }
            LinkStrip(holder, "RT Pit Entry", scene.pitlaneEntryPath.data, W * 0.5f, m.Plain, envTrack, built, stats);
            LinkStrip(holder, "RT Pit Exit", scene.pitlaneExitPath.data, W * 0.5f, m.Plain, envTrack, built, stats);

            built.Counts["roadChunks"] = stats.Chunks;
            built.Counts["roadVerts"] = stats.Verts;
            built.Counts["roadMaxChunkVerts"] = stats.MaxChunk;
            built.Counts["roadTris"] = stats.Tris / 3;
            Main.Log(string.Format(Inv, "RT VENUE ROAD: {0} chunk(s) on layer {1} ({2}), {3} verts total, largest chunk {4} verts (< 65,000), width {5:0.0}..{6:0.0} m, pit lane {7:0.0} m (garages on the {8}), {9} MeshCollider(s).",
                stats.Chunks, envTrack, LayerMask.LayerToName(envTrack), stats.Verts, stats.MaxChunk, wMin + 2f * shoulder, wMax + 2f * shoulder, W, side > 0f ? "right" : "left", CountColliders(built)));
        }

        private class Stats
        {
            public int Chunks, Verts, MaxChunk, Tris;
        }

        private static void LinkStrip(Transform holder, string name, PathData d, float half, Material mat, int layer, BuiltVenue built, Stats stats)
        {
            List<Row> rows = SampleRows(d.centerLineSpline, Spacing);
            if (rows.Count < 2) return;
            float[] a = new float[rows.Count], b = new float[rows.Count];
            for (int i = 0; i < rows.Count; i++) { a[i] = -half; b[i] = half; }
            Strip(holder, name, rows, a, b, 0.04f, mat, 14f, layer, true, built, stats);
        }

        // ------------------------------------------------------------------ sampling

        private static List<Row> SampleRows(PathSpline sp, float spacing)
        {
            List<Row> list = new List<Row>();
            if (sp == null || sp.splinePositions == null || sp.splinePositions.Length < 2) return list;
            PathSpline.SplinePosition[] a = sp.splinePositions;
            float since = spacing;
            Vector3 prev = a[0].position;
            Vector3 lastRight = Vector3.right;
            for (int i = 0; i < a.Length; i++)
            {
                since += Vector3.Distance(prev, a[i].position);
                prev = a[i].position;
                if (since < spacing && i != a.Length - 1) continue;
                since = 0f;
                Vector3 fwd = a[i].forward;
                Vector3 right = a[i].right;
                right.y = 0f;
                if (right.sqrMagnitude < 1e-4f) right = lastRight; else right.Normalize();
                lastRight = right;
                Row r = new Row();
                r.P = a[i].position;
                r.Right = right;
                r.Fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
                r.Dist = a[i].pathDistance;
                list.Add(r);
            }
            return list;
        }

        /// <summary>Half-widths at a fraction u (0..1) of the lap, from the pack's per-point width table.</summary>
        private static void WidthAt(LayoutPaths lp, float total, float u, out float wl, out float wr)
        {
            int n = lp.S.Length;
            float s = Mathf.Clamp01(u) * total;
            int lo = 0, hi = n - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (lp.S[mid] <= s) lo = mid; else hi = mid - 1;
            }
            int i0 = lo, i1 = (lo + 1) % n;
            float s0 = lp.S[i0];
            float s1 = i1 == 0 ? total : lp.S[i1];
            float t = s1 - s0 > 1e-4f ? Mathf.Clamp01((s - s0) / (s1 - s0)) : 0f;
            wl = Mathf.Max(2f, Mathf.Lerp(lp.WidthLeft[i0], lp.WidthLeft[i1], t));
            wr = Mathf.Max(2f, Mathf.Lerp(lp.WidthRight[i0], lp.WidthRight[i1], t));
        }

        /// <summary>+1 if the garages are on the right of the pit lane's direction of travel, -1 if on the left (measured, not read from the manifest).</summary>
        private static float GarageSide(PathData lane, LayoutPaths lp)
        {
            PathSpline sp = lane.centerLineSpline;
            foreach (GarageSlot g in lp.Garages.Values)
            {
                int id = sp.FindClosestPointIDToPosition(g.Position);
                Vector3 d = g.Position - sp.splinePositions[id].position;
                d.y = 0f;
                float v = Vector3.Dot(d, sp.splinePositions[id].right);
                if (Mathf.Abs(v) > 0.5f) return v >= 0f ? 1f : -1f;
            }
            return 1f;
        }

        /// <summary>How far from the pit lane centreline (towards the garages) the pitbox and garage paths reach, plus a margin.</summary>
        private static float ApronOuter(PathData lane, LayoutPaths lp, float side, float minimum)
        {
            PathSpline sp = lane.centerLineSpline;
            float far = minimum;
            foreach (GarageSlot g in lp.Garages.Values)
            {
                List<Vector3>[] lists = { g.PitboxEntry, g.PitboxExit, g.GarageEntry[0], g.GarageExit[0], g.GarageEntry[1], g.GarageExit[1] };
                foreach (List<Vector3> l in lists)
                {
                    foreach (Vector3 p in l)
                    {
                        int id = sp.FindClosestPointIDToPosition(p);
                        Vector3 d = p - sp.splinePositions[id].position;
                        d.y = 0f;
                        float v = Vector3.Dot(d, sp.splinePositions[id].right) * side;
                        if (v + 3f > far) far = v + 3f;
                    }
                }
            }
            return Mathf.Min(far, 60f);
        }

        // ------------------------------------------------------------------ mesh

        private static void Strip(Transform holder, string name, List<Row> rows, float[] off0, float[] off1, float lift, Material mat, float tile,
                                  int layer, bool collide, BuiltVenue built, Stats stats)
        {
            int start = 0, part = 0;
            while (start < rows.Count - 1)
            {
                int end = Mathf.Min(start + MaxRows, rows.Count - 1); // inclusive; adjacent chunks share one row
                int count = end - start + 1;
                Vector3[] v = new Vector3[count * 2];
                Vector3[] nrm = new Vector3[count * 2];
                Vector2[] uv = new Vector2[count * 2];
                int[] tri = new int[(count - 1) * 12];
                for (int k = 0; k < count; k++)
                {
                    Row r = rows[start + k];
                    Vector3 lift3 = Vector3.up * lift;
                    v[k * 2] = r.P + r.Right * off0[start + k] + lift3;
                    v[k * 2 + 1] = r.P + r.Right * off1[start + k] + lift3;
                    Vector3 n = Vector3.Cross(r.Fwd, r.Right);
                    n = n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.up;
                    if (n.y < 0f) n = -n;
                    nrm[k * 2] = n;
                    nrm[k * 2 + 1] = n;
                    float vv = r.Dist / tile;
                    uv[k * 2] = new Vector2(0f, vv);
                    uv[k * 2 + 1] = new Vector2(1f, vv);
                }
                int t = 0;
                for (int k = 0; k < count - 1; k++)
                {
                    int i = k * 2;
                    // up-facing (a, c, b), (b, c, d) then the reverse, so a ray from either side hits the collider
                    tri[t++] = i; tri[t++] = i + 2; tri[t++] = i + 1;
                    tri[t++] = i + 1; tri[t++] = i + 2; tri[t++] = i + 3;
                    tri[t++] = i; tri[t++] = i + 1; tri[t++] = i + 2;
                    tri[t++] = i + 1; tri[t++] = i + 3; tri[t++] = i + 2;
                }
                Mesh mesh = new Mesh();
                mesh.name = name + (part > 0 || end < rows.Count - 1 ? " " + (part + 1) : "");
                mesh.vertices = v;
                mesh.normals = nrm;
                mesh.uv = uv;
                mesh.triangles = tri;
                mesh.RecalculateBounds();

                GameObject go = new GameObject(mesh.name);
                go.layer = layer;
                go.transform.SetParent(holder, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.receiveShadows = true;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                built.RoadObjects.Add(go);

                stats.Chunks++;
                stats.Verts += v.Length;
                stats.MaxChunk = Mathf.Max(stats.MaxChunk, v.Length);
                stats.Tris += tri.Length;
                part++;
                start = end;
            }
        }

        // ------------------------------------------------------------------ materials

        private static Shader PickShader(Material fallback)
        {
            string[] names = { "Standard", "Legacy Shaders/Diffuse", "Diffuse", "Mobile/Diffuse" };
            foreach (string n in names)
            {
                Shader s = Shader.Find(n);
                if (s != null) return s;
            }
            return fallback != null ? fallback.shader : null;
        }

        private static Material MakeMaterial(string name, Texture2D tex, Shader sh, Material fallback)
        {
            Material m = sh != null ? new Material(sh) : new Material(fallback);
            m.name = name;
            m.mainTexture = tex;
            if (m.HasProperty("_Color")) m.color = Color.white;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.12f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            return m;
        }

        private static Texture2D Tex(int w, int h, Func<int, int, System.Random, Color> f)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            System.Random rnd = new System.Random(4321);
            Color[] px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f(x, y, rnd);
            t.SetPixels(px);
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 8;
            t.Apply(true);
            return t;
        }

        private static Mats MakeMats(Material fallback)
        {
            Shader sh = PickShader(fallback);
            if (sh == null && fallback == null) throw new Exception("no shader found for the road material");
            Mats m = new Mats();
            // u runs across the road (0..1 = full width), v along it (1 = 14 m): white edge lines at both edges.
            Texture2D road = Tex(256, 256, delegate (int x, int y, System.Random r)
            {
                float u = x / 255f;
                if ((u > 0.012f && u < 0.026f) || (u > 0.974f && u < 0.988f)) return new Color(0.92f, 0.92f, 0.90f);
                float g = 0.20f + (float)r.NextDouble() * 0.05f;
                return new Color(g, g, g * 1.02f);
            });
            Texture2D plain = Tex(256, 256, delegate (int x, int y, System.Random r)
            {
                float g = 0.22f + (float)r.NextDouble() * 0.05f;
                return new Color(g, g, g * 1.02f);
            });
            Texture2D concrete = Tex(128, 128, delegate (int x, int y, System.Random r)
            {
                float g = 0.52f + (float)r.NextDouble() * 0.06f;
                if (x % 64 < 1 || y % 64 < 1) g -= 0.08f; // expansion joints
                return new Color(g, g, g * 0.98f);
            });
            m.Road = MakeMaterial("RT Asphalt", road, sh, fallback);
            m.Plain = MakeMaterial("RT Asphalt Plain", plain, sh, fallback);
            m.Concrete = MakeMaterial("RT Concrete", concrete, sh, fallback);
            Main.Log("RT VENUE ROAD: shader '" + (m.Road.shader != null ? m.Road.shader.name : "none") + "'.");
            return m;
        }

        // ------------------------------------------------------------------ collision state

        /// <summary>
        /// CircuitScene.Start only adds colliders to Env_Track meshes that exist when it runs and sets the private
        /// mIsPathCollisionMeshActive / mPathCollisionLayerMask. Whatever the order, make the end state explicit.
        /// </summary>
        public static void SetCollisionState(CircuitScene scene, BuiltVenue built)
        {
            int envTrack = LayerMask.NameToLayer("Env_Track");
            bool active = envTrack >= 0 && built.RoadObjects.Count > 0;
            sCollisionActive(scene) = active;
            sCollisionMask(scene) = envTrack >= 0 ? 1 << envTrack : 0;
            built.Counts["pathCollisionActive"] = active ? 1 : 0;
        }

        public static int DedupeColliders(BuiltVenue built)
        {
            int removed = 0;
            foreach (GameObject go in built.RoadObjects)
            {
                if (go == null) continue;
                MeshCollider[] cs = go.GetComponents<MeshCollider>();
                for (int i = 1; i < cs.Length; i++) { UnityEngine.Object.Destroy(cs[i]); removed++; }
                if (cs.Length == 0 && go.GetComponent<MeshFilter>() != null)
                {
                    go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                }
            }
            return removed;
        }

        private static int CountColliders(BuiltVenue built)
        {
            int n = 0;
            foreach (GameObject go in built.RoadObjects) if (go != null) n += go.GetComponents<MeshCollider>().Length;
            return n;
        }

        /// <summary>After the art generator ran: only our road may be on Env_Track. Anything else found there is moved off (and reported).</summary>
        public static void CheckTrackLayerOwnership(CircuitScene scene, BuiltVenue built)
        {
            int envTrack = LayerMask.NameToLayer("Env_Track");
            if (envTrack < 0) return;
            HashSet<GameObject> ours = new HashSet<GameObject>(built.RoadObjects);
            int onLayer = 0, foreign = 0, noCollider = 0;
            foreach (MeshFilter mf in scene.GetComponentsInChildren<MeshFilter>(true))
            {
                GameObject go = mf.gameObject;
                if (go.layer != envTrack) continue;
                onLayer++;
                if (!ours.Contains(go))
                {
                    foreign++;
                    go.layer = 0;
                    Collider c = go.GetComponent<Collider>();
                    if (c != null) c.enabled = false;
                    VenueBuilder.Warn(built, "'" + go.name + "' was on the Env_Track layer but is not part of the road; moved to Default.");
                }
                else if (go.GetComponent<MeshCollider>() == null) noCollider++;
            }
            built.Counts["envTrackMeshes"] = onLayer - foreign;
            built.Counts["envTrackForeignMoved"] = foreign;
            Main.Log("RT VENUE ROAD STATE: Env_Track layer holds " + (onLayer - foreign) + " mesh(es) (all generated road), " + foreign + " foreign moved off, " + noCollider +
                     " without a MeshCollider; isPathCollisionMeshActive=" + scene.isPathCollisionMeshActive + ", pathCollisionLayerMask=" + scene.pathCollisionLayerMask +
                     " (expected " + (1 << envTrack) + ").");
        }
    }
}
