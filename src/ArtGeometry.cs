// Unity-free geometry for the Real Tracks venue art generator (WP3b).
// This file (and ArtShapes.cs) must not reference UnityEngine: the net8 console test in ../test compiles them directly.
// Only .NET 3.5 BCL APIs are used so the same source builds for the game (net35) and for the test (net8).
using System;
using System.Collections.Generic;

namespace RealTracks.ArtGeo
{
    public struct V2
    {
        public float X, Y;
        public V2(float x, float y) { X = x; Y = y; }
        public static V2 operator +(V2 a, V2 b) { return new V2(a.X + b.X, a.Y + b.Y); }
        public static V2 operator -(V2 a, V2 b) { return new V2(a.X - b.X, a.Y - b.Y); }
        public static V2 operator *(V2 a, float s) { return new V2(a.X * s, a.Y * s); }
        public float Len { get { return (float)Math.Sqrt(X * X + Y * Y); } }
        public V2 Norm { get { float l = Len; return l > 1e-9f ? new V2(X / l, Y / l) : new V2(0, 0); } }
        public static float Dot(V2 a, V2 b) { return a.X * b.X + a.Y * b.Y; }
        public static float Cross(V2 a, V2 b) { return a.X * b.Y - a.Y * b.X; }
        public static float Dist(V2 a, V2 b) { return (a - b).Len; }
        public static V2 Lerp(V2 a, V2 b, float t) { return new V2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }
        public override string ToString() { return "(" + X + ", " + Y + ")"; }
    }

