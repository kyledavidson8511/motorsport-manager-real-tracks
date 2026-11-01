// Unity-free tunnel geometry (M2 w3): the span record read from the pack, the stations along it, the roof / side-wall shell with its portal frames, the lamp strips,
// and the "is this point inside the tunnel" test the camera check uses. No UnityEngine references (compiled by the net8 unit tests too).
using System;
using System.Collections.Generic;

namespace RealTracks.ArtGeo
{
    /// <summary>One tunnel of a layout (pack layout key "tunnels"): s = distance along [track] from point 0, the same s as width_KEY.csv. s1 may exceed the lap length when the span wraps past the start line.</summary>
    public class TunnelSpan
    {
        public string Name;
        public float S0, S1;
        public float Length { get { return S1 - S0; } }
    }

    /// <summary>Cross-section and spacing of the shell. Metres.</summary>
    public class TunnelParams
    {
        public float StationStep = 4f;       // spacing of the cross-sections along the road
        public float WallMargin = 0.9f;      // inner wall this far outside the tarmac edge
        public float WallHeight = 3.6f;      // height of the straight wall part above the road
        public float RoofHeight = 5.8f;      // inner height at the crown above the road (Monaco's tunnel is about 5 m clear)
        public float Thickness = 0.5f;       // shell thickness (the outer skin is what a camera inside the hill sees)
        public float PortalFrame = 1.0f;     // how far the frame around each portal reaches beyond the inner outline
        public float LampSpacing = 6f;       // one lamp strip per row every this many metres
        public float LampLateral = 2.6f;     // the two rows sit this far either side of the road centre
        public float LampLength = 3f, LampWidth = 0.4f;
        public float TileM = 4f;             // texture tile (one tile = 4 m x 4 m)
        public int ArcSegments = 6;
    }

    /// <summary>Cross-sections along a span plus the meshes built from them.</summary>
    public class TunnelShell
    {
        public string Name;
        public float S0, S1;
        public readonly List<V3> Centre = new List<V3>();         // road centre at each station (y = road height)
        public readonly List<V2> Tangent = new List<V2>();        // unit direction in x,z
        public readonly List<float> Left = new List<float>(), Right = new List<float>();   // wall offsets (m) either side of the centre
        public readonly List<float> Along = new List<float>();    // s of each station
        public MeshData Concrete = new MeshData();
        public MeshData Lamps = new MeshData();
        public int Segments, Lamp;
        public float RoofHeight;

        /// <summary>True when (x,y,z) is within the shell: laterally inside the walls, between the road and the crown, and along the span. margin widens the test.</summary>
        public bool Inside(float x, float y, float z, float margin)
        {
            int n = Centre.Count;
            for (int i = 0; i + 1 < n; i++)
            {
                V3 a = Centre[i], b = Centre[i + 1];
                V2 d = new V2(b.X - a.X, b.Z - a.Z);
                float len = d.Len;
                if (len < 1e-4f) continue;
                V2 dir = d * (1f / len);
                V2 p = new V2(x - a.X, z - a.Z);
                float t = V2.Dot(p, dir);
                if (t < -margin || t > len + margin) continue;
                float u = V2.Dot(p, new V2(dir.Y, -dir.X));          // right positive
                float tt = MathX.Clamp(t / len, 0f, 1f);
                float l = MathX.Lerp(Left[i], Left[i + 1], tt), r = MathX.Lerp(Right[i], Right[i + 1], tt);
                float ry = MathX.Lerp(a.Y, b.Y, tt);
                if (u > -(l + margin) && u < r + margin && y > ry - 0.5f - margin && y < ry + RoofHeight + 0.5f + margin) return true;
            }
            return false;
        }
    }

