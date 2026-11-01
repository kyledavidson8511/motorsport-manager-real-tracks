// Unity-free rules and shapes for M1.1 package C: which way a grandstand faces, which mapped "stands" are not real seating, and the
// procedural pit building. No UnityEngine references (same rule as ArtGeometry.cs). The Python mirror of StandRules lives in
// tools/trackbuild/features.py (stand_decision); keep both in step, tools/trackbuild/art_checks.py checks the results.
using System;
using System.Collections.Generic;

namespace RealTracks.ArtGeo
{
    public class StandDecision
    {
        public string Role;          // "seating" | "block"
        public V2 Front;             // unit vector from the stand towards the track
        public V2 Centre;
        public float Length, Depth;  // along the track / away from it (metres)
        public float EdgeDist;       // front edge to the nearest track edge
        public float FaceDeg;        // angle between Front and the direction to the nearest track point
        public float AlongDeg;       // angle between the long axis and the track tangent there
    }

    public static class StandRules
    {
        public const float SeatMaxEdge = 95f;   // front edge at most this far from the track edge
        public const float FaceMaxDeg = 45f;
        public const float AlongMaxDeg = 45f;
        public const float Elongated = 1.35f;

        /// <summary>Nearest point on a closed polyline: distance, point, unit tangent, segment index.</summary>
        public static float NearestOnClosed(IList<V2> poly, V2 p, out V2 q, out V2 tangent, out int seg)
        {
            float best = float.MaxValue; q = p; tangent = new V2(1, 0); seg = 0;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                V2 a = poly[i], b = poly[(i + 1) % n], ab = b - a;
                float l2 = V2.Dot(ab, ab);
                float t = l2 > 1e-9f ? MathX.Clamp01(V2.Dot(p - a, ab) / l2) : 0f;
                V2 c = a + ab * t;
                float d = V2.Dist(c, p);
                if (d < best) { best = d; q = c; tangent = ab.Norm; seg = i; }
            }
            return best;
        }

