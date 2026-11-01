using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace RealTracks
{
    /// <summary>One mesh of a harvested donor prop, in the prop's own space (world scale, the prop's pivot at the origin).</summary>
    public class RtPart
    {
        public Mesh Mesh;
        public Material[] Mats;
        public Matrix4x4 Local;
    }

    /// <summary>
    /// A donor prop copied out of the Phoenix scene before its art is disabled. Only props whose renderers are NOT part of a
    /// static batch are taken (a batched renderer's mesh is the combined batch mesh and cannot be reused).
    /// </summary>
    public class RtProto
    {
        public string Name, Kind, Path;
        public int RenderersInSubtree, BatchedRenderers, Verts;
        public bool Accepted;
        public string Why = "";
        public readonly List<RtPart> Parts = new List<RtPart>();
        public Vector3 Min, Max;
        public GameObject Clone;
        public Vector3 Size { get { return Max - Min; } }
    }

    /// <summary>Donor props and materials harvested from the scene (see <see cref="ArtGenerator.HarvestDonor"/>).</summary>
    public static class DonorProps
    {
        public static readonly List<RtProto> All = new List<RtProto>();
        public static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        public static Material Water, Crowd, Asphalt, Concrete;
        public static GameObject Holder;
        public static int SceneId;
        public static readonly List<string> Report = new List<string>();

        public static void Clear()
        {
            All.Clear(); Materials.Clear(); Report.Clear();
            Water = Crowd = Asphalt = Concrete = null;
            if (Holder != null) UnityEngine.Object.Destroy(Holder);
            Holder = null;
        }

        public static List<RtProto> Accepted(string kind)
        {
            List<RtProto> l = new List<RtProto>();
            foreach (RtProto p in All) if (p.Accepted && p.Kind == kind) l.Add(p);
            return l;
        }

        // ------------------------------------------------------------------ harvest

        private struct Pattern { public string Kind; public string Match; public bool StartsWith; public int Max; }

        private static readonly Pattern[] Patterns =
        {
            new Pattern { Kind = "grandstand", Match = "grandstand", StartsWith = false, Max = 5 },
            new Pattern { Kind = "grandstand", Match = "smallstand", StartsWith = false, Max = 3 },
            new Pattern { Kind = "barrier", Match = "concrete_barrier_01", StartsWith = true, Max = 2 },
            new Pattern { Kind = "tent", Match = "longtent_01", StartsWith = true, Max = 1 },
            new Pattern { Kind = "tree", Match = "tree_cluster_c", StartsWith = true, Max = 6 },
            new Pattern { Kind = "tree", Match = "shrub_cluster_c", StartsWith = true, Max = 4 },
        };

        private static string PathOf(Transform t, Transform root)
        {
            StringBuilder sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null && p != root; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        public static void Harvest(CircuitScene scene, BuiltVenue built)
        {
            Clear();
            SceneId = scene.GetInstanceID();
            Holder = new GameObject("RT Donor Props");
            Holder.SetActive(false);
            Holder.transform.SetParent(scene.transform, false);

            Transform root = scene.transform;
            Transform[] all = scene.GetComponentsInChildren<Transform>(true);
            Dictionary<string, int> perPattern = new Dictionary<string, int>();
            HashSet<Transform> taken = new HashSet<Transform>();
            int examined = 0;
            foreach (Transform t in all)
            {
                if (t == root || t == Holder.transform || t.IsChildOf(Holder.transform)) continue;
                string lname = t.name.ToLowerInvariant();
                Pattern? hit = null;
                foreach (Pattern p in Patterns)
                {
                    if (p.StartsWith ? lname.StartsWith(p.Match) : lname.Contains(p.Match)) { hit = p; break; }
                }
                if (hit == null) continue;
                Pattern pat = hit.Value;
                // outermost match only: skip anything inside an already harvested prop
                bool inside = false;
                for (Transform a = t.parent; a != null && a != root; a = a.parent) if (taken.Contains(a)) { inside = true; break; }
                if (inside) continue;
                string pk = pat.Kind + ":" + pat.Match;
                int have;
                perPattern.TryGetValue(pk, out have);
                if (have >= pat.Max) continue;
                examined++;
                RtProto proto = TryHarvest(t, pat.Kind, root);
                if (proto == null) continue;
                All.Add(proto);
                if (proto.Accepted) { taken.Add(t); perPattern[pk] = have + 1; }
                Report.Add("HARVEST " + proto.Kind + " '" + proto.Name + "' [" + proto.Path + "] renderers " + proto.RenderersInSubtree +
                           " (static-batched " + proto.BatchedRenderers + ") verts " + proto.Verts +
                           (proto.Accepted ? " size " + Fmt(proto.Size) + " -> taken" : " -> skipped: " + proto.Why));
            }
            HarvestMaterials(scene, all);

            int ok = 0, no = 0;
            foreach (RtProto p in All) { if (p.Accepted) ok++; else no++; }
            string mats = "water=" + Nm(Water) + ", crowd=" + Nm(Crowd) + ", asphalt=" + Nm(Asphalt) + ", concrete=" + Nm(Concrete) + " (of " + Materials.Count + " distinct donor materials)";
            Main.Logger.Log("RT ART: harvested " + ok + " donor props (" + no + " skipped as static-batched / unreadable / unusable); " + mats);
            foreach (string r in Report) Main.Log("RT ART: " + r);
            built.Counts["donorProps"] = ok;
        }

        private static string Nm(Material m) { return m == null ? "none" : m.name; }
        private static string Fmt(Vector3 v) { return v.x.ToString("0.#") + "x" + v.y.ToString("0.#") + "x" + v.z.ToString("0.#") + " m"; }

        private static RtProto TryHarvest(Transform t, string kind, Transform root)
        {
            RtProto p = new RtProto();
            p.Name = t.name; p.Kind = kind; p.Path = PathOf(t, root);
            Renderer[] subtree = t.GetComponentsInChildren<Renderer>(true);
            p.RenderersInSubtree = subtree.Length;
            foreach (Renderer r in subtree) if (r.isPartOfStaticBatch) p.BatchedRenderers++;

            List<Renderer> use = new List<Renderer>();
            LODGroup lg = t.GetComponent<LODGroup>();
            if (lg != null)
            {
                LOD[] lods = lg.GetLODs();
                if (lods != null && lods.Length > 0 && lods[0].renderers != null)
                    foreach (Renderer r in lods[0].renderers) if (r != null) use.Add(r);
            }
            else
            {
                foreach (Renderer r in subtree) use.Add(r);
            }
            if (use.Count == 0) { p.Why = "no renderers"; return p; }
            if (use.Count > 14) { p.Why = "too many renderers (" + use.Count + ") for one prop"; return p; }

            Matrix4x4 toProto = Matrix4x4.TRS(Vector3.zero, Quaternion.Inverse(t.rotation), Vector3.one) * Matrix4x4.TRS(-t.position, Quaternion.identity, Vector3.one);
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (Renderer r in use)
            {
                if (r.isPartOfStaticBatch) { p.Why = "static-batched renderer '" + r.name + "'"; p.BatchedRenderers = Math.Max(p.BatchedRenderers, 1); return p; }
                if (!(r is MeshRenderer)) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Mesh mesh = mf.sharedMesh;
                if (mesh.vertexCount == 0) continue;
                if (!mesh.isReadable) { p.Why = "mesh '" + mesh.name + "' is not readable"; return p; }
                if (mesh.vertexCount > 30000) { p.Why = "mesh '" + mesh.name + "' has " + mesh.vertexCount + " verts"; return p; }
                Material[] mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;
                bool bad = false;
                foreach (Material m in mats) if (!ArtMaterials.IsUsable(m)) { bad = true; break; }
                if (bad) { p.Why = "a material or shader of '" + r.name + "' is missing/unsupported"; return p; }
                RtPart part = new RtPart();
                part.Mesh = mesh; part.Mats = mats;
                part.Local = toProto * r.transform.localToWorldMatrix;
                p.Parts.Add(part);
                p.Verts += mesh.vertexCount;
                Bounds b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = b.center + new Vector3(((i & 1) == 0 ? -1 : 1) * b.extents.x, ((i & 2) == 0 ? -1 : 1) * b.extents.y, ((i & 4) == 0 ? -1 : 1) * b.extents.z);
                    Vector3 w = part.Local.MultiplyPoint3x4(c);
                    min = Vector3.Min(min, w); max = Vector3.Max(max, w);
                }
            }
            if (p.Parts.Count == 0) { p.Why = "no mesh renderers"; return p; }
            p.Min = min; p.Max = max;
            Vector3 size = max - min;
            if (size.x > 300f || size.z > 300f || size.y > 150f) { p.Why = "too large (" + Fmt(size) + ")"; return p; }
            if (size.x < 0.05f && size.z < 0.05f) { p.Why = "degenerate size"; return p; }

            // clean inactive clone (meshes + materials only, no scripts, nothing that could run or tooltip) parked under the holder
            GameObject clone = new GameObject("RT proto " + p.Name);
            clone.SetActive(false);
            clone.transform.SetParent(Holder.transform, false);
            foreach (RtPart part in p.Parts)
            {
                GameObject child = new GameObject("part");
                child.transform.SetParent(clone.transform, false);
                Vector3 c0 = part.Local.GetColumn(0), c1 = part.Local.GetColumn(1), c2 = part.Local.GetColumn(2);
                child.transform.localPosition = part.Local.GetColumn(3);
                if (c2.sqrMagnitude > 1e-8f && c1.sqrMagnitude > 1e-8f) child.transform.localRotation = Quaternion.LookRotation(c2, c1);
                child.transform.localScale = new Vector3(c0.magnitude, c1.magnitude, c2.magnitude);
                child.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                child.AddComponent<MeshRenderer>().sharedMaterials = part.Mats;
            }
            p.Clone = clone;
            p.Accepted = true;
            return p;
        }

        private static void HarvestMaterials(CircuitScene scene, Transform[] all)
        {
            foreach (Renderer r in scene.GetComponentsInChildren<Renderer>(true))
            {
                Material[] ms;
                try { ms = r.sharedMaterials; } catch (Exception) { continue; }
                if (ms == null) continue;
                string rn = r.name.ToLowerInvariant();
                foreach (Material m in ms)
                {
                    if (m == null) continue;
                    if (!Materials.ContainsKey(m.name)) Materials[m.name] = m;
                    string mn = m.name.ToLowerInvariant();
                    if (Water == null && (rn.Contains("river") || rn.Contains("water") || mn.Contains("river") || mn.Contains("water")) && ArtMaterials.IsUsable(m)) Water = m;
                    if (Asphalt == null && mn.Contains("asphalt_01") && ArtMaterials.IsUsable(m)) Asphalt = m;
                    if (Concrete == null && mn.Contains("concrete") && ArtMaterials.IsUsable(m)) Concrete = m;
                }
            }
            try
            {
                FieldInfo f = typeof(CircuitScene).GetField("crowdMaterial", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) { Material m = f.GetValue(scene) as Material; if (ArtMaterials.IsUsable(m)) Crowd = m; }
            }
            catch (Exception) { }
            // any working donor material lends its shader if no built-in one can be found
            foreach (Material m in Materials.Values) if (ArtMaterials.IsUsable(m)) { ArtMaterials.SetShaderDonor(m); break; }
        }
    }
}
