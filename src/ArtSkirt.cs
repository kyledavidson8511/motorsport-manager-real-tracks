// Unity-free builder of the M1.1 horizon skirt: concentric rings around the terrain whose colour is a per-segment gradient from the
// colour of the orthophoto edge to a countryside tan. The mesh carries no colours: u picks the ring column and v the perimeter
// segment (row) of a small gradient texture that ArtMaterials builds ("skirt_fade"). No UnityEngine references.
using System;
using System.Collections.Generic;

namespace RealTracks.ArtGeo
{
    public static class SkirtGeo
    {
        public const int SegsPerSide = 128;
        public const float FlatAt = 400f;      // the skirt reaches the mean boundary height this far from the terrain edge

        /// <summary>Distance of each ring from the terrain edge: 0, 40, 94, 221 ... (x2.35) up to the radius.</summary>
        public static float[] RingDistances(float radius)
        {
            List<float> d = new List<float>(); d.Add(0f);
            float v = 40f;
            while (v < radius * 0.999f && d.Count < 14) { d.Add(v); v *= 2.35f; }
            d.Add(radius);
            return d.ToArray();
        }

        /// <summary>Number of texture rows: 4 sides x SegsPerSide, then the four corners.</summary>
        public static int Rows { get { return 4 * SegsPerSide + 4; } }
        public static float SmoothStep(float t) { t = MathX.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>
        /// Builds the skirt as a list of meshes (each below 65,000 vertices): sides 0 = z-min, 1 = z-max, 2 = x-min, 3 = x-max, then the corners.
        /// Row of side s, segment j = s * SegsPerSide + j; corner k = 4 * SegsPerSide + k (z-min/x-min, z-min/x-max, z-max/x-min, z-max/x-max).
        /// </summary>
        public static List<MeshData> Build(float[] h, int nx, int nz, float x0, float z0, float dx, float dz, float radius, out float outerY, out float[] rings)
        {
            return Build(h, nx, nz, x0, z0, dx, dz, radius, 0f, out outerY, out rings);
        }

        /// <summary>Distance from the terrain edge where the far rim starts to rise (see rimRise in Build).</summary>
        public const float RimRiseFrom = 6000f;

        /// <summary>
        /// As above; rimRise &gt; 0 lifts the outer rings by rimRise * t^2 (t = 0 at RimRiseFrom, 1 at the radius) so that for a camera a few hundred
        /// metres up the far rim sits at or above the eye-level horizon and the skirt's top edge is not a hard line against the sky.
        /// </summary>
        public static List<MeshData> Build(float[] h, int nx, int nz, float x0, float z0, float dx, float dz, float radius, float rimRise, out float outerY, out float[] rings)
        {
            return Build(h, nx, nz, x0, z0, dx, dz, radius, rimRise, null, out outerY, out rings);
        }

        /// <summary>
        /// As above; with a sea mask (M2 w11, ArtSea.cs) every skirt vertex that looks out onto the sea stays at or below the sea bed
        /// (SeaGeo.SkirtY: no rise to the mean boundary height, no rim lift), so the skirt never stands above the sea; null = unchanged.
        /// </summary>
        public static List<MeshData> Build(float[] h, int nx, int nz, float x0, float z0, float dx, float dz, float radius, float rimRise, SeaMask sea, out float outerY, out float[] rings)
        {
            rings = RingDistances(radius);
            int K = rings.Length;
            double sum = 0; int cnt = 0;
            for (int i = 0; i < nx; i += 4) { sum += h[i] + h[(nz - 1) * nx + i]; cnt += 2; }
            for (int j = 0; j < nz; j += 4) { sum += h[j * nx] + h[j * nx + nx - 1]; cnt += 2; }
            outerY = (float)(sum / Math.Max(1, cnt));
            float oy = outerY;
            float xMin = x0, xMax = x0 + (nx - 1) * dx, zMin = z0, zMax = z0 + (nz - 1) * dz;
            int rows = Rows;
            float[] rd = rings;
            float rr = rimRise, rrFrom = Math.Min(RimRiseFrom, radius * 0.5f), rrSpan = Math.Max(1f, radius - rrFrom);
            Func<float, float, float> Y = delegate (float hb, float d)
            {
                float y = hb + (oy - hb) * SmoothStep(d / FlatAt);
                if (rr > 0f) { float t = MathX.Clamp01((d - rrFrom) / rrSpan); y += rr * t * t; }
                return y;
            };
            SeaMask sm = sea;
            Func<float, float, bool, float> YS = delegate (float hb, float d, bool overSea) { return overSea ? SeaGeo.SkirtY(hb, sm) : Y(hb, d); };
            Func<int, float> U = delegate (int k) { return (k + 0.5f) / K; };
            List<MeshData> list = new List<MeshData>();

            // sides: (axis positions along the side, boundary heights)
            for (int s = 0; s < 4; s++)
            {
                bool alongX = s < 2;                 // sides 0,1 run along x
                int n = alongX ? nx : nz;
                MeshData m = new MeshData();
                for (int j = 0; j < SegsPerSide; j++)
                {
                    int ia = (int)((long)(n - 1) * j / SegsPerSide), ib = (int)((long)(n - 1) * (j + 1) / SegsPerSide);
                    float ha = SideH(h, nx, nz, s, ia), hb = SideH(h, nx, nz, s, ib);
                    float pa = alongX ? x0 + ia * dx : z0 + ia * dz, pb = alongX ? x0 + ib * dx : z0 + ib * dz;
                    float v = (s * SegsPerSide + j + 0.5f) / rows;
                    bool sa = sm != null && sm.At(s, ia), sb = sm != null && sm.At(s, ib);
                    for (int k = 0; k + 1 < K; k++)
                    {
                        float d0 = rd[k], d1 = rd[k + 1];
                        V3 a = SidePoint(s, pa, YS(ha, d0, sa), d0, xMin, xMax, zMin, zMax), b = SidePoint(s, pb, YS(hb, d0, sb), d0, xMin, xMax, zMin, zMax);
                        V3 c = SidePoint(s, pb, YS(hb, d1, sb), d1, xMin, xMax, zMin, zMax), d = SidePoint(s, pa, YS(ha, d1, sa), d1, xMin, xMax, zMin, zMax);
                        m.QuadUp(a, b, c, d, new V2(U(k), v), new V2(U(k), v), new V2(U(k + 1), v), new V2(U(k + 1), v));
                    }
                }
                list.Add(m);
            }
            // corners: rings in both directions, ring index of a vertex = max of its two indices
            float[] cx = { xMin, xMax, xMin, xMax }, cz = { zMin, zMin, zMax, zMax };
            float[] sx = { -1, 1, -1, 1 }, sz = { -1, -1, 1, 1 };
            float[] ch = { h[0], h[nx - 1], h[(nz - 1) * nx], h[(nz - 1) * nx + nx - 1] };
            for (int c = 0; c < 4; c++)
            {
                MeshData m = new MeshData();
                float v = (4 * SegsPerSide + c + 0.5f) / rows;
                bool cs = sm != null && sm.Corner[c];
                for (int i = 0; i + 1 < K; i++)
                    for (int j = 0; j + 1 < K; j++)
                    {
                        Func<int, int, V3> Pt = delegate (int a, int b)
                        {
                            return new V3(cx[c] + sx[c] * rd[a], YS(ch[c], Math.Max(rd[a], rd[b]), cs), cz[c] + sz[c] * rd[b]);
                        };
                        m.QuadUp(Pt(i, j), Pt(i + 1, j), Pt(i + 1, j + 1), Pt(i, j + 1),
                            new V2(U(Math.Max(i, j)), v), new V2(U(Math.Max(i + 1, j)), v), new V2(U(Math.Max(i + 1, j + 1)), v), new V2(U(Math.Max(i, j + 1)), v));
                    }
                list.Add(m);
            }
            return list;
        }

        // ------------------------------------------------------------------------------------------------------------------------
        // Colour field of the skirt (Unity-free so the net8 harness in tools/skirt renders exactly what the game builds).
        // Per row r (see Build for the row order) and ring distance d from the terrain edge:
        //   photo   = lerp(raw[r], smooth[r], S(d / RawToSmoothM))          the first ring is the photo's own edge colour; the circular
        //                                                                    Gaussian smoothing (SmoothM) takes over within a few hundred metres
        //   body    = lerp(photo, mean, S((d - RawToSmoothM) / (MeanByM - RawToSmoothM)))   by MeanByM every row has the same colour: no rays
        //   ground  = lerp(body, tan, S(d / FadeM))                          the pack's selected horizon body colour
        //   result  = lerp(ground, haze, S((d - HazeFromM) / (Radius - HazeFromM)))   dissolves into the sky haze at the rim (HorizonBlend)
        // ------------------------------------------------------------------------------------------------------------------------
        public class ColourSpec
        {
            public float FadeM = 2500f, Radius = 25000f;
            public float SmoothM = 1200f;          // Gaussian arc length (full width = 2 sigma) around the perimeter; 0 = no smoothing (the M1.1 behaviour)
            public float RawToSmoothM = 120f;
            public float MeanByM = 1500f;          // 0 = never blend to the mean colour (the M1.1 behaviour)
            public bool HorizonBlend = true;
            public float HazeFromM = 6000f;
            public float[] Tan = { 0.60f, 0.50f, 0.36f };
            public float[] Haze = { 0.56f, 0.58f, 0.60f };
            /// <summary>Optional sea-row body colour. Null keeps the source sea-bed colour, preserving the pre-M2 behaviour.</summary>
            public float[] Sea;
        }

        /// <summary>Perimeter order of the rows: corner 0, side 0 (x rising), corner 1, side 3 (z rising), corner 3, side 1 (x falling), corner 2, side 2 (z falling); arc = midpoint of each row in metres.</summary>
        public static void PerimeterOrder(float xSize, float zSize, out int[] rows, out float[] arc, out float[] len, out float total)
        {
            int S = SegsPerSide, n = 4 * S + 4;
            int[] R = new int[n]; float[] A = new float[n], L = new float[n];
            float lx = xSize / S, lz = zSize / S, a = 0f; int k = 0;
            List<KeyValuePair<int, float>> order = new List<KeyValuePair<int, float>>();
            order.Add(new KeyValuePair<int, float>(4 * S + 0, 0f));
            for (int j = 0; j < S; j++) order.Add(new KeyValuePair<int, float>(0 * S + j, lx));
            order.Add(new KeyValuePair<int, float>(4 * S + 1, 0f));
            for (int j = 0; j < S; j++) order.Add(new KeyValuePair<int, float>(3 * S + j, lz));
            order.Add(new KeyValuePair<int, float>(4 * S + 3, 0f));
            for (int j = S - 1; j >= 0; j--) order.Add(new KeyValuePair<int, float>(1 * S + j, lx));
            order.Add(new KeyValuePair<int, float>(4 * S + 2, 0f));
            for (int j = S - 1; j >= 0; j--) order.Add(new KeyValuePair<int, float>(2 * S + j, lz));
            foreach (KeyValuePair<int, float> o in order) { R[k] = o.Key; L[k] = o.Value; A[k] = a + o.Value * 0.5f; a += o.Value; k++; }
            rows = R; arc = A; len = L;
            total = a;
            // corner rows carry no length of their own: give them one segment's worth so they take part in the average
            for (int i = 0; i < n; i++) if (len[i] <= 0f) len[i] = (lx + lz) * 0.5f;
        }

        /// <summary>Circular Gaussian smoothing of per-row RGB (rows*3 floats) along the perimeter; sigma = smoothM / 2.</summary>
        public static float[] SmoothEdges(float[] raw, float xSize, float zSize, float smoothM)
        {
            int n = Rows;
            float[] outv = (float[])raw.Clone();
            if (smoothM <= 1f) return outv;
            int[] rows; float[] arc, len; float total;
            PerimeterOrder(xSize, zSize, out rows, out arc, out len, out total);
            float sigma = smoothM * 0.5f, inv = 1f / (2f * sigma * sigma);
            for (int i = 0; i < n; i++)
            {
                double wr = 0, wg = 0, wb = 0, ws = 0;
                for (int j = 0; j < n; j++)
                {
                    float dd = Math.Abs(arc[i] - arc[j]); dd = Math.Min(dd, total - dd);
                    if (dd > 3.2f * sigma) continue;
                    double w = Math.Exp(-dd * dd * inv) * len[j];
                    int r = rows[j];
                    wr += w * raw[r * 3]; wg += w * raw[r * 3 + 1]; wb += w * raw[r * 3 + 2]; ws += w;
                }
                int ri = rows[i];
                outv[ri * 3] = (float)(wr / ws); outv[ri * 3 + 1] = (float)(wg / ws); outv[ri * 3 + 2] = (float)(wb / ws);
            }
            return outv;
        }

        /// <summary>Length-weighted mean colour of all rows.</summary>
        public static float[] MeanEdge(float[] raw, float xSize, float zSize)
        {
            int[] rows; float[] arc, len; float total;
            PerimeterOrder(xSize, zSize, out rows, out arc, out len, out total);
            double r = 0, g = 0, b = 0, ws = 0;
            for (int i = 0; i < rows.Length; i++) { int q = rows[i]; r += len[i] * raw[q * 3]; g += len[i] * raw[q * 3 + 1]; b += len[i] * raw[q * 3 + 2]; ws += len[i]; }
            return new float[] { (float)(r / ws), (float)(g / ws), (float)(b / ws) };
        }

        private static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        /// <summary>Colour of row `row` at distance d from the terrain edge. raw/smooth are rows*3 floats, mean is 3 floats.</summary>
        public static void Colour(float[] raw, float[] smooth, float[] mean, int row, float d, ColourSpec sp, float[] outRgb)
        {
            float tS = sp.SmoothM > 1f ? SmoothStep(d / Math.Max(1f, sp.RawToSmoothM)) : 0f;
            float tM = sp.MeanByM > sp.RawToSmoothM ? SmoothStep((d - sp.RawToSmoothM) / (sp.MeanByM - sp.RawToSmoothM)) : 0f;
            float tT = SmoothStep(d / Math.Max(1f, sp.FadeM));
            float tH = sp.HorizonBlend ? SmoothStep((d - sp.HazeFromM) / Math.Max(1f, sp.Radius - sp.HazeFromM)) : 0f;
            for (int c = 0; c < 3; c++)
            {
                float photo = Lerp(raw[row * 3 + c], smooth[row * 3 + c], tS);
                float body = Lerp(photo, mean[c], tM);
                float ground = Lerp(body, sp.Tan[c], tT);
                outRgb[c] = Lerp(ground, sp.Haze[c], tH);
            }
        }

        /// <summary>
        /// Colour of a sea row (M2 w11): normally the photo's own sea-bed colour out to the rim.  Coastal packs can provide a
        /// sea body colour so land and sea converge before the far haze instead of forming a visible horizon seam.
        /// </summary>
        public static void ColourSea(float[] raw, int row, float d, ColourSpec sp, float[] outRgb)
        {
            float tS = sp.Sea != null && sp.Sea.Length >= 3 ? SmoothStep(d / Math.Max(1f, sp.FadeM)) : 0f;
            float tH = sp.HorizonBlend ? SmoothStep((d - sp.HazeFromM) / Math.Max(1f, sp.Radius - sp.HazeFromM)) : 0f;
            for (int c = 0; c < 3; c++)
            {
                float body = sp.Sea != null && sp.Sea.Length >= 3 ? Lerp(raw[row * 3 + c], sp.Sea[c], tS) : raw[row * 3 + c];
                outRgb[c] = Lerp(body, sp.Haze[c], tH);
            }
        }

        /// <summary>Largest colour step (max channel difference) between neighbouring perimeter rows at distance d; also the 95th percentile.</summary>
        public static void NeighbourStep(float[] raw, float[] smooth, float[] mean, float xSize, float zSize, float d, ColourSpec sp, out float max, out float p95)
        {
            int[] rows; float[] arc, len; float total;
            PerimeterOrder(xSize, zSize, out rows, out arc, out len, out total);
            int n = rows.Length;
            float[][] col = new float[n][];
            for (int i = 0; i < n; i++) { col[i] = new float[3]; Colour(raw, smooth, mean, rows[i], d, sp, col[i]); }
            List<float> steps = new List<float>();
            for (int i = 0; i < n; i++)
            {
                float[] a = col[i], b = col[(i + 1) % n];
                steps.Add(Math.Max(Math.Abs(a[0] - b[0]), Math.Max(Math.Abs(a[1] - b[1]), Math.Abs(a[2] - b[2]))));
            }
            steps.Sort();
            max = steps[steps.Count - 1]; p95 = steps[(int)(0.95 * (steps.Count - 1))];
        }

        private static float SideH(float[] h, int nx, int nz, int s, int i)
        {
            switch (s)
            {
                case 0: return h[i];
                case 1: return h[(nz - 1) * nx + i];
                case 2: return h[i * nx];
                default: return h[i * nx + nx - 1];
            }
        }

        public static V3 SidePoint(int s, float along, float y, float d, float xMin, float xMax, float zMin, float zMax)
        {
            switch (s)
            {
                case 0: return new V3(along, y, zMin - d);
                case 1: return new V3(along, y, zMax + d);
                case 2: return new V3(xMin - d, y, along);
                default: return new V3(xMax + d, y, along);
            }
        }
    }
}
