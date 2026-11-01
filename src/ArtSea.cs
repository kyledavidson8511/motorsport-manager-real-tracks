// Unity-free (M2 w11): the sea beyond the terrain edge. Kyle's playtest 1 note "the ocean floating above the actual sea level": the horizon
// skirt (ArtSkirt.cs) rose from the terrain edge to the mean boundary height (49.5 m at Monaco) within 400 m everywhere, also over the sea, and
// was painted with the photo's edge colour (the painted sea bed), so a dark-blue slab stood ~52 m above the harbour water (y -2.71) and hid the
// pack's sea polygons beyond the terrain. This file holds the maths that fixes it, compiled by the mod and by tools/autotest-unit-tests:
//   SeaMask   which skirt boundary samples (and corners) look out onto the pack's sea (harbour.json polygons, probed just outside the edge)
//   ClampEdge the terrain's outermost row/column over the sea down to the sea bed (the builder leaves it at the photo surface, ~0.6 m above)
//   FarSea    a water surface at the harbour level over the sea part of the skirt, out to the skirt radius (one level to the horizon)
//   ClipToRect the pack's sea polygons cut to the terrain rectangle, so the far sea and the harbour water never overlap (double-blended water)
using System;
using System.Collections.Generic;

namespace RealTracks.ArtGeo
{
    public class SeaMask
    {
        /// <summary>Per side (SkirtGeo order: 0 z-min, 1 z-max, 2 x-min, 3 x-max), per terrain sample along the side: true = sea outside.</summary>
        public readonly bool[][] Side = new bool[4][];
        /// <summary>Corners (SkirtGeo order: z-min/x-min, z-min/x-max, z-max/x-min, z-max/x-max).</summary>
        public readonly bool[] Corner = new bool[4];
        /// <summary>The harbour's water level (the one sea level) and the sea bed under it (level - seabedDropM).</summary>
        public float SurfaceY, FloorY;
        public int Nx, Nz;
        /// <summary>Samples turned to sea by SeaGeo.FillGaps (jetties crossing the edge), in how many runs, and corners added.</summary>
        public int Filled, GapRuns, CornersFilled;
        public float X0, Z0, Dx, Dz;

        public bool At(int s, int i) { bool[] a = Side[s]; return a != null && i >= 0 && i < a.Length && a[i]; }

        public int SeaSamples
        {
            get { int n = 0; for (int s = 0; s < 4; s++) if (Side[s] != null) foreach (bool b in Side[s]) if (b) n++; return n; }
        }
        public int Samples { get { return 2 * Nx + 2 * Nz; } }
        public int SeaCorners { get { int n = 0; foreach (bool b in Corner) if (b) n++; return n; } }
        public bool Any { get { return SeaSamples > 0 || SeaCorners > 0; } }
        public float XMax { get { return X0 + (Nx - 1) * Dx; } }
        public float ZMax { get { return Z0 + (Nz - 1) * Dz; } }
    }

    public static class SeaGeo
    {
        /// <summary>How far outside the terrain edge the sea test probes (the builder extends the sea polygons straight out from the edge).</summary>
        public const float ProbeOut = 5f;

        public static SeaMask Mask(int nx, int nz, float x0, float z0, float dx, float dz, Func<float, float, bool> isSea, float surfaceY, float floorY)
        {
            return Mask(nx, nz, x0, z0, dx, dz, isSea, surfaceY, floorY, 0f);
        }

        /// <summary>
        /// As above, then FillGaps(maxGapM): a jetty or breakwater that crosses the terrain edge does not run on to the horizon, so short land
        /// runs between sea along a side (and at a side's end next to sea round the corner) count as sea; a corner is sea when the probe says so
        /// or both side ends meeting there are sea.
        /// </summary>
        public static SeaMask Mask(int nx, int nz, float x0, float z0, float dx, float dz, Func<float, float, bool> isSea, float surfaceY, float floorY, float maxGapM)
        {
            SeaMask m = new SeaMask();
            m.Nx = nx; m.Nz = nz; m.X0 = x0; m.Z0 = z0; m.Dx = dx; m.Dz = dz; m.SurfaceY = surfaceY; m.FloorY = floorY;
            float xMax = m.XMax, zMax = m.ZMax, p = ProbeOut;
            m.Side[0] = new bool[nx]; m.Side[1] = new bool[nx]; m.Side[2] = new bool[nz]; m.Side[3] = new bool[nz];
            for (int i = 0; i < nx; i++) { float x = x0 + i * dx; m.Side[0][i] = isSea(x, z0 - p); m.Side[1][i] = isSea(x, zMax + p); }
            for (int j = 0; j < nz; j++) { float z = z0 + j * dz; m.Side[2][j] = isSea(x0 - p, z); m.Side[3][j] = isSea(xMax + p, z); }
            m.Corner[0] = isSea(x0 - p, z0 - p); m.Corner[1] = isSea(xMax + p, z0 - p);
            m.Corner[2] = isSea(x0 - p, zMax + p); m.Corner[3] = isSea(xMax + p, zMax + p);
            if (maxGapM > 0f) FillGaps(m, maxGapM);
            return m;
        }

