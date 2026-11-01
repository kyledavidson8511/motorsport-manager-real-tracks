// Unity-free shape builders for the venue art generator (WP3b): terrain chunks, horizon skirt, corners, kerb and paint
// ribbons, draped polygons, extrusions, stands, trees, tower, barriers and fences. No UnityEngine references.
using System;
using System.Collections.Generic;

namespace RealTracks.ArtGeo
{
    public delegate float HeightFn(float x, float z);
    public delegate float RibbonY(int row, int col, float x, float z);
    public delegate bool RibbonKeep(int row, int col, float x, float z);

    // =================================================================================================
    // Terrain chunks and horizon skirt
    // =================================================================================================
    public static class TerrainGeo
    {
        public struct Range { public int A0, A1, B0, B1; } // inclusive indices into the axis sample arrays

        public class Chunk
        {
            public MeshData M = new MeshData();
            public List<int> TriOrtho = new List<int>();   // submesh 0: inside the orthophoto
            public List<int> TriOther = new List<int>();   // submesh 1: outside it (plain ground texture)
            public float MinY = float.MaxValue, MaxY = float.MinValue;
        }

        /// <summary>Grid indices 0, stride, 2 x stride ... always ending on n - 1.</summary>
        public static int[] AxisSamples(int n, int stride)
        {
            if (stride < 1) stride = 1;
            List<int> l = new List<int>();
            for (int i = 0; i < n - 1; i += stride) l.Add(i);
            l.Add(n - 1);
            return l.ToArray();
        }

        /// <summary>Chunk ranges of at most cells x cells cells (so at most (cells+1)^2 vertices each).</summary>
        public static List<Range> Plan(int samplesX, int samplesZ, int cells)
        {
            List<Range> res = new List<Range>();
            for (int b = 0; b < samplesZ - 1; b += cells)
                for (int a = 0; a < samplesX - 1; a += cells)
                {
                    Range r;
                    r.A0 = a; r.A1 = Math.Min(a + cells, samplesX - 1);
                    r.B0 = b; r.B1 = Math.Min(b + cells, samplesZ - 1);
                    res.Add(r);
                }
            return res;
        }

        /// <summary>
        /// One terrain chunk. Normals come from the full-resolution grid (so they match across chunk borders). UV is the
        /// orthophoto UV: (x - ox)/ow, (z - oz)/oh (v = 0 at the south edge, matching a north-up image). Triangles whose three
        /// corners all lie inside the orthophoto go to TriOrtho, the rest to TriOther. adjust(x, z, y) may lower vertices (road clamp).
        /// </summary>
        public static Chunk BuildChunk(float[] h, int nx, int nz, float x0, float z0, float dx, float dz, int[] xs, int[] zs, Range r,
                                       float ox, float oz, float ow, float oh, bool hasOrtho, Func<float, float, float, float> adjust)
        {
            Chunk c = new Chunk();
            int wa = r.A1 - r.A0 + 1, wb = r.B1 - r.B0 + 1;
            bool[] inOrtho = new bool[wa * wb];
            float[] ys = new float[wa * wb];
            for (int jb = 0; jb < wb; jb++)
                for (int ia = 0; ia < wa; ia++)
                {
                    int gx = xs[r.A0 + ia], gz = zs[r.B0 + jb];
                    float x = x0 + gx * dx, z = z0 + gz * dz;
                    float y = h[gz * nx + gx];
                    if (adjust != null) y = adjust(x, z, y);
                    ys[jb * wa + ia] = y;
                    if (y < c.MinY) c.MinY = y; if (y > c.MaxY) c.MaxY = y;
                    float u = (x - ox) / ow, v = (z - oz) / oh;
                    inOrtho[jb * wa + ia] = hasOrtho && u >= -1e-4f && u <= 1.0001f && v >= -1e-4f && v <= 1.0001f;
                    float hxm = h[gz * nx + Math.Max(gx - 1, 0)], hxp = h[gz * nx + Math.Min(gx + 1, nx - 1)];
                    float hzm = h[Math.Max(gz - 1, 0) * nx + gx], hzp = h[Math.Min(gz + 1, nz - 1) * nx + gx];
                    float sx = (Math.Min(gx + 1, nx - 1) - Math.Max(gx - 1, 0)) * dx, sz = (Math.Min(gz + 1, nz - 1) - Math.Max(gz - 1, 0)) * dz;
                    V3 n = new V3(-(hxp - hxm) / sx, 1f, -(hzp - hzm) / sz).Norm;
                    c.M.Add(new V3(x, y, z), n, new V2(u, v));
                }
            for (int jb = 0; jb < wb - 1; jb++)
                for (int ia = 0; ia < wa - 1; ia++)
                {
                    int i00 = jb * wa + ia, i10 = i00 + 1, i01 = i00 + wa, i11 = i01 + 1;
                    bool diag00 = Math.Abs(ys[i00] - ys[i11]) <= Math.Abs(ys[i10] - ys[i01]);
                    if (diag00)
                    {
                        Emit(c, inOrtho, i00, i01, i11);
                        Emit(c, inOrtho, i00, i11, i10);
                    }
                    else
                    {
                        Emit(c, inOrtho, i00, i01, i10);
                        Emit(c, inOrtho, i10, i01, i11);
                    }
                }
            return c;
        }

        private static void Emit(Chunk c, bool[] inOrtho, int a, int b, int d)
        {
            List<int> t = (inOrtho[a] && inOrtho[b] && inOrtho[d]) ? c.TriOrtho : c.TriOther;
            t.Add(a); t.Add(b); t.Add(d);
        }

