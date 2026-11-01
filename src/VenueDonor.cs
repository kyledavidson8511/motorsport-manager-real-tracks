using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RealTracks
{
    /// <summary>What the donor scene holds, gathered read-only before anything changes.</summary>
    public class DonorSurvey
    {
        public readonly List<Renderer> ToHide = new List<Renderer>();
        public readonly List<Renderer> EnvTrack = new List<Renderer>();
        public Material Asphalt;
        public int Total, Kept, KeptStaticBatched, HiddenStaticBatched;
        public readonly SortedDictionary<string, int[]> Roots = new SortedDictionary<string, int[]>(); // [renderers, staticBatched, hidden?1:0]
        public readonly SortedDictionary<int, int[]> Layers = new SortedDictionary<int, int[]>();      // [renderers, staticBatched]
    }

    /// <summary>
    /// Donor scene art: survey, take the donor's drivable-surface meshes off the Env_Track layer, hide every art renderer.
    /// Renderers are only disabled (never destroyed, never SetActive(false): SetTrackLayout re-activates the TrackVariations
    /// objects and PreferenceTag re-activates crowds/decals, but neither touches Renderer.enabled).
    /// </summary>
    public static class VenueDonor
    {
        /// <summary>Top-level children of the CircuitScene root that stay as they are (game machinery, not art).</summary>
        private static readonly HashSet<string> sKeepRoots = new HashSet<string>
        {
            "Paths",            // the path components (their renderers are switched off by PathDataComponent.Awake); also the sprinklers
            "TeamGarages",      // TeamPitGarage + pit crew + pit fire (created by the game; no garage building art lives here)
            "Cameras",          // the eight scene cameras (no renderers)
            "Environment",      // EnvironmentManager + uSky (sun, moon, fog, clouds)
            "Debug",
            "Minimap Bounds",
            "Env_Art_Lighting"  // reflection probe + light probes
        };

        public static bool IsKeptRoot(string name) { return sKeepRoots.Contains(name); }

        private static string RootOf(Transform sceneRoot, Transform t)
        {
            if (t == sceneRoot) return "(scene root)";
            while (t.parent != null && t.parent != sceneRoot) t = t.parent;
            return t.parent == sceneRoot ? t.name : null;
        }

        public static DonorSurvey Survey(CircuitScene scene)
        {
            DonorSurvey s = new DonorSurvey();
            Transform root = scene.transform;
            int envTrack = LayerMask.NameToLayer("Env_Track");
            Renderer[] all = scene.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in all)
            {
                if (r == null) continue;
                string rn = RootOf(root, r.transform);
                if (rn == null) continue;
                s.Total++;
                bool sb = r.isPartOfStaticBatch;
                bool keep = sKeepRoots.Contains(rn) || rn == "(scene root)";
                int[] rc;
                if (!s.Roots.TryGetValue(rn, out rc)) { rc = new int[3]; s.Roots[rn] = rc; }
                rc[0]++;
                if (sb) rc[1]++;
                rc[2] = keep ? 0 : 1;
                int layer = r.gameObject.layer;
                int[] lc;
                if (!s.Layers.TryGetValue(layer, out lc)) { lc = new int[2]; s.Layers[layer] = lc; }
                lc[0]++;
                if (sb) lc[1]++;
                if (keep) { s.Kept++; if (sb) s.KeptStaticBatched++; }
                else { s.ToHide.Add(r); if (sb) s.HiddenStaticBatched++; }
                if (envTrack >= 0 && layer == envTrack)
                {
                    s.EnvTrack.Add(r);
                    if (r.sharedMaterial != null && (s.Asphalt == null || r.sharedMaterial.name.IndexOf("sphalt", StringComparison.OrdinalIgnoreCase) >= 0))
                        s.Asphalt = r.sharedMaterial;
                }
            }

            StringBuilder sb2 = new StringBuilder("RT VENUE DONOR SURVEY: " + s.Total + " renderers, " + s.Kept + " kept (" + s.KeptStaticBatched + " static-batched), " +
                                                  s.ToHide.Count + " to hide (" + s.HiddenStaticBatched + " static-batched). Roots:");
            foreach (KeyValuePair<string, int[]> kv in s.Roots)
                sb2.Append(" ").Append(kv.Key).Append("=").Append(kv.Value[0]).Append("(sb").Append(kv.Value[1]).Append(kv.Value[2] == 1 ? ",HIDE" : ",KEEP").Append(");");
            Main.Log(sb2.ToString());
            StringBuilder sb3 = new StringBuilder("RT VENUE DONOR LAYERS:");
            foreach (KeyValuePair<int, int[]> kv in s.Layers)
                sb3.Append(" layer ").Append(kv.Key).Append(" ").Append(LayerMask.LayerToName(kv.Key)).Append(" r=").Append(kv.Value[0]).Append(" sb=").Append(kv.Value[1]).Append(";");
            Main.Log(sb3.ToString());
            return s;
        }

        /// <summary>
        /// Takes every donor Env_Track mesh off that layer (and disables its collider) so that nothing of the donor's road
        /// catches the 3D cars' down-rays (UnityVehicle.Update) or snaps the racing line during Prepare (PathSpline.SetPositionsToGatePlane).
        /// </summary>
        public static void TakeOffTrackLayer(DonorSurvey s, BuiltVenue built)
        {
            int moved = 0, colliders = 0;
            foreach (Renderer r in s.EnvTrack)
            {
                if (r == null) continue;
                r.gameObject.layer = 0;
                Collider c = r.GetComponent<Collider>();
                if (c != null && c.enabled) { c.enabled = false; colliders++; }
                moved++;
            }
            built.Counts["donorEnvTrackMoved"] = moved;
            built.Counts["donorCollidersOff"] = colliders;
            Main.Log("RT VENUE: " + moved + " donor Env_Track meshes moved to the Default layer, " + colliders + " of their colliders disabled.");
        }

        /// <summary>Hides all donor art, from the list surveyed before ArtGenerator.HarvestDonor ran (so anything it parked is never touched).</summary>
        public static void HideArt(CircuitScene scene, DonorSurvey s, BuiltVenue built)
        {
            Transform root = scene.transform;
            int hidden = 0, already = 0;
            foreach (Renderer r in s.ToHide)
            {
                if (r == null) continue;
                if (!r.enabled) { already++; continue; }
                r.enabled = false;
                hidden++;
            }
            built.Counts["donorRenderersHidden"] = hidden;
            built.Counts["donorRenderersAlreadyOff"] = already;
            built.Counts["donorStaticBatchedHidden"] = s.HiddenStaticBatched;

            // The donor's Unity Terrain draws itself (it is not a Renderer).
            int terrains = 0;
            foreach (Terrain t in scene.GetComponentsInChildren<Terrain>(true))
            {
                string rn = RootOf(root, t.transform);
                if (rn == null || sKeepRoots.Contains(rn)) continue;
                t.enabled = false;
                TerrainCollider tc = t.GetComponent<TerrainCollider>();
                if (tc != null) tc.enabled = false;
                terrains++;
            }
            built.Counts["donorTerrainsOff"] = terrains;

            // CoreLod re-enables the renderers it manages by camera distance (CoreLod.cs SetCurrentDrawMode): stop it.
            int lodMgr = 0, lod = 0;
            foreach (CoreLodManager m in scene.GetComponentsInChildren<CoreLodManager>(true)) { if (m.enabled) { m.enabled = false; lodMgr++; } }
            foreach (CoreLod c in scene.GetComponentsInChildren<CoreLod>(true)) { if (c.enabled) { c.enabled = false; lod++; } }
            built.Counts["donorCoreLodOff"] = lodMgr + lod;

            // Donor lights (floodlights etc.) would light the wrong places; EnvironmentManager flips the Env_DynamicLight ones on at night.
            int lights = 0;
            int dynLayer = LayerMask.NameToLayer("Env_DynamicLight");
            foreach (Light l in scene.GetComponentsInChildren<Light>(true))
            {
                string rn = RootOf(root, l.transform);
                if (rn == null || sKeepRoots.Contains(rn)) continue;
                l.enabled = false;
                if (dynLayer >= 0 && l.gameObject.layer == dynLayer) l.gameObject.SetActive(false);
                lights++;
            }
            built.Counts["donorLightsOff"] = lights;

            // Animators on hidden art could animate renderer.enabled back on.
            int anims = 0;
            foreach (Animator a in scene.GetComponentsInChildren<Animator>(true))
            {
                string rn = RootOf(root, a.transform);
                if (rn == null || sKeepRoots.Contains(rn)) continue;
                if (a.enabled) { a.enabled = false; anims++; }
            }
            built.Counts["donorAnimatorsOff"] = anims;

            Main.Log("RT VENUE: hid " + hidden + " donor renderers (" + already + " were already off; " + s.HiddenStaticBatched + " of the listed ones are static-batched and can only be disabled), " +
                     terrains + " terrain(s), " + lodMgr + " CoreLodManager + " + lod + " CoreLod, " + lights + " light(s), " + anims + " animator(s).");
        }
    }
}