        // side ends meeting at each corner: (side, index 0 or last) pairs, SkirtGeo corner order
        private static readonly int[,] CornerSides = { { 0, 0, 2, 0 }, { 0, 1, 3, 0 }, { 1, 0, 2, 1 }, { 1, 1, 3, 1 } };

        /// <summary>Fills short land runs (see Mask); returns the number of samples turned to sea (also in m.Filled).</summary>
        public static int FillGaps(SeaMask m, float maxGapM)
        {
            int filled = 0;
            // 1) interior runs bounded by sea on both sides
            for (int s = 0; s < 4; s++)
            {
                bool[] a = m.Side[s]; float step = s < 2 ? m.Dx : m.Dz;
                int i = 0;
                while (i < a.Length)
                {
                    if (a[i]) { i++; continue; }
                    int j = i; while (j < a.Length && !a[j]) j++;
                    if (i > 0 && j < a.Length && (j - i) * step <= maxGapM) { for (int k = i; k < j; k++) a[k] = true; filled += j - i; m.GapRuns++; }
                    i = j;
                }
            }
            // 2) runs at a side's end whose neighbour round the corner has sea within the gap
            for (int c = 0; c < 4; c++)
                for (int e = 0; e < 2; e++)
                {
                    int s = CornerSides[c, e * 2], end = CornerSides[c, e * 2 + 1];
                    int o = CornerSides[c, (1 - e) * 2], oend = CornerSides[c, (1 - e) * 2 + 1];
                    float step = s < 2 ? m.Dx : m.Dz, ostep = o < 2 ? m.Dx : m.Dz;
                    if (EndRun(m.Side[s], end) * step > maxGapM || EndRun(m.Side[s], end) == m.Side[s].Length) continue;
                    if (EndRun(m.Side[o], oend) * ostep > maxGapM || EndRun(m.Side[o], oend) == m.Side[o].Length) continue;
                    int n = EndRun(m.Side[s], end);
                    if (n == 0) continue;
                    bool[] a = m.Side[s];
                    for (int k = 0; k < n; k++) a[end == 0 ? k : a.Length - 1 - k] = true;
                    filled += n; m.GapRuns++;
                }
            // 3) corners: both side ends sea
            for (int c = 0; c < 4; c++)
            {
                bool[] a = m.Side[CornerSides[c, 0]], b = m.Side[CornerSides[c, 2]];
                bool ea = a[CornerSides[c, 1] == 0 ? 0 : a.Length - 1], eb = b[CornerSides[c, 3] == 0 ? 0 : b.Length - 1];
                if (ea && eb && !m.Corner[c]) { m.Corner[c] = true; m.CornersFilled++; }
            }
            m.Filled += filled;
            return filled;
        }

        /// <summary>Length (samples) of the land run at one end of a side (0 when the end sample is sea).</summary>
        private static int EndRun(bool[] a, int end)
        {
            int n = 0;
            if (end == 0) { while (n < a.Length && !a[n]) n++; }
            else { while (n < a.Length && !a[a.Length - 1 - n]) n++; }
            return n;
        }

        /// <summary>Heightfield index of sample i on side s, and of its inward neighbour.</summary>
        public static void EdgeCell(int nx, int nz, int s, int i, out int cell, out int inward)
        {
            switch (s)
            {
                case 0: cell = i; inward = nx + i; break;
                case 1: cell = (nz - 1) * nx + i; inward = (nz - 2) * nx + i; break;
                case 2: cell = i * nx; inward = i * nx + 1; break;
                default: cell = i * nx + nx - 1; inward = i * nx + nx - 2; break;
            }
        }