    public static class TunnelGeo
    {
        /// <summary>Cumulative distance of a closed polyline; returns (per-point s, lap length including the closing segment).</summary>
        public static float[] Cumulative(IList<V3> track, out float lap)
        {
            float[] s = new float[track.Count];
            for (int i = 1; i < s.Length; i++) s[i] = s[i - 1] + (track[i].XZ - track[i - 1].XZ).Len;
            lap = s.Length > 0 ? s[s.Length - 1] + (track[0].XZ - track[track.Count - 1].XZ).Len : 0f;
            return s;
        }

        /// <summary>Position, road height and half-widths at distance s along the closed track (s may exceed the lap by the wrap).</summary>
        private static void At(IList<V3> track, IList<float> hl, IList<float> hr, float[] cum, float lap, float s, out V3 p, out float l, out float r)
        {
            int n = track.Count;
            s = s % lap; if (s < 0) s += lap;
            int lo = 0, hi = n - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (cum[mid] <= s) lo = mid; else hi = mid - 1; }
            int j = (lo + 1) % n;
            float segLen = (j == 0) ? lap - cum[lo] : cum[j] - cum[lo];
            float t = segLen > 1e-6f ? MathX.Clamp((s - cum[lo]) / segLen, 0f, 1f) : 0f;
            V3 a = track[lo], b = track[j];
            p = new V3(MathX.Lerp(a.X, b.X, t), MathX.Lerp(a.Y, b.Y, t), MathX.Lerp(a.Z, b.Z, t));
            l = MathX.Lerp(hl[lo], hl[j], t); r = MathX.Lerp(hr[lo], hr[j], t);
        }

        /// <summary>Cross-sections for a span: one every ~StationStep m from S0 to S1 inclusive, with tangents from the neighbouring stations.</summary>
        public static TunnelShell Stations(IList<V3> track, IList<float> hl, IList<float> hr, TunnelSpan span, TunnelParams prm)
        {
            float lap;
            float[] cum = Cumulative(track, out lap);
            TunnelShell sh = new TunnelShell();
            sh.Name = span.Name; sh.S0 = span.S0; sh.S1 = span.S1; sh.RoofHeight = prm.RoofHeight;
            float len = span.Length;
            if (len < 1f || lap < 10f) return sh;
            int cnt = Math.Max(1, (int)Math.Ceiling(len / prm.StationStep));
            for (int k = 0; k <= cnt; k++)
            {
                float s = span.S0 + len * k / cnt;
                V3 p; float l, r;
                At(track, hl, hr, cum, lap, s, out p, out l, out r);
                sh.Centre.Add(p); sh.Along.Add(s);
                sh.Left.Add(l + prm.WallMargin); sh.Right.Add(r + prm.WallMargin);
            }
            int n = sh.Centre.Count;
            for (int k = 0; k < n; k++)
            {
                V3 a = sh.Centre[Math.Max(0, k - 1)], b = sh.Centre[Math.Min(n - 1, k + 1)];
                V2 d = new V2(b.X - a.X, b.Z - a.Z);
                sh.Tangent.Add(d.Len > 1e-6f ? d * (1f / d.Len) : new V2(0, 1));
            }
            return sh;
        }

