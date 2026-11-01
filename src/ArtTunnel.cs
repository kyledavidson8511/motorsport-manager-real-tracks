using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using RealTracks.ArtGeo;

namespace RealTracks
{
    public partial class Settings
    {
        /// <summary>Roof and side walls over the pack layout's tunnels (concrete shell). Off = the road runs in an open cutting under the raised ground, as if the tunnel were not there.</summary>
        public bool ArtTunnels = true;
        /// <summary>Two rows of emissive lamp strips under the tunnel roof (geometry, not real lights).</summary>
        public bool ArtTunnelLights = true;
        /// <summary>Inner height of the tunnel at the crown (m).</summary>
        public float ArtTunnelRoofM = 5.8f;
    }

    /// <summary>
    /// Tunnels of a pack layout (manifest layout key "tunnels": name, s0, s1 along [track]): a concrete shell over the road (inner surface, outer skin, a frame at each
    /// portal), two rows of emissive lamp strips, the terrain/props height function that lets props stand on the road inside, and the camera check.
    /// The geometry maths is in ArtTunnelGeo.cs (Unity-free, unit-tested).
    /// </summary>
    public static class ArtTunnels
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void DrawGui(Settings s)
        {
            s.ArtTunnels = GUILayout.Toggle(s.ArtTunnels, " Tunnels: roof and walls over the road where the pack has a tunnel (off = open cutting)");
            s.ArtTunnelLights = GUILayout.Toggle(s.ArtTunnelLights, "   Tunnel lamp strips (emissive)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("   Tunnel inner height (m) " + s.ArtTunnelRoofM.ToString("0.0", Inv), GUILayout.ExpandWidth(false));
            s.ArtTunnelRoofM = GUILayout.HorizontalSlider(s.ArtTunnelRoofM, 4.5f, 7.5f, GUILayout.Width(220));
            GUILayout.EndHorizontal();
        }

        public static TunnelParams Params(Settings s)
        {
            TunnelParams p = new TunnelParams();
            p.RoofHeight = Mathf.Clamp(s.ArtTunnelRoofM, 4.5f, 7.5f);
            p.WallHeight = Mathf.Min(p.WallHeight, p.RoofHeight - 1.2f);
            return p;
        }

        /// <summary>The shells of a layout (stations only, meshes built when build is true). Empty list for a layout without tunnels.</summary>
        public static List<TunnelShell> Shells(PackLayout layout, Settings s, bool build)
        {
            List<TunnelShell> list = new List<TunnelShell>();
            if (layout == null || layout.Tunnels == null || layout.Tunnels.Count == 0) return list;
            LayoutPaths lp = layout.Paths;
            List<V3> track = new List<V3>(lp.Track.Count);
            foreach (Vector3 v in lp.Track) track.Add(new V3(v.x, v.y, v.z));
            TunnelParams prm = Params(s);
            foreach (TunnelSpan sp in layout.Tunnels)
            {
                TunnelShell sh = TunnelGeo.Stations(track, lp.WidthLeft, lp.WidthRight, sp, prm);
                if (build) TunnelGeo.Build(sh, prm);
                if (sh.Centre.Count >= 2) list.Add(sh);
            }
            return list;
        }

        /// <summary>
        /// Warns (log + fallback registry) about every position of a scene camera that would sit inside a tunnel shell. orbitSamples > 0 also tries the positions of an orbit
        /// (azimuth 0..360). Returns the number of positions found inside.
        /// </summary>
        public static int CheckCamera(PackLayout layout, string cameraName, Vector3 focus, float radius, float height, float azimuth, bool orbits)
        {
            try
            {
                if (layout == null || layout.Tunnels == null || layout.Tunnels.Count == 0 || !Main.Settings.ArtTunnels) return 0;
                List<TunnelShell> shells = Shells(layout, Main.Settings, false);
                if (shells.Count == 0) return 0;
                int inside = 0, tried = 0;
                int steps = orbits ? 24 : 1;
                for (int k = 0; k < steps; k++)
                {
                    float a = (orbits ? k * 360f / steps : azimuth) * Mathf.Deg2Rad;
                    Vector3 p = focus + new Vector3(Mathf.Sin(a) * radius, height, -Mathf.Cos(a) * radius);
                    tried++;
                    foreach (TunnelShell sh in shells)
                        if (sh.Inside(p.x, p.y, p.z, 0.5f)) { inside++; break; }
                }
                if (inside > 0)
                {
                    string m = "camera '" + cameraName + "' would sit inside a tunnel shell at " + inside + " of " + tried + " position(s) (focus " + focus.ToString("F0") + ", radius " + radius.ToString("0") + " m, height " + height.ToString("0") + " m)";
                    Main.Log("RT ART: tunnel warning: " + m);
                    RtFallbacks.Note("tunnel cameras", m);
                }
                return inside;
            }
            catch (Exception e) { Main.Log("RT ART: tunnel camera check failed for '" + cameraName + "': " + e.Message); return 0; }
        }
    }