        /// <summary>
        /// In place: terrain edge cells that look out onto the sea and whose inward neighbour already lies on the sea bed (the builder lowered
        /// it) go down to the sea bed too. Returns the number changed; maxDrop = the largest lowering in metres. Idempotent.
        /// </summary>
        public static int ClampEdge(float[] h, SeaMask m, out float maxDrop)
        {
            maxDrop = 0f;
            int n = 0;
            for (int s = 0; s < 4; s++)
            {
                int len = s < 2 ? m.Nx : m.Nz;
                for (int i = 0; i < len; i++)
                {
                    if (!m.At(s, i)) continue;
                    int cell, inward;
                    EdgeCell(m.Nx, m.Nz, s, i, out cell, out inward);
                    if (h[cell] <= m.FloorY || h[inward] > m.FloorY + 0.5f) continue;
                    maxDrop = Math.Max(maxDrop, h[cell] - m.FloorY);
                    h[cell] = m.FloorY;
                    n++;
                }
            }
            return n;
        }

        /// <summary>Height of a skirt vertex over the sea: never above the sea bed (the water surface above it is FarSea).</summary>
        public static float SkirtY(float boundaryH, SeaMask m) { return Math.Min(boundaryH, m.FloorY); }

        /// <summary>Skirt texture rows (SkirtGeo.Rows) whose segment looks out onto the sea at both ends (corners: the corner flag).</summary>
        public static bool[] SeaRows(SeaMask m)
        {
            int S = SkirtGeo.SegsPerSide;
            bool[] rows = new bool[SkirtGeo.Rows];
            for (int s = 0; s < 4; s++)
            {
                int n = s < 2 ? m.Nx : m.Nz;
                for (int j = 0; j < S; j++)
                {
                    int ia = (int)((long)(n - 1) * j / S), ib = (int)((long)(n - 1) * (j + 1) / S);
                    rows[s * S + j] = m.At(s, ia) && m.At(s, ib);
                }
            }
            for (int c = 0; c < 4; c++) rows[4 * S + c] = m.Corner[c];
            return rows;
        }

        /// <summary>
        /// The far sea: flat water at m.SurfaceY over every skirt segment with sea at either end and every sea corner, from the terrain edge
        /// out to the last ring (the same quads as the skirt). UV = x/uvTile, z/uvTile like the harbour water.
        /// </summary>
        public static MeshData FarSea(SeaMask m, float[] rings, float uvTile)
        {
            MeshData md = new MeshData();
            int S = SkirtGeo.SegsPerSide, K = rings.Length;
            float xMin = m.X0, xMax = m.XMax, zMin = m.Z0, zMax = m.ZMax, y = m.SurfaceY;
            Func<V3, V2> uv = delegate (V3 p) { return new V2(p.X / uvTile, p.Z / uvTile); };
            for (int s = 0; s < 4; s++)
            {
                bool alongX = s < 2;
                int n = alongX ? m.Nx : m.Nz;
                for (int j = 0; j < S; j++)
                {
                    int ia = (int)((long)(n - 1) * j / S), ib = (int)((long)(n - 1) * (j + 1) / S);
                    if (!m.At(s, ia) && !m.At(s, ib)) continue;
                    float pa = alongX ? m.X0 + ia * m.Dx : m.Z0 + ia * m.Dz, pb = alongX ? m.X0 + ib * m.Dx : m.Z0 + ib * m.Dz;
                    for (int k = 0; k + 1 < K; k++)
                    {
                        V3 a = SkirtGeo.SidePoint(s, pa, y, rings[k], xMin, xMax, zMin, zMax), b = SkirtGeo.SidePoint(s, pb, y, rings[k], xMin, xMax, zMin, zMax);
                        V3 c = SkirtGeo.SidePoint(s, pb, y, rings[k + 1], xMin, xMax, zMin, zMax), d = SkirtGeo.SidePoint(s, pa, y, rings[k + 1], xMin, xMax, zMin, zMax);
                        md.QuadUp(a, b, c, d, uv(a), uv(b), uv(c), uv(d));
                    }
                }
            }
            float[] cx = { xMin, xMax, xMin, xMax }, cz = { zMin, zMin, zMax, zMax };
            float[] sx = { -1, 1, -1, 1 }, sz = { -1, -1, 1, 1 };
            for (int c = 0; c < 4; c++)
            {
                if (!m.Corner[c]) continue;
                for (int i = 0; i + 1 < K; i++)
                    for (int j = 0; j + 1 < K; j++)
                    {
                        V3 a = new V3(cx[c] + sx[c] * rings[i], y, cz[c] + sz[c] * rings[j]), b = new V3(cx[c] + sx[c] * rings[i + 1], y, cz[c] + sz[c] * rings[j]);
                        V3 e = new V3(cx[c] + sx[c] * rings[i + 1], y, cz[c] + sz[c] * rings[j + 1]), d = new V3(cx[c] + sx[c] * rings[i], y, cz[c] + sz[c] * rings[j + 1]);
                        md.QuadUp(a, b, e, d, uv(a), uv(b), uv(e), uv(d));
                    }
            }
            return md;
        }