        /// <summary>Inner cross-section outline (lateral u, height h above the road), left base to right base, with the outward unit normal (in u,h) at each point.</summary>
        public static void Profile(float uL, float uR, TunnelParams p, out List<V2> pts, out List<V2> normals)
        {
            pts = new List<V2>(); normals = new List<V2>();
            pts.Add(new V2(uL, -0.3f));
            pts.Add(new V2(uL, p.WallHeight));
            for (int k = 1; k < p.ArcSegments; k++)
            {
                float t = (float)k / p.ArcSegments;
                float h = p.WallHeight + (p.RoofHeight - p.WallHeight) * (float)Math.Pow(Math.Sin(Math.PI * t), 0.8);
                pts.Add(new V2(MathX.Lerp(uL, uR, t), h));
            }
            pts.Add(new V2(uR, p.WallHeight));
            pts.Add(new V2(uR, -0.3f));
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                V2 s0 = i > 0 ? Seg(pts[i - 1], pts[i]) : Seg(pts[0], pts[1]);
                V2 s1 = i < n - 1 ? Seg(pts[i], pts[i + 1]) : Seg(pts[n - 2], pts[n - 1]);
                V2 d = s0 + s1;
                if (d.Len < 1e-6f) d = s1;
                d = d * (1f / d.Len);
                // walking from the left base to the right base over the crown, "outward" is the left-hand side of travel in (u,h): (-dh, du)
                normals.Add(new V2(-d.Y, d.X));
            }
        }

        private static V2 Seg(V2 a, V2 b) { V2 d = b - a; return d.Len > 1e-9f ? d * (1f / d.Len) : new V2(0, 1); }

        private static V3 World(TunnelShell sh, int k, V2 uh)
        {
            V3 c = sh.Centre[k]; V2 t = sh.Tangent[k];
            V2 right = new V2(t.Y, -t.X);
            return new V3(c.X + right.X * uh.X, c.Y + uh.Y, c.Z + right.Y * uh.X);
        }

        private static V3 WorldDir(TunnelShell sh, int k, V2 uh)
        {
            V2 t = sh.Tangent[k];
            V2 right = new V2(t.Y, -t.X);
            return new V3(right.X * uh.X, uh.Y, right.Y * uh.X).Norm;
        }

        /// <summary>
        /// Builds the shell meshes for the stations in sh: the inner surface (normals into the tunnel), the outer skin (normals out, so a camera that ends up inside the hill sees
        /// concrete instead of the sky), a frame around each portal facing out along the road, and the lamp strips (emissive). Fills sh.Concrete, sh.Lamps, sh.Segments, sh.Lamp.
        /// </summary>
        public static void Build(TunnelShell sh, TunnelParams p)
        {
            int n = sh.Centre.Count;
            if (n < 2) return;
            MeshData m = sh.Concrete;
            int pc = 0;
            float cum = 0;
            List<List<V2>> prof = new List<List<V2>>(), norm = new List<List<V2>>();
            for (int k = 0; k < n; k++)
            {
                List<V2> pts, nrm;
                Profile(-sh.Left[k], sh.Right[k], p, out pts, out nrm);
                prof.Add(pts); norm.Add(nrm);
                pc = pts.Count;
            }
            // vertex rows: inner row k, then outer row k
            int[,] iv = new int[n, pc], ov = new int[n, pc];
            float[] alongM = new float[n];
            for (int k = 0; k < n; k++)
            {
                if (k > 0) cum += (new V2(sh.Centre[k].X - sh.Centre[k - 1].X, sh.Centre[k].Z - sh.Centre[k - 1].Z)).Len;
                alongM[k] = cum;
            }
            for (int k = 0; k < n; k++)
            {
                float arc = 0;
                for (int j = 0; j < pc; j++)
                {
                    if (j > 0) arc += (prof[k][j] - prof[k][j - 1]).Len;
                    V2 pt = prof[k][j], nr = norm[k][j];
                    V3 w = World(sh, k, pt);
                    V3 wn = WorldDir(sh, k, nr);
                    iv[k, j] = m.Add(w, wn * -1f, new V2(alongM[k] / p.TileM, arc / p.TileM));
                    V2 po = new V2(pt.X + nr.X * p.Thickness, pt.Y + nr.Y * p.Thickness);
                    ov[k, j] = m.Add(World(sh, k, po), wn, new V2(alongM[k] / p.TileM, arc / p.TileM));
                }
            }
            int segs = 0;
            for (int k = 0; k + 1 < n; k++)
                for (int j = 0; j + 1 < pc; j++)
                {
                    // inner: must face the tunnel axis, outer: away from it
                    AddQuad(m, iv[k, j], iv[k + 1, j], iv[k + 1, j + 1], iv[k, j + 1], true);
                    AddQuad(m, ov[k, j], ov[k + 1, j], ov[k + 1, j + 1], ov[k, j + 1], false);
                    segs += 2;
                }
            // portal frames: a band from the inner outline to the outline pushed out by PortalFrame, facing away from the tunnel
            for (int end = 0; end < 2; end++)
            {
                int k = end == 0 ? 0 : n - 1;
                V2 t = sh.Tangent[k];
                V3 face = end == 0 ? new V3(-t.X, 0, -t.Y) : new V3(t.X, 0, t.Y);
                int[] a = new int[pc], b = new int[pc];
                for (int j = 0; j < pc; j++)
                {
                    V2 pt = prof[k][j], nr = norm[k][j];
                    V2 po = new V2(pt.X + nr.X * p.PortalFrame, pt.Y + nr.Y * p.PortalFrame);
                    a[j] = m.Add(World(sh, k, pt), face, new V2(pt.X / p.TileM, pt.Y / p.TileM));
                    b[j] = m.Add(World(sh, k, po), face, new V2(po.X / p.TileM, po.Y / p.TileM));
                }
                for (int j = 0; j + 1 < pc; j++)
                {
                    // wall and arch ends: the band between the inner outline and the frame outline; the ground-level ends (j==0 / pc-2 bases) are included
                    AddFacing(m, a[j], b[j], b[j + 1], a[j + 1], face);
                    segs++;
                }
            }
            sh.Segments = segs;
            BuildLamps(sh, p);
        }

        private static void AddQuad(MeshData m, int a, int b, int c, int d, bool inward)
        {
            // choose the winding whose geometric normal agrees with the stored vertex normal (the profile normal decides which side is visible)
            V3 n = V3.Cross(m.P[b] - m.P[a], m.P[c] - m.P[a]);
            if (n.Len < 1e-9f) n = V3.Cross(m.P[c] - m.P[a], m.P[d] - m.P[a]);
            V3 want = m.N[a] + m.N[b] + m.N[c] + m.N[d];
            if (V3.Dot(n, want) >= 0) m.Quad(a, b, c, d); else m.Quad(a, d, c, b);
        }

        private static void AddFacing(MeshData m, int a, int b, int c, int d, V3 face)
        {
            V3 n = V3.Cross(m.P[b] - m.P[a], m.P[c] - m.P[a]);
            if (n.Len < 1e-9f) n = V3.Cross(m.P[c] - m.P[a], m.P[d] - m.P[a]);
            if (V3.Dot(n, face) >= 0) m.Quad(a, b, c, d); else m.Quad(a, d, c, b);
        }

        /// <summary>Two rows of emissive strips under the crown, one strip per LampSpacing metres per row, lying flat on the inner surface and facing into the tunnel.</summary>
        private static void BuildLamps(TunnelShell sh, TunnelParams p)
        {
            int n = sh.Centre.Count;
            float total = sh.S1 - sh.S0;
            if (total < p.LampSpacing) return;
            int count = Math.Max(1, (int)(total / p.LampSpacing));
            for (int row = 0; row < 2; row++)
            {
                float u = row == 0 ? -p.LampLateral : p.LampLateral;
                for (int q = 0; q < count; q++)
                {
                    float s = sh.S0 + (q + 0.5f) * total / count;
                    V3 c; V2 t; float l, r;
                    Sample(sh, s, out c, out t, out l, out r);
                    // crown height at this lateral position: same curve as Profile (clamped to the arc)
                    float uL = -l, uR = r;
                    float f = MathX.Clamp((u - uL) / (uR - uL), 0.02f, 0.98f);
                    float h = p.WallHeight + (p.RoofHeight - p.WallHeight) * (float)Math.Pow(Math.Sin(Math.PI * f), 0.8) - 0.06f;
                    V2 right = new V2(t.Y, -t.X);
                    float hl2 = p.LampLength * 0.5f, hw = p.LampWidth * 0.5f;
                    V3 a = new V3(c.X + right.X * (u - hw) - t.X * hl2, c.Y + h, c.Z + right.Y * (u - hw) - t.Y * hl2);
                    V3 b = new V3(c.X + right.X * (u + hw) - t.X * hl2, c.Y + h, c.Z + right.Y * (u + hw) - t.Y * hl2);
                    V3 cc = new V3(c.X + right.X * (u + hw) + t.X * hl2, c.Y + h, c.Z + right.Y * (u + hw) + t.Y * hl2);
                    V3 d = new V3(c.X + right.X * (u - hw) + t.X * hl2, c.Y + h, c.Z + right.Y * (u - hw) + t.Y * hl2);
                    LampQuad(sh.Lamps, a, b, cc, d);
                    sh.Lamp++;
                }
            }
        }

        /// <summary>One lamp strip quad facing down (normal -y), winding chosen so the geometric normal agrees.</summary>
        private static void LampQuad(MeshData m, V3 a, V3 b, V3 c, V3 d)
        {
            V3 down = new V3(0, -1, 0);
            bool flip = V3.Dot(V3.Cross(b - a, c - a), down) < 0;
            int ia = m.Add(a, down, new V2(0, 0)), ib = m.Add(b, down, new V2(1, 0)), ic = m.Add(c, down, new V2(1, 1)), id = m.Add(d, down, new V2(0, 1));
            if (!flip) m.Quad(ia, ib, ic, id); else m.Quad(ia, id, ic, ib);
        }

        /// <summary>Interpolated station data at distance s (clamped to the span).</summary>
        public static void Sample(TunnelShell sh, float s, out V3 c, out V2 t, out float left, out float right)
        {
            int n = sh.Centre.Count;
            int k = 0;
            while (k + 2 < n && sh.Along[k + 1] < s) k++;
            float f = sh.Along[k + 1] > sh.Along[k] ? MathX.Clamp((s - sh.Along[k]) / (sh.Along[k + 1] - sh.Along[k]), 0f, 1f) : 0f;
            V3 a = sh.Centre[k], b = sh.Centre[k + 1];
            c = new V3(MathX.Lerp(a.X, b.X, f), MathX.Lerp(a.Y, b.Y, f), MathX.Lerp(a.Z, b.Z, f));
            V2 ta = sh.Tangent[k], tb = sh.Tangent[k + 1];
            V2 tt = new V2(MathX.Lerp(ta.X, tb.X, f), MathX.Lerp(ta.Y, tb.Y, f));
            t = tt.Len > 1e-6f ? tt * (1f / tt.Len) : ta;
            left = MathX.Lerp(sh.Left[k], sh.Left[k + 1], f); right = MathX.Lerp(sh.Right[k], sh.Right[k + 1], f);
        }

        /// <summary>Parses the layout's "tunnels" array (objects with name, s0, s1); invalid entries are dropped and described in errors.</summary>
        public static List<TunnelSpan> ParseList(object raw, List<string> errors)
        {
            List<TunnelSpan> list = new List<TunnelSpan>();
            List<object> arr = raw as List<object>;
            if (arr == null) return list;
            for (int i = 0; i < arr.Count; i++)
            {
                Dictionary<string, object> d = arr[i] as Dictionary<string, object>;
                if (d == null) { errors.Add("tunnels[" + i + "] is not an object"); continue; }
                TunnelSpan sp = new TunnelSpan();
                object v;
                sp.Name = d.TryGetValue("name", out v) && v != null ? v.ToString() : "tunnel";
                double s0, s1;
                if (!d.TryGetValue("s0", out v) || !(v is double)) { errors.Add("tunnels[" + i + "] has no numeric s0"); continue; }
                s0 = (double)v;
                if (!d.TryGetValue("s1", out v) || !(v is double)) { errors.Add("tunnels[" + i + "] has no numeric s1"); continue; }
                s1 = (double)v;
                if (!(s1 > s0)) { errors.Add("tunnels[" + i + "] s1 <= s0"); continue; }
                sp.S0 = (float)s0; sp.S1 = (float)s1;
                list.Add(sp);
            }
            return list;
        }
    }
}