        /// <summary>
        /// The facing rule. The front of a stand is the long side nearest the track (seats rise away from it); a stand that is not
        /// elongated may face along either axis. It is real seating only when its front edge is within SeatMaxEdge of the track edge, the
        /// front looks at the nearest track point within FaceMaxDeg, and the long axis runs along the track within AlongMaxDeg;
        /// otherwise it is a solid block (the mapped "bleachers" in the infield are such).
        /// </summary>
        public static StandDecision Decide(IList<V2> ring, IList<V2> track, IList<float> halfWidth)
        {
            V2 c, u, v; float hu, hv;
            Poly.OrientedBox(ring, out c, out u, out v, out hu, out hv);
            bool elong = hu / Math.Max(hv, 0.1f) >= Elongated;
            List<V2> fr = new List<V2>(); List<float> hf = new List<float>(); List<V2> al = new List<V2>(); List<float> ha = new List<float>();
            fr.Add(v); hf.Add(hv); al.Add(u); ha.Add(hu);
            fr.Add(v * -1f); hf.Add(hv); al.Add(u); ha.Add(hu);
            if (!elong)
            {
                fr.Add(u); hf.Add(hu); al.Add(v); ha.Add(hv);
                fr.Add(u * -1f); hf.Add(hu); al.Add(v); ha.Add(hv);
            }
            float[] ts = { -0.8f, -0.4f, 0f, 0.4f, 0.8f };
            float bestScore = float.MaxValue; int bi = 0;
            float bestEdge = 0, bestDist = 0; V2 bestQ = c, bestTan = new V2(1, 0), bestP = c;
            for (int k = 0; k < fr.Count; k++)
            {
                float sum = 0, minEdge = float.MaxValue, minDist = 0; V2 mq = c, mt = c, mp = c;
                for (int j = 0; j < ts.Length; j++)
                {
                    V2 p = c + fr[k] * hf[k] + al[k] * (ha[k] * ts[j]);
                    V2 q, tg; int seg;
                    float d = NearestOnClosed(track, p, out q, out tg, out seg);
                    float edge = d - halfWidth[seg];
                    sum += edge;
                    if (edge < minEdge) { minEdge = edge; minDist = d; mq = q; mt = tg; mp = p; }
                }
                float score = sum / ts.Length;
                if (score < bestScore) { bestScore = score; bi = k; bestEdge = minEdge; bestDist = minDist; bestQ = mq; bestTan = mt; bestP = mp; }
            }
            StandDecision r = new StandDecision();
            r.Front = fr[bi]; r.Centre = c; r.Length = ha[bi] * 2f; r.Depth = hf[bi] * 2f; r.EdgeDist = bestEdge;
            V2 to = bestDist > 1e-6f ? (bestQ - bestP) * (1f / bestDist) : r.Front;
            r.FaceDeg = (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, V2.Dot(r.Front, to)))) * 180.0 / Math.PI);
            r.AlongDeg = (float)(Math.Acos(Math.Max(0f, Math.Min(1f, Math.Abs(V2.Dot(al[bi], bestTan))))) * 180.0 / Math.PI);
            bool seating = r.EdgeDist <= SeatMaxEdge && r.FaceDeg <= FaceMaxDeg && r.AlongDeg <= AlongMaxDeg;
            r.Role = seating ? "seating" : "block";
            return r;
        }

        /// <summary>The M1 heuristic (front = the box axis most towards the nearest track point, long axes penalised), kept only to count how many stands the new rule turned.</summary>
        public static V2 LegacyFront(IList<V2> ring, V2 toTrack)
        {
            V2 c, u, v; float hu, hv;
            Poly.OrientedBox(ring, out c, out u, out v, out hu, out hv);
            V2[] axes = { u, u * -1f, v, v * -1f };
            int best = 0; float bd = -2f;
            for (int i = 0; i < 4; i++) { float d = V2.Dot(axes[i], toTrack); if (hu / Math.Max(hv, 0.1f) > 1.4f && i < 2) d -= 0.7f; if (d > bd) { bd = d; best = i; } }
            return axes[best];
        }

        /// <summary>Share of the polygon `small` (sampled on a grid in its own box) that lies inside `big`.</summary>
        public static float OverlapShare(IList<V2> small, IList<V2> big)
        {
            V2 c, u, v; float hu, hv;
            Poly.OrientedBox(small, out c, out u, out v, out hu, out hv);
            int inS = 0, inB = 0;
            for (int i = 0; i < 12; i++)
                for (int j = 0; j < 5; j++)
                {
                    V2 p = c + u * (hu * ((i + 0.5f) / 6f - 1f)) + v * (hv * ((j + 0.5f) / 2.5f - 1f));
                    if (!Poly.Contains(small, p)) continue;
                    inS++;
                    if (Poly.Contains(big, p)) inB++;
                }
            return inS > 0 ? (float)inB / inS : 0f;
        }
    }

    // =================================================================================================
    // Pit building
    // =================================================================================================
    public class PitParts
    {
        public MeshData Facade = new MeshData();   // lane-side wall: garage level + two glass levels ("pit_facade", one tile = 12 m wide, the full height)
        public MeshData Walls = new MeshData();    // ends and back ("pit_side")
        public MeshData Roof = new MeshData();     // flat roof ("bld_roof")
        public MeshData Slabs = new MeshData();    // canopies and columns ("stand_roof", white)
    }

    public static class PitShape
    {
        public const float CanopyOverhang = 2.4f;
        public const float CanopyY = 4.9f;
        public const float ColumnSpacing = 12f;

        /// <summary>
        /// A long low pit building on the footprint `ring` (a rectangle in practice). `toLane` is the unit vector from the building
        /// towards the pit lane (its front). `ground` is the ground level at the front, `bottom` where the walls end below it,
        /// `height` the roof height above `ground`. The first-floor canopy and the roof overhang stick out `CanopyOverhang` towards the lane.
        /// Returns the world-space rectangle actually used (front-left, front-right, back-right, back-left).
        /// </summary>
        public static V2[] Build(PitParts pp, IList<V2> ring, V2 toLane, float ground, float bottom, float height)
        {
            V2 c, u, v; float hu, hv;
            Poly.OrientedBox(ring, out c, out u, out v, out hu, out hv);
            // front = the box axis that looks towards the lane; `along` runs along the building
            V2 front; float depthHalf, lenHalf, signAlong = 1f;
            if (Math.Abs(V2.Dot(v, toLane)) >= Math.Abs(V2.Dot(u, toLane))) { front = V2.Dot(v, toLane) >= 0 ? v : v * -1f; depthHalf = hv; lenHalf = hu; }
            else { front = V2.Dot(u, toLane) >= 0 ? u : u * -1f; depthHalf = hu; lenHalf = hv; }
            V2 along = new V2(front.Y, -front.X) * signAlong;
            Func<float, float, V2> W = delegate (float a, float b) { return c + along * a + front * (depthHalf - b); };    // b = depth behind the front face
            Func<float, float, float, V3> P = delegate (float a, float b, float y) { V2 q = W(a, b); return new V3(q.X, y, q.Y); };
            float L = lenHalf * 2f, D = depthHalf * 2f, top = ground + height;
            V3 fw = new V3(front.X, 0, front.Y), bk = new V3(-front.X, 0, -front.Y), rt = new V3(along.X, 0, along.Y), lf = new V3(-along.X, 0, -along.Y);
            float tileW = 12f;
            // facade: u runs with `along` so 12 m is one tile; v 0..1 is ground..roof
            Shapes.QuadFacing(pp.Facade, P(-lenHalf, 0, bottom), P(-lenHalf, 0, top), P(lenHalf, 0, top), P(lenHalf, 0, bottom),
                new V2(0, (bottom - ground) / height), new V2(0, 1), new V2(L / tileW, 1), new V2(L / tileW, (bottom - ground) / height), fw);
            // back and ends: plain panels
            Shapes.QuadFacing(pp.Walls, P(-lenHalf, D, bottom), P(-lenHalf, D, top), P(lenHalf, D, top), P(lenHalf, D, bottom),
                new V2(0, bottom / 7f), new V2(0, top / 7f), new V2(L / tileW, top / 7f), new V2(L / tileW, bottom / 7f), bk);
            Shapes.QuadFacing(pp.Walls, P(-lenHalf, 0, bottom), P(-lenHalf, 0, top), P(-lenHalf, D, top), P(-lenHalf, D, bottom),
                new V2(0, bottom / 7f), new V2(0, top / 7f), new V2(D / tileW, top / 7f), new V2(D / tileW, bottom / 7f), lf);
            Shapes.QuadFacing(pp.Walls, P(lenHalf, 0, bottom), P(lenHalf, 0, top), P(lenHalf, D, top), P(lenHalf, D, bottom),
                new V2(0, bottom / 7f), new V2(0, top / 7f), new V2(D / tileW, top / 7f), new V2(D / tileW, bottom / 7f), rt);
            // flat roof
            pp.Roof.QuadUp(P(-lenHalf, 0, top), P(lenHalf, 0, top), P(lenHalf, D, top), P(-lenHalf, D, top),
                new V2(0, 0), new V2(L / 10f, 0), new V2(L / 10f, D / 10f), new V2(0, D / 10f));
            // pit-lane canopy at the first-floor slab and the roof overhang (white slabs)
            float ov = CanopyOverhang;
            Shapes.BoxFromCorners(pp.Slabs, P(-lenHalf, -ov, ground + CanopyY), P(lenHalf, -ov, ground + CanopyY), P(lenHalf, 0.3f, ground + CanopyY), P(-lenHalf, 0.3f, ground + CanopyY), 0.45f);
            Shapes.BoxFromCorners(pp.Slabs, P(-lenHalf, -ov * 0.8f, top + 0.45f), P(lenHalf, -ov * 0.8f, top + 0.45f), P(lenHalf, 0.4f, top + 0.45f), P(-lenHalf, 0.4f, top + 0.45f), 0.6f);
            // slim columns under the canopy
            int cols = Math.Max(2, (int)Math.Round(L / ColumnSpacing) + 1);
            for (int i = 0; i < cols; i++)
            {
                float a = -lenHalf + 0.6f + (L - 1.2f) * i / (cols - 1);
                V2 q = W(a, -ov + 0.35f);
                Shapes.Frustum(pp.Slabs, q.X, q.Y, bottom, ground + CanopyY, 0.22f, 0.22f, 6, false, false, 2f, 2f);
            }
            return new V2[] { W(-lenHalf, 0), W(lenHalf, 0), W(lenHalf, D), W(-lenHalf, D) };
        }
    }
}