        /// <summary>
        /// A flat ring around the terrain: its inner edge follows the terrain's boundary heights, its outer edge sits `outer`
        /// metres away at the mean boundary height, so no void shows past the heightfield from any camera.
        /// </summary>
        public static MeshData Skirt(float[] h, int nx, int nz, float x0, float z0, float dx, float dz, int[] xs, int[] zs, float outer, float uvTile, out float outerY)
        {
            MeshData m = new MeshData();
            double sum = 0; int cnt = 0;
            for (int i = 0; i < xs.Length; i++) { sum += h[xs[i]] + h[(nz - 1) * nx + xs[i]]; cnt += 2; }
            for (int j = 0; j < zs.Length; j++) { sum += h[zs[j] * nx] + h[zs[j] * nx + nx - 1]; cnt += 2; }
            outerY = (float)(sum / cnt);
            float xMin = x0, xMax = x0 + (nx - 1) * dx, zMin = z0, zMax = z0 + (nz - 1) * dz;
            Func<float, float, float, V3> P = delegate (float x, float y, float z) { return new V3(x, y, z); };
            Func<V3, V2> uv = delegate (V3 p) { return new V2(p.X / uvTile, p.Z / uvTile); };

            for (int i = 0; i + 1 < xs.Length; i++)
            {
                float xa = x0 + xs[i] * dx, xb = x0 + xs[i + 1] * dx;
                V3 a = P(xa, h[xs[i]], zMin), b = P(xb, h[xs[i + 1]], zMin), a2 = P(xa, outerY, zMin - outer), b2 = P(xb, outerY, zMin - outer);
                m.QuadUp(a, b, b2, a2, uv(a), uv(b), uv(b2), uv(a2));
                V3 c = P(xa, h[(nz - 1) * nx + xs[i]], zMax), d = P(xb, h[(nz - 1) * nx + xs[i + 1]], zMax), c2 = P(xa, outerY, zMax + outer), d2 = P(xb, outerY, zMax + outer);
                m.QuadUp(c, d, d2, c2, uv(c), uv(d), uv(d2), uv(c2));
            }
            for (int j = 0; j + 1 < zs.Length; j++)
            {
                float za = z0 + zs[j] * dz, zb = z0 + zs[j + 1] * dz;
                V3 a = P(xMin, h[zs[j] * nx], za), b = P(xMin, h[zs[j + 1] * nx], zb), a2 = P(xMin - outer, outerY, za), b2 = P(xMin - outer, outerY, zb);
                m.QuadUp(a, b, b2, a2, uv(a), uv(b), uv(b2), uv(a2));
                V3 c = P(xMax, h[zs[j] * nx + nx - 1], za), d = P(xMax, h[zs[j + 1] * nx + nx - 1], zb), c2 = P(xMax + outer, outerY, za), d2 = P(xMax + outer, outerY, zb);
                m.QuadUp(c, d, d2, c2, uv(c), uv(d), uv(d2), uv(c2));
            }
            // four corner squares
            float[] cxs = { xMin, xMax, xMin, xMax }, czs = { zMin, zMin, zMax, zMax };
            float[] sxs = { -1, 1, -1, 1 }, szs = { -1, -1, 1, 1 };
            float[] ch = { h[0], h[nx - 1], h[(nz - 1) * nx], h[(nz - 1) * nx + nx - 1] };
            for (int k = 0; k < 4; k++)
            {
                V3 a = P(cxs[k], ch[k], czs[k]);
                V3 b = P(cxs[k] + sxs[k] * outer, outerY, czs[k]);
                V3 c = P(cxs[k] + sxs[k] * outer, outerY, czs[k] + szs[k] * outer);
                V3 d = P(cxs[k], outerY, czs[k] + szs[k] * outer);
                m.QuadUp(a, b, c, d, uv(a), uv(b), uv(c), uv(d));
            }
            return m;
        }
    }

    // =================================================================================================
    // Polylines
    // =================================================================================================
    public static class Lines
    {
        public static float Length(IList<V2> l)
        {
            float s = 0;
            for (int i = 1; i < l.Count; i++) s += V2.Dist(l[i - 1], l[i]);
            return s;
        }

        /// <summary>Points spaced at most `step` apart (original vertices kept).</summary>
        public static List<V2> Resample(IList<V2> l, float step)
        {
            List<V2> r = new List<V2>();
            if (l.Count == 0) return r;
            r.Add(l[0]);
            for (int i = 1; i < l.Count; i++)
            {
                float d = V2.Dist(l[i - 1], l[i]);
                int n = (int)Math.Ceiling(d / step);
                for (int k = 1; k <= n; k++) r.Add(V2.Lerp(l[i - 1], l[i], (float)k / n));
            }
            return r;
        }

        /// <summary>Splits into runs of at most maxLen metres (runs share their end points).</summary>
        public static List<List<V2>> Chop(IList<V2> l, float maxLen)
        {
            List<List<V2>> runs = new List<List<V2>>();
            List<V2> cur = new List<V2>();
            float acc = 0;
            for (int i = 0; i < l.Count; i++)
            {
                if (i > 0) acc += V2.Dist(l[i - 1], l[i]);
                cur.Add(l[i]);
                if (acc >= maxLen && i < l.Count - 1) { runs.Add(cur); cur = new List<V2>(); cur.Add(l[i]); acc = 0; }
            }
            if (cur.Count >= 2) runs.Add(cur);
            return runs;
        }