    internal partial class ArtBuild
    {
        private List<TunnelShell> mTunnels = new List<TunnelShell>();
        private RoadIndex mTunnelIdx;          // interior of the tunnels (first and last station dropped): the terrain keeps the pack's height there and props stand on the road

        /// <summary>Called at the end of the constructor: reads the layout's tunnels and installs the tunnel-aware height function. Never throws.</summary>
        private void InitTunnels()
        {
            try
            {
                if (!mS.ArtTunnels || mLayout.Tunnels == null || mLayout.Tunnels.Count == 0) return;
                mTunnels = ArtTunnels.Shells(mLayout, mS, true);
                mTunnelIdx = new RoadIndex(64f, 64f);
                foreach (TunnelShell sh in mTunnels)
                {
                    int n = sh.Centre.Count;
                    if (n < 4) continue;
                    List<V3> pts = new List<V3>();
                    List<float> l = new List<float>(), r = new List<float>();
                    for (int k = 1; k < n - 1; k++) { pts.Add(sh.Centre[k]); l.Add(sh.Left[k]); r.Add(sh.Right[k]); }
                    mTunnelIdx.AddPolyline(pts, l, r, 6f, 6f, false);
                }
                PackTerrain t = mTerrain;
                RoadIndex idx = mTunnelIdx;
                mTy = delegate (float x, float z)
                {
                    float ry;
                    float c = idx.Nearest(x, z, out ry);
                    return c < 1.5f ? ry : t.Sample(x, z);          // props along the walls (barriers, kerbs) stand on the road inside a tunnel, not on the hill above
                };
            }
            catch (Exception e)
            {
                mTunnelIdx = null; mTunnels = new List<TunnelShell>();
                Main.Logger.Error("RT ART: tunnel setup failed: " + e);
                RtFallbacks.Note("art tunnels", "setup failed, no tunnel shell: " + e.Message);
                mBuilt.Warnings.Add("art: tunnel setup failed: " + e.Message);
            }
        }

        public void StageTunnels()
        {
            if (mLayout.Tunnels == null || mLayout.Tunnels.Count == 0) { Main.Log("RT ART: tunnels: none in the pack layout " + mLayout.Key); return; }
            int segs = 0, lamps = 0, tn = 0;
            foreach (TunnelShell sh in mTunnels)
            {
                AddMesh("tunnel", sh.Concrete);
                if (mS.ArtTunnelLights && sh.Lamps.Count > 0) AddMesh("tunnel_light", sh.Lamps);
                int lampCount = mS.ArtTunnelLights ? sh.Lamp : 0;
                segs += sh.Segments; lamps += lampCount; tn++;
                Main.Log("RT ART: tunnel " + sh.Name + " " + sh.S0.ToString("0") + ".." + sh.S1.ToString("0") + " " + (sh.S1 - sh.S0).ToString("0") + " m, segments " + sh.Segments + ", lights " + lampCount +
                         " (road y " + sh.Centre[0].Y.ToString("0.0") + " -> " + sh.Centre[sh.Centre.Count - 1].Y.ToString("0.0") + ", roof " + ArtTunnels.Params(mS).RoofHeight.ToString("0.0") + " m)");
                float minClear = 1e9f;
                for (int k = 0; k < sh.Centre.Count; k++)
                {
                    float c = TY(sh.Centre[k].X, sh.Centre[k].Z) - sh.Centre[k].Y;
                    if (k > 1 && k < sh.Centre.Count - 2 && c < minClear) minClear = c;
                }
                if (minClear < ArtTunnels.Params(mS).RoofHeight + 0.5f) Warn("tunnel " + sh.Name + ": the terrain is only " + minClear.ToString("0.0") + " m above the road inside (roof " + ArtTunnels.Params(mS).RoofHeight.ToString("0.0") + " m), the shell may poke through the ground");
            }
            Bump("tunnels", tn); Bump("tunnelSegments", segs); Bump("tunnelLights", lamps);
        }
    }
}