    public struct V3
    {
        public float X, Y, Z;
        public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static V3 operator +(V3 a, V3 b) { return new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static V3 operator -(V3 a, V3 b) { return new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static V3 operator *(V3 a, float s) { return new V3(a.X * s, a.Y * s, a.Z * s); }
        public float Len { get { return (float)Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public V3 Norm { get { float l = Len; return l > 1e-9f ? new V3(X / l, Y / l, Z / l) : new V3(0, 1, 0); } }
        public static float Dot(V3 a, V3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        public static V3 Cross(V3 a, V3 b) { return new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
        public V2 XZ { get { return new V2(X, Z); } }
        public bool IsFinite { get { return IsOk(X) && IsOk(Y) && IsOk(Z); } }
        private static bool IsOk(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }
        public override string ToString() { return "(" + X + ", " + Y + ", " + Z + ")"; }
    }

    /// <summary>Tiny deterministic xorshift generator (same sequence on every runtime).</summary>
    public class Rng
    {
        private uint mS;
        public Rng(int seed) { mS = (uint)(seed * 747796405 + 2891336453u); if (mS == 0) mS = 0x9E3779B9u; Next(); Next(); }
        /// <summary>Uniform in [0, 1).</summary>
        public float Next()
        {
            mS ^= mS << 13; mS ^= mS >> 17; mS ^= mS << 5;
            return (mS & 0xFFFFFF) / 16777216f;
        }
        public float Range(float a, float b) { return a + (b - a) * Next(); }
        public int Int(int n) { return n <= 0 ? 0 : (int)(Next() * n) % n; }
    }

    public static class MathX
    {
        public const float Pi = 3.14159265f;
        public static float Clamp(float v, float a, float b) { return v < a ? a : (v > b ? b : v); }
        public static int Clamp(int v, int a, int b) { return v < a ? a : (v > b ? b : v); }
        public static float Clamp01(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        public static float Frac(float v) { return v - (float)Math.Floor(v); }
    }

    /// <summary>One indexed triangle mesh with explicit normals and UVs. Triangles are clockwise seen from the front (Unity).</summary>
    public class MeshData
    {
        public readonly List<V3> P = new List<V3>();
        public readonly List<V3> N = new List<V3>();
        public readonly List<V2> UV = new List<V2>();
        public readonly List<int> T = new List<int>();

        public int Count { get { return P.Count; } }

        public int Add(V3 p, V3 n, V2 uv) { P.Add(p); N.Add(n); UV.Add(uv); return P.Count - 1; }
        public int Add(V3 p, V2 uv) { return Add(p, new V3(0, 1, 0), uv); }
        public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }
        /// <summary>Two triangles (a,b,c) and (a,c,d).</summary>
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        public void Append(MeshData o)
        {
            int off = P.Count;
            P.AddRange(o.P); N.AddRange(o.N); UV.AddRange(o.UV);
            for (int i = 0; i < o.T.Count; i++) T.Add(o.T[i] + off);
        }

        /// <summary>Appends a copy scaled, rotated about +y (yaw in degrees, Unity convention: 0 = +z, 90 = +x) and moved.</summary>
        public void AppendTransformed(MeshData o, V3 pos, float yawDeg, float sx, float sy, float sz)
        {
            int off = P.Count;
            float r = yawDeg * MathX.Pi / 180f, c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            for (int i = 0; i < o.P.Count; i++)
            {
                V3 p = o.P[i];
                float x = p.X * sx, y = p.Y * sy, z = p.Z * sz;
                P.Add(new V3(x * c + z * s + pos.X, y + pos.Y, -x * s + z * c + pos.Z));
                V3 n = o.N[i];
                N.Add(new V3(n.X * c + n.Z * s, n.Y, -n.X * s + n.Z * c));
                UV.Add(o.UV[i]);
            }
            for (int i = 0; i < o.T.Count; i++) T.Add(o.T[i] + off);
        }

        /// <summary>Area-weighted smooth normals from the triangles (overwrites N).</summary>
        public void RecalcNormals()
        {
            V3[] acc = new V3[P.Count];
            for (int i = 0; i + 2 < T.Count; i += 3)
            {
                int a = T[i], b = T[i + 1], c = T[i + 2];
                V3 n = V3.Cross(P[b] - P[a], P[c] - P[a]);
                acc[a] = acc[a] + n; acc[b] = acc[b] + n; acc[c] = acc[c] + n;
            }
            for (int i = 0; i < P.Count; i++) N[i] = acc[i].Norm;
        }

        public void Bounds(out V3 min, out V3 max)
        {
            min = new V3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new V3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < P.Count; i++)
            {
                V3 p = P[i];
                if (p.X < min.X) min.X = p.X; if (p.Y < min.Y) min.Y = p.Y; if (p.Z < min.Z) min.Z = p.Z;
                if (p.X > max.X) max.X = p.X; if (p.Y > max.Y) max.Y = p.Y; if (p.Z > max.Z) max.Z = p.Z;
            }
        }

        public V3 Centre()
        {
            if (P.Count == 0) return new V3();
            V3 a, b; Bounds(out a, out b);
            return (a + b) * 0.5f;
        }

        /// <summary>Surface area (sum of triangle areas).</summary>
        public double Area()
        {
            double sum = 0;
            for (int i = 0; i + 2 < T.Count; i += 3)
                sum += 0.5 * V3.Cross(P[T[i + 1]] - P[T[i]], P[T[i + 2]] - P[T[i]]).Len;
            return sum;
        }

        /// <summary>Checks indices in range, finite values, normals/uv counts. Returns null when fine.</summary>
        public string Validate()
        {
            if (N.Count != P.Count || UV.Count != P.Count) return "attribute counts differ";
            if (T.Count % 3 != 0) return "index count not a multiple of 3";
            for (int i = 0; i < P.Count; i++)
            {
                if (!P[i].IsFinite || !N[i].IsFinite) return "non-finite vertex " + i;
                if (float.IsNaN(UV[i].X) || float.IsNaN(UV[i].Y) || float.IsInfinity(UV[i].X) || float.IsInfinity(UV[i].Y)) return "non-finite uv " + i;
            }
            for (int i = 0; i < T.Count; i++) if (T[i] < 0 || T[i] >= P.Count) return "index " + T[i] + " out of range (" + P.Count + " verts)";
            return null;
        }

        /// <summary>Splits a mesh into pieces with at most maxVerts vertices each (re-indexed, whole triangles only).</summary>
        public static List<MeshData> Split(MeshData m, int maxVerts)
        {
            List<MeshData> list = new List<MeshData>();
            if (m.Count <= maxVerts) { list.Add(m); return list; }
            MeshData cur = new MeshData();
            Dictionary<int, int> map = new Dictionary<int, int>();
            for (int i = 0; i + 2 < m.T.Count; i += 3)
            {
                int need = 0;
                for (int k = 0; k < 3; k++) if (!map.ContainsKey(m.T[i + k])) need++;
                if (cur.Count + need > maxVerts)
                {
                    list.Add(cur); cur = new MeshData(); map.Clear();
                }
                for (int k = 0; k < 3; k++)
                {
                    int src = m.T[i + k], dst;
                    if (!map.TryGetValue(src, out dst)) { dst = cur.Add(m.P[src], m.N[src], m.UV[src]); map[src] = dst; }
                    cur.T.Add(dst);
                }
            }
            if (cur.Count > 0) list.Add(cur);
            return list;
        }

        /// <summary>Appends a quad whose two triangles face up (+y) whatever the order of the four corners.</summary>
        public void QuadUp(V3 a, V3 b, V3 c, V3 d, V2 ua, V2 ub, V2 uc, V2 ud)
        {
            int ia = Add(a, ua), ib = Add(b, ub), ic = Add(c, uc), id = Add(d, ud);
            V3 n = V3.Cross(b - a, c - a);
            if (n.Y >= 0) Quad(ia, ib, ic, id); else Quad(ia, id, ic, ib);
        }
    }

    /// <summary>Collects meshes per (material, spatial cell) so frustum culling still works, flushing before 65k vertices.</summary>
    public class MeshBatcher
    {
        public class Batch { public string Material; public int CellX, CellZ; public MeshData Mesh; }

        public int MaxVerts = 60000;
        public float CellSize = 400f;
        private readonly Dictionary<string, Batch> mOpen = new Dictionary<string, Batch>();
        private readonly List<Batch> mDone = new List<Batch>();

        public void Add(string material, MeshData piece)
        {
            if (piece == null || piece.Count == 0 || piece.T.Count == 0) return;
            V3 c = piece.Centre();
            int cx = (int)Math.Floor(c.X / CellSize), cz = (int)Math.Floor(c.Z / CellSize);
            string key = material + "|" + cx + "|" + cz;
            List<MeshData> parts = MeshData.Split(piece, MaxVerts);
            foreach (MeshData part in parts)
            {
                Batch b;
                if (mOpen.TryGetValue(key, out b) && b.Mesh.Count + part.Count > MaxVerts)
                {
                    mDone.Add(b); mOpen.Remove(key); b = null;
                }
                if (b == null)
                {
                    b = new Batch(); b.Material = material; b.CellX = cx; b.CellZ = cz; b.Mesh = new MeshData();
                    mOpen[key] = b;
                }
                b.Mesh.Append(part);
            }
        }

        public List<Batch> Finish()
        {
            foreach (KeyValuePair<string, Batch> kv in mOpen) mDone.Add(kv.Value);
            mOpen.Clear();
            List<Batch> res = new List<Batch>(mDone);
            mDone.Clear();
            return res;
        }
    }

    // =================================================================================================
    // Polygons: area, containment, oriented box, ear-clipping triangulation with holes
    // =================================================================================================
    public static class Poly
    {
        /// <summary>Positive when counter-clockwise on a map with x east / y north.</summary>
        public static double SignedArea(IList<V2> r)
        {
            double a = 0;
            for (int i = 0, n = r.Count; i < n; i++)
            {
                V2 p = r[i], q = r[(i + 1) % n];
                a += (double)p.X * q.Y - (double)q.X * p.Y;
            }
            return a * 0.5;
        }

        public static double Area(IList<V2> r) { return Math.Abs(SignedArea(r)); }

        public static double Perimeter(IList<V2> r)
        {
            double l = 0;
            for (int i = 0, n = r.Count; i < n; i++) l += V2.Dist(r[i], r[(i + 1) % n]);
            return l;
        }

        public static bool Contains(IList<V2> r, V2 p)
        {
            bool inside = false;
            for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
            {
                V2 a = r[i], b = r[j];
                if (((a.Y > p.Y) != (b.Y > p.Y)) && (p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)) inside = !inside;
            }
            return inside;
        }

        /// <summary>Inside the outer ring and outside every hole.</summary>
        public static bool Contains(IList<V2> outer, IList<IList<V2>> holes, V2 p)
        {
            if (!Contains(outer, p)) return false;
            if (holes != null) foreach (IList<V2> h in holes) if (Contains(h, p)) return false;
            return true;
        }

        public static V2 Centroid(IList<V2> r)
        {
            double a = 0, cx = 0, cy = 0;
            for (int i = 0, n = r.Count; i < n; i++)
            {
                V2 p = r[i], q = r[(i + 1) % n];
                double cr = (double)p.X * q.Y - (double)q.X * p.Y;
                a += cr; cx += (p.X + q.X) * cr; cy += (p.Y + q.Y) * cr;
            }
            if (Math.Abs(a) < 1e-9)
            {
                double sx = 0, sy = 0;
                foreach (V2 p in r) { sx += p.X; sy += p.Y; }
                return new V2((float)(sx / r.Count), (float)(sy / r.Count));
            }
            return new V2((float)(cx / (3 * a)), (float)(cy / (3 * a)));
        }

        /// <summary>Counter-clockwise copy of the ring, without a repeated closing point.</summary>
        public static List<V2> Clean(IList<V2> r)
        {
            List<V2> l = new List<V2>(r);
            if (l.Count > 1 && Math.Abs(l[0].X - l[l.Count - 1].X) < 1e-6f && Math.Abs(l[0].Y - l[l.Count - 1].Y) < 1e-6f) l.RemoveAt(l.Count - 1);
            if (SignedArea(l) < 0) l.Reverse();
            return l;
        }

        /// <summary>
        /// Minimum-area oriented box around the ring. u is the long axis (unit), v is perpendicular (unit, 90 degrees
        /// counter-clockwise from u), half lengths along u and v.
        /// </summary>
        public static void OrientedBox(IList<V2> r, out V2 centre, out V2 u, out V2 v, out float halfU, out float halfV)
        {
            float bestArea = float.MaxValue;
            centre = new V2(); u = new V2(1, 0); v = new V2(0, 1); halfU = 1; halfV = 1;
            for (int i = 0, n = r.Count; i < n; i++)
            {
                V2 d = (r[(i + 1) % n] - r[i]).Norm;
                if (d.Len < 0.5f) continue;
                V2 e = new V2(-d.Y, d.X);
                float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
                foreach (V2 p in r)
                {
                    float a = V2.Dot(p, d), b = V2.Dot(p, e);
                    if (a < minA) minA = a; if (a > maxA) maxA = a;
                    if (b < minB) minB = b; if (b > maxB) maxB = b;
                }
                float area = (maxA - minA) * (maxB - minB);
                if (area < bestArea)
                {
                    bestArea = area;
                    float ca = (minA + maxA) * 0.5f, cb = (minB + maxB) * 0.5f;
                    centre = d * ca + e * cb;
                    u = d; v = e; halfU = (maxA - minA) * 0.5f; halfV = (maxB - minB) * 0.5f;
                }
            }
            if (halfV > halfU)
            {
                // swap the axes so u is the long one (M1 bug: v was rebuilt from the already-overwritten u, so u == v and stands got a
                // depth axis along their length: the "perpendicular grey slab" across the main straight)
                V2 t = u; u = v; v = t * -1f;
                float h = halfU; halfU = halfV; halfV = h;
            }
        }

        // ------------------------------------------------------------------------------ triangulation
        // Ear clipping with hole bridging (same family as mapbox/earcut, written for double precision).

        private class Node
        {
            public int I; public double X, Y; public Node Prev, Next; public bool Steiner;
            public Node(int i, double x, double y) { I = i; X = x; Y = y; }
        }

        /// <summary>
        /// Triangulates a polygon with optional holes. Returns points (outer ring first, then each hole, closing
        /// duplicates removed) and indices into them, three per triangle, counter-clockwise on an x-east/y-north map.
        /// </summary>
        public static List<int> Triangulate(IList<V2> outer, IList<IList<V2>> holes, out List<V2> points)
        {
            points = new List<V2>();
            List<int> tris = new List<int>();
            List<V2> o = StripClosing(outer);
            if (o.Count < 3) return tris;
            List<List<V2>> hs = new List<List<V2>>();
            if (holes != null) foreach (IList<V2> h in holes) { List<V2> c = StripClosing(h); if (c.Count >= 3) hs.Add(c); }

            points.AddRange(o);
            List<int> holeStart = new List<int>();
            foreach (List<V2> h in hs) { holeStart.Add(points.Count); points.AddRange(h); }

            Node outerNode = Link(points, 0, o.Count, true);
            if (outerNode == null || outerNode.Next == outerNode.Prev) return tris;
            if (hs.Count > 0) outerNode = EliminateHoles(points, holeStart, o.Count, hs, outerNode);
            EarcutLinked(outerNode, tris, 0);
            return tris;
        }

        private static List<V2> StripClosing(IList<V2> r)
        {
            List<V2> l = new List<V2>(r);
            if (l.Count > 1 && Math.Abs(l[0].X - l[l.Count - 1].X) < 1e-6f && Math.Abs(l[0].Y - l[l.Count - 1].Y) < 1e-6f) l.RemoveAt(l.Count - 1);
            return l;
        }

        private static double SumArea(List<V2> pts, int start, int end)
        {
            double sum = 0;
            for (int i = start, j = end - 1; i < end; j = i, i++)
                sum += ((double)pts[j].X - pts[i].X) * ((double)pts[i].Y + pts[j].Y);
            return sum;
        }

        private static Node Link(List<V2> pts, int start, int end, bool outer)
        {
            Node last = null;
            // earcut convention: the outer ring is stored with a positive SumArea (counter-clockwise on a y-up map), holes the opposite
            if (outer == (SumArea(pts, start, end) > 0))
            {
                for (int i = start; i < end; i++) last = Insert(i, pts[i].X, pts[i].Y, last);
            }
            else
            {
                for (int i = end - 1; i >= start; i--) last = Insert(i, pts[i].X, pts[i].Y, last);
            }
            if (last != null && Eq(last, last.Next)) { Remove(last); last = last.Next; }
            return last;
        }

        private static Node Insert(int i, double x, double y, Node last)
        {
            Node p = new Node(i, x, y);
            if (last == null) { p.Prev = p; p.Next = p; }
            else { p.Next = last.Next; p.Prev = last; last.Next.Prev = p; last.Next = p; }
            return p;
        }

        private static void Remove(Node p) { p.Next.Prev = p.Prev; p.Prev.Next = p.Next; }
        private static bool Eq(Node a, Node b) { return a.X == b.X && a.Y == b.Y; }
        private static double Area(Node p, Node q, Node r) { return (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y); }

        private static Node Filter(Node start, Node end)
        {
            if (start == null) return null;
            if (end == null) end = start;
            Node p = start;
            bool again;
            do
            {
                again = false;
                if (!p.Steiner && (Eq(p, p.Next) || Area(p.Prev, p, p.Next) == 0))
                {
                    Remove(p);
                    p = end = p.Prev;
                    if (p == p.Next) break;
                    again = true;
                }
                else p = p.Next;
            } while (again || p != end);
            return end;
        }

        private static void EarcutLinked(Node ear, List<int> tris, int pass)
        {
            if (ear == null) return;
            Node stop = ear;
            while (ear.Prev != ear.Next)
            {
                Node prev = ear.Prev, next = ear.Next;
                if (IsEar(ear))
                {
                    tris.Add(prev.I); tris.Add(ear.I); tris.Add(next.I);
                    Remove(ear);
                    ear = next.Next; stop = next.Next;
                    continue;
                }
                ear = next;
                if (ear == stop)
                {
                    if (pass == 0) EarcutLinked(Filter(ear, null), tris, 1);
                    else if (pass == 1) { ear = CureLocalIntersections(Filter(ear, null), tris); EarcutLinked(ear, tris, 2); }
                    else if (pass == 2) SplitEarcut(ear, tris);
                    break;
                }
            }
        }

        private static bool IsEar(Node ear)
        {
            Node a = ear.Prev, b = ear, c = ear.Next;
            if (Area(a, b, c) >= 0) return false; // reflex
            for (Node p = c.Next; p != a; p = p.Next)
            {
                if (InTri(a.X, a.Y, b.X, b.Y, c.X, c.Y, p.X, p.Y) && Area(p.Prev, p, p.Next) >= 0) return false;
            }
            return true;
        }

        private static bool InTri(double ax, double ay, double bx, double by, double cx, double cy, double px, double py)
        {
            return (cx - px) * (ay - py) - (ax - px) * (cy - py) >= 0 &&
                   (ax - px) * (by - py) - (bx - px) * (ay - py) >= 0 &&
                   (bx - px) * (cy - py) - (cx - px) * (by - py) >= 0;
        }

        private static Node CureLocalIntersections(Node start, List<int> tris)
        {
            Node p = start;
            do
            {
                Node a = p.Prev, b = p.Next.Next;
                if (!Eq(a, b) && Intersects(a, p, p.Next, b) && LocallyInside(a, b) && LocallyInside(b, a))
                {
                    tris.Add(a.I); tris.Add(p.I); tris.Add(b.I);
                    Remove(p); Remove(p.Next);
                    p = start = b;
                }
                p = p.Next;
            } while (p != start);
            return Filter(p, null);
        }

        private static void SplitEarcut(Node start, List<int> tris)
        {
            Node a = start;
            do
            {
                Node b = a.Next.Next;
                while (b != a.Prev)
                {
                    if (a.I != b.I && IsValidDiagonal(a, b))
                    {
                        Node c = SplitPolygon(a, b);
                        a = Filter(a, a.Next);
                        c = Filter(c, c.Next);
                        EarcutLinked(a, tris, 0);
                        EarcutLinked(c, tris, 0);
                        return;
                    }
                    b = b.Next;
                }
                a = a.Next;
            } while (a != start);
        }

        private static Node EliminateHoles(List<V2> pts, List<int> holeStart, int outerLen, List<List<V2>> hs, Node outer)
        {
            List<Node> queue = new List<Node>();
            for (int k = 0; k < hs.Count; k++)
            {
                int start = holeStart[k], end = start + hs[k].Count;
                Node list = Link(pts, start, end, false);
                if (list == null) continue;
                if (list == list.Next) list.Steiner = true;
                queue.Add(Leftmost(list));
            }
            queue.Sort(delegate (Node a, Node b) { return a.X < b.X ? -1 : (a.X > b.X ? 1 : 0); });
            foreach (Node h in queue) outer = EliminateHole(h, outer);
            return outer;
        }

        private static Node EliminateHole(Node hole, Node outer)
        {
            Node bridge = FindHoleBridge(hole, outer);
            if (bridge == null) return outer;
            Node rev = SplitPolygon(bridge, hole);
            Filter(rev, rev.Next);
            return Filter(bridge, bridge.Next);
        }

        private static Node FindHoleBridge(Node hole, Node outer)
        {
            Node p = outer;
            double hx = hole.X, hy = hole.Y, qx = double.NegativeInfinity;
            Node m = null;
            do
            {
                if (hy <= p.Y && hy >= p.Next.Y && p.Next.Y != p.Y)
                {
                    double x = p.X + (hy - p.Y) * (p.Next.X - p.X) / (p.Next.Y - p.Y);
                    if (x <= hx && x > qx)
                    {
                        qx = x;
                        m = p.X < p.Next.X ? p : p.Next;
                        if (x == hx) return m;
                    }
                }
                p = p.Next;
            } while (p != outer);
            if (m == null) return null;

            Node stop = m;
            double mx = m.X, my = m.Y, tanMin = double.PositiveInfinity;
            p = m;
            do
            {
                if (hx >= p.X && p.X >= mx && hx != p.X &&
                    InTri(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.X, p.Y))
                {
                    double tan = Math.Abs(hy - p.Y) / (hx - p.X);
                    if (LocallyInside(p, hole) && (tan < tanMin || (tan == tanMin && (p.X > m.X || (p.X == m.X && SectorContainsSector(m, p))))))
                    {
                        m = p; tanMin = tan;
                    }
                }
                p = p.Next;
            } while (p != stop);
            return m;
        }

        private static bool SectorContainsSector(Node m, Node p) { return Area(m.Prev, m, p.Prev) < 0 && Area(p.Next, m, m.Next) < 0; }

        private static Node Leftmost(Node start)
        {
            Node p = start, l = start;
            do
            {
                if (p.X < l.X || (p.X == l.X && p.Y < l.Y)) l = p;
                p = p.Next;
            } while (p != start);
            return l;
        }

        private static bool IsValidDiagonal(Node a, Node b)
        {
            return a.Next.I != b.I && a.Prev.I != b.I && !IntersectsPolygon(a, b) &&
                   ((LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b) &&
                     (Area(a.Prev, a, b.Prev) != 0 || Area(a, b.Prev, b) != 0)) ||
                    (Eq(a, b) && Area(a.Prev, a, a.Next) > 0 && Area(b.Prev, b, b.Next) > 0));
        }

        private static int Sign(double v) { return v > 0 ? 1 : (v < 0 ? -1 : 0); }

        private static bool Intersects(Node p1, Node q1, Node p2, Node q2)
        {
            int o1 = Sign(Area(p1, q1, p2)), o2 = Sign(Area(p1, q1, q2)), o3 = Sign(Area(p2, q2, p1)), o4 = Sign(Area(p2, q2, q1));
            if (o1 != o2 && o3 != o4) return true;
            if (o1 == 0 && OnSeg(p1, p2, q1)) return true;
            if (o2 == 0 && OnSeg(p1, q2, q1)) return true;
            if (o3 == 0 && OnSeg(p2, p1, q2)) return true;
            if (o4 == 0 && OnSeg(p2, q1, q2)) return true;
            return false;
        }

        private static bool OnSeg(Node p, Node q, Node r)
        {
            return q.X <= Math.Max(p.X, r.X) && q.X >= Math.Min(p.X, r.X) && q.Y <= Math.Max(p.Y, r.Y) && q.Y >= Math.Min(p.Y, r.Y);
        }

        private static bool IntersectsPolygon(Node a, Node b)
        {
            Node p = a;
            do
            {
                if (p.I != a.I && p.Next.I != a.I && p.I != b.I && p.Next.I != b.I && Intersects(p, p.Next, a, b)) return true;
                p = p.Next;
            } while (p != a);
            return false;
        }

        private static bool LocallyInside(Node a, Node b)
        {
            return Area(a.Prev, a, a.Next) < 0
                ? Area(a, b, a.Next) >= 0 && Area(a, a.Prev, b) >= 0
                : Area(a, b, a.Prev) < 0 || Area(a, a.Next, b) < 0;
        }

        private static bool MiddleInside(Node a, Node b)
        {
            Node p = a;
            bool inside = false;
            double px = (a.X + b.X) / 2, py = (a.Y + b.Y) / 2;
            do
            {
                if (((p.Y > py) != (p.Next.Y > py)) && p.Next.Y != p.Y && (px < (p.Next.X - p.X) * (py - p.Y) / (p.Next.Y - p.Y) + p.X)) inside = !inside;
                p = p.Next;
            } while (p != a);
            return inside;
        }

        private static Node SplitPolygon(Node a, Node b)
        {
            Node a2 = new Node(a.I, a.X, a.Y), b2 = new Node(b.I, b.X, b.Y);
            Node an = a.Next, bp = b.Prev;
            a.Next = b; b.Prev = a;
            a2.Next = an; an.Prev = a2;
            b2.Next = a2; a2.Prev = b2;
            bp.Next = b2; b2.Prev = bp;
            return b2;
        }

        /// <summary>Sum of the triangle areas of a triangulation.</summary>
        public static double TriArea(List<V2> pts, List<int> tris)
        {
            double sum = 0;
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                V2 a = pts[tris[i]], b = pts[tris[i + 1]], c = pts[tris[i + 2]];
                sum += Math.Abs(((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)c.X - a.X) * ((double)b.Y - a.Y)) * 0.5;
            }
            return sum;
        }
    }

    // =================================================================================================
    // Road footprint index: clearance of a point from the road edge (negative = on the road)
    // =================================================================================================
    public class RoadIndex
    {
        private struct Seg
        {
            public V2 A, B, Dir, Right;
            public float Len, Ya, Yb, HlA, HlB, HrA, HrB;
        }

        private readonly List<Seg> mSegs = new List<Seg>();
        private readonly Dictionary<long, List<int>> mGrid = new Dictionary<long, List<int>>();
        private readonly float mCell;
        private readonly float mReach;

        /// <summary>cell = grid size; reach = how far (m) a query looks for road. Clearances up to about reach minus the widest half-width are exact.</summary>
        public RoadIndex(float cell, float reach) { mCell = cell; mReach = reach; }
        public RoadIndex() : this(64f, 64f) { }

        public int SegmentCount { get { return mSegs.Count; } }

        public void AddPolyline(IList<V3> pts, IList<float> hl, IList<float> hr, float defHl, float defHr, bool closed)
        {
            int n = pts.Count;
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                int j = (i + 1) % n;
                V2 a = pts[i].XZ, b = pts[j].XZ;
                V2 d = b - a;
                float len = d.Len;
                if (len < 1e-4f) continue;
                Seg s = new Seg();
                s.A = a; s.B = b; s.Len = len; s.Dir = d * (1f / len);
                s.Right = new V2(s.Dir.Y, -s.Dir.X);
                s.Ya = pts[i].Y; s.Yb = pts[j].Y;
                s.HlA = hl != null ? hl[i] : defHl; s.HlB = hl != null ? hl[j] : defHl;
                s.HrA = hr != null ? hr[i] : defHr; s.HrB = hr != null ? hr[j] : defHr;
                int idx = mSegs.Count;
                mSegs.Add(s);
                int x0 = Cell(Math.Min(a.X, b.X)), x1 = Cell(Math.Max(a.X, b.X)), z0 = Cell(Math.Min(a.Y, b.Y)), z1 = Cell(Math.Max(a.Y, b.Y));
                for (int cx = x0; cx <= x1; cx++)
                    for (int cz = z0; cz <= z1; cz++)
                    {
                        long key = Key(cx, cz);
                        List<int> l;
                        if (!mGrid.TryGetValue(key, out l)) { l = new List<int>(); mGrid[key] = l; }
                        l.Add(idx);
                    }
            }
        }

        public void AddPolyline(IList<V3> pts, float halfWidth, bool closed) { AddPolyline(pts, null, null, halfWidth, halfWidth, closed); }

        private int Cell(float v) { return (int)Math.Floor(v / mCell); }
        private static long Key(int cx, int cz) { return ((long)cx << 32) ^ (cz & 0xffffffffL); }

        /// <summary>Distance from the road edge (negative on the road); 1e9 when no road within reach.</summary>
        public float Clearance(float x, float z)
        {
            float y; V2 q;
            return Nearest(x, z, out y, out q);
        }

        public float Clearance(V2 p) { return Clearance(p.X, p.Y); }

        /// <summary>As <see cref="Clearance(float,float)"/>, also the road height and centreline point of the closest segment.</summary>
        public float Nearest(float x, float z, out float roadY, out V2 centrePoint)
        {
            float best = 1e9f;
            roadY = 0; centrePoint = new V2(x, z);
            int x0 = Cell(x - mReach), x1 = Cell(x + mReach), z0 = Cell(z - mReach), z1 = Cell(z + mReach);
            V2 p = new V2(x, z);
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    List<int> l;
                    if (!mGrid.TryGetValue(Key(cx, cz), out l)) continue;
                    for (int k = 0; k < l.Count; k++)
                    {
                        Seg s = mSegs[l[k]];
                        V2 ap = p - s.A;
                        float t = MathX.Clamp(V2.Dot(ap, s.Dir) / s.Len, 0f, 1f);
                        V2 q = s.A + s.Dir * (t * s.Len);
                        V2 o = p - q;
                        float dist = o.Len;
                        float lat = V2.Dot(o, s.Right);
                        float hw = lat >= 0 ? MathX.Lerp(s.HrA, s.HrB, t) : MathX.Lerp(s.HlA, s.HlB, t);
                        float c = dist - hw;
                        if (c < best) { best = c; roadY = MathX.Lerp(s.Ya, s.Yb, t); centrePoint = q; }
                    }
                }
            return best;
        }

        public float Nearest(float x, float z, out float roadY) { V2 q; return Nearest(x, z, out roadY, out q); }
    }