        /// <summary>Splits a polyline into the runs whose points satisfy keep (drops the rest).</summary>
        public static List<List<V2>> Runs(IList<V2> l, Func<V2, bool> keep)
        {
            List<List<V2>> runs = new List<List<V2>>();
            List<V2> cur = new List<V2>();
            foreach (V2 p in l)
            {
                if (keep(p)) cur.Add(p);
                else { if (cur.Count >= 2) runs.Add(cur); cur = new List<V2>(); }
            }
            if (cur.Count >= 2) runs.Add(cur);
            return runs;
        }
    }

    // =================================================================================================
    // Corners from the track centreline
    // =================================================================================================
    public class CornerSpan
    {
        public int Start, End, Apex;       // point indices (inclusive, may wrap past the end of the loop)
        public float Sign;                 // +1 = left-hander, -1 = right-hander
        public float TotalAngleDeg;        // absolute turning angle
        public float MinRadius;            // metres
        public float SStart, SEnd;         // arc length from point 0 (SEnd may exceed the lap length when the span wraps)
        public int Count(int n) { return ((End - Start + n) % n) + 1; }
        public float Length { get { return SEnd - SStart; } }
    }

    public static class Corners
    {
        /// <summary>Corner spans of a closed loop from smoothed curvature (enter at radius 220 m, leave at 500 m), merged when close.</summary>
        public static List<CornerSpan> Find(IList<V2> p, float[] s, float lapLength)
        {
            int n = p.Count;
            List<CornerSpan> res = new List<CornerSpan>();
            if (n < 12) return res;
            float[] ang = new float[n], kap = new float[n];
            for (int i = 0; i < n; i++)
            {
                V2 a = p[(i + n - 1) % n], b = p[i], c = p[(i + 1) % n];
                V2 v1 = b - a, v2 = c - b;
                float l1 = v1.Len, l2 = v2.Len;
                if (l1 < 1e-4f || l2 < 1e-4f) continue;
                ang[i] = (float)Math.Atan2(V2.Cross(v1, v2), V2.Dot(v1, v2));
                kap[i] = ang[i] / ((l1 + l2) * 0.5f);
            }
            float[] ks = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0;
                for (int k = -2; k <= 2; k++) sum += kap[(i + k + n) % n];
                ks[i] = sum / 5f;
            }
            int m = 0;
            for (int i = 1; i < n; i++) if (Math.Abs(ks[i]) < Math.Abs(ks[m])) m = i;

            const float Enter = 1f / 220f, Exit = 1f / 500f;
            List<int[]> raw = new List<int[]>(); // start, count (in rotated order), sign
            int st = -1; float sg = 0;
            for (int j = 0; j <= n; j++)
            {
                int i = (m + j) % n;
                float k = j < n ? ks[i] : 0f;
                bool cont = st >= 0 && Math.Sign(k) == (int)sg && Math.Abs(k) >= Exit;
                if (st >= 0 && !cont)
                {
                    raw.Add(new int[] { st, j - st, (int)sg });
                    st = -1;
                }
                if (st < 0 && j < n && Math.Abs(k) >= Enter) { st = j; sg = Math.Sign(k); }
            }
            // merge same-sign spans whose gap is small (25 m of arc length)
            List<int[]> merged = new List<int[]>();
            foreach (int[] sp in raw)
            {
                if (merged.Count > 0)
                {
                    int[] last = merged[merged.Count - 1];
                    int gapPts = sp[0] - (last[0] + last[1]);
                    float gap = ArcBetween(p, (m + last[0] + last[1] - 1) % n, (m + sp[0]) % n);
                    if (last[2] == sp[2] && gap <= 25f && gapPts >= 0)
                    {
                        last[1] = sp[0] + sp[1] - last[0];
                        continue;
                    }
                }
                merged.Add(new int[] { sp[0], sp[1], sp[2] });
            }
            foreach (int[] sp in merged)
            {
                CornerSpan c = new CornerSpan();
                c.Start = (m + sp[0]) % n;
                c.End = (m + sp[0] + sp[1] - 1) % n;
                c.Sign = sp[2];
                float total = 0, maxK = 0;
                int apex = c.Start;
                for (int q = 0; q < sp[1]; q++)
                {
                    int i = (c.Start + q) % n;
                    total += ang[i];
                    if (Math.Abs(ks[i]) > maxK) { maxK = Math.Abs(ks[i]); apex = i; }
                }
                c.Apex = apex;
                c.TotalAngleDeg = Math.Abs(total) * 180f / MathX.Pi;
                c.MinRadius = maxK > 1e-6f ? 1f / maxK : 1e6f;
                c.SStart = s[c.Start];
                c.SEnd = c.End >= c.Start ? s[c.End] : s[c.End] + lapLength;
                if (c.TotalAngleDeg < 18f || c.Length < 20f) continue;
                res.Add(c);
            }
            res.Sort(delegate (CornerSpan a, CornerSpan b) { return a.SStart.CompareTo(b.SStart); });
            return res;
        }

        private static float ArcBetween(IList<V2> p, int a, int b)
        {
            int n = p.Count;
            float d = 0;
            int i = a;
            for (int guard = 0; i != b && guard < n; guard++)
            {
                int j = (i + 1) % n;
                d += V2.Dist(p[i], p[j]);
                i = j;
            }
            return d;
        }