        /// <summary>
        /// True when (x, z) lies outside the terrain rectangle in front of a sea sample (or in a sea corner): the part of the skirt that the far
        /// sea covers. Points inside the rectangle are false. Used by the checks (skirt height over the sea).
        /// </summary>
        public static bool OverFarSea(SeaMask m, float x, float z)
        {
            float xMax = m.XMax, zMax = m.ZMax;
            bool lx = x < m.X0 - 0.01f, hx = x > xMax + 0.01f, lz = z < m.Z0 - 0.01f, hz = z > zMax + 0.01f;
            if (!(lx || hx || lz || hz)) return false;
            if ((lx || hx) && (lz || hz)) return m.Corner[(lz ? 0 : 2) + (hx ? 1 : 0)];
            if (lz || hz) { int i = (int)Math.Round((x - m.X0) / m.Dx); return m.At(lz ? 0 : 1, i); }
            int jz = (int)Math.Round((z - m.Z0) / m.Dz);
            return m.At(lx ? 2 : 3, jz);
        }

        /// <summary>Keeps the parts of src's triangles inside the rectangle (positions, normals and UVs interpolated; winding kept).</summary>
        public static MeshData ClipToRect(MeshData src, float xMin, float xMax, float zMin, float zMax)
        {
            MeshData o = new MeshData();
            for (int t = 0; t + 2 < src.T.Count; t += 3)
            {
                List<int> ids = new List<int> { src.T[t], src.T[t + 1], src.T[t + 2] };
                List<V3> P = new List<V3>(), N = new List<V3>(); List<V2> U = new List<V2>();
                bool inside = true;
                foreach (int i in ids)
                {
                    V3 p = src.P[i]; P.Add(p); N.Add(src.N[i]); U.Add(src.UV[i]);
                    if (p.X < xMin || p.X > xMax || p.Z < zMin || p.Z > zMax) inside = false;
                }
                if (!inside)
                {
                    Clip(P, N, U, 0, xMin, 1); Clip(P, N, U, 0, xMax, -1);
                    Clip(P, N, U, 2, zMin, 1); Clip(P, N, U, 2, zMax, -1);
                    if (P.Count < 3) continue;
                }
                int b0 = o.Add(P[0], N[0], U[0]);
                int prev = o.Add(P[1], N[1], U[1]);
                for (int k = 2; k < P.Count; k++)
                {
                    int cur = o.Add(P[k], N[k], U[k]);
                    o.Tri(b0, prev, cur);
                    prev = cur;
                }
            }
            return o;
        }

        // Sutherland-Hodgman against one axis-aligned half plane: keep sign * (coord - v) >= 0 (axis 0 = x, 2 = z).
        private static void Clip(List<V3> P, List<V3> N, List<V2> U, int axis, float v, int sign)
        {
            if (P.Count == 0) return;
            List<V3> p2 = new List<V3>(), n2 = new List<V3>(); List<V2> u2 = new List<V2>();
            int n = P.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float di = sign * ((axis == 0 ? P[i].X : P[i].Z) - v), dj = sign * ((axis == 0 ? P[j].X : P[j].Z) - v);
                if (di >= 0) { p2.Add(P[i]); n2.Add(N[i]); u2.Add(U[i]); }
                if ((di >= 0) != (dj >= 0))
                {
                    float t = di / (di - dj);
                    p2.Add(new V3(P[i].X + (P[j].X - P[i].X) * t, P[i].Y + (P[j].Y - P[i].Y) * t, P[i].Z + (P[j].Z - P[i].Z) * t));
                    n2.Add(new V3(N[i].X + (N[j].X - N[i].X) * t, N[i].Y + (N[j].Y - N[i].Y) * t, N[i].Z + (N[j].Z - N[i].Z) * t));
                    u2.Add(new V2(U[i].X + (U[j].X - U[i].X) * t, U[i].Y + (U[j].Y - U[i].Y) * t));
                }
            }
            P.Clear(); P.AddRange(p2); N.Clear(); N.AddRange(n2); U.Clear(); U.AddRange(u2);
        }

        /// <summary>Lowest and highest y of a mesh (NaN when empty).</summary>
        public static void YRange(MeshData m, out float lo, out float hi)
        {
            lo = float.NaN; hi = float.NaN;
            for (int i = 0; i < m.Count; i++)
            {
                float y = m.P[i].Y;
                if (float.IsNaN(lo) || y < lo) lo = y;
                if (float.IsNaN(hi) || y > hi) hi = y;
            }
        }
    }
}