    // =================================================================================================
    // Scatter, stripes
    // =================================================================================================
    public static class Scatter
    {
        /// <summary>
        /// Jittered-grid scatter inside a polygon (outer ring + optional holes). Each cell yields at most one point, so
        /// points are never closer than about 0.2 x spacing. reject(p) lets the caller keep points off roads and features.
        /// </summary>
        public static List<V2> InPolygon(IList<V2> ring, IList<IList<V2>> holes, float spacing, int seed, Func<V2, bool> reject, int maxCount)
        {
            List<V2> res = new List<V2>();
            if (ring.Count < 3 || spacing < 0.5f) return res;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (V2 p in ring)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minZ) minZ = p.Y; if (p.Y > maxZ) maxZ = p.Y;
            }
            Rng rng = new Rng(seed);
            int nx = (int)Math.Ceiling((maxX - minX) / spacing), nz = (int)Math.Ceiling((maxZ - minZ) / spacing);
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    V2 p = new V2(minX + (ix + 0.1f + 0.8f * rng.Next()) * spacing, minZ + (iz + 0.1f + 0.8f * rng.Next()) * spacing);
                    if (!Poly.Contains(ring, holes, p)) continue;
                    if (reject != null && reject(p)) continue;
                    res.Add(p);
                    if (res.Count >= maxCount) return res;
                }
            return res;
        }
    }

    public static class Stripes
    {
        public const int Red = 0, White = 1, Blue = 2;

        /// <summary>
        /// Diagonal red/white/blue stripe index for a texture coordinate (a, b in tile units, period 1 in both).
        /// Stripes run along the diagonal; each of the three colours takes one third of every period.
        /// </summary>
        public static int Index(float a, float b)
        {
            float f = MathX.Frac(a + b);
            int k = (int)(f * 3f);
            return k > 2 ? 2 : k;
        }
    }
}