        /// <summary>The first big corner after the start line: first span past minS metres turning at least minAngle degrees.</summary>
        public static CornerSpan FirstBig(List<CornerSpan> spans, float minS, float minAngle)
        {
            foreach (CornerSpan c in spans) if (c.SStart >= minS && c.TotalAngleDeg >= minAngle) return c;
            CornerSpan best = null;
            foreach (CornerSpan c in spans) if (c.SStart >= minS && (best == null || c.TotalAngleDeg > best.TotalAngleDeg)) best = c;
            return best;
        }
    }

    // =================================================================================================
    // Ribbons along the track and draped polygons
    // =================================================================================================
    public static class Ribbons
    {
        /// <summary>
        /// A strip of (columns) quads across and one per row pair along `centre`. Offsets are signed metres along the right-hand
        /// normal of the direction of travel; offA &lt; offB gives faces that point up. u = column / columns (or metres / acrossTile when
        /// acrossTile &gt; 0), v = distance along / alongTile. Quads that touch a vertex rejected by keep are dropped.
        /// </summary>
        public static MeshData Build(IList<V2> centre, bool closed, IList<float> offA, IList<float> offB, int columns, RibbonY yf,
                                     float alongTile, float acrossTile, RibbonKeep keep)
        {
            MeshData m = new MeshData();
            int n = centre.Count;
            if (n < 2) return m;
            int cw = columns + 1;
            float dist = 0;
            bool[] ok = new bool[n * cw];
            for (int i = 0; i < n; i++)
            {
                V2 t;
                if (closed) t = centre[(i + 1) % n] - centre[(i + n - 1) % n];
                else t = i == 0 ? centre[1] - centre[0] : (i == n - 1 ? centre[n - 1] - centre[n - 2] : centre[i + 1] - centre[i - 1]);
                t = t.Norm;
                V2 right = new V2(t.Y, -t.X);
                if (i > 0) dist += V2.Dist(centre[i - 1], centre[i]);
                for (int c = 0; c < cw; c++)
                {
                    float off = MathX.Lerp(offA[i], offB[i], (float)c / columns);
                    V2 q = centre[i] + right * off;
                    float y = yf(i, c, q.X, q.Y);
                    float u = acrossTile > 0 ? off / acrossTile : (float)c / columns;
                    m.Add(new V3(q.X, y, q.Y), new V2(u, dist / alongTile));
                    ok[i * cw + c] = keep == null || keep(i, c, q.X, q.Y);
                }
            }
            int rows = closed ? n : n - 1;
            for (int i = 0; i < rows; i++)
            {
                int j = (i + 1) % n;
                if (closed && j == 0) { /* the closing quad reuses row 0 with its own v: fine for tiling textures */ }
                for (int c = 0; c < columns; c++)
                {
                    int a = i * cw + c, b = j * cw + c, d = i * cw + c + 1, e = j * cw + c + 1;
                    if (!(ok[a] && ok[b] && ok[d] && ok[e])) continue;
                    // A=(i,c) B=(j,c) C=(j,c+1) D=(i,c+1): forward then right = clockwise seen from above = up-facing
                    m.Quad(a, b, e, d);
                }
            }
            m.RecalcNormals();
            return m;
        }

        /// <summary>
        /// Triangulates a polygon (with optional holes), subdivides the triangles to at most maxEdge metres and drapes them on
        /// the terrain (y(x,z) + lift). Triangles whose centre is rejected by drop are removed. UV = x/uvTile, z/uvTile.
        /// </summary>
        public static MeshData Drape(IList<V2> ring, IList<IList<V2>> holes, float maxEdge, HeightFn y, float lift, float uvTile, Func<V2, bool> drop)
        {
            MeshData m = new MeshData();
            List<V2> pts;
            List<int> tris = Poly.Triangulate(ring, holes, out pts);
            Stack<V2[]> work = new Stack<V2[]>();
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                // earcut hands back counter-clockwise (map) triangles; Unity wants clockwise for faces that look up
                work.Push(new V2[] { pts[tris[i]], pts[tris[i + 2]], pts[tris[i + 1]], new V2(0, 0) });
            }
            while (work.Count > 0)
            {
                V2[] t = work.Pop();
                V2 a = t[0], b = t[1], c = t[2];
                float lab = V2.Dist(a, b), lbc = V2.Dist(b, c), lca = V2.Dist(c, a);
                float longest = Math.Max(lab, Math.Max(lbc, lca));
                int depth = (int)t[3].X;
                if (longest > maxEdge && depth < 12)
                {
                    V2 p, q, r; // longest edge p-q, opposite vertex r, keeping the cyclic order p,q,r
                    if (longest == lab) { p = a; q = b; r = c; }
                    else if (longest == lbc) { p = b; q = c; r = a; }
                    else { p = c; q = a; r = b; }
                    V2 mid = V2.Lerp(p, q, 0.5f);
                    work.Push(new V2[] { p, mid, r, new V2(depth + 1, 0) });
                    work.Push(new V2[] { mid, q, r, new V2(depth + 1, 0) });
                    continue;
                }
                V2 cen = new V2((a.X + b.X + c.X) / 3f, (a.Y + b.Y + c.Y) / 3f);
                if (drop != null && drop(cen)) continue;
                int ia = AddDraped(m, a, y, lift, uvTile), ib = AddDraped(m, b, y, lift, uvTile), ic = AddDraped(m, c, y, lift, uvTile);
                m.Tri(ia, ib, ic);
            }
            return m;
        }

        private static int AddDraped(MeshData m, V2 p, HeightFn y, float lift, float uvTile)
        {
            float h = y(p.X, p.Y);
            float hx = y(p.X - 1f, p.Y) - y(p.X + 1f, p.Y), hz = y(p.X, p.Y - 1f) - y(p.X, p.Y + 1f);
            return m.Add(new V3(p.X, h + lift, p.Y), new V3(hx, 2f, hz).Norm, new V2(p.X / uvTile, p.Y / uvTile));
        }
    }

    // =================================================================================================
    // Kerbs
    // =================================================================================================
    public static class Kerbs
    {
        public const float TileLength = 2f; // 1 m red, 1 m white

        /// <summary>Row indices (wrapping) from a to b inclusive.</summary>
        public static List<int> Rows(int a, int b, int n, int padBefore, int padAfter)
        {
            List<int> l = new List<int>();
            int count = ((b - a + n) % n) + 1 + padBefore + padAfter;
            if (count > n) count = n;
            for (int k = 0; k < count; k++) l.Add(((a - padBefore + k) % n + n) % n);
            return l;
        }

        /// <summary>
        /// Builds one kerb ribbon along the given rows on one side (+1 right, -1 left) just outside the road edge.
        /// Cross profile (flat on the road at the inner edge, raised 6.5 cm in the middle, easing into the ground outside).
        /// </summary>
        public static MeshData Strip(IList<V3> track, float[] hl, float[] hr, List<int> rows, int side, float width, HeightFn terrain, RibbonKeep keep)
        {
            List<V2> c = new List<V2>();
            List<float> a = new List<float>(), b = new List<float>(), ty = new List<float>();
            foreach (int i in rows)
            {
                c.Add(track[i].XZ);
                ty.Add(track[i].Y);
                if (side > 0) { a.Add(hr[i]); b.Add(hr[i] + width); }
                else { a.Add(-hl[i] - width); b.Add(-hl[i]); }
            }
            RibbonY yf = delegate (int row, int col, float x, float z)
            {
                float road = ty[row];
                int inner = side > 0 ? 0 : 2;
                if (col == inner) return road + 0.025f;
                if (col == 1) return road + 0.065f;
                return Math.Max(terrain(x, z), road - 0.02f) + 0.012f;
            };
            return Ribbons.Build(c, false, a, b, 2, yf, TileLength, 0f, keep);
        }
    }

    // =================================================================================================
    // Solids and props
    // =================================================================================================
    public static class Shapes
    {
        /// <summary>Appends a quad (a,b,c,d) with the winding flipped if needed so it faces `want`; normals set to the face normal.</summary>
        public static void QuadFacing(MeshData m, V3 a, V3 b, V3 c, V3 d, V2 ua, V2 ub, V2 uc, V2 ud, V3 want)
        {
            V3 n = V3.Cross(b - a, c - a);
            if (n.Len < 1e-9f) n = V3.Cross(c - a, d - a);
            n = n.Norm;
            bool flip = V3.Dot(n, want) < 0;
            if (flip) n = n * -1f;
            int ia = m.Add(a, n, ua), ib = m.Add(b, n, ub), ic = m.Add(c, n, uc), id = m.Add(d, n, ud);
            if (!flip) m.Quad(ia, ib, ic, id); else m.Quad(ia, id, ic, ib);
        }

        // ------------------------------------------------------------------ extruded footprints

        /// <summary>Walls (outward faces) and a flat roof from a footprint ring. Wall UV: metres/tileW, y/tileH; roof UV: x,z/roofTile.</summary>
        public static void Extrude(IList<V2> ringIn, float yBottom, float yTop, float tileW, float tileH, float roofTile, MeshData walls, MeshData roof)
        {
            List<V2> ring = Poly.Clean(ringIn);
            int n = ring.Count;
            if (n < 3) return;
            float cum = 0;
            for (int i = 0; i < n; i++)
            {
                V2 p = ring[i], q = ring[(i + 1) % n];
                V2 d = q - p;
                float len = d.Len;
                if (len < 1e-3f) continue;
                V3 nrm = new V3(d.Y / len, 0, -d.X / len);
                int a = walls.Add(new V3(p.X, yBottom, p.Y), nrm, new V2(cum / tileW, yBottom / tileH));
                int b = walls.Add(new V3(p.X, yTop, p.Y), nrm, new V2(cum / tileW, yTop / tileH));
                int c = walls.Add(new V3(q.X, yTop, q.Y), nrm, new V2((cum + len) / tileW, yTop / tileH));
                int e = walls.Add(new V3(q.X, yBottom, q.Y), nrm, new V2((cum + len) / tileW, yBottom / tileH));
                walls.Quad(a, b, c, e);
                cum += len;
            }
            List<V2> pts;
            List<int> tris = Poly.Triangulate(ring, null, out pts);
            int baseIdx = roof.Count;
            foreach (V2 p in pts) roof.Add(new V3(p.X, yTop, p.Y), new V3(0, 1, 0), new V2(p.X / roofTile, p.Y / roofTile));
            for (int i = 0; i + 2 < tris.Count; i += 3) roof.Tri(baseIdx + tris[i], baseIdx + tris[i + 2], baseIdx + tris[i + 1]);
        }

        // ------------------------------------------------------------------ prisms / frusta

        /// <summary>A vertical frustum (cylinder when r0 == r1) with `sides` sides around (cx, cz). Side faces point outward.</summary>
        public static void Frustum(MeshData m, float cx, float cz, float y0, float y1, float r0, float r1, int sides, bool capTop, bool capBottom, float uvTileU, float uvTileV)
        {
            int[] bot = new int[sides + 1], top = new int[sides + 1];
            for (int j = 0; j <= sides; j++)
            {
                float a = 2f * MathX.Pi * j / sides;
                float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                float slope = (r0 - r1) / Math.Max(y1 - y0, 1e-3f);
                V3 nrm = new V3(ca, slope, sa).Norm;
                float u = (r0 * 2f * MathX.Pi * j / sides) / uvTileU;
                bot[j] = m.Add(new V3(cx + ca * r0, y0, cz + sa * r0), nrm, new V2(u, y0 / uvTileV));
                top[j] = m.Add(new V3(cx + ca * r1, y1, cz + sa * r1), nrm, new V2(u, y1 / uvTileV));
            }
            for (int j = 0; j < sides; j++)
            {
                V3 mid = new V3(m.P[bot[j]].X + m.P[bot[j + 1]].X - 2 * cx, 0, m.P[bot[j]].Z + m.P[bot[j + 1]].Z - 2 * cz);
                AddFacing(m, bot[j], top[j], top[j + 1], bot[j + 1], mid);
            }
            if (capTop && r1 > 0)
            {
                int c = m.Add(new V3(cx, y1, cz), new V3(0, 1, 0), new V2(0.5f, 0.5f));
                int[] ring = new int[sides + 1];
                for (int j = 0; j <= sides; j++) { V3 p = m.P[top[j]]; ring[j] = m.Add(p, new V3(0, 1, 0), new V2((p.X - cx) / uvTileU, (p.Z - cz) / uvTileU)); }
                for (int j = 0; j < sides; j++) AddFacing(m, c, ring[j], ring[j + 1], c, new V3(0, 1, 0), true);
            }
            if (capBottom && r0 > 0)
            {
                int c = m.Add(new V3(cx, y0, cz), new V3(0, -1, 0), new V2(0.5f, 0.5f));
                int[] ring = new int[sides + 1];
                for (int j = 0; j <= sides; j++) { V3 p = m.P[bot[j]]; ring[j] = m.Add(p, new V3(0, -1, 0), new V2((p.X - cx) / uvTileU, (p.Z - cz) / uvTileU)); }
                for (int j = 0; j < sides; j++) AddFacing(m, c, ring[j], ring[j + 1], c, new V3(0, -1, 0), true);
            }
        }

        private static void AddFacing(MeshData m, int a, int b, int c, int d, V3 want) { AddFacing(m, a, b, c, d, want, false); }

        /// <summary>Adds the quad (a,b,c,d) (or the triangle a,b,c when asTri) wound so its geometric normal faces `want`.</summary>
        private static void AddFacing(MeshData m, int a, int b, int c, int d, V3 want, bool asTri)
        {
            V3 n = V3.Cross(m.P[b] - m.P[a], m.P[c] - m.P[a]);
            if (n.Len < 1e-12f && !asTri) n = V3.Cross(m.P[c] - m.P[a], m.P[d] - m.P[a]);
            bool ok = V3.Dot(n, want) >= 0;
            if (asTri) { if (ok) m.Tri(a, b, c); else m.Tri(a, c, b); }
            else { if (ok) m.Quad(a, b, c, d); else m.Quad(a, d, c, b); }
        }

        // ------------------------------------------------------------------ trees (unit height, built once per variant)

        /// <summary>
        /// Low-poly tree of height 1 and canopy half-width `w`, trunk and canopy in separate meshes. variant 0..2 round broadleaf
        /// (different silhouettes), 3 conifer. Instanced with <see cref="MeshData.AppendTransformed"/>.
        /// </summary>
        public static void UnitTree(int variant, MeshData canopy, MeshData trunk)
        {
            const int sides = 7;
            float tr = 0.035f;
            Frustum(trunk, 0, 0, 0f, 0.5f, tr * 1.4f, tr, 5, false, false, 0.3f, 0.5f);
            float[][] rings;
            float w;
            switch (variant)
            {
                case 0: w = 0.34f; rings = new float[][] { new float[] { 0.28f, 0.35f }, new float[] { 0.48f, 1.0f }, new float[] { 0.72f, 0.8f }, new float[] { 0.92f, 0.38f } }; break;
                case 1: w = 0.28f; rings = new float[][] { new float[] { 0.22f, 0.45f }, new float[] { 0.45f, 1.0f }, new float[] { 0.7f, 0.85f }, new float[] { 0.88f, 0.5f }, new float[] { 0.98f, 0.12f } }; break;
                case 2: w = 0.42f; rings = new float[][] { new float[] { 0.3f, 0.4f }, new float[] { 0.45f, 1.0f }, new float[] { 0.62f, 0.92f }, new float[] { 0.82f, 0.45f } }; break;
                default: w = 0.22f; rings = new float[][] { new float[] { 0.12f, 0.0f }, new float[] { 0.15f, 1.0f }, new float[] { 0.5f, 0.45f }, new float[] { 0.45f, 0.78f }, new float[] { 0.75f, 0.28f }, new float[] { 0.7f, 0.5f }, new float[] { 1.0f, 0.0f } }; break;
            }
            if (variant < 3)
            {
                List<int[]> ringIdx = new List<int[]>();
                for (int k = 0; k < rings.Length; k++)
                {
                    int[] idx = new int[sides + 1];
                    for (int j = 0; j <= sides; j++)
                    {
                        float a = 2f * MathX.Pi * j / sides + k * 0.4f;
                        idx[j] = canopy.Add(new V3((float)Math.Cos(a) * rings[k][1] * w, rings[k][0], (float)Math.Sin(a) * rings[k][1] * w), new V2((float)j / sides, rings[k][0]));
                    }
                    ringIdx.Add(idx);
                }
                for (int k = 0; k + 1 < rings.Length; k++)
                    for (int j = 0; j < sides; j++)
                    {
                        V3 pm = canopy.P[ringIdx[k][j]] + canopy.P[ringIdx[k][j + 1]];
                        AddFacing(canopy, ringIdx[k][j], ringIdx[k + 1][j], ringIdx[k + 1][j + 1], ringIdx[k][j + 1], new V3(pm.X, 0f, pm.Z), false);
                    }
                int topC = canopy.Add(new V3(0, rings[rings.Length - 1][0] + 0.05f, 0), new V2(0.5f, 1f));
                int[] last = ringIdx[ringIdx.Count - 1];
                for (int j = 0; j < sides; j++) AddFacing(canopy, last[j], topC, last[j + 1], topC, new V3(0, 1, 0), true);
                int botC = canopy.Add(new V3(0, rings[0][0] - 0.02f, 0), new V2(0.5f, 0f));
                int[] first = ringIdx[0];
                for (int j = 0; j < sides; j++) AddFacing(canopy, first[j], botC, first[j + 1], botC, new V3(0, -1, 0), true);
            }
            else
            {
                // conifer: three stacked cones
                float[] y0 = { 0.14f, 0.38f, 0.62f }, y1 = { 0.55f, 0.76f, 1.0f }, rr = { 1.0f, 0.75f, 0.5f };
                for (int k = 0; k < 3; k++) Frustum(canopy, 0, 0, y0[k], y1[k], rr[k] * w, 0f, sides, false, k == 0, 0.5f, 1f);
            }
            canopy.RecalcNormals();
            trunk.RecalcNormals();
        }

        // ------------------------------------------------------------------ grandstand (generated)

        public class StandParts
        {
            public MeshData Seats = new MeshData();    // treads
            public MeshData Crowd = new MeshData();    // risers carrying the crowd texture
            public MeshData Concrete = new MeshData(); // side and back walls
            public MeshData Roof = new MeshData();
        }

        /// <summary>
        /// A stepped stand. `front` is the unit direction from the stand towards the track, `length` runs along the track edge,
        /// `depth` away from it; `baseY` is ground level at the front. Rows are about 0.85 m deep and rise `height` in total.
        /// </summary>
        public static void Stand(StandParts sp, V2 centre, V2 front, float length, float depth, float height, float baseY, bool roof)
        {
            front = front.Norm;
            V2 along = new V2(front.Y, -front.X);
            int rows = MathX.Clamp((int)Math.Round(depth / 0.85f), 3, 48);
            float tread = depth / rows, riser = height / rows;
            float hl = length * 0.5f;
            Func<float, float, float, V3> W = delegate (float a, float b, float y)
            {
                V2 q = centre + along * a + front * (depth * 0.5f - b);
                return new V3(q.X, y, q.Y);
            };
            V3 f3 = new V3(front.X, 0, front.Y), l3 = new V3(-along.X, 0, -along.Y), r3 = new V3(along.X, 0, along.Y);
            float crowdTile = 2.4f;
            for (int k = 0; k < rows; k++)
            {
                float b0 = k * tread, b1 = (k + 1) * tread;
                float yLow = baseY + k * riser, yHigh = baseY + (k + 1) * riser;
                // tread (up-facing)
                sp.Seats.QuadUp(W(-hl, b0, yHigh), W(hl, b0, yHigh), W(hl, b1, yHigh), W(-hl, b1, yHigh),
                                new V2(0, 0), new V2(length / 3f, 0), new V2(length / 3f, 1), new V2(0, 1));
                // riser facing the track, carrying the crowd band (shifted per row so rows do not line up)
                float off = (k * 0.37f) % 1f;
                QuadFacing(sp.Crowd, W(-hl, b0, yLow), W(-hl, b0, yHigh), W(hl, b0, yHigh), W(hl, b0, yLow),
                           new V2(off, 0), new V2(off, 1), new V2(off + length / crowdTile, 1), new V2(off + length / crowdTile, 0), f3);
                // side walls down to below ground
                QuadFacing(sp.Concrete, W(-hl, b0, baseY - 0.6f), W(-hl, b0, yHigh), W(-hl, b1, yHigh), W(-hl, b1, baseY - 0.6f),
                           new V2(0, 0), new V2(0, 1), new V2(1, 1), new V2(1, 0), l3);
                QuadFacing(sp.Concrete, W(hl, b0, baseY - 0.6f), W(hl, b0, yHigh), W(hl, b1, yHigh), W(hl, b1, baseY - 0.6f),
                           new V2(0, 0), new V2(0, 1), new V2(1, 1), new V2(1, 0), r3);
            }
            // back wall
            QuadFacing(sp.Concrete, W(-hl, depth, baseY - 0.6f), W(-hl, depth, baseY + height), W(hl, depth, baseY + height), W(hl, depth, baseY - 0.6f),
                       new V2(0, 0), new V2(0, 1), new V2(length / 6f, 1), new V2(length / 6f, 0), new V3(-f3.X, 0, -f3.Z));
            if (roof && height >= 8f)
            {
                float rb0 = depth * 0.45f, rb1 = depth + 0.5f, ry = baseY + height + 3.2f, th = 0.35f;
                BoxFromCorners(sp.Roof, W(-hl - 0.3f, rb0, ry), W(hl + 0.3f, rb0, ry), W(hl + 0.3f, rb1, ry), W(-hl - 0.3f, rb1, ry), th);
            }
        }

        /// <summary>A slab: the four corners are the top face (y given), thickness `t` downwards.</summary>
        public static void BoxFromCorners(MeshData m, V3 a, V3 b, V3 c, V3 d, float t)
        {
            V3 dn = new V3(0, -t, 0);
            m.QuadUp(a, b, c, d, new V2(0, 0), new V2(1, 0), new V2(1, 1), new V2(0, 1));
            V3[] top = { a, b, c, d };
            for (int i = 0; i < 4; i++)
            {
                V3 p = top[i], q = top[(i + 1) % 4];
                V3 mid = (p + q) * 0.5f - (a + b + c + d) * 0.25f; mid.Y = 0;
                QuadFacing(m, p, q, q + dn, p + dn, new V2(0, 0), new V2(1, 0), new V2(1, 0.1f), new V2(0, 0.1f), mid);
            }
            QuadFacing(m, a + dn, b + dn, c + dn, d + dn, new V2(0, 0), new V2(1, 0), new V2(1, 1), new V2(0, 1), new V3(0, -1, 0));
        }

        // ------------------------------------------------------------------ tower

        public class TowerParts { public MeshData Concrete = new MeshData(); public MeshData Glass = new MeshData(); }

        /// <summary>A slender tapered shaft with a flared observation deck and a mast, `height` metres tall from baseY.</summary>
        public static void Tower(TowerParts tp, float cx, float cz, float baseY, float height)
        {
            float deckH = Math.Max(4f, height * 0.06f), flare = Math.Max(4f, height * 0.05f), mast = Math.Max(4f, height * 0.1f);
            float topShaft = baseY + height - deckH - flare - mast * 0.2f;
            Frustum(tp.Concrete, cx, cz, baseY - 1f, topShaft, 2.8f, 2.0f, 8, false, true, 6f, 6f);
            Frustum(tp.Concrete, cx, cz, topShaft, topShaft + flare, 2.0f, 8.5f, 8, false, false, 6f, 6f);
            Frustum(tp.Glass, cx, cz, topShaft + flare, topShaft + flare + deckH, 8.5f, 8.5f, 8, false, false, 6f, 6f);
            Frustum(tp.Concrete, cx, cz, topShaft + flare + deckH, topShaft + flare + deckH + 0.6f, 9.2f, 7.5f, 8, true, true, 6f, 6f);
            Frustum(tp.Concrete, cx, cz, topShaft + flare + deckH + 0.6f, baseY + height, 0.5f, 0.25f, 6, true, false, 2f, 2f);
        }

        // ------------------------------------------------------------------ barriers and fences

        /// <summary>A concrete wall along a polyline: two side faces and a top, terrain-following, resampled every segMax metres.</summary>
        public static void BoxStrip(MeshData m, IList<V2> line, HeightFn y, float width, float height, float segMax, float tile)
        {
            List<V2> l = Lines.Resample(line, segMax);
            float cum = 0;
            for (int i = 0; i + 1 < l.Count; i++)
            {
                V2 p = l[i], q = l[i + 1];
                V2 d = q - p; float len = d.Len;
                if (len < 1e-3f) continue;
                V2 dir = d * (1f / len), rt = new V2(dir.Y, -dir.X);
                float hw = width * 0.5f;
                V2 pr = p + rt * hw, qr = q + rt * hw, pl = p - rt * hw, ql = q - rt * hw;
                float yp = y(p.X, p.Y), yq = y(q.X, q.Y);
                float u0 = cum / tile, u1 = (cum + len) / tile;
                V3 rn = new V3(rt.X, 0, rt.Y), ln = new V3(-rt.X, 0, -rt.Y);
                QuadFacing(m, new V3(pr.X, yp - 0.15f, pr.Y), new V3(pr.X, yp + height, pr.Y), new V3(qr.X, yq + height, qr.Y), new V3(qr.X, yq - 0.15f, qr.Y),
                           new V2(u0, 0), new V2(u0, 1), new V2(u1, 1), new V2(u1, 0), rn);
                QuadFacing(m, new V3(ql.X, yq - 0.15f, ql.Y), new V3(ql.X, yq + height, ql.Y), new V3(pl.X, yp + height, pl.Y), new V3(pl.X, yp - 0.15f, pl.Y),
                           new V2(u1, 0), new V2(u1, 1), new V2(u0, 1), new V2(u0, 0), ln);
                QuadFacing(m, new V3(pl.X, yp + height, pl.Y), new V3(pr.X, yp + height, pr.Y), new V3(qr.X, yq + height, qr.Y), new V3(ql.X, yq + height, ql.Y),
                           new V2(u0, 0.95f), new V2(u0, 1), new V2(u1, 1), new V2(u1, 0.95f), new V3(0, 1, 0));
                cum += len;
            }
        }

        /// <summary>A double-sided catch-fence panel along a polyline (cut-out texture), terrain-following.</summary>
        public static void FencePanel(MeshData m, IList<V2> line, HeightFn y, float height, float segMax, float tile)
        {
            List<V2> l = Lines.Resample(line, segMax);
            float cum = 0;
            for (int i = 0; i + 1 < l.Count; i++)
            {
                V2 p = l[i], q = l[i + 1];
                V2 d = q - p; float len = d.Len;
                if (len < 1e-3f) continue;
                V2 dir = d * (1f / len), rt = new V2(dir.Y, -dir.X);
                float yp = y(p.X, p.Y), yq = y(q.X, q.Y);
                float u0 = cum / tile, u1 = (cum + len) / tile;
                V3 a = new V3(p.X, yp - 0.1f, p.Y), b = new V3(p.X, yp + height, p.Y), c = new V3(q.X, yq + height, q.Y), e = new V3(q.X, yq - 0.1f, q.Y);
                QuadFacing(m, a, b, c, e, new V2(u0, 0), new V2(u0, 1), new V2(u1, 1), new V2(u1, 0), new V3(rt.X, 0, rt.Y));
                QuadFacing(m, a, b, c, e, new V2(u0, 0), new V2(u0, 1), new V2(u1, 1), new V2(u1, 0), new V3(-rt.X, 0, -rt.Y));
                cum += len;
            }
        }
    }
}
